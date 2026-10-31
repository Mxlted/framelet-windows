# Preview status

The first preview is a functional Windows application, not a finished replacement for every capture tool. The implementation and release scope are intentionally visible here for future work.

## Implemented

Region, visible-window, monitor and virtual-desktop screenshots; frozen per-monitor selector; configurable global hotkeys; tray; disk/clipboard image loading; annotated image export; automatic capture actions; atomic settings; dark/light/system theme. The editor retains movable/resizable annotations, layer order, text edits, crop/resize, undo/redo, blur and pixelation. Native recording and optional FFmpeg recording have separate concrete pipelines. Native supports system and mic endpoints, pause/resume, finalization, cancel/restart, and worker isolation. Clip preview, exact and keyframe trim, compression and WebM export are included.

## 0.3.0 preview release checks, October 30, 2026

The UI polish is prepared for `0.3.0-preview.2`. A read-only review of the 52 commits in the latest ShareX pull found one additional applicable improvement: selected annotations now request their object row be brought into view, and property edits preserve object-list scrolling. [Comparison and evidence](ui-polish.md#follow-up-comparison-and-release-preparation).

- Fresh application and full x64 Release solution builds pass with zero warnings/errors. Locked restore passes after updating project-version constraints; external dependency versions are unchanged.
- `scripts/test.ps1`: **172 passed, 0 failed** (Core 44, Editor 54, Capture 16, Recording 25, Session 10, UI 23). Added regressions cover virtualized object selection/reordering, scroll/label preservation, discovery subprocess protocol/failure/deadline handling, and usable settings after device discovery fails.
- File-only FFmpeg processing: **51 passed, 0 failed**, including decoded outputs for all four export modes, source preservation, cancellation cleanup and occupied destinations.
- A fresh self-contained portable ZIP and per-user installer compiled successfully; all **514 package file hashes** passed verification. The native host, app-local CRT, .NET/WPF runtime and dependency notices are included; FFmpeg is not bundled.
- Real isolated device discovery completed successfully with 10 output and 5 input endpoints. This is endpoint-list evidence, not recording or audio-quality validation. Native discovery now stays inside a bounded worker; the first tag's hosted native-enumeration crash and correction are documented in the comparison report.
- Final logs are retained locally under `artifacts/release-check/b7ed9225215d46309c8cfc25ba70f04a/`. No live desktop input, new recording capture, installation or uninstallation was performed during this release check. The live-interaction limitations below remain.

## UI polish pass, October 30, 2026

Shared theme/focus/menu resources, selected navigation, denser home/editor layouts, stable settings actions and validation, readable recording recovery, and explicit clip states are implemented. Pending text selection, accessible navigation, stale media callbacks and source reservation during export have regression coverage. [Changes and evidence](ui-polish.md).

- Application and full x64 Release solution builds pass with zero warnings/errors. `scripts/test.ps1` passes 161 checks: Core 44, Editor 54, Capture 16, Recording 17, Session 10, UI 20.
- File-only FFmpeg checks pass 51 checks (44 base plus 7), including four exported formats with complete decode, unchanged source hashes, cancellation cleanup and occupied-destination preservation.
- Six live baseline images and 22 static after-state renders are retained locally. Updated README/editor images are explicitly labeled static. Layout assertions and 96/120/144/192-DPI raster outputs do not establish live monitor-DPI behavior.
- Computer Use was stopped with physical Escape before live after-validation. Complete capture/clipboard/save, keyboard-only, tray/unsaved-dialog, native recording/recovery and live clip-preview checks remain unverified in this pass. Desktop/native opt-in suites were not rerun; the October 26 live evidence below is historical.
- Native remains the default; recording backends, media ownership, capture coordinates and effect-preview scheduling are unchanged. ShareX is unchanged. No shipping or installation actions were performed during the original polish pass.

## Audit fixes validated on October 26, 2026

All eight confirmed audit findings are implemented: recording recovery through disposal, correct translucent effects, image size checks before full decoding, painted crop bounds, denied-access settings recovery, refreshed delayed window geometry, transactional hotkeys, and asynchronous scaled effect previews. [Implementation, files, regressions, commands and measurements](audit-fixes.md).

- `scripts/build.ps1` and the full x64 Release solution build pass with zero warnings and errors. `scripts/test.ps1` passes 141 checks: Core 44, Editor 54, Capture 16, Recording 17, Session 10.
- FFmpeg integration passes 52 checks (44 base plus eight integrations), including a real late collision and full recovery/Save As decode. The native live suite passes 26 checks with an empty PATH, including pause/resume, audio, cancellation, natural completion, both recovery cases, worker failure and external recovery decoding. The desktop-capture suite passes 19 checks including a moved/resized synthetic window, unavailable targets, and preserved frozen snapshots.
- All seven real MP4 outputs from the final native run fully decode externally, including both recovery files and the alternate saved copy. This inspection is separate from native recording dependencies. The audio clip contains 151 video frames over 2.516650 seconds and stereo 48 kHz AAC over 2.538646 seconds; the roughly 22 ms endpoint difference is short-run evidence, not a long-term synchronization guarantee. The requested-120-FPS clip contains 193 frames over 2.015208 seconds (about 95.8 encoded frames/second), not 120 unique FPS.
- The same warmed 4K blur preview workload at 10% zoom changed from 367.87–530.68 ms on the UI thread to 0.02–2.80 ms. Background preview readiness takes 46.05–67.48 ms including coalescing. Exact commit/export remains full resolution.
- A fresh diagnostic portable ZIP and installer compiled successfully; 513 package hashes and worker, self-contained .NET/WPF, app-local CRT and notice checks passed. No install/uninstall, commit, push, tag or publication was performed during this implementation. The initial-preview installation evidence below is historical.

## Initial preview validation on October 26, 2026

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
