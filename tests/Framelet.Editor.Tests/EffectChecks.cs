using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet.App.Editor;

internal static class EffectChecks
{
    internal static void Run(Action<string, Action> check)
    {
        foreach (var tool in new[] { EditorTool.Blur, EditorTool.Pixelate })
            foreach (byte alpha in new byte[] { 0, 128, 255 })
                check($"Rendered and PNG-exported {tool} replaces RGBA at alpha {alpha}", () =>
                {
                    var document = new ImageDocument(Pattern(12, 8, alpha));
                    var effect = Effect(tool, new Rect(2, 1, 8, 6));
                    byte[] expected = Bytes(document.Image);
                    ReferenceEffect(expected, 12, 8, effect);
                    document.Add(effect);
                    var output = document.Export();
                    EqualBytes(expected, Bytes(output));
                    using var stream = new MemoryStream();
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(output)); png.Save(stream);
                    stream.Position = 0;
                    var stored = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    EqualBytes(expected, Bytes(stored), tolerance: 1); // PNG uses straight-alpha channel rounding.
                });
        check("Two semitransparent contrasting pixels become one identical block without extra opacity", () =>
        {
            var document = new ImageDocument(Pattern(2, 1, 128));
            document.Add(Effect(EditorTool.Pixelate, new Rect(0, 0, 2, 1)));
            EqualBytes([64, 64, 64, 128, 64, 64, 64, 128], Bytes(document.Export()));
        });
        check("Overlapping blur and pixelation preserve alpha, clipping, earlier annotations and later layers", () =>
        {
            var document = new ImageDocument(Pattern(12, 10, 128));
            var earlier = new Annotation { Tool = EditorTool.Rectangle, Start = new Point(3, 2), End = new Point(7, 8), Filled = true, StrokeWidth = 0, Color = Color.FromArgb(128, 255, 0, 0) };
            document.Add(earlier);
            byte[] expected = Bytes(document.Export());
            foreach (var effect in new[] { Effect(EditorTool.Blur, new Rect(-2, 2, 10, 6)), Effect(EditorTool.Pixelate, new Rect(4, -2, 12, 8)) })
            {
                ReferenceEffect(expected, 12, 10, effect);
                document.Add(effect);
            }
            var later = earlier with { Id = Guid.NewGuid(), Start = new Point(6, 3), End = new Point(9, 5), Color = Colors.Blue };
            document.Add(later);
            var expectedImage = AnnotationRenderer.Render(Bitmap(expected, 12, 10), [later]);
            EqualBytes(Bytes(expectedImage), Bytes(document.Export()));
        });
        check("Effects replace mixed transparent and opaque alpha values", () =>
        {
            byte[] input = [0, 0, 0, 0, 128, 128, 128, 128, 255, 255, 255, 255, 0, 0, 0, 255];
            foreach (var tool in new[] { EditorTool.Blur, EditorTool.Pixelate })
            {
                var document = new ImageDocument(Bitmap(input, 4, 1));
                var effect = Effect(tool, new Rect(0, 0, 4, 1));
                byte[] expected = input.ToArray(); ReferenceEffect(expected, 4, 1, effect);
                document.Add(effect); EqualBytes(expected, Bytes(document.Export()));
            }
        });
        check("Background effect preview scales to visible resolution and caps its pixel buffers", () =>
        {
            var source = Pattern(1600, 1000, 128);
            var annotation = Effect(EditorTool.Blur, new Rect(0, 0, 1600, 1000));
            var small = Task.Run(() => EffectPreviewRenderer.Process(source, annotation, .1, default)).GetAwaiter().GetResult()!;
            Require(small.Bitmap.IsFrozen && small.Bitmap.PixelWidth == 160 && small.Bitmap.PixelHeight == 100);
            var large = Task.Run(() => EffectPreviewRenderer.Process(source, annotation, 8, default)).GetAwaiter().GetResult()!;
            Require((long)large.Bitmap.PixelWidth * large.Bitmap.PixelHeight <= EffectPreviewRenderer.MaximumPixels);
        });
        check("Unscaled preview replacement matches exact translucent committed output", () =>
        {
            var source = Pattern(40, 20, 128);
            foreach (var tool in new[] { EditorTool.Blur, EditorTool.Pixelate })
            {
                var effect = Effect(tool, new Rect(5, 2, 20, 16));
                var patch = Task.Run(() => EffectPreviewRenderer.Process(source, effect, 1, default)).GetAwaiter().GetResult()!;
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen()) AnnotationRenderer.DrawReplacement(drawing, source, patch);
                var rendered = new RenderTargetBitmap(40, 20, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
                EqualBytes(Bytes(AnnotationRenderer.Render(source, [effect])), Bytes(rendered));
            }
        });
        check("Preview bursts coalesce and only the latest geometry is published", () =>
        {
            using var renderer = new EffectPreviewRenderer(Dispatcher.CurrentDispatcher);
            var source = Pattern(120, 80, 128);
            int callbacks = 0; renderer.Changed += () => callbacks++;
            for (int x = 0; x < 30; x++) renderer.RequestPreview(source, Effect(EditorTool.Blur, new Rect(x, 0, 80, 80)), .5);
            PumpUntil(() => renderer.Result != null || renderer.Error != null);
            Require(renderer.Error == null && renderer.Result!.Bounds.X == 29 && callbacks == 1);
        });
        check("A completed but queued preview cannot publish after clear or disposal", () =>
        {
            foreach (bool dispose in new[] { false, true })
            {
                using var renderer = new EffectPreviewRenderer(Dispatcher.CurrentDispatcher);
                int callbacks = 0; renderer.Changed += () => callbacks++;
                renderer.RequestPreview(Pattern(120, 80, 128), Effect(EditorTool.Blur, new Rect(0, 0, 120, 80)), 1);
                Require(renderer.PendingWork.Wait(TimeSpan.FromSeconds(5)));
                if (dispose) renderer.Dispose(); else renderer.Clear();
                Pump();
                Require(renderer.Result == null && callbacks == 0);
            }
        });
        check("New source and zoom supersede a queued preview result", () =>
        {
            using var renderer = new EffectPreviewRenderer(Dispatcher.CurrentDispatcher);
            renderer.RequestPreview(Pattern(120, 80, 128), Effect(EditorTool.Blur, new Rect(0, 0, 120, 80)), 1);
            Require(renderer.PendingWork.Wait(TimeSpan.FromSeconds(5)));
            renderer.RequestPreview(Pattern(60, 40, 255), Effect(EditorTool.Pixelate, new Rect(0, 0, 60, 40)), .5);
            PumpUntil(() => renderer.Result != null || renderer.Error != null);
            Require(renderer.Error == null && renderer.Result!.Bitmap.PixelWidth == 30 && Bytes(renderer.Result.Bitmap)[3] == 255);
        });
        foreach (string change in new[] { "document", "revision", "selection", "tool", "zoom", "geometry", "unload" })
            check($"Surface invalidates queued preview after {change} changes", () => SurfaceInvalidation(change));
        check("Cancelled pixel processing exits before modifying its buffer", () =>
        {
            var pixels = Bytes(Pattern(20, 20, 128)); var expected = pixels.ToArray();
            foreach (var tool in new[] { EditorTool.Blur, EditorTool.Pixelate })
            {
                try { PixelEffects.ApplyPreview(pixels, 20, 20, tool, 12, new CancellationToken(true)); throw new Exception("Cancellation was ignored."); }
                catch (OperationCanceledException) { }
                EqualBytes(expected, pixels);
            }
        });
        check("Disposing a requested preview drains its worker without publishing", () =>
        {
            var renderer = new EffectPreviewRenderer(Dispatcher.CurrentDispatcher);
            int callbacks = 0; renderer.Changed += () => callbacks++;
            renderer.RequestPreview(Pattern(1600, 1000, 128), Effect(EditorTool.Blur, new Rect(0, 0, 1600, 1000)), 1);
            renderer.Dispose();
            Require(renderer.PendingWork.Wait(TimeSpan.FromSeconds(5)));
            Pump(); Require(renderer.Result == null && callbacks == 0);
        });
    }

    private static void SurfaceInvalidation(string change)
    {
        var surface = new EditorSurface { Document = new ImageDocument(Pattern(120, 80, 128)) };
        var preview = typeof(EditorSurface).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        preview.SetValue(surface, Effect(EditorTool.Blur, new Rect(0, 0, 120, 80)));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) typeof(EditorSurface).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [context]);
        var renderer = (EffectPreviewRenderer)typeof(EditorSurface).GetField("_effectPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
        Require(renderer.PendingWork.Wait(TimeSpan.FromSeconds(5)));
        switch (change)
        {
            case "document": surface.Document = new ImageDocument(Pattern(80, 40, 255)); break;
            case "revision": surface.Document!.Add(new Annotation { Tool = EditorTool.Rectangle }); break;
            case "selection": surface.Select(Guid.NewGuid()); break;
            case "tool": surface.Tool = EditorTool.Pixelate; break;
            case "zoom": surface.Zoom = .25; break;
            case "geometry":
                var latest = Effect(EditorTool.Blur, new Rect(20, 10, 40, 40));
                var published = new List<Rect>();
                renderer.Changed += () => { if (renderer.Result is { } result) published.Add(result.Bounds); };
                preview.SetValue(surface, latest);
                using (var context = visual.RenderOpen()) typeof(EditorSurface).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, [context]);
                PumpUntil(() => renderer.Result != null || renderer.Error != null);
                Require(renderer.Error == null && published.Count == 1 && published[0] == latest.Bounds);
                surface.CancelInteraction();
                break;
            case "unload": surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
        }
        Pump(); Require(renderer.Result == null);
        surface.CancelInteraction();
        surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    internal static void PumpUntil(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        while (!complete())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Preview worker did not complete.");
            Pump(); Thread.Sleep(1);
        }
    }

    internal static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static Annotation Effect(EditorTool tool, Rect bounds) => new() { Tool = tool, Start = bounds.TopLeft, End = bounds.BottomRight, EffectSize = 2 };
    private static BitmapSource Pattern(int width, int height, byte alpha)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte value = (i / 4 % 2 == 0) ? (byte)0 : alpha;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value; pixels[i + 3] = alpha;
        }
        return Bitmap(pixels, width, height);
    }
    private static BitmapSource Bitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4); bitmap.Freeze(); return bitmap;
    }
    private static byte[] Bytes(BitmapSource bitmap)
    {
        var converted = bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
    private static void EqualBytes(byte[] expected, byte[] actual, int tolerance = 0)
    {
        Require(expected.Length == actual.Length);
        for (int i = 0; i < expected.Length; i++)
            if (Math.Abs(expected[i] - actual[i]) > (i % 4 == 3 ? 0 : tolerance)) throw new Exception($"Pixel byte {i}: expected {expected[i]}, received {actual[i]}.");
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }

    // Deliberately simple reference filter for tiny fixtures, independent of the optimized
    // rolling-sum implementation. It calculates expected complete premultiplied pixels.
    private static void ReferenceEffect(byte[] image, int imageWidth, int imageHeight, Annotation effect)
    {
        int left = Math.Max(0, (int)Math.Floor(effect.Bounds.Left)), top = Math.Max(0, (int)Math.Floor(effect.Bounds.Top));
        int right = Math.Min(imageWidth, (int)Math.Ceiling(effect.Bounds.Right)), bottom = Math.Min(imageHeight, (int)Math.Ceiling(effect.Bounds.Bottom));
        int width = right - left, height = bottom - top;
        byte[] region = new byte[width * height * 4];
        for (int y = 0; y < height; y++) Array.Copy(image, ((top + y) * imageWidth + left) * 4, region, y * width * 4, width * 4);
        int amount = effect.EffectSize;
        if (effect.Tool == EditorTool.Pixelate)
        {
            for (int y = 0; y < height; y += amount)
                for (int x = 0; x < width; x += amount)
                    for (int channel = 0; channel < 4; channel++)
                    {
                        var offsets = (from yy in Enumerable.Range(y, Math.Min(amount, height - y)) from xx in Enumerable.Range(x, Math.Min(amount, width - x)) select (yy * width + xx) * 4 + channel).ToArray();
                        byte average = (byte)(offsets.Sum(i => region[i]) / offsets.Length);
                        foreach (int i in offsets) region[i] = average;
                    }
        }
        else
        {
            for (int pass = 0; pass < 6; pass++)
            {
                byte[] input = region.ToArray();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        for (int channel = 0; channel < 4; channel++)
                        {
                            int sum = 0;
                            for (int offset = -amount; offset <= amount; offset++)
                            {
                                int xx = pass % 2 == 0 ? Math.Clamp(x + offset, 0, width - 1) : x;
                                int yy = pass % 2 == 1 ? Math.Clamp(y + offset, 0, height - 1) : y;
                                sum += input[(yy * width + xx) * 4 + channel];
                            }
                            region[(y * width + x) * 4 + channel] = (byte)(sum / (amount * 2 + 1));
                        }
            }
        }
        for (int y = 0; y < height; y++) Array.Copy(region, y * width * 4, image, ((top + y) * imageWidth + left) * 4, width * 4);
    }
}
