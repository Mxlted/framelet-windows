# Screenshot capture

Framelet uses GDI BitBlt for a one-shot SDR desktop snapshot. This deliberately avoids starting a continuous GPU capture session for a still image. Recording uses its own native pipeline. No ShareX source is included.

All `CaptureTarget.Bounds` coordinates are **physical desktop pixels**, including negative coordinates on monitors to the left or above the primary display. The process declares PerMonitorV2 awareness; native enumeration and capture also enter an explicit per-monitor thread context. DWM extended frame bounds remove the invisible resize border from window screenshots.

Region selection creates one borderless window for each display. HWND positions and sizes are assigned in physical pixels. Each window maps the shared physical selection to its own rendered dimensions, rather than applying one monitor's DPI to the entire desktop. The pointer comes from `GetPhysicalCursorPos`. A drag can cross monitor boundaries.

The overlay shows a frozen desktop. The selected screenshot is cropped from that same frame, with no recapture of the overlay. A small result gets an independent pixel buffer so it does not keep the full desktop alive. Recording callers discard this pending still with `DiscardSelectionSnapshot()` after selecting their target.

## Controls

- Drag and release to capture a region; Shift constrains it to a square.
- Arrow keys move the physical cursor one pixel; Ctrl+arrow moves ten pixels.
- Space starts a keyboard selection; arrows adjust its endpoint; Enter accepts it.
- Window mode highlights the topmost eligible window under the pointer. Click or Enter accepts it.
- Escape or right-click cancels without writing a file.

Global hotkeys use RegisterHotKey with MOD_NOREPEAT, report conflicts, and release every owned registration on shutdown. An invalid replacement leaves the previous shortcut registered.

## Boundaries

- Screenshots represent visible desktop pixels. A window screenshot does not reconstruct content hidden behind another window. Native window **recording** has separate capabilities.
- HDR tone mapping and wide-gamut output are not implemented; stills are 8-bit SDR. HDR displays may produce different colors from the live screen.
- Windows protected content, secure desktop, and display capture restrictions remain in force.
- Areas between nonrectangular monitor arrangements are included as desktop pixels in a spanning selection.
- A desktop snapshot over 512 MiB is rejected with a request to choose a smaller area.

## References

- [Microsoft: GetPhysicalCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getphysicalcursorpos)
- [Microsoft: DWM extended frame bounds](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [Microsoft: BitBlt](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-bitblt)
- [Microsoft: per-monitor DPI awareness](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)

Validation results are recorded in `validation.md`; this design is not itself evidence of a mixed-DPI hardware test.
