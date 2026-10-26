using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Framelet.App.Capture;
using Framelet.Core;
using Framelet.Media;
using Framelet.Recording;
using Microsoft.Win32;

namespace Framelet.App;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings draft;
    private readonly Action<AppSettings> apply;
    private readonly StackPanel page = new() { Margin = new Thickness(28, 24, 34, 20) };
    private readonly ListBox navigation = new() { Margin = new Thickness(10, 16, 10, 0) };
    private readonly TextBlock error = Ui.Text("", 12);
    public AppSettings Result => draft;

    public SettingsWindow(AppSettings current, Action<AppSettings> apply, string initial = "General")
    {
        this.apply = apply;
        SetResourceReference(StyleProperty, typeof(Window));
        draft = SettingsStore.Clone(current);
        Title = "Framelet settings"; Width = 830; Height = 710; MinWidth = 740; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(174) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var navBorder = new Border { Child = navigation, BorderThickness = new Thickness(0, 0, 1, 0) }; navBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); navBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); body.Children.Add(navBorder);
        foreach (var name in new[] { "General", "Capture", "Editor", "Recording", "Hotkeys", "Appearance" }) navigation.Items.Add(name);
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll); root.Children.Add(body);
        var footer = new DockPanel { Margin = new Thickness(18, 12, 18, 16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(Ui.Button("Cancel", () => Close())); buttons.Children.Add(Ui.Button("Save settings", Save)); DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(error); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
        navigation.SelectionChanged += (_, _) => ShowPage(navigation.SelectedItem?.ToString() ?? "General"); navigation.SelectedItem = initial;
    }

    private void ShowPage(string name)
    {
        page.Children.Clear();
        switch (name)
        {
            case "General":
                Ui.Header(page, "General", "Your files stay on this computer. Framelet has no accounts, uploads, or telemetry.");
                var folder = Ui.Input(draft, nameof(draft.OutputDirectory));
                Ui.Row(page, "Save captures to", folder);
                page.Children.Add(Ui.Button("Choose folder…", () => { var dialog = new OpenFolderDialog { InitialDirectory = draft.OutputDirectory }; if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName; }));
                Ui.Row(page, "Filename pattern", Ui.Input(draft, nameof(draft.FilenamePattern)), "Use {date} and {time}. Existing files get a numbered suffix.");
                page.Children.Add(Ui.Check(draft, nameof(draft.MinimizeToTray), "Keep Framelet in the system tray when the window closes"));
                break;
            case "Capture":
                Ui.Header(page, "Capture", "Choose what happens after you take a screenshot.");
                page.Children.Add(Ui.Check(draft, nameof(draft.OpenEditorAfterCapture), "Open the image editor"));
                page.Children.Add(Ui.Check(draft, nameof(draft.CopyAfterCapture), "Copy the image to the clipboard"));
                page.Children.Add(Ui.Check(draft, nameof(draft.SaveAfterCapture), "Automatically save a PNG"));
                Ui.Row(page, "Delay before capture (seconds)", Ui.Choice(draft, nameof(draft.CaptureDelaySeconds), Enumerable.Range(0, 11)));
                page.Children.Add(Ui.Text("Region selection uses a frozen desktop. Escape cancels; drag across displays to include more than one monitor.", 13, true));
                break;
            case "Editor":
                Ui.Header(page, "Editor", "Defaults for new annotations. Existing objects keep their own styling.");
                Ui.Row(page, "Default color", Ui.Choice(draft, nameof(draft.EditorColor), new[] { "#F0B45A", "#EF6464", "#66B9EF", "#80C59B", "#FFFFFF", "#202124" }));
                Ui.Row(page, "Default stroke width", Ui.Choice(draft, nameof(draft.EditorStroke), new double[] { 2, 3, 4, 6, 8, 12 }));
                page.Children.Add(Ui.Text("Ctrl+Z / Ctrl+Y for undo and redo. Ctrl+C copies the finished image. Ctrl+S saves it. Original files are preserved by Save As.", 13, true));
                break;
            case "Recording": RecordingPage(); break;
            case "Hotkeys":
                Ui.Header(page, "Hotkeys", "Global shortcuts work while Framelet is in the system tray. Conflicts are reported when settings are applied.");
                Ui.Row(page, "Capture region", Ui.Input(draft, nameof(draft.RegionHotkey)));
                Ui.Row(page, "Capture active window", Ui.Input(draft, nameof(draft.WindowHotkey)));
                Ui.Row(page, "Capture monitor under the pointer", Ui.Input(draft, nameof(draft.MonitorHotkey)));
                Ui.Row(page, "Start / stop recording", Ui.Input(draft, nameof(draft.RecordHotkey)), "Example: Ctrl+Shift+5. Leave blank to disable a shortcut.");
                break;
            case "Appearance":
                Ui.Header(page, "Appearance", "A quiet workspace with clear controls and consistent contrast.");
                Ui.Row(page, "Theme", Ui.Choice(draft, nameof(draft.Theme), new[] { "Dark", "Light", "System" }));
                page.Children.Add(Ui.Text("System follows your Windows app theme when Framelet starts or settings are saved. Interface scaling follows Windows display settings.", 13, true));
                break;
        }
    }

    private void RecordingPage()
    {
        Ui.Header(page, "Recording", "Native recording saves H.264/AAC MP4 directly through Windows. FFmpeg is an optional alternative.");
        var toggle = Ui.Check(draft.Recording, nameof(RecordingSettings.UseFfmpeg), "Use FFmpeg for recording"); page.Children.Add(toggle);
        toggle.Checked += (_, _) => ShowPage("Recording"); toggle.Unchecked += (_, _) => ShowPage("Recording");
        Ui.Row(page, "Frame rate", Ui.Choice(draft.Recording, nameof(RecordingSettings.FrameRate), new[] { 24, 30, 60, 120 }), "60 FPS is the default. 120 FPS is experimental and depends on the display, GPU, and capture source.");
        var presets = new ComboBox { ItemsSource = new[] { "Custom", "Small clip · 30 FPS / 6 Mbps", "Balanced · 60 FPS / 12 Mbps", "High detail · 60 FPS / 24 Mbps" }, SelectedIndex = 0 };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex < 1) return; draft.Recording.FrameRate = presets.SelectedIndex == 1 ? 30 : 60; draft.Recording.VideoBitrate = presets.SelectedIndex switch { 1 => 6_000_000, 2 => 12_000_000, _ => 24_000_000 }; ShowPage("Recording"); };
        Ui.Row(page, "Recording preset", presets);
        Ui.Row(page, "Video bitrate", Ui.Choice(draft.Recording, nameof(RecordingSettings.VideoBitrate), new[] { 4_000_000, 6_000_000, 8_000_000, 12_000_000, 16_000_000, 24_000_000, 40_000_000 }), "Bits per second. Higher values preserve more detail and make larger files.");
        page.Children.Add(Ui.Check(draft.Recording, nameof(RecordingSettings.IncludeCursor), "Include the mouse pointer"));
        if (!draft.Recording.UseFfmpeg)
        {
            page.Children.Add(Ui.Check(draft.Recording, nameof(RecordingSettings.HardwareEncoding), "Prefer hardware encoding"));
            page.Children.Add(Ui.Check(draft.Recording, nameof(RecordingSettings.SystemAudio), "Record system audio"));
            Ui.Row(page, "System audio device", Devices(false));
            page.Children.Add(Ui.Check(draft.Recording, nameof(RecordingSettings.Microphone), "Record microphone"));
            Ui.Row(page, "Microphone device", Devices(true));
            page.Children.Add(Ui.Text("Windows chooses the available encoder. Hardware encoding is requested when enabled; it is not guaranteed. Pause and resume are available with native recording.", 12, true));
        }
        else
        {
            Ui.Row(page, "Video encoder", Ui.Choice(draft.Recording, nameof(RecordingSettings.FfmpegVideoCodec), FfmpegArguments.VideoCodecs), "libx264/libx265 use the CPU; nvenc, qsv, and amf require compatible GPU encoders in your FFmpeg build.");
            Ui.Row(page, "Audio encoder", Ui.Choice(draft.Recording, nameof(RecordingSettings.FfmpegAudioCodec), FfmpegArguments.AudioCodecs));
            Ui.Row(page, "DirectShow audio input", Ui.Input(draft.Recording, nameof(RecordingSettings.FfmpegAudioDevice)), "Exact device name; blank records silent video. This backend supports one DirectShow microphone or installed loopback input. Native system/microphone toggles do not apply.");
            page.Children.Add(Ui.Button("List FFmpeg audio devices", async () => await ListAudioDevices()));
            Ui.Row(page, "Advanced encoder parameters", Ui.Input(draft.Recording, nameof(RecordingSettings.FfmpegExtraArguments)), "For example: -crf 20 -preset slow. Inputs and output paths are managed by Framelet. Pause is unavailable with this backend.");
        }
        var dependency = new Expander { Header = "FFmpeg executable · also used for clip export", Margin = new Thickness(0, 22, 0, 0), IsExpanded = draft.Recording.UseFfmpeg };
        var details = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var path = Ui.Input(draft.Recording, nameof(RecordingSettings.FfmpegPath)); Ui.Row(details, "Executable path", path, "Leave blank to detect tools\\ffmpeg.exe or your PATH. Native capture never needs FFmpeg.");
        var actions = new WrapPanel();
        actions.Children.Add(Ui.Button("Browse…", () => { var picker = new OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe|Executables|*.exe" }; if (picker.ShowDialog(this) == true) path.Text = picker.FileName; }));
        actions.Children.Add(Ui.Button("Verify", async () => { try { error.Text = "Found: " + await FfmpegLocator.VerifyAsync(draft.Recording.FfmpegPath); } catch (Exception ex) { error.Text = ex.Message; } }));
        actions.Children.Add(Ui.Button("Download page ↗", () => Process.Start(new ProcessStartInfo("https://ffmpeg.org/download.html#build-windows") { UseShellExecute = true })));
        details.Children.Add(actions); dependency.Content = details; page.Children.Add(dependency);
    }

    private ComboBox Devices(bool microphone)
    {
        try
        {
            var items = (microphone ? NativeAudioDevices.GetInputs() : NativeAudioDevices.GetOutputs()).ToList();
            items.Insert(0, new NativeAudioDevice("", "Windows default", true));
            var combo = new ComboBox { ItemsSource = items, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
            combo.SetBinding(ComboBox.SelectedValueProperty, Ui.Bind(draft.Recording, microphone ? nameof(RecordingSettings.MicrophoneDeviceId) : nameof(RecordingSettings.SystemAudioDeviceId)));
            return combo;
        }
        catch (Exception ex) { return new ComboBox { ItemsSource = new[] { "Device list unavailable: " + ex.Message }, SelectedIndex = 0 }; }
    }

    private async Task ListAudioDevices()
    {
        try
        {
            var exe = await FfmpegLocator.VerifyAsync(draft.Recording.FfmpegPath);
            var result = await ProcessRunner.RunAsync(exe, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]);
            var output = string.Join(Environment.NewLine, result.Output.Split('\n').Where(x => x.Contains("audio") || x.Contains("Alternative name")));
            MessageBox.Show(this, string.IsNullOrWhiteSpace(output) ? "No DirectShow audio devices reported." : output, "FFmpeg audio inputs");
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }

    private void Save()
    {
        try
        {
            draft.Validate(); FfmpegArguments.ParseAdvanced(draft.Recording.FfmpegExtraArguments);
            var shortcuts = new[] { draft.RegionHotkey, draft.WindowHotkey, draft.MonitorHotkey, draft.RecordHotkey }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var pairs = new HashSet<(uint, uint)>();
            foreach (var shortcut in shortcuts)
            {
                if (!HotkeyManager.TryParse(shortcut, out var modifiers, out var key, out var reason)) throw new ArgumentException(reason);
                if (!pairs.Add((modifiers, key))) throw new ArgumentException("Each action needs a different hotkey.");
            }
            if (!draft.CopyAfterCapture && !draft.SaveAfterCapture && !draft.OpenEditorAfterCapture) throw new ArgumentException("Enable at least one post-capture action.");
            apply(draft); DialogResult = true;
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }
}
