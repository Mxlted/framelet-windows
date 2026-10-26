using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet.Core;

namespace Framelet.App.Capture;

public static class CaptureService
{
    private static CaptureTarget? _selectedTarget;
    private static BitmapSource? _selectedImage;
    private static bool _isSelecting;

    public static IReadOnlyList<CaptureTarget> GetMonitors()
    {
        using var awareness = new CaptureNative.PhysicalPixels();
        var monitors = new List<CaptureTarget>();
        CaptureNative.EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref CaptureNative.Rect rect, nint data) =>
        {
            var info = new CaptureNative.MonitorInfo { Size = Marshal.SizeOf<CaptureNative.MonitorInfo>(), Device = "" };
            if (CaptureNative.GetMonitorInfo(monitor, ref info))
                monitors.Add(new(CaptureKind.Monitor, info.Monitor.ToPixels(), 0, info.Device,
                    $"Display {monitors.Count + 1}{((info.Flags & 1) != 0 ? " · primary" : "")} · {rect.Right - rect.Left} × {rect.Bottom - rect.Top}"));
            return true;
        }, 0);
        if (monitors.Count == 0) throw new InvalidOperationException("No desktop displays are available for capture.");
        return monitors;
    }

    public static CaptureTarget GetDesktop()
    {
        var monitors = GetMonitors();
        int left = monitors.Min(x => x.Bounds.X), top = monitors.Min(x => x.Bounds.Y);
        int right = monitors.Max(x => x.Bounds.X + x.Bounds.Width), bottom = monitors.Max(x => x.Bounds.Y + x.Bounds.Height);
        return new(CaptureKind.Desktop, new(left, top, right - left, bottom - top), Title: "Entire desktop");
    }

    public static CaptureTarget GetCurrentMonitor()
    {
        CaptureNative.GetPhysicalCursorPos(out var cursor);
        return GetMonitors().FirstOrDefault(x => Contains(x.Bounds, cursor.X, cursor.Y)) ?? GetMonitors()[0];
    }

    public static CaptureTarget GetActiveWindow()
    {
        using var awareness = new CaptureNative.PhysicalPixels();
        nint handle = CaptureNative.GetForegroundWindow();
        CaptureNative.GetWindowThreadProcessId(handle, out uint processId);
        if (processId == Environment.ProcessId) return GetWindows().FirstOrDefault()
            ?? throw new InvalidOperationException("Switch to the window you want to capture, then use the window capture hotkey.");
        if (handle == 0 || CaptureNative.IsIconic(handle)) throw new InvalidOperationException("The active window is not available for capture.");
        return CreateWindowTarget(handle);
    }

    public static Task<CaptureTarget?> SelectRegionAsync() => SelectAsync(false);
    public static Task<CaptureTarget?> SelectWindowAsync() => SelectAsync(true);

    private static async Task<CaptureTarget?> SelectAsync(bool windowSelection)
    {
        Application.Current.Dispatcher.VerifyAccess();
        if (_isSelecting) throw new InvalidOperationException("A capture selection is already open.");
        _isSelecting = true;
        DiscardSelectionSnapshot();
        try
        {
            var desktop = GetDesktop();
            var snapshot = CapturePixels(desktop.Bounds);
            var session = new SelectionSession(GetMonitors(), desktop.Bounds, snapshot, windowSelection ? GetWindows() : null);
            CaptureTarget? target = await session.SelectAsync();
            if (target is not null)
            {
                _selectedTarget = target;
                _selectedImage = CropSnapshot(snapshot, desktop.Bounds, target.Bounds);
            }
            return target;
        }
        finally { _isSelecting = false; }
    }

    public static BitmapSource Capture(CaptureTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(_selectedTarget, target) && _selectedImage is not null)
        {
            var image = _selectedImage;
            DiscardSelectionSnapshot();
            return image;
        }
        DiscardSelectionSnapshot();
        if (target.Kind == CaptureKind.Window && (!CaptureNative.IsWindow(target.WindowHandle) || CaptureNative.IsIconic(target.WindowHandle)))
            throw new InvalidOperationException("The selected window has closed or is minimized.");
        return CapturePixels(Intersect(target.Bounds, GetDesktop().Bounds));
    }

    public static void DiscardSelectionSnapshot() { _selectedTarget = null; _selectedImage = null; }

    internal static IReadOnlyList<CaptureTarget> GetWindows()
    {
        using var awareness = new CaptureNative.PhysicalPixels();
        var windows = new List<CaptureTarget>();
        PixelRect desktop = GetDesktop().Bounds;
        CaptureNative.EnumWindows((window, _) =>
        {
            if (!CaptureNative.IsWindowVisible(window) || CaptureNative.IsIconic(window)) return true;
            CaptureNative.GetWindowThreadProcessId(window, out uint owner);
            if (owner == Environment.ProcessId) return true;
            if (CaptureNative.DwmGetWindowInt(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            string title = CaptureNative.WindowTitle(window);
            if (string.IsNullOrWhiteSpace(title)) return true;
            try
            {
                var target = CreateWindowTarget(window);
                if (Intersect(target.Bounds, desktop) is { Width: > 1, Height: > 1 }) windows.Add(target);
            }
            catch (Win32Exception) { /* A window can close during enumeration. */ }
            return true;
        }, 0);
        return windows;
    }

    private static CaptureTarget CreateWindowTarget(nint handle) =>
        new(CaptureKind.Window, CaptureNative.WindowBounds(handle), handle, Title: CaptureNative.WindowTitle(handle));

    internal static bool Contains(PixelRect bounds, int x, int y) => x >= bounds.X && y >= bounds.Y && x < bounds.X + bounds.Width && y < bounds.Y + bounds.Height;

    internal static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    internal static BitmapSource CropSnapshot(BitmapSource snapshot, PixelRect desktop, PixelRect region)
    {
        region = Intersect(region, desktop);
        if (region.Width < 1 || region.Height < 1) throw new ArgumentException("The selection is outside the desktop.");
        var image = new CroppedBitmap(snapshot, new Int32Rect(region.X - desktop.X, region.Y - desktop.Y, region.Width, region.Height));
        // Copy so a small snip does not retain a complete multi-monitor desktop in memory.
        int stride = checked(region.Width * 4);
        byte[] pixels = new byte[checked(stride * region.Height)];
        image.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    internal static BitmapSource CapturePixels(PixelRect bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1) throw new ArgumentException("Choose a nonempty area to capture.");
        long byteCount = (long)bounds.Width * bounds.Height * 4;
        if (byteCount > 512L * 1024 * 1024) throw new InvalidOperationException("This desktop is too large to capture at once. Select a smaller region or one monitor.");
        using var awareness = new CaptureNative.PhysicalPixels();
        nint source = CaptureNative.GetDC(0), destination = 0, bitmap = 0, old = 0;
        try
        {
            if (source == 0) throw CaptureNative.Error("Cannot access the desktop. Try again after unlocking Windows.");
            destination = CaptureNative.CreateCompatibleDC(source);
            if (destination == 0) throw CaptureNative.Error("Cannot create a capture device context.");
            bitmap = CaptureNative.CreateCompatibleBitmap(source, bounds.Width, bounds.Height);
            if (bitmap == 0) throw CaptureNative.Error("Cannot allocate the screenshot.");
            old = CaptureNative.SelectObject(destination, bitmap);
            if (old == 0 || old == -1) throw CaptureNative.Error("Cannot select the screenshot bitmap.");
            if (!CaptureNative.BitBlt(destination, 0, 0, bounds.Width, bounds.Height, source, bounds.X, bounds.Y, CaptureNative.Srccopy | CaptureNative.CaptureBlt))
                throw CaptureNative.Error("Windows could not capture the desktop. Protected content and the secure desktop cannot be captured.");
            CaptureNative.SelectObject(destination, old);
            old = 0;
            var info = new CaptureNative.BitmapInfo { Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32 };
            byte[] pixels = new byte[(int)byteCount];
            if (CaptureNative.GetDIBits(source, bitmap, 0, (uint)bounds.Height, pixels, ref info, 0) != bounds.Height)
                throw CaptureNative.Error("Windows returned an incomplete screenshot.");
            var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, bounds.Width * 4);
            image.Freeze();
            return image;
        }
        finally
        {
            if (old != 0 && destination != 0) CaptureNative.SelectObject(destination, old);
            if (bitmap != 0) CaptureNative.DeleteObject(bitmap);
            if (destination != 0) CaptureNative.DeleteDC(destination);
            if (source != 0) CaptureNative.ReleaseDC(0, source);
        }
    }
}
