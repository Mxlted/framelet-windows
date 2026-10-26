using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Framelet.App.Capture;
using Framelet.Core;

namespace Framelet.Capture.Tests;

internal static class WindowCaptureChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("delayed live window capture follows selected HWND after move and resize", () =>
        {
            var window = new Window { Width = 240, Height = 180, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.Red, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show();
                nint handle = new WindowInteropHelper(window).Handle;
                var desktop = CaptureService.GetCurrentMonitor().Bounds;
                using var awareness = new CaptureNative.PhysicalPixels();
                CaptureNative.SetWindowPos(handle, 0, desktop.X + 30, desktop.Y + 30, 240, 180, CaptureNative.SetWindowNoActivate);
                Pump();
                var target = new CaptureTarget(CaptureKind.Window, CaptureNative.WindowBounds(handle), handle);
                CaptureNative.SetWindowPos(handle, 0, desktop.X + 330, desktop.Y + 140, 360, 220, CaptureNative.SetWindowNoActivate);
                Pump();
                var refreshed = CaptureService.RefreshLiveTarget(target);
                Require(refreshed.WindowHandle == handle && refreshed.Bounds != target.Bounds, "Target did not follow its original HWND.");
                Require(refreshed.Bounds == CaptureNative.WindowBounds(handle), "Refreshed geometry differs from physical DWM bounds.");
                var bitmap = CaptureService.Capture(target);
                var expected = CaptureService.Intersect(refreshed.Bounds, CaptureService.GetDesktop().Bounds);
                Require(bitmap.PixelWidth == expected.Width && bitmap.PixelHeight == expected.Height, "Capture used old dimensions.");
                byte[] center = new byte[4]; bitmap.CopyPixels(new Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), center, 4, 0);
                Require(center[2] > 240 && center[1] < 10 && center[0] < 10, "Capture did not contain the moved synthetic red window.");
            }
            finally { window.Close(); }
        });
        check("delayed window target becoming minimized or closed is recoverable", () =>
        {
            var window = new Window { Width = 200, Height = 140, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show(); Pump();
                nint handle = new WindowInteropHelper(window).Handle;
                var target = new CaptureTarget(CaptureKind.Window, CaptureNative.WindowBounds(handle), handle);
                window.WindowState = WindowState.Minimized; Pump();
                Reject(() => CaptureService.Capture(target));
                window.Close();
                Reject(() => CaptureService.Capture(target));
            }
            finally { if (window.IsVisible) window.Close(); }
        });
    }
    private static void Pump() { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); CaptureNative.DwmFlush(); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unavailable window was captured."); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
