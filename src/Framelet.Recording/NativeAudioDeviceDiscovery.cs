using ScreenRecorderLib;

namespace Framelet.Recording;

/// <summary>Native enumeration is called only inside the isolated recording worker.</summary>
internal static class NativeAudioDeviceDiscovery
{
    internal static IReadOnlyList<NativeAudioDevice> Enumerate(bool microphone) => microphone
        ? Recorder.GetSystemAudioCaptureDevices()
            .Select(device => new NativeAudioDevice(device.DeviceName, device.FriendlyName, device.IsDefaultDevice)).ToArray()
        : Recorder.GetSystemAudioLoopbackDevices()
            .Select(device => new NativeAudioDevice(device.DeviceName, device.FriendlyName, device.IsDefaultDevice)).ToArray();
}
