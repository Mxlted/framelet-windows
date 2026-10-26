# Audit implementation and validation

Implemented against audit baseline `1a5572858df4cc256ec8cfaa1c39ffbc196a7a94` on October 26, 2026. The working tree was clean at the start. The sibling ShareX reference remained unchanged. The audit implementation and validation did not commit, push, tag, publish, install, or uninstall. Subsequent authorized release preparation is recorded in [release notes](release-notes.md).

## Changes

| Finding | Implementation and principal files |
| --- | --- |
| Completed recordings lost after a final save collision | Explicit incomplete/discarded/finalized/published ownership, typed recovery paths, native protocol notification, cleanup reconciliation, and atomic non-overwriting Save As. `NativeRecordingBackend.cs`, `NativeSession.cs`, `NativeHostProtocol.cs`, `NativeRecorderHost.cs`, `FfmpegRecordingBackend.cs`, `RecordingRecovery.cs`, `RecordingSessionController.cs`, `RecordingWindow.cs`. |
| Effects composited over translucent source | Replace the processed RGBA region and retain the surrounding composited pixels; earlier annotations and later layers keep their order. `AnnotationRenderer.cs`. |
| Image limit checked after decoding | PNG header preflight and deferred WIC frame dimensions before checked pixel copying. One owned stream, detached bitmap, recoverable invalid-image errors. `SafeImageLoader.cs`, `ImageDocument.cs`, `MainWindow.xaml.cs`. |
| Cropping removed visible annotations | Use painted bounds including stroke thickness, round caps, arrow wings and antialias margins; conservatively retain text, translate objects, and clip during rendering. `Annotation.cs`, `ImageDocument.cs`. |
| Denied settings reads crashed startup | Recover expected access/filesystem errors to defaults with an original-file warning; install dispatcher handling before loading; keep invalid store paths as caller errors. Atomic saves remain. `AppSettings.cs`, `App.xaml.cs`. |
| Delayed window capture used stale bounds | Keep the selected HWND before prompting/delay and refresh physical DWM bounds just before live capture; reject closed/minimized targets. Frozen overlays retain snapshot semantics. `CaptureService.cs`, `MainWindow.xaml.cs`. |
| Hotkey changes lost working bindings | Reserve new normalized key/modifier pairs before save, reuse native registrations for swaps, release old pairs only on success, and invalidate stale queued callbacks. `HotkeyManager.cs`, `SettingsWindow.cs`, `MainWindow.xaml.cs`. |
| Large effect previews blocked pointer movement | Display-scaled background processing, one worker/one pending request, 33 ms coalescing, one-million-pixel buffer cap, cancellation and versioned results. Exact committed/export rendering remains. `EffectPreviewRenderer.cs`, `EditorSurface.cs`, `AnnotationRenderer.cs`. |

`UseFfmpeg` remains false by default. Native recording, optional FFmpeg recording, and FFmpeg clip processing remain separate. Recovery sources are retained after Save As so the only completed copy cannot be lost to a second cleanup failure.

## Regression coverage

- Recording subprocess fixtures cover late collisions, natural completion, repeated cleanup/disposal, disposal before the exit observer, worker EOF after finalized notification, genuine failure and cancellation. An injected native teardown fault verifies successful media is flushed, closed and announced before disposal. Fixtures assert file ownership, not decodability. Live native and FFmpeg checks create real MP4s, introduce collisions after startup, preserve sentinels, and decode recovery files with a separately launched inspector. Session tests cover recovery UI state and failed/successful alternate saves.
- Complete rendered and PNG-exported blur/pixelation tests use alpha 0, 128 and 255, mixed transparency, independent reference filtering, overlapping effects, clipping, and earlier/later annotations. Crop tests compare retained pixels for thick pen flanks, round caps, arrowheads, rectangles and ellipses, plus object identity, translation, order, undo and redo.
- PNG, JPEG, BMP, GIF and TIFF load tests check pixel dimensions, DPI normalization and immediate file-handle release. Malformed, truncated and unsupported inputs leave the active dirty document intact. A valid 16,385 × 2,048 PNG is rejected in a child process with a 384 MiB Windows Job limit and 15-second timeout. The recorded run showed 499,712 bytes private-memory growth and 40,263,680 bytes peak working set, without allocating the approximately 134 MB decoded image.
- Settings tests deny read-data permission on a fresh temporary file and remove the temporary ACL rule in `finally`. Tests also cover malformed/null settings, default recovery, warning reset, invalid caller paths, validation failures, and failed atomic replacement without temporary-file leaks.
- Native hotkey tests hold a conflict in a separate process and check rollback, persisted-file preservation, successful replacement, disabling, normalized duplicates, swaps, unrelated registrations, failed saves, callback invalidation, and final release of all registrations.
- Desktop tests move/resize a synthetic red HWND before capture, compare refreshed DWM geometry and captured pixels, then test minimized/closed targets. Existing negative-origin and frozen-snapshot checks still pass.
- Preview tests cover scaling limits, reference output at full scale, burst coalescing, cancellation, bounded disposal, and stale callbacks after source, document revision, geometry, zoom, selection, tool and unload changes.

## Preview measurements

Same warmed committed-cache workload before and after: 3840 × 2160 synthetic image, full-image blur radius 12, six slightly changing rectangles, 10% zoom. The desktop used 125% DPI, so new preview buffers were approximately 480 × 270 pixels. Timed `EditorSurface.OnRender` calls include request scheduling; completion latency is recorded separately and includes coalescing.

| Metric | Six samples in milliseconds |
| --- | --- |
| Before, synchronous UI render | 530.68, 415.46, 385.43, 390.23, 367.87, 378.11 |
| After, UI render/request | 2.80, 0.31, 0.03, 0.02, 0.02, 0.02 |
| After, preview ready | 67.48, 46.05, 47.98, 46.47, 47.48, 46.89 |
| After, cached completed-preview draw | 0.21, 0.07, 0.06, 0.06, 0.10, 0.12 |

Reproduce with `dotnet run --project tests/Framelet.Editor.Tests -c Release -p:Platform=x64 -- --benchmark-preview`. No machine-speed assertions were added. Initial committed rendering and exact export can still take full-resolution processing time; the pointer preview is approximate and does not replace export pixels.

## Commands and results

Final build and test runs all returned exit code 0:

| Command | Result |
| --- | --- |
| `./scripts/build.ps1` | Release app and native host build; 0 warnings/errors. |
| `./scripts/test.ps1` | 141 passed, 0 failed: Core 44, Editor 54, Capture 16, Recording 17, Session 10. |
| `dotnet build Framelet.sln -c Release -p:Platform=x64 --no-restore` | Full solution; 0 warnings/errors. |
| `dotnet run --project tests/Framelet.Core.Tests -c Release -p:Platform=x64 --no-build -- --ffmpeg-integration` | 52 passed, 0 failed, including eight integration checks. |
| `dotnet run --project tests/Framelet.Capture.Tests -c Release -p:Platform=x64 --no-build -- --desktop` | 19 passed, 0 failed, including three live desktop checks. |
| `dotnet run --project tests/Framelet.Recording.Tests -c Release -p:Platform=x64 --no-build -- --live --without-ffmpeg --output <fresh-directory> --decode-with <absolute-ffmpeg.exe>` | 26 passed, 0 failed, including nine live/inspection checks. |
| `dotnet run --project tests/Framelet.Editor.Tests -c Release -p:Platform=x64 --no-build -- --benchmark-preview` | Six measured samples above; no speed assertions. |
| `./scripts/package.ps1 -SkipTests -OutputDirectory artifacts/audit-validation/c4c9da35a3024de4be1b85fbfed24462/package-final` | Portable ZIP and installer compiled; 513 file hashes and runtime/notice checks passed; no bundled FFmpeg. |

Final logs and generated native media are retained locally under `artifacts/audit-validation/c4c9da35a3024de4be1b85fbfed24462/`; filenames ending in `-final` identify reruns after the last recording lifetime fixes. All seven real native MP4s fully decoded with external `ffmpeg -v error -i <file> -f null -`, and `ffprobe` recorded stream/frame metadata in `native-decode-final.json`. Sentinel collision destinations are deliberately not media and were excluded from decoding. FFmpeg integration uses fresh temporary directories and removes them after its full decode assertions.

The final allocation rerun recorded 569,344 bytes private-memory growth and 40,214,528 bytes peak working set. Small differences between allocation runs reflect runtime behavior; both are far below the decoded oversized fixture.

Create diagnostic packages with `./scripts/package.ps1 -SkipTests -OutputDirectory artifacts/audit-validation/<run>/package-final` after the tests pass. The output-directory option keeps prior release artifacts untouched; `verify-package.ps1` runs automatically. Installer compilation was exercised, but `check-installer.ps1` was deliberately not run because installation and uninstallation were excluded from this task.

The final audit diff had no whitespace errors, the reference checkout was clean, and no Framelet application/test/worker or FFmpeg processes remained after validation. At that point HEAD was still the audit baseline and changes were uncommitted, before subsequent release preparation.

## Evidence boundaries

Validation uses .NET SDK 10.0.403 on this Windows 11 development desktop. The native run clears the test process and worker PATH and supplies a missing FFmpeg configuration; an explicit external decoder runs only after native sessions finish. Audio endpoint availability and a short decoded AAC stream do not establish microphone quality or long-term synchronization. The requested-120-FPS test is not a claim of 120 unique captured frames per second. Hardware encoding remains a preference without authoritative encoder telemetry.

Package verification checks hashes, both executables and runtime configuration, self-contained .NET/WPF files, all four required CRT DLLs, and notices/licenses; FFmpeg is excluded. Installer compilation does not establish installation, upgrades, clean-VM compatibility, or signing. The recovery Save As operation is tested through the controller and filesystem; a completed manual common-dialog interaction is not claimed.

## API references checked

- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) for ownership, native IDs and conflicts.
- [DWM window attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) for physical extended frame bounds.
- [WPF source](https://github.com/dotnet/wpf/tree/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Imaging) and Context7 `/dotnet/wpf` for deferred decoder creation and frozen bitmap ownership; [Freezable](https://learn.microsoft.com/en-us/dotnet/api/system.windows.freezable) for cross-thread frozen objects.
- [ScreenRecorderLib 7.0.1 recording completion](https://github.com/sskodje/ScreenRecorderLib/blob/v7.0.1/ScreenRecorderLibNative/RecordingManager.cpp) and [managed completion callback](https://github.com/sskodje/ScreenRecorderLib/blob/v7.0.1/ScreenRecorderLib/Recorder.cpp) for finalization and stream release ordering.
