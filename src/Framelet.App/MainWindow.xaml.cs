using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Framelet.App.Capture;
using Framelet.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Framelet.App;

public partial class MainWindow : Window
{
    private readonly App app = (App)Application.Current;
    private readonly ClipView clips = new();
    private HotkeyManager? hotkeys;
    private Forms.NotifyIcon? tray;
    private RecordingWindow? recording;
    private bool capturing, quitting;
    private string? currentPath;

    public MainWindow()
    {
        InitializeComponent(); ClipHost.Content = clips;
        Editor.DocumentChanged += (_, _) => { CopyButton.IsEnabled = SaveButton.IsEnabled = Editor.HasImage; Title = "Framelet" + (Editor.HasImage ? " · " + (Editor.SourcePath is null ? "Untitled image" : Path.GetFileName(Editor.SourcePath)) + (Editor.IsDirty ? " *" : "") : ""); };
        Editor.CopyRequested += (_, _) => Copy(); Editor.SaveRequested += (_, _) => Save(); Editor.PasteRequested += (_, _) => Paste(); Editor.FileOpenRequested += OpenFile;
        Loaded += (_, _) => { hotkeys = new HotkeyManager(this); Configure(); CreateTray(); RefreshRecent(); };
        Closing += OnClosing;
    }

    private void Configure()
    {
        app.ApplyTheme(app.Settings.Theme); Editor.ApplyDefaults(app.Settings.EditorColor, app.Settings.EditorStroke);
        CaptureHint.Text = app.Settings.RegionHotkey.Replace("+", " + ");
        BackendHint.Text = (app.Settings.Recording.UseFfmpeg ? "FFmpeg" : "Native recording") + " · " + app.Settings.Recording.FrameRate + " FPS · MP4";
        hotkeys?.UnregisterAll();
        List<string> issues = [];
        void Register(string id, string gesture, Action action) { if (string.IsNullOrWhiteSpace(gesture)) return; var result = hotkeys?.Register(id, gesture, action); if (result is not null) issues.Add(result); }
        Register("region", app.Settings.RegionHotkey, async () => await Capture("region"));
        Register("window", app.Settings.WindowHotkey, async () => await Capture("active"));
        Register("monitor", app.Settings.MonitorHotkey, async () => await Capture("monitor"));
        Register("record", app.Settings.RecordHotkey, async () => { if (recording is not null) await recording.Stop(); else await SetupRecording("region"); });
        Status.Text = issues.Count == 0 ? "Ready" : string.Join("  ", issues);
    }

    private void CreateTray()
    {
        tray = new Forms.NotifyIcon { Text = "Framelet", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Framelet", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("Capture region", null, (_, _) => Dispatcher.InvokeAsync(async () => await Capture("region")));
        menu.Items.Add("Capture active window", null, (_, _) => Dispatcher.InvokeAsync(async () => await Capture("active")));
        menu.Items.Add("Record region", null, (_, _) => Dispatcher.InvokeAsync(async () => await SetupRecording("region")));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => SettingsClick(this, new RoutedEventArgs())));
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit Framelet", null, (_, _) => Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
    }

    private void ShowMain() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void ShowPage(string page) { HomeView.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed; Editor.Visibility = page == "editor" ? Visibility.Visible : Visibility.Collapsed; ClipHost.Visibility = page == "clips" ? Visibility.Visible : Visibility.Collapsed; }

    private async Task Capture(string mode)
    {
        if (capturing || recording is not null) { Status.Text = "Finish the current recording before taking a screenshot."; return; }
        CaptureTarget? active = mode == "active" ? CaptureService.GetActiveWindow() : null;
        if (!CanReplaceImage()) return;
        capturing = true;
        var wasVisible = IsVisible;
        try
        {
            Hide(); await Task.Delay(180 + app.Settings.CaptureDelaySeconds * 1000);
            var target = mode switch
            {
                "region" => await CaptureService.SelectRegionAsync(),
                "window" => await CaptureService.SelectWindowAsync(),
                "active" => active,
                "monitor" => CaptureService.GetCurrentMonitor(),
                _ => CaptureService.GetDesktop()
            };
            if (target is null) return;
            var image = CaptureService.Capture(target);
            currentPath = null;
            Editor.LoadImage(image); Editor.ApplyDefaults(app.Settings.EditorColor, app.Settings.EditorStroke);
            List<string> actionErrors = [];
            if (app.Settings.CopyAfterCapture)
            {
                try { await CopyToClipboard(image); }
                catch (Exception ex) { actionErrors.Add("Copy failed: " + ex.Message); }
            }
            if (app.Settings.SaveAfterCapture)
            {
                try
                {
                    var savedPath = OutputPaths.Next(app.Settings.OutputDirectory, app.Settings.FilenamePattern, ".png");
                    WriteImage(image, savedPath); currentPath = savedPath; Editor.MarkSaved(savedPath);
                }
                catch (Exception ex) { actionErrors.Add("Auto-save failed: " + ex.Message); }
            }
            Status.Text = $"Captured {image.PixelWidth} × {image.PixelHeight}" + (currentPath is null ? "" : " · Saved " + Path.GetFileName(currentPath));
            if (actionErrors.Count > 0) Status.Text = "Your capture is open in the editor. " + string.Join("  ", actionErrors);
            if (app.Settings.OpenEditorAfterCapture || actionErrors.Count > 0) { ShowMain(); ShowPage("editor"); }
            else { tray?.ShowBalloonTip(2000, "Framelet", Status.Text, Forms.ToolTipIcon.Info); }
            RefreshRecent();
        }
        catch (Exception ex) { ShowMain(); ShowError(ex); }
        finally { capturing = false; if (wasVisible && !IsVisible) ShowMain(); }
    }

    public void OpenFile(string path)
    {
        if (!CanReplaceImage()) return;
        try
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze();
            Editor.LoadImage(image, path); currentPath = path; Editor.ApplyDefaults(app.Settings.EditorColor, app.Settings.EditorStroke); ShowMain(); ShowPage("editor"); Status.Text = Path.GetFileName(path) + $" · {image.PixelWidth} × {image.PixelHeight}";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private bool CanReplaceImage()
    {
        if (!Editor.IsDirty) return true;
        var answer = MessageBox.Show(this, "Save the current image before continuing?", "Unsaved image", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && Save();
    }

    private async void Paste()
    {
        if (!CanReplaceImage()) return;
        try
        {
            var image = Clipboard.GetImage();
            if (image is null) { Status.Text = "The clipboard does not contain an image."; return; }
            currentPath = null; Editor.LoadImage(image); Editor.ApplyDefaults(app.Settings.EditorColor, app.Settings.EditorStroke); ShowPage("editor"); Status.Text = "Pasted image";
        }
        catch (Exception ex) { ShowError(ex); }
        await Task.CompletedTask;
    }
    private async void Copy()
    {
        if (!Editor.HasImage) return;
        try { await CopyToClipboard(Editor.ExportImage()); Status.Text = "Image copied to the clipboard"; }
        catch (Exception ex) { ShowError(ex); }
    }
    private static async Task CopyToClipboard(BitmapSource image)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(image); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 3) { await Task.Delay(75); }
        }
    }

    private bool Save()
    {
        if (!Editor.HasImage) return false;
        try
        {
            var now = DateTime.Now;
            var suggested = app.Settings.FilenamePattern.Replace("{date}", now.ToString("yyyy-MM-dd")).Replace("{time}", now.ToString("HH-mm-ss")) + ".png";
            var initialFolder = Directory.Exists(app.Settings.OutputDirectory) ? app.Settings.OutputDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var dialog = new SaveFileDialog { Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp", FileName = suggested, InitialDirectory = initialFolder, AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return false;
            WriteImage(Editor.ExportImage(), dialog.FileName, true); currentPath = dialog.FileName; Editor.MarkSaved(currentPath); Status.Text = "Saved " + currentPath; RefreshRecent(); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    private static void WriteImage(BitmapSource image, string path, bool overwrite = false)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg")
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); drawing.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); }
            var flattened = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); flattened.Render(visual); image = flattened;
        }
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 }, ".bmp" => new BmpBitmapEncoder(), _ => new PngBitmapEncoder() };
        encoder.Frames.Add(BitmapFrame.Create(image));
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try { using (var stream = File.Create(temporary)) encoder.Save(stream); File.Move(temporary, path, overwrite); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task SetupRecording(string mode)
    {
        if (recording is not null) { recording.Activate(); return; }
        if (capturing) return;
        capturing = true; var visible = IsVisible;
        try
        {
            Hide(); await Task.Delay(180);
            var target = mode switch { "window" => await CaptureService.SelectWindowAsync(), "monitor" => CaptureService.GetCurrentMonitor(), _ => await CaptureService.SelectRegionAsync() };
            CaptureService.DiscardSelectionSnapshot();
            if (target is null) return;
            var output = OutputPaths.Next(app.Settings.OutputDirectory, app.Settings.FilenamePattern, ".mp4");
            recording = new RecordingWindow(new RecordingRequest(output, target, SettingsStore.Clone(app.Settings).Recording));
            recording.ClipSaved += file => { ShowMain(); ShowPage("clips"); clips.LoadClip(file); RefreshRecent(); Status.Text = "Saved " + file; };
            recording.Closed += (_, _) => { recording = null; if (visible) ShowMain(); };
            recording.Show();
        }
        catch (Exception ex) { ShowMain(); ShowError(ex); }
        finally { capturing = false; if (recording is null && visible && !IsVisible) ShowMain(); }
    }

    private void RefreshRecent()
    {
        try
        {
            var files = Directory.Exists(app.Settings.OutputDirectory) ? Directory.EnumerateFiles(app.Settings.OutputDirectory).Where(x => new[] { ".png", ".jpg", ".bmp", ".mp4", ".webm" }.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderByDescending(File.GetLastWriteTimeUtc).Take(12).ToArray() : [];
            RecentList.Items.Clear(); foreach (var file in files)
            {
                var row = new DockPanel(); var info = Ui.Text(File.GetLastWriteTime(file).ToString("MMM d · h:mm tt"), 12, true); DockPanel.SetDock(info, Dock.Right); row.Children.Add(info); row.Children.Add(Ui.Text((Path.GetExtension(file) == ".mp4" ? "▷   " : "▧   ") + Path.GetFileName(file)));
                RecentList.Items.Add(new ListBoxItem { Content = row, Tag = file });
            }
            EmptyRecent.Visibility = files.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (IOException ex) { Status.Text = "Recent files could not be read: " + ex.Message; }
    }
    private void RecentOpen(object sender, MouseButtonEventArgs e) { if (RecentList.SelectedItem is ListBoxItem { Tag: string path }) { if (Path.GetExtension(path) is ".mp4" or ".webm") { ShowPage("clips"); clips.LoadClip(path); } else OpenFile(path); } }
    private void ShowError(Exception ex) { Status.Text = ex.Message; MessageBox.Show(this, ex.Message, "Framelet", MessageBoxButton.OK, MessageBoxImage.Error); }
    private void CaptureMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); foreach (var (label, key) in new[] { ("Region", "region"), ("Choose a window", "window"), ("Monitor under pointer", "monitor"), ("All displays", "desktop") }) { var item = new MenuItem { Header = label }; item.Click += async (_, _) => await Capture(key); menu.Items.Add(item); } menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void RecordClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); foreach (var (label, key) in new[] { ("Record a region", "region"), ("Record a window", "window"), ("Record monitor under pointer", "monitor") }) { var item = new MenuItem { Header = label }; item.Click += async (_, _) => await SetupRecording(key); menu.Items.Add(item); }
        var settings = new MenuItem { Header = "Recording settings…" }; settings.Click += (_, _) => ShowSettings("Recording"); menu.Items.Add(new Separator()); menu.Items.Add(settings); menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void RegionClick(object sender, RoutedEventArgs e) => _ = Capture("region");
    private void OpenClick(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" }; if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName); }
    private void PasteClick(object sender, RoutedEventArgs e) => Paste();
    private void CopyClick(object sender, RoutedEventArgs e) => Copy();
    private void SaveClick(object sender, RoutedEventArgs e) => Save();
    private void HomeClick(object sender, RoutedEventArgs e) { ShowPage("home"); RefreshRecent(); }
    private void EditorClick(object sender, RoutedEventArgs e) => ShowPage("editor");
    private void ClipsClick(object sender, RoutedEventArgs e) => ShowPage("clips");
    private void SettingsClick(object sender, RoutedEventArgs e) => ShowSettings("General");
    private void ShowSettings(string category) { var window = new SettingsWindow(app.Settings, category) { Owner = this }; if (window.ShowDialog() == true) { app.Settings = window.Result; Configure(); RefreshRecent(); } }
    private void FolderClick(object sender, RoutedEventArgs e) { try { Directory.CreateDirectory(app.Settings.OutputDirectory); Process.Start(new ProcessStartInfo(app.Settings.OutputDirectory) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } }
    private void OnDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenFile(files[0]); }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.O) { OpenClick(this, e); e.Handled = true; }
        else if (e.Key == Key.S) { Save(); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.FocusedElement is not TextBox) { Paste(); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.FocusedElement is not TextBox) { Copy(); e.Handled = true; }
    }
    private void Quit() { quitting = true; Close(); quitting = false; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!quitting && app.Settings.MinimizeToTray) { e.Cancel = true; Hide(); return; }
        if (recording is not null) { e.Cancel = true; recording.Activate(); Status.Text = "Stop or cancel the recording before quitting."; return; }
        if (clips.IsExporting) { e.Cancel = true; ShowPage("clips"); Status.Text = "Wait for the export or cancel it before quitting."; return; }
        if (!CanReplaceImage()) { e.Cancel = true; return; }
        tray?.Dispose(); hotkeys?.Dispose(); app.Shutdown();
    }
}
