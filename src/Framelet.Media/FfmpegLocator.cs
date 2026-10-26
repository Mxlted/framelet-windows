using System.Diagnostics;

namespace Framelet.Media;

public static class FfmpegLocator
{
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        var local = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static async Task<string> VerifyAsync(string? configuredPath, CancellationToken ct = default)
    {
        var path = Find(configuredPath) ?? throw new FileNotFoundException("FFmpeg was not found. Set its executable in Settings → Recording, or install it from the linked download page.");
        var result = await ProcessRunner.RunAsync(path, ["-hide_banner", "-version"], ct);
        if (result.ExitCode != 0 || !result.Output.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected executable did not identify itself as FFmpeg.");
        return path;
    }
}

public sealed record ProcessResult(int ExitCode, string Output);

public static class ProcessRunner
{
    public static ProcessStartInfo Create(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = Create(executable, arguments) };
        if (!process.Start()) throw new IOException("The media process could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        using var cancel = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(ct);
        return new(process.ExitCode, await output + await error);
    }
}
