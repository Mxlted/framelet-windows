# Image editor

The editor works in image pixels. A screenshot at 200% desktop scaling is still edited and exported at its captured pixel dimensions. The view zoom and display DPI do not change the document size.

Choose a tool in the left rail, draw on the image, then use **Select** to move or resize the object. The object list provides ordering, duplication and deletion. Text, arrows, rectangles, ellipses, pen strokes, highlights, blur and pixelation remain editable while the document is open. Cropping and resizing preserve the remaining annotations as objects. Undo includes those operations.

The compact rail keeps the tools together. Contextual controls show only the properties relevant to the current tool or object, while undo, redo and resize stay separate. **Tool defaults** means the values apply to the next annotation; **Selected object** means changes also edit the selected annotation. Those changed values remain available for the next object. The **Objects** control hides or restores the object list to give the canvas more room. Long object names are shortened in the list and available in tooltips.

Selecting an annotation on the canvas brings its row into view in a long object list. Property edits update row labels without resetting the list or changing a deliberate scroll position.

| Action | Shortcut |
| --- | --- |
| Select / arrow / rectangle / ellipse | V / A / R / O |
| Text / pen / highlight | T / P / H |
| Blur / pixelate / crop | B / X / C |
| Constrain a shape or arrow | Hold Shift while drawing |
| Apply / cancel a crop | Enter / Escape |
| Undo / redo | Ctrl+Z / Ctrl+Shift+Z or Ctrl+Y |
| Duplicate / delete an object | Ctrl+D / Delete |
| Nudge an object | Arrow keys; hold Shift for 10 pixels |
| Bring forward / send backward | Ctrl+] / Ctrl+[ |
| Fit / actual pixels | Ctrl+0 / Ctrl+1 |
| Zoom / pan | Ctrl+wheel / middle-drag or Space-drag |

For text, enter the wording in the contextual toolbar and click the image. Select an existing text object or double-click it to edit the wording. Enter applies the edit. Choosing another object in the object list commits pending text before changing the selection. Pending text also counts as unsaved work and is committed before copying or saving. Resizing a text object scales its font and reflows its text.

Export and clipboard copy rasterize the document. Blur and pixelation process the pixels of the image **and earlier annotations**, so changing layer order can change their result. These effects run locally and write altered pixels into the exported image. Use an opaque filled rectangle when information must be completely concealed: a weak blur or small pixel blocks can leave information recognizable.

Processed regions replace all four pixel channels, including alpha. Translucent effects do not blend back over the unprocessed source. Cropping tests painted bounds, including thick strokes, round caps, and arrowheads, then translates and clips the retained editable objects. Text is conservatively retained because glyphs can extend outside their layout box.

The renderer caches the committed document for responsive panning and drawing. During object dragging it renders the moving object above the other objects; the final layer order is restored when the drag finishes. A blur/pixelation preview can therefore differ where it overlaps later objects until committed.

Effect drags use a scaled preview with at most one million pixels, processed on one background worker with a replaceable pending request and 33 ms coalescing. Only frozen sources cross threads; WPF visual drawing stays on the dispatcher. Geometry, zoom, source, selection, tool, document edits, and unloading invalidate pending results. Preview scaling is approximate. Committed rendering and export keep the exact full-resolution filter; committing a large effect can still take longer than a preview frame.

Undo stores immutable bitmap references for annotation changes and limits history to 64 operations, with a 256 MiB budget for source images in history. The current image and at least one undo step can exceed that budget. Images are limited to 64 megapixels and 16,384 pixels per side to avoid exhausting desktop rendering resources.

File imports inspect PNG header dimensions and deferred WIC frame dimensions before full pixel copying. Decoding uses one owned stream and produces a detached frozen bitmap. Failed imports leave the current document intact and release file handles. Clipboard images are already materialized by Windows when supplied to Framelet; their dimensions are checked before Framelet's own normalization copy.

This version exports flattened images. It does not yet save editable project files, rotate objects, select several objects at once, or preserve multiple frames from animated images. Object edits are retained only while that image remains open; the application checks unsaved changes before replacing it.

## Validation

The [dark editor layout](images/editor-dark.png) and [light editor layout](images/editor-light.png) are static WPF renderings of a generated sample, not live interaction evidence. See [UI polish and validation](ui-polish.md) for the separate before/after desktop observations, layout checks and remaining limitations.

Run `dotnet run --project tests/Framelet.Editor.Tests -c Release` on Windows. The test runner uses an STA thread and exercises DPI normalization, pixel output and compositing, annotation transforms, crop/resize, undo/redo branching, invalid bounds, and history limits. `-- --render artifacts/editor-preview.png` additionally creates a static UI rendering with a generated sample image. That static rendering is a layout check, not proof of live mouse or keyboard interaction.

The suite also covers complete translucent/transparent/opaque effect exports, overlapping filters, painted crop intersections, decoder ownership, and preview cancellation/stale callbacks. The oversized PNG experiment runs in a child process with a 384 MiB memory cap and 15-second timeout. `-- --benchmark-preview` reports warmed 4K blur preview timings without machine-speed assertions. Actual measurements are in [audit fixes](audit-fixes.md).
