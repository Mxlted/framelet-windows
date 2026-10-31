using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Framelet.Media;
using Microsoft.Win32;

namespace Framelet.App;

public sealed class ClipView : UserControl
{
    private MediaElement player = CreatePlayer();
    private readonly Grid preview = new();
    private readonly Slider timeline = new()
    {
        Minimum = 0, Maximum = 0, SmallChange = .1, LargeChange = 1, IsMoveToPointEnabled = true,
        AutoToolTipPlacement = AutoToolTipPlacement.TopLeft, AutoToolTipPrecision = 2,
        Margin = new Thickness(0, 10, 0, 6)
    };
    private readonly TextBox start = new() { Text = "0.000", Width = 88 }, end = new() { Text = "0.000", Width = 88 };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Exact MP4", "Lossless trim", "Smaller MP4", "WebM" }, SelectedIndex = 0, MinWidth = 180 };
    private readonly TextBlock sourceName = Ui.Text("No clip open", 12, true);
    private readonly TextBlock playbackState = Ui.Text("No clip", 12, true);
    private readonly TextBlock position = Ui.Text("00:00.000 / --:--.---", 12, true);
    private readonly TextBlock status = Ui.Text("Open a video to preview or trim it.", 12);
    private readonly TextBlock modeNote = Ui.Text("", 12, true);
    private readonly TextBlock previewTitle = Ui.Text("Open a clip", 20);
    private readonly TextBlock previewNote = Ui.Text("Preview a recording, choose the section to keep, then export a new file.", 13, true);
    private readonly StackPanel previewMessage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440, Margin = new Thickness(24) };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 3, Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Hidden };
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Button open, export, cancelExport, play, setIn, setOut;
    private CancellationTokenSource? exporting;
    private string? path;
    private bool playing, updating, loading, previewReady, hasExportStatus;
    private double duration;
    public bool IsExporting => exporting is not null;

    public ClipView()
    {
        AutomationProperties.SetName(timeline, "Playback position in seconds");
        AutomationProperties.SetHelpText(timeline, "Use arrow keys to move one tenth of a second, Page Up or Page Down for one second, and Home or End for the clip boundaries.");
        AutomationProperties.SetName(start, "Trim in point in seconds");
        AutomationProperties.SetHelpText(start, "The first timestamp to keep, using seconds and a decimal point.");
        AutomationProperties.SetName(end, "Trim out point in seconds");
        AutomationProperties.SetHelpText(end, "The final timestamp to keep, using seconds and a decimal point.");
        AutomationProperties.SetName(mode, "Clip export format and quality");
        AutomationProperties.SetName(position, "Playback time and duration");
        AutomationProperties.SetName(progress, "Export in progress");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var grid = new Grid { Margin = new Thickness(20, 16, 20, 14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var title = Ui.Text("Clip preview", 20); title.FontWeight = FontWeights.SemiBold; heading.Children.Add(title);
        sourceName.TextWrapping = TextWrapping.NoWrap; sourceName.TextTrimming = TextTrimming.CharacterEllipsis; sourceName.Margin = new Thickness(0, 4, 0, 0); heading.Children.Add(sourceName);
        top.Children.Add(heading); open = Ui.Button("Open clip…", Open); open.Margin = new Thickness(0); open.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(open, 1); top.Children.Add(open); grid.Children.Add(top);
        preview.Children.Add(player);
        previewTitle.TextAlignment = previewNote.TextAlignment = TextAlignment.Center;
        previewNote.Margin = new Thickness(0, 8, 0, 0); previewMessage.Children.Add(previewTitle); previewMessage.Children.Add(previewNote); preview.Children.Add(previewMessage);
        var backdrop = new Border { Child = preview, BorderThickness = new Thickness(1), MinHeight = 80 };
        backdrop.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); backdrop.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(backdrop, 1); grid.Children.Add(backdrop);
        var controls = new StackPanel(); controls.Children.Add(timeline);
        var transport = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        position.TextWrapping = TextWrapping.NoWrap; position.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(position, Dock.Right); transport.Children.Add(position);
        play = Ui.Button("Play", TogglePlay); play.MinWidth = 68; DockPanel.SetDock(play, Dock.Left); transport.Children.Add(play); transport.Children.Add(playbackState); controls.Children.Add(transport);
        var trim = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        setIn = Ui.Button("Set in", () => start.Text = player.Position.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        setOut = Ui.Button("Set out", () => end.Text = player.Position.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        trim.Children.Add(TrimPoint("In", start, setIn)); trim.Children.Add(TrimPoint("Out", end, setOut));
        var units = Ui.Text("seconds", 12, true); units.Margin = new Thickness(0, 0, 0, 8); trim.Children.Add(units); controls.Children.Add(trim);
        var exportRow = new WrapPanel(); mode.Margin = new Thickness(0, 0, 8, 8); exportRow.Children.Add(mode);
        export = Ui.Button("Export clip…", async () => await Export()); export.SetResourceReference(StyleProperty, "PrimaryButton"); export.Margin = new Thickness(0, 0, 8, 8); exportRow.Children.Add(export);
        cancelExport = Ui.Button("Cancel export", CancelExport); cancelExport.Margin = new Thickness(0, 0, 0, 8); exportRow.Children.Add(cancelExport); controls.Children.Add(exportRow);
        controls.Children.Add(modeNote); controls.Children.Add(progress);
        controls.Children.Add(new ScrollViewer { Content = status, MaxHeight = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(controls, 2); grid.Children.Add(controls); Content = grid;
        timeline.ValueChanged += (_, _) =>
        {
            if (!updating && previewReady) player.Position = TimeSpan.FromSeconds(timeline.Value);
            UpdatePosition();
        };
        mode.SelectionChanged += (_, _) => UpdateModeNote();
        AttachPlayer(player);
        ticker.Tick += (_, _) =>
        {
            if (!playing) return;
            updating = true; timeline.Value = player.Position.TotalSeconds; updating = false; UpdatePosition();
        };
        Loaded += (_, _) => ticker.Start();
        Unloaded += (_, _) => { ticker.Stop(); PausePlayback(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ticker.Start(); else { ticker.Stop(); PausePlayback(); } };
        UpdateModeNote(); RefreshControls();
    }

    private static FrameworkElement TrimPoint(string label, TextBox input, Button set)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 8) };
        var name = Ui.Text(label, 12, true); name.Margin = new Thickness(0, 0, 8, 0); input.Margin = new Thickness(0, 0, 6, 0); set.Margin = new Thickness(0);
        group.Children.Add(name); group.Children.Add(input); group.Children.Add(set); return group;
    }

    private static MediaElement CreatePlayer()
    {
        var value = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = System.Windows.Media.Stretch.Uniform, ScrubbingEnabled = true };
        AutomationProperties.SetName(value, "Clip preview");
        return value;
    }

    private void AttachPlayer(MediaElement value)
    {
        value.MediaOpened += PlayerOpened;
        value.MediaEnded += PlayerEnded;
        value.MediaFailed += PlayerFailed;
    }

    private void ReplacePlayer()
    {
        var previous = player;
        player = CreatePlayer();
        // WPF media notifications are queued without a source URI. Give each load its own
        // element so an old notification can never change the current source or trim values.
        previous.MediaOpened -= PlayerOpened;
        previous.MediaEnded -= PlayerEnded;
        previous.MediaFailed -= PlayerFailed;
        previous.Close();
        previous.Source = null;
        preview.Children.Remove(previous);
        AttachPlayer(player);
        preview.Children.Insert(0, player);
    }

    private void PlayerOpened(object sender, RoutedEventArgs e) { if (sender is MediaElement source) MediaOpened(source); }
    private void PlayerEnded(object sender, RoutedEventArgs e) { if (sender is MediaElement source) MediaEnded(source); }
    private void PlayerFailed(object? sender, ExceptionRoutedEventArgs e) { if (sender is MediaElement source) MediaFailed(source, e.ErrorException); }

    public void LoadClip(string file)
    {
        if (exporting is not null) return;
        var fullPath = Path.GetFullPath(file);
        PausePlayback(); ReplacePlayer(); path = fullPath; duration = 0; loading = true; previewReady = false; hasExportStatus = false;
        updating = true; timeline.Value = 0; timeline.Maximum = 0; updating = false;
        start.Text = "0.000"; end.Text = "0.000";
        sourceName.Text = Path.GetFileName(fullPath); sourceName.ToolTip = fullPath;
        previewTitle.Text = "Loading preview…"; previewNote.Text = "Opening the clip with Windows media codecs.";
        previewNote.ToolTip = null; AutomationProperties.SetHelpText(previewNote, "");
        previewMessage.Visibility = Visibility.Visible; status.Text = "Loading " + Path.GetFileName(fullPath) + "…";
        RefreshControls(); UpdatePosition(); player.Source = new Uri(fullPath); player.Play();
    }

    private void MediaOpened(MediaElement source)
    {
        if (!ReferenceEquals(source, player) || path is null || !loading) return;
        duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
        loading = false; previewReady = duration > 0;
        if (!previewReady) { MediaFailed(source, new InvalidOperationException("Windows did not provide a playable duration.")); return; }
        timeline.Maximum = duration; end.Text = duration.ToString("F3", CultureInfo.InvariantCulture);
        player.Pause(); previewMessage.Visibility = Visibility.Collapsed;
        status.Text = "Choose in and out points, then export a new file. The source clip is kept.";
        RefreshControls(); UpdatePosition();
    }

    private void MediaEnded(MediaElement source)
    {
        if (!ReferenceEquals(source, player) || !previewReady || loading) return;
        PausePlayback(); playbackState.Text = "Ended";
        updating = true; timeline.Value = duration; updating = false; UpdatePosition();
    }

    private void MediaFailed(MediaElement source, Exception error)
    {
        if (!ReferenceEquals(source, player) || path is null) return;
        loading = false; previewReady = false; PausePlayback();
        previewTitle.Text = "Preview unavailable";
        previewNote.Text = "Windows could not play this codec. You can still enter in and out points in seconds and export with FFmpeg.";
        previewNote.ToolTip = error.Message;
        AutomationProperties.SetHelpText(previewNote, error.Message);
        previewMessage.Visibility = Visibility.Visible;
        if (!hasExportStatus && exporting is null)
            status.Text = "Windows preview: " + error.Message + " Your source clip and trim values are kept.";
        RefreshControls();
    }

    private void RefreshControls()
    {
        var busy = exporting is not null;
        open.IsEnabled = !busy; play.IsEnabled = timeline.IsEnabled = setIn.IsEnabled = setOut.IsEnabled = previewReady && !busy;
        start.IsEnabled = end.IsEnabled = mode.IsEnabled = path is not null && !loading && !busy;
        export.IsEnabled = path is not null && !loading && !busy;
        cancelExport.IsEnabled = busy && !exporting!.IsCancellationRequested;
        cancelExport.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        playbackState.Text = loading ? "Loading…" : previewReady ? playing ? "Playing" : "Paused" : path is null ? "No clip" : "Preview unavailable";
        play.Content = playing ? "Pause" : "Play";
    }

    private void UpdateModeNote()
    {
        modeNote.Text = mode.SelectedIndex switch
        {
            1 => "Copies streams without re-encoding. The start may move to an earlier keyframe.",
            2 => "Re-encodes as a smaller MP4 with reduced quality.",
            3 => "Re-encodes the selected range as WebM.",
            _ => "Re-encodes at the selected in and out points for an exact trim."
        };
        modeNote.Text += " Export uses FFmpeg, independently of the recording backend.";
        AutomationProperties.SetHelpText(mode, modeNote.Text);
    }

    private void UpdatePosition() => position.Text = FormatTime(timeline.Value) + " / " + (duration > 0 ? FormatTime(duration) : "--:--.---");
    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.fff" : @"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }
    private void Open()
    {
        if (exporting is not null) return;
        var picker = new OpenFileDialog { Filter = "Video files|*.mp4;*.mkv;*.webm;*.mov;*.avi|All files|*.*" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) LoadClip(picker.FileName);
    }
    private void PausePlayback()
    {
        player.Pause(); playing = false; play.Content = "Play";
        if (previewReady) playbackState.Text = "Paused";
    }
    private void TogglePlay()
    {
        if (!previewReady || exporting is not null) return;
        if (playing) PausePlayback();
        else { if (player.Position.TotalSeconds >= duration) player.Position = TimeSpan.Zero; player.Play(); playing = true; }
        RefreshControls();
    }
    private void CancelExport()
    {
        if (exporting is null || exporting.IsCancellationRequested) return;
        exporting.Cancel(); status.Text = "Cancelling export…"; RefreshControls();
    }
    private bool TryGetTrimRange(out double from, out double to)
    {
        var validFrom = double.TryParse(start.Text, CultureInfo.InvariantCulture, out from);
        var validTo = double.TryParse(end.Text, CultureInfo.InvariantCulture, out to);
        return validFrom && validTo && double.IsFinite(from) && double.IsFinite(to) && from >= 0 && to > from && (duration <= 0 || to <= duration + .1);
    }

    private CancellationTokenSource? BeginExport()
    {
        if (path is null || loading || exporting is not null) return null;
        exporting = new(); hasExportStatus = true;
        try
        {
            PausePlayback(); status.Text = "Choose where to save the exported clip…"; RefreshControls();
            return exporting;
        }
        catch { exporting.Dispose(); exporting = null; throw; }
    }

    private void EndExport(CancellationTokenSource? operation)
    {
        if (operation is null) return;
        try { if (ReferenceEquals(exporting, operation)) { exporting = null; RefreshControls(); } }
        finally { operation.Dispose(); }
    }

    private async Task Export()
    {
        if (path is null || loading || exporting is not null) return;
        CancellationTokenSource? operation = null;
        try
        {
            hasExportStatus = true;
            if (!TryGetTrimRange(out var from, out var to))
            {
                status.Text = duration > 0 ? "Enter an in/out range in seconds within this clip. Out must be later than In." : "Enter finite in/out points in seconds. Out must be later than In; the clip duration is unavailable.";
                start.Focus(); return;
            }
            var ffmpegPath = ((App)Application.Current).Settings.Recording.FfmpegPath;
            if (FfmpegLocator.Find(ffmpegPath) is null)
            {
                status.Text = "FFmpeg is required for clip export. Choose its executable in Settings → Recording. Your source clip and trim values are kept."; return;
            }
            var source = path;
            var exportMode = (ClipExportMode)mode.SelectedIndex;
            operation = BeginExport();
            if (operation is null) return;
            var extension = exportMode == ClipExportMode.WebM ? ".webm" : ".mp4";
            var picker = new SaveFileDialog { Filter = extension == ".webm" ? "WebM video|*.webm" : "MP4 video|*.mp4", FileName = Path.GetFileNameWithoutExtension(source) + "-trim" + extension, OverwritePrompt = false };
            if (picker.ShowDialog(Window.GetWindow(this)) != true || operation.IsCancellationRequested)
            {
                status.Text = "Export cancelled. The source clip and trim values are kept."; return;
            }
            status.Text = "Exporting… The source clip is kept.";
            await new ClipProcessor().ExportAsync(source, picker.FileName, from, to, exportMode, ffmpegPath, operation.Token);
            status.Text = "Saved " + picker.FileName;
        }
        catch (OperationCanceledException) { status.Text = "Export cancelled. The source clip and trim values are kept."; }
        catch (Exception ex) { status.Text = ex.Message + " Your source clip and trim values are kept."; }
        finally { EndExport(operation); }
    }
}
