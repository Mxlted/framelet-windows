using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Framelet.Recording;

// These subprocess fixtures verify discovery isolation and protocol handling without audio hardware.
internal static class AudioDeviceChecks
{
    private const string ModeVariable = "FRAMELET_AUDIO_TEST_MODE";
    private const string PidVariable = "FRAMELET_AUDIO_TEST_PID_FILE";

    internal static bool IsFixture(string[] args) => args.Contains("--list-audio-devices");

    internal static async Task<int> RunFixtureAsync(string[] args)
    {
        if (args is not ["--list-audio-devices", "input" or "output"]) return 2;
        switch (Environment.GetEnvironmentVariable(ModeVariable))
        {
            case "valid":
                WriteDevices([new("fixture-" + args[1], "Fixture " + args[1] + " café", true), new("second-" + args[1], "Second endpoint", false)]);
                return 0;
            case "empty": Console.WriteLine("[]"); return 0;
            case "malformed": Console.WriteLine("{not-json"); return 0;
            case "null-name": Console.WriteLine("[{\"Id\":\"valid\",\"Name\":null,\"IsDefault\":true}]"); return 0;
            case "empty-id": Console.WriteLine("[{\"Id\":\"\",\"Name\":\"Endpoint\",\"IsDefault\":true}]"); return 0;
            case "null-list": Console.WriteLine("null"); return 0;
            case "native-exit":
                Console.Error.WriteLine("Fixture endpoint discovery failed.");
                return unchecked((int)0xC0000005);
            case "stdout-limit": Console.Write(new string('x', 1_048_577)); return 0;
            case "device-limit":
                WriteDevices(Enumerable.Range(0, 257).Select(index => new NativeAudioDevice("device-" + index, "Endpoint", false)).ToArray());
                return 0;
            case "stderr-volume":
                await Console.Error.WriteAsync(new string('e', 1_048_576));
                Console.WriteLine("[]");
                return 0;
            case "timeout":
                await File.WriteAllTextAsync(Environment.GetEnvironmentVariable(PidVariable)!, Environment.ProcessId.ToString());
                await Task.Delay(TimeSpan.FromMinutes(5));
                return 0;
            default: return 3;
        }
    }

    internal static async Task RunAsync(string directory, Func<string, Func<Task>, Task> check)
    {
        await check("audio discovery isolates input and output queries and retains device identity", async () =>
        {
            foreach (var microphone in new[] { false, true })
            {
                var start = Fixture("valid", microphone);
                Require(!start.UseShellExecute && start.CreateNoWindow && start.RedirectStandardOutput && start.RedirectStandardError,
                    "Discovery must isolate and redirect its hidden child process.");
                var kind = microphone ? "input" : "output";
                Require(start.ArgumentList.SequenceEqual(new[] { "--list-audio-devices", kind }), "The query routed to the wrong endpoint kind.");
                var devices = await NativeAudioDevices.QueryAsync(start, TimeSpan.FromSeconds(10));
                Require(devices.Count == 2 && devices[0] == new NativeAudioDevice("fixture-" + kind, "Fixture " + kind + " café", true) &&
                    devices[1] == new NativeAudioDevice("second-" + kind, "Second endpoint", false), "Discovery lost endpoint IDs, names, order or default flags.");
            }
        });
        await check("audio discovery accepts a machine with no endpoints", async () =>
        {
            var devices = await NativeAudioDevices.QueryAsync(Fixture("empty"), TimeSpan.FromSeconds(10));
            Require(devices.Count == 0, "Empty device inventory was not preserved.");
        });
        await check("audio discovery rejects malformed responses and invalid device fields", async () =>
        {
            foreach (var mode in new[] { "malformed", "null-name", "empty-id", "null-list" })
                await Reject<InvalidDataException>(() => NativeAudioDevices.QueryAsync(Fixture(mode), TimeSpan.FromSeconds(10)));
        });
        await check("audio discovery reports a simulated native worker failure without terminating its caller", async () =>
        {
            await Reject<IOException>(() => NativeAudioDevices.QueryAsync(Fixture("native-exit"), TimeSpan.FromSeconds(10)));
            var devices = await NativeAudioDevices.QueryAsync(Fixture("valid"), TimeSpan.FromSeconds(10));
            Require(devices.Count == 2, "A failed worker prevented a subsequent query.");
        });
        await check("audio discovery rejects oversized standard output", async () =>
        {
            await Reject<InvalidDataException>(() => NativeAudioDevices.QueryAsync(Fixture("stdout-limit"), TimeSpan.FromSeconds(10)));
        });
        await check("audio discovery rejects excessive device counts", async () =>
        {
            await Reject<InvalidDataException>(() => NativeAudioDevices.QueryAsync(Fixture("device-limit"), TimeSpan.FromSeconds(10)));
        });
        await check("audio discovery drains large diagnostic output without blocking device results", async () =>
        {
            var devices = await NativeAudioDevices.QueryAsync(Fixture("stderr-volume"), TimeSpan.FromSeconds(10));
            Require(devices.Count == 0, "Diagnostic output blocked a valid empty inventory.");
        });
        await check("audio discovery deadline terminates its own stalled worker", async () =>
        {
            var pidFile = Path.Combine(directory, "audio-worker.pid");
            var start = Fixture("timeout");
            start.Environment[PidVariable] = pidFile;
            try
            {
                await Reject<TimeoutException>(() => NativeAudioDevices.QueryAsync(start, TimeSpan.FromSeconds(5)));
                Require(File.Exists(pidFile), "The timeout fixture did not reach its discovery operation.");
                var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
                Require(!IsRunning(pid), "The discovery deadline left its worker running.");
            }
            finally
            {
                if (File.Exists(pidFile) && int.TryParse(await File.ReadAllTextAsync(pidFile), out var pid))
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                    }
                    catch (ArgumentException) { }
                }
            }
        });
    }

    private static ProcessStartInfo Fixture(string mode, bool microphone = false)
    {
        var start = NativeAudioDevices.CreateStartInfo(Environment.ProcessPath!, microphone);
        start.Environment[ModeVariable] = mode;
        return start;
    }
    private static void WriteDevices(NativeAudioDevice[] devices) => Console.WriteLine(JsonSerializer.Serialize(devices));
    private static bool IsRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
