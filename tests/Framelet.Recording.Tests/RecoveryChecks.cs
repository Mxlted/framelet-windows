using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Framelet.Core;
using Framelet.Media;
using Framelet.Recording;

// These child processes exercise ownership and protocol races, not codec validity.
// Real generated MP4s are decoded separately by the opt-in integration checks.
internal static class RecoveryChecks
{
    public static bool IsFixture(string[] args) => args.Contains("--stdio") || args.Contains("-version") || args.Contains("gdigrab");

    public static async Task<int> RunFixtureAsync(string[] args)
    {
        if (args.Contains("-version")) { Console.WriteLine("ffmpeg version Framelet-protocol-fixture"); return 0; }
        if (args.Contains("gdigrab"))
        {
            var file = args[^1];
            await File.WriteAllBytesAsync(file, new byte[512]);
            if (file.Contains("natural", StringComparison.Ordinal)) { await Task.Delay(1600); return 0; }
            if (file.Contains("failure", StringComparison.Ordinal)) { await Task.Delay(1600); return 1; }
            await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            return 0;
        }
        string? staging = null;
        var natural = false;
        while (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) is { } line)
        {
            var command = JsonSerializer.Deserialize<NativeHostCommand>(line)!;
            if (command.Operation == "start")
            {
                var request = command.Request!;
                staging = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, $".framelet-{request.SessionId}.recording.mp4");
                await File.WriteAllBytesAsync(staging, new byte[512]);
                Send(new("result", command.Id));
                natural = request.OutputPath.Contains("natural", StringComparison.Ordinal);
                if (natural)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!File.Exists(request.OutputPath) && DateTime.UtcNow < deadline) await Task.Delay(10);
                    Send(new("finalized", RecoveryPath: staging));
                    if (request.OutputPath.Contains("eof", StringComparison.Ordinal)) return 0;
                    Send(new("failed", ErrorType: nameof(RecordingPublicationException), RecoveryPath: staging));
                }
            }
            else
            {
                if (command.Operation == "stop" && !natural)
                {
                    Send(new("finalized", RecoveryPath: staging));
                    Send(new("result", command.Id, ErrorType: nameof(RecordingPublicationException), RecoveryPath: staging));
                }
                else Send(new("result", command.Id));
                return 0;
            }
        }
        return 0;
    }

    private static void Send(NativeHostMessage message)
    {
        Console.WriteLine(JsonSerializer.Serialize(message));
        Console.Out.Flush();
    }

    public static async Task RunAsync(string root, Func<string, Func<Task>, Task> check)
    {
        await check("native finalized file is closed and announced before a teardown fault", async () =>
        {
            var file = Path.Combine(root, "finalized-before-teardown.mp4");
            var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            await stream.WriteAsync(new byte[512]);
            var state = RecordingFileState.Incomplete;
            var announced = false;
            try
            {
                await NativeSession.FinalizeBeforeDisposalAsync(stream, () =>
                {
                    // An exclusive reopen proves the owned stream was closed before the
                    // finalization message, not merely before the disposal task returned.
                    using var reopened = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
                    Require(reopened.Length == 512, "Completed stream was not flushed.");
                    state = RecordingFileState.Finalized;
                    announced = true;
                }, () =>
                {
                    Require(announced && state == RecordingFileState.Finalized, "Teardown began before recovery ownership was established.");
                    return Task.FromException(new IOException("injected native teardown failure"));
                });
                throw new Exception("Expected the injected teardown failure.");
            }
            catch (IOException error) when (error.Message == "injected native teardown failure") { }
            Require(state == RecordingFileState.Finalized && File.Exists(file) && new FileInfo(file).Length == 512,
                "Teardown failure lost the finalized file or state.");
            await stream.DisposeAsync();
        });
        foreach (var native in new[] { true, false })
        {
            var name = native ? "native" : "FFmpeg";
            IRecordingBackend Create() => native ? new NativeRecordingBackend(Environment.ProcessPath!) : new FfmpegRecordingBackend();
            RecordingRequest Request(string path) => new(path, new(CaptureKind.Region, new(0, 0, 320, 240)),
                new() { SystemAudio = false, UseFfmpeg = !native, FfmpegPath = Environment.ProcessPath! });
            await check(name + " late collision survives repeated cleanup and disposal", async () =>
            {
                var directory = Path.Combine(root, name + "-collision");
                Directory.CreateDirectory(directory);
                var output = Path.Combine(directory, "collision.mp4");
                var recorder = Create();
                try
                {
                    await recorder.StartAsync(Request(output));
                    await File.WriteAllTextAsync(output, "existing sentinel");
                    string recovery;
                    try { await recorder.StopAsync(); throw new Exception("Expected publication failure."); }
                    catch (RecordingPublicationException error) { recovery = error.RecoveryPath; }
                    Require(recovery == recorder.RecoveryPath && File.Exists(recovery), "Recovery path is missing.");
                    try { await recorder.CancelAsync(); } catch (RecordingPublicationException) { }
                    await recorder.DisposeAsync(); await recorder.DisposeAsync();
                    Require(File.Exists(recovery) && new FileInfo(recovery).Length == 512, "Cleanup deleted finalized media.");
                    Require(await File.ReadAllTextAsync(output) == "existing sentinel", "Destination changed.");
                    var recovered = Path.Combine(directory, "recovered.mp4");
                    await RecordingRecovery.SaveAsAsync(recovery, recovered);
                    Require(File.Exists(recovery) && new FileInfo(recovered).Length == 512, "Save As did not preserve both copies.");
                }
                finally { await recorder.DisposeAsync(); }
            });
            await check(name + " natural publication failure survives automatic cleanup", async () =>
            {
                var directory = Path.Combine(root, name + "-natural");
                Directory.CreateDirectory(directory);
                var output = Path.Combine(directory, "natural.mp4");
                await using var recorder = Create();
                var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                recorder.Failed += error => failed.TrySetResult(error);
                await recorder.StartAsync(Request(output));
                await File.WriteAllTextAsync(output, "natural sentinel");
                var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
                Require(failure is RecordingPublicationException, "Natural publication error lost its recovery data.");
                await recorder.DisposeAsync();
                Require(File.Exists(recorder.RecoveryPath), "Natural cleanup deleted completed media.");
                Require(await File.ReadAllTextAsync(output) == "natural sentinel", "Natural completion overwrote destination.");
            });
            await check(name + " cancelled incomplete fixture is deleted", async () =>
            {
                var directory = Path.Combine(root, name + "-cancel");
                Directory.CreateDirectory(directory);
                await using var recorder = Create();
                await recorder.StartAsync(Request(Path.Combine(directory, "cancel.mp4")));
                await recorder.CancelAsync(); await recorder.DisposeAsync();
                Require(recorder.RecoveryPath is null && Directory.GetFiles(directory).Length == 0, "Cancellation retained incomplete media.");
            });
        }
        await check("native finalized protocol survives worker EOF with actual recovery path", async () =>
        {
            var directory = Path.Combine(root, "native-eof");
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, "natural-eof.mp4");
            await using var recorder = new NativeRecordingBackend(Environment.ProcessPath!);
            var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            recorder.Failed += error => failed.TrySetResult(error);
            await recorder.StartAsync(new(output, new(CaptureKind.Region, new(0, 0, 320, 240)), new()));
            await File.WriteAllTextAsync(output, "EOF sentinel");
            var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await recorder.DisposeAsync();
            Require(error is RecordingPublicationException publication && publication.RecoveryPath == recorder.RecoveryPath && File.Exists(publication.RecoveryPath),
                "Worker EOF lost finalized media or reported the wrong recovery path.");
            Require(await File.ReadAllTextAsync(output) == "EOF sentinel", "Worker EOF changed the existing destination.");
        });
        await check("FFmpeg disposal before natural-exit observer preserves finalized collision media", async () =>
        {
            var directory = Path.Combine(root, "ffmpeg-natural-disposal");
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, "natural-disposal.mp4");
            await using var recorder = new FfmpegRecordingBackend();
            await recorder.StartAsync(new(output, new(CaptureKind.Region, new(0, 0, 320, 240)),
                new() { UseFfmpeg = true, FfmpegPath = Environment.ProcessPath! }));
            // Suppress observer delivery to deterministically reproduce Dispose winning
            // the finish lock after successful process exit. No production test seam.
            typeof(FfmpegRecordingBackend).GetField("expectedExit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(recorder, true);
            var process = (Process)typeof(FfmpegRecordingBackend).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recorder)!;
            await File.WriteAllTextAsync(output, "natural disposal sentinel");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Require(process.ExitCode == 0, "Fixture did not complete successfully.");
            await recorder.DisposeAsync(); await recorder.DisposeAsync();
            Require(recorder.RecoveryPath is { } recovery && File.Exists(recovery) && new FileInfo(recovery).Length == 512,
                "Disposal won the observer race and deleted finalized media.");
            Require(await File.ReadAllTextAsync(output) == "natural disposal sentinel", "Disposal overwrote the existing destination.");
        });
        await check("FFmpeg genuine encoder failure deletes incomplete media", async () =>
        {
            var directory = Path.Combine(root, "ffmpeg-failure");
            Directory.CreateDirectory(directory);
            await using var recorder = new FfmpegRecordingBackend();
            var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            recorder.Failed += error => failed.TrySetResult(error);
            await recorder.StartAsync(new(Path.Combine(directory, "failure.mp4"), new(CaptureKind.Region, new(0, 0, 320, 240)),
                new() { UseFfmpeg = true, FfmpegPath = Environment.ProcessPath! }));
            Require(await failed.Task.WaitAsync(TimeSpan.FromSeconds(8)) is not RecordingPublicationException, "Encoder failure was reported as recoverable.");
            await recorder.DisposeAsync();
            Require(Directory.GetFiles(directory).Length == 0, "Failed encoder leaked incomplete media.");
        });
    }

    public static async Task VerifyMediaAsync(string executable, string directory, Func<string, Func<Task>, Task> check)
    {
        // This is deliberately after all native sessions have ended. FFmpeg is an
        // optional external test inspector, never part of native recording/finalization.
        await check("native recovery MP4s fully decode with optional external inspector", async () =>
        {
            var files = Directory.GetFiles(directory, ".framelet-*.recording.mp4", SearchOption.AllDirectories);
            Require(files.Length == 2, "Expected the explicit-stop and natural-completion recovery clips.");
            foreach (var file in files.Append(Path.Combine(directory, "recovery-explicit", "saved-as.mp4")))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var decoded = await ProcessRunner.RunAsync(executable, ["-v", "error", "-i", file, "-f", "null", "-"], timeout.Token);
                Require(decoded.ExitCode == 0, "External recovery decode failed: " + decoded.Output);
            }
        });
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
