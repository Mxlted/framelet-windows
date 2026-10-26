using System.Text.Json;

namespace Framelet.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "Dark";
    public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Framelet");
    public string FilenamePattern { get; set; } = "Framelet_{date}_{time}";
    public bool CopyAfterCapture { get; set; }
    public bool SaveAfterCapture { get; set; }
    public bool OpenEditorAfterCapture { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int CaptureDelaySeconds { get; set; }
    public string RegionHotkey { get; set; } = "Ctrl+Shift+2";
    public string WindowHotkey { get; set; } = "Ctrl+Shift+3";
    public string MonitorHotkey { get; set; } = "Ctrl+Shift+4";
    public string RecordHotkey { get; set; } = "Ctrl+Shift+5";
    public string EditorColor { get; set; } = "#F0B45A";
    public double EditorStroke { get; set; } = 4;
    public RecordingSettings Recording { get; set; } = new();

    public void Validate()
    {
        if (RegionHotkey is null || WindowHotkey is null || MonitorHotkey is null || RecordHotkey is null)
            throw new ArgumentException("Hotkey values must be strings; use an empty string to disable one.");
        if (EditorColor is null || !System.Text.RegularExpressions.Regex.IsMatch(EditorColor, "^#[0-9A-Fa-f]{6}$"))
            throw new ArgumentException("The editor color must be a six-digit hex color.");
        if (!double.IsFinite(EditorStroke) || EditorStroke is < 1 or > 64)
            throw new ArgumentException("Editor stroke width must be between 1 and 64 pixels.");
        if (string.IsNullOrWhiteSpace(OutputDirectory) || !Path.IsPathFullyQualified(OutputDirectory))
            throw new ArgumentException("Choose an absolute output folder.");
        if (Theme is not ("Dark" or "Light" or "System")) throw new ArgumentException("Choose a valid theme.");
        if (CaptureDelaySeconds is < 0 or > 10) throw new ArgumentException("Capture delay must be between 0 and 10 seconds.");
        OutputPaths.ValidatePattern(FilenamePattern);
        Recording.Validate();
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Path { get; } = path;
    public string? LoadWarning { get; private set; }

    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions)
                ?? throw new InvalidDataException("Settings file is empty.");
            if (value.Recording is null) throw new InvalidDataException("Recording settings are missing.");
            value.Validate();
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidDataException)
        {
            LoadWarning = $"Settings could not be read. Defaults are in use. Your file was preserved at {Path}. {ex.Message}";
            return new();
        }
    }

    public void Save(AppSettings value)
    {
        value.Validate();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static AppSettings Clone(AppSettings value) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;
}
