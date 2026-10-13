using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using Shared.Core.Settings;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiInteractionStateTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void SelectableControls_PreserveRailStatePriorityAndKeyboardFocus(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            ThemesController.SetTheme(theme);
            var controls = new (string Key, Control Control)[]
            {
                ("AeShell.ActivityItem", new ListBoxItem { Content = "资源" }),
                ("AeList.Item", new ListBoxItem { Content = "资源列表" }),
                ("AeTab.Item", new TabItem { Header = "编辑标签" }),
                ("AeShell.EditorTabItem", new TabItem { Header = "模型编辑器" }),
                ("AeInput.ComboBoxItem", new ComboBoxItem { Content = "下拉选项" }),
                ("AeTable.Row", new DataGridRow { Item = "数据行" }),
            };
            var panel = new StackPanel { Width = 320, Margin = new Thickness(8) };
            foreach (var (key, control) in controls)
            {
                control.Style = (Style)Application.Current.FindResource(key);
                panel.Children.Add(control);
            }
            var window = new Window { Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.UpdateLayout();
                foreach (var (key, control) in controls)
                {
                    Assert.That(control.FocusVisualStyle, Is.SameAs(Application.Current.FindResource("AeFocus.Keyboard")), key);
                    Assert.That(ColorOf(control.Background).A, Is.Zero, key);
                    SetMouseOver(control, true);
                    Assert.That(ColorOf(control.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")), key);
                    SetSelected(control, true);
                    Assert.That(ColorOf(control.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")), key);
                    SetMouseOver(control, false);
                    Assert.That(ColorOf(control.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")), key);
                    Assert.That(ColorOf(control.Foreground), Is.EqualTo(ResourceColor("AeBrush.TextPrimary")), key);
                }
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                foreach (var dpi in new[] { 96, 120, 144 })
                    UiRefinementTests.SaveSnapshot(panel, $"selected-controls-{theme}-{dpi}.png", dpi);
                foreach (var (key, control) in controls)
                {
                    control.IsEnabled = false;
                    Assert.That(control.Opacity, Is.EqualTo(0.42), key);
                    Assert.That(ColorOf(control.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")), key);
                    control.IsEnabled = true;
                    SetSelected(control, false);
                    Assert.That(control.Opacity, Is.EqualTo(1), key);
                    Assert.That(ColorOf(control.Background).A, Is.Zero, key);
                }
            }
            finally
            {
                window.Close();
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    [TestCase(Dock.Top)]
    [TestCase(Dock.Bottom)]
    [TestCase(Dock.Left)]
    [TestCase(Dock.Right)]
    public void TabPanels_JoinContentWithoutOuterGuttersOrClipping(Dock placement)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var first = new Border();
            var second = new Border();
            var tabs = new TabControl { TabStripPlacement = placement };
            tabs.Items.Add(new TabItem { Header = "资源", Content = first });
            tabs.Items.Add(new TabItem { Header = "属性", Content = second });
            tabs.SelectedIndex = 0;
            var host = new Grid();
            host.Children.Add(tabs);
            var window = new Window { Content = host, Width = 480, Height = 320, Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.UpdateLayout();
                var header = (FrameworkElement)tabs.Template.FindName("headerPanel", tabs);
                var content = (FrameworkElement)tabs.Template.FindName("contentPanel", tabs);
                var headerOrigin = header.TranslatePoint(new Point(), tabs);
                var contentOrigin = content.TranslatePoint(new Point(), tabs);
                var gap = placement switch
                {
                    Dock.Top => contentOrigin.Y - headerOrigin.Y - header.ActualHeight,
                    Dock.Bottom => headerOrigin.Y - contentOrigin.Y - content.ActualHeight,
                    Dock.Left => contentOrigin.X - headerOrigin.X - header.ActualWidth,
                    _ => headerOrigin.X - contentOrigin.X - content.ActualWidth,
                };
                Assert.That(gap, Is.EqualTo(0).Within(0.01));
                Assert.That(tabs.TranslatePoint(new Point(), host).Y, Is.GreaterThanOrEqualTo(0));
                Assert.That(tabs.ActualHeight, Is.LessThanOrEqualTo(host.ActualHeight));
                tabs.SelectedIndex = 1;
                window.UpdateLayout();
                var presenter = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
                Assert.That(presenter.Content, Is.SameAs(second));
                UiRefinementTests.SaveSnapshot(host, $"tab-seam-{placement}.png");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void NestedTreeHover_LeavesAncestorBackgroundClearAndSelectionStable(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            ThemesController.SetTheme(theme);
            var style = (Style)Application.Current.FindResource("AeTree.Item");
            var child = new TreeViewItem { Header = "资源文件", Style = style };
            var parent = new TreeViewItem { Header = "工程", IsExpanded = true, Style = style };
            parent.Items.Add(child);
            var tree = new TreeView { Style = (Style)Application.Current.FindResource("AeTree.View") };
            tree.Items.Add(parent);
            var window = new Window { Content = tree, Width = 320, Height = 200, Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.UpdateLayout();
                var row = (Border)child.Template.FindName("Row", child);
                var parentRow = (Border)parent.Template.FindName("Row", parent);
                SetMouseOver(parent, true);
                SetMouseOver(row, true);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Assert.That(ColorOf(parentRow.Background).A, Is.Zero);
                Assert.That(ColorOf(row.Background), Is.EqualTo(ResourceColor("AeBrush.SurfaceHover")));
                child.IsSelected = true;
                Assert.That(ColorOf(row.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
                Assert.That(row.CornerRadius, Is.EqualTo(Application.Current.FindResource("AeRadius.Control")));
                SetMouseOver(row, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Assert.That(ColorOf(row.Background), Is.EqualTo(ResourceColor("AeBrush.AccentSoft")));
                Assert.That(ColorOf(parentRow.Background).A, Is.Zero);
            }
            finally
            {
                window.Close();
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    private static void SetSelected(Control control, bool selected)
    {
        switch (control)
        {
            case ListBoxItem listItem: listItem.IsSelected = selected; break;
            case TabItem tab: tab.IsSelected = selected; break;
            case DataGridRow row: row.IsSelected = selected; break;
            default: throw new InvalidOperationException(control.GetType().Name);
        }
    }

    // Drive WPF's state properties without moving the user's desktop pointer.
    private static void SetMouseOver(UIElement element, bool value)
    {
        var key = (DependencyPropertyKey)typeof(UIElement)
            .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        element.SetValue(key, value);
    }

    private static Color ResourceColor(string key) => ColorOf((Brush)Application.Current.FindResource(key));
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
}
