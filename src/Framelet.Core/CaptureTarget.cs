namespace Framelet.Core;

public enum CaptureKind { Region, Window, Monitor, Desktop }

/// <summary>Physical desktop pixels, including negative coordinates on secondary displays.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public sealed record CaptureTarget(CaptureKind Kind, PixelRect Bounds, nint WindowHandle = 0,
    string? DeviceName = null, string? Title = null);
