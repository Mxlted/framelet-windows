using System.Globalization;
using System.Text.RegularExpressions;
using Framelet.Core;

namespace Framelet.Media;

public static partial class FfmpegArguments
{
    public static readonly string[] VideoCodecs = ["libx264", "h264_nvenc", "h264_qsv", "h264_amf", "libx265", "hevc_nvenc"];
    public static readonly string[] AudioCodecs = ["aac", "libmp3lame"];

    public static IReadOnlyList<string> Record(RecordingRequest request, string outputPath)
    {
        var s = request.Settings;
        s.Validate();
        if (!VideoCodecs.Contains(s.FfmpegVideoCodec) || !AudioCodecs.Contains(s.FfmpegAudioCodec))
            throw new ArgumentException("Choose a supported FFmpeg codec.");
        var b = request.Target.Bounds;
        if (b.Width < 2 || b.Height < 2) throw new ArgumentException("The recording area must be at least 2 × 2 pixels.");
        List<string> args = ["-hide_banner", "-y", "-thread_queue_size", "512", "-f", "gdigrab", "-framerate", s.FrameRate.ToString(CultureInfo.InvariantCulture), "-draw_mouse", s.IncludeCursor ? "1" : "0"];
        if (request.Target.Kind == CaptureKind.Window && request.Target.WindowHandle != 0)
            args.AddRange(["-i", "hwnd=" + request.Target.WindowHandle.ToInt64().ToString(CultureInfo.InvariantCulture)]);
        else
            args.AddRange(["-offset_x", b.X.ToString(CultureInfo.InvariantCulture), "-offset_y", b.Y.ToString(CultureInfo.InvariantCulture), "-video_size", $"{b.Width}x{b.Height}", "-i", "desktop"]);
        if (!string.IsNullOrWhiteSpace(s.FfmpegAudioDevice))
            args.AddRange(["-thread_queue_size", "512", "-f", "dshow", "-i", "audio=" + s.FfmpegAudioDevice, "-map", "0:v:0", "-map", "1:a:0", "-c:a", s.FfmpegAudioCodec, "-b:a", s.AudioBitrate.ToString(CultureInfo.InvariantCulture), "-af", "aresample=async=1:first_pts=0"]);
        else args.Add("-an");
        args.AddRange(["-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", s.FfmpegVideoCodec, "-b:v", s.VideoBitrate.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p"]);
        if (s.FfmpegVideoCodec is "libx264" or "libx265") args.AddRange(["-preset", "veryfast"]);
        args.AddRange(ParseAdvanced(s.FfmpegExtraArguments));
        args.AddRange(["-movflags", "+faststart", outputPath]);
        return args;
    }

    // Encoding-only arguments keep advanced settings from replacing inputs, output files or protocols.
    public static IReadOnlyList<string> ParseAdvanced(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length % 2 != 0) throw new ArgumentException("Advanced parameters must be option/value pairs, for example -crf 20 -preset slow.");
        string[] options = ["-crf", "-preset", "-tune", "-profile:v", "-level:v", "-g", "-bf", "-cq", "-qp", "-maxrate", "-bufsize"];
        for (var i = 0; i < tokens.Length; i += 2)
            if (!options.Contains(tokens[i]) || !SafeValue().IsMatch(tokens[i + 1]))
                throw new ArgumentException("Advanced parameters support encoding options only: " + string.Join(", ", options));
        return tokens;
    }

    [GeneratedRegex("^[a-zA-Z0-9_.:+-]+$")]
    private static partial Regex SafeValue();
}
