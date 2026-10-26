using System.Diagnostics;

namespace Framelet.Core;

public enum RecordingSessionState { Ready, Starting, Recording, Paused, Finishing, Completed, Cancelled, Failed }

public sealed record RecordingSessionSnapshot(RecordingSessionState State, bool IsBusy, string Status,
    bool CanPause, TimeSpan Elapsed)
{
    public bool IsRecording => State is RecordingSessionState.Recording or RecordingSessionState.Paused;
}

/// <summary>Owns one replaceable recording take and reconciles backend callbacks with user operations.</summary>
public sealed class RecordingSessionController(RecordingRequest request, Func<IRecordingBackend> createBackend)
{
    private readonly object _gate = new();
    private readonly Stopwatch _elapsed = new();
    private IRecordingBackend? _backend;
    private CancellationTokenSource? _starting;
    private long _generation;
    private bool _busy, _completed, _cancelled;
    private Exception? _failure;
    private RecordingSessionState _state = RecordingSessionState.Ready;
    private string _status = "Ready to record";

    public event Action<RecordingSessionSnapshot>? Changed;
    public event Action<string>? ClipSaved;

    public RecordingSessionSnapshot Snapshot
    {
        get { lock (_gate) return new(_state, _busy, _status, _backend?.CanPause == true, _elapsed.Elapsed); }
    }

    public async Task StartAsync()
    {
        CancellationTokenSource cancellation;
        long generation;
        lock (_gate)
        {
            if (_busy || _state is RecordingSessionState.Recording or RecordingSessionState.Paused or RecordingSessionState.Completed) return;
            _busy = true;
            _completed = _cancelled = false;
            _failure = null;
            _elapsed.Reset();
            _state = RecordingSessionState.Starting;
            _status = "Starting capture…";
            cancellation = _starting = new();
            generation = ++_generation;
        }
        Notify();
        IRecordingBackend? backend = null;
        try
        {
            backend = createBackend();
            lock (_gate) _backend = backend;
            var source = backend;
            backend.StatusChanged += text => BackendStatus(source, generation, text);
            backend.Completed += () => BackendTerminal(source, generation, null);
            backend.Failed += error => BackendTerminal(source, generation, error);
            await backend.StartAsync(request, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (Current(backend, generation) && !HasTerminal)
                {
                    _state = RecordingSessionState.Recording;
                    _status = "Recording";
                    _elapsed.Start();
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    if (error is OperationCanceledException && cancellation.IsCancellationRequested) _cancelled = true;
                    else _failure ??= error;
                }
            }
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_starting, cancellation)) _starting = null; }
            cancellation.Dispose();
            if (backend is null)
            {
                lock (_gate)
                {
                    _busy = false;
                    _state = RecordingSessionState.Failed;
                    _status = _failure?.Message ?? "The recording engine could not be created.";
                }
                Notify();
            }
            else await EndOperationAsync(backend, generation).ConfigureAwait(false);
        }
    }

    public void CancelStarting()
    {
        lock (_gate)
        {
            if (_state != RecordingSessionState.Starting || _starting is null) return;
            _status = "Cancelling startup…";
            _starting.Cancel();
        }
        Notify();
    }

    public Task TogglePauseAsync() => OperateAsync("pause");
    public Task StopAsync() => OperateAsync("stop");
    public Task CancelAsync() => OperateAsync("cancel");

    private async Task OperateAsync(string operation)
    {
        IRecordingBackend? backend;
        long generation;
        bool wasPaused;
        lock (_gate)
        {
            if (_busy) return;
            backend = _backend;
            if (backend is null)
            {
                if (operation != "cancel") return;
                _state = RecordingSessionState.Cancelled;
                _status = "Recording cancelled";
            }
            else if (_state is not (RecordingSessionState.Recording or RecordingSessionState.Paused)) return;
            if (operation == "pause" && backend?.CanPause != true) return;
            generation = _generation;
            wasPaused = _state == RecordingSessionState.Paused;
            _busy = backend is not null;
            if (operation != "pause" && backend is not null)
            {
                _state = RecordingSessionState.Finishing;
                _status = operation == "stop" ? "Finishing the MP4 file…" : "Discarding recording…";
                _elapsed.Stop();
            }
        }
        Notify();
        if (backend is null) return;
        try
        {
            if (operation == "stop") await backend.StopAsync().ConfigureAwait(false);
            else if (operation == "cancel") await backend.CancelAsync().ConfigureAwait(false);
            else if (wasPaused) await backend.ResumeAsync().ConfigureAwait(false);
            else await backend.PauseAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (!Current(backend, generation)) return;
                if (operation == "stop") _completed = true;
                else if (operation == "cancel") _cancelled = true;
                else if (!HasTerminal)
                {
                    _state = wasPaused ? RecordingSessionState.Recording : RecordingSessionState.Paused;
                    _status = wasPaused ? "Recording" : "Paused · paused time will be removed from the clip";
                    if (wasPaused) _elapsed.Start(); else _elapsed.Stop();
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate) { if (Current(backend, generation)) _failure ??= error; }
        }
        finally { await EndOperationAsync(backend, generation).ConfigureAwait(false); }
    }

    private bool Current(IRecordingBackend backend, long generation) =>
        generation == _generation && ReferenceEquals(backend, _backend);

    private bool HasTerminal => _completed || _cancelled || _failure is not null;

    private void BackendStatus(IRecordingBackend backend, long generation, string text)
    {
        lock (_gate)
        {
            if (!Current(backend, generation) || HasTerminal) return;
            _status = text;
        }
        Notify();
    }

    private void BackendTerminal(IRecordingBackend backend, long generation, Exception? error)
    {
        lock (_gate)
        {
            if (!Current(backend, generation)) return;
            if (error is null) _completed = true;
            else _failure ??= error;
            // Preserve terminal signals during start, pause and stop. The operation that
            // currently owns this take performs its cleanup after returning from the backend.
            if (_busy) return;
            _busy = true;
        }
        Notify();
        _ = SettleAsync(backend, generation);
    }

    private async Task EndOperationAsync(IRecordingBackend backend, long generation)
    {
        bool terminal;
        lock (_gate)
        {
            if (!Current(backend, generation)) return;
            terminal = HasTerminal;
            _busy = terminal;
        }
        if (terminal) await SettleAsync(backend, generation).ConfigureAwait(false);
        else Notify();
    }

    private async Task SettleAsync(IRecordingBackend backend, long generation)
    {
        try { await backend.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error)
        {
            lock (_gate) { if (Current(backend, generation)) _failure ??= error; }
        }
        bool saved;
        lock (_gate)
        {
            if (!Current(backend, generation)) return;
            saved = _completed;
            _state = saved ? RecordingSessionState.Completed : _failure is not null
                ? RecordingSessionState.Failed : RecordingSessionState.Cancelled;
            _status = saved ? "Recording saved" : _failure?.Message ?? "Recording cancelled";
            _elapsed.Stop();
            _backend = null;
            _busy = false;
        }
        Notify();
        if (saved) InvokeSafely(ClipSaved, request.OutputPath);
    }

    private void Notify() => InvokeSafely(Changed, Snapshot);

    private static void InvokeSafely<T>(Action<T>? handlers, T value)
    {
        foreach (var handler in handlers?.GetInvocationList() ?? [])
            try { ((Action<T>)handler)(value); }
            catch (Exception error) { Debug.WriteLine(error); }
    }
}
