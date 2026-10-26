using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Framelet.Core;
using Framelet.Recording;

internal static class Program
{
    private static int _passed, _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (RecoveryChecks.IsFixture(args)) return RecoveryChecks.RunFixtureAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--without-ffmpeg"))
        {
            Environment.SetEnvironmentVariable("PATH", "");
            Console.WriteLine("Native validation child process PATH is empty; no FFmpeg executable is available through PATH.");
        }
        SetProcessDpiAwarenessContext(new nint(-4));
        if (!args.Contains("--live"))
        {
            RunValidationAsync().GetAwaiter().GetResult();
            return Summary();
        }

        var app = new Application();
        var text = new TextBlock { FontSize = 38, Foreground = Brushes.White, Margin = new Thickness(30), Text = "Framelet capture check" };
        var panel = new Grid { Background = new SolidColorBrush(Color.FromRgb(32, 34, 38)) };
        panel.Children.Add(text);
        var window = new Window
        {
            Title = "Framelet native recording validation", Width = 640, Height = 400,
            Left = 40, Top = 40, Content = panel, Topmost = true
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var counter = 0;
        timer.Tick += (_, _) =>
        {
            counter++;
            text.Text = $"Native capture check\nFrame {counter}\n{DateTime.Now:HH:mm:ss.fff}";
            panel.Background = new SolidColorBrush(Color.FromRgb((byte)(28 + counter % 100), 40, 53));
        };
        window.Loaded += async (_, _) =>
        {
            timer.Start();
            try
            {
                await RunValidationAsync();
                var handle = new WindowInteropHelper(window).Handle;
                var bounds = new PixelRect(40, 40, 640, 400);
                var target = new CaptureTarget(CaptureKind.Window, bounds, handle);
                var directory = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault()
                    ?? Path.Combine(Path.GetTempPath(), "Framelet-native-validation", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(directory);
                Console.WriteLine("Live outputs: " + directory);
                Console.WriteLine($"Audio endpoints: {NativeAudioDevices.GetOutputs().Count} output, {NativeAudioDevices.GetInputs().Count} input");

                await Check("native 60 FPS window, pause/resume and finalization", async () =>
                {
                    var file = Path.Combine(directory, "native-window-pause.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    recorder.StatusChanged += Console.WriteLine;
                    await recorder.StartAsync(new(file, target, new() { SystemAudio = false }));
                    await Task.Delay(1400);
                    await recorder.PauseAsync();
                    await Task.Delay(1000);
                    await recorder.ResumeAsync();
                    await Task.Delay(1400);
                    await recorder.StopAsync();
                    Assert(File.Exists(file) && new FileInfo(file).Length > 1000, "MP4 was not finalized.");
                });

                await Check("native system audio and microphone", async () =>
                {
                    var file = Path.Combine(directory, "native-audio.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    recorder.StatusChanged += Console.WriteLine;
                    await recorder.StartAsync(new(file, target, new() { SystemAudio = true, Microphone = true }));
                    var tone = Path.Combine(directory, "loopback-test-tone.wav");
                    CreateTestTone(tone);
                    Assert(PlaySound(tone, 0, 0x20003), "The loopback test tone could not be played.");
                    await Task.Delay(2500);
                    PlaySound(null, 0, 0);
                    await recorder.StopAsync();
                    Assert(File.Exists(file) && new FileInfo(file).Length > 1000, "MP4 was not finalized.");
                });

                await Check("native region at requested 120 FPS", async () =>
                {
                    var file = Path.Combine(directory, "native-region-120.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    recorder.StatusChanged += Console.WriteLine;
                    await recorder.StartAsync(new(file, target with { Kind = CaptureKind.Region, WindowHandle = 0 },
                        new() { SystemAudio = false, FrameRate = 120 }));
                    await Task.Delay(2000);
                    await recorder.StopAsync();
                    Assert(File.Exists(file), "MP4 was not finalized.");
                });

                await Check("native cancellation deletes only its own output", async () =>
                {
                    var file = Path.Combine(directory, "cancelled.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    await recorder.StartAsync(new(file, target, new() { SystemAudio = false }));
                    await Task.Delay(350);
                    await recorder.CancelAsync();
                    Assert(!File.Exists(file), "Cancelled clip was published.");
                    Assert(!Directory.EnumerateFiles(directory, ".framelet-*.recording.mp4").Any(), "Native staging file was leaked.");
                });

                await Check("native late destination collision preserves a finalized MP4 through disposal", async () =>
                {
                    var recoveryDirectory = Path.Combine(directory, "recovery-explicit");
                    Directory.CreateDirectory(recoveryDirectory);
                    var file = Path.Combine(recoveryDirectory, "occupied.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    await recorder.StartAsync(new(file, target, new() { SystemAudio = false, FfmpegPath = "missing-ffmpeg.exe" }));
                    await Task.Delay(600);
                    await File.WriteAllTextAsync(file, "existing destination sentinel");
                    await Reject<RecordingPublicationException>(recorder.StopAsync);
                    var recovery = recorder.RecoveryPath;
                    await recorder.DisposeAsync(); await recorder.DisposeAsync();
                    Assert(recovery is not null && File.Exists(recovery) && new FileInfo(recovery).Length > 1000, "Completed recovery MP4 was lost.");
                    Assert(await File.ReadAllTextAsync(file) == "existing destination sentinel", "Existing destination was changed.");
                    await RecordingRecovery.SaveAsAsync(recovery!, Path.Combine(recoveryDirectory, "saved-as.mp4"));
                    Console.WriteLine("Externally decode recovery: " + recovery);
                });

                await Check("native natural completion collision survives automatic cleanup and disposal", async () =>
                {
                    var recoveryDirectory = Path.Combine(directory, "recovery-natural");
                    Directory.CreateDirectory(recoveryDirectory);
                    var child = new Window { Title = "Recovery capture target", Width = 320, Height = 240,
                        Content = new TextBlock { Text = "Preserve completed recording", FontSize = 18 } };
                    child.Show();
                    var childTarget = new CaptureTarget(CaptureKind.Window, new(0, 0, 320, 240), new WindowInteropHelper(child).Handle);
                    var file = Path.Combine(recoveryDirectory, "occupied.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                    recorder.Failed += error => failure.TrySetResult(error);
                    try
                    {
                        await recorder.StartAsync(new(file, childTarget, new() { SystemAudio = false }));
                        await Task.Delay(600);
                        await File.WriteAllTextAsync(file, "natural destination sentinel");
                        child.Close();
                        Assert(await failure.Task.WaitAsync(TimeSpan.FromSeconds(15)) is RecordingPublicationException,
                            "Natural finalization did not report recovery.");
                        await recorder.DisposeAsync(); await recorder.DisposeAsync();
                        Assert(File.Exists(recorder.RecoveryPath), "Natural cleanup deleted the completed MP4.");
                        Assert(await File.ReadAllTextAsync(file) == "natural destination sentinel", "Natural completion changed the existing destination.");
                        Console.WriteLine("Externally decode recovery: " + recorder.RecoveryPath);
                    }
                    finally { child.Close(); }
                });

                await Check("native worker failure is isolated and staging is removed", async () =>
                {
                    var file = Path.Combine(directory, "failed-worker.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    recorder.Failed += _ => failure.TrySetResult();
                    await recorder.StartAsync(new(file, target, new() { SystemAudio = false }));
                    using (var worker = Process.GetProcessById(recorder.ProcessId)) worker.Kill();
                    await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (Directory.EnumerateFiles(directory, ".framelet-*.recording.mp4").Any() && DateTime.UtcNow < deadline)
                        await Task.Delay(50);
                    Assert(!File.Exists(file), "Failed recording was published.");
                    Assert(!Directory.EnumerateFiles(directory, ".framelet-*.recording.mp4").Any(), "Failed worker leaked a staging file.");
                });

                await Check("closing the captured window finalizes and signals completion", async () =>
                {
                    var child = new Window { Title = "Temporary capture target", Width = 320, Height = 240,
                        Content = new TextBlock { Text = "Closing target check", FontSize = 22, Margin = new Thickness(20) } };
                    child.Show();
                    var childTarget = new CaptureTarget(CaptureKind.Window, new(0, 0, 320, 240), new WindowInteropHelper(child).Handle);
                    var file = Path.Combine(directory, "native-target-closed.mp4");
                    await using var recorder = new NativeRecordingBackend();
                    var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    recorder.Completed += () => complete.TrySetResult();
                    try
                    {
                        await recorder.StartAsync(new(file, childTarget, new() { SystemAudio = false }));
                        await Task.Delay(500);
                        child.Close();
                        await complete.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        Assert(File.Exists(file), "Closed target did not finalize a clip.");
                    }
                    finally { child.Close(); }
                });
                var inspector = args.SkipWhile(a => a != "--decode-with").Skip(1).FirstOrDefault();
                if (inspector is not null)
                    await RecoveryChecks.VerifyMediaAsync(inspector, directory, Check);
            }
            catch (Exception ex) { _failed++; Console.WriteLine(ex); }
            finally { timer.Stop(); app.Shutdown(); }
        };
        app.Run(window);
        return Summary();
    }

    private static async Task RunValidationAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Framelet-native-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = new CaptureTarget(CaptureKind.Region, new(0, 0, 640, 480));
        try
        {
            await RecoveryChecks.RunAsync(directory, Check);
            await Check("pre-existing file is preserved", async () =>
            {
                var file = Path.Combine(directory, "existing.mp4");
                await File.WriteAllTextAsync(file, "keep this exact content");
                await using var recorder = new NativeRecordingBackend();
                await Reject<IOException>(() => recorder.StartAsync(new(file, target, new())));
                Assert(await File.ReadAllTextAsync(file) == "keep this exact content", "Existing file changed.");
            });
            await Check("wrong output extension rejected", async () =>
            {
                await using var recorder = new NativeRecordingBackend();
                await Reject<ArgumentException>(() => recorder.StartAsync(new(Path.Combine(directory, "clip.gif"), target, new())));
            });
            await Check("invalid dimensions rejected", async () =>
            {
                await using var recorder = new NativeRecordingBackend();
                await Reject<ArgumentException>(() => recorder.StartAsync(new(Path.Combine(directory, "small.mp4"),
                    target with { Bounds = new(0, 0, 1, 1) }, new())));
            });
            await Check("invalid recording settings rejected", async () =>
            {
                await using var recorder = new NativeRecordingBackend();
                await Reject<ArgumentException>(() => recorder.StartAsync(new(Path.Combine(directory, "fps.mp4"), target, new() { FrameRate = 121 })));
            });
            await Check("cancelled start creates no file", async () =>
            {
                var file = Path.Combine(directory, "token.mp4");
                await using var recorder = new NativeRecordingBackend();
                await Reject<OperationCanceledException>(() => recorder.StartAsync(new(file, target, new()), new CancellationToken(true)));
                Assert(!File.Exists(file), "Cancelled start wrote an output.");
            });
            await Check("inactive pause is explicit error", async () =>
            {
                await using var recorder = new NativeRecordingBackend();
                await Reject<InvalidOperationException>(recorder.PauseAsync);
                await recorder.CancelAsync();
            });
            await Check("failed starts do not leak staging files", () =>
            {
                Assert(!Directory.EnumerateFiles(directory, ".framelet-*.recording.mp4").Any(), "Staging file leaked.");
                return Task.CompletedTask;
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task Check(string name, Func<Task> test)
    {
        try { await test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
    }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static int Summary()
    {
        Console.WriteLine($"{_passed} passed; {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    private static void CreateTestTone(string path)
    {
        const int rate = 48000, samples = rate * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 1000));
    }
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? sound, nint module, uint flags);
}
