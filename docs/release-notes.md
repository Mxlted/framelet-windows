# Framelet 0.1.0-preview.1

First independent preview for Windows 11 x64. Includes physical-pixel screenshot selection, a retained-object annotation editor, native H.264/AAC recording with independent system/microphone audio, optional FFmpeg recording, and focused clip exports.

Native recording uses WGC, D3D11, Media Foundation and WASAPI through ScreenRecorderLib in an isolated worker. The FFmpeg recording checkbox is disabled by default; FFmpeg-based clip processing remains available independently.

Downloads include a self-contained portable ZIP and a per-user installer, with app-local native runtime files and SHA-256 checksums. FFmpeg is not bundled or downloaded automatically. All artifacts are unsigned.

60 FPS is the default target; 120 FPS remains experimental. Hardware encoding is a preference. FFmpeg recording has one DirectShow audio input and no pause. Screenshots are SDR visible-desktop captures, and saved annotations are flattened. Read [validation and remaining work](status.md) before relying on the preview for critical recordings.
