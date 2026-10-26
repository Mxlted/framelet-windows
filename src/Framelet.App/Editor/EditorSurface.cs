using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet.App.Editor;

internal sealed class EditorSurface : FrameworkElement
{
    private ImageDocument? _document;
    private BitmapSource? _rendered;
    private BitmapSource? _withoutSelected;
    private Annotation? _preview;
    private Annotation? _original;
    private Point _anchor;
    private bool _drawing;
    private bool _moving;
    private int _resizeHandle = -1;
    private Guid? _selectedId;
    private readonly List<Point> _penPoints = [];
    private Rect? _crop;
    private EffectPreviewRenderer? _effectPreview;
    private EditorTool _tool = EditorTool.Select;
    private double _zoom = 1;
    private int _effectSize = 12;

    public EditorSurface()
    {
        Focusable = true;
        ClipToBounds = false;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Cross;
        Unloaded += (_, _) =>
        {
            ResetDrag();
            _effectPreview?.Dispose();
            _effectPreview = null;
        };
    }

    public ImageDocument? Document
    {
        get => _document;
        set
        {
            if (_document != null) _document.Changed -= OnDocumentChanged;
            _document = value;
            if (value != null) value.Changed += OnDocumentChanged;
            _selectedId = null;
            _crop = null;
            OnDocumentChanged(this, EventArgs.Empty);
        }
    }

    public EditorTool Tool
    {
        get => _tool;
        set { if (_tool == value) return; ResetDrag(); _tool = value; InvalidateVisual(); }
    }
    public Color StrokeColor { get; set; } = Color.FromRgb(241, 113, 88);
    public double StrokeWidth { get; set; } = 4;
    public double TextSize { get; set; } = 24;
    public string TextContent { get; set; } = "Your text";
    public bool Filled { get; set; }
    public int EffectSize
    {
        get => _effectSize;
        set
        {
            if (_effectSize == value) return;
            _effectSize = value;
            if (_preview is { Tool: EditorTool.Blur or EditorTool.Pixelate }) _preview = _preview with { EffectSize = value };
            _effectPreview?.Clear();
            InvalidateVisual();
        }
    }
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_zoom == value) return;
            _zoom = value;
            _effectPreview?.Clear();
            InvalidateVisual();
        }
    }
    public Annotation? Selected => Document?.Annotations.FirstOrDefault(item => item.Id == _selectedId);
    public Rect? PendingCrop => _crop;
    public event EventHandler? SelectionChanged;
    public event EventHandler? CropChanged;
    public event EventHandler? TextEditRequested;
    public event EventHandler<Point>? PointerChanged;

    private void OnDocumentChanged(object? sender, EventArgs args)
    {
        ResetDrag();
        if (Document != null) { Width = Document.Width; Height = Document.Height; }
        _rendered = null;
        _withoutSelected = null;
        if (Selected == null) _selectedId = null;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (Document == null) return;
        context.DrawRectangle(Checkerboard(), null, new Rect(0, 0, Width, Height));
        _rendered ??= Document.Export();
        var underneath = _withoutSelected ?? _rendered;
        if (_preview is { Tool: EditorTool.Blur or EditorTool.Pixelate } effect)
        {
            if (_effectPreview == null)
            {
                _effectPreview = new EffectPreviewRenderer(Dispatcher);
                _effectPreview.Changed += InvalidateVisual;
            }
            var dpi = VisualTreeHelper.GetDpi(this);
            _effectPreview.RequestPreview(underneath, effect, Zoom * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
            if (_effectPreview.Result is { } patch) AnnotationRenderer.DrawReplacement(context, underneath, patch);
            else context.DrawImage(underneath, new Rect(0, 0, Width, Height));
        }
        else
        {
            context.DrawImage(underneath, new Rect(0, 0, Width, Height));
            if (_preview != null)
            {
                context.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));
                AnnotationRenderer.Draw(context, _preview, underneath);
                context.Pop();
            }
        }
        if (_crop is Rect crop)
        {
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(crop));
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, outside);
            context.DrawRectangle(null, new System.Windows.Media.Pen(Brushes.White, 1 / Zoom), crop);
            var guide = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1 / Zoom);
            for (int third = 1; third < 3; third++)
            {
                context.DrawLine(guide, new Point(crop.X + crop.Width * third / 3, crop.Y), new Point(crop.X + crop.Width * third / 3, crop.Bottom));
                context.DrawLine(guide, new Point(crop.X, crop.Y + crop.Height * third / 3), new Point(crop.Right, crop.Y + crop.Height * third / 3));
            }
        }
        else if ((_preview ?? Selected) is Annotation selected)
        {
            Rect bounds = selected.Bounds;
            var outline = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(78, 159, 241)), 1 / Zoom);
            outline.DashStyle = DashStyles.Dash;
            context.DrawRectangle(null, outline, bounds);
            foreach (Point handle in Handles(bounds))
                context.DrawRectangle(Brushes.White, new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(44, 113, 184)), 1 / Zoom),
                    new Rect(handle.X - 3.5 / Zoom, handle.Y - 3.5 / Zoom, 7 / Zoom, 7 / Zoom));
        }
    }

    private static Brush Checkerboard()
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 235, 235)), null, new Rect(0, 0, 16, 16));
            var gray = new SolidColorBrush(Color.FromRgb(211, 211, 211));
            context.DrawRectangle(gray, null, new Rect(0, 0, 8, 8));
            context.DrawRectangle(gray, null, new Rect(8, 8, 8, 8));
        }
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Document == null) return;
        Focus();
        _anchor = ClampPoint(e.GetPosition(this));
        if (Tool == EditorTool.Select)
        {
            _resizeHandle = Selected == null ? -1 : Array.FindIndex(Handles(Selected.Bounds), point => (point - _anchor).Length <= 8 / Zoom);
            if (_resizeHandle < 0) Select(Document.Annotations.Reverse().FirstOrDefault(a => a.HitTest(_anchor, 5 / Zoom))?.Id);
            if (Selected == null) return;
            if (e.ClickCount == 2 && Selected.Tool == EditorTool.Text)
            {
                TextEditRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            _moving = _resizeHandle < 0;
            _original = Selected;
            _preview = Selected;
            _withoutSelected = AnnotationRenderer.Render(Document.Image, Document.Annotations.Where(a => a.Id != _selectedId));
        }
        else if (Tool == EditorTool.Text)
        {
            var annotation = AnnotationRenderer.FitTextBounds(CreateAnnotation(_anchor, new Point(Math.Min(Width, _anchor.X + Math.Max(180, TextSize * 10)), _anchor.Y + TextSize * 1.5)));
            Document.Add(annotation);
            Select(annotation.Id);
            TextEditRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        else
        {
            Select(null);
            _drawing = true;
            if (Tool == EditorTool.Crop)
            {
                _crop = new Rect(_anchor, _anchor);
                CropChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _penPoints.Clear();
                _penPoints.Add(_anchor);
                _preview = CreateAnnotation(_anchor, _anchor);
            }
        }
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = ClampPoint(e.GetPosition(this));
        PointerChanged?.Invoke(this, point);
        if (!IsMouseCaptured) return;
        if (_drawing)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                Vector delta = point - _anchor;
                if (Tool == EditorTool.Arrow)
                {
                    double angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * (Math.PI / 4);
                    point = _anchor + new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length;
                }
                else if (Tool != EditorTool.Pen)
                {
                    double side = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
                    point = _anchor + new Vector(Math.Sign(delta.X) * side, Math.Sign(delta.Y) * side);
                }
                point = ClampPoint(point);
            }
            if (Tool == EditorTool.Crop)
            {
                _crop = new Rect(_anchor, point);
                CropChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_preview != null)
            {
                if (Tool == EditorTool.Pen && (_penPoints[^1] - point).Length > .8 / Zoom) _penPoints.Add(point);
                _preview = _preview with { End = point, Points = _penPoints.ToArray() };
            }
        }
        else if (_original != null)
        {
            if (_moving) _preview = _original.Translate(point - _anchor);
            else if (_resizeHandle >= 0)
            {
                Rect old = _original.Bounds;
                double x1 = old.Left, x2 = old.Right, y1 = old.Top, y2 = old.Bottom;
                if (_resizeHandle is 0 or 6 or 7) x1 = Math.Min(point.X, x2 - 2);
                if (_resizeHandle is 2 or 3 or 4) x2 = Math.Max(point.X, x1 + 2);
                if (_resizeHandle is 0 or 1 or 2) y1 = Math.Min(point.Y, y2 - 2);
                if (_resizeHandle is 4 or 5 or 6) y2 = Math.Max(point.Y, y1 + 2);
                _preview = _original.Transform(old, new Rect(x1, y1, x2 - x1, y2 - y1));
                if (_preview.Tool == EditorTool.Text) _preview = AnnotationRenderer.FitTextBounds(_preview);
            }
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured || Document == null) return;
        Annotation? finished = _preview;
        bool add = _drawing;
        bool changed = _original != null && finished != null && finished != _original;
        ResetDrag();
        if (finished != null && add && (finished.Tool == EditorTool.Pen || (finished.End - finished.Start).Length >= 2))
        {
            Document.Add(finished);
            Select(finished.Id);
        }
        else if (finished != null && changed) Document.Replace(finished);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _preview = null;
        _original = null;
        _withoutSelected = null;
        _drawing = _moving = false;
        _resizeHandle = -1;
        _effectPreview?.Clear();
        InvalidateVisual();
    }

    private Annotation CreateAnnotation(Point start, Point end) => new()
    {
        Tool = Tool, Start = start, End = end, Color = StrokeColor, StrokeWidth = StrokeWidth,
        FontSize = TextSize, Text = TextContent, Filled = Filled, EffectSize = EffectSize, Points = [start]
    };

    public void Select(Guid? id)
    {
        if (_selectedId != id) ResetDrag();
        _selectedId = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void UpdateSelected(Func<Annotation, Annotation> change)
    {
        if (Selected is Annotation selected) Document?.Replace(change(selected));
    }

    public void DeleteSelected()
    {
        if (_selectedId is Guid id) Document?.Remove(id);
    }

    public void DuplicateSelected()
    {
        if (Selected is not Annotation selected || Document == null) return;
        var duplicate = selected.Translate(new Vector(16, 16)) with { Id = Guid.NewGuid() };
        Document.Add(duplicate);
        Select(duplicate.Id);
    }

    public void Nudge(Vector offset)
    {
        if (Selected != null) UpdateSelected(a => a.Translate(offset));
    }

    public void ApplyCrop()
    {
        if (_crop is not Rect crop || Document == null) return;
        int x = Math.Clamp((int)Math.Floor(crop.Left), 0, Document.Width - 1);
        int y = Math.Clamp((int)Math.Floor(crop.Top), 0, Document.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(crop.Right), x + 1, Document.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(crop.Bottom), y + 1, Document.Height);
        _crop = null;
        Document.Crop(new Int32Rect(x, y, right - x, bottom - y));
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelInteraction()
    {
        ResetDrag();
        _crop = null;
        Select(null);
        CropChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ResetDrag()
    {
        _effectPreview?.Clear();
        _drawing = _moving = false;
        _preview = _original = null;
        _withoutSelected = null;
        _resizeHandle = -1;
        ReleaseMouseCapture();
    }

    private Point ClampPoint(Point p) => new(Math.Clamp(p.X, 0, Math.Max(0, Width)), Math.Clamp(p.Y, 0, Math.Max(0, Height)));
    private static Point[] Handles(Rect bounds) =>
    [
        bounds.TopLeft, new(bounds.X + bounds.Width / 2, bounds.Top), bounds.TopRight,
        new(bounds.Right, bounds.Y + bounds.Height / 2), bounds.BottomRight,
        new(bounds.X + bounds.Width / 2, bounds.Bottom), bounds.BottomLeft, new(bounds.Left, bounds.Y + bounds.Height / 2)
    ];
}
