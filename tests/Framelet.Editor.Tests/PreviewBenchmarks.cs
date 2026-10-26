using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet.App.Editor;

internal static class PreviewBenchmarks
{
    internal static int Run()
    {
        const int width = 3840, height = 2160;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = (byte)x; pixels[i + 1] = (byte)y; pixels[i + 2] = (byte)(x + y); pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        var surface = new EditorSurface { Document = new ImageDocument(bitmap), Zoom = .1 };
        var render = typeof(EditorSurface).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var preview = typeof(EditorSurface).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Draw()
        {
            var visual = new DrawingVisual();
            using var context = visual.RenderOpen();
            context.PushTransform(new ScaleTransform(.1, .1));
            render.Invoke(surface, [context]);
            context.Pop();
        }
        Draw(); // Warm the full-resolution committed cache before measuring pointer previews.
        for (int i = 0; i < 6; i++)
        {
            preview.SetValue(surface, new Annotation { Tool = EditorTool.Blur, Start = new Point(i, 0), End = new Point(width, height), EffectSize = 12 });
            var watch = Stopwatch.StartNew(); Draw();
            double uiMilliseconds = watch.Elapsed.TotalMilliseconds;
            var worker = (EffectPreviewRenderer)typeof(EditorSurface).GetField("_effectPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
            EffectChecks.PumpUntil(() => worker.Result != null || worker.Error != null);
            watch.Stop();
            if (worker.Error != null) throw new InvalidOperationException("Preview benchmark failed.", worker.Error);
            double readyMilliseconds = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); Draw(); watch.Stop();
            Console.WriteLine($"4K blur preview, zoom 10%, sample {i + 1}: UI render {uiMilliseconds:F2} ms; preview ready {readyMilliseconds:F2} ms; cached UI draw {watch.Elapsed.TotalMilliseconds:F2} ms; {worker.Result!.Bitmap.PixelWidth}x{worker.Result.Bitmap.PixelHeight} preview pixels");
        }
        surface.CancelInteraction();
        surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        return 0;
    }
}
