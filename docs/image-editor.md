# Image editor

The editor works in image pixels. A screenshot at 200% desktop scaling is still edited and exported at its captured pixel dimensions. The view zoom and display DPI do not change the document size.

Choose a tool in the left rail, draw on the image, then use **Select** to move or resize the object. The object list provides ordering, duplication and deletion. Text, arrows, rectangles, ellipses, pen strokes, highlights, blur and pixelation remain editable while the document is open. Cropping and resizing preserve the remaining annotations as objects. Undo includes those operations.

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

For text, enter the wording in the contextual toolbar and click the image. Select an existing text object or double-click it to edit the wording. Enter applies the edit. Resizing a text object scales its font and reflows its text.

Export and clipboard copy rasterize the document. Blur and pixelation process the pixels of the image **and earlier annotations**, so changing layer order can change their result. These effects run locally and write altered pixels into the exported image. Use an opaque filled rectangle when information must be completely concealed: a weak blur or small pixel blocks can leave information recognizable.

The renderer caches the committed document for responsive panning and drawing. During object dragging it renders the moving object above the other objects; the final layer order is restored when the drag finishes. A blur/pixelation preview can therefore differ where it overlaps later objects until committed.

Undo stores immutable bitmap references for annotation changes and limits history to 64 operations, with a 256 MiB budget for source images in history. The current image and at least one undo step can exceed that budget. Images are limited to 64 megapixels and 16,384 pixels per side to avoid exhausting desktop rendering resources.

This version exports flattened images. It does not yet save editable project files, rotate objects, select several objects at once, or preserve multiple frames from animated images. Object edits are retained only while that image remains open; the application checks unsaved changes before replacing it.

## Validation

Run `dotnet run --project tests/Framelet.Editor.Tests -c Release` on Windows. The test runner uses an STA thread and exercises DPI normalization, pixel output and compositing, annotation transforms, crop/resize, undo/redo branching, invalid bounds, and history limits. `-- --render artifacts/editor-preview.png` additionally creates a static UI rendering with a generated sample image. That static rendering is a layout check, not proof of live mouse or keyboard interaction.
