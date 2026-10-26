# Framelet 0.2.0-preview.1

This preview fixes the eight confirmed Framelet audit findings and adds a reusable [UI/UX polish prompt](prompts/ui-ux-polish.md) for the next design pass.

- Completed native and FFmpeg recordings survive final-save collisions, automatic cleanup, and disposal. The recording window reports the actual recovery path and offers Save recording as without overwriting existing files.
- Blur and pixelation correctly replace translucent pixels, and effect previews use scaled background processing. The measured warmed 4K blur workload at 10% zoom dropped from 367.87–530.68 ms to 0.02–2.80 ms in the UI render call; preview readiness took 46.05–67.48 ms. Committed exports remain full resolution.
- Image imports enforce size limits before full decoding, and cropping preserves visible thick strokes, caps, arrowheads, and crossing shapes as editable objects.
- Unreadable settings recover safely with a warning. Delayed window capture refreshes the originally selected HWND's bounds. Hotkey changes preserve working registrations and saved settings on conflicts or failed saves.

Validation: 141 deterministic checks passed; the FFmpeg integration suite passed 52 checks, desktop capture passed 19, and native live validation passed 26 with FFmpeg unavailable during recording. Generated recording and recovery files decoded successfully using an external inspector afterward. See [audit validation](audit-fixes.md) for exact commands, measurements and evidence limits.

Downloads include the self-contained Windows x64 portable ZIP, per-user installer, and SHA-256 checksums. Native recording remains the default, FFmpeg remains optional and separately selected, and clip processing is independent. Artifacts are unsigned. No installation or uninstallation was performed for this release; clean-VM compatibility, hardware encoder selection, sustained 120 unique FPS and long-term synchronization remain unverified.

## Previous preview: 0.1.0-preview.1

First independent preview for Windows 11 x64. Includes physical-pixel screenshot selection, a retained-object annotation editor, native H.264/AAC recording with independent system/microphone audio, optional FFmpeg recording, and focused clip exports.

Native recording uses WGC, D3D11, Media Foundation and WASAPI through ScreenRecorderLib in an isolated worker. The FFmpeg recording checkbox is off by default; FFmpeg-based clip processing remains available independently.

Downloads include a self-contained portable ZIP and a per-user installer, with app-local native runtime files and SHA-256 checksums. FFmpeg is not bundled or downloaded automatically. All artifacts are unsigned.

60 FPS is the default target; 120 FPS remains experimental. Hardware encoding is a preference. FFmpeg recording has one DirectShow audio input and no pause. Screenshots are SDR visible-desktop captures, and saved annotations are flattened. Read [validation and remaining work](status.md) before relying on the preview for critical recordings.
