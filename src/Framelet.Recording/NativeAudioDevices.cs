using ScreenRecorderLib;

namespace Framelet.Recording;

public sealed record NativeAudioDevice(string Id, string Name, bool IsDefault)
{
    public override string ToString() => Name;
}

public static class NativeAudioDevices
{
    public static IReadOnlyList<NativeAudioDevice> GetOutputs() => Recorder.GetSystemAudioLoopbackDevices()
        .Select(d => new NativeAudioDevice(d.DeviceName, d.FriendlyName, d.IsDefaultDevice)).ToArray();

    public static IReadOnlyList<NativeAudioDevice> GetInputs() => Recorder.GetSystemAudioCaptureDevices()
        .Select(d => new NativeAudioDevice(d.DeviceName, d.FriendlyName, d.IsDefaultDevice)).ToArray();
}
