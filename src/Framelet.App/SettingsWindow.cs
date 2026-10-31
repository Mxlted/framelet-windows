using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
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
    private readonly Func<bool, IReadOnlyList<NativeAudioDevice>> listNativeAudioDevices;
    private readonly StackPanel page = new() { Margin = new Thickness(24, 20, 24, 20) };
    private readonly ListBox navigation = new() { Name = "SettingsCategories", Margin = new Thickness(10, 12, 10, 0) };
    private readonly TextBlock error = Ui.Text("", 12);
    private readonly Dictionary<string, FrameworkElement> fields = [];
    private readonly ScrollViewer scroll;
    private readonly ScrollViewer errorScroll;
    private CancellationTokenSource? pageOperation;
    private FrameworkElement? invalidField;
    public AppSettings Result => draft;

    public SettingsWindow(AppSettings current, Action<AppSettings> apply, string initial = "General",
        Func<bool, IReadOnlyList<NativeAudioDevice>>? listNativeAudioDevices = null)
    {
        this.apply = apply;
        this.listNativeAudioDevices = listNativeAudioDevices ?? (microphone => microphone ? NativeAudioDevices.GetInputs() : NativeAudioDevices.GetOutputs());
        SetResourceReference(StyleProperty, typeof(Window));
        draft = SettingsStore.Clone(current);
        Title = "Framelet settings"; Width = 830; Height = 710; MinWidth = 740; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(156) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var navBorder = new Border { Child = navigation, BorderThickness = new Thickness(0, 0, 1, 0) }; navBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); navBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); body.Children.Add(navBorder);
        foreach (var name in new[] { "General", "Capture", "Editor", "Recording", "Hotkeys", "Appearance" }) navigation.Items.Add(name);
        AutomationProperties.SetName(navigation, "Settings categories");
        scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll); root.Children.Add(body);
        var footer = new StackPanel { Margin = new Thickness(18, 12, 18, 14) };
        error.Name = "SettingsError";
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        errorScroll = new ScrollViewer { Content = error, MaxHeight = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
        footer.Children.Add(errorScroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("Cancel", () => Close()); cancel.IsCancel = true; cancel.MinWidth = 88;
        var save = Ui.Button("Save settings", Save); save.Name = "SaveSettings"; save.IsDefault = true; save.MinWidth = 116; save.Margin = new Thickness(0); save.SetResourceReference(StyleProperty, "PrimaryButton");
        buttons.Children.Add(cancel); buttons.Children.Add(save); footer.Children.Add(buttons);
        var footerBorder = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); footerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(footerBorder, 1); root.Children.Add(footerBorder); Content = root;
        navigation.SelectionChanged += (_, _) => { ShowPage(navigation.SelectedItem?.ToString() ?? "General"); scroll.ScrollToTop(); };
        navigation.SelectedItem = navigation.Items.Contains(initial) ? initial : "General";
        Closed += (_, _) => pageOperation?.Cancel();
    }

    private void ShowPage(string name)
    {
        pageOperation?.Cancel();
        fields.Clear(); page.Children.Clear();
        switch (name)
        {
            case "General":
                Ui.Header(page, "General", "Your files stay on this computer. Framelet has no accounts, uploads, or telemetry.");
                var folder = Input(draft, nameof(draft.OutputDirectory));
                var folderRow = new DockPanel { LastChildFill = true };
                var browseFolder = Ui.Button("Browse…", () => { var dialog = new OpenFolderDialog { InitialDirectory = draft.OutputDirectory }; if (dialog.ShowDialog(this) == true) folder.SetCurrentValue(TextBox.TextProperty, dialog.FolderName); });
                browseFolder.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(browseFolder, Dock.Right); folderRow.Children.Add(browseFolder); folderRow.Children.Add(folder);
                AutomationProperties.SetName(folder, "Save captures to");
                Ui.Row(page, "Save captures to", folderRow);
                Ui.Row(page, "Filename pattern", Input(draft, nameof(draft.FilenamePattern)), "Use {date} and {time}. Existing files get a numbered suffix.");
                page.Children.Add(Check(draft, nameof(draft.MinimizeToTray), "Keep Framelet in the system tray when the window closes"));
                break;
            case "Capture":
                Ui.Header(page, "Capture", "Choose what happens after you take a screenshot.");
                page.Children.Add(Check(draft, nameof(draft.OpenEditorAfterCapture), "Open the image editor"));
                page.Children.Add(Check(draft, nameof(draft.CopyAfterCapture), "Copy the image to the clipboard"));
                page.Children.Add(Check(draft, nameof(draft.SaveAfterCapture), "Automatically save a PNG"));
                Ui.Row(page, "Delay before capture (seconds)", Choice(draft, nameof(draft.CaptureDelaySeconds), Enumerable.Range(0, 11)));
                page.Children.Add(Ui.Text("Region selection uses a frozen desktop. Escape cancels; drag across displays to include more than one monitor.", 13, true));
                break;
            case "Editor":
                Ui.Header(page, "Editor", "Defaults for new annotations. Existing objects keep their own styling.");
                Ui.Row(page, "Default color", Choice(draft, nameof(draft.EditorColor), new[] { "#F0B45A", "#EF6464", "#66B9EF", "#80C59B", "#FFFFFF", "#202124" }));
                Ui.Row(page, "Default stroke width", Choice(draft, nameof(draft.EditorStroke), new double[] { 2, 3, 4, 6, 8, 12 }));
                page.Children.Add(Ui.Text("Ctrl+Z / Ctrl+Y for undo and redo. Ctrl+C copies the finished image. Ctrl+S saves it. Original files are preserved by Save As.", 13, true));
                break;
            case "Recording": RecordingPage(); break;
            case "Hotkeys":
                Ui.Header(page, "Hotkeys", "Global shortcuts work while Framelet is in the system tray. Conflicts are reported when settings are applied.");
                Ui.Row(page, "Capture region", Input(draft, nameof(draft.RegionHotkey)));
                Ui.Row(page, "Capture active window", Input(draft, nameof(draft.WindowHotkey)));
                Ui.Row(page, "Capture monitor under the pointer", Input(draft, nameof(draft.MonitorHotkey)));
                Ui.Row(page, "Start / stop recording", Input(draft, nameof(draft.RecordHotkey)), "Example: Ctrl+Shift+5. Leave blank to disable a shortcut.");
                break;
            case "Appearance":
                Ui.Header(page, "Appearance", "Choose a theme for every Framelet window.");
                Ui.Row(page, "Theme", Choice(draft, nameof(draft.Theme), new[] { "Dark", "Light", "System" }));
                page.Children.Add(Ui.Text("System follows your Windows app theme when Framelet starts or settings are saved. Interface scaling follows Windows display settings.", 13, true));
                break;
        }
    }

    private void RecordingPage()
    {
        Ui.Header(page, "Recording", "Native recording saves MP4 through Windows. Clip export uses FFmpeg independently of this choice.");
        var toggle = Check(draft.Recording, nameof(RecordingSettings.UseFfmpeg), "Use FFmpeg for recording"); page.Children.Add(toggle);
        toggle.Checked += (_, _) => ShowPage("Recording"); toggle.Unchecked += (_, _) => ShowPage("Recording");
        Section(draft.Recording.UseFfmpeg ? "FFmpeg video" : "Native video");
        var presets = new ComboBox { ItemsSource = new[] { "Custom", "Small clip · 30 FPS / 6 Mbps", "Balanced · 60 FPS / 12 Mbps", "High detail · 60 FPS / 24 Mbps" }, SelectedIndex = 0 };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex < 1) return; draft.Recording.FrameRate = presets.SelectedIndex == 1 ? 30 : 60; draft.Recording.VideoBitrate = presets.SelectedIndex switch { 1 => 6_000_000, 2 => 12_000_000, _ => 24_000_000 }; ShowPage("Recording"); };
        Ui.Row(page, "Recording preset", presets);
        var video = new Grid(); video.ColumnDefinitions.Add(new ColumnDefinition()); video.ColumnDefinitions.Add(new ColumnDefinition());
        var rate = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var bitrate = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(bitrate, 1);
        Ui.Row(rate, "Frame rate", Choice(draft.Recording, nameof(RecordingSettings.FrameRate), new[] { 24, 30, 60, 120 }));
        Ui.Row(bitrate, "Video bitrate", Choice(draft.Recording, nameof(RecordingSettings.VideoBitrate), new[] { 4_000_000, 6_000_000, 8_000_000, 12_000_000, 16_000_000, 24_000_000, 40_000_000 }));
        video.Children.Add(rate); video.Children.Add(bitrate); page.Children.Add(video);
        page.Children.Add(Ui.Text("120 FPS is experimental. Higher bitrates preserve more detail and create larger files.", 12, true));
        page.Children.Add(Check(draft.Recording, nameof(RecordingSettings.IncludeCursor), "Include the mouse pointer"));
        if (!draft.Recording.UseFfmpeg)
        {
            page.Children.Add(Check(draft.Recording, nameof(RecordingSettings.HardwareEncoding), "Prefer hardware encoding"));
            Section("Native audio");
            page.Children.Add(Check(draft.Recording, nameof(RecordingSettings.SystemAudio), "Record system audio"));
            page.Children.Add(Check(draft.Recording, nameof(RecordingSettings.Microphone), "Record microphone"));
            var audio = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Ui.Row(audio, "System audio device", Devices(false));
            Ui.Row(audio, "Microphone device", Devices(true));
            page.Children.Add(new Expander { Header = "Audio devices", Content = audio, IsExpanded = true, Margin = new Thickness(0, 8, 0, 12) });
            page.Children.Add(Ui.Text("Windows chooses the available encoder. Hardware encoding is requested when enabled; it is not guaranteed. Pause and resume are available with native recording.", 12, true));
        }
        else
        {
            Ui.Row(page, "Video encoder", Choice(draft.Recording, nameof(RecordingSettings.FfmpegVideoCodec), FfmpegArguments.VideoCodecs), "libx264/libx265 use the CPU; nvenc, qsv, and amf require compatible GPU encoders in your FFmpeg build.");
            Ui.Row(page, "Audio encoder", Choice(draft.Recording, nameof(RecordingSettings.FfmpegAudioCodec), FfmpegArguments.AudioCodecs));
            Ui.Row(page, "DirectShow audio input", Input(draft.Recording, nameof(RecordingSettings.FfmpegAudioDevice)), "Exact device name; blank records silent video. This backend supports one DirectShow microphone or installed loopback input. Native system/microphone toggles do not apply.");
            var audioFeedback = Ui.Text("", 12, true); audioFeedback.Margin = new Thickness(0, 8, 0, 12);
            AutomationProperties.SetLiveSetting(audioFeedback, AutomationLiveSetting.Polite);
            var listDevices = Ui.Button("List FFmpeg audio devices", () => { });
            listDevices.Click += async (_, _) => await RunPageOperation(listDevices, audioFeedback, ListAudioDevices);
            page.Children.Add(listDevices); page.Children.Add(audioFeedback);
            Ui.Row(page, "Advanced encoder parameters", Input(draft.Recording, nameof(RecordingSettings.FfmpegExtraArguments)), "For example: -crf 20 -preset slow. Inputs and output paths are managed by Framelet. Pause is unavailable with this backend.");
        }
        var dependency = new Expander { Header = "FFmpeg executable and clip export", Margin = new Thickness(0, 18, 0, 0), IsExpanded = draft.Recording.UseFfmpeg };
        var details = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var path = Input(draft.Recording, nameof(RecordingSettings.FfmpegPath)); Ui.Row(details, "Executable path", path, "Leave blank to detect tools\\ffmpeg.exe or your PATH. Native capture never needs FFmpeg.");
        var actions = new WrapPanel();
        actions.Children.Add(Ui.Button("Browse…", () => { var picker = new OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe|Executables|*.exe" }; if (picker.ShowDialog(this) == true) path.SetCurrentValue(TextBox.TextProperty, picker.FileName); }));
        var feedback = Ui.Text("", 12, true); feedback.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        var verify = Ui.Button("Verify", () => { });
        verify.Click += async (_, _) => await RunPageOperation(verify, feedback, async ct => "Verified: " + await FfmpegLocator.VerifyAsync(draft.Recording.FfmpegPath, ct));
        actions.Children.Add(verify);
        actions.Children.Add(Ui.Button("Download page ↗", () => Process.Start(new ProcessStartInfo("https://ffmpeg.org/download.html#build-windows") { UseShellExecute = true })));
        details.Children.Add(actions); details.Children.Add(feedback); dependency.Content = details; page.Children.Add(dependency);
    }

    private ComboBox Devices(bool microphone)
    {
        try
        {
            var items = listNativeAudioDevices(microphone).ToList();
            items.Insert(0, new NativeAudioDevice("", "Windows default", true));
            var combo = new ComboBox { ItemsSource = items, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
            combo.SetBinding(ComboBox.SelectedValueProperty, Ui.Bind(draft.Recording, microphone ? nameof(RecordingSettings.MicrophoneDeviceId) : nameof(RecordingSettings.SystemAudioDeviceId)));
            return combo;
        }
        catch (Exception ex) { return new ComboBox { ItemsSource = new[] { "Device list unavailable: " + ex.Message }, SelectedIndex = 0 }; }
    }

    private async Task<string> ListAudioDevices(CancellationToken ct)
    {
        var exe = await FfmpegLocator.VerifyAsync(draft.Recording.FfmpegPath, ct);
        var result = await ProcessRunner.RunAsync(exe, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], ct);
        var output = string.Join(Environment.NewLine, result.Output.Split('\n').Where(x => x.Contains("audio") || x.Contains("Alternative name")));
        return string.IsNullOrWhiteSpace(output) ? "No DirectShow audio inputs found. Choose native recording for independent system and microphone audio." : output;
    }

    private async Task RunPageOperation(Button button, TextBlock feedback, Func<CancellationToken, Task<string>> operation)
    {
        if (pageOperation is not null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        pageOperation = cancellation; button.IsEnabled = false; feedback.Text = "Checking…";
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        try { feedback.Text = await operation(cancellation.Token); }
        catch (OperationCanceledException) { feedback.Text = "The check was cancelled or timed out. Check the executable and try again."; }
        catch (Exception ex) { feedback.Text = ex.Message; feedback.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush"); }
        finally { button.IsEnabled = true; if (ReferenceEquals(pageOperation, cancellation)) pageOperation = null; }
    }

    private void Save()
    {
        ClearFieldError(invalidField);
        try
        {
            ValidateField("General", nameof(draft.OutputDirectory), () =>
            {
                if (string.IsNullOrWhiteSpace(draft.OutputDirectory) || !System.IO.Path.IsPathFullyQualified(draft.OutputDirectory))
                    throw new ArgumentException("Choose an absolute output folder using Browse, or enter its full path.");
            });
            ValidateField("General", nameof(draft.FilenamePattern), () => OutputPaths.ValidatePattern(draft.FilenamePattern));
            ValidateField("Recording", nameof(RecordingSettings.FfmpegExtraArguments), () => FfmpegArguments.ParseAdvanced(draft.Recording.FfmpegExtraArguments));
            var shortcuts = new[]
            {
                (Property: nameof(draft.RegionHotkey), Label: "Capture region", Value: draft.RegionHotkey),
                (Property: nameof(draft.WindowHotkey), Label: "Capture active window", Value: draft.WindowHotkey),
                (Property: nameof(draft.MonitorHotkey), Label: "Capture monitor", Value: draft.MonitorHotkey),
                (Property: nameof(draft.RecordHotkey), Label: "Start / stop recording", Value: draft.RecordHotkey)
            };
            var pairs = new Dictionary<(uint, uint), string>();
            foreach (var shortcut in shortcuts.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
            {
                ValidateField("Hotkeys", shortcut.Property, () =>
                {
                    if (!HotkeyManager.TryParse(shortcut.Value, out var modifiers, out var key, out var reason))
                        throw new ArgumentException(shortcut.Label + ": " + reason);
                    if (pairs.TryGetValue((modifiers, key), out var other))
                        throw new ArgumentException(shortcut.Label + " uses the same shortcut as " + other + ". Choose a different shortcut, or leave it blank to disable.");
                    pairs.Add((modifiers, key), shortcut.Label);
                });
            }
            ValidateField("Capture", nameof(draft.OpenEditorAfterCapture), () =>
            {
                if (!draft.CopyAfterCapture && !draft.SaveAfterCapture && !draft.OpenEditorAfterCapture)
                    throw new ArgumentException("Choose at least one action after capture: open the editor, copy the image, or save a PNG.");
            });
            draft.Validate();
            apply(draft); DialogResult = true;
        }
        catch (FieldValidationException ex)
        {
            navigation.SelectedItem = ex.Category;
            ShowError(ex.Message); UpdateLayout();
            if (fields.TryGetValue(ex.Property, out var field))
            {
                invalidField = field;
                field.SetResourceReference(Control.BorderBrushProperty, "DangerBrush");
                field.BringIntoView(); field.Focus();
            }
        }
        catch (Exception ex) { ShowError("Settings were not saved. " + ex.Message + " Your changes are still available here."); }
    }

    private void ShowError(string message)
    {
        error.Text = message; errorScroll.Visibility = Visibility.Visible; errorScroll.ScrollToTop();
    }

    private static void ValidateField(string category, string property, Action validate)
    {
        try { validate(); }
        catch (ArgumentException ex) { throw new FieldValidationException(category, property, ex.Message); }
    }

    private sealed class FieldValidationException(string category, string property, string message) : Exception(message)
    {
        public string Category { get; } = category;
        public string Property { get; } = property;
    }

    private void Section(string title)
    {
        var heading = Ui.Text(title); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 14, 0, 10); page.Children.Add(heading);
    }

    private T Field<T>(T control, string property) where T : FrameworkElement
    {
        control.Name = property; AutomationProperties.SetAutomationId(control, property); fields[property] = control;
        if (control is TextBox input) input.TextChanged += (_, _) => ClearFieldError(input);
        if (control is ComboBox choice) choice.SelectionChanged += (_, _) => ClearFieldError(choice);
        if (control is CheckBox check)
        {
            check.Checked += (_, _) => ClearFieldError(check);
            check.Unchecked += (_, _) => ClearFieldError(check);
        }
        return control;
    }

    private void ClearFieldError(FrameworkElement? field)
    {
        if (field is null || !ReferenceEquals(invalidField, field)) return;
        field.ClearValue(Control.BorderBrushProperty); invalidField = null;
        error.Text = ""; errorScroll.Visibility = Visibility.Collapsed;
    }

    private TextBox Input(object source, string path) => Field(Ui.Input(source, path), path);
    private ComboBox Choice(object source, string path, System.Collections.IEnumerable items) => Field(Ui.Choice(source, path, items), path);
    private CheckBox Check(object source, string path, string label)
    {
        var check = Field(Ui.Check(source, path, label), path);
        check.Content = Ui.Text(label); AutomationProperties.SetName(check, label); return check;
    }
}
