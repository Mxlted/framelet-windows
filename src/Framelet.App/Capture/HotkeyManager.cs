using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Framelet.App.Capture;

public sealed record HotkeyBinding(string Id, string Gesture, Action Callback);

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
        => Apply([new(id, gesture, callback)]);

    /// <summary>Updates only the specified actions. Reserve new pairs before saving or releasing any old pair.</summary>
    public string? Apply(IReadOnlyList<HotkeyBinding> changes, Action? persist = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _source.Dispatcher.VerifyAccess();
        var desired = _registrations.ToDictionary(x => x.Key, x => (x.Value.Modifiers, x.Value.Key, x.Value.Action), StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change.Callback);
            if (!ids.Add(change.Id)) return "Each action can only be configured once.";
            if (string.IsNullOrWhiteSpace(change.Gesture)) { desired.Remove(change.Id); continue; }
            if (!TryParse(change.Gesture, out uint modifiers, out uint key, out string? error)) return error;
            desired[change.Id] = (modifiers, key, change.Callback);
        }
        if (desired.Values.Select(x => (x.Modifiers, x.Key)).Distinct().Count() != desired.Count)
            return "Each action needs a different hotkey (equivalent modifier/key combinations count as duplicates).";

        var existing = _registrations.Values.ToDictionary(x => (x.Modifiers, x.Key), x => x.NativeId);
        var reserved = new Dictionary<(uint Modifiers, uint Key), int>();
        bool committed = false;
        try
        {
            foreach (var pair in desired.Values.Select(x => (x.Modifiers, x.Key)))
            {
                if (existing.ContainsKey(pair)) continue; // Swaps only change the callback owning an existing pair.
                int nativeId = NextNativeId(reserved.Values);
                if (!CaptureNative.RegisterHotKey(_source.Handle, nativeId, pair.Modifiers | 0x4000, pair.Key))
                {
                    var gesture = changes.First(x => TryParse(x.Gesture, out var m, out var k, out _) && (m, k) == pair).Gesture;
                    return $"{gesture} is already used by Windows or another application. Your previous shortcuts and settings were kept.";
                }
                reserved.Add(pair, nativeId);
            }
            var next = desired.ToDictionary(x => x.Key, x =>
            {
                var pair = (x.Value.Modifiers, x.Value.Key);
                return (NativeId: existing.TryGetValue(pair, out int id) ? id : reserved[pair], x.Value.Modifiers, x.Value.Key, x.Value.Action);
            }, StringComparer.Ordinal);
            // If persistence fails, no previous native registration or callback has changed.
            persist?.Invoke();
            foreach (var old in _registrations.Values)
                if (!next.Values.Any(x => x.NativeId == old.NativeId)) CaptureNative.UnregisterHotKey(_source.Handle, old.NativeId);
            _registrations.Clear();
            foreach (var item in next) _registrations.Add(item.Key, item.Value);
            committed = true;
            return null;
        }
        finally
        {
            if (!committed) foreach (int id in reserved.Values) CaptureNative.UnregisterHotKey(_source.Handle, id);
        }
    }

    private int NextNativeId(IEnumerable<int> reserved)
    {
        // Application hotkey identifiers are restricted to 0x0000..0xBFFF.
        for (int attempt = 0; attempt < 0x7F00; attempt++)
        {
            _nextId = _nextId >= 0xBFFF ? 0x4100 : _nextId + 1;
            if (!_registrations.Values.Any(x => x.NativeId == _nextId) && !reserved.Contains(_nextId)) return _nextId;
        }
        throw new InvalidOperationException("No hotkey identifiers are available.");
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
        foreach (var registration in _registrations)
        {
            var item = registration.Value;
            if (item.NativeId != (int)wParam) continue;
            handled = true;
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                // A queued keypress must not invoke a removed, reassigned, or disposed action.
                if (!_disposed && _registrations.TryGetValue(registration.Key, out var current) && current == item)
                    item.Action();
            }));
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
