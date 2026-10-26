using Framelet.Core;

var passed = 0;
var failed = 0;
var request = new RecordingRequest("test.mp4", new(CaptureKind.Region, new(0, 0, 640, 480)), new());

await Check("completion during startup is deferred and delivered once", async () =>
{
    var ready = Signal();
    var fake = new FakeBackend { Start = _ => ready.Task };
    var session = new RecordingSessionController(request, () => fake);
    var saved = 0;
    session.ClipSaved += _ => saved++;
    var start = session.StartAsync();
    fake.Complete();
    Assert(session.Snapshot.IsBusy && saved == 0, "Completion was handled before startup returned.");
    ready.SetResult();
    await start;
    Assert(session.Snapshot.State == RecordingSessionState.Completed && saved == 1 && fake.Disposals == 1, "Completed take was lost or disposed twice.");
});

await Check("failure during startup cannot start a replacement prematurely", async () =>
{
    var ready = Signal();
    var old = new FakeBackend { Start = _ => ready.Task };
    var current = new FakeBackend();
    var created = 0;
    var session = new RecordingSessionController(request, () => ++created == 1 ? old : current);
    var first = session.StartAsync();
    old.Fail();
    await session.StartAsync();
    Assert(created == 1 && session.Snapshot.IsBusy, "The failed starting take released ownership too early.");
    ready.SetResult();
    await first;
    Assert(session.Snapshot.State == RecordingSessionState.Failed, "Failure was overwritten by startup success.");
    await session.StartAsync();
    old.Fail(); old.Complete(); old.Status("stale status");
    Assert(session.Snapshot.State == RecordingSessionState.Recording && current.Disposals == 0,
        "Late callbacks altered or disposed the new take.");
    Assert(session.Snapshot.Status != "stale status", "Late status altered the new take.");
    await session.CancelAsync();
});

await Check("completion during pause is retained", async () =>
{
    var paused = Signal();
    var fake = new FakeBackend { Pause = () => paused.Task };
    var session = new RecordingSessionController(request, () => fake);
    await session.StartAsync();
    var pause = session.TogglePauseAsync();
    fake.Complete(); fake.Complete();
    paused.SetResult();
    await pause;
    Assert(session.Snapshot.State == RecordingSessionState.Completed && fake.Disposals == 1, "Pause hid completion or duplicated cleanup.");
});

await Check("completion during resume is retained", async () =>
{
    var resumed = Signal();
    var fake = new FakeBackend { Resume = () => resumed.Task };
    var session = new RecordingSessionController(request, () => fake);
    await session.StartAsync(); await session.TogglePauseAsync();
    var resume = session.TogglePauseAsync();
    fake.Complete();
    resumed.SetResult();
    await resume;
    Assert(session.Snapshot.State == RecordingSessionState.Completed, "Resume hid completion.");
});

await Check("startup cancellation reaches only its own cancellation token", async () =>
{
    var old = new FakeBackend { Start = token => Task.Delay(Timeout.Infinite, token) };
    var current = new FakeBackend();
    var created = 0;
    var session = new RecordingSessionController(request, () => ++created == 1 ? old : current);
    var start = session.StartAsync();
    session.CancelStarting();
    await start;
    Assert(session.Snapshot.State == RecordingSessionState.Cancelled && old.Disposals == 1, "Startup did not cancel and clean up.");
    await session.StartAsync();
    Assert(session.Snapshot.State == RecordingSessionState.Recording && current.Disposals == 0, "Old cancellation affected new take.");
    await session.CancelAsync();
});

await Check("failure during stop cannot dispose a replacement", async () =>
{
    var stopped = Signal();
    var old = new FakeBackend { Stop = () => stopped.Task };
    var current = new FakeBackend();
    var created = 0;
    var session = new RecordingSessionController(request, () => ++created == 1 ? old : current);
    await session.StartAsync();
    var stop = session.StopAsync();
    old.Fail();
    await session.StartAsync();
    Assert(created == 1, "Replacement started before stop finished.");
    stopped.SetException(new IOException("stop failure"));
    await stop;
    Assert(session.Snapshot.State == RecordingSessionState.Failed, "Stop failure was hidden.");
    await session.StartAsync();
    old.Complete();
    Assert(current.Disposals == 0 && session.Snapshot.IsRecording, "Stale stop touched replacement.");
    await session.CancelAsync();
});

await Check("natural completion waits for cleanup before making controls idle", async () =>
{
    var disposed = Signal();
    var fake = new FakeBackend { Dispose = () => disposed.Task };
    var session = new RecordingSessionController(request, () => fake);
    var saved = Signal();
    session.ClipSaved += _ => saved.TrySetResult();
    await session.StartAsync();
    fake.Complete();
    Assert(session.Snapshot.IsBusy && !saved.Task.IsCompleted, "Controls became idle while cleanup owned the take.");
    disposed.SetResult();
    await saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert(!session.Snapshot.IsBusy && session.Snapshot.State == RecordingSessionState.Completed, "Natural completion did not settle.");
});

await Check("successful stop saves once even when backend also emits completion", async () =>
{
    var fake = new FakeBackend();
    fake.Stop = () => { fake.Complete(); return Task.CompletedTask; };
    var session = new RecordingSessionController(request, () => fake);
    var saved = 0;
    session.ClipSaved += _ => saved++;
    await session.StartAsync(); await session.StopAsync();
    fake.Complete();
    Assert(saved == 1 && fake.Disposals == 1, "Stop emitted duplicate save or cleanup.");
});

await Check("factory failure produces a usable retry state", async () =>
{
    var created = 0;
    var fake = new FakeBackend();
    var session = new RecordingSessionController(request, () => ++created == 1 ? throw new IOException("factory failure") : fake);
    await session.StartAsync();
    Assert(!session.Snapshot.IsBusy && session.Snapshot.State == RecordingSessionState.Failed, "Factory failure left controls busy.");
    await session.StartAsync();
    Assert(session.Snapshot.IsRecording, "Retry after factory failure did not start.");
    await session.CancelAsync();
});

await Check("publication failure exposes recovery and Save As preserves originals", async () =>
{
    var root = Path.Combine(Path.GetTempPath(), "Framelet-session-recovery-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var source = Path.Combine(root, "completed.mp4");
        var occupied = Path.Combine(root, "occupied.mp4");
        var destination = Path.Combine(root, "recovered.mp4");
        await File.WriteAllBytesAsync(source, new byte[512]);
        await File.WriteAllTextAsync(occupied, "preserve existing");
        var fake = new FakeBackend { RecoveryPath = source, Stop = () => Task.FromException(new RecordingPublicationException(source)) };
        var session = new RecordingSessionController(request, () => fake);
        string? saved = null;
        session.ClipSaved += path => saved = path;
        await session.StartAsync(); await session.StopAsync();
        Assert(session.Snapshot.State == RecordingSessionState.Recoverable && session.Snapshot.RecoveryPath == source,
            "Publication failure was not distinguished from an incomplete recording.");
        await session.StartAsync(); await session.CancelAsync();
        Assert(session.Snapshot.State == RecordingSessionState.Recoverable && File.Exists(source), "Retry or cancellation lost recovery state.");
        await session.SaveRecoveryAsync(occupied);
        Assert(session.Snapshot.State == RecordingSessionState.Recoverable && await File.ReadAllTextAsync(occupied) == "preserve existing",
            "Failed recovery replaced an existing file or lost the recovery action.");
        await session.SaveRecoveryAsync(destination);
        Assert(session.Snapshot.State == RecordingSessionState.Completed && saved == destination && File.Exists(source) && new FileInfo(destination).Length == 512,
            "Save As did not preserve the completed source and notify the actual destination.");
    }
    finally { Directory.Delete(root, true); }
});

Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> check)
{
    try { await check().WaitAsync(TimeSpan.FromSeconds(5)); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

internal sealed class FakeBackend : IRecordingBackend
{
    public string Name => "deterministic test backend";
    public bool CanPause => true;
    public string? RecoveryPath { get; set; }
    public event Action<string>? StatusChanged;
    public event Action<Exception>? Failed;
    public event Action? Completed;
    public Func<CancellationToken, Task> Start { get; set; } = _ => Task.CompletedTask;
    public Func<Task> Pause { get; set; } = () => Task.CompletedTask;
    public Func<Task> Resume { get; set; } = () => Task.CompletedTask;
    public Func<Task> Stop { get; set; } = () => Task.CompletedTask;
    public Func<Task> Dispose { get; set; } = () => Task.CompletedTask;
    public int Disposals { get; private set; }
    public Task StartAsync(RecordingRequest request, CancellationToken ct = default) => Start(ct);
    public Task PauseAsync() => Pause();
    public Task ResumeAsync() => Resume();
    public Task StopAsync() => Stop();
    public Task CancelAsync() => Task.CompletedTask;
    public async ValueTask DisposeAsync() { Disposals++; await Dispose(); }
    public void Complete() => Completed?.Invoke();
    public void Fail() => Failed?.Invoke(new IOException("backend failed"));
    public void Status(string text) => StatusChanged?.Invoke(text);
}
