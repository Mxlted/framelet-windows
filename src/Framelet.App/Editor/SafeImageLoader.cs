using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Framelet.App.Editor;

/// <summary>Loads the first image frame only after checking its dimensions.</summary>
public static class SafeImageLoader
{
    public static BitmapSource Load(string path)
    {
        // Keep one handle throughout inspection and decoding, so the file cannot be
        // replaced between those operations. No decoder-backed frame escapes it.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            ValidatePngHeader(stream);
            stream.Position = 0;
            // OnLoad would decode before we can inspect the dimensions. None leaves
            // pixel reads deferred until CopyPixels, after the dimension guard.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("The image contains no frames.");
            var frame = decoder.Frames[0];
            ImageDocument.ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
            return ImageDocument.Normalize(frame);
        }
        catch (Exception error) when (error is FileFormatException or NotSupportedException or COMException)
        {
            throw new InvalidDataException("The image is damaged, incomplete, or in an unsupported format. Try another image or export it as PNG.", error);
        }
    }

    private static void ValidatePngHeader(Stream stream)
    {
        // PNG dimensions are directly available before any compressed pixel data.
        // This also rejects an oversized truncated PNG before asking a codec to load it.
        Span<byte> header = stackalloc byte[24];
        int count = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (count < 8 || !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return;
        if (count != header.Length || BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13 ||
            !header[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("The PNG header is incomplete or invalid.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width > int.MaxValue || height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(stream), "Images must be between 1 and 16,384 pixels per side and at most 64 megapixels.");
        ImageDocument.ValidateDimensions((int)width, (int)height);
    }
}
