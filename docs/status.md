# Preview status

The first preview is a functional Windows application, not a finished replacement for every capture tool. The implementation and release scope are intentionally visible here for future work.

## Implemented

Region, visible-window, monitor and virtual-desktop screenshots; frozen per-monitor selector; configurable global hotkeys; tray; disk/clipboard image loading; annotated image export; automatic capture actions; atomic settings; dark/light/system theme. The editor retains movable/resizable annotations, layer order, text edits, crop/resize, undo/redo, blur and pixelation. Native recording and optional FFmpeg recording have separate concrete pipelines. Native supports system and mic endpoints, pause/resume, finalization, cancel/restart, and worker isolation. Clip preview, exact and keyframe trim, compression and WebM export are included.

## Actual validation on October 26, 2026

- The full x64 Release solution builds with zero warnings and errors on .NET SDK 10.0.403 / Windows 11 build 26300.
- All 80 deterministic checks passed, covering settings/defaults/backend selection, safe filenames and file collisions, FFmpeg argument boundaries, clip validation, editor pixels and object history, capture geometry and hotkey parsing, native failure handling, and recording-session races. The final combined run passed 88 checks with the seven FFmpeg integration checks and one live desktop-capture check enabled; the separate native live suite is detailed below.
- Real screenshots were captured on a two-display desktop. A drag on the 125% display produced matching physical selection/image dimensions. Region selection and Escape cancellation were exercised interactively. This does not establish every mixed-DPI topology.
- The native suite passed 13 checks with live mode enabled, including moving-window video, pause/resume, simultaneous system/microphone capture, a region, cancellation, forced worker termination, and captured-window closure. A 60 FPS clip contained 169 frames in 2.81665 seconds after excluding a one-second pause. Another contained 151 video frames over 2.51665 seconds and stereo 48 kHz AAC over 2.517313 seconds, an endpoint difference of about 0.66 ms. A generated quiet 440 Hz tone was measurable in the loopback output, and WASAPI diagnostics reported packets from both inputs. This is a short-duration synchronization test, not a long-duration guarantee or a subjective microphone quality evaluation.
- A requested 120 FPS region test produced about 103 encoded frames per second on this machine. 120 is consequently labeled experimental; unique captured-frame throughput is not guaranteed.
- FFmpeg integration passed seven additional checks: synthetic audio/video generation; exact trim, lossless trim, compressed MP4 and WebM exports; real desktop recording start/stop with full decode; cancellation cleanup. The native backend never invoked FFmpeg. FFmpeg/ffprobe were used externally to inspect test outputs.
- The running dark and light UIs were inspected, and changing theme preserved editable objects. Live arrow/rectangle creation, undo/redo, native/FFmpeg settings switching, and native audio device controls were exercised. Static editor rendering additionally checks layout. The common Windows Save As dialog was observed but the automation helper did not reliably target its child input, so a completed manual-dialog interaction is not claimed.
- All portable package file hashes were verified. A real per-user installer installed and uninstalled in a fresh validation directory; existing settings and a user-file sentinel were preserved. A self-contained packaged native worker produced a decoded H.264 clip, and process module inspection confirmed the C++ runtime loaded from the package directory. These are local packaging/lifecycle checks, not a clean virtual-machine compatibility test.

## Known limits and next priorities

1. Test longer simultaneous audio recordings, device changes, suspend/resume, disk exhaustion, HDR, capture across different GPUs, and more mixed-DPI layouts. Worker isolation bounds native failures, but does not guarantee recovery of an interrupted ordinary MP4 after power loss.
2. Add selected hardware-encoder telemetry and modern WGC minimum-update-interval support before promising 120 unique FPS. Hardware encoding is requested, not authoritatively verified by the current native library.
3. Save editable image projects and multiple documents. Current image files are flattened, and only the active session keeps editable annotation objects. No multiselect or object rotation yet.
4. Improve window screenshots for occluded content and color-managed HDR. Current one-shot GDI screenshots capture visible SDR desktop pixels; WGC video can capture an occluded window.
5. FFmpeg recording supports one DirectShow input and no pause. It does not automatically install loopback drivers or mix independent system/mic inputs. Native recording is the recommended path for these features.
6. Expand live keyboard/screen-reader testing and trimming interaction coverage. Clip export depends on the codecs present in the user's FFmpeg build; Windows preview depends on installed Windows media codecs.
7. Sign release artifacts and validate installer upgrades in clean VMs. The preview installer and executables are unsigned; ARM64 and Windows 10 packages are not provided.

No website uploading, account management, cloud destinations, URL shortening, or telemetry is planned.
