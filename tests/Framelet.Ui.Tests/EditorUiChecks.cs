using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet.App;
using Framelet.App.Editor;

internal static class EditorUiChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Editor disables unavailable actions and exposes the active tool", () =>
        {
            var editor = new ImageEditorView();
            Require(!Find<Button>(editor, "ResizeButton").IsEnabled, "Resize must require an image.");
            Require(!Find<StackPanel>(editor, "ZoomControls").IsEnabled, "Zoom must require an image.");
            Require(!Find<Button>(editor, "DeleteButton").IsEnabled, "Delete must require a selection.");
            Require(!Find<ComboBox>(editor, "WidthPicker").IsEnabled, "Properties must require an image.");
            editor.LoadImage(Image(), "sample.png");
            ClickTool(editor, "Rectangle");
            Require(Find<Button>(editor, "ResizeButton").IsEnabled, "Resize should be available after import.");
            Require(Find<StackPanel>(editor, "ZoomControls").IsEnabled, "Zoom should be available after import.");
            Require(Find<ComboBox>(editor, "WidthPicker").IsEnabled, "Drawing properties should be available after import.");
            Require(Find<TextBlock>(editor, "PropertyScope").Text == "Tool defaults", "Drawing must distinguish defaults from selection.");
            var selected = Find<StackPanel>(editor, "ToolRail").Children.OfType<Button>()
                .Where(button => AutomationProperties.GetItemStatus(button) == "Selected tool").ToArray();
            Require(selected.Length == 1 && Equals(selected[0].Tag, "Rectangle"), "One tool must be announced as selected.");
            Require(!editor.IsDirty, "Changing tools must not dirty a saved image.");
        });

        check("Editor properties and commands remain separate at minimum layout in both themes", () =>
        {
            var app = (App)Application.Current;
            foreach (string theme in new[] { "Dark", "Light" })
            {
                app.ApplyTheme(theme);
                var editor = new ImageEditorView { Width = 920, Height = 537 };
                editor.LoadImage(Image(), "sample.png");
                foreach (string tool in new[] { "Rectangle", "Text", "Blur", "Crop" })
                {
                    ClickTool(editor, tool);
                    Arrange(editor);
                    var commands = Find<FrameworkElement>(editor, "EditorCommands");
                    Rect commandBounds = Bounds(commands, editor);
                    var properties = Find<WrapPanel>(editor, "PropertyOptions");
                    foreach (FrameworkElement group in properties.Children)
                    {
                        if (group.Visibility != Visibility.Visible) continue;
                        Rect bounds = Bounds(group, editor);
                        Require(bounds.Right <= commandBounds.Left + .5, $"{theme} {tool} properties overlap commands.");
                        Require(bounds.Bottom <= Bounds(properties, editor).Bottom + .5, $"{theme} {tool} properties are clipped.");
                    }
                    Require(commandBounds.Right <= editor.ActualWidth + .5, "Commands must stay within the editor.");
                    var rail = Find<StackPanel>(editor, "ToolRail");
                    var railScroller = Parent<ScrollViewer>(rail);
                    Require(railScroller.ScrollableHeight < 1, "All tools should fit at the supported minimum height.");
                    Require(Find<ListBox>(editor, "ObjectList").ActualHeight > 200, "Object actions must leave useful list space.");
                }
            }
            app.ApplyTheme("Dark");
        });

        check("Editor keeps pending text, source and objects through theme and pane changes", () =>
        {
            var editor = new ImageEditorView { Width = 920, Height = 537 };
            editor.LoadImage(Image(), @"C:\fixtures\source with a long name.png");
            var surface = Find<FrameworkElement>(editor, "Surface");
            var document = (ImageDocument)surface.GetType().GetProperty("Document")!.GetValue(surface)!;
            var annotation = new Annotation { Tool = EditorTool.Text, Start = new Point(2, 2), End = new Point(60, 40), Text = "Before" };
            document.Add(annotation);
            surface.GetType().GetMethod("Select")!.Invoke(surface, new object?[] { annotation.Id });
            editor.MarkSaved();
            var input = Find<TextBox>(editor, "AnnotationTextBox");
            input.Text = "Pending text stays editable";
            ((App)Application.Current).ApplyTheme("Light");
            Find<Button>(editor, "ObjectsToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Arrange(editor);
            Require(input.Text == "Pending text stays editable" && editor.IsDirty, "Theme or pane changes must preserve dirty pending text.");
            Require(editor.SourcePath == @"C:\fixtures\source with a long name.png", "Source path must survive visual changes.");
            Require(document.Annotations.Count == 1 && document.Annotations[0].Id == annotation.Id, "Objects must keep identity.");
            Require(Find<TextBlock>(editor, "PropertyScope").Text == "Selected object", "Existing properties must be identified.");
            editor.ExportImage();
            Require(document.Annotations[0].Text == "Pending text stays editable", "Export must commit pending text.");
            editor.Undo();
            Require(document.Annotations[0].Text == "Before", "Text commit must remain undoable.");
            ((App)Application.Current).ApplyTheme("Dark");
        });

        check("Selecting another object commits pending text without losing either object", () =>
        {
            var editor = new ImageEditorView();
            editor.LoadImage(Image(), "sample.png");
            var surface = Find<FrameworkElement>(editor, "Surface");
            var document = (ImageDocument)surface.GetType().GetProperty("Document")!.GetValue(surface)!;
            var first = new Annotation { Tool = EditorTool.Text, Start = new Point(2, 2), End = new Point(60, 40), Text = "Before" };
            var second = new Annotation { Tool = EditorTool.Rectangle, Start = new Point(20, 20), End = new Point(50, 50) };
            document.Add(first); document.Add(second);
            surface.GetType().GetMethod("Select")!.Invoke(surface, new object?[] { first.Id });
            Find<TextBox>(editor, "AnnotationTextBox").Text = "Committed by selecting another object";
            Find<ListBox>(editor, "ObjectList").SelectedIndex = 0;
            Require(document.Annotations[0].Text == "Committed by selecting another object", "Object selection must commit the prior text edit.");
            var selected = (Annotation?)surface.GetType().GetProperty("Selected")!.GetValue(surface);
            Require(selected?.Id == second.Id && document.Annotations.Count == 2, "New selection must survive the text commit refresh.");
        });

        check("Canvas selection and layer reordering realize the selected object in a long virtualized list", () =>
        {
            var (editor, surface, document) = PopulatedEditor();
            var list = Find<ListBox>(editor, "ObjectList");
            var selected = document.Annotations[0];
            Require(list.ItemContainerGenerator.ContainerFromIndex(29) == null, "The fixture must start with an offscreen virtualized object.");
            surface.GetType().GetMethod("Select")!.Invoke(surface, new object?[] { selected.Id });
            FlushLayout(editor);
            RequireRealizedSelection(list);

            document.MoveLayer(selected.Id, true);
            FlushLayout(editor);
            Require(list.SelectedIndex == 28, "Layer reordering must retain the selected object's identity.");
            RequireRealizedSelection(list);
        });

        check("Object property refresh retains list position while updating text labels", () =>
        {
            var (editor, surface, document) = PopulatedEditor();
            var list = Find<ListBox>(editor, "ObjectList");
            var selected = document.Annotations[0];
            surface.GetType().GetMethod("Select")!.Invoke(surface, new object?[] { selected.Id });
            FlushLayout(editor);
            var scroller = Descendant<ScrollViewer>(list);
            scroller.ScrollToVerticalOffset(3);
            FlushLayout(editor);
            double offset = scroller.VerticalOffset;
            Require(offset > 0, "The fixture must preserve a deliberate scroll position away from the selected row.");

            document.Replace(selected with { Text = "Updated object label", StrokeWidth = 10 });
            FlushLayout(editor);
            Require(Math.Abs(scroller.VerticalOffset - offset) < .01, "Property refresh must preserve the user's scroll position.");
            Require(list.SelectedIndex == 29, "Property refresh must retain the selected object.");
            string label = (string)list.SelectedItem.GetType().GetProperty("Label")!.GetValue(list.SelectedItem)!;
            Require(label.Contains("Updated object label"), "The row must still update its displayed text.");
        });
    }

    private static (ImageEditorView Editor, FrameworkElement Surface, ImageDocument Document) PopulatedEditor()
    {
        var editor = new ImageEditorView { Width = 920, Height = 537 };
        editor.LoadImage(Image(), "sample.png");
        var surface = Find<FrameworkElement>(editor, "Surface");
        var document = (ImageDocument)surface.GetType().GetProperty("Document")!.GetValue(surface)!;
        for (int index = 0; index < 30; index++)
            document.Add(new Annotation { Tool = EditorTool.Text, Start = new Point(2, 2), End = new Point(60, 40), Text = $"Object {index}" });
        FlushLayout(editor);
        return (editor, surface, document);
    }

    private static void FlushLayout(ImageEditorView editor)
    {
        // WPF may defer realizing a virtualized row and scrolling it until subsequent layout work.
        for (int pass = 0; pass < 3; pass++)
        {
            Arrange(editor);
            editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static void RequireRealizedSelection(ListBox list)
    {
        // A disconnected offscreen tree can realize the requested row, but has no visible viewport.
        var container = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem;
        Require(container?.IsSelected == true, "The requested row must be realized and selected.");
    }

    private static T Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) return found;
            try { return Descendant<T>(child); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Missing {typeof(T).Name} descendant.");
    }

    private static T Find<T>(ImageEditorView editor, string name) where T : FrameworkElement => (T)editor.FindName(name);
    private static void ClickTool(ImageEditorView editor, string name) => Find<StackPanel>(editor, "ToolRail").Children
        .OfType<Button>().Single(button => Equals(button.Tag, name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Arrange(ImageEditorView editor)
    {
        editor.Measure(new Size(editor.Width, editor.Height));
        editor.Arrange(new Rect(0, 0, editor.Width, editor.Height));
        editor.UpdateLayout();
    }
    private static Rect Bounds(FrameworkElement element, Visual ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static T Parent<T>(DependencyObject child) where T : DependencyObject
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(child); current != null; current = VisualTreeHelper.GetParent(current))
            if (current is T parent) return parent;
        throw new InvalidOperationException($"Missing {typeof(T).Name} parent.");
    }
    private static BitmapSource Image()
    {
        var image = BitmapSource.Create(80, 60, 96, 96, PixelFormats.Bgra32, null, new byte[80 * 60 * 4], 80 * 4);
        image.Freeze();
        return image;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
