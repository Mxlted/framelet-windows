using Framelet.Core;
using Framelet.Media;

var failures = 0;
var count = 0;
var testRoot = Path.Combine(Path.GetTempPath(), "Framelet-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
void Check(string name, Action action)
{
    count++;
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Require(bool value) { if (!value) throw new Exception("Assertion failed."); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation to reject the input."); }
var settingsPath = Path.Combine(testRoot, "settings.json");
var store = new SettingsStore(settingsPath);
Check("Native recording is the default", () => Require(!new AppSettings().Recording.UseFfmpeg));
Check("Settings preserve actual backend switch", () => { var s = new AppSettings(); s.Recording.UseFfmpeg = true; store.Save(s); Require(store.Load().Recording.UseFfmpeg); });
Check("Settings preserve independent audio devices", () => { var s = store.Load(); s.Recording.SystemAudioDeviceId = "render:1"; s.Recording.MicrophoneDeviceId = "capture:2"; store.Save(s); var loaded = store.Load(); Require(loaded.Recording.SystemAudioDeviceId == "render:1" && loaded.Recording.MicrophoneDeviceId == "capture:2"); });
Check("Malformed settings preserved and reported", () => { File.WriteAllText(settingsPath, "{broken"); var defaults = store.Load(); Require(!defaults.Recording.UseFfmpeg && store.LoadWarning is not null && File.ReadAllText(settingsPath) == "{broken"); });
Check("Null settings do not crash startup", () => { File.WriteAllText(settingsPath, "null"); Require(store.Load() is not null && store.LoadWarning is not null); });
Check("Invalid persisted values recover to defaults", () => { File.WriteAllText(settingsPath, "{\"Theme\":\"Invisible\"}"); Require(store.Load().Theme == "Dark"); });
Check("Save failure preserves previous contents", () => { var s = new AppSettings(); store.Save(s); var before = File.ReadAllText(settingsPath); s.Recording.FrameRate = 900; Reject(() => store.Save(s)); Require(File.ReadAllText(settingsPath) == before); });
Check("Settings drafts are independent", () => { var s = new AppSettings(); var copy = SettingsStore.Clone(s); copy.Recording.UseFfmpeg = true; Require(!s.Recording.UseFfmpeg); });
Check("No temporary settings files remain", () => Require(Directory.GetFiles(testRoot, "*.tmp").Length == 0));
foreach (var invalid in new[] { "../escape", "sub/name", "{unknown}", "NUL", "CON.txt", "COM1", "...", "a?b" })
    Check("Reject unsafe filename " + invalid, () => Reject(() => OutputPaths.ValidatePattern(invalid)));
Check("Output filenames are deterministic", () => Require(Path.GetFileName(OutputPaths.Next(testRoot, "Clip_{date}_{time}", ".png", new DateTime(2026, 10, 26, 12, 30, 14))) == "Clip_2026-10-26_12-30-14.png"));
Check("Output collisions preserve existing file", () => { var first = OutputPaths.Next(testRoot, "shot", ".png"); File.WriteAllText(first, "keep"); Require(OutputPaths.Next(testRoot, "shot", ".png") != first && File.ReadAllText(first) == "keep"); });
Check("Negative monitor origins remain physical pixels", () => { var bounds = new PixelRect(-1920, -1080, 1920, 1080); Require(bounds.Right == 0 && bounds.Bottom == 0); });
foreach (var fps in new[] { 0, -1, 121 }) Check("Reject invalid frame rate " + fps, () => Reject(() => new RecordingSettings { FrameRate = fps }.Validate()));
Check("Backend selector never constructs FFmpeg for native", () => { var native = new FakeBackend("native"); var chosen = RecordingBackendSelector.Create(new(), () => native, () => throw new Exception("FFmpeg was created")); Require(ReferenceEquals(native, chosen)); });
Check("Backend selector honors FFmpeg preference", () => { var ffmpeg = new FakeBackend("ffmpeg"); Require(ReferenceEquals(ffmpeg, RecordingBackendSelector.Create(new() { UseFfmpeg = true }, () => throw new Exception("Native was created"), () => ffmpeg))); });
var target = new CaptureTarget(CaptureKind.Region, new(-640, 0, 639, 359));
Check("FFmpeg uses physical negative desktop coordinates", () => { var args = FfmpegArguments.Record(new("unused", target, new()), "file with spaces.mp4"); Require(args.Contains("-640") && args.Contains("639x359") && args[^1] == "file with spaces.mp4"); });
Check("FFmpeg audio uses explicitly selected input", () => { var args = FfmpegArguments.Record(new("unused", target, new() { FfmpegAudioDevice = "Microphone (USB)" }), "out.mp4"); Require(args.Contains("audio=Microphone (USB)")); });
Check("FFmpeg native audio toggle cannot silently pick a device", () => { var args = FfmpegArguments.Record(new("unused", target, new() { SystemAudio = true, Microphone = true }), "out.mp4"); Require(args.Contains("-an") && !args.Contains("dshow")); });
Check("Advanced encoding arguments accepted", () => Require(FfmpegArguments.ParseAdvanced("-crf 20 -preset slow").Count == 4));
Check("Advanced arguments cannot replace output", () => Reject(() => FfmpegArguments.ParseAdvanced("-i secret.txt")));
Check("Advanced arguments cannot add network output", () => Reject(() => FfmpegArguments.ParseAdvanced("https://example.com")));
Check("Unknown codec rejected before process launch", () => Reject(() => FfmpegArguments.Record(new("unused", target, new() { FfmpegVideoCodec = "unknown" }), "out.mp4")));
Check("Clip processor independent of recording selection", () => { var args = ClipProcessor.BuildArguments("native.mp4", 1, 2, ClipExportMode.ExactMp4); Require(args.Contains("native.mp4") && args.Contains("libx264")); });
Check("Lossless trim uses stream copy", () => Require(ClipProcessor.BuildArguments("in.mp4", 1, 4, ClipExportMode.LosslessTrim).Contains("copy")));
Check("Reversed trim rejected", () => Reject(() => ClipProcessor.BuildArguments("in.mp4", 5, 1, ClipExportMode.ExactMp4)));
Check("Nonfinite trim rejected", () => Reject(() => ClipProcessor.BuildArguments("in.mp4", double.NaN, 2, ClipExportMode.ExactMp4)));
Check("Explicit missing FFmpeg path does not silently choose PATH", () => Require(FfmpegLocator.Find(Path.Combine(testRoot, "missing.exe")) is null));
Check("Native defaults need no FFmpeg executable", () => { var s = new RecordingSettings { FfmpegPath = "missing.exe" }; s.Validate(); Require(!s.UseFfmpeg); });

if (args.Contains("--ffmpeg-integration"))
{
    async Task Live(string name, Func<Task> action) { count++; try { await action(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    var exe = await FfmpegLocator.VerifyAsync("");
    var source = Path.Combine(testRoot, "source.mp4");
    await Live("FFmpeg synthetic clip generation", async () => { var result = await ProcessRunner.RunAsync(exe, ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]); Require(result.ExitCode == 0); });
    foreach (var mode in Enum.GetValues<ClipExportMode>())
        await Live("Clip export " + mode, async () => { var output = Path.Combine(testRoot, mode + (mode == ClipExportMode.WebM ? ".webm" : ".mp4")); await new ClipProcessor().ExportAsync(source, output, .5, 2.2, mode, exe); var decoded = await ProcessRunner.RunAsync(exe, ["-v", "error", "-i", output, "-f", "null", "-"]); Require(decoded.ExitCode == 0 && new FileInfo(output).Length > 1000); });
    await Live("FFmpeg live region start stop and decode", async () => { var output = Path.Combine(testRoot, "desktop.mp4"); await using var recorder = new FfmpegRecordingBackend(); await recorder.StartAsync(new(output, new(CaptureKind.Region, new(100, 100, 640, 360)), new() { UseFfmpeg = true, FrameRate = 30, FfmpegPath = exe })); await Task.Delay(1300); await recorder.StopAsync(); var result = await ProcessRunner.RunAsync(exe, ["-v", "error", "-i", output, "-f", "null", "-"]); Require(result.ExitCode == 0); });
    await Live("FFmpeg cancellation removes incomplete output", async () => { var output = Path.Combine(testRoot, "cancel.mp4"); await using var recorder = new FfmpegRecordingBackend(); await recorder.StartAsync(new(output, new(CaptureKind.Region, new(0, 0, 320, 240)), new() { UseFfmpeg = true, FrameRate = 30, FfmpegPath = exe })); await recorder.CancelAsync(); Require(!File.Exists(output) && Directory.GetFiles(testRoot, "*.partial.mp4").Length == 0); });
}

Console.WriteLine($"{count - failures} passed; {failures} failed.");
Directory.Delete(testRoot, true);
return failures == 0 ? 0 : 1;

sealed class FakeBackend(string name) : IRecordingBackend
{
    public string Name => name;
    public bool CanPause => true;
    public event Action<string>? StatusChanged { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public event Action? Completed { add { } remove { } }
    public Task StartAsync(RecordingRequest request, CancellationToken ct = default) => Task.CompletedTask;
    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task CancelAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
