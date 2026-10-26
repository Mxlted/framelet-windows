using System.Windows;
using System.Windows.Media;

namespace Framelet.App.Editor;

public enum EditorTool { Select, Arrow, Rectangle, Ellipse, Text, Pen, Highlight, Blur, Pixelate, Crop }

public sealed record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public EditorTool Tool { get; init; }
    public Point Start { get; init; }
    public Point End { get; init; }
    public Point[] Points { get; init; } = [];
    public Color Color { get; init; } = Color.FromRgb(241, 113, 88);
    public double StrokeWidth { get; init; } = 4;
    public double FontSize { get; init; } = 24;
    public string Text { get; init; } = "Text";
    public bool Filled { get; init; }
    public int EffectSize { get; init; } = 12;

    public Rect Bounds
    {
        get
        {
            if (Tool == EditorTool.Pen && Points.Length > 0)
            {
                double left = Points.Min(p => p.X), top = Points.Min(p => p.Y);
                return new Rect(left, top, Math.Max(1, Points.Max(p => p.X) - left), Math.Max(1, Points.Max(p => p.Y) - top));
            }
            return new Rect(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y),
                Math.Max(1, Math.Abs(End.X - Start.X)), Math.Max(1, Math.Abs(End.Y - Start.Y)));
        }
    }

    public Annotation Translate(Vector offset) => this with
    {
        Start = Start + offset, End = End + offset,
        Points = Points.Select(point => point + offset).ToArray()
    };

    public Annotation Transform(Rect before, Rect after, bool scaleStyle = false)
    {
        double scaleX = after.Width / Math.Max(1, before.Width);
        double scaleY = after.Height / Math.Max(1, before.Height);
        Point Map(Point p) => new(after.X + (p.X - before.X) * scaleX, after.Y + (p.Y - before.Y) * scaleY);
        double scale = Math.Sqrt(scaleX * scaleY);
        return this with
        {
            Start = Map(Start), End = Map(End), Points = Points.Select(Map).ToArray(),
            StrokeWidth = scaleStyle ? Math.Max(1, StrokeWidth * scale) : StrokeWidth,
            FontSize = Tool == EditorTool.Text ? Math.Clamp(FontSize * scale, 6, 512) : FontSize,
            EffectSize = scaleStyle ? Math.Clamp((int)Math.Round(EffectSize * scale), 2, 128) : EffectSize
        };
    }

    public bool HitTest(Point point, double tolerance)
    {
        var expanded = Bounds;
        expanded.Inflate(tolerance + StrokeWidth / 2, tolerance + StrokeWidth / 2);
        if (!expanded.Contains(point)) return false;
        if (Tool == EditorTool.Arrow)
            return SegmentDistance(point, Start, End) <= tolerance + StrokeWidth * 2;
        if (Tool == EditorTool.Pen)
            return Points.Length == 1 ? (point - Points[0]).Length <= tolerance + StrokeWidth :
                Points.Zip(Points.Skip(1)).Any(pair => SegmentDistance(point, pair.First, pair.Second) <= tolerance + StrokeWidth / 2);
        return true;
    }

    private static double SegmentDistance(Point point, Point start, Point end)
    {
        Vector line = end - start;
        double t = line.LengthSquared < .001 ? 0 : Math.Clamp(Vector.Multiply(point - start, line) / line.LengthSquared, 0, 1);
        return (point - (start + t * line)).Length;
    }
}
