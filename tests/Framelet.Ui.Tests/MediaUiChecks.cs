using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Framelet.App;
using Framelet.Core;

internal static class MediaUiChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Clip loading and codec failure keep source and entered trim values", () => WithClip(view =>
        {
            Require(!Field<Button>(view, "play").IsEnabled && !Field<Button>(view, "export").IsEnabled,
                "Empty clip actions should be unavailable.");
            Field<Slider>(view, "timeline").Maximum = 90;
            view.LoadClip(Path.Combine(Path.GetTempPath(), "framelet-ui-codec-fixture.webm"));
            Require(Field<Slider>(view, "timeline").Maximum == 0 && !Field<Button>(view, "export").IsEnabled,
                "Loading a new source retained the previous timeline or enabled export prematurely.");
            Field<TextBox>(view, "start").Text = "1.250";
            Field<TextBox>(view, "end").Text = "5.500";
            Invoke(view, "MediaFailed", Field<MediaElement>(view, "player"), new InvalidOperationException("Unavailable test codec"));
            Require(!Field<Button>(view, "play").IsEnabled && Field<Button>(view, "export").IsEnabled,
                "A codec failure should disable playback but retain manual export.");
            Require(Field<TextBox>(view, "start").Text == "1.250" && Field<TextBox>(view, "end").Text == "5.500",
                "Codec failure discarded trim points.");
            Require(Field<string>(view, "path").EndsWith("framelet-ui-codec-fixture.webm", StringComparison.Ordinal),
                "Codec failure discarded the source.");
        }));

        check("Late media notifications cannot alter a replacement source or its entered trim values", () => WithClip(view =>
        {
            view.LoadClip(Path.Combine(Path.GetTempPath(), "framelet-ui-previous.mp4"));
            var previous = Field<MediaElement>(view, "player");
            var currentPath = Path.Combine(Path.GetTempPath(), "framelet-ui-current.mp4");
            view.LoadClip(currentPath);
            var current = Field<MediaElement>(view, "player");
            Require(!ReferenceEquals(previous, current) && previous.Source is null,
                "Replacing a clip did not release and separate the old media element.");
            Field<TextBox>(view, "start").Text = "2.250"; Field<TextBox>(view, "end").Text = "6.500";
            var loadingStatus = Field<TextBlock>(view, "status").Text;
            Invoke(view, "MediaOpened", previous);
            Invoke(view, "MediaFailed", previous, new InvalidOperationException("Late previous failure"));
            Invoke(view, "MediaEnded", previous);
            Require(Field<bool>(view, "loading") && Field<string>(view, "path") == currentPath && Field<TextBlock>(view, "status").Text == loadingStatus,
                "An old callback settled or replaced the current load.");
            Invoke(view, "MediaFailed", current, new InvalidOperationException("Current unavailable codec"));
            var currentStatus = Field<TextBlock>(view, "status").Text;
            Invoke(view, "MediaOpened", previous); Invoke(view, "MediaEnded", previous);
            Require(Field<TextBox>(view, "start").Text == "2.250" && Field<TextBox>(view, "end").Text == "6.500" &&
                Field<TextBlock>(view, "status").Text == currentStatus && Field<Button>(view, "export").IsEnabled,
                "A stale callback lost trim values or the current manual-export state.");
        }));

        check("Clip ranges allow unavailable preview duration but reject nonfinite and out-of-bounds input", () => WithClip(view =>
        {
            var start = Field<TextBox>(view, "start"); var end = Field<TextBox>(view, "end");
            start.Text = "1.250"; end.Text = "5.500";
            Require(RangeValid(view), "A finite manual range needs no Windows preview duration.");
            foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity", "-1", "1.250", "invalid" })
            {
                end.Text = invalid; Require(!RangeValid(view), "Invalid out point was accepted: " + invalid);
            }
            end.Text = "5.500"; start.Text = "NaN"; Require(!RangeValid(view), "NaN in point was accepted.");
            start.Text = "1.250"; SetField(view, "duration", 4d); Require(!RangeValid(view), "Known clip duration was ignored.");
            end.Text = "4.000"; Require(RangeValid(view), "A valid range within the known duration failed.");
        }));

        check("Clip export reserves source before its chooser and stays locked until cancellation settles", () => WithClip(view =>
        {
            var source = Path.Combine(Path.GetTempPath(), "framelet-ui-export-fixture.mp4");
            view.LoadClip(source); Invoke(view, "MediaFailed", Field<MediaElement>(view, "player"), new InvalidOperationException("test"));
            Field<TextBox>(view, "start").Text = "2.000"; Field<TextBox>(view, "end").Text = "8.000";
            var cancellation = (CancellationTokenSource)Invoke(view, "BeginExport")!;
            Require(cancellation is not null && view.IsExporting, "Export did not reserve its source before opening the output chooser.");
            foreach (var name in new[] { "open", "export", "play", "setIn", "setOut" })
                Require(!Field<Button>(view, name).IsEnabled, "Export left an action enabled: " + name);
            Require(!Field<TextBox>(view, "start").IsEnabled && !Field<ComboBox>(view, "mode").IsEnabled,
                "Export left its input values editable.");
            view.LoadClip(Path.Combine(Path.GetTempPath(), "replacement.mp4"));
            Require(Field<string>(view, "path") == source, "An active export allowed source replacement.");
            Invoke(view, "CancelExport");
            Require(cancellation!.IsCancellationRequested && !Field<Button>(view, "cancelExport").IsEnabled && !Field<Button>(view, "export").IsEnabled,
                "Cancellation released controls before the export settled.");
            Invoke(view, "EndExport", cancellation);
            Require(Field<Button>(view, "export").IsEnabled && Field<TextBox>(view, "start").Text == "2.000" && Field<TextBox>(view, "end").Text == "8.000",
                "Settling an export lost trim points or did not restore controls.");
        }));

        check("Playback failures preserve active export progress and its completed result", () => WithClip(view =>
        {
            view.LoadClip(Path.Combine(Path.GetTempPath(), "framelet-ui-export-status.mp4"));
            var player = Field<MediaElement>(view, "player");
            Invoke(view, "MediaFailed", player, new InvalidOperationException("Initial unavailable codec"));
            Field<TextBox>(view, "start").Text = "1.000"; Field<TextBox>(view, "end").Text = "3.000";
            var cancellation = (CancellationTokenSource)Invoke(view, "BeginExport")!;
            var status = Field<TextBlock>(view, "status");
            status.Text = "Exporting fixture…";
            Invoke(view, "MediaFailed", player, new InvalidOperationException("Late preview failure during export"));
            Require(status.Text == "Exporting fixture…" && view.IsExporting, "Playback failure overwrote export progress.");
            Invoke(view, "EndExport", cancellation); status.Text = "Saved exported-fixture.mp4";
            Invoke(view, "MediaFailed", player, new InvalidOperationException("Late preview failure after export"));
            Require(status.Text == "Saved exported-fixture.mp4" && Field<TextBox>(view, "end").Text == "3.000" &&
                Field<StackPanel>(view, "previewMessage").Visibility == Visibility.Visible,
                "Playback failure hid the export result, lost input, or failed to show a preview explanation.");
        }));

        check("Missing FFmpeg is explained before a save dialog without losing trim input", () => WithClip(view =>
        {
            var app = (App)Application.Current;
            var original = app.Settings.Recording.FfmpegPath;
            try
            {
                app.Settings.Recording.FfmpegPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ffmpeg.exe");
                view.LoadClip(Path.Combine(Path.GetTempPath(), "framelet-ui-missing-ffmpeg.mp4"));
                Invoke(view, "MediaFailed", Field<MediaElement>(view, "player"), new InvalidOperationException("test"));
                Field<TextBox>(view, "start").Text = "0.000"; Field<TextBox>(view, "end").Text = "4.000";
                ((Task)Invoke(view, "Export")!).GetAwaiter().GetResult();
                Require(Field<TextBlock>(view, "status").Text.Contains("Settings → Recording", StringComparison.Ordinal),
                    "Missing FFmpeg did not provide a relevant next step.");
                Require(!view.IsExporting && Field<Button>(view, "export").IsEnabled && Field<TextBox>(view, "end").Text == "4.000",
                    "Missing dependency lost input or left the view busy.");
            }
            finally { app.Settings.Recording.FfmpegPath = original; }
        }));

        check("Recording recovery keeps a selectable path and visible save/close actions at minimum size", () =>
        {
            var file = Path.Combine(Path.GetTempPath(), "Framelet-ui-recovery-" + Guid.NewGuid().ToString("N") + ".mp4");
            File.WriteAllText(file, "completed fixture; not media validation");
            var window = new RecordingWindow(new RecordingRequest(file, new(CaptureKind.Region, new(-640, 0, 640, 480)), new()));
            try
            {
                var controller = Field<RecordingSessionController>(window, "session");
                // Inject a settled snapshot only: this test exercises layout and closing, not recording media.
                SetField(controller, "_state", RecordingSessionState.Recoverable);
                SetField(controller, "_recoveryPath", file);
                SetField(controller, "_status", string.Join(" ", Enumerable.Repeat("A long publication failure needs another output location.", 30)));
                Invoke(window, "Refresh");
                var root = (FrameworkElement)window.Content;
                var available = new Size(window.MinWidth - 32, window.MinHeight - 42);
                root.Measure(available); root.Arrange(new Rect(available)); root.UpdateLayout();
                foreach (var name in new[] { "recover", "cancel" })
                {
                    var button = Field<Button>(window, name);
                    var origin = button.TranslatePoint(new Point(), root);
                    Require(button.IsEnabled && button.Visibility == Visibility.Visible && origin.Y >= 0 && origin.Y + button.ActualHeight <= root.ActualHeight + .1,
                        "Long recovery details displaced an essential action: " + name);
                }
                var path = Field<TextBox>(window, "recoveryPath");
                Require(path.IsReadOnly && path.Focusable && path.Text == file, "Recovery path is not selectable or accurate.");
                var details = ((Grid)root).Children.OfType<ScrollViewer>().Single();
                var viewport = details.Template.FindName("PART_ScrollContentPresenter", details) as FrameworkElement
                    ?? throw new InvalidOperationException("Recording details have no scroll viewport.");
                var visiblePath = new Rect(path.TranslatePoint(new Point(), viewport), path.RenderSize);
                visiblePath.Intersect(new Rect(viewport.RenderSize));
                Require(!visiblePath.IsEmpty && visiblePath.Width > 0 && visiblePath.Height >= Math.Min(path.ActualHeight, path.FontSize),
                    "The recovery path is buried below diagnostics instead of visibly intersecting the details viewport.");
                Require(Field<Panel>(window, "nativeAudio").Visibility == Visibility.Collapsed,
                    "Noneditable recording options are taking space from recovery details.");
                window.Close(); Require(File.ReadAllText(file).StartsWith("completed fixture", StringComparison.Ordinal), "Closing recovery discarded completed media.");
            }
            finally { Field<DispatcherTimer>(window, "timer").Stop(); window.Close(); File.Delete(file); }
        });
    }

    private static void WithClip(Action<ClipView> test)
    {
        var view = new ClipView();
        try { test(view); }
        finally
        {
            Invoke(view, "EndExport", Field<CancellationTokenSource?>(view, "exporting"));
            Field<DispatcherTimer>(view, "ticker").Stop(); Field<MediaElement>(view, "player").Close();
        }
    }
    private static bool RangeValid(ClipView view) => (bool)Invoke(view, "TryGetTrimRange", 0d, 0d)!;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
