using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Framelet.Core;
using Microsoft.Win32;

namespace Framelet.App;

public partial class App : Application
{
    private Mutex? instance;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Framelet");
    public SettingsStore Store { get; } = new(Path.Combine(DataDirectory, "settings.json"));
    public AppSettings Settings { get; set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), DateTimeOffset.Now + " " + args.Exception + Environment.NewLine); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            MessageBox.Show(args.Exception.Message, "Framelet couldn't complete that action", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        instance = new Mutex(true, "Local\\Framelet." + Environment.UserName, out var first);
        if (!first) { MessageBox.Show("Framelet is already running. Open it from the system tray.", "Framelet"); Shutdown(); return; }
        Settings = Store.Load();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => ApplyTitleTheme((Window)sender)));
        ApplyTheme(Settings.Theme);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.FirstOrDefault() is { } path && File.Exists(path)) window.OpenFile(path);
        if (Store.LoadWarning is not null) MessageBox.Show(Store.LoadWarning, "Framelet settings");
    }

    public void ApplyTheme(string theme)
    {
        var light = theme == "Light" || theme == "System" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) == 1;
        var colors = light ? new[] { "#F2F2F2", "#FAFAFA", "#E8E8E8", "#CDCDCF", "#202124", "#606166", "#875617", "#EFE1CB", "#795018", "#B32626" } : new[] { "#191A1C", "#222326", "#2B2D30", "#3D3F43", "#EEEEF0", "#A6A7AD", "#E9B566", "#494035", "#F3CE98", "#F08B88" };
        string[] keys = ["BackgroundBrush", "SurfaceBrush", "RaisedBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush", "SelectionBrush", "FocusBrush", "DangerBrush"];
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        foreach (Window window in Windows) ApplyTitleTheme(window);
    }

    private void ApplyTitleTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var color = ((SolidColorBrush)Resources["BackgroundBrush"]).Color;
        var dark = color.R < 128 ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
