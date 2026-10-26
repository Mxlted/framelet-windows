using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Framelet.App.Editor;

// One worker and one replaceable pending request per surface. WPF visuals stay on the
// dispatcher; only frozen bitmaps and immutable annotation snapshots cross threads.
internal sealed class EffectPreviewRenderer(Dispatcher dispatcher) : IDisposable
{
    internal const int MaximumPixels = 1_000_000;
    private sealed record Request(BitmapSource Source, Annotation Annotation, double Scale, long Version);
    private readonly object _gate = new();
    private Request? _requested, _pending;
    private CancellationTokenSource? _active;
    private bool _running, _disposed;
    private long _version;
    private Task _work = Task.CompletedTask;

    public EffectPatch? Result { get; private set; }
    public Exception? Error { get; private set; }
    public event Action? Changed;
    internal Task PendingWork { get { lock (_gate) return _work; } }

    public void RequestPreview(BitmapSource source, Annotation annotation, double displayScale)
    {
        dispatcher.VerifyAccess();
        if (!source.IsFrozen) throw new ArgumentException("Preview sources must be frozen.", nameof(source));
        if (annotation.Tool is not (EditorTool.Blur or EditorTool.Pixelate)) throw new ArgumentException("Expected a pixel effect.", nameof(annotation));
        if (!double.IsFinite(displayScale) || displayScale <= 0) throw new ArgumentOutOfRangeException(nameof(displayScale));
        lock (_gate)
        {
            if (_disposed) return;
            if (_requested is { } previous && ReferenceEquals(previous.Source, source) && previous.Annotation == annotation && previous.Scale == displayScale) return;
            Result = null;
            Error = null;
            _requested = _pending = new Request(source, annotation, displayScale, ++_version);
            _active?.Cancel();
            if (!_running)
            {
                _running = true;
                _work = Task.Run(ProcessPendingAsync);
            }
        }
    }

    private async Task ProcessPendingAsync()
    {
        while (true)
        {
            // Coalesce bursts to at most about 30 requests/second, without postponing
            // indefinitely when the pointer keeps moving.
            await Task.Delay(33).ConfigureAwait(false);
            Request request;
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_disposed || _pending == null) { _running = false; return; }
                request = _pending;
                _pending = null;
                _active = cancellation = new CancellationTokenSource();
            }
            EffectPatch? result = null;
            Exception? error = null;
            try { result = Process(request.Source, request.Annotation, request.Scale, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception) { error = exception; }
            bool publish;
            lock (_gate)
            {
                publish = !_disposed && request.Version == _version && !cancellation.IsCancellationRequested;
                _active = null;
                cancellation.Dispose();
            }
            if (publish && !dispatcher.HasShutdownStarted)
            {
                _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    lock (_gate)
                    {
                        if (_disposed || request.Version != _version) return;
                        Result = result;
                        Error = error;
                    }
                    Changed?.Invoke();
                }));
            }
        }
    }

    internal static EffectPatch? Process(BitmapSource source, Annotation annotation, double displayScale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var region = AnnotationRenderer.EffectRegion(source, annotation.Bounds);
        if (region.IsEmpty) return null;
        double scale = Math.Min(1, Math.Min(displayScale, Math.Sqrt((double)MaximumPixels / ((long)region.Width * region.Height))));
        int width = Math.Max(1, (int)(region.Width * scale));
        int height = Math.Max(1, (int)(region.Height * scale));
        var cropped = new CroppedBitmap(source, region);
        BitmapSource scaled = width == region.Width && height == region.Height ? cropped :
            new TransformedBitmap(cropped, new ScaleTransform((double)width / region.Width, (double)height / region.Height));
        // WIC scaling runs on the worker too. The destination and effect scratch buffers
        // are capped even at 100% zoom; cancellation is checked between pixel scanlines.
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        scaled.CopyPixels(pixels, stride, 0);
        cancellationToken.ThrowIfCancellationRequested();
        int amount = Math.Max(1, (int)Math.Round(annotation.EffectSize * scale));
        PixelEffects.ApplyPreview(pixels, width, height, annotation.Tool, amount, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return new EffectPatch(bitmap, new Rect(region.X, region.Y, region.Width, region.Height));
    }

    public void Clear()
    {
        dispatcher.VerifyAccess();
        lock (_gate)
        {
            ++_version;
            _requested = _pending = null;
            Result = null;
            Error = null;
            _active?.Cancel();
        }
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        Clear();
        lock (_gate) _disposed = true;
        Changed = null;
        // Never wait for pixel processing on the UI thread. The single worker observes
        // cancellation and owns disposal of its token; queued callbacks check the version.
    }
}
