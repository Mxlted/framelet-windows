using System.Diagnostics;
using Framelet.Core;
using ScreenRecorderLib;

namespace Framelet.Recording;

/// <summary>One native recording session. Completion means the MP4 has been finalized and published.</summary>
internal sealed class NativeSession : IRecordingBackend
{
    private readonly string? _requestedStagingPath;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Recorder? _recorder;
    private FileStream? _stream;
    private string? _stagingPath;
    private string? _outputPath;
    private bool _used;
    private bool _discard;
    private bool _finalized;
    private bool _disposed;
    private CancellationTokenSource? _watchCancellation;

    public string Name => "Windows native";
    public bool CanPause => true;
    public event Action<string>? StatusChanged;
    public event Action<Exception>? Failed;
    public event Action? Completed;

    public NativeSession(string? stagingPath = null) => _requestedStagingPath = stagingPath;

    public async Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_used) throw new InvalidOperationException("Create a new recording session to record again.");
            ct.ThrowIfCancellationRequested();
            _used = true;
            _outputPath = Path.GetFullPath(request.OutputPath);
            if (!Path.GetExtension(_outputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Native recordings are saved as H.264/AAC MP4 files.");
            if (File.Exists(_outputPath)) throw new IOException("The destination already exists. Choose a new filename.");
            var options = NativeRecordingOptions.Create(request);
            var directory = Path.GetDirectoryName(_outputPath)!;
            Directory.CreateDirectory(directory);
            _stagingPath = _requestedStagingPath ?? Path.Combine(directory, $".framelet-{Guid.NewGuid():N}.recording.mp4");
            // The stream owns a new private file. Windows never gets an existing destination
            // to overwrite; publish only after the native sink signals finalization.
            _stream = new FileStream(_stagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            _recorder = Recorder.CreateRecorder(options);
            _recorder.OnStatusChanged += OnNativeStatus;
            _recorder.OnRecordingComplete += OnNativeComplete;
            _recorder.OnRecordingFailed += OnNativeFailure;
            Notify(request.Settings.HardwareEncoding
                ? "Starting Windows capture · hardware encoding preferred"
                : "Starting Windows capture · software encoding");
            _recorder.Record(_stream);
        }
        catch
        {
            _discard = true;
            await ReleaseAsync().ConfigureAwait(false);
            throw;
        }
        finally { _operations.Release(); }

        try
        {
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            _watchCancellation = new CancellationTokenSource();
            _ = WatchTargetAsync(request.Target, _watchCancellation.Token);
        }
        catch
        {
            await CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task PauseAsync() => ChangePauseAsync(true);
    public Task ResumeAsync() => ChangePauseAsync(false);

    private async Task ChangePauseAsync(bool pause)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recorder is null || _completed.Task.IsCompleted)
                throw new InvalidOperationException("There is no active recording.");
            if (pause && _recorder.Status == RecorderStatus.Recording) _recorder.Pause();
            else if (!pause && _recorder.Status == RecorderStatus.Paused) _recorder.Resume();
        }
        finally { _operations.Release(); }
    }

    public Task StopAsync() => FinishAsync(false);
    public Task CancelAsync() => FinishAsync(true);

    private async Task FinishAsync(bool discard)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            _discard |= discard;
            if (_finalized || !_used) return;
            if (_recorder is not null && !_completed.Task.IsCompleted)
            {
                Notify(discard ? "Discarding recording…" : "Finishing MP4…");
                _recorder.Stop();
            }
            if (_recorder is not null)
            {
                try { await _completed.Task.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    // A driver may still own GPU work. Keep resources alive until its callback;
                    // disposing them beneath the worker can crash the entire desktop application.
                    _discard = true;
                    _ = ReleaseAfterNativeCompletionAsync();
                    throw new TimeoutException("Windows is taking too long to finalize this recording. The session is being cleaned up in the background; no completed clip was published.");
                }
                catch
                {
                    _discard = true;
                    await ReleaseAsync().ConfigureAwait(false);
                    if (!discard) throw;
                    return;
                }
            }
            await ReleaseAsync().ConfigureAwait(false);
        }
        finally { _operations.Release(); }
    }

    private async Task ReleaseAfterNativeCompletionAsync()
    {
        try { await _completed.Task.ConfigureAwait(false); }
        catch { /* The failure has already been reported by the native callback. */ }
        await _operations.WaitAsync().ConfigureAwait(false);
        try { await ReleaseAsync().ConfigureAwait(false); }
        catch (Exception ex) { Report(ex); }
        finally { _operations.Release(); }
    }

    private async Task ReleaseAsync()
    {
        if (_finalized) return;
        _finalized = true;
        _watchCancellation?.Cancel();
        var recorder = _recorder;
        _recorder = null;
        if (recorder is not null)
        {
            recorder.OnStatusChanged -= OnNativeStatus;
            recorder.OnRecordingComplete -= OnNativeComplete;
            recorder.OnRecordingFailed -= OnNativeFailure;
            await Task.Run(recorder.Dispose).ConfigureAwait(false);
        }
        if (_stream is not null)
        {
            try
            {
                await _stream.FlushAsync().ConfigureAwait(false);
                _stream.Flush(true);
            }
            finally { await _stream.DisposeAsync().ConfigureAwait(false); _stream = null; }
        }
        if (_stagingPath is null || !File.Exists(_stagingPath)) return;
        if (_discard)
        {
            File.Delete(_stagingPath);
            Notify("Recording discarded");
        }
        else
        {
            if (new FileInfo(_stagingPath).Length < 32)
            {
                File.Delete(_stagingPath);
                throw new IOException("Windows finished without producing a playable video.");
            }
            // Move has no overwrite flag. Even a file created during capture is preserved.
            try { File.Move(_stagingPath, _outputPath!); }
            catch (IOException ex)
            {
                throw new IOException($"The clip was recorded but could not be moved to its destination. The completed MP4 is preserved at {_stagingPath}.", ex);
            }
            Notify("Recording saved");
            foreach (var handler in Completed?.GetInvocationList() ?? [])
                try { ((Action)handler)(); }
                catch (Exception ex) { Debug.WriteLine(ex); }
        }
    }

    private void OnNativeStatus(object? sender, RecordingStatusEventArgs e)
    {
        if (e.Status == RecorderStatus.Recording) _started.TrySetResult();
        Notify(e.Status switch
        {
            RecorderStatus.Recording => "Recording",
            RecorderStatus.Paused => "Paused",
            RecorderStatus.Finishing => "Finishing MP4…",
            _ => "Ready"
        });
    }

    private void OnNativeComplete(object? sender, RecordingCompleteEventArgs e)
    {
        _started.TrySetException(new IOException("Recording ended before it could start."));
        _completed.TrySetResult();
        _ = FinishAfterNativeEventAsync(false);
    }

    private void OnNativeFailure(object? sender, RecordingFailedEventArgs e)
    {
        var exception = new IOException("Windows native recording failed: " + e.Error);
        _started.TrySetException(exception);
        _completed.TrySetException(exception);
        Report(exception);
        _ = FinishAfterNativeEventAsync(true);
    }

    private async Task FinishAfterNativeEventAsync(bool discard)
    {
        // A callback runs on the library's own worker. Never join/dispose that worker
        // directly from its callback, even when completion happens without a UI stop.
        await Task.Yield();
        try { await FinishAsync(discard).ConfigureAwait(false); }
        catch (Exception ex) { Report(ex); }
    }

    private async Task WatchTargetAsync(CaptureTarget target, CancellationToken cancellation)
    {
        var monitors = NativeRecordingOptions.EnumerateMonitors().ToArray();
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(500, cancellation).ConfigureAwait(false);
                var unavailable = target.Kind == CaptureKind.Window && !NativeRecordingOptions.WindowIsAvailable(target.WindowHandle);
                var layoutChanged = target.Kind != CaptureKind.Window &&
                    !monitors.SequenceEqual(NativeRecordingOptions.EnumerateMonitors());
                if (!unavailable && !layoutChanged) continue;
                Notify(unavailable ? "Capture window closed or minimized. Finishing the clip…"
                    : "Display configuration changed. Finishing the clip…");
                await StopAsync().ConfigureAwait(false);
                break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { Report(ex); }
    }

    private void Notify(string message)
    {
        // A UI subscriber must never propagate an exception through a native callback.
        foreach (var handler in StatusChanged?.GetInvocationList() ?? [])
            try { ((Action<string>)handler)(message); }
            catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void Report(Exception exception)
    {
        foreach (var handler in Failed?.GetInvocationList() ?? [])
            try { ((Action<Exception>)handler)(exception); }
            catch (Exception ex) { Debug.WriteLine(ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await CancelAsync().ConfigureAwait(false); }
        finally { _disposed = true; }
    }
}

