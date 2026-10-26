using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Framelet.Core;
using Framelet.Media;
using Microsoft.Win32;

namespace Framelet.App;

public sealed class ClipView : UserControl
{
    private readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = System.Windows.Media.Stretch.Uniform, ScrubbingEnabled = true };
    private readonly Slider timeline = new()
    {
        Minimum = 0, SmallChange = .1, LargeChange = 1, IsMoveToPointEnabled = true,
        AutoToolTipPlacement = AutoToolTipPlacement.TopLeft, AutoToolTipPrecision = 2,
        Margin = new Thickness(0, 14, 0, 12)
    };
    private readonly TextBox start = new() { Text = "0.000", Width = 92 }, end = new() { Text = "0.000", Width = 92 };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Exact MP4", "Lossless trim (keyframes)", "Smaller MP4", "WebM" }, SelectedIndex = 0, MinWidth = 200 };
    private readonly TextBlock status = Ui.Text("Open a video to preview or trim it.", 12, true);
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Button export;
    private readonly Button play;
    private CancellationTokenSource? exporting;
    private string? path;
    private bool playing, updating;
    private double duration;
    public bool IsExporting => exporting is not null;

    public ClipView()
    {
        AutomationProperties.SetName(player, "Clip preview");
        AutomationProperties.SetName(timeline, "Playback position in seconds");
        AutomationProperties.SetHelpText(timeline, "Use arrow keys to move one tenth of a second, Page Up or Page Down for one second, and Home or End for the clip boundaries.");
        AutomationProperties.SetName(start, "Trim in point in seconds");
        AutomationProperties.SetHelpText(start, "The first timestamp to keep, using seconds and a decimal point.");
        AutomationProperties.SetName(end, "Trim out point in seconds");
        AutomationProperties.SetHelpText(end, "The final timestamp to keep, using seconds and a decimal point.");
        AutomationProperties.SetName(mode, "Clip export format and quality");
        AutomationProperties.SetHelpText(mode, "Exact MP4 re-encodes at the selected timestamps. Lossless trim copies streams at keyframe boundaries.");
        var grid = new Grid { Margin = new Thickness(26, 22, 26, 18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var open = Ui.Button("Open clip…", Open); DockPanel.SetDock(open, Dock.Right); top.Children.Add(open); top.Children.Add(Ui.Text("Clip preview & trim", 22)); grid.Children.Add(top);
        var backdrop = new Border { Background = System.Windows.Media.Brushes.Black, Child = player }; Grid.SetRow(backdrop, 1); grid.Children.Add(backdrop);
        var controls = new StackPanel(); controls.Children.Add(timeline);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        play = Ui.Button("Play", TogglePlay); actions.Children.Add(play); actions.Children.Add(Ui.Button("Set in", () => start.Text = player.Position.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)));
        actions.Children.Add(Ui.Button("Set out", () => end.Text = player.Position.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)));
        actions.Children.Add(Ui.Text("In  ", 12, true)); actions.Children.Add(start); actions.Children.Add(Ui.Text("   Out  ", 12, true)); actions.Children.Add(end); controls.Children.Add(actions);
        var exportRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; mode.Margin = new Thickness(0, 0, 10, 0); exportRow.Children.Add(mode); export = Ui.Button("Export clip…", async () => await Export()); exportRow.Children.Add(export);
        exportRow.Children.Add(Ui.Button("Cancel export", () => exporting?.Cancel())); controls.Children.Add(exportRow); controls.Children.Add(status);
        var note = Ui.Text("Exact trims re-encode. Lossless trims snap to codec keyframes and can begin before the selected time. Export requires FFmpeg independently of your recording backend.", 12, true); note.Margin = new Thickness(0, 8, 0, 0); controls.Children.Add(note);
        Grid.SetRow(controls, 2); grid.Children.Add(controls); Content = grid;
        timeline.ValueChanged += (_, _) => { if (!updating && path is not null) player.Position = TimeSpan.FromSeconds(timeline.Value); };
        player.MediaOpened += (_, _) => { duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0; timeline.Maximum = duration; end.Text = duration.ToString("F3", CultureInfo.InvariantCulture); player.Pause(); status.Text = Path.GetFileName(path) + " · " + duration.ToString("F1") + " seconds"; };
        player.MediaEnded += (_, _) => { playing = false; play.Content = "Play"; player.Pause(); };
        player.MediaFailed += (_, e) => status.Text = "Windows could not preview this codec. " + e.ErrorException.Message;
        ticker.Tick += (_, _) => { if (playing) { updating = true; timeline.Value = player.Position.TotalSeconds; updating = false; } };
        Loaded += (_, _) => ticker.Start(); Unloaded += (_, _) => { ticker.Stop(); player.Pause(); playing = false; play.Content = "Play"; };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) ticker.Start();
            else { ticker.Stop(); player.Pause(); playing = false; play.Content = "Play"; }
        };
    }
    public void LoadClip(string file)
    {
        if (exporting is not null) return;
        player.Stop(); path = file; duration = 0; start.Text = "0.000"; end.Text = "0.000"; player.Source = new Uri(Path.GetFullPath(file)); player.Play(); playing = false; play.Content = "Play";
    }
    private void Open() { var picker = new OpenFileDialog { Filter = "Video files|*.mp4;*.mkv;*.webm;*.mov;*.avi|All files|*.*" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) LoadClip(picker.FileName); }
    private void TogglePlay() { if (path is null) return; if (playing) player.Pause(); else player.Play(); playing = !playing; play.Content = playing ? "Pause" : "Play"; }
    private async Task Export()
    {
        if (path is null || exporting is not null) return;
        try
        {
            if (!double.TryParse(start.Text, CultureInfo.InvariantCulture, out var from) || !double.TryParse(end.Text, CultureInfo.InvariantCulture, out var to) || from < 0 || to <= from || duration <= 0 || to > duration + .1)
                throw new ArgumentException("Enter a valid in/out range in seconds within this clip.");
            var extension = mode.SelectedIndex == 3 ? ".webm" : ".mp4";
            var picker = new SaveFileDialog { Filter = extension == ".webm" ? "WebM video|*.webm" : "MP4 video|*.mp4", FileName = Path.GetFileNameWithoutExtension(path) + "-trim" + extension, OverwritePrompt = false };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            exporting = new(); export.IsEnabled = false; status.Text = "Exporting…";
            await new ClipProcessor().ExportAsync(path, picker.FileName, from, to, (ClipExportMode)mode.SelectedIndex, ((App)Application.Current).Settings.Recording.FfmpegPath, exporting.Token);
            status.Text = "Saved " + picker.FileName;
        }
        catch (OperationCanceledException) { status.Text = "Export canceled. The source clip is unchanged."; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { exporting?.Dispose(); exporting = null; export.IsEnabled = true; }
    }
}
