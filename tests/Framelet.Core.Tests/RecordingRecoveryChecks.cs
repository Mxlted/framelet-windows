using Framelet.Core;
using Framelet.Media;

internal static class RecordingRecoveryChecks
{
    public static async Task RunIntegrationAsync(string executable, string root, Func<string, Func<Task>, Task> check)
    {
        await check("FFmpeg late collision recovery survives disposal and fully decodes", async () =>
        {
            var directory = Path.Combine(root, "ffmpeg-recovery");
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, "occupied.mp4");
            await using var recorder = new FfmpegRecordingBackend();
            await recorder.StartAsync(new(destination, new(CaptureKind.Region, new(100, 100, 320, 240)),
                new() { UseFfmpeg = true, FrameRate = 30, FfmpegPath = executable }));
            await Task.Delay(500);
            await File.WriteAllTextAsync(destination, "existing destination sentinel");
            string recovery;
            try { await recorder.StopAsync(); throw new Exception("Expected a publication failure."); }
            catch (RecordingPublicationException error) { recovery = error.RecoveryPath; }
            await recorder.DisposeAsync(); await recorder.DisposeAsync();
            Require(File.Exists(recovery) && recovery == recorder.RecoveryPath, "Disposal lost the completed recording.");
            Require(await File.ReadAllTextAsync(destination) == "existing destination sentinel", "Existing destination was changed.");
            var decoded = await ProcessRunner.RunAsync(executable, ["-v", "error", "-i", recovery, "-f", "null", "-"]);
            Require(decoded.ExitCode == 0, "Recovery MP4 did not decode: " + decoded.Output);
            var saved = Path.Combine(directory, "saved-as.mp4");
            await RecordingRecovery.SaveAsAsync(recovery, saved);
            decoded = await ProcessRunner.RunAsync(executable, ["-v", "error", "-i", saved, "-f", "null", "-"]);
            Require(decoded.ExitCode == 0 && File.Exists(recovery), "Save As did not preserve decodable media.");
        });
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
