using System.Windows;
using Framelet.App;

internal static class Program
{
    private static int passed, failed;

    [STAThread]
    private static int Main(string[] args)
    {
        // Load the real resource dictionary without OnStartup, hotkeys, tray or persisted settings.
        var app = new App();
        app.InitializeComponent();
        app.ApplyTheme("Dark");
        if (args.Contains("--render"))
        {
            try { return RenderEvidence.Run(args); }
            catch (Exception ex) { Console.Error.WriteLine("Static evidence failed: " + ex); return 1; }
            finally { app.Shutdown(); }
        }
        ShellUiChecks.Run(Check);
        EditorUiChecks.Run(Check);
        SettingsUiChecks.Run(Check);
        MediaUiChecks.Run(Check);
        Console.WriteLine($"{passed} passed; {failed} failed");
        app.Shutdown();
        return failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }
}
