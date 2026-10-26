using System.Diagnostics;
using System.Text;
using Framelet.Core;

namespace Framelet.Media;

public sealed class FfmpegRecordingBackend : IRecordingBackend
{
    private Process? process;
    private Task? readError;
    private Task? readOutput;
    private readonly StringBuilder errors = new();
    private string? temporary, destination;
    private bool expectedExit;
    public string Name => "FFmpeg";
    public bool CanPause => false;
    public event Action<string>? StatusChanged;
    public event Action<Exception>? Failed;
    public event Action? Completed;

    public async Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        if (process is not null) throw new InvalidOperationException("A recording is already active.");
        var executable = await FfmpegLocator.VerifyAsync(request.Settings.FfmpegPath, ct);
        destination = Path.GetFullPath(request.OutputPath);
        if (File.Exists(destination)) throw new IOException("The output file already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        temporary = Path.Combine(Path.GetDirectoryName(destination)!, "." + Path.GetFileNameWithoutExtension(destination) + "." + Guid.NewGuid().ToString("N") + ".partial.mp4");
        var info = ProcessRunner.Create(executable, FfmpegArguments.Record(request, temporary));
        info.RedirectStandardInput = true;
        process = new Process { StartInfo = info };
        expectedExit = false;
        errors.Clear();
        if (!process.Start()) throw new IOException("FFmpeg could not start.");
        var running = process;
        readError = Task.Run(async () =>
        {
            while (await running.StandardError.ReadLineAsync() is { } line)
            {
                lock (errors) { errors.AppendLine(line); if (errors.Length > 16000) errors.Remove(0, errors.Length - 12000); }
            }
        });
        readOutput = running.StandardOutput.ReadToEndAsync();
        try
        {
            await Task.Delay(900, ct);
            if (running.HasExited)
            {
                await readError;
                throw new IOException("FFmpeg could not begin recording. " + ErrorTail());
            }
            StatusChanged?.Invoke("Recording with FFmpeg · " + request.Settings.FfmpegVideoCodec);
            _ = ObserveExit(running);
        }
        catch { await CancelAsync(); throw; }
    }

    private async Task ObserveExit(Process running)
    {
        try
        {
            await running.WaitForExitAsync();
            if (readError is not null) await readError;
            if (!expectedExit) Failed?.Invoke(new IOException("FFmpeg stopped unexpectedly. " + ErrorTail()));
        }
        catch (InvalidOperationException) { }
    }

    public Task PauseAsync() => Task.FromException(new NotSupportedException("Pausing is available with native recording. Stop this clip to save it."));
    public Task ResumeAsync() => PauseAsync();

    public async Task StopAsync()
    {
        var running = process ?? throw new InvalidOperationException("No recording is active.");
        expectedExit = true;
        try
        {
            if (!running.HasExited)
            {
                await running.StandardInput.WriteLineAsync("q");
                await running.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await running.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { running.Kill(true); await running.WaitForExitAsync(); throw new IOException("FFmpeg did not finish within 30 seconds. The incomplete file was discarded."); }
            }
            if (readError is not null) await readError;
            if (readOutput is not null) await readOutput;
            if (running.ExitCode != 0 || temporary is null || !File.Exists(temporary) || new FileInfo(temporary).Length < 256)
                throw new IOException("FFmpeg did not produce a complete clip. " + ErrorTail());
            File.Move(temporary, destination!, false);
            temporary = null;
            StatusChanged?.Invoke("Clip saved");
            Completed?.Invoke();
        }
        finally { running.Dispose(); process = null; Cleanup(); }
    }

    public async Task CancelAsync()
    {
        expectedExit = true;
        if (process is { } running)
        {
            if (!running.HasExited) { running.Kill(true); await running.WaitForExitAsync(); }
            if (readError is not null) await readError;
            if (readOutput is not null) await readOutput;
            running.Dispose();
            process = null;
        }
        Cleanup();
    }

    private string ErrorTail() { lock (errors) return errors.ToString()[Math.Max(0, errors.Length - 2200)..]; }
    private void Cleanup() { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); temporary = null; }
    public async ValueTask DisposeAsync() { if (process is not null || temporary is not null) await CancelAsync(); }
}
