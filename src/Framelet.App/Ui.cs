using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Framelet.App;

internal static class Ui
{
    public static TextBlock Text(string text, double size = 13, bool muted = false)
    {
        var value = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        return value;
    }
    public static Button Button(string text, Action action)
    {
        var value = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        value.Click += (_, _) => action();
        return value;
    }
    public static Binding Bind(object source, string path) => new(path) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true };
    public static void Header(Panel panel, string text, string? help = null)
    {
        var title = Text(text, 23); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 10); panel.Children.Add(title);
        if (help is not null) { var label = Text(help, 13, true); label.Margin = new Thickness(0, 0, 0, 24); panel.Children.Add(label); }
    }
    public static void Row(Panel panel, string label, FrameworkElement control, string? help = null)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var text = Text(label); text.Margin = new Thickness(0, 0, 0, 7); block.Children.Add(text);
        AutomationProperties.SetLabeledBy(control, text);
        if (help is not null) AutomationProperties.SetHelpText(control, help);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        block.Children.Add(control);
        if (help is not null) { var note = Text(help, 12, true); note.Margin = new Thickness(0, 6, 0, 0); block.Children.Add(note); }
        panel.Children.Add(block);
    }
    public static TextBox Input(object source, string path)
    {
        var input = new TextBox(); input.SetBinding(TextBox.TextProperty, Bind(source, path)); return input;
    }
    public static ComboBox Choice(object source, string path, System.Collections.IEnumerable items)
    {
        var combo = new ComboBox { ItemsSource = items };
        if (path == "VideoBitrate")
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new BitrateConverter() });
            combo.ItemTemplate = new DataTemplate { VisualTree = text };
        }
        combo.SetBinding(ComboBox.SelectedItemProperty, Bind(source, path)); return combo;
    }
    public static CheckBox Check(object source, string path, string label)
    {
        var check = new CheckBox { Content = label }; check.SetBinding(CheckBox.IsCheckedProperty, Bind(source, path)); return check;
    }
}

internal sealed class BitrateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is int bits ? $"{bits / 1_000_000d:0.#} Mbps" : value;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
}
