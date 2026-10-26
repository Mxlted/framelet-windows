using System.Globalization;

namespace Framelet.Media;

public enum ClipExportMode { ExactMp4, LosslessTrim, CompressedMp4, WebM }

public sealed class ClipProcessor
{
    public async Task ExportAsync(string input, string output, double start, double end, ClipExportMode mode,
        string ffmpegPath, CancellationToken ct = default)
    {
        if (!File.Exists(input)) throw new FileNotFoundException("The source clip is missing.", input);
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a new output filename to preserve the source clip.");
        if (File.Exists(output)) throw new IOException("The output file already exists. Choose a new filename.");
        var args = BuildArguments(input, start, end, mode);
        var exe = await FfmpegLocator.VerifyAsync(ffmpegPath, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "." + Guid.NewGuid().ToString("N") + ".partial" + Path.GetExtension(output));
        try
        {
            var result = await ProcessRunner.RunAsync(exe, args.Concat([temporary]), ct);
            if (result.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length < 256)
                throw new IOException("Clip export failed. " + result.Output[^Math.Min(2000, result.Output.Length)..]);
            File.Move(temporary, output, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static IReadOnlyList<string> BuildArguments(string input, double start, double end, ClipExportMode mode)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
            throw new ArgumentException("The end of the trim must be after its start.");
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-ss", start.ToString("F3", CultureInfo.InvariantCulture), "-i", input, "-t", (end - start).ToString("F3", CultureInfo.InvariantCulture), "-map", "0:v:0", "-map", "0:a?", "-map_metadata", "-1"];
        switch (mode)
        {
            case ClipExportMode.LosslessTrim: args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]); break;
            case ClipExportMode.WebM: args.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-c:a", "libopus"]); break;
            default: args.AddRange(["-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "medium", "-crf", mode == ClipExportMode.CompressedMp4 ? "28" : "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k"]); break;
        }
        if (mode != ClipExportMode.WebM) args.AddRange(["-movflags", "+faststart"]);
        return args;
    }
}
