using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet.App;
using Framelet.App.Editor;

internal static class ShellUiChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("Shell navigation exposes selection and preserves an unsaved image across themes", () =>
        {
            var shell = new MainWindow();
            var editor = (ImageEditorView)shell.FindName("Editor");
            editor.LoadImage(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[256], 32));
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ((App)Application.Current).ApplyTheme(theme);
                foreach (var page in new[] { "EditorNav", "ClipsNav", "HomeNav" })
                {
                    var nav = (RadioButton)shell.FindName(page);
                    var peer = new RadioButtonAutomationPeer(nav);
                    ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                    Require(nav.IsChecked == true && new RadioButtonAutomationPeer(nav).GetName().Length > 0);
                    var viewName = page switch { "EditorNav" => "Editor", "ClipsNav" => "ClipHost", _ => "HomeView" };
                    Require(((UIElement)shell.FindName(viewName)).Visibility == Visibility.Visible);
                    Require(new[] { "EditorNav", "ClipsNav", "HomeNav" }.Count(n => ((RadioButton)shell.FindName(n)).IsChecked == true) == 1);
                    Require(editor.HasImage && editor.IsDirty);
                }
            }
        });

        check("Shell primary actions and recent-file controls fit minimum and ordinary layouts", () =>
        {
            var shell = new MainWindow();
            var root = (FrameworkElement)shell.Content;
            foreach (var size in new[] { new Size(984, 621), new Size(1224, 781), new Size(1904, 1001) })
            {
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                foreach (var name in new[] { "CopyButton", "SaveButton", "OpenRecentButton", "OutputFolder" })
                {
                    var control = (FrameworkElement)shell.FindName(name);
                    var bounds = control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
                    Require(bounds.Left >= 0 && bounds.Right <= size.Width + 1 && bounds.Bottom <= size.Height + 1);
                }
            }
        });

        check("Shared ComboBox preserves the WPF popup contract and accessible selection", () =>
        {
            var combo = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 1, Width = 180 };
            var host = new Window { Content = combo };
            combo.Style = (Style)Application.Current.FindResource(typeof(ComboBox));
            combo.Measure(new Size(180, 40)); combo.Arrange(new Rect(0, 0, 180, 40)); combo.ApplyTemplate();
            Require(combo.Template.FindName("PART_Popup", combo) is Popup);
            Require(new ComboBoxAutomationPeer(combo).GetPattern(PatternInterface.Selection) is not null);
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ((App)Application.Current).ApplyTheme(theme);
                Require(Equals(combo.SelectedItem, "Second"));
                Require(ReferenceEquals(combo.Foreground, Application.Current.Resources["TextBrush"]));
            }
            host.Close();
        });

        check("Themed menu items retain accessible commands and submenu structure", () =>
        {
            var menu = new ContextMenu();
            menu.Style = (Style)Application.Current.FindResource(typeof(ContextMenu));
            var command = new MenuItem { Header = "Capture region", InputGestureText = "Ctrl+Shift+2" };
            var submenu = new MenuItem { Header = "More" };
            submenu.Items.Add(new MenuItem { Header = "Capture window" });
            menu.Items.Add(command); menu.Items.Add(submenu);
            menu.Measure(new Size(320, 200)); menu.Arrange(new Rect(0, 0, 320, 200)); menu.UpdateLayout();
            foreach (var item in new[] { command, submenu })
            {
                item.Style = (Style)Application.Current.FindResource(typeof(MenuItem)); item.ApplyTemplate();
                Require(new MenuItemAutomationPeer(item).GetName().Length > 0);
            }
            Require(new MenuItemAutomationPeer(command).GetPattern(PatternInterface.Invoke) is not null);
            Require(new MenuItemAutomationPeer(submenu).GetPattern(PatternInterface.ExpandCollapse) is not null);
            Require(submenu.Template.FindName("PART_Popup", submenu) is Popup);
        });
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Shell UI invariant failed.");
    }
}
