using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Framelet.App.Editor;

internal sealed class ResizeImageDialog : Window
{
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly CheckBox _lock;
    private readonly TextBlock _error;
    private readonly double _ratio;
    private bool _syncing;
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }

    public ResizeImageDialog(int width, int height)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Resize image";
        Width = 390; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        _ratio = (double)width / height;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Resize image", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        _width = new TextBox { Text = width.ToString(), Height = 34, VerticalContentAlignment = VerticalAlignment.Center };
        _height = new TextBox { Text = height.ToString(), Height = 34, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_width, "Width in image pixels");
        AutomationProperties.SetName(_height, "Height in image pixels");
        var widthPanel = new StackPanel();
        var widthLabel = new Label { Content = "_Width (px)", Target = _width, Padding = new Thickness(0, 0, 0, 6) };
        widthLabel.SetResourceReference(ForegroundProperty, "TextBrush");
        widthPanel.Children.Add(widthLabel);
        widthPanel.Children.Add(_width);
        var heightPanel = new StackPanel();
        var heightLabel = new Label { Content = "_Height (px)", Target = _height, Padding = new Thickness(0, 0, 0, 6) };
        heightLabel.SetResourceReference(ForegroundProperty, "TextBrush");
        heightPanel.Children.Add(heightLabel);
        heightPanel.Children.Add(_height);
        Grid.SetColumn(heightPanel, 2);
        fields.Children.Add(widthPanel); fields.Children.Add(heightPanel);
        panel.Children.Add(fields);
        _lock = new CheckBox { Content = "Keep proportions", IsChecked = true, Margin = new Thickness(0, 14, 0, 10) };
        panel.Children.Add(_lock);
        _error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        panel.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Resize", IsDefault = true, Padding = new Thickness(16, 7, 16, 7) };
        apply.SetResourceReference(StyleProperty, "PrimaryButton");
        apply.Click += (_, _) => Apply();
        actions.Children.Add(cancel); actions.Children.Add(apply);
        panel.Children.Add(actions);
        Content = panel;
        _width.TextChanged += (_, _) => SyncDimensions(true);
        _height.TextChanged += (_, _) => SyncDimensions(false);
        Loaded += (_, _) => { _width.Focus(); _width.SelectAll(); };
    }

    private void SyncDimensions(bool widthChanged)
    {
        if (_syncing || _lock.IsChecked != true) return;
        _syncing = true;
        if (widthChanged && int.TryParse(_width.Text, out int width) && width > 0)
            _height.Text = Math.Max(1, Math.Round(width / _ratio)).ToString("0");
        else if (!widthChanged && int.TryParse(_height.Text, out int height) && height > 0)
            _width.Text = Math.Max(1, Math.Round(height * _ratio)).ToString("0");
        _syncing = false;
    }

    private void Apply()
    {
        if (!int.TryParse(_width.Text, out int width) || !int.TryParse(_height.Text, out int height))
        {
            ShowError("Enter whole numbers for both dimensions.");
            return;
        }
        try { ImageDocument.ValidateDimensions(width, height); }
        catch (ArgumentOutOfRangeException) { ShowError("Use 1–16,384 pixels per side, up to 64 megapixels."); return; }
        ImageWidth = width; ImageHeight = height;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
