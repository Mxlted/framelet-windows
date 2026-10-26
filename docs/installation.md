# Install and build

Framelet preview targets **Windows 11 22H2 or later, x64**. Release packages include .NET and the app-local Visual C++ runtime required by native recording. FFmpeg is optional and is not bundled.

Download the installer or portable ZIP from [GitHub Releases](https://github.com/Mxlted/framelet-windows/releases). Preview artifacts are unsigned.

- **Installer:** installs for the current user in `%LOCALAPPDATA%\Programs\Framelet`, with a Start menu entry and an optional desktop shortcut. No administrator account is required. Uninstall through Windows Installed apps.
- **Portable ZIP:** extract the entire archive into a writable folder and run `Framelet.exe`. Keep `Framelet.NativeHost.exe` and every accompanying DLL beside it.

Both distributions currently keep preferences in `%LOCALAPPDATA%\Framelet`; "portable" refers to installation, not isolated settings. Captures default to `Pictures\Framelet`, and can be moved in Settings. Uninstall preserves preferences and captured files. Close Framelet, including its tray icon, before upgrading.

Native recording starts and saves without FFmpeg. For FFmpeg recording or clip processing, install a Windows FFmpeg build using the link in Settings and select `ffmpeg.exe`. No component downloads FFmpeg in the background. Windows N editions may need Microsoft's [Media Feature Pack](https://support.microsoft.com/en-us/windows/media-feature-pack-for-windows-n-8622b390-4ce6-43c9-9b42-549e5328e407).

## Development

Install the .NET 10 SDK compatible with `global.json`. From a PowerShell 7 terminal in this repository:

```powershell
./scripts/build.ps1
./scripts/test.ps1
```

The ordinary test command runs deterministic console checks and does not capture the desktop or microphone. On an interactive Windows machine, opt into real capture tests with `./scripts/test.ps1 -DesktopCapture -LiveRecording`. These checks use the current desktop and default audio endpoints. They are deliberately excluded from hosted CI.

Use `-FfmpegIntegration` to additionally exercise an installed FFmpeg against generated test clips. Live recording outputs use a new folder in `artifacts/recording-check` for each run.

## Packaging

Install Inno Setup 6 and the Visual Studio 2022 C++ redistributable build components explicitly, then run:

```powershell
./scripts/package.ps1 -Version 0.1.0-preview.1
```

The script publishes both the application and isolated native worker as self-contained win-x64 applications, copies only the licensed release CRT DLLs, includes dependency notices, creates the ZIP and per-user installer, and verifies every ZIP entry against its SHA-256 manifest. It does not download or install packaging tools. Use `-PortableOnly` to omit Inno Setup, `-InnoCompiler` for a custom compiler location, and `-VCRuntimeDirectory` for a custom licensed x64 `Microsoft.VC143.CRT` directory. Existing named release artifacts require explicit `-Force` replacement.

Outputs go to `artifacts/releases`. Unpacked publish and verification directories remain under `artifacts` for inspection. `SHA256SUMS.txt` inside the ZIP covers package contents; the release hash file covers the ZIP and installer.

With Framelet fully closed, `./scripts/check-installer.ps1 -Installer <setup.exe>` validates installation and uninstallation in a fresh `artifacts/installer-check` directory. It refuses to run if this user already has a registered Framelet installation, preserves user-created files, and checks that existing settings remain unchanged.

Microsoft allows redistribution of unmodified release runtime files from the licensed Visual Studio redist directory. App-local deployment avoids a machine-wide prerequisite, but makes Framelet responsible for distributing runtime updates. See [Microsoft's redistribution list](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution) and [deployment guidance](https://learn.microsoft.com/en-us/cpp/windows/determining-which-dlls-to-redistribute).

The Windows workflow builds, tests, and packages every main-branch change and pull request. Version tags create a **draft prerelease** for review; the workflow does not publish it automatically or replace an existing release. Hosted tests do not validate GPU capture, actual audio synchronization, or interactive UI behavior.
