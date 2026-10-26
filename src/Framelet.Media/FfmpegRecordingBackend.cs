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
    private volatile bool expectedExit;
    private readonly object finishLock = new();
    private Task? finishTask;
    private RecordingFileState fileState;
    private bool used;
    private bool disposed;
    public string Name => "FFmpeg";
    public bool CanPause => false;
    public string? RecoveryPath => fileState == RecordingFileState.Finalized ? temporary : null;
    public event Action<string>? StatusChanged;
    public event Action<Exception>? Failed;
    public event Action? Completed;

    public async Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (used) throw new InvalidOperationException("Create a new recording session to record again.");
        used = true;
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
            if (!expectedExit)
            {
                try { await StopAsync(); }
                catch (Exception error) { Failed?.Invoke(error); }
            }
        }
        catch (InvalidOperationException) { }
    }

    public Task PauseAsync() => Task.FromException(new NotSupportedException("Pausing is available with native recording. Stop this clip to save it."));
    public Task ResumeAsync() => PauseAsync();

    public Task StopAsync() => FinishAsync(false);
    public Task CancelAsync() => FinishAsync(true);

    private Task FinishAsync(bool discard)
    {
        lock (finishLock) { used = true; return finishTask ??= FinishCoreAsync(discard); }
    }

    private async Task FinishCoreAsync(bool discard)
    {
        var running = process;
        if (running is null) { Cleanup(); return; }
        expectedExit = true;
        try
        {
            // Disposal may win the finish lock after a successful natural exit but
            // before ObserveExit runs. Reconcile that completed encoder as a stop;
            // only cancellation of an active or failed encoder discards its media.
            if (discard && !(running.HasExited && running.ExitCode == 0))
            {
                if (!running.HasExited) { running.Kill(true); await running.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                if (readError is not null) await readError.WaitAsync(TimeSpan.FromSeconds(5));
                if (readOutput is not null) await readOutput.WaitAsync(TimeSpan.FromSeconds(5));
                return;
            }
            if (!running.HasExited)
            {
                try
                {
                    await running.StandardInput.WriteLineAsync("q");
                    await running.StandardInput.FlushAsync();
                }
                catch (Exception error) when ((error is IOException or InvalidOperationException) && running.HasExited)
                {
                    // The encoder can finish between HasExited and the quit write. Its
                    // exit code and completed output below decide ownership, not a broken pipe.
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await running.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { running.Kill(true); await running.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); throw new IOException("FFmpeg did not finish within 30 seconds. The incomplete file was discarded."); }
            }
            if (readError is not null) await readError.WaitAsync(TimeSpan.FromSeconds(5));
            if (readOutput is not null) await readOutput.WaitAsync(TimeSpan.FromSeconds(5));
            if (running.ExitCode != 0 || temporary is null || !File.Exists(temporary) || new FileInfo(temporary).Length < 256)
                throw new IOException("FFmpeg did not produce a complete clip. " + ErrorTail());
            fileState = RecordingFileState.Finalized;
            try { File.Move(temporary, destination!, false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new RecordingPublicationException(temporary, error);
            }
            fileState = RecordingFileState.Published;
            temporary = null;
            StatusChanged?.Invoke("Clip saved");
            Completed?.Invoke();
        }
        finally
        {
            try
            {
                // A failed stdin write must not leave an encoder recording after its owner
                // has disappeared. Never wait indefinitely for a damaged child or pipe.
                if (!running.HasExited)
                {
                    running.Kill(true);
                    await running.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                if (readError is not null) await readError.WaitAsync(TimeSpan.FromSeconds(5));
                if (readOutput is not null) await readOutput.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { running.Dispose(); process = null; Cleanup(); }
        }
    }

    private string ErrorTail() { lock (errors) return errors.ToString()[Math.Max(0, errors.Length - 2200)..]; }
    private void Cleanup()
    {
        if (fileState != RecordingFileState.Incomplete) return;
        if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        temporary = null;
        fileState = RecordingFileState.Discarded;
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await CancelAsync(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException) { Debug.WriteLine(error); }
        finally { disposed = true; }
    }
}
