# Architecture and decisions

Framelet is an independent application. The local ShareX checkout was read as a reference and is neither a build input nor part of this repository. No ShareX source, resources, or UI components are included.

## Windows interface

WPF on .NET 10 was chosen for mature Windows input, accessibility, clipboard, HWND interop, retained drawing, and deployment support. Avalonia offers modern Skia rendering and excellent custom styling, but its portability brings little benefit to a product whose capture, audio, codecs, and tray behavior are Windows-specific. WinForms is useful for the tray, but less suitable for the editor's scaled vector canvas. WinUI 3 was excluded as requested. The initial supported distribution is Windows 11 22H2 or later, x64.

The editor uses one custom drawing surface, not a UI element per stroke point. Annotations remain objects while editing; export flattens them at the image's actual pixel size. Moving a window between monitors changes its display scale, not the document coordinates. Crop and resize retain annotation geometry. See [editor details](image-editor.md).

The shell separates image editing, clip preview, settings, and a small recording window. There is no account, uploader, background network service, telemetry, embedded browser, or dependency download. The FFmpeg download link opens only when clicked.

## Capture and recording are different workloads

Still captures use a short-lived GDI desktop snapshot and DWM window bounds. A frozen selection overlay runs one HWND per monitor, uses physical cursor coordinates, and supports negative desktop origins. This is inexpensive for a one-shot capture and avoids starting a video pipeline. It captures visible SDR pixels; occluded windows, HDR color management, and protected content have different requirements. See [capture details](capture.md).

Video uses Windows Graphics Capture and Direct3D through ScreenRecorderLib 7.0.1, with Media Foundation H.264/AAC MP4 encoding and WASAPI system/microphone capture. This follows the modern native Windows approach studied in ShareX 22, but uses a separately licensed MIT engine with a new adapter and lifecycle. It does not load or call FFmpeg. Rather than reimplementing several thousand lines of codec, COM, timing, and audio-mixing code for the first release, Framelet owns the product behavior and delegates those mechanisms to a focused library. That dependency is a real architectural tradeoff: encoder selection and the newest capture-rate extensions are constrained by its implementation.

A live trial exposed an unmanaged startup stall. The native engine therefore runs in a child process, `Framelet.NativeHost.exe`. A small JSON-lines stdio protocol controls one session. Timeouts can terminate only that worker and discard its private staging file without hanging the UI. The host writes the final destination only after MP4 finalization. See [native recording](native-recording.md) for synchronization, device behavior, limits, and measured results.

`IRecordingBackend` is the one shared recording boundary. `RecordingBackendSelector` constructs exactly the selected backend. The persisted `UseFfmpeg` flag is false by default. No fallback switches engines after a failure.

The optional FFmpeg backend runs `gdigrab` and an optional explicitly named DirectShow audio input. It supports region, monitor, and HWND capture, configurable codecs, bitrate and restricted encoding parameters. It does not support pause or independently mixed WASAPI system/mic capture. The UI hides native audio options when FFmpeg is selected and explains these differences.

`ClipProcessor` is separate from backend selection. Native MP4 files can be previewed without FFmpeg; trimming, compression, and format conversion explicitly resolve FFmpeg only when exporting. Exact MP4 export re-encodes with accurate seeking; stream-copy trimming is fast but keyframe-bound. Source files are preserved.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| Framelet.Core | Settings, validation, physical target coordinates, recording contract, output naming |
| Framelet.App | WPF shell, capture overlay, editor, tray, hotkeys, settings, clip preview |
| Framelet.Recording | Native adapter, host protocol, target/device mapping and lifecycle |
| Framelet.NativeHost | Small isolated native engine entry point |
| Framelet.Media | Optional FFmpeg recording and independent clip processing |

Tests are small executable suites with a nonzero failure exit code. Native/GPU/audio tests are opt-in, because hosted CI cannot represent an interactive Windows desktop. Dependencies are pinned and lockfiles are committed. Settings use an atomic replacement; malformed files are preserved until the user explicitly saves new settings. Recording/export files use private staging names and never silently overwrite an existing destination.

## Research record

Reviewed October 26, 2026:

- ShareX reference commit `f3ae963af1c03c242f58adf162d3dffc1d45c657`: `ScreenRecordManager`, `ScreenRecordingLib`, capture overlays and editor organization. [ShareX 22 changelog](https://getsharex.com/changelog) and [native source](https://github.com/ShareX/ShareX/tree/develop/ShareX.ScreenRecordingLib).
- [Windows Graphics Capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture), [Desktop Duplication](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api), and [WPF hardware rendering](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware). WGC timestamps and WASAPI timing matter more here than the UI framework's renderer.
- [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) and its native source, license, audio device API, recording source/crop options, and MF settings. Package 7.0.1 was tested on .NET 10 x64.
- [Avalonia Windows integration](https://docs.avaloniaui.net/docs/platform-specific-guides/windows/) and [dotnet/wpf](https://github.com/dotnet/wpf); Context7 was used for WPF DPI/rendering APIs and FFmpeg capture/seeking documentation.
- [FFmpeg device documentation](https://ffmpeg.org/ffmpeg-devices.html) and [seeking/encoding documentation](https://ffmpeg.org/ffmpeg-all.html). `ProcessStartInfo.ArgumentList` keeps filenames and device names out of a shell.
- [CleanShot annotation/workflow](https://cleanshot.com/features) and [Figma contextual property panels](https://help.figma.com/hc/en-us/articles/360039832014-Design-prototype-and-explore-layer-properties-in-the-right-sidebar): useful interaction references for persistent tools, contextual properties, and fast copy/save. The layout, icons, colors, and implementation are original. Pinterest and Figma Community searches were attempted but their pages were not accessible; no assets or uninspected designs were used.
- [GitHub Trending](https://github.com/trending?l=csharp), [LibHunt C# trends](https://www.libhunt.com/l/c-sharp/trending), and [v2rayN repository](https://github.com/2dust/v2rayN) were consulted for repository practices. Trend pages were partially inaccessible and were not used as technical evidence. The useful conventions are straightforward: SDK pinning, editor configuration, focused commits, reproducible builds, test gates, and release artifacts.
