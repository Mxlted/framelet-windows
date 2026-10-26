# Framelet UI/UX polish prompt

Use the prompt below when starting a dedicated design and implementation pass. Creating this document does not itself apply UI changes.

---

Polish Framelet into a cohesive, professional Windows screenshot and annotation workspace.

This is an implementation task. Inspect the current application, choose a clear design direction, and make the necessary application, test, and documentation changes. Do not stop after an audit, mockup, or proposed plan.

Repository: `C:\Users\Nathan\Desktop\Screenshot\framelet`
Read-only reference: `C:\Users\Nathan\Desktop\Screenshot\sharex`
GitHub: `https://github.com/Mxlted/framelet-windows`

## Start with the current application

1. Read `AGENTS.md`, `README.md`, `docs/architecture.md`, `docs/image-editor.md`, `docs/capture.md`, `docs/native-recording.md`, `docs/status.md`, and `docs/audit-fixes.md`.
2. Inspect Git state and the build/test/package scripts. Preserve unrelated workspace changes and use fresh diagnostic outputs.
3. Read the shared resources in `App.xaml` and theme handling in `App.xaml.cs`, then inspect `MainWindow.xaml`, `Ui.cs`, the editor, `SettingsWindow.cs`, `RecordingWindow.cs`, and `ClipView.cs`.
4. Run the existing app on the available desktop and inspect its actual behavior before changing it. Use the computer-use skill for live UI inspection and interaction.
5. Capture a compact before-state inventory: screenshot, visible problem, affected workflow, and expected improvement. Include both dark and light themes.
6. State the design direction and highest-impact fixes briefly, then implement. Delegate independent areas with explicit file ownership where that helps.

Current layout details are starting points, not assumptions that they still match the running version: the shell has capture/open/paste and copy/save actions, a narrow navigation rail, a home view with recent files, and separate editor and clip views. The editor has a tool rail, contextual toolbar, canvas, object list, and status/zoom controls. Verify their current sizes and behavior.

## Design direction

- Keep the capture → annotate → copy/save flow central. Give the image and its editing controls most of the usable space.
- Use neutral charcoal and gray for dark mode, with a clean neutral light mode. Keep the existing warm accent restrained and purposeful.
- Make the interface feel like a focused Windows utility: readable, compact, predictable, and calm.
- Establish consistent spacing, typography, control heights, alignment, icon sizing, borders, and corner radii across the app.
- Use hierarchy to distinguish primary actions, secondary actions, navigation, selection, and destructive actions.
- Keep Framelet's original identity and existing vector/icon language. Improve inconsistent icons with original, code-native vector assets where useful.
- Avoid decorative gradients, excessive shadows, oversized cards, pill-shaped everything, marketing filler, or large empty areas that crowd the canvas.
- Prefer explicit actions and familiar Windows behavior. A visual refresh must not make a previously direct workflow harder to find or complete.

## Polish the full workflow

### Shell and navigation

- Make the current page visibly selected in the navigation rail, with accessible names and useful tooltips.
- Keep capture, recording, opening, and pasting easy to find. Keep copy/save prominent when an image is available.
- Review the home view for useful density, clear empty states, readable recent-file names, and consistent file-opening behavior.
- Make long filenames, output paths, and status messages readable without displacing primary controls.
- Keep the tray, restore, close, unsaved-document prompt, and application-exit behavior predictable.
- Replace ambiguous glyphs or labels only where this improves recognition and consistency.

### Image editor

- Refine the tool rail so the selected tool, hover state, keyboard focus, and available tools are easy to distinguish.
- Keep contextual controls stable when switching between shapes, text, effects, and crop. Prevent toolbar collisions at the minimum supported width.
- Align color, width, fill, text, font size, strength, crop actions, undo/redo, resize, and object controls into clear groups.
- Preserve useful canvas space when the object list is open. Ensure the object list remains readable with long text and many annotations.
- Improve selection handles, outlines, crop guides, canvas framing, checkerboard, zoom controls, and status text where observed problems justify it.
- Maintain a clear distinction between tool defaults and properties of an existing selected annotation.
- Keep text editing, Enter/Escape behavior, object movement/resizing, layer ordering, keyboard nudging, and undo/redo consistent.
- Preserve dirty-state tracking, pending text edits, source-path handling, and document state during theme changes and navigation.
- Preserve the warning that blur/pixelation can leave information recognizable; opaque filled shapes are the concealment option.

### Settings

- Make category selection, field labels, descriptions, validation, and Save/Cancel placement consistent.
- Keep essential controls visible and scroll secondary detail naturally. Preserve drafts when navigating categories.
- Clearly distinguish native recording settings from FFmpeg recording settings and independent clip-export dependencies.
- Keep errors close to the relevant action or field and provide a useful next step. Preserve entered values after validation fails.
- Check hotkey conflicts, duplicates, disabling, swapping, and save failures without losing working bindings or persisted settings.
- Ensure theme changes preserve editable content and all controls update through semantic dynamic resources.

### Recording and recovery

- Make Ready, Starting, Recording, Paused, Finishing, Recoverable, Failed, and Completed states visibly understandable.
- Keep start, pause/resume, stop/save, restart, cancel, and recovery actions clearly differentiated. Busy states must prevent duplicate operations.
- Give the timer, selected target, backend, and audio state useful hierarchy without expanding the recording window unnecessarily.
- Keep completed-media recovery practical: readable/copyable recovery path, clear Save recording as action, and accurate close behavior.
- Ensure long errors and recovery paths fit at supported DPI without hiding buttons. Preserve window capture-exclusion behavior.
- Keep destructive cancellation/restart distinct from saving a completed recording. Never change file ownership merely to simplify a UI state.

### Clip preview and export

- Clarify playback state, timeline position, in/out points, trim mode, output action, progress, cancellation, and errors.
- Keep exact export and keyframe-based lossless trimming understandable through short, relevant copy.
- Explain missing FFmpeg or unavailable Windows preview codecs at the point of use while retaining the source clip and entered trim values.
- Check empty, loading, playing, paused, exporting, cancelled, failed, and successful states for consistent layout and enabled controls.

## Accessibility, sizing, and responsiveness

- Test keyboard navigation, visible focus, Tab/Shift+Tab order, Enter/Escape behavior, and existing shortcuts across all changed screens.
- Ensure custom control templates preserve accessible names, labels, keyboard operation, focus states, disabled states, and validation feedback.
- Make selection distinguishable without relying only on color. Check text, icons, borders, muted labels, and disabled controls in both themes.
- Inspect minimum window size, ordinary desktop size, and maximized layout. Exercise 100%, 125%, 150%, and 200% scaling where the environment permits.
- Check dropdowns, menus, tooltips, dialogs, scrollbars, long text, and expanded settings pages for clipping or inaccessible controls.
- Keep screen geometry in physical pixels, editor geometry in image pixels, and layout in WPF DIPs. Preserve negative monitor coordinates and mixed-DPI handling.
- Preserve asynchronous scaled effect previews, cancellation, and stale-result rejection. Do not reintroduce full-resolution effect work on every pointer movement.
- Use motion sparingly and only when it helps explain a state change. Keep interaction immediate and cleanup bounded.

## Implementation boundaries

- Stay with WPF/.NET and the existing project boundaries. Prefer shared resources and focused reusable styles over copied per-screen values.
- Preserve all eight audit fixes and their regression coverage, including image preflight, translucent effects, painted crop bounds, settings recovery, delayed-window geometry, hotkey transactions, and recording recovery.
- Native recording remains the default with `UseFfmpeg` false. Keep native and FFmpeg recording separate, and keep clip processing independent.
- Keep the isolated native worker, atomic saves, non-overwriting publication, preserved source media, and bounded cancellation/disposal.
- Keep `sharex/` unchanged. Do not copy its code, assets, or branding into Framelet.
- Do not add accounts, uploads, telemetry, cloud features, a new UI framework, or unrelated editor capabilities as part of polish.
- Preserve existing public behavior unless a demonstrated usability issue requires a focused correction. Explain any such behavior change.
- Use official documentation, current upstream source, and Context7 when WPF or Windows API behavior needs verification.
- Follow the current user's shipping instructions. Commit, push, tag, publish, install, or uninstall only when those actions are authorized in the active task.

## Validate the result

1. Revisit the same before-state scenarios in the running app. Save comparable before/after images in a fresh diagnostic directory.
2. Exercise complete capture/open/paste → annotate → copy/save workflows, plus settings and recording/clip flows affected by the changes.
3. Test both themes, minimum-size layouts, keyboard-only paths, unsaved work, long paths, errors, cancellation, and recovery states.
4. Add meaningful regressions for changed behavior. Avoid tests that only repeat visual constants or assert machine-specific speed.
5. Run `scripts/build.ps1`, `scripts/test.ps1`, and a full-solution x64 Release build. Run the relevant opt-in live checks when the desktop permits.
6. If effect rendering or preview scheduling changes, rerun the warmed 4K preview benchmark and report actual timings with the same workload.
7. Review the final diff for inconsistent styles, hardcoded theme colors, missing accessible labels, stale callbacks, leaked resources, accidental backend changes, and unrelated edits.
8. Update the relevant documentation and screenshots to match the implemented result. Distinguish static rendering, live interaction, native/media inspection, and checks that could not run.

Finish with the changes made and their purpose, relevant files, before/after evidence, commands and actual results, and remaining limitations. Do not claim hardware encoding, 120 unique FPS, long-term synchronization, or clean-VM compatibility without corresponding evidence.
