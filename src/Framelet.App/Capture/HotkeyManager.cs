using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Framelet.App.Capture;

public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<string, (int NativeId, uint Modifiers, uint Key, Action Action)> _registrations = new(StringComparer.Ordinal);
    private int _nextId = 0x4100;
    private bool _disposed;

    public HotkeyManager(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        nint handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("The hotkey host window is unavailable.");
        _source.AddHook(WndProc);
    }

    public string? Register(string id, string gesture, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (!TryParse(gesture, out uint modifiers, out uint key, out string? error)) return error;
        if (_registrations.TryGetValue(id, out var current) && current.Modifiers == modifiers && current.Key == key)
        {
            _registrations[id] = (current.NativeId, modifiers, key, callback);
            return null;
        }
        int nativeId = ++_nextId;
        if (!CaptureNative.RegisterHotKey(_source.Handle, nativeId, modifiers | 0x4000, key))
            return $"{gesture} is already used by Windows or another application. Choose a different shortcut.";
        if (_registrations.Remove(id, out var previous)) CaptureNative.UnregisterHotKey(_source.Handle, previous.NativeId);
        _registrations.Add(id, (nativeId, modifiers, key, callback));
        return null;
    }

    public void UnregisterAll()
    {
        foreach (var item in _registrations.Values) CaptureNative.UnregisterHotKey(_source.Handle, item.NativeId);
        _registrations.Clear();
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint key, out string? error)
    {
        modifiers = 0;
        key = 0;
        error = null;
        try
        {
            var parsed = (KeyGesture?)new KeyGestureConverter().ConvertFromInvariantString(gesture);
            if (parsed is null || parsed.Key == Key.None) { error = "Enter a shortcut such as Ctrl+Shift+1."; return false; }
            if ((parsed.Modifiers & ModifierKeys.Alt) != 0) modifiers |= 1;
            if ((parsed.Modifiers & ModifierKeys.Control) != 0) modifiers |= 2;
            if ((parsed.Modifiers & ModifierKeys.Shift) != 0) modifiers |= 4;
            if ((parsed.Modifiers & ModifierKeys.Windows) != 0) modifiers |= 8;
            key = (uint)KeyInterop.VirtualKeyFromKey(parsed.Key);
            return key != 0;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException or InvalidEnumArgumentException)
        {
            error = "Enter a valid shortcut such as Ctrl+Shift+1, Alt+F8, or PrintScreen.";
            return false;
        }
    }

    private nint WndProc(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312) return 0;
        foreach (var item in _registrations.Values)
        {
            if (item.NativeId != (int)wParam) continue;
            handled = true;
            _source.Dispatcher.BeginInvoke(item.Action);
            break;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
