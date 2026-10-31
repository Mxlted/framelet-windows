# UI polish and validation

Implementation pass begun October 30, 2026. Framelet remains a WPF/.NET Windows utility with local files, native recording by default, and separate optional FFmpeg recording and clip processing. The existing deletion of `docs/prompts/ui-ux-polish.md` was present before this pass and was preserved.

## Direction and before-state inventory

The interface uses neutral charcoal and gray in dark mode, neutral surfaces in light mode, restrained warm selection accents, compact controls, and visible keyboard focus. Capture, annotation, copy and save remain the primary workflow. Existing vector artwork is retained; navigation and command icons are code-native vectors.

The original application was inspected on the available desktop through the computer-use skill. Baseline screenshots are retained locally under `artifacts/ui-polish/5a6d5d6452ac4cf5981b529934cef36e/`. These are live application captures, separate from static layout tests.

| Baseline image | Observed problem or review target | Workflow and intended improvement |
| --- | --- | --- |
| `before-home-dark.png` | Oversized introductory copy and cards; no visible current-page selection in the navigation rail. | Replace filler with compact capture/open/paste actions, clearer navigation and readable recent files. |
| `before-editor-empty-dark.png` | The tool rail needed scrolling at the default window size; properties and actions appeared available without an image. | Fit the complete tool set, distinguish active tools and disable unavailable operations. |
| `before-editor-empty-light.png` | Light-theme comparison of the same empty editor. | Keep hierarchy, focus, borders and disabled controls consistent across themes. |
| `before-editor-dark.png` | Loaded-image baseline for canvas space, contextual properties and object-list density. | Reduce rail and pane overhead while keeping property and document actions discoverable. |
| `before-editor-light.png` | Light-theme counterpart of the loaded editor. | Compare the same editing workflow after the shared-resource changes. |
| `before-settings-dark.png` | Baseline settings layout. Footer/error behavior and expanded recording sections were also reviewed in source. | Keep Save/Cancel visible, retain drafts, and place validation or dependency feedback close to its context. |

Recording/recovery and clip state handling were reviewed in source before implementation. No baseline live recording or clip screenshot is implied by this inventory.

## Implemented changes

| Area | Change and purpose | Principal files |
| --- | --- | --- |
| Shared controls | Semantic focus and error brushes, consistent compact sizing, visible list/combobox focus and selection, primary/destructive button styles, and themed menus. Theme changes update resource references rather than rebuilding editable views. | `App.xaml`, `App.xaml.cs`, `Ui.cs` |
| Shell and home | Persistent navigation selection, vector icons with accessible names, a denser action area, readable recent-file rows with full-path tooltips, and explicit Open selected plus Enter support. Output paths and status text have bounded layout. | `MainWindow.xaml`, `MainWindow.xaml.cs` |
| Editor | Compact complete tool rail, persistent selection edge and keyboard focus, grouped contextual controls with explicit Tool defaults/Selected object scope, disabled unavailable actions, larger usable canvas and object-list area, long-label tooltips, and named zoom/property controls. The blur/pixelation concealment warning remains visible at the point of use. | `Editor/ImageEditorView.xaml`, `Editor/ImageEditorView.xaml.cs` |
| Resize | Named dimension fields with access keys, consistent primary action, and expanding semantic validation feedback that retains entered values. | `Editor/ResizeImageDialog.cs` |
| Settings | Compact categories and fields, a fixed action footer with scrollable content/errors, preserved drafts, focused field validation, clearer native/FFmpeg distinctions, and bounded dependency checks with local feedback. | `SettingsWindow.cs`, `Ui.cs` |
| Recording | Separate state, timer, target and backend hierarchy; clear Start, Stop & save, Restart take, Discard and recovery actions; scrollable details and a selectable/copyable recovery path. The window can resize while keeping actions outside the details scroller. | `RecordingWindow.cs` |
| Clip preview | Explicit empty/loading/playing/paused/unavailable states, time and duration display, grouped in/out controls, mode-specific export explanations, export progress/cancellation, and useful codec/dependency errors. Source and trim values remain available after failures. | `ClipView.cs` |

## Focused behavior corrections

- Selecting another annotation now explicitly commits pending text before changing the selected object. This protects a text draft when selection triggers a document/list refresh.
- Recent files use one case-insensitive image/clip routing path, including supported clip extensions. Enter and Open selected perform the same action as a row double-click; double-clicking unused list space does not reopen the old selection.
- If Windows cannot preview a clip, a finite, valid manually entered in/out range can still be exported through FFmpeg. Preview failure does not discard the source or entered values. Exact and keyframe-based lossless modes retain their distinct semantics.
- The recording recovery Save As dialog no longer offers an overwrite confirmation that the recovery publisher cannot honor. Publication still refuses existing destinations, reports the failure, and retains the completed recovery source. Closing recovery retains that source too.
- The WPF ComboBox popup and menu templates preserve their native control contracts; navigation also responds to accessibility selection. Old media-player callbacks are ignored, export reserves its source before the destination dialog, and playback failures cannot overwrite export progress or results.
- Settings errors identify the relevant category/field where possible and preserve the draft. Existing transactional hotkey reservation, rollback and atomic settings persistence remain the underlying save mechanism.

The pass does not change the capture coordinate model, annotation renderer, scaled asynchronous effect preview scheduler, native worker isolation, backend defaults, atomic publication, source-media ownership, or bounded recording cancellation/disposal. All eight audit fixes and their existing regression coverage remain required. The read-only ShareX checkout is not an implementation dependency.

## Validation record

Final checks used .NET SDK 10.0.403. Diagnostic files are in `artifacts/ui-polish/5a6d5d6452ac4cf5981b529934cef36e/`; the final command logs are `build-verified.log`, `tests-verified.log`, `solution-verified.log`, `ffmpeg-processing.log`, and `render-verified.log`.

| Check | Command or scenario | Result |
| --- | --- | --- |
| Application build | `./scripts/build.ps1` | Exit 0; zero warnings/errors. |
| Deterministic suites | `./scripts/test.ps1` | Exit 0; **161 passed, 0 failed**: Core 44, Editor 54, Capture 16, Recording 17, Session 10, UI 20. All original audit regressions remain. |
| Full solution | `dotnet build Framelet.sln -c Release -p:Platform=x64 --no-restore` | Exit 0; zero warnings/errors. |
| File-only media | `dotnet run --project tests/Framelet.Core.Tests -c Release -p:Platform=x64 -- --ffmpeg-processing` | Exit 0; **51 passed** (44 base plus 7 media checks). A generated H.264/AAC clip exported through Exact MP4, Lossless trim, Smaller MP4 and WebM; every output fully decoded with no decoder errors. Source SHA stayed unchanged. Pre-cancellation left no staging files; an occupied destination kept its sentinel. No desktop/audio capture. |
| Static WPF evidence | `dotnet run --project tests/Framelet.Ui.Tests -c Release -p:Platform=x64 --no-build -- --render <fresh-directory> --sample <sample.png>` | Exit 0; **22 PNGs** and a manifest under `verified-ui/`. Both themes, empty/loaded/annotated editor, minimum Text toolbar, home, settings, clip empty/codec error, recording Ready/recovery. Recovery and codec error are injected states. |
| Layout/accessibility regressions | UI suite | Production controls at minimum/ordinary/large layout sizes; navigation selection provider and visible page agreement; combo/menu accessibility contracts; property groups without command collisions; all tools fit minimum editor height; pending text/source/dirty state retained; settings drafts and validation; long recovery path visibly reachable above fixed actions; clip source/callback/export cancellation state. These are programmatic checks, not physical keyboard or screen-reader operation. |
| Live baseline | Computer-use inspection before implementation | Home, empty/loaded editor, settings and theme switching inspected; six images listed above. The sample was opened via launch argument after the helper failed to reliably target the common Open dialog's filename field. |
| Final review | `git diff --check`, source and static-image inspection | Whitespace check passes. Shared theme/control resources, selected states, disabled controls, media callback identity, file ownership and defaults reviewed. ShareX remains clean. Original local settings restored byte-for-byte. |

Reproduce a generated sample with `dotnet run --project tests/Framelet.Editor.Tests -c Release -p:Platform=x64 -- --sample <sample.png>`, then use the render command above. The renderer refuses a nonempty output directory. It shows no windows and changes no persisted settings.

The final README/editor images are static WPF renders with generated editable annotations. For the closest comparison to the unannotated live baseline, use `verified-ui/static-editor-dark.png` and `static-editor-light.png`; title bars and raster density differ. The manifest records exact dimensions. Minimum-layout images at 96, 120, 144 and 192 DPI change output raster density while holding layout DIPs fixed. They are not real monitor-DPI or mixed-DPI tests.

The editor's render and effect scheduling code is unchanged, so this polish pass does not introduce a new preview-performance claim. Existing benchmark measurements remain historical in [audit fixes](audit-fixes.md).

## Evidence boundaries and shipping

Computer Use was stopped with the physical Escape key when the updated app was being selected. No further desktop input was sent. Consequently, live after-state comparison, complete capture/open/paste → annotate → copy/save interactions, keyboard-only and unsaved-dialog paths, tray/restore/quit behavior, actual DPI changes, and live native recording/clip-preview/recovery interaction remain **unverified for this pass**. The opt-in desktop-capture and native recording suites were not rerun. The common Windows Open/Save dialog interaction also remains unverified. Static renders and generated-media export checks do not fill these gaps.

No claim is made for hardware encoder selection, 120 unique captured frames per second, long-term audio/video synchronization, clean-VM compatibility or signed artifacts. During the original polish pass, no commit, push, tag, publication, package installation or uninstallation was performed. Existing release artifacts and the user's pre-existing prompt deletion were preserved. The later release preparation is recorded below.

## Follow-up comparison and release preparation

The October 30 ShareX pull advanced the read-only reference from `f3ae963af` to `43f126ae2`, adding 52 commits. Its new Photo editor, statistics and sharing capabilities are outside this release's scope. Relevant behavior changes were compared with the current Framelet implementation:

| ShareX change | Framelet result |
| --- | --- |
| Recent files on first open (`966fe475f`) | Already refreshed by the main window's Loaded event and Home navigation. |
| Region Enter/crosshair handling (`e4071972b`) and dimming (`f880582fa`) | Enter already accepts the selection and the chosen region stays visible. Framelet separates capture from annotation. |
| Drag-first crop (`997bd75ba`) | Already starts from a drag and requires Apply or Enter. |
| Initial zoom at most 100% (`5668552d3`) and device-pixel edges (`3427cfe6d`) | Already limits fit zoom. Existing WPF layout rounding and pixel-snapping flags were confirmed in source; exact visual equivalence at different display scales was not established. |
| Neutral themes, compact fields, toolbar and slider fixes | Existing polish covers the applicable controls; several changes fix Avalonia-specific templates. |
| Keep active history row visible (`ba329ffac`) | A comparable Framelet gap was reproduced with 30 annotations: canvas selection left the corresponding object row virtualized and offscreen. An original focused correction reveals newly selected objects while preserving deliberate scrolling during property-only refreshes. |

No ShareX code or assets were copied. No recording, media or FFmpeg backend changes were present in the pull. Upstream Framelet README cleanup and prompt deletion were synchronized before release preparation. The release version is `0.3.0-preview.2`; the evidence boundaries above still apply.

Two added UI regressions cover selected-row realization in a long virtualized list, selection identity after layer reordering, and scroll preservation with updated text labels after property edits. The original code failed these checks. Offscreen tests establish row realization and state preservation, not live viewport visibility.

Fresh release checks passed: application and full x64 Release solution builds with zero warnings/errors, **163 deterministic checks** (including 22 UI checks), and **51 file-only FFmpeg processing checks**. Logs are retained locally in `artifacts/release-check/fd255108aca0428099438652a6835c11/`. No additional desktop input, native recording, installation or uninstallation was performed.

The first tag, `v0.3.0-preview.1`, failed both hosted workflows before packaging and was not published. The new UI suite opened Recording settings and accidentally invoked physical device enumeration. ScreenRecorderLib terminated with `0xC0000005` in `ListAudioDevices`. Its [7.0.1 source](https://github.com/sskodje/ScreenRecorderLib/blob/v7.0.1/ScreenRecorderLibNative/CoreAudio.util.cpp#L62-L66) dereferences a default device without checking that discovery succeeded; this can also affect systems without a default endpoint. The exact endpoint state of the hosted runner was not captured.

Settings tests and static renders now inject named audio-device fixtures. Production discovery uses the isolated native worker with bounded output and lifetime, so a native discovery failure becomes an unavailable-list error while the app and draft settings remain intact. Device resolution during recording remains inside the recording worker. Fixtures establish UI and subprocess behavior; they do not validate capture or audio quality.

After this correction, fresh application and full x64 Release solution builds passed with zero warnings/errors, all **172 deterministic checks** passed (Core 44, Editor 54, Capture 16, Recording 25, Session 10, UI 23), and all **51 file-only FFmpeg checks** passed. Real isolated device discovery returned 10 output and 5 input endpoints. Native-exit and timeout cases use subprocess fixtures; no physical endpoint was removed and no new recording was captured. Final logs are in `artifacts/release-check/b7ed9225215d46309c8cfc25ba70f04a/`.

## API references checked

- Context7 `/dotnet/wpf` and the [WPF ComboBox template/source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/Themes/XAML/ComboBox.xaml) for `PART_Popup`, keyboard focus and selection.
- [WPF RadioButton automation peer](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Automation/Peers/RadioButtonAutomationPeer.cs) for selection independent of Click.
- [WPF media event dispatch](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/mediaeventshelper.cs) for queued callbacks and source ownership.
- [Microsoft SetCurrentValue](https://learn.microsoft.com/en-us/dotnet/api/system.windows.dependencyobject.setcurrentvalue?view=windowsdesktop-10.0) for preserving bound settings values when a file/folder picker supplies a value.
