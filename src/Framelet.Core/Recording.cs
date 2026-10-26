namespace Framelet.Core;

public sealed class RecordingSettings
{
    public bool UseFfmpeg { get; set; }
    public int FrameRate { get; set; } = 60;
    public int VideoBitrate { get; set; } = 12_000_000;
    public int AudioBitrate { get; set; } = 192_000;
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    public string SystemAudioDeviceId { get; set; } = "";
    public string MicrophoneDeviceId { get; set; } = "";
    public bool IncludeCursor { get; set; } = true;
    public bool HardwareEncoding { get; set; } = true;
    public string FfmpegPath { get; set; } = "";
    public string FfmpegVideoCodec { get; set; } = "libx264";
    public string FfmpegAudioCodec { get; set; } = "aac";
    public string FfmpegExtraArguments { get; set; } = "";
    public string FfmpegAudioDevice { get; set; } = "";

    public void Validate()
    {
        if (FrameRate is < 1 or > 120) throw new ArgumentException("Frame rate must be between 1 and 120 FPS.");
        if (VideoBitrate is < 500_000 or > 100_000_000) throw new ArgumentException("Video bitrate must be between 0.5 and 100 Mbps.");
        if (AudioBitrate is not (96_000 or 128_000 or 160_000 or 192_000)) throw new ArgumentException("Choose a supported AAC bitrate.");
    }
}

public sealed record RecordingRequest(string OutputPath, CaptureTarget Target, RecordingSettings Settings);

public interface IRecordingBackend : IAsyncDisposable
{
    string Name { get; }
    bool CanPause { get; }
    event Action<string>? StatusChanged;
    event Action<Exception>? Failed;
    event Action? Completed;
    Task StartAsync(RecordingRequest request, CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task CancelAsync();
}

public static class RecordingBackendSelector
{
    public static IRecordingBackend Create(RecordingSettings settings,
        Func<IRecordingBackend> native, Func<IRecordingBackend> ffmpeg) => settings.UseFfmpeg ? ffmpeg() : native();
}
