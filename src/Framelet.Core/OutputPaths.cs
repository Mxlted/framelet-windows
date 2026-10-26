namespace Framelet.Core;

public static class OutputPaths
{
    public static void ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 100 ||
            pattern.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || pattern.Any(char.IsControl))
            throw new ArgumentException("Use a filename pattern without path separators or reserved characters (up to 100 characters).");
        var expanded = pattern.Replace("{date}", "2026-10-26").Replace("{time}", "12-30-00");
        if (expanded.Contains('{') || expanded.Contains('}')) throw new ArgumentException("Filename patterns support {date} and {time}.");
        if (expanded.Trim(' ', '.').Length == 0) throw new ArgumentException("The filename cannot be blank.");
        var stem = expanded.Split('.')[0].Trim().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9'))
            throw new ArgumentException("The filename uses a reserved Windows device name.");
    }

    public static string Next(string directory, string pattern, string extension, DateTime? now = null)
    {
        ValidatePattern(pattern);
        if (extension is not (".png" or ".jpg" or ".bmp" or ".mp4" or ".webm" or ".mkv" or ".gif"))
            throw new ArgumentException("Unsupported output extension.");
        Directory.CreateDirectory(directory);
        var time = now ?? DateTime.Now;
        var name = pattern.Replace("{date}", time.ToString("yyyy-MM-dd")).Replace("{time}", time.ToString("HH-mm-ss")).TrimEnd(' ', '.');
        for (var i = 0; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, name + (i == 0 ? "" : $"_{i:000}") + extension);
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Too many files with the same name. Choose a different pattern.");
    }
}
