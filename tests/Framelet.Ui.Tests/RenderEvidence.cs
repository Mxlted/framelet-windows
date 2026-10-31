using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet.App;
using Framelet.App.Editor;
using Framelet.Core;

/// <summary>
/// Offscreen production-control evidence, with no HWND, input, capture, recorder or settings save.
/// Reflection is confined to this diagnostic utility to arrange fixture state without public test hooks.
/// Output DPI changes raster density; it does not emulate a physical monitor or establish live DPI behavior.
/// </summary>
internal static class RenderEvidence
{
    private sealed record ImageRecord(string File, string Scenario, string Theme, double WidthDip, double HeightDip,
        int Dpi, int WidthPixels, int HeightPixels);

    internal static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] != "--render" || args[2] != "--sample")
            throw new ArgumentException("Use --render <fresh-directory> --sample <existing-png>.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Evidence output must be a fresh or empty directory.");
        BitmapSource sample;
        using (var stream = File.OpenRead(Path.GetFullPath(args[3])))
        {
            sample = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            sample.Freeze();
        }
        Directory.CreateDirectory(output);
        var images = new List<ImageRecord>();
        var app = (App)Application.Current;
        var settings = app.Settings;
        // This is an in-memory fixture. Store.Load/Save and application OnStartup are never called.
        app.Settings = new AppSettings { OutputDirectory = @"C:\Pictures\Framelet", MinimizeToTray = false };
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                app.ApplyTheme(theme);
                var shell = new MainWindow();
                try
                {
                    ((TextBlock)shell.FindName("OutputFolder")).Text = app.Settings.OutputDirectory;
                    Render(shell, new Size(1224, 781), 120, $"static-home-{theme.ToLowerInvariant()}.png", "Home, empty recent-files fixture", theme);
                    Invoke(shell, "ShowPage", "editor");
                    Render(shell, new Size(1224, 781), 120, $"static-editor-empty-{theme.ToLowerInvariant()}.png", "Empty image editor", theme);
                    var editor = (ImageEditorView)shell.FindName("Editor");
                    editor.LoadImage(sample, "sample.png");
                    Render(shell, new Size(1224, 781), 120, $"static-editor-{theme.ToLowerInvariant()}.png", "Unannotated baseline sample", theme);

                    // The minimum series uses the same layout, with text properties visible at every output density.
                    Invoke(editor, "SelectTool", EditorTool.Text);
                    ((TextBox)editor.FindName("AnnotationTextBox")).Text = "A clear annotation";
                    foreach (int dpi in new[] { 96, 120, 144, 192 })
                        Render(shell, new Size(984, 621), dpi, $"static-editor-minimum-{theme.ToLowerInvariant()}-{dpi}dpi.png",
                            "Minimum client layout, Text tool defaults", theme);

                    Invoke(editor, "SelectTool", EditorTool.Select);
                    var surface = (FrameworkElement)editor.FindName("Surface");
                    var document = (ImageDocument)surface.GetType().GetProperty("Document")!.GetValue(surface)!;
                    var scaleX = sample.PixelWidth / 1120d;
                    var scaleY = sample.PixelHeight / 700d;
                    Point At(double x, double y) => new(x * scaleX, y * scaleY);
                    document.Add(new Annotation { Tool = EditorTool.Rectangle, Start = At(785, 360), End = At(1092, 446), StrokeWidth = 4, Color = Color.FromRgb(233, 181, 102) });
                    document.Add(new Annotation { Tool = EditorTool.Arrow, Start = At(645, 556), End = At(800, 436), StrokeWidth = 5 });
                    var text = new Annotation { Tool = EditorTool.Text, Start = At(480, 570), End = At(730, 615), Text = "Ready to share", FontSize = 26 * scaleY };
                    document.Add(text);
                    surface.GetType().GetMethod("Select")!.Invoke(surface, new object?[] { text.Id });
                    Render(shell, new Size(1224, 781), 120, $"static-editor-annotated-{theme.ToLowerInvariant()}.png", "Generated annotations on the baseline sample; selected text fixture", theme);
                }
                finally { CloseShell(shell); }
            }

            app.ApplyTheme("Dark");
            var general = new SettingsWindow(app.Settings, _ => throw new InvalidOperationException("Evidence must not save settings."), listNativeAudioDevices: NativeAudioFixture.List);
            try { Render(general, new Size(814, 671), 120, "static-settings-general-dark.png", "Settings, General draft", "Dark"); }
            finally { general.Close(); }

            app.ApplyTheme("Light");
            var recordingSettings = new SettingsWindow(app.Settings, _ => throw new InvalidOperationException("Evidence must not save settings."), "Recording", NativeAudioFixture.List);
            try { Render(recordingSettings, new Size(724, 561), 120, "static-settings-recording-light.png", "Native recording settings with fixture audio devices at minimum client layout", "Light"); }
            finally { recordingSettings.Close(); }

            RenderClip("Dark", failed: false);
            RenderClip("Light", failed: true);
            RenderRecording("Dark", recoverable: false);
            RenderRecording("Light", recoverable: true);

            var lines = new List<string>
            {
                "# Static WPF evidence", "",
                "These PNGs render the production controls offscreen with RenderTargetBitmap. No window was shown; no keyboard/mouse input, capture, recording, export or persisted-settings change occurred. Native title bars are excluded.", "",
                "The supplied sample PNG is used unchanged in the plain editor views. Annotated views add generated editable annotations. Home recent files are empty fixture data. Settings audio devices use fixture IDs and names without enumerating hardware. Clip codec failure and recording recovery are injected UI states, not real media/codec/recovery observations.", "",
                "96/120/144/192 DPI specify output raster density (100/125/150/200 percent). DIPs stay fixed. These files do not establish real monitor-DPI, mixed-DPI, keyboard, screen-reader or live-interaction behavior.", "",
                "| File | Scenario | Theme | Content DIPs | Output DPI | Pixels |", "| --- | --- | --- | --- | --- | --- |"
            };
            lines.AddRange(images.Select(i => $"| {i.File} | {i.Scenario} | {i.Theme} | {i.WidthDip.ToString(CultureInfo.InvariantCulture)} × {i.HeightDip.ToString(CultureInfo.InvariantCulture)} | {i.Dpi} | {i.WidthPixels} × {i.HeightPixels} |"));
            File.WriteAllLines(Path.Combine(output, "evidence.md"), lines);
            Console.WriteLine($"Rendered {images.Count} static production-control images to {output}. See evidence.md for fixture and DPI limits.");
            return 0;
        }
        finally { app.Settings = settings; app.ApplyTheme("Dark"); }

        void Render(Window window, Size size, int dpi, string file, string scenario, string theme)
        {
            if (window.IsVisible) throw new InvalidOperationException("Static evidence must never show a window.");
            var content = (FrameworkElement)window.Content;
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            if (window is MainWindow main && ((ImageEditorView)main.FindName("Editor")) is { HasImage: true, Visibility: Visibility.Visible } editor)
            {
                Invoke(editor, "FitImage"); content.UpdateLayout();
            }
            int width = checked((int)Math.Ceiling(size.Width * dpi / 96d));
            int height = checked((int)Math.Ceiling(size.Height * dpi / 96d));
            var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var context = background.RenderOpen())
                context.DrawRectangle((Brush)app.Resources["BackgroundBrush"], null, new Rect(size));
            bitmap.Render(background); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(Path.Combine(output, file), FileMode.CreateNew, FileAccess.Write)) encoder.Save(stream);
            images.Add(new(file, scenario, theme, size.Width, size.Height, dpi, width, height));
        }

        void RenderClip(string theme, bool failed)
        {
            app.ApplyTheme(theme);
            var shell = new MainWindow();
            try
            {
                Invoke(shell, "ShowPage", "clips");
                var clip = Field<ClipView>(shell, "clips");
                if (failed)
                {
                    // No MediaElement source is opened: only the production failure presenter is exercised.
                    SetField(clip, "path", @"C:\Pictures\Framelet\meeting-demo-unavailable-codec.webm");
                    SetField(clip, "loading", true);
                    Field<TextBlock>(clip, "sourceName").Text = "meeting-demo-unavailable-codec.webm";
                    Field<TextBox>(clip, "start").Text = "1.250"; Field<TextBox>(clip, "end").Text = "5.500";
                    Invoke(clip, "MediaFailed", Field<MediaElement>(clip, "player"), new InvalidOperationException("The installed Windows codecs cannot decode this fixture."));
                }
                Render(shell, new Size(984, 621), 120, $"static-clip-{(failed ? "codec-error" : "empty")}-{theme.ToLowerInvariant()}.png",
                    failed ? "Injected unavailable-codec state, retained manual trim points" : "Empty clip view", theme);
            }
            finally { CloseShell(shell); }
        }

        void RenderRecording(string theme, bool recoverable)
        {
            app.ApplyTheme(theme);
            const string path = @"C:\Pictures\Framelet\Project review\Recorded demonstrations\A deliberately long completed recording recovery filename.mp4";
            var window = new RecordingWindow(new RecordingRequest(path, new(CaptureKind.Region, new(-640, 0, 1280, 720), Title: "Project review"), new()));
            try
            {
                if (recoverable)
                {
                    var controller = Field<RecordingSessionController>(window, "session");
                    SetField(controller, "_state", RecordingSessionState.Recoverable);
                    SetField(controller, "_recoveryPath", path);
                    SetField(controller, "_status", "The recording finished, but the destination already exists. Choose another filename to save a copy. The completed recording is preserved at the path below.");
                    Invoke(window, "Refresh");
                }
                Render(window, recoverable ? new Size(504, 261) : new Size(634, 291), 120,
                    $"static-recording-{(recoverable ? "recovery" : "ready")}-{theme.ToLowerInvariant()}.png",
                    recoverable ? "Injected recovery snapshot at minimum size; no media file created" : "Ready state; no recorder started", theme);
            }
            finally { Field<DispatcherTimer>(window, "timer").Stop(); window.Close(); }
        }
    }

    private static void CloseShell(MainWindow shell)
    {
        var clip = Field<ClipView>(shell, "clips");
        Field<DispatcherTimer>(clip, "ticker").Stop();
        Field<MediaElement>(clip, "player").Close();
        // An unshown diagnostic shell must not run the production tray/unsaved/exit workflow at cleanup.
        var closing = shell.GetType().GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(object), typeof(CancelEventArgs)], null)!;
        shell.Closing -= (CancelEventHandler)closing.CreateDelegate(typeof(CancelEventHandler), shell);
        shell.Close();
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
