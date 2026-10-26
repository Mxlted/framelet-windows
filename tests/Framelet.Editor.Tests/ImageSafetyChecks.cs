using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet.App.Editor;
using Microsoft.Win32.SafeHandles;

internal static class ImageSafetyChecks
{
    public static bool TryRunChild(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 2 || args[0] != "--image-allocation-child") return false;
        // The parent assigns a hard process memory limit before releasing this gate.
        if (Console.ReadLine() != "go") { exitCode = 3; return true; }
        using var process = Process.GetCurrentProcess();
        _ = Solid(1, 1);
        GC.Collect(); GC.WaitForPendingFinalizers();
        process.Refresh();
        long before = process.PrivateMemorySize64;
        try
        {
            _ = SafeImageLoader.Load(args[1]);
            Console.Error.WriteLine("Oversized input unexpectedly loaded."); exitCode = 1;
        }
        catch (ArgumentOutOfRangeException)
        {
            process.Refresh();
            long growth = process.PrivateMemorySize64 - before;
            Console.WriteLine($"Oversized PNG rejected before decoding; private-memory growth {growth:N0} bytes, peak working set {process.PeakWorkingSet64:N0} bytes.");
            // This checks allocation scale, not machine speed. The decoded fixture
            // alone is 134,225,920 bytes; a header check needs a tiny fraction of it.
            if (growth > 32L * 1024 * 1024) exitCode = 2;
        }
        catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
        return true;
    }

    public static void Run(Action<string, Action> check)
    {
        foreach (var (name, annotation, crop) in new[]
        {
            ("thick pen flank", new Annotation { Tool = EditorTool.Pen, Points = [new(9, 4), new(9, 36)], Color = Colors.Red, StrokeWidth = 10 }, new Int32Rect(12, 0, 28, 40)),
            ("round pen cap", new Annotation { Tool = EditorTool.Pen, Points = [new(3, 20), new(9, 20)], Color = Colors.Red, StrokeWidth = 10 }, new Int32Rect(12, 0, 28, 40)),
            ("arrowhead wing", new Annotation { Tool = EditorTool.Arrow, Start = new(9, 4), End = new(9, 37), Color = Colors.Red, StrokeWidth = 10 }, new Int32Rect(14, 0, 26, 40)),
            ("rectangle stroke", new Annotation { Tool = EditorTool.Rectangle, Start = new(2, 4), End = new(8, 36), Color = Colors.Red, StrokeWidth = 10 }, new Int32Rect(10, 0, 30, 40)),
            ("ellipse stroke", new Annotation { Tool = EditorTool.Ellipse, Start = new(2, 4), End = new(8, 36), Color = Colors.Red, StrokeWidth = 10 }, new Int32Rect(10, 0, 30, 40))
        })
            check("Crop retains visible " + name + " and editable history", () =>
            {
                var document = new ImageDocument(Solid(40, 40));
                document.Add(annotation);
                var expected = Pixels(document.Export(), crop);
                Require(expected.Where((_, index) => index % 4 == 0).Any(channel => channel < 255), "Fixture must contain painted pixels in retained region.");
                document.Crop(crop);
                Require(document.Annotations.Count == 1 && document.Annotations[0].Id == annotation.Id, "Visible annotation was discarded.");
                Require(Pixels(document.Export()).SequenceEqual(expected), "Cropped pixel output differs from retained original pixels.");
                Require(document.Annotations[0].Start == annotation.Start - new Vector(crop.X, crop.Y), "Coordinates were not translated.");
                document.Undo(); Require(document.Annotations[0] == annotation && document.Width == 40, "Undo lost original geometry.");
                document.Redo(); Require(Pixels(document.Export()).SequenceEqual(expected), "Redo changed cropped pixels.");
                document.Replace(document.Annotations[0] with { Color = Colors.Blue });
                Require(!Pixels(document.Export()).SequenceEqual(expected), "Retained annotation is not editable.");
            });

        check("Crop preserves crossing-shape layer order", () =>
        {
            var document = new ImageDocument(Solid(40, 40));
            var red = new Annotation { Tool = EditorTool.Rectangle, Start = new(2, 4), End = new(8, 36), Color = Colors.Red, StrokeWidth = 10 };
            var blue = red with { Id = Guid.NewGuid(), Start = new(1, 16), End = new(9, 24), Color = Colors.Blue };
            document.Add(red); document.Add(blue);
            var crop = new Int32Rect(11, 0, 29, 40);
            var expected = Pixels(document.Export(), crop);
            document.Crop(crop);
            Require(document.Annotations.Select(a => a.Id).SequenceEqual(new[] { red.Id, blue.Id }), "Layer order changed.");
            Require(Pixels(document.Export()).SequenceEqual(expected), "Layer composition changed.");
        });

        check("Crop arithmetic rejects overflowing coordinates", () =>
        {
            var document = new ImageDocument(Solid(4, 4));
            Expect<ArgumentOutOfRangeException>(() => document.Crop(new Int32Rect(int.MaxValue - 1, 0, 10, 1)));
            Require(document.Width == 4 && !document.CanUndo, "Rejected crop changed document state.");
        });

        var root = Path.Combine(Path.GetTempPath(), "Framelet-image-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var (name, encoder) in new (string, BitmapEncoder)[]
            {
                ("PNG", new PngBitmapEncoder()), ("JPEG", new JpegBitmapEncoder()), ("BMP", new BmpBitmapEncoder()),
                ("GIF", new GifBitmapEncoder()), ("TIFF", new TiffBitmapEncoder())
            })
                check("Safe " + name + " decoding detaches the stream and normalizes pixels", () =>
                {
                    string path = Path.Combine(root, name + ".image");
                    encoder.Frames.Add(BitmapFrame.Create(Solid(7, 5)));
                    using (var output = File.Create(path)) encoder.Save(output);
                    var image = SafeImageLoader.Load(path);
                    Require(image.IsFrozen && image.PixelWidth == 7 && image.PixelHeight == 5 && image.DpiX == 96 && image.Format == PixelFormats.Pbgra32, "Unexpected decoded image.");
                    using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    Require(Pixels(image).Length == 140, "Decoded source still depends on the closed stream.");
                });

            check("Malformed, truncated and unsupported files preserve the current document", () =>
            {
                var editor = new ImageEditorView();
                editor.LoadImage(Solid(8, 8), "current.png");
                var surface = (EditorSurface)editor.FindName("Surface");
                var original = surface.Document!;
                original.Add(new Annotation { Tool = EditorTool.Rectangle, Start = new(1, 1), End = new(6, 6), Filled = true, Color = Colors.Red });
                var expected = Pixels(editor.ExportImage());
                foreach (var input in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("This is not an image"), PngHeader(4, 4) })
                {
                    string path = Path.Combine(root, "invalid.image");
                    File.WriteAllBytes(path, input);
                    Expect<InvalidDataException>(() => editor.LoadImage(SafeImageLoader.Load(path), path));
                    Require(ReferenceEquals(surface.Document, original) && editor.SourcePath == "current.png" && original.IsDirty && original.CanUndo, "Failed load replaced the document.");
                    Require(Pixels(editor.ExportImage()).SequenceEqual(expected), "Failed load changed pixels.");
                    using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
            });

            check("Oversized PNG headers reject dimensions before truncated pixel data", () =>
            {
                foreach (var (width, height) in new[] { (16385u, 2048u), (10000u, 10000u), (uint.MaxValue, 1u), (0u, 10u) })
                {
                    string path = Path.Combine(root, "oversized-header.png");
                    File.WriteAllBytes(path, PngHeader(width, height));
                    Expect<ArgumentOutOfRangeException>(() => SafeImageLoader.Load(path));
                    using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
            });

            check("Oversized compressed PNG rejects early in a memory-capped child", () =>
            {
                string path = Path.Combine(root, "oversized.png");
                WriteOversizedPng(path);
                using var job = MemoryLimitedJob.Create(384L * 1024 * 1024);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--image-allocation-child"); start.ArgumentList.Add(path);
                using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start allocation test child.");
                try
                {
                    job.Assign(child);
                    var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                    child.StandardInput.WriteLine("go"); child.StandardInput.Close();
                    if (!child.WaitForExit(15000)) throw new TimeoutException("Allocation test child exceeded 15 seconds.");
                    Console.WriteLine(output.GetAwaiter().GetResult().Trim());
                    Require(child.ExitCode == 0, $"Allocation test child exited {child.ExitCode}: {error.GetAwaiter().GetResult()}");
                }
                finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } }
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BitmapSource Solid(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static byte[] Pixels(BitmapSource image, Int32Rect region = default)
    {
        if (region.IsEmpty) region = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        int stride = checked(region.Width * 4); var pixels = new byte[checked(stride * region.Height)];
        image.CopyPixels(region, pixels, stride, 0); return pixels;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static byte[] PngHeader(uint width, uint height)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] data = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), width); BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), height);
        data[8] = 8; data[9] = 6; WriteChunk(stream, "IHDR", data); return stream.ToArray();
    }
    private static void WriteOversizedPng(string path)
    {
        using var stream = File.Create(path); stream.Write(PngHeader(16385, 2048));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[16385 * 4 + 1];
            for (int y = 0; y < 2048; y++) zlib.Write(row);
        }
        WriteChunk(stream, "IDAT", compressed.ToArray()); WriteChunk(stream, "IEND", []);
    }
    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] kind = Encoding.ASCII.GetBytes(type); Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(integer, (uint)data.Length); stream.Write(integer); stream.Write(kind); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in kind.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc); stream.Write(integer);
    }

    private sealed class MemoryLimitedJob : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private MemoryLimitedJob(SafeFileHandle handle) => _handle = handle;
        public static MemoryLimitedJob Create(long bytes)
        {
            var handle = CreateJobObject(0, null);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x100 | 0x2000 }, ProcessMemoryLimit = (nuint)bytes };
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
            { handle.Dispose(); throw new System.ComponentModel.Win32Exception(); }
            return new(handle);
        }
        public void Assign(Process process) { if (!AssignProcessToJobObject(_handle, process.Handle)) throw new System.ComponentModel.Win32Exception(); }
        public void Dispose() => _handle.Dispose();
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
        {
            public BasicLimits Basic; public IoCounters Io;
            public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    }
}
