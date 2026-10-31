using System.IO;
using System.Windows;
using System.Windows.Controls;
using Framelet.App;
using Framelet.Core;
using Framelet.Recording;

internal static class SettingsUiChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Settings retain drafts across categories and theme resource changes", () =>
        {
            var original = new AppSettings();
            var window = new SettingsWindow(original, _ => throw new InvalidOperationException("Unexpected save"), listNativeAudioDevices: NativeAudioFixture.List);
            try
            {
                SetText(Find<TextBox>(window, nameof(AppSettings.FilenamePattern)), "Draft_{date}_{time}");
                Navigate(window, "Hotkeys");
                SetText(Find<TextBox>(window, nameof(AppSettings.RegionHotkey)), "Ctrl+Alt+9");
                ((App)Application.Current).ApplyTheme("Light");
                Navigate(window, "General");
                Require(Find<TextBox>(window, nameof(AppSettings.FilenamePattern)).Text == "Draft_{date}_{time}");
                Require(window.Result.RegionHotkey == "Ctrl+Alt+9");
                Require(original.FilenamePattern == "Framelet_{date}_{time}" && original.RegionHotkey == "Ctrl+Shift+2");
            }
            finally { window.Close(); ((App)Application.Current).ApplyTheme("Dark"); }
        });

        check("Settings route an invalid output folder without losing entered values", () =>
        {
            bool applied = false;
            var window = new SettingsWindow(new AppSettings(), _ => applied = true, listNativeAudioDevices: NativeAudioFixture.List);
            try
            {
                SetText(Find<TextBox>(window, nameof(AppSettings.OutputDirectory)), "relative-folder");
                SetText(Find<TextBox>(window, nameof(AppSettings.FilenamePattern)), "Retained_{time}");
                Navigate(window, "Appearance"); Save(window);
                Require(!applied && Equals(Find<ListBox>(window, "SettingsCategories").SelectedItem, "General"));
                Require(Find<TextBox>(window, nameof(AppSettings.OutputDirectory)).Text == "relative-folder");
                Require(Find<TextBox>(window, nameof(AppSettings.FilenamePattern)).Text == "Retained_{time}");
                Require(Find<TextBlock>(window, "SettingsError").Text.Contains("Browse", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });

        check("Settings identify both actions in duplicate hotkeys and preserve the draft", () =>
        {
            bool applied = false;
            var window = new SettingsWindow(new AppSettings(), _ => applied = true, "Hotkeys", NativeAudioFixture.List);
            try
            {
                SetText(Find<TextBox>(window, nameof(AppSettings.WindowHotkey)), "Shift+Ctrl+2");
                Navigate(window, "General"); Save(window);
                Require(!applied && Equals(Find<ListBox>(window, "SettingsCategories").SelectedItem, "Hotkeys"));
                Require(Find<TextBox>(window, nameof(AppSettings.WindowHotkey)).Text == "Shift+Ctrl+2");
                string error = Find<TextBlock>(window, "SettingsError").Text;
                Require(error.Contains("Capture active window", StringComparison.Ordinal) && error.Contains("Capture region", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });

        check("Long settings save failures keep actions reachable and preserve working values", () =>
        {
            var current = new AppSettings();
            var window = new SettingsWindow(current, _ => throw new IOException(new string('x', 4000)), listNativeAudioDevices: NativeAudioFixture.List) { Width = 740, Height = 600 };
            try
            {
                SetText(Find<TextBox>(window, nameof(AppSettings.FilenamePattern)), "Unsaved_{time}");
                Save(window);
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(740, 600)); root.Arrange(new Rect(0, 0, 740, 600)); root.UpdateLayout();
                var save = Find<Button>(window, "SaveSettings");
                var bounds = save.TransformToAncestor(root).TransformBounds(new Rect(save.RenderSize));
                Require(save.IsEnabled && save.IsDefault && bounds.Top >= 0 && bounds.Bottom <= root.ActualHeight && bounds.Right <= root.ActualWidth);
                Require(Find<TextBox>(window, nameof(AppSettings.FilenamePattern)).Text == "Unsaved_{time}");
                Require(current.FilenamePattern == "Framelet_{date}_{time}");
                Require(Find<TextBlock>(window, "SettingsError").Text.Contains("Your changes are still available", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });

        check("Recording settings retain native and FFmpeg drafts when switching backend pages", () =>
        {
            var current = new AppSettings();
            current.Recording.FfmpegAudioDevice = "Saved input";
            current.Recording.SystemAudioDeviceId = NativeAudioFixture.Outputs[0].Id;
            current.Recording.MicrophoneDeviceId = NativeAudioFixture.Inputs[0].Id;
            var window = new SettingsWindow(current, _ => { }, "Recording", NativeAudioFixture.List);
            try
            {
                Require(!window.Result.Recording.UseFfmpeg);
                var outputs = FindDevices(window, nameof(RecordingSettings.SystemAudioDeviceId));
                var inputs = FindDevices(window, nameof(RecordingSettings.MicrophoneDeviceId));
                Require(outputs.Items.Cast<NativeAudioDevice>().Select(device => device.Name).SequenceEqual(
                    new[] { "Windows default" }.Concat(NativeAudioFixture.Outputs.Select(device => device.Name))));
                Require(inputs.Items.Cast<NativeAudioDevice>().Select(device => device.Name).SequenceEqual(
                    new[] { "Windows default" }.Concat(NativeAudioFixture.Inputs.Select(device => device.Name))));
                Require(Equals(outputs.SelectedValue, NativeAudioFixture.Outputs[0].Id) &&
                    Equals(inputs.SelectedValue, NativeAudioFixture.Inputs[0].Id));
                SetDevice(outputs, NativeAudioFixture.Outputs[1].Id);
                SetDevice(inputs, NativeAudioFixture.Inputs[1].Id);
                var toggle = Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg));
                toggle.SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Require(window.Result.Recording.UseFfmpeg);
                SetText(Find<TextBox>(window, nameof(RecordingSettings.FfmpegAudioDevice)), "Draft audio input");
                Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg)).SetCurrentValue(CheckBox.IsCheckedProperty, false);
                Require(!window.Result.Recording.UseFfmpeg);
                Require(Equals(FindDevices(window, nameof(RecordingSettings.SystemAudioDeviceId)).SelectedValue, NativeAudioFixture.Outputs[1].Id));
                Require(Equals(FindDevices(window, nameof(RecordingSettings.MicrophoneDeviceId)).SelectedValue, NativeAudioFixture.Inputs[1].Id));
                Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg)).SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Require(Find<TextBox>(window, nameof(RecordingSettings.FfmpegAudioDevice)).Text == "Draft audio input");
                Require(!current.Recording.UseFfmpeg && current.Recording.FfmpegAudioDevice == "Saved input");
                Require(current.Recording.SystemAudioDeviceId == NativeAudioFixture.Outputs[0].Id &&
                    current.Recording.MicrophoneDeviceId == NativeAudioFixture.Inputs[0].Id);
            }
            finally { window.Close(); }
        });

        check("Native device discovery failures preserve settings and leave the FFmpeg backend available", () =>
        {
            var current = new AppSettings();
            current.Recording.SystemAudioDeviceId = "saved-output-id";
            current.Recording.MicrophoneDeviceId = "saved-input-id";
            var requests = new List<bool>();
            IReadOnlyList<NativeAudioDevice> FailedDiscovery(bool microphone)
            {
                requests.Add(microphone);
                throw new IOException("Isolated audio worker stopped unexpectedly.");
            }
            var window = new SettingsWindow(current, _ => { }, "Recording", FailedDiscovery);
            try
            {
                Require(requests.SequenceEqual(new[] { false, true }));
                Require(Descendants(window).OfType<ComboBox>().Count(combo =>
                    combo.SelectedItem is string text && text.Contains("Isolated audio worker stopped unexpectedly.", StringComparison.Ordinal)) == 2);
                Require(Find<Button>(window, "SaveSettings").IsEnabled);
                Require(window.Result.Recording.SystemAudioDeviceId == "saved-output-id" &&
                    window.Result.Recording.MicrophoneDeviceId == "saved-input-id");
                Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg)).SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Require(window.Result.Recording.UseFfmpeg && requests.Count == 2);
                SetText(Find<TextBox>(window, nameof(RecordingSettings.FfmpegAudioDevice)), "Optional DirectShow input");
                Require(window.Result.Recording.FfmpegAudioDevice == "Optional DirectShow input");
                Require(window.Result.Recording.SystemAudioDeviceId == "saved-output-id" &&
                    window.Result.Recording.MicrophoneDeviceId == "saved-input-id");
                Require(!current.Recording.UseFfmpeg && current.Recording.SystemAudioDeviceId == "saved-output-id" &&
                    current.Recording.MicrophoneDeviceId == "saved-input-id");
            }
            finally { window.Close(); }
        });
    }

    private static void Navigate(SettingsWindow window, string category) => Find<ListBox>(window, "SettingsCategories").SelectedItem = category;
    private static ComboBox FindDevices(SettingsWindow window, string property)
        => Descendants(window).OfType<ComboBox>().Single(combo =>
            combo.GetBindingExpression(ComboBox.SelectedValueProperty)?.ParentBinding.Path.Path == property);
    private static void SetDevice(ComboBox combo, string id)
    {
        combo.SetCurrentValue(ComboBox.SelectedValueProperty, id);
        combo.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateSource();
    }
    private static void Save(SettingsWindow window) => Find<Button>(window, "SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void SetText(TextBox field, string value)
    {
        field.SetCurrentValue(TextBox.TextProperty, value); field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
    }
    private static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement
        => Descendants(parent).OfType<T>().Single(x => x.Name == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Settings UI invariant failed.");
    }
}
