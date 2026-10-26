using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet.Core;

namespace Framelet.App.Capture;

internal sealed class SelectionSession
{
    private readonly IReadOnlyList<CaptureTarget> _monitors;
    private readonly IReadOnlyList<CaptureTarget>? _windows;
    private readonly PixelRect _desktop;
    private readonly BitmapSource _snapshot;
    private readonly List<SelectionWindow> _overlays = [];
    private readonly TaskCompletionSource<CaptureTarget?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer;
    private CaptureNative.Point _origin, _cursor;
    private CaptureTarget? _hoverWindow;
    private bool _dragging, _finished;
    internal PixelRect? Selection { get; private set; }
    internal bool IsWindowSelection => _windows is not null;
    internal bool IsDragging => _dragging;
    internal CaptureNative.Point Cursor => _cursor;
    internal string? WindowTitle => _hoverWindow?.Title;

    internal SelectionSession(IReadOnlyList<CaptureTarget> monitors, PixelRect desktop, BitmapSource snapshot, IReadOnlyList<CaptureTarget>? windows)
    {
        _monitors = monitors;
        _desktop = desktop;
        _snapshot = snapshot;
        _windows = windows;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => UpdateCursor();
    }

    internal async Task<CaptureTarget?> SelectAsync()
    {
        try
        {
            foreach (CaptureTarget monitor in _monitors)
            {
                var cropped = new CroppedBitmap(_snapshot, new Int32Rect(monitor.Bounds.X - _desktop.X, monitor.Bounds.Y - _desktop.Y, monitor.Bounds.Width, monitor.Bounds.Height));
                cropped.Freeze();
                var window = new SelectionWindow(this, monitor.Bounds, cropped);
                _overlays.Add(window);
                window.Show();
            }
            UpdateCursor();
            _overlays.FirstOrDefault(x => CaptureService.Contains(x.MonitorBounds, _cursor.X, _cursor.Y))?.Activate();
            _timer.Start();
            return await _completion.Task;
        }
        finally
        {
            _finished = true;
            _timer.Stop();
            Mouse.Capture(null);
            foreach (var window in _overlays) window.Close();
            _overlays.Clear();
            CaptureNative.DwmFlush();
        }
    }

    private void UpdateCursor()
    {
        if (_finished || !CaptureNative.GetPhysicalCursorPos(out var position)) return;
        position.X = Math.Clamp(position.X, _desktop.X, _desktop.X + _desktop.Width - 1);
        position.Y = Math.Clamp(position.Y, _desktop.Y, _desktop.Y + _desktop.Height - 1);
        bool changed = _cursor.X != position.X || _cursor.Y != position.Y || Selection is null;
        PixelRect? previousSelection = Selection;
        _cursor = position;
        if (IsWindowSelection)
        {
            _hoverWindow = _windows!.FirstOrDefault(x => CaptureService.Contains(x.Bounds, position.X, position.Y));
            Selection = _hoverWindow is null ? null : CaptureService.Intersect(_hoverWindow.Bounds, _desktop);
        }
        else if (_dragging)
        {
            int left = Math.Min(_origin.X, position.X), top = Math.Min(_origin.Y, position.Y);
            int width = Math.Abs(position.X - _origin.X) + 1, height = Math.Abs(position.Y - _origin.Y) + 1;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                int edge = Math.Min(width, height);
                left = position.X < _origin.X ? _origin.X - edge + 1 : _origin.X;
                top = position.Y < _origin.Y ? _origin.Y - edge + 1 : _origin.Y;
                width = height = edge;
            }
            Selection = new(left, top, width, height);
        }
        if (changed || previousSelection != Selection) Redraw();
    }

    internal void MouseDown(UIElement source)
    {
        UpdateCursor();
        if (IsWindowSelection)
        {
            if (_hoverWindow is not null) Finish(_hoverWindow);
            return;
        }
        _origin = _cursor;
        _dragging = true;
        Selection = new(_origin.X, _origin.Y, 0, 0);
        Mouse.Capture(source);
        Redraw();
    }

    internal void MouseUp()
    {
        if (!_dragging) return;
        UpdateCursor();
        _dragging = false;
        Mouse.Capture(null);
        if (Selection is { Width: > 1, Height: > 1 } bounds)
            Finish(new CaptureTarget(CaptureKind.Region, bounds, Title: $"Region · {bounds.Width} × {bounds.Height}"));
        else { Selection = null; Redraw(); }
    }

    internal void KeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { Finish(null); args.Handled = true; }
        else if (args.Key == Key.Enter && IsWindowSelection && _hoverWindow is not null) { Finish(_hoverWindow); args.Handled = true; }
        else if (args.Key == Key.Enter && Selection is { Width: > 1, Height: > 1 } bounds)
        {
            Finish(new CaptureTarget(CaptureKind.Region, bounds, Title: $"Region · {bounds.Width} × {bounds.Height}"));
            args.Handled = true;
        }
        else if (args.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10 : 1;
            int x = _cursor.X + (args.Key == Key.Left ? -step : args.Key == Key.Right ? step : 0);
            int y = _cursor.Y + (args.Key == Key.Up ? -step : args.Key == Key.Down ? step : 0);
            CaptureNative.SetPhysicalCursorPos(x, y);
            UpdateCursor();
            args.Handled = true;
        }
        else if (args.Key == Key.Space && !IsWindowSelection && !_dragging)
        {
            _origin = _cursor;
            _dragging = true;
            Selection = new(_origin.X, _origin.Y, 0, 0);
            Redraw();
            args.Handled = true;
        }
    }

    internal void Finish(CaptureTarget? target)
    {
        if (_finished) return;
        _finished = true;
        _completion.TrySetResult(target);
    }

    private void Redraw() { foreach (var window in _overlays) window.Surface.InvalidateVisual(); }
}

internal sealed class SelectionWindow : Window
{
    internal PixelRect MonitorBounds { get; }
    internal SelectionSurface Surface { get; }

    internal SelectionWindow(SelectionSession session, PixelRect bounds, BitmapSource background)
    {
        MonitorBounds = bounds;
        Title = "Framelet capture selection";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        Opacity = 0;
        Width = Math.Max(1, bounds.Width);
        Height = Math.Max(1, bounds.Height);
        Content = Surface = new SelectionSurface(session, bounds, background);
        SourceInitialized += (_, _) =>
        {
            Place();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x007E) Dispatcher.BeginInvoke(() => session.Finish(null)); // Display topology changed.
                return 0;
            });
        };
        Loaded += (_, _) => { Place(); Opacity = 1; Surface.Focus(); };
        PreviewKeyDown += (_, e) => session.KeyDown(e);
        PreviewMouseLeftButtonDown += (_, e) => { session.MouseDown(Surface); e.Handled = true; };
        PreviewMouseLeftButtonUp += (_, e) => { session.MouseUp(); e.Handled = true; };
        PreviewMouseRightButtonDown += (_, e) => { session.Finish(null); e.Handled = true; };
        Closed += (_, _) => session.Finish(null);
    }

    private void Place()
    {
        using var awareness = new CaptureNative.PhysicalPixels();
        CaptureNative.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1), MonitorBounds.X, MonitorBounds.Y,
            MonitorBounds.Width, MonitorBounds.Height, CaptureNative.SetWindowNoActivate | CaptureNative.SetWindowShow);
    }
}

internal sealed class SelectionSurface : FrameworkElement
{
    private static readonly Brush Dim = FrozenBrush(Color.FromArgb(112, 0, 0, 0));
    private static readonly Brush Hud = FrozenBrush(Color.FromRgb(30, 30, 32));
    private static readonly Brush Subtle = FrozenBrush(Color.FromRgb(183, 183, 188));
    private static readonly Brush Accent = FrozenBrush(Color.FromRgb(236, 190, 105));
    private readonly SelectionSession _session;
    private readonly PixelRect _monitor;
    private readonly BitmapSource _background;

    internal SelectionSurface(SelectionSession session, PixelRect monitor, BitmapSource background)
    {
        _session = session;
        _monitor = monitor;
        _background = background;
        Focusable = true;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        double sx = ActualWidth / _monitor.Width, sy = ActualHeight / _monitor.Height;
        var canvas = new Rect(0, 0, ActualWidth, ActualHeight);
        drawing.DrawImage(_background, canvas);
        drawing.DrawRectangle(Dim, null, canvas);
        double x = (_session.Cursor.X - _monitor.X) * sx, y = (_session.Cursor.Y - _monitor.Y) * sy;
        bool cursorHere = CaptureService.Contains(_monitor, _session.Cursor.X, _session.Cursor.Y);
        PixelRect? selection = _session.Selection;
        if (selection is { Width: > 0, Height: > 0 } chosen)
        {
            Rect local = new((chosen.X - _monitor.X) * sx, (chosen.Y - _monitor.Y) * sy, chosen.Width * sx, chosen.Height * sy);
            drawing.PushClip(new RectangleGeometry(Rect.Intersect(local, canvas)));
            drawing.DrawImage(_background, canvas);
            drawing.Pop();
            drawing.DrawRectangle(null, new Pen(Brushes.Black, 3), local);
            drawing.DrawRectangle(null, new Pen(Accent, 1), local);
            if (cursorHere)
            {
                string dimensions = $"{chosen.Width} × {chosen.Height} px";
                string title = _session.IsWindowSelection ? _session.WindowTitle ?? "Window" : dimensions;
                DrawHud(drawing, title, _session.IsWindowSelection ? $"{dimensions}    Click to capture" : "Release to capture", x + 20, y + 24, canvas);
            }
        }
        else if (cursorHere)
        {
            drawing.DrawLine(new Pen(Subtle, 0.5), new Point(x, 0), new Point(x, ActualHeight));
            drawing.DrawLine(new Pen(Subtle, 0.5), new Point(0, y), new Point(ActualWidth, y));
            DrawHud(drawing, _session.IsWindowSelection ? "Select a window" : "Select an area", _session.IsWindowSelection ? "Click a window to capture" : $"{_session.Cursor.X}, {_session.Cursor.Y} px", x + 20, y + 24, canvas);
        }

        if (cursorHere)
        {
            string help = _session.IsWindowSelection
                ? "WINDOW CAPTURE     Click to select     Esc to cancel"
                : "REGION CAPTURE     Drag to select     Shift for square     Arrows for precision     Esc to cancel";
            var text = Text(help, 12, Brushes.White);
            double width = Math.Min(text.Width + 32, ActualWidth - 32);
            var pill = new Rect(Math.Max(16, (ActualWidth - width) / 2), Math.Max(16, ActualHeight - 64), width, 38);
            drawing.DrawRoundedRectangle(Hud, new Pen(FrozenBrush(Color.FromRgb(69, 69, 72)), 1), pill, 5, 5);
            drawing.PushClip(new RectangleGeometry(pill));
            drawing.DrawText(text, new Point(pill.X + 16, pill.Y + 11));
            drawing.Pop();
        }
    }

    private void DrawHud(DrawingContext drawing, string title, string subtitle, double x, double y, Rect canvas)
    {
        var main = Text(title, 13, Brushes.White);
        main.MaxTextWidth = Math.Max(80, Math.Min(350, canvas.Width - 56));
        main.Trimming = TextTrimming.CharacterEllipsis;
        var secondary = Text(subtitle, 11, Subtle);
        double width = Math.Max(main.Width, secondary.Width) + 24;
        width = Math.Min(width, Math.Max(80, canvas.Width - 24));
        x = Math.Clamp(x, 12, Math.Max(12, canvas.Width - width - 12));
        y = Math.Clamp(y, 12, Math.Max(12, canvas.Height - 124));
        drawing.DrawRoundedRectangle(Hud, new Pen(FrozenBrush(Color.FromRgb(69, 69, 72)), 1), new Rect(x, y, width, 54), 5, 5);
        drawing.DrawText(main, new Point(x + 12, y + 9));
        drawing.DrawText(secondary, new Point(x + 12, y + 30));
    }

    private FormattedText Text(string text, double size, Brush brush) => new(text, CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush FrozenBrush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
}
