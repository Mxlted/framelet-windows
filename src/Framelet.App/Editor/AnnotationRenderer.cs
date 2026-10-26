using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet.App.Editor;

public static class AnnotationRenderer
{
    public static BitmapSource Render(BitmapSource image, IEnumerable<Annotation> annotations)
    {
        var visual = new DrawingVisual();
        DrawingContext context = visual.RenderOpen();
        context.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        context.PushClip(new RectangleGeometry(new Rect(0, 0, image.PixelWidth, image.PixelHeight)));
        try
        {
            foreach (var annotation in annotations)
            {
                if (annotation.Tool is EditorTool.Blur or EditorTool.Pixelate)
                {
                    context.Pop();
                    context.Close();
                    var underneath = Rasterize(visual, image.PixelWidth, image.PixelHeight);
                    visual = new DrawingVisual();
                    context = visual.RenderOpen();
                    context.PushClip(new RectangleGeometry(new Rect(0, 0, image.PixelWidth, image.PixelHeight)));
                    Draw(context, annotation, underneath);
                }
                else Draw(context, annotation, image);
            }
            context.Pop();
        }
        finally { context.Close(); }
        return Rasterize(visual, image.PixelWidth, image.PixelHeight);
    }

    private static BitmapSource Rasterize(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Draw(DrawingContext context, Annotation annotation, BitmapSource underlying)
    {
        var brush = new SolidColorBrush(annotation.Color);
        brush.Freeze();
        var pen = new System.Windows.Media.Pen(brush, annotation.StrokeWidth)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        Rect bounds = annotation.Bounds;
        switch (annotation.Tool)
        {
            case EditorTool.Rectangle:
                context.DrawRectangle(annotation.Filled ? brush : null, pen, bounds);
                break;
            case EditorTool.Ellipse:
                context.DrawEllipse(annotation.Filled ? brush : null, pen,
                    new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2);
                break;
            case EditorTool.Arrow:
                Vector direction = annotation.End - annotation.Start;
                if (direction.Length < 1) return;
                direction.Normalize();
                Vector across = new(-direction.Y, direction.X);
                double head = Math.Min((annotation.End - annotation.Start).Length * .6, Math.Max(13, annotation.StrokeWidth * 3.5));
                Point stemEnd = annotation.End - direction * (head * .65);
                context.DrawLine(pen, annotation.Start, stemEnd);
                var triangle = new StreamGeometry();
                using (var geometry = triangle.Open())
                {
                    geometry.BeginFigure(annotation.End, true, true);
                    geometry.LineTo(annotation.End - direction * head + across * head * .48, true, false);
                    geometry.LineTo(annotation.End - direction * head - across * head * .48, true, false);
                }
                triangle.Freeze();
                context.DrawGeometry(brush, null, triangle);
                break;
            case EditorTool.Text:
                var formatted = FormatText(annotation);
                context.DrawText(formatted, bounds.TopLeft);
                break;
            case EditorTool.Pen:
                if (annotation.Points.Length == 1) context.DrawEllipse(brush, null, annotation.Points[0], annotation.StrokeWidth / 2, annotation.StrokeWidth / 2);
                else if (annotation.Points.Length > 1)
                {
                    var path = new StreamGeometry();
                    using (var geometry = path.Open())
                    {
                        geometry.BeginFigure(annotation.Points[0], false, false);
                        geometry.PolyLineTo(annotation.Points.Skip(1).ToArray(), true, false);
                    }
                    path.Freeze();
                    context.DrawGeometry(null, pen, path);
                }
                break;
            case EditorTool.Highlight:
                context.PushOpacity(.32);
                context.DrawRectangle(brush, null, bounds);
                context.Pop();
                break;
            case EditorTool.Blur:
            case EditorTool.Pixelate:
                DrawEffect(context, underlying, bounds, annotation.Tool, annotation.EffectSize);
                break;
        }
    }

    public static FormattedText FormatText(Annotation annotation) => new(annotation.Text.Length == 0 ? " " : annotation.Text,
        CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
        annotation.FontSize, new SolidColorBrush(annotation.Color), 1)
    {
        MaxTextWidth = Math.Max(4, annotation.Bounds.Width),
        Trimming = TextTrimming.None
    };

    public static Annotation FitTextBounds(Annotation annotation)
    {
        var formatted = FormatText(annotation);
        return annotation with { End = new Point(annotation.Bounds.Right, annotation.Bounds.Top + Math.Max(annotation.FontSize * 1.25, formatted.Height)) };
    }

    private static void DrawEffect(DrawingContext context, BitmapSource source, Rect requested, EditorTool tool, int amount)
    {
        var region = EffectRegion(source, requested);
        if (region.IsEmpty)
        {
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            return;
        }
        int width = region.Width, height = region.Height, stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        source.CopyPixels(region, pixels, stride, 0);
        if (tool == EditorTool.Pixelate) PixelEffects.Pixelate(pixels, width, height, amount);
        else PixelEffects.Blur(pixels, width, height, amount);
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        DrawReplacement(context, source, new EffectPatch(result, new Rect(region.X, region.Y, width, height)));
    }

    internal static Int32Rect EffectRegion(BitmapSource source, Rect requested)
    {
        // Clamp doubles before conversion, including off-image annotations retained by cropping.
        int x = (int)Math.Clamp(Math.Floor(requested.X), 0, source.PixelWidth);
        int y = (int)Math.Clamp(Math.Floor(requested.Y), 0, source.PixelHeight);
        int right = (int)Math.Clamp(Math.Ceiling(requested.Right), 0, source.PixelWidth);
        int bottom = (int)Math.Clamp(Math.Ceiling(requested.Bottom), 0, source.PixelHeight);
        return right <= x || bottom <= y ? Int32Rect.Empty : new Int32Rect(x, y, right - x, bottom - y);
    }

    internal static void DrawReplacement(DrawingContext context, BitmapSource source, EffectPatch patch)
    {
        var imageBounds = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
        // A processed pixel replaces all four source channels. Drawing it over the source
        // would source-over blend translucent pixels, leaking contrast and increasing alpha.
        context.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(imageBounds), new RectangleGeometry(patch.Bounds)));
        context.DrawImage(source, imageBounds);
        context.Pop();
        context.DrawImage(patch.Bitmap, patch.Bounds);
    }
}

internal sealed record EffectPatch(BitmapSource Bitmap, Rect Bounds);

public static class PixelEffects
{
    public static void Pixelate(byte[] pixels, int width, int height, int blockSize, CancellationToken cancellationToken = default)
        => PixelateCore(pixels, width, height, Math.Clamp(blockSize, 2, 128), cancellationToken);

    public static void Blur(byte[] pixels, int width, int height, int radius, CancellationToken cancellationToken = default)
        => BlurCore(pixels, width, height, Math.Clamp(radius, 2, 64), cancellationToken);

    internal static void ApplyPreview(byte[] pixels, int width, int height, EditorTool tool, int amount, CancellationToken cancellationToken)
    {
        // A full-resolution two-pixel effect can occupy only one preview pixel.
        if (tool == EditorTool.Pixelate) PixelateCore(pixels, width, height, Math.Clamp(amount, 1, 128), cancellationToken);
        else BlurCore(pixels, width, height, Math.Clamp(amount, 1, 64), cancellationToken);
    }

    private static void PixelateCore(byte[] pixels, int width, int height, int blockSize, CancellationToken cancellationToken)
    {
        Validate(pixels, width, height);
        for (int y = 0; y < height; y += blockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x += blockSize)
            {
                int xmax = Math.Min(width, x + blockSize), ymax = Math.Min(height, y + blockSize);
                int[] sum = new int[4];
                int count = (xmax - x) * (ymax - y);
                for (int yy = y; yy < ymax; yy++)
                    for (int xx = x; xx < xmax; xx++)
                        for (int channel = 0; channel < 4; channel++) sum[channel] += pixels[(yy * width + xx) * 4 + channel];
                for (int yy = y; yy < ymax; yy++)
                    for (int xx = x; xx < xmax; xx++)
                        for (int channel = 0; channel < 4; channel++) pixels[(yy * width + xx) * 4 + channel] = (byte)(sum[channel] / count);
            }
        }
    }

    private static void BlurCore(byte[] pixels, int width, int height, int radius, CancellationToken cancellationToken)
    {
        Validate(pixels, width, height);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] temporary = new byte[pixels.Length];
        // Three separable box passes approximate a Gaussian with linear cost per pixel.
        for (int pass = 0; pass < 3; pass++)
        {
            BoxPass(pixels, temporary, width, height, radius, horizontal: true, cancellationToken);
            BoxPass(temporary, pixels, width, height, radius, horizontal: false, cancellationToken);
        }
    }

    private static void BoxPass(byte[] input, byte[] output, int width, int height, int radius, bool horizontal, CancellationToken cancellationToken)
    {
        int length = horizontal ? width : height, lines = horizontal ? height : width;
        int diameter = radius * 2 + 1;
        int Index(int line, int offset, int channel) => (horizontal ? line * width + offset : offset * width + line) * 4 + channel;
        for (int line = 0; line < lines; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int channel = 0; channel < 4; channel++)
            {
                int sum = 0;
                for (int offset = -radius; offset <= radius; offset++) sum += input[Index(line, Math.Clamp(offset, 0, length - 1), channel)];
                for (int position = 0; position < length; position++)
                {
                    output[Index(line, position, channel)] = (byte)(sum / diameter);
                    sum -= input[Index(line, Math.Clamp(position - radius, 0, length - 1), channel)];
                    sum += input[Index(line, Math.Clamp(position + radius + 1, 0, length - 1), channel)];
                }
            }
        }
    }

    private static void Validate(byte[] pixels, int width, int height)
    {
        if (width < 1 || height < 1 || pixels.Length != checked(width * height * 4))
            throw new ArgumentException("The pixel buffer must contain four channels for each pixel.");
    }
}
