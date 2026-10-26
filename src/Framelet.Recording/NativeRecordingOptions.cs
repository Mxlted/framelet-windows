using System.Runtime.InteropServices;
using Framelet.Core;
using ScreenRecorderLib;

namespace Framelet.Recording;

internal static class NativeRecordingOptions
{
    internal static bool WindowIsAvailable(nint handle) => IsWindow(handle) && !IsIconic(handle);

    public static RecorderOptions Create(RecordingRequest request)
    {
        request.Settings.Validate();
        var settings = request.Settings;
        var target = request.Target;
        var width = target.Bounds.Width & ~1;
        var height = target.Bounds.Height & ~1;
        if (width < 2 || height < 2 || width > 16384 || height > 16384)
            throw new ArgumentException("Choose a recording area between 2 and 16384 pixels in each dimension.");

        List<RecordingSourceBase> sources = [];
        if (target.Kind == CaptureKind.Window)
        {
            if (target.WindowHandle == 0 || !IsWindow(target.WindowHandle) || IsIconic(target.WindowHandle))
                throw new ArgumentException("The recording window is unavailable or minimized.");
            sources.Add(new WindowRecordingSource(target.WindowHandle)
            {
                IsCursorCaptureEnabled = settings.IncludeCursor,
                IsBorderRequired = true,
                OutputSize = new ScreenSize(width, height),
                Position = new ScreenPoint(0, 0),
                Stretch = StretchMode.Uniform,
                AnchorPoint = Anchor.Center
            });
        }
        else
        {
            // Compose in physical desktop coordinates. A crop may cross display origins,
            // gaps, or DPI boundaries; only intersections are fed to the GPU compositor.
            foreach (var monitor in EnumerateMonitors())
            {
                var left = Math.Max(target.Bounds.X, monitor.Bounds.X);
                var top = Math.Max(target.Bounds.Y, monitor.Bounds.Y);
                var right = Math.Min(target.Bounds.X + width, monitor.Bounds.Right);
                var bottom = Math.Min(target.Bounds.Y + height, monitor.Bounds.Bottom);
                if (right <= left || bottom <= top) continue;
                sources.Add(new DisplayRecordingSource(monitor.DeviceName)
                {
                    RecorderApi = RecorderApi.WindowsGraphicsCapture,
                    IsCursorCaptureEnabled = settings.IncludeCursor,
                    IsBorderRequired = true,
                    SourceRect = new ScreenRect(left - monitor.Bounds.X, top - monitor.Bounds.Y, right - left, bottom - top),
                    OutputSize = new ScreenSize(right - left, bottom - top),
                    Position = new ScreenPoint(left - target.Bounds.X, top - target.Bounds.Y),
                    AnchorPoint = Anchor.TopLeft,
                    Stretch = StretchMode.Fill
                });
            }
            if (sources.Count == 0) throw new ArgumentException("The recording area does not intersect a connected display.");
        }

        List<AudioSourceBase> audio = [];
        if (settings.SystemAudio)
        {
            var device = string.IsNullOrWhiteSpace(settings.SystemAudioDeviceId)
                ? NativeAudioDevices.GetOutputs().FirstOrDefault(d => d.IsDefault)
                : NativeAudioDevices.GetOutputs().FirstOrDefault(d => d.Id == settings.SystemAudioDeviceId);
            if (device is null) throw new InvalidOperationException("The selected system audio device is unavailable. Choose another output device or turn off system audio.");
            audio.Add(new LoopbackAudioSource(device.Id));
        }
        if (settings.Microphone)
        {
            var device = string.IsNullOrWhiteSpace(settings.MicrophoneDeviceId)
                ? NativeAudioDevices.GetInputs().FirstOrDefault(d => d.IsDefault)
                : NativeAudioDevices.GetInputs().FirstOrDefault(d => d.Id == settings.MicrophoneDeviceId);
            if (device is null) throw new InvalidOperationException("The selected microphone is unavailable. Connect it, choose another microphone, or turn off microphone recording.");
            audio.Add(new CaptureAudioSource(device.Id));
        }

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = sources },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(width, height),
                Stretch = StretchMode.Fill,
                IsVideoFramePreviewEnabled = false
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audio.Count > 0,
                AudioSources = audio,
                Channels = AudioChannels.Stereo,
                Bitrate = (AudioBitrate)(settings.AudioBitrate / 8)
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = settings.FrameRate,
                Bitrate = settings.VideoBitrate,
                IsFixedFramerate = true,
                IsHardwareEncodingEnabled = settings.HardwareEncoding,
                IsThrottlingDisabled = false,
                IsLowLatencyEnabled = false,
                IsMp4FastStartEnabled = true,
                IsFragmentedMp4Enabled = false,
                Encoder = new H264VideoEncoder
                {
                    EncoderProfile = H264Profile.High,
                    BitrateMode = H264BitrateControlMode.UnconstrainedVBR
                }
            },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = settings.IncludeCursor, IsMouseClicksDetected = false },
            SnapshotOptions = new SnapshotOptions { SnapshotsWithVideo = false },
            LogOptions = new LogOptions
            {
                IsLogEnabled = Environment.GetEnvironmentVariable("FRAMELET_NATIVE_LOG") is not null,
                LogFilePath = Environment.GetEnvironmentVariable("FRAMELET_NATIVE_LOG") ?? "",
                LogSeverityLevel = LogLevel.Debug
            }
        };
    }

    internal sealed record MonitorInfo(string DeviceName, PixelRect Bounds);

    internal static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        List<MonitorInfo> monitors = [];
        MonitorEnum callback = (nint monitor, nint dc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
                monitors.Add(new(info.Device, new(info.Monitor.Left, info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top)));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new System.ComponentModel.Win32Exception();
        return monitors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInformation
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorEnum(nint monitor, nint dc, ref NativeRect bounds, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
