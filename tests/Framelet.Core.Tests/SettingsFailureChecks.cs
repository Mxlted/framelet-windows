using System.Security.AccessControl;
using System.Security.Principal;
using Framelet.Core;

internal static class SettingsFailureChecks
{
    public static void Run(Action<string, Action> check, string root)
    {
        check("Invalid settings store path remains a caller error", () =>
        {
            try { _ = new SettingsStore(""); throw new Exception("Invalid store path was accepted."); }
            catch (ArgumentException) { }
        });
        check("Settings read denial preserves original and recovers defaults", () =>
        {
            if (!OperatingSystem.IsWindows()) { Console.WriteLine("  SKIP: NTFS ACL check requires Windows."); return; }
            CheckDeniedRead(root);
        });
        check("Settings failed atomic replacement preserves original and cleans temporary file", () =>
        {
            var path = Path.Combine(root, "locked-settings.json");
            var store = new SettingsStore(path);
            store.Save(new AppSettings());
            var before = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { store.Save(new AppSettings { Theme = "Light" }); throw new Exception("Save should have failed."); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                Require(File.ReadAllText(path) == before, "Failed save replaced the original.");
            }
            Require(!Directory.EnumerateFiles(root, "locked-settings.json.*.tmp").Any(), "Temporary save file leaked.");
        });
        check("Settings load warning resets after recovery or missing file", () =>
        {
            var path = Path.Combine(root, "recover-settings.json");
            var store = new SettingsStore(path);
            File.WriteAllText(path, "{bad");
            store.Load(); Require(store.LoadWarning is not null, "Missing malformed warning.");
            store.Save(new AppSettings());
            store.Load(); Require(store.LoadWarning is null, "Warning remained after successful load.");
            File.WriteAllText(path, "null"); store.Load();
            File.Delete(path); store.Load();
            Require(store.LoadWarning is null, "Warning remained for a missing file.");
        });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CheckDeniedRead(string root)
    {
        var path = Path.Combine(root, "denied-settings.json");
        const string contents = "{\"Theme\":\"Light\"}";
        File.WriteAllText(path, contents);
        var file = new FileInfo(path);
        var denied = file.GetAccessControl();
        var sid = WindowsIdentity.GetCurrent().User ?? throw new Exception("No current user SID.");
        var rule = new FileSystemAccessRule(sid, FileSystemRights.ReadData, AccessControlType.Deny);
        denied.AddAccessRule(rule);
        try
        {
            file.SetAccessControl(denied);
            try { File.ReadAllText(path); throw new Exception("Read denial was not enforced on the test filesystem."); }
            catch (UnauthorizedAccessException) { }
            var store = new SettingsStore(path);
            var loaded = store.Load();
            Require(loaded.Theme == "Dark" && !loaded.Recording.UseFfmpeg, "Unsafe defaults.");
            Require(store.LoadWarning?.Contains(path, StringComparison.Ordinal) == true, "Warning omitted preserved file path.");
        }
        finally { denied.RemoveAccessRuleSpecific(rule); file.SetAccessControl(denied); }
        Require(File.ReadAllText(path) == contents, "Load changed the denied settings file.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
