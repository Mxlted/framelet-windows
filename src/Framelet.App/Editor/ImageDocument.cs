using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet.App.Editor;

public sealed class ImageDocument
{
    private sealed record State(BitmapSource Image, Annotation[] Annotations, Guid Revision);
    private readonly List<State> _undo = [];
    private readonly List<State> _redo = [];
    private State _state;
    private Guid _savedRevision;

    public ImageDocument(BitmapSource image, bool isSaved = true)
    {
        _state = new State(Normalize(image), [], Guid.NewGuid());
        _savedRevision = isSaved ? _state.Revision : Guid.Empty;
    }

    internal static BitmapSource Normalize(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateDimensions(image.PixelWidth, image.PixelHeight);
        // Normalize DPI metadata: document coordinates always represent image pixels.
        var converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        int stride = checked(image.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * image.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var normalized = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        normalized.Freeze();
        return normalized;
    }

    public BitmapSource Image => _state.Image;
    public IReadOnlyList<Annotation> Annotations => _state.Annotations;
    public Guid Revision => _state.Revision;
    public int Width => Image.PixelWidth;
    public int Height => Image.PixelHeight;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => _state.Revision != _savedRevision;
    public event EventHandler? Changed;

    public void MarkSaved() { _savedRevision = _state.Revision; Changed?.Invoke(this, EventArgs.Empty); }

    public void Add(Annotation annotation) => Change(Image, [.. Annotations, annotation]);
    public void Replace(Annotation annotation)
    {
        if (!Annotations.Any(item => item.Id == annotation.Id)) return;
        Change(Image, Annotations.Select(item => item.Id == annotation.Id ? annotation : item).ToArray());
    }
    public void Remove(Guid id)
    {
        if (Annotations.Any(item => item.Id == id)) Change(Image, Annotations.Where(item => item.Id != id).ToArray());
    }
    public void MoveLayer(Guid id, bool forward)
    {
        var items = Annotations.ToList();
        int from = items.FindIndex(item => item.Id == id);
        int to = Math.Clamp(from + (forward ? 1 : -1), 0, items.Count - 1);
        if (from < 0 || from == to) return;
        (items[from], items[to]) = (items[to], items[from]);
        Change(Image, items.ToArray());
    }

    public void Crop(Int32Rect crop)
    {
        if (crop.Width < 1 || crop.Height < 1 || crop.X < 0 || crop.Y < 0 || (long)crop.X + crop.Width > Width || (long)crop.Y + crop.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(crop), "Crop must be inside the image.");
        if (crop == new Int32Rect(0, 0, Width, Height)) return;
        int stride = checked(crop.Width * 4);
        byte[] pixels = new byte[checked(stride * crop.Height)];
        Image.CopyPixels(crop, pixels, stride, 0);
        var image = BitmapSource.Create(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        image.Freeze();
        var bounds = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
        // Text may extend beyond its layout box; keep it conservatively. Other
        // objects use painted bounds, including round caps and arrowhead wings.
        Change(image, Annotations.Where(a => a.Tool == EditorTool.Text || bounds.IntersectsWith(a.RenderedBounds))
            .Select(a => a.Translate(new Vector(-crop.X, -crop.Y))).ToArray());
    }

    public void Resize(int width, int height)
    {
        ValidateDimensions(width, height);
        if (width == Width && height == Height) return;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen()) context.DrawImage(Image, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var before = new Rect(0, 0, Width, Height);
        var after = new Rect(0, 0, width, height);
        Change(bitmap, Annotations.Select(a => a.Transform(before, after, scaleStyle: true)).ToArray());
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Add(_state);
        _state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Add(_state);
        _state = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public BitmapSource Export() => AnnotationRenderer.Render(Image, Annotations);

    private void Change(BitmapSource image, Annotation[] annotations)
    {
        _undo.Add(_state);
        _redo.Clear();
        _state = new State(image, annotations, Guid.NewGuid());
        TrimHistory();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TrimHistory()
    {
        // Annotation edits share immutable bitmaps; crop/resize states consume the image budget.
        while (_undo.Count > 64) _undo.RemoveAt(0);
        const long budget = 256L * 1024 * 1024;
        while (_undo.Count > 1 && _undo.Select(s => s.Image).Append(Image).Distinct().Sum(b => (long)b.PixelWidth * b.PixelHeight * 4) > budget)
            _undo.RemoveAt(0);
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > 64_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Images must be between 1 and 16,384 pixels per side and at most 64 megapixels.");
    }
}
