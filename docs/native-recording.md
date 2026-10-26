# Native recording

Framelet records through Windows Graphics Capture, Direct3D 11, Media Foundation and WASAPI. FFmpeg is not loaded or launched by the native backend, including finalization and cancellation. The default output is H.264 High Profile video and 48 kHz stereo AAC in MP4.

## Reference review and independent decision

The read-only ShareX reference was inspected at `f3ae963af1c03c242f58adf162d3dffc1d45c657` (2026-10-26), after the [22.0.0 release](https://getsharex.com/changelog). `ScreenRecordManager` selects distinct native and FFmpeg paths, prepares a session, routes pause/stop/abort, and awaits finalization. Its native `ScreenRecorder` coordinates a free-threaded WGC frame pool, GPU composition and NV12 conversion, Media Foundation encoding, texture ownership, and a shared clock. WGC frame timestamps and WASAPI QPC packet positions are mapped onto the recording timeline; paused time is excluded. Separate audio streams are resampled, drift-corrected, mixed, and padded with silence when packets are absent. Region coordinates are physical desktop pixels, including negative monitor origins. The current ShareX implementation also explicitly handles Windows' newer minimum-update-interval capture API for high refresh rates.

Framelet uses the same Windows API architecture through [ScreenRecorderLib 7.0.1](https://github.com/sskodje/ScreenRecorderLib), an MIT-licensed native component independent of ShareX. Framelet's source selection, configuration mapping, worker protocol, state transitions, file ownership, and UI are new code. No ShareX source, editor, icons, or binaries are included. Its GPL reference is not a dependency. ScreenRecorderLib's license is shipped in `Licenses/ScreenRecorderLib.txt`.

Implementing another Media Foundation encoder, device-clock resampler, GPU texture compositor, and codec lifecycle at once would increase the most failure-prone part of the first release. The selected component already provides WGC display/window sources, WASAPI device selection and mixing, a Media Foundation presentation clock, drift correction, pause/resume, and sink finalization. Its 7.0.1 release specifically fixes silent-WASAPI padding stalls. This is a deliberate dependency tradeoff, not an assertion that its internals are identical to ShareX's.

## Process and file lifetime

`NativeRecordingBackend` supervises one `Framelet.NativeHost.exe` per session. The private protocol is JSON lines over inherited standard input/output; no port, service, account, or network connection is created. A host handles one recording. Only known operations are accepted. Standard error is continuously drained with an 8 KiB retained limit.

A real local test exposed a stalled native display startup. Isolation was added so a stuck capture driver can be terminated without losing an open image editor. Startup has a 25-second deadline; pause/resume 5 seconds; stop 45 seconds; cancel 8 seconds. The supervisor first closes the command stream, then terminates only its own worker if necessary. It removes only that session's known staging path. The UI remains responsive while these operations run.

The worker opens a unique `.framelet-<session>.recording.mp4` with `CreateNew`. It records into that stream, waits for Media Foundation's finalization callback, flushes it, closes native resources, then moves the file to the requested destination without overwrite. An existing destination is never replaced. If the final move fails, the completed staging MP4 is preserved and its path is reported. Cancellation and failed recordings are discarded. Power loss or forcibly terminating the entire application can leave a staging file; ordinary MP4 is not guaranteed recoverable after abrupt termination.

After successful encoder completion, the worker flushes and closes its stream and announces finalized staging before fallible native teardown or publication. The supervisor, backend disposal, and automatic session cleanup retain that file even when publication fails or the worker then exits. The separate FFmpeg backend follows the same finalized-file ownership rule, including when disposal wins a race with the natural-exit observer. The recording window enters a recovery state with the actual path, **Copy details**, and **Save recording as…**. Save As never overwrites a destination and retains the recovery source even after a successful copy; it can be removed manually once the saved copy is checked. Closing the recovery window also leaves the file in place.

The capture window closing or becoming minimized stops and finalizes the clip. A display-layout change also finalizes rather than continuing against stale geometry. Window resizing is handled by WGC frame-pool recreation and fitted into the original fixed output dimensions. It does not change the encoded frame size halfway through a file.

## Sources, encoding and audio

- Region capture intersects the physical rectangle with each monitor and places the source crops onto one fixed canvas. Monitor capture is the corresponding one-display case. WGC is selected explicitly; it never silently changes to Desktop Duplication or FFmpeg.
- Window capture uses its HWND and continues through ordinary occlusion. Minimized, destroyed and protected windows are distinct limitations; no protected-content bypass is attempted.
- Odd output dimensions are reduced by one pixel to satisfy 4:2:0 H.264 requirements. Coordinates remain physical pixels; DPI scaling belongs to the selection UI.
- Hardware preference enables Media Foundation hardware transforms and its D3D manager. Windows can select a software encoder. Framelet therefore says **Prefer hardware encoding**, not **hardware encoder confirmed**. The library does not expose authoritative selected-encoder telemetry.
- System audio and microphone are independent WASAPI sources with separately persisted endpoint IDs. Empty IDs mean the current default endpoint. Unavailable explicitly selected devices produce a clear error instead of switching to a different device.
- Native AAC supports 96, 128, 160 and 192 kbps. WASAPI sources are mixed into stereo. Microphone permissions are controlled by Windows.
- Fixed-frame-rate mode schedules the requested timeline, but capture/encoder throughput can fall below it. The 120 FPS setting is experimental: the component does not expose the newer WGC `MinUpdateInterval` setting used by current ShareX. Do not claim 120 unique captured frames per second from an output stream's nominal rate alone.

The native component requires the Microsoft Visual C++ x64 runtime and Windows Media Foundation. Windows N installations may need the Media Feature Pack. FFmpeg is neither a substitute for these prerequisites nor a native startup dependency.

## Validation

Run deterministic checks without screen/audio capture:

```powershell
dotnet run --project tests/Framelet.Recording.Tests -c Release
```

Run the opt-in desktop integration suite (opens a moving test chart, records brief local clips and plays a quiet 440 Hz loopback test tone):

```powershell
dotnet run --project tests/Framelet.Recording.Tests -c Release -- --live --output artifacts/native-validation
```

Use a new output directory each run. Existing files intentionally cause a failure. Integration outputs and native diagnostic logs are excluded from Git. For native diagnostics, set `FRAMELET_NATIVE_LOG` to an absolute local log path before launching. Logs contain device names and local paths; review them before sharing.

Add `--without-ffmpeg` to the live suite to clear the test/worker PATH and configure an unavailable FFmpeg path. Native recording still starts, pauses/resumes, stops, and finalizes. Inspect generated files with a separately launched external decoder afterward. Deterministic subprocess fixtures test protocol and cleanup races, but their dummy bytes are not codec validation. The live suite adds late destination collisions and natural-completion recovery with real MP4 output. See [audit validation](audit-fixes.md).

The deterministic checks cover destination preservation, output format, dimensions, settings, cancellation before startup, inactive pause, and staging cleanup. Live checks cover window recording, pause/resume, system/microphone audio, region recording, cancellation, forced worker failure, and natural completion after the captured window closes. Live results from the development machine are recorded separately from these runnable assertions; short recordings do not establish long-duration synchronization, capture on every GPU, or 120 FPS throughput.

Relevant primary documentation: [Windows screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture), [WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording), [Media Foundation sink writer](https://learn.microsoft.com/en-us/windows/win32/medfound/sink-writer), [ScreenRecorderLib API guide](https://github.com/sskodje/ScreenRecorderLib/wiki/Quickstart-guide-v7.x.x-and-newer).

### Local evidence, October 26, 2026

The Windows 11 development machine completed the 13-check desktop suite with exit code 0. A 640×400 moving window recorded 169 H.264 frames in 2.81665 seconds at an average 60 FPS; the one-second pause was excluded. The combined-audio clip contained 48 kHz stereo AAC. Its audio/video stream durations differed by 0.66 ms, and a quiet 440 Hz loopback tone was present in the decoded audio. Native logs showed packets from both the selected render endpoint and microphone. This verifies short simultaneous capture, not perceptual microphone quality or long-duration lip sync.

A requested-120-FPS region clip contained 207 frames over 2.005775 seconds, about 103.2 FPS. That result explicitly does not meet a sustained 120 FPS claim. The software-encoding path, other GPU families, HDR tone mapping, long recordings, physical audio-device removal, and display reconfiguration during capture still need broader hardware validation. The forced-worker-termination check verified that the test application stayed alive and no final clip or staging file remained. Decoded frames were inspected visually.
