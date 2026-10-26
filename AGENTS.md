# Working on Framelet

Framelet is a Windows-only, local-first screenshot, annotation, and short-clip application. Keep the editor and capture-to-save flow central. No accounts, uploading, telemetry, or unrelated productivity tools.

- The sibling `../sharex/` directory is a read-only reference and is never a dependency. Do not copy ShareX code or assets into this MIT project.
- WPF / .NET 10, Windows 11 22H2+, x64. Do not introduce WinUI 3 or a browser shell.
- Native recording is the default. `UseFfmpeg` must remain false by default and must select a separate backend. No silent fallback or dependency download.
- Native capture must start, stop, and finalize without FFmpeg. `ClipProcessor` may require FFmpeg regardless of the recording preference.
- Keep screen geometry in physical desktop pixels, including negative monitor origins. Editor geometry is image pixels; UI layout is WPF DIPs.
- Keep native drivers/codecs in the existing isolated host. Use the backend contract and bounded shutdown. Preserve source media and publish finalized files atomically.
- Use dynamic neutral light/dark resources. Prefer dense, readable controls, restrained corners, visible keyboard focus, and contextual editor properties.
- Run `scripts/test.ps1` for deterministic tests. Use its documented live options only on an interactive unlocked desktop. Report actual frame/audio evidence separately from build/static tests.
- Read `docs/architecture.md`, `docs/native-recording.md`, and `docs/status.md` before substantial changes. Keep unfinished work and actual validation current.
- Release packages include both Framelet and its native host, .NET runtime, required app-local CRT, and third-party notices. FFmpeg is user supplied. Never claim artifacts are signed unless signing was actually performed.
