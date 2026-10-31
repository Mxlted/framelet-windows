using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Framelet.Recording;

public sealed record NativeAudioDevice(string Id, string Name, bool IsDefault)
{
    public override string ToString() => Name;
}

public static class NativeAudioDevices
{
    private const int MaximumOutputLength = 1024 * 1024;
    private const int MaximumErrorLength = 4096;
    private const string UnavailableMessage = "Windows audio devices could not be listed. Check your connected audio devices, then reopen Recording settings.";
    private const string InvalidResultMessage = "The audio device list could not be read. Reinstall the complete Framelet package and try again.";

    public static IReadOnlyList<NativeAudioDevice> GetOutputs() => Query(false);
    public static IReadOnlyList<NativeAudioDevice> GetInputs() => Query(true);

    private static IReadOnlyList<NativeAudioDevice> Query(bool microphone) => QueryAsync(
        CreateStartInfo(Path.Combine(AppContext.BaseDirectory, "Framelet.NativeHost.exe"), microphone),
        TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

    internal static ProcessStartInfo CreateStartInfo(string hostPath, bool microphone)
    {
        if (!File.Exists(hostPath))
            throw new FileNotFoundException("The native recording component is missing. Reinstall the complete Framelet package.", hostPath);
        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--list-audio-devices");
        startInfo.ArgumentList.Add(microphone ? "input" : "output");
        return startInfo;
    }

    internal static async Task<IReadOnlyList<NativeAudioDevice>> QueryAsync(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new IOException(UnavailableMessage);
        }
        catch (Win32Exception ex) { throw new IOException(UnavailableMessage, ex); }

        using var reading = new CancellationTokenSource();
        var output = ReadBoundedAsync(process.StandardOutput, MaximumOutputLength, rejectOverflow: true, reading.Token);
        var errors = ReadBoundedAsync(process.StandardError, MaximumErrorLength, rejectOverflow: false, reading.Token);
        var exited = process.WaitForExitAsync(reading.Token);
        var completed = Task.WhenAll(output, errors, exited);
        try
        {
            try { await completed.WaitAsync(timeout).ConfigureAwait(false); }
            catch (TimeoutException ex)
            {
                throw new TimeoutException("Audio device discovery timed out. Reopen Recording settings to try again.", ex);
            }
            if (process.ExitCode != 0)
                throw new IOException(UnavailableMessage,
                    new IOException($"Audio discovery worker exited with code {process.ExitCode}. {await errors.ConfigureAwait(false)}"));
            return Parse(await output.ConfigureAwait(false));
        }
        finally
        {
            // A broken driver must not leave a worker or an unbounded pipe read behind.
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException) { Debug.WriteLine(ex); }
            reading.Cancel();
            try { await completed.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or TimeoutException) { Debug.WriteLine(ex); }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, bool rejectOverflow, CancellationToken ct)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        bool overflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            int retained = Math.Min(count, limit - result.Length);
            result.Append(buffer, 0, retained);
            overflow |= retained != count;
        }
        if (overflow && rejectOverflow) throw new InvalidDataException(InvalidResultMessage);
        return result.ToString();
    }

    private static IReadOnlyList<NativeAudioDevice> Parse(string output)
    {
        try
        {
            using var json = JsonDocument.Parse(output);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 256)
                throw new InvalidDataException(InvalidResultMessage);
            var devices = new List<NativeAudioDevice>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("Id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) ||
                    !item.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("IsDefault", out var isDefault) || isDefault.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException(InvalidResultMessage);
                devices.Add(new NativeAudioDevice(id.GetString()!, name.GetString()!, isDefault.GetBoolean()));
            }
            return devices;
        }
        catch (JsonException ex) { throw new InvalidDataException(InvalidResultMessage, ex); }
    }
}
