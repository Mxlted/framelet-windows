using System.Diagnostics;
using System.IO;
using System.Windows;
using Framelet.App.Capture;
using Framelet.Core;

namespace Framelet.Capture.Tests;

internal static class HotkeyTransactionChecks
{
    private const string A = "Ctrl+Alt+Shift+F13", B = "Ctrl+Alt+Shift+F14", Unrelated = "Ctrl+Alt+Shift+F15", Replacement = "Ctrl+Alt+Shift+F16", Conflict = "Ctrl+Alt+Shift+F18";

    public static int Child(string gesture, bool hold)
    {
        var window = new Window();
        try
        {
            using var manager = new HotkeyManager(window);
            bool registered = manager.Register("test", gesture, () => { }) is null;
            if (!hold) return registered ? 0 : 1;
            Console.WriteLine(registered ? "ready" : "conflict");
            Console.Out.Flush();
            if (registered) Console.ReadLine();
            return registered ? 0 : 1;
        }
        finally { window.Close(); }
    }

    public static void Run(Action<string, Action> check)
    {
        check("native hotkey conflict rolls back all reservations and persisted settings", () => WithManager(manager =>
        {
            using var owner = Start("--hold-hotkey", Conflict);
            try
            {
                Require(owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "ready", "Cannot reserve test conflict.");
                Require(manager.Apply([Bind("a", A), Bind("b", B), Bind("unrelated", Unrelated)]) is null, "Cannot register test shortcuts.");
                string root = Path.Combine(Path.GetTempPath(), "Framelet-hotkeys-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var store = new SettingsStore(Path.Combine(root, "settings.json"));
                    var original = new AppSettings { RegionHotkey = A, WindowHotkey = B };
                    store.Save(original);
                    string contents = File.ReadAllText(store.Path);
                    var replacement = SettingsStore.Clone(original); replacement.RegionHotkey = Replacement; replacement.WindowHotkey = Conflict;
                    string? error = manager.Apply([Bind("a", Replacement), Bind("b", Conflict)], () => store.Save(replacement));
                    Require(error?.Contains(Conflict) == true, "Conflict did not identify the shortcut.");
                    Require(File.ReadAllText(store.Path) == contents, "Conflict changed persisted settings.");
                    Require(!CanRegister(A) && !CanRegister(B) && !CanRegister(Unrelated), "An old or unrelated native shortcut was lost.");
                    Require(CanRegister(Replacement), "A rolled back new registration leaked.");
                }
                finally { Directory.Delete(root, true); }
            }
            finally { Stop(owner); }
        }));
        check("native hotkeys support replacement and disabling", () => WithManager(manager =>
        {
            Require(manager.Register("a", A, () => { }) is null, "Initial registration failed.");
            bool saved = false;
            Require(manager.Apply([Bind("a", Replacement)], () => saved = true) is null && saved, "Replacement failed.");
            Require(CanRegister(A) && !CanRegister(Replacement), "Native replacement not reflected.");
            Require(manager.Apply([Bind("a", "")]) is null && CanRegister(Replacement), "Disabled shortcut is still reserved.");
        }));
        check("native hotkey swaps reuse reservations and preserve callbacks", () => WithManager(manager =>
        {
            Action first = () => { }, second = () => { };
            Require(manager.Apply([new("a", A, first), new("b", B, second)]) is null, "Initial registration failed.");
            Require(manager.Apply([new("a", B, first), new("b", A, second)]) is null, "Swap failed.");
            Require(!CanRegister(A) && !CanRegister(B), "Swap dropped native reservations.");
            // Inspect dispatch ownership without synthesizing keyboard input into the user's desktop.
            var registrations = (Dictionary<string, (int, uint, uint, Action)>)typeof(HotkeyManager)
                .GetField("_registrations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!;
            HotkeyManager.TryParse(B, out _, out uint keyB, out _);
            Require(registrations["a"].Item3 == keyB && registrations["a"].Item4 == first && registrations["b"].Item4 == second, "Swapped dispatch action is incorrect.");
        }));
        check("normalized duplicate hotkeys reject before mutation", () => WithManager(manager =>
        {
            Require(manager.Register("a", A, () => { }) is null, "Initial registration failed.");
            bool persisted = false;
            var error = manager.Apply([Bind("a", "Shift+Ctrl+Alt+F16"), Bind("b", "Alt+Shift+Ctrl+F16")], () => persisted = true);
            Require(error is not null && !persisted && !CanRegister(A) && CanRegister(Replacement), "Duplicate changed native or persisted state.");
        }));
        check("failed settings save leaves native hotkeys unchanged", () => WithManager(manager =>
        {
            Require(manager.Register("a", A, () => { }) is null, "Initial registration failed.");
            try { manager.Apply([Bind("a", Replacement)], () => throw new IOException("Synthetic save failure")); throw new Exception("Save failure was swallowed."); }
            catch (IOException) { }
            Require(!CanRegister(A) && CanRegister(Replacement), "Save failure did not roll back reservations.");
        }));
        check("native test registrations are all released", () =>
        {
            foreach (string gesture in new[] { A, B, Unrelated, Replacement, Conflict }) Require(CanRegister(gesture), "Leaked registration: " + gesture);
        });
        check("queued hotkeys are invalidated by disable, reassignment and disposal", () => WithManager(manager =>
        {
            int calls = 0;
            Action callback = () => calls++;
            Require(manager.Register("a", A, callback) is null, "Initial registration failed.");
            Queue(manager, "a"); Pump();
            Require(calls == 1, "Live shortcut callback did not run.");
            Queue(manager, "a"); manager.Apply([Bind("a", "")]); Pump();
            Require(calls == 1, "Disabled action ran from a queued keypress.");
            Require(manager.Register("a", A, callback) is null, "Re-registration failed.");
            Queue(manager, "a"); manager.Apply([new("a", Replacement, callback)]); Pump();
            Require(calls == 1, "Reassigned action ran from a queued keypress.");
            Queue(manager, "a"); manager.Dispose(); Pump();
            Require(calls == 1, "Disposed action ran from a queued keypress.");
        }));
    }

    private static HotkeyBinding Bind(string id, string gesture) => new(id, gesture, () => { });
    private static void Queue(HotkeyManager manager, string id)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var registrations = (Dictionary<string, (int, uint, uint, Action)>)typeof(HotkeyManager).GetField("_registrations", flags)!.GetValue(manager)!;
        typeof(HotkeyManager).GetMethod("WndProc", flags)!.Invoke(manager, [(nint)0, 0x0312, (nint)registrations[id].Item1, (nint)0, false]);
    }
    private static void Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    private static void WithManager(Action<HotkeyManager> action)
    {
        var window = new Window();
        try { using var manager = new HotkeyManager(window); action(manager); }
        finally { window.Close(); }
    }
    private static Process Start(string mode, string gesture)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(mode); start.ArgumentList.Add(gesture);
        return Process.Start(start) ?? throw new Exception("Cannot start hotkey test child.");
    }
    private static bool CanRegister(string gesture)
    {
        using var child = Start("--probe-hotkey", gesture);
        try
        {
            if (!child.WaitForExit(10000)) throw new TimeoutException("Hotkey probe timed out.");
            if (child.ExitCode is not (0 or 1)) throw new Exception("Hotkey probe failed: " + child.StandardError.ReadToEnd());
            return child.ExitCode == 0;
        }
        finally { if (!child.HasExited) child.Kill(true); child.WaitForExit(5000); }
    }
    private static void Stop(Process child)
    {
        if (!child.HasExited) child.StandardInput.WriteLine();
        if (!child.WaitForExit(5000)) { child.Kill(true); child.WaitForExit(5000); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
