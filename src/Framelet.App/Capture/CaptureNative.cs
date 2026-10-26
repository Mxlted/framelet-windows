using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Framelet.Core;

namespace Framelet.App.Capture;

internal static class CaptureNative
{
    internal const uint Srccopy = 0x00CC0020;
    internal const uint CaptureBlt = 0x40000000;
    internal const uint SetWindowNoActivate = 0x0010;
    internal const uint SetWindowShow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect ToPixels() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
        public uint Color;
    }

    internal delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    internal delegate bool WindowCallback(nint window, nint data);

    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static extern int DwmGetWindowInt(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] buffer, ref BitmapInfo info, uint usage);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);

    internal static Win32Exception Error(string operation) => new(Marshal.GetLastWin32Error(), operation);

    internal static PixelRect WindowBounds(nint window)
    {
        if (DwmGetWindowAttribute(window, 9, out Rect rect, Marshal.SizeOf<Rect>()) == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
            return rect.ToPixels();
        if (!GetWindowRect(window, out rect)) throw Error("Cannot read the selected window bounds.");
        return rect.ToPixels();
    }

    internal static string WindowTitle(nint window)
    {
        var title = new StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    // Native coordinates must remain physical even if an embedding host changes its thread context.
    internal readonly struct PhysicalPixels : IDisposable
    {
        private readonly nint _previous;
        public PhysicalPixels() => _previous = SetThreadDpiAwarenessContext(new nint(-4));
        public void Dispose() { if (_previous != 0) SetThreadDpiAwarenessContext(_previous); }
    }
}
