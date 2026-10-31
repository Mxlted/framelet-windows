using System.IO;
using System.Windows;
using System.Windows.Controls;
using Framelet.App;
using Framelet.Core;

internal static class SettingsUiChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Settings retain drafts across categories and theme resource changes", () =>
        {
            var original = new AppSettings();
            var window = new SettingsWindow(original, _ => throw new InvalidOperationException("Unexpected save"));
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
            var window = new SettingsWindow(new AppSettings(), _ => applied = true);
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
            var window = new SettingsWindow(new AppSettings(), _ => applied = true, "Hotkeys");
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
            var window = new SettingsWindow(current, _ => throw new IOException(new string('x', 4000))) { Width = 740, Height = 600 };
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
            var window = new SettingsWindow(current, _ => { }, "Recording");
            try
            {
                Require(!window.Result.Recording.UseFfmpeg);
                var toggle = Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg));
                toggle.SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Require(window.Result.Recording.UseFfmpeg);
                SetText(Find<TextBox>(window, nameof(RecordingSettings.FfmpegAudioDevice)), "Draft audio input");
                Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg)).SetCurrentValue(CheckBox.IsCheckedProperty, false);
                Require(!window.Result.Recording.UseFfmpeg);
                Find<CheckBox>(window, nameof(RecordingSettings.UseFfmpeg)).SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Require(Find<TextBox>(window, nameof(RecordingSettings.FfmpegAudioDevice)).Text == "Draft audio input");
                Require(!current.Recording.UseFfmpeg && current.Recording.FfmpegAudioDevice == "Saved input");
            }
            finally { window.Close(); }
        });
    }

    private static void Navigate(SettingsWindow window, string category) => Find<ListBox>(window, "SettingsCategories").SelectedItem = category;
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
