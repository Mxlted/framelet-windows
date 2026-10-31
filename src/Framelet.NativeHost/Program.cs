using Framelet.Recording;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args is ["--list-audio-devices", "input" or "output"])
{
    NativeDiscoveryErrors.SuppressDialogs();
    try
    {
        Console.WriteLine(JsonSerializer.Serialize(NativeAudioDeviceDiscovery.Enumerate(args[1] == "input")));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if (args is not ["--stdio"])
{
    Console.Error.WriteLine("This is Framelet's private recording worker. Start a recording from the Framelet application.");
    return 2;
}
return await NativeRecorderHost.RunAsync();

internal static class NativeDiscoveryErrors
{
    internal static void SuppressDialogs()
    {
        const uint FailCriticalErrors = 0x0001, NoGeneralProtectionFaultDialog = 0x0002;
        SetErrorMode(GetErrorMode() | FailCriticalErrors | NoGeneralProtectionFaultDialog);
    }

    [DllImport("kernel32.dll")] private static extern uint GetErrorMode();
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
