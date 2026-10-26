using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet.App.Capture;
using Framelet.Core;

namespace Framelet.Capture.Tests;

internal static class Program
{
    private static int _passed, _failed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--probe-hotkey", var probe]) return HotkeyTransactionChecks.Child(probe, false);
        if (args is ["--hold-hotkey", var held]) return HotkeyTransactionChecks.Child(held, true);
        if (args.Contains("--select-region", StringComparer.Ordinal)) return RunOverlaySession(false);
        if (args.Contains("--select-window", StringComparer.Ordinal)) return RunOverlaySession(true);
        Check("negative-origin intersection retains physical coordinates", () =>
        {
            Equal(new PixelRect(-100, 30, 60, 40), CaptureService.Intersect(new(-100, 30, 80, 90), new(-120, 20, 80, 50)));
        });
        Check("monitor right and bottom edges are exclusive", () =>
        {
            if (CaptureService.Contains(new(-100, 0, 100, 100), 0, 10)) throw new Exception("Right edge belongs to the next display.");
            if (!CaptureService.Contains(new(-100, 0, 100, 100), -100, 0)) throw new Exception("Top-left pixel must be included.");
        });
        Check("disjoint rectangles have no area", () =>
        {
            var result = CaptureService.Intersect(new(0, 0, 10, 10), new(20, 20, 10, 10));
            Equal(0, result.Width); Equal(0, result.Height);
        });
        Check("frozen snapshot crop preserves exact source pixels", () =>
        {
            byte[] pixels = new byte[4 * 4 * 4];
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                int offset = (y * 4 + x) * 4; pixels[offset] = (byte)x; pixels[offset + 1] = (byte)y; pixels[offset + 2] = 173;
            }
            var source = BitmapSource.Create(4, 4, 144, 144, PixelFormats.Bgr32, null, pixels, 16);
            var result = CaptureService.CropSnapshot(source, new(-4, -4, 4, 4), new(-3, -2, 2, 2));
            byte[] cropped = new byte[16]; result.CopyPixels(cropped, 8, 0);
            Equal(2, result.PixelWidth); Equal(2, result.PixelHeight); Equal((byte)1, cropped[0]); Equal((byte)2, cropped[1]);
            Equal((byte)2, cropped[12]); Equal((byte)3, cropped[13]); Equal(96.0, result.DpiX);
            if (!result.IsFrozen) throw new Exception("Result should be safe to pass across threads.");
        });
        Check("empty capture is rejected before native allocation", () => Expect<ArgumentException>(() => CaptureService.CapturePixels(new(0, 0, 0, 10))));
        Check("frozen window selection uses its original snapshot even after target closes", () =>
        {
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[] { 1, 2, 3, 0 }, 4);
            source.Freeze();
            var target = new CaptureTarget(CaptureKind.Window, new(-100, -100, 1, 1), 0);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            try
            {
                typeof(CaptureService).GetField("_selectedTarget", flags)!.SetValue(null, target);
                typeof(CaptureService).GetField("_selectedImage", flags)!.SetValue(null, source);
                if (!ReferenceEquals(source, CaptureService.Capture(target))) throw new Exception("Frozen snapshot was recaptured.");
                Expect<InvalidOperationException>(() => CaptureService.Capture(target));
            }
            finally { CaptureService.DiscardSelectionSnapshot(); }
        });
        Check("oversized capture is rejected before native allocation", () => Expect<InvalidOperationException>(() => CaptureService.CapturePixels(new(0, 0, 100000, 100000))));
        Check("shortcut modifiers and virtual key are parsed", () =>
        {
            if (!HotkeyManager.TryParse("Ctrl+Shift+F8", out uint modifiers, out uint key, out string? error)) throw new Exception(error);
            Equal(6u, modifiers); Equal(0x77u, key);
        });
        Check("invalid shortcut is rejected with explanation", () =>
        {
            if (HotkeyManager.TryParse("this is not a shortcut", out _, out _, out string? error) || string.IsNullOrWhiteSpace(error)) throw new Exception("Invalid shortcut accepted.");
        });

        HotkeyTransactionChecks.Run(Check);

        if (args.Contains("--desktop", StringComparer.Ordinal))
        {
            WindowCaptureChecks.Run(Check);
            Check("live desktop enumeration and capture agree", () =>
            {
                var target = CaptureService.GetCurrentMonitor();
                var image = CaptureService.Capture(target);
                Equal(target.Bounds.Width, image.PixelWidth); Equal(target.Bounds.Height, image.PixelHeight);
                Console.WriteLine($"  Captured {image.PixelWidth} × {image.PixelHeight}; {CaptureService.GetMonitors().Count} display(s).");
            });
        }
        else Console.WriteLine("Desktop capture skipped; pass --desktop on an interactive Windows session.");

        Console.WriteLine($"{_passed} passed; {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception exception) { _failed++; Console.WriteLine($"FAIL {name}: {exception.Message}"); }
    }
    private static int RunOverlaySession(bool windowMode)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        timeout.Tick += (_, _) => { Console.Error.WriteLine("Interactive overlay test timed out."); app.Shutdown(2); };
        app.Startup += async (_, _) =>
        {
            try
            {
                Console.WriteLine("Opening capture selection.");
                timeout.Start();
                var selection = windowMode ? CaptureService.SelectWindowAsync() : CaptureService.SelectRegionAsync();
                Console.WriteLine($"Selection created {app.Windows.Count} windows.");
                // Expose tool windows to desktop UI test inventories without changing the production overlay.
                int number = 1;
                foreach (Window overlay in app.Windows) { overlay.ShowInTaskbar = true; overlay.Title += $" · display {number++}"; }
                var target = await selection;
                if (target is null) Console.WriteLine("Selection canceled cleanly.");
                else
                {
                    var image = CaptureService.Capture(target);
                    Console.WriteLine($"Selected {target.Bounds}; image {image.PixelWidth} × {image.PixelHeight}; frozen={image.IsFrozen}.");
                }
                timeout.Stop();
                app.Shutdown(0);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); app.Shutdown(1); }
        };
        return app.Run();
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
