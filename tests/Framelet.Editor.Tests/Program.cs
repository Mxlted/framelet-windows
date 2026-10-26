using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet.App.Editor;

internal static class Program
{
    private static int _passed, _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        Check("DPI is normalized without changing physical pixels", () =>
        {
            var document = new ImageDocument(Solid(32, 24, Colors.AliceBlue, 192));
            Equal(32, document.Width); Equal(24, document.Height); Equal(96d, document.Image.DpiX);
            Equal(Colors.AliceBlue, Pixel(document.Export(), 4, 6));
        });
        Check("Undo, redo, saved revision and branch invalidation", () =>
        {
            var document = new ImageDocument(Solid(64, 64, Colors.White));
            var first = Box(new Rect(4, 4, 20, 20), Colors.Red);
            document.Add(first); True(document.IsDirty); document.MarkSaved(); True(!document.IsDirty);
            document.Add(Box(new Rect(30, 30, 10, 10), Colors.Blue)); document.Undo(); True(!document.IsDirty);
            document.Redo(); True(document.IsDirty); document.Undo();
            document.Replace(first with { Color = Colors.Green }); True(!document.CanRedo);
            Equal(Colors.Green, Pixel(document.Export(), 12, 12));
        });
        Check("Unsaved captures remain dirty until explicitly saved", () =>
        {
            var document = new ImageDocument(Solid(32, 24, Colors.White), isSaved: false);
            True(document.IsDirty);
            document.Add(Box(new Rect(2, 2, 10, 10), Colors.Red)); document.Undo(); True(document.IsDirty);
            document.MarkSaved(); True(!document.IsDirty);
        });
        Check("Crop keeps annotations editable, translated and undoable", () =>
        {
            var document = new ImageDocument(Solid(100, 80, Colors.White));
            document.Add(Box(new Rect(30, 20, 10, 10), Colors.Red));
            document.Crop(new Int32Rect(20, 10, 50, 40));
            Equal(50, document.Width); Equal(new Point(10, 10), document.Annotations[0].Start);
            Equal(Colors.Red, Pixel(document.Export(), 15, 15));
            document.Undo(); Equal(100, document.Width); Equal(new Point(30, 20), document.Annotations[0].Start);
        });
        Check("Resize scales source, geometry and style together", () =>
        {
            var document = new ImageDocument(Solid(100, 80, Colors.White));
            document.Add(Box(new Rect(10, 20, 30, 20), Colors.Blue) with { StrokeWidth = 4 });
            document.Resize(200, 160);
            Equal(200, document.Width); Equal(new Point(20, 40), document.Annotations[0].Start);
            Equal(8d, document.Annotations[0].StrokeWidth);
            Equal(Colors.Blue, Pixel(document.Export(), 30, 50));
        });
        Check("Pixelation averages partial edge blocks and preserves alpha", () =>
        {
            byte[] pixels = [0, 0, 0, 255, 100, 100, 100, 255, 200, 200, 200, 255];
            PixelEffects.Pixelate(pixels, 3, 1, 2);
            Equal((byte)50, pixels[0]); Equal((byte)50, pixels[4]); Equal((byte)200, pixels[8]); Equal((byte)255, pixels[11]);
        });
        Check("Blur preserves constant colors, alpha and image edges", () =>
        {
            byte[] pixels = Enumerable.Repeat(new byte[] { 70, 90, 110, 255 }, 35).SelectMany(x => x).ToArray();
            byte[] expected = pixels.ToArray();
            PixelEffects.Blur(pixels, 7, 5, 12);
            True(pixels.SequenceEqual(expected));
        });
        Check("Blur changes a high contrast region without changing outside pixels", () =>
        {
            var document = new ImageDocument(Solid(40, 20, Colors.Black));
            document.Add(Box(new Rect(10, 0, 20, 20), Colors.White));
            document.Add(new Annotation { Tool = EditorTool.Blur, Start = new Point(5, 0), End = new Point(35, 20), EffectSize = 6 });
            var image = document.Export();
            Equal(Colors.Black, Pixel(image, 0, 10));
            byte channel = Pixel(image, 10, 10).R;
            True(channel > 0 && channel < 255);
        });
        Check("Pixelation composes earlier annotations, not just the source", () =>
        {
            var document = new ImageDocument(Solid(20, 20, Colors.Black));
            document.Add(Box(new Rect(0, 0, 10, 20), Colors.Red) with { StrokeWidth = 0 });
            document.Add(new Annotation { Tool = EditorTool.Pixelate, Start = new Point(0, 0), End = new Point(20, 20), EffectSize = 20 });
            var sample = Pixel(document.Export(), 15, 15);
            True(sample.R is > 100 and < 160); Equal((byte)0, sample.G); Equal((byte)255, sample.A);
        });
        Check("Arrow orientation survives translation and resize", () =>
        {
            var arrow = new Annotation { Tool = EditorTool.Arrow, Start = new Point(90, 40), End = new Point(10, 10) };
            var changed = arrow.Transform(arrow.Bounds, new Rect(20, 30, 160, 60));
            Equal(new Point(180, 90), changed.Start); Equal(new Point(20, 30), changed.End);
            True(changed.HitTest(new Point(100, 60), 2)); True(!changed.HitTest(new Point(30, 80), 2));
        });
        Check("Freehand hit testing follows the stroke", () =>
        {
            var pen = new Annotation { Tool = EditorTool.Pen, Points = [new Point(0, 0), new Point(10, 10), new Point(20, 0)] };
            True(pen.HitTest(new Point(5, 5), 1)); True(!pen.HitTest(new Point(10, 0), 1));
        });
        Check("Object ordering, duplicate identity and deletion preserve undo", () =>
        {
            var document = new ImageDocument(Solid(40, 40, Colors.White));
            var red = Box(new Rect(5, 5, 20, 20), Colors.Red);
            var blue = Box(new Rect(5, 5, 20, 20), Colors.Blue);
            document.Add(red); document.Add(blue); Equal(Colors.Blue, Pixel(document.Export(), 10, 10));
            document.MoveLayer(red.Id, true); Equal(Colors.Red, Pixel(document.Export(), 10, 10));
            document.Remove(red.Id); Equal(Colors.Blue, Pixel(document.Export(), 10, 10));
            document.Undo(); Equal(Colors.Red, Pixel(document.Export(), 10, 10));
        });
        Check("Invalid dimensions and out-of-bounds crop are rejected", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => ImageDocument.ValidateDimensions(0, 10));
            Throws<ArgumentOutOfRangeException>(() => ImageDocument.ValidateDimensions(16000, 16000));
            var document = new ImageDocument(Solid(20, 20, Colors.Black));
            Throws<ArgumentOutOfRangeException>(() => document.Crop(new Int32Rect(15, 15, 10, 10)));
        });
        Check("Text, shapes, pen and highlight render with transparent source", () =>
        {
            var document = new ImageDocument(Solid(400, 200, Colors.Transparent));
            document.Add(new Annotation { Tool = EditorTool.Text, Start = new Point(20, 20), End = new Point(220, 80), Text = "Framelet", FontSize = 30, Color = Colors.Black });
            document.Add(new Annotation { Tool = EditorTool.Ellipse, Start = new Point(250, 20), End = new Point(330, 100), Color = Colors.Blue, Filled = true });
            document.Add(new Annotation { Tool = EditorTool.Pen, Points = [new Point(20, 130), new Point(180, 130)], Color = Colors.Green, StrokeWidth = 6 });
            document.Add(new Annotation { Tool = EditorTool.Highlight, Start = new Point(250, 120), End = new Point(330, 170), Color = Colors.Yellow });
            var image = document.Export(); Equal(Colors.Blue, Pixel(image, 290, 60)); Equal(Colors.Green, Pixel(image, 100, 130));
            True(Pixel(image, 280, 140).A is > 50 and < 130); Equal((byte)0, Pixel(image, 390, 190).A);
        });
        Check("History remains bounded after many object edits", () =>
        {
            var document = new ImageDocument(Solid(10, 10, Colors.Black));
            var box = Box(new Rect(1, 1, 3, 3), Colors.Red); document.Add(box);
            for (int i = 0; i < 100; i++) document.Replace(box with { StrokeWidth = i + 1 });
            int count = 0; while (document.CanUndo) { document.Undo(); count++; }
            Equal(64, count);
        });
        Check("Pending text edits are dirty before losing keyboard focus and commit on export", () =>
        {
            var editor = new ImageEditorView();
            editor.LoadImage(Solid(200, 100, Colors.White), "fixture.png");
            var surface = (EditorSurface)editor.FindName("Surface");
            var text = new Annotation { Tool = EditorTool.Text, Start = new Point(10, 10), End = new Point(160, 60), Text = "Before" };
            surface.Document!.Add(text); surface.Select(text.Id); editor.MarkSaved(); True(!editor.IsDirty);
            ((System.Windows.Controls.TextBox)editor.FindName("AnnotationTextBox")).Text = "After";
            True(editor.IsDirty); editor.ExportImage();
            Equal("After", surface.Document.Annotations[0].Text);
            editor.MarkSaved(); True(!editor.IsDirty);
        });
        if (args.Length == 2 && args[0] == "--render") Check("Editor layout renders with a generated sample image", () => RenderEditor(args[1]));
        if (args.Length == 2 && args[0] == "--sample") Check("Generate a local sample image for UI review", () => WritePng(SampleImage(), args[1]));
        Console.WriteLine($"{_passed} passed; {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static Annotation Box(Rect bounds, Color color) => new() { Tool = EditorTool.Rectangle, Start = bounds.TopLeft, End = bounds.BottomRight, Color = color, Filled = true };
    private static BitmapSource Solid(int width, int height, Color color, double dpi = 96)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = color.A; }
        var bitmap = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze(); return bitmap;
    }
    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixel = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static void Check(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void True(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; received {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void RenderEditor(string path)
    {
        var app = Application.Current ?? new Application();
        foreach (var item in new Dictionary<string, string> { ["BackgroundBrush"] = "#1A1B1E", ["SurfaceBrush"] = "#222326", ["RaisedBrush"] = "#2E3034", ["BorderBrush"] = "#393B40", ["TextBrush"] = "#ECEDEF", ["MutedBrush"] = "#A3A5AC", ["AccentBrush"] = "#E0B36B", ["SelectionBrush"] = "#3A3832" })
            app.Resources[item.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Value));
        var editor = new ImageEditorView { Width = 1240, Height = 820 };
        editor.LoadImage(SampleImage());
        editor.Measure(new Size(1240, 820)); editor.Arrange(new Rect(0, 0, 1240, 820)); editor.UpdateLayout();
        var rendered = new RenderTargetBitmap(1240, 820, 96, 96, PixelFormats.Pbgra32); rendered.Render(editor);
        WritePng(rendered, path);
    }

    private static void WritePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static BitmapSource SampleImage()
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            void Text(string text, double x, double y, double size = 14, string color = "#303237", bool semibold = false)
                => context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(semibold ? "Segoe UI Semibold" : "Segoe UI"), size, Brush(color), 1), new Point(x, y));
            void Line(double x1, double y1, double x2, double y2) => context.DrawLine(new System.Windows.Media.Pen(Brush("#DEDFDC"), 1), new Point(x1, y1), new Point(x2, y2));
            void Rectangle(double x, double y, double width, double height, string fill, double radius = 0)
                => context.DrawRoundedRectangle(Brush(fill), null, new Rect(x, y, width, height), radius, radius);
            Rectangle(0, 0, 1120, 700, "#FBFBF9");
            Rectangle(0, 0, 1120, 62, "#FFFFFF");
            Rectangle(0, 62, 190, 604, "#F1F2EE");
            Line(0, 62, 1120, 62); Line(190, 62, 190, 666); Line(0, 666, 1120, 666);
            Rectangle(22, 17, 28, 28, "#3F554A", 5);
            Text("r", 30, 15, 23, "#FFFFFF", true);
            Text("ridge", 62, 18, 19, "#282B2B", true);
            Text("WORKSPACE", 219, 25, 10, "#888C87", true);
            Text("/   desktop-preview", 301, 21, 14);
            Rectangle(963, 17, 134, 28, "#F1F3EF", 4);
            Text("●  Local workspace", 975, 23, 11, "#536A5B");
            Text("PROJECT", 23, 91, 10, "#858B84", true);
            Text("desktop-preview", 23, 116, 15, "#2F3431", true);
            Text("Overview", 28, 169, 13, "#717771");
            Rectangle(14, 204, 161, 36, "#DEE6DD", 4);
            Text("Builds", 28, 213, 13, "#334C3A", true);
            Text("Artifacts", 28, 257, 13, "#717771");
            Text("Diagnostics", 28, 301, 13, "#717771");
            Text("Settings", 28, 345, 13, "#717771");
            Text("main", 25, 617, 12, "#717771");
            Text("Builds   /   #218", 223, 90, 12, "#7D827C");
            Text("Windows desktop", 222, 126, 28, "#262C29", true);
            Rectangle(491, 130, 100, 28, "#E4EEE2", 4);
            Text("✓  Completed", 503, 136, 12, "#3B6943", true);
            Text("release/x64    ·    a8c02f1    ·    Triggered manually", 223, 172, 13, "#7D827C");
            Line(223, 215, 1086, 215);
            Text("TARGET", 223, 234, 10, "#8A8E88", true); Text("win-x64", 223, 255, 15);
            Text("CONFIGURATION", 443, 234, 10, "#8A8E88", true); Text("Release", 443, 255, 15);
            Text("DURATION", 663, 234, 10, "#8A8E88", true); Text("16.0 seconds", 663, 255, 15);
            Text("OUTPUT", 883, 234, 10, "#8A8E88", true); Text("Portable package", 883, 255, 15);
            Line(223, 296, 1086, 296);
            Text("Build steps", 223, 324, 17, "#303733", true);
            Text("5 of 5 completed", 620, 328, 12, "#7D827C");
            string[] steps = ["Restore packages", "Compile application", "Validate assets", "Bundle release", "Write checksums"];
            string[] times = ["2.1 s", "8.5 s", "0.4 s", "4.8 s", "0.2 s"];
            for (int index = 0; index < steps.Length; index++)
            {
                double y = 369 + index * 43;
                context.DrawEllipse(Brush("#DFEADB"), null, new Point(233, y + 10), 10, 10);
                Text("✓", 227, y, 13, "#477548", true);
                Text(steps[index], 257, y, 14);
                Text(times[index], 679, y + 2, 12, "#7D827C");
                if (index < steps.Length - 1) Line(257, y + 31, 721, y + 31);
            }
            Line(759, 324, 759, 575);
            Text("Artifacts", 793, 324, 17, "#303733", true);
            Rectangle(793, 367, 292, 72, "#F0F2ED", 4);
            Text("desktop-preview-win-x64.zip", 807, 381, 13, "#3A463D", true);
            Text("68.4 MB   ·   Portable application", 807, 405, 11, "#7D827C");
            Rectangle(793, 451, 292, 72, "#F0F2ED", 4);
            Text("SHA256SUMS.txt", 807, 465, 13, "#3A463D", true);
            Text("192 bytes   ·   Verification", 807, 489, 11, "#7D827C");
            Rectangle(223, 602, 126, 36, "#3F554A", 4);
            Text("View output", 250, 611, 13, "#FFFFFF", true);
            Text("Open artifacts  ↗", 374, 611, 13, "#57665A");
            Text("Demo data", 22, 678, 11, "#949A92");
            Text("All checks completed successfully", 223, 678, 11, "#697D68");
            Text("No remote connection", 971, 678, 11, "#949A92");
        }
        var image = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32); image.Render(drawing); image.Freeze();
        return image;
    }
}
