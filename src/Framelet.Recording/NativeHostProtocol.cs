using Framelet.Core;

namespace Framelet.Recording;

internal sealed record NativeHostCommand(int Id, string Operation, NativeHostRequest? Request = null);
internal sealed record NativeHostMessage(string Kind, int Id = 0, string? Text = null, string? ErrorType = null,
    string? RecoveryPath = null);
internal sealed record NativeHostRequest(string SessionId, string OutputPath, CaptureKind Kind, PixelRect Bounds,
    long WindowHandle, string? DeviceName, RecordingSettings Settings)
{
    public RecordingRequest ToRecordingRequest() =>
        new(OutputPath, new(Kind, Bounds, new nint(WindowHandle), DeviceName), Settings);
}
