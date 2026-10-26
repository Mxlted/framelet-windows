using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Framelet.App.Editor;

public partial class ImageEditorView : UserControl
{
    private sealed record ObjectItem(Guid Id, string Label);
    private ImageDocument? _document;
    private bool _ready;
    private bool _synchronizing;
    private bool _fitToWindow = true;
    private double _zoom = 1;
    private Point? _panStart;
    private double _panHorizontal, _panVertical;
    private readonly List<Button> _colorButtons = [];

    public ImageEditorView()
    {
        InitializeComponent();
        CreateSwatches();
        Surface.SelectionChanged += (_, _) => RefreshSelection();
        Surface.CropChanged += (_, _) => RefreshCrop();
        Surface.TextEditRequested += (_, _) =>
        {
            RefreshSelection();
            AnnotationTextBox.Focus();
            AnnotationTextBox.SelectAll();
        };
        Surface.PointerChanged += (_, point) =>
        {
            if (_document != null && Surface.PendingCrop == null)
                StatusLabel.Text = $"{Math.Min(_document.Width - 1, (int)point.X)}, {Math.Min(_document.Height - 1, (int)point.Y)} px";
        };
        _ready = true;
        SelectTool(EditorTool.Select);
        RefreshState();
    }

    public bool HasImage => _document != null;
    public bool IsDirty => (_document?.IsDirty ?? false) ||
        Surface.Selected is { Tool: EditorTool.Text } selected && selected.Text != AnnotationTextBox.Text;
    public string? SourcePath { get; private set; }
    public event EventHandler? DocumentChanged;
    public event EventHandler? CopyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? PasteRequested;
    public event Action<string>? FileOpenRequested;

    public void LoadImage(BitmapSource image, string? sourcePath = null)
    {
        var next = new ImageDocument(image, isSaved: sourcePath != null);
        if (_document != null) _document.Changed -= OnDocumentChanged;
        _document = next;
        _document.Changed += OnDocumentChanged;
        Surface.Document = _document;
        SourcePath = sourcePath;
        _fitToWindow = true;
        SelectTool(EditorTool.Select);
        RefreshState();
        Dispatcher.BeginInvoke(FitImage, DispatcherPriority.Loaded);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public BitmapSource ExportImage()
    {
        CommitText();
        return _document?.Export() ?? throw new InvalidOperationException("Capture or open an image first.");
    }

    public void MarkSaved(string? path = null)
    {
        if (path != null) SourcePath = path;
        _document?.MarkSaved();
    }

    public void Undo() { Surface.CancelInteraction(); _document?.Undo(); }
    public void Redo() { Surface.CancelInteraction(); _document?.Redo(); }

    public void ApplyDefaults(string color, double strokeWidth, double fontSize = 24)
    {
        try { Surface.StrokeColor = (Color)ColorConverter.ConvertFromString(color); }
        catch (FormatException) { /* Invalid persisted colors retain the built-in default. */ }
        Surface.StrokeWidth = Math.Clamp(strokeWidth, 1, 40);
        Surface.TextSize = Math.Clamp(fontSize, 8, 160);
        RefreshSelection();
    }

    private void OnDocumentChanged(object? sender, EventArgs args)
    {
        RefreshState();
        if (_fitToWindow) FitImage();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshState()
    {
        if (!_ready) return;
        EmptyState.Visibility = HasImage ? Visibility.Collapsed : Visibility.Visible;
        CanvasHost.Visibility = HasImage ? Visibility.Visible : Visibility.Collapsed;
        ToolRail.IsEnabled = HasImage;
        UndoButton.IsEnabled = _document?.CanUndo == true;
        RedoButton.IsEnabled = _document?.CanRedo == true;
        DimensionsLabel.Text = _document == null ? "No image" : $"{_document.Width:N0} × {_document.Height:N0} px";
        ObjectCount.Text = _document == null || _document.Annotations.Count == 0 ? "No annotations yet" :
            $"{_document.Annotations.Count} editable {(_document.Annotations.Count == 1 ? "object" : "objects")}";
        _synchronizing = true;
        var selected = Surface.Selected?.Id;
        ObjectList.ItemsSource = _document?.Annotations.Reverse().Select((a, index) => new ObjectItem(a.Id,
            a.Tool == EditorTool.Text ? $"T   {a.Text.Replace('\n', ' ').Trim()}" : $"{ToolLabel(a.Tool)}  {_document.Annotations.Count - index}")).ToArray();
        ObjectList.SelectedItem = ObjectList.Items.Cast<ObjectItem>().FirstOrDefault(item => item.Id == selected);
        _synchronizing = false;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (!_ready || _synchronizing) return;
        _synchronizing = true;
        var selected = Surface.Selected;
        var effective = selected?.Tool ?? Surface.Tool;
        ToolTitle.Text = ToolLabel(effective);
        bool crop = Surface.Tool == EditorTool.Crop;
        bool effect = effective is EditorTool.Blur or EditorTool.Pixelate;
        bool text = effective == EditorTool.Text;
        StyleOptions.Visibility = crop || effect ? Visibility.Collapsed : Visibility.Visible;
        TextOptions.Visibility = text && !crop ? Visibility.Visible : Visibility.Collapsed;
        EffectOptions.Visibility = effect && !crop ? Visibility.Visible : Visibility.Collapsed;
        CropOptions.Visibility = crop ? Visibility.Visible : Visibility.Collapsed;
        FillToggle.Visibility = effective is EditorTool.Rectangle or EditorTool.Ellipse ? Visibility.Visible : Visibility.Collapsed;
        WidthPicker.Visibility = StrokeLabel.Visibility = text ? Visibility.Collapsed : Visibility.Visible;
        SetPicker(WidthPicker, selected?.StrokeWidth ?? Surface.StrokeWidth);
        SetPicker(FontPicker, selected?.FontSize ?? Surface.TextSize);
        SetPicker(EffectPicker, selected?.EffectSize ?? Surface.EffectSize);
        FillToggle.IsChecked = selected?.Filled ?? Surface.Filled;
        if (!AnnotationTextBox.IsKeyboardFocusWithin) AnnotationTextBox.Text = selected?.Tool == EditorTool.Text ? selected.Text : Surface.TextContent;
        Color activeColor = selected?.Color ?? Surface.StrokeColor;
        foreach (var button in _colorButtons)
            button.BorderThickness = Equals(button.Tag, activeColor) ? new Thickness(2) : new Thickness(0);
        if (selected != null)
            ObjectList.SelectedItem = ObjectList.Items.Cast<ObjectItem>().FirstOrDefault(item => item.Id == selected.Id);
        else ObjectList.SelectedItem = null;
        _synchronizing = false;
    }

    private static void SetPicker(ComboBox picker, double value)
    {
        foreach (var temporary in picker.Items.OfType<ComboBoxItem>().Where(item => Equals(item.Tag, "custom")).ToArray()) picker.Items.Remove(temporary);
        var match = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => double.TryParse(item.Content?.ToString(), out var current) && Math.Abs(current - value) < .01);
        if (match == null)
        {
            match = new ComboBoxItem { Content = value.ToString("0.#"), Tag = "custom" };
            picker.Items.Add(match);
        }
        picker.SelectedItem = match;
    }

    private void CreateSwatches()
    {
        string[] palette = ["#F17158", "#F4C95D", "#56B795", "#5D9DE2", "#ECEDEE", "#24262A"];
        string[] names = ["Coral", "Amber", "Green", "Blue", "White", "Charcoal"];
        for (int index = 0; index < palette.Length; index++)
        {
            Color color = (Color)ColorConverter.ConvertFromString(palette[index]);
            var button = new Button
            {
                Width = 22, Height = 24, Padding = new Thickness(3), Margin = new Thickness(1, 0, 1, 0),
                Background = Brushes.Transparent, BorderBrush = new SolidColorBrush(Color.FromRgb(152, 156, 166)),
                BorderThickness = new Thickness(0), Tag = color, ToolTip = names[index],
                Content = new Border { Background = new SolidColorBrush(color), Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 140, 140, 140)), BorderThickness = new Thickness(1) }
            };
            System.Windows.Automation.AutomationProperties.SetName(button, names[index] + " annotation color");
            button.Click += (_, _) =>
            {
                Surface.StrokeColor = color;
                Surface.UpdateSelected(a => a with { Color = color });
                RefreshSelection();
            };
            _colorButtons.Add(button);
            ColorSwatches.Children.Add(button);
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<EditorTool>(name, out var tool)) SelectTool(tool);
    }

    private void SelectTool(EditorTool tool)
    {
        CommitText();
        Surface.CancelInteraction();
        Surface.Tool = tool;
        Surface.Cursor = tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
        foreach (Button button in ToolRail.Children.OfType<Button>())
            button.SetResourceReference(BackgroundProperty, button.Tag?.ToString() == tool.ToString() ? "SelectionBrush" : "SurfaceBrush");
        RefreshSelection();
        StatusLabel.Text = tool switch
        {
            EditorTool.Select => "Select an object to move or resize it.",
            EditorTool.Text => "Enter text above, then click to place it.",
            EditorTool.Crop => "Drag a crop. Enter applies; Escape cancels.",
            EditorTool.Blur or EditorTool.Pixelate => "Drag over the area to obscure.",
            EditorTool.Pen => "Draw on the image. Each stroke stays editable.",
            _ => "Drag to draw. Hold Shift to constrain."
        };
        Surface.Focus();
    }

    private void OnWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _synchronizing || WidthPicker.SelectedItem is not ComboBoxItem item || !double.TryParse(item.Content?.ToString(), out var width)) return;
        Surface.StrokeWidth = width;
        Surface.UpdateSelected(a => a with { StrokeWidth = width });
    }
    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _synchronizing || FontPicker.SelectedItem is not ComboBoxItem item || !double.TryParse(item.Content?.ToString(), out var size)) return;
        Surface.TextSize = size;
        Surface.UpdateSelected(a => a.Tool == EditorTool.Text ? AnnotationRenderer.FitTextBounds(a with { FontSize = size }) : a);
    }
    private void OnEffectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _synchronizing || EffectPicker.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Content?.ToString(), out var amount)) return;
        Surface.EffectSize = amount;
        Surface.UpdateSelected(a => a with { EffectSize = amount });
    }
    private void OnFillChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _synchronizing) return;
        Surface.Filled = FillToggle.IsChecked == true;
        Surface.UpdateSelected(a => a with { Filled = Surface.Filled });
    }

    private void CommitText()
    {
        if (!_ready || _synchronizing) return;
        Surface.TextContent = AnnotationTextBox.Text;
        if (Surface.Selected is { Tool: EditorTool.Text } selected && selected.Text != AnnotationTextBox.Text)
            Surface.UpdateSelected(a => AnnotationRenderer.FitTextBounds(a with { Text = AnnotationTextBox.Text }));
    }
    private void OnTextCommit(object sender, KeyboardFocusChangedEventArgs e) => CommitText();
    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready && !_synchronizing && Surface.Selected?.Tool == EditorTool.Text) DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitText();
        Surface.Focus();
        e.Handled = true;
    }

    private void RefreshCrop()
    {
        CropSize.Text = Surface.PendingCrop is Rect crop ? $"{Math.Ceiling(crop.Width):N0} × {Math.Ceiling(crop.Height):N0} px" : "Drag to choose a crop";
    }
    private void OnApplyCrop(object sender, RoutedEventArgs e) { Surface.ApplyCrop(); SelectTool(EditorTool.Select); }
    private void OnCancelCrop(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Select);
    private void OnUndo(object sender, RoutedEventArgs e) => Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Redo();
    private void OnDelete(object sender, RoutedEventArgs e) => Surface.DeleteSelected();
    private void OnDuplicate(object sender, RoutedEventArgs e) => Surface.DuplicateSelected();
    private void OnForward(object sender, RoutedEventArgs e) { if (Surface.Selected is Annotation selected) _document?.MoveLayer(selected.Id, true); }
    private void OnBackward(object sender, RoutedEventArgs e) { if (Surface.Selected is Annotation selected) _document?.MoveLayer(selected.Id, false); }
    private void OnObjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_synchronizing && ObjectList.SelectedItem is ObjectItem item)
        {
            Surface.Tool = EditorTool.Select;
            Surface.Cursor = Cursors.Arrow;
            foreach (Button button in ToolRail.Children.OfType<Button>())
                button.SetResourceReference(BackgroundProperty, button.Tag?.ToString() == EditorTool.Select.ToString() ? "SelectionBrush" : "SurfaceBrush");
            Surface.Select(item.Id);
        }
    }

    private void OnToggleObjects(object sender, RoutedEventArgs e)
    {
        bool show = ObjectsPanel.Visibility != Visibility.Visible;
        ObjectsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ObjectsColumn.Width = new GridLength(show ? 190 : 0);
    }

    private void OnResize(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        var dialog = new ResizeImageDialog(_document.Width, _document.Height) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        Surface.CancelInteraction();
        _document.Resize(dialog.ImageWidth, dialog.ImageHeight);
        _fitToWindow = true;
        FitImage();
    }

    private void FitImage()
    {
        if (_document == null || Viewport.ActualWidth < 120 || Viewport.ActualHeight < 120) return;
        SetZoom(Math.Min(1, Math.Min((Viewport.ActualWidth - 96) / _document.Width, (Viewport.ActualHeight - 96) / _document.Height)), userInitiated: false);
    }
    private void SetZoom(double zoom, bool userInitiated = true)
    {
        _zoom = Math.Clamp(zoom, .025, 8);
        if (userInitiated) _fitToWindow = false;
        Surface.Zoom = _zoom;
        Surface.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        Surface.InvalidateVisual();
        ZoomLabel.Text = $"{_zoom * 100:0}%";
    }
    private void OnFit(object sender, RoutedEventArgs e) { _fitToWindow = true; FitImage(); }
    private void OnActualSize(object sender, RoutedEventArgs e) => SetZoom(1);
    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) { if (_fitToWindow) FitImage(); }
    private void OnViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        double previous = _zoom;
        Point pointer = e.GetPosition(Viewport);
        double horizontal = Viewport.HorizontalOffset, vertical = Viewport.VerticalOffset;
        SetZoom(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2));
        Viewport.UpdateLayout();
        Viewport.ScrollToHorizontalOffset((horizontal + pointer.X - 40) * (_zoom / previous) - pointer.X + 40);
        Viewport.ScrollToVerticalOffset((vertical + pointer.Y - 40) * (_zoom / previous) - pointer.Y + 40);
        e.Handled = true;
    }
    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space))) return;
        _panStart = e.GetPosition(Viewport);
        _panHorizontal = Viewport.HorizontalOffset;
        _panVertical = Viewport.VerticalOffset;
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.Hand;
        e.Handled = true;
    }
    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is not Point anchor) return;
        Point current = e.GetPosition(Viewport);
        Viewport.ScrollToHorizontalOffset(_panHorizontal - (current.X - anchor.X));
        Viewport.ScrollToVerticalOffset(_panVertical - (current.Y - anchor.Y));
        e.Handled = true;
    }
    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panStart == null) return;
        _panStart = null;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox) return;
        for (DependencyObject? element = Keyboard.FocusedElement as DependencyObject; element != null; element = element is Visual ? VisualTreeHelper.GetParent(element) : null)
            if (element is ComboBox or ListBox) return;
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (control)
        {
            switch (e.Key)
            {
                case Key.Z: if (shift) Redo(); else Undo(); break;
                case Key.Y: Redo(); break;
                case Key.C: CopyRequested?.Invoke(this, EventArgs.Empty); break;
                case Key.S: SaveRequested?.Invoke(this, EventArgs.Empty); break;
                case Key.V: PasteRequested?.Invoke(this, EventArgs.Empty); break;
                case Key.D: Surface.DuplicateSelected(); break;
                case Key.D0: _fitToWindow = true; FitImage(); break;
                case Key.D1: SetZoom(1); break;
                case Key.OemCloseBrackets: OnForward(this, new RoutedEventArgs()); break;
                case Key.OemOpenBrackets: OnBackward(this, new RoutedEventArgs()); break;
                default: return;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Delete: Surface.DeleteSelected(); break;
                case Key.Escape: Surface.CancelInteraction(); if (Surface.Tool == EditorTool.Crop) SelectTool(EditorTool.Select); break;
                case Key.Enter: if (Surface.PendingCrop != null) { Surface.ApplyCrop(); SelectTool(EditorTool.Select); } else return; break;
                case Key.Left: Surface.Nudge(new Vector(shift ? -10 : -1, 0)); break;
                case Key.Right: Surface.Nudge(new Vector(shift ? 10 : 1, 0)); break;
                case Key.Up: Surface.Nudge(new Vector(0, shift ? -10 : -1)); break;
                case Key.Down: Surface.Nudge(new Vector(0, shift ? 10 : 1)); break;
                case Key.V: SelectTool(EditorTool.Select); break;
                case Key.A: SelectTool(EditorTool.Arrow); break;
                case Key.R: SelectTool(EditorTool.Rectangle); break;
                case Key.O: SelectTool(EditorTool.Ellipse); break;
                case Key.T: SelectTool(EditorTool.Text); break;
                case Key.P: SelectTool(EditorTool.Pen); break;
                case Key.H: SelectTool(EditorTool.Highlight); break;
                case Key.B: SelectTool(EditorTool.Blur); break;
                case Key.X: SelectTool(EditorTool.Pixelate); break;
                case Key.C: SelectTool(EditorTool.Crop); break;
                default: return;
            }
        }
        e.Handled = true;
    }

    private static bool IsImagePath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && IsImagePath(paths[0]) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths || !IsImagePath(paths[0])) return;
        FileOpenRequested?.Invoke(paths[0]);
        e.Handled = FileOpenRequested != null;
    }
    private static string ToolLabel(EditorTool tool) => tool switch { EditorTool.Pen => "Freehand", EditorTool.Pixelate => "Pixelate", _ => tool.ToString() };
}

