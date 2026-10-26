using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using Framelet.Core;

namespace Framelet.Recording;

/// <summary>Isolates capture drivers and native codecs from the desktop application's process.</summary>
public sealed class NativeRecordingBackend : IRecordingBackend
{
    private readonly string _hostPath;
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _pending = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly SemaphoreSlim _termination = new(1, 1);
    private readonly object _finishLock = new();
    private readonly StringBuilder _errors = new();
    private Process? _process;
    private Task? _readOutput;
    private Task? _readErrors;
    private string? _stagingPath;
    private int _nextCommand;
    private int _used;
    private volatile bool _disposed;
    private volatile bool _ended;
    private volatile bool _finishing;
    private int _reportedFailure;
    private volatile bool _completedOutput;
    private volatile RecordingFileState _fileState;
    private Task? _finishTask;

    public string Name => "Windows native";
    public bool CanPause => true;
    public string? RecoveryPath => _fileState == RecordingFileState.Finalized ? _stagingPath : null;
    internal int ProcessId => _process?.Id ?? 0;
    public event Action<string>? StatusChanged;
    public event Action<Exception>? Failed;
    public event Action? Completed;

    public NativeRecordingBackend() : this(Path.Combine(AppContext.BaseDirectory, "Framelet.NativeHost.exe")) { }
    internal NativeRecordingBackend(string hostPath) => _hostPath = hostPath;

    public async Task StartAsync(RecordingRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (_ended) throw new InvalidOperationException("Create a new recording session to record again.");
        if (Interlocked.Exchange(ref _used, 1) != 0) throw new InvalidOperationException("Create a new recording session to record again.");
        Validate(request);
        var hostPath = _hostPath;
        if (!File.Exists(hostPath))
            throw new FileNotFoundException("The native recording component is missing. Reinstall the complete Framelet package.", hostPath);
        var sessionId = Guid.NewGuid().ToString("N");
        var outputPath = Path.GetFullPath(request.OutputPath);
        _stagingPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".framelet-{sessionId}.recording.mp4");
        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--stdio");
        _process = Process.Start(startInfo) ?? throw new IOException("Windows could not start the native recording component.");
        _readOutput = ReadOutputAsync(_process);
        _readErrors = ReadErrorsAsync(_process);
        var hostRequest = new NativeHostRequest(sessionId, outputPath, request.Target.Kind, request.Target.Bounds,
            request.Target.WindowHandle.ToInt64(), request.Target.DeviceName, request.Settings);
        try
        {
            await SendAsync("start", hostRequest, TimeSpan.FromSeconds(25), ct).ConfigureAwait(false);
        }
        catch
        {
            await TerminateAsync(deleteStaging: true).ConfigureAwait(false);
            throw;
        }
    }

    public Task PauseAsync() => ControlAsync("pause");
    public Task ResumeAsync() => ControlAsync("resume");
    public Task StopAsync() => FinishAsync(false);
    public Task CancelAsync() => FinishAsync(true);

    private async Task ControlAsync(string operation)
    {
        try { await SendAsync(operation, null, TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException ex)
        {
            await TerminateAsync(deleteStaging: true).ConfigureAwait(false);
            Report(ex);
            throw;
        }
    }

    private Task FinishAsync(bool discard)
    {
        lock (_finishLock) return _finishTask ??= FinishCoreAsync(discard);
    }

    private async Task FinishCoreAsync(bool discard)
    {
        if (_process is null || _ended) { _ended = true; return; }
        _finishing = true;
        var success = false;
        Exception? failure = null;
        try
        {
            await SendAsync(discard ? "cancel" : "stop", null,
                TimeSpan.FromSeconds(discard ? 8 : 45)).ConfigureAwait(false);
            success = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            // MP4 publication happens only in the worker after finalization. Killing a hung
            // worker cannot overwrite an old file or publish a half-written destination.
            await TerminateAsync(deleteStaging: discard || !success).ConfigureAwait(false);
        }
        if (RecoveryPath is { } recovery && (!discard || failure is not null))
            throw new RecordingPublicationException(recovery, failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task SendAsync(string operation, NativeHostRequest? request, TimeSpan timeout,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is null || _ended || _process.HasExited)
            throw new InvalidOperationException("There is no active native recording.");
        var id = Interlocked.Increment(ref _nextCommand);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = response;
        try
        {
            await _commands.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new NativeHostCommand(id, operation, request))).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            }
            finally { _commands.Release(); }
            await response.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Windows did not finish the recording {operation} operation. The native worker will be stopped; try a smaller area, lower frame rate, or software encoding.", ex);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReadOutputAsync(Process process)
    {
        Exception? failure = null;
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 65536) throw new InvalidDataException("Invalid response from the recording component.");
                var message = JsonSerializer.Deserialize<NativeHostMessage>(line);
                if (message is null) continue;
                if (message.RecoveryPath is not null)
                {
                    if (!string.Equals(Path.GetFullPath(message.RecoveryPath), _stagingPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The recording component reported an unexpected recovery path.");
                    _fileState = RecordingFileState.Finalized;
                }
                switch (message.Kind)
                {
                    case "finalized":
                        break;
                    case "result":
                        if (_pending.TryGetValue(message.Id, out var response))
                        {
                            if (message.ErrorType is null) response.TrySetResult();
                            else response.TrySetException(MapError(message));
                        }
                        break;
                    case "status":
                        Notify(message.Text ?? "");
                        break;
                    case "failed":
                        Report(MapError(message));
                        _ = CleanupAfterFailureAsync();
                        break;
                    case "completed":
                        _completedOutput = true;
                        _fileState = RecordingFileState.Published;
                        foreach (var handler in Completed?.GetInvocationList() ?? [])
                            try { ((Action)handler)(); }
                            catch (Exception ex) { Debug.WriteLine(ex); }
                        break;
                }
            }
            if (!_finishing && !_ended && !_completedOutput)
                failure = new IOException("The native recording component stopped unexpectedly. The destination was not overwritten.");
        }
        catch (Exception ex) { if (!_ended) failure = ex; }
        finally
        {
            var error = failure ?? new IOException("The native recording component has closed.");
            foreach (var response in _pending.Values) response.TrySetException(error);
            if (failure is not null)
            {
                Report(failure);
                _ = CleanupAfterFailureAsync();
            }
        }
    }

    private async Task ReadErrorsAsync(Process process)
    {
        try
        {
            var buffer = new char[1024];
            int read;
            while ((read = await process.StandardError.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                lock (_errors)
                {
                    _errors.Append(buffer, 0, read);
                    if (_errors.Length > 8192) _errors.Remove(0, _errors.Length - 8192);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Debug.WriteLine(ex); }
    }

    private async Task CleanupAfterFailureAsync()
    {
        await Task.Yield();
        try { await FinishAsync(true).ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private async Task TerminateAsync(bool deleteStaging)
    {
        await _termination.WaitAsync().ConfigureAwait(false);
        try
        {
            var process = _process;
            if (process is null) return;
            _ended = true;
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch (TimeoutException)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                }
                if (_readOutput is not null) await _readOutput.ConfigureAwait(false);
                if (_readErrors is not null) await _readErrors.ConfigureAwait(false);
            }
            finally
            {
                process.Dispose();
                _process = null;
                if (deleteStaging && _fileState == RecordingFileState.Incomplete && _stagingPath is not null && File.Exists(_stagingPath))
                {
                    File.Delete(_stagingPath);
                    _fileState = RecordingFileState.Discarded;
                }
            }
        }
        finally { _termination.Release(); }
    }

    private static void Validate(RecordingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Settings.Validate();
        if (!Path.GetExtension(request.OutputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Native recordings are saved as H.264/AAC MP4 files.");
        if (File.Exists(request.OutputPath)) throw new IOException("The destination already exists. Choose a new filename.");
        if (request.Target.Bounds.Width is < 2 or > 16384 || request.Target.Bounds.Height is < 2 or > 16384)
            throw new ArgumentException("Choose a recording area between 2 and 16384 pixels in each dimension.");
    }

    private static Exception MapError(NativeHostMessage message) => message.ErrorType switch
    {
        nameof(RecordingPublicationException) when message.RecoveryPath is not null => new RecordingPublicationException(message.RecoveryPath),
        nameof(ArgumentException) => new ArgumentException(message.Text),
        nameof(InvalidOperationException) => new InvalidOperationException(message.Text),
        nameof(TimeoutException) => new TimeoutException(message.Text),
        nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(message.Text),
        nameof(DllNotFoundException) or nameof(TypeInitializationException) =>
            new InvalidOperationException("Windows could not load the native recording engine. Install the Microsoft Visual C++ 2015–2022 x64 runtime and, on Windows N, the Media Feature Pack. " + message.Text),
        _ => new IOException(message.Text)
    };

    private void Notify(string message)
    {
        foreach (var handler in StatusChanged?.GetInvocationList() ?? [])
            try { ((Action<string>)handler)(message); }
            catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private void Report(Exception error)
    {
        if (Interlocked.Exchange(ref _reportedFailure, 1) != 0) return;
        if (RecoveryPath is { } recovery && error is not RecordingPublicationException)
            error = new RecordingPublicationException(recovery, error);
        lock (_errors)
            if (_errors.Length > 0 && error is not RecordingPublicationException) error = new IOException(error.Message + " " + _errors.ToString().Trim(), error);
        foreach (var handler in Failed?.GetInvocationList() ?? [])
            try { ((Action<Exception>)handler)(error); }
            catch (Exception ex) { Debug.WriteLine(ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Debug.WriteLine(ex);
            await TerminateAsync(deleteStaging: true).ConfigureAwait(false);
        }
        finally { _disposed = true; }
    }
}
