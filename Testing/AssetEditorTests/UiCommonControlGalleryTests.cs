using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using Shared.Core.Settings;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiCommonControlGalleryTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void CommonControls_RenderReadableStatesAtSupportedScales(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                ThemesController.SetTheme(theme);
                foreach (var dpi in new[] { 96, 120, 144 })
                    RenderGallery(theme, dpi);
            }
            finally
            {
                ThemesController.SetTheme(previous);
            }
        });
    }

    private static void RenderGallery(ThemeType theme, int dpi)
    {
        var grid = new Grid { Margin = new Thickness(24) };
        for (var index = 0; index < 3; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        var columns = Enumerable.Range(0, 3).Select(index =>
        {
            var column = new StackPanel { Margin = new Thickness(index == 0 ? 0 : 20, 0, 0, 0) };
            Grid.SetColumn(column, index);
            grid.Children.Add(column);
            return column;
        }).ToArray();
        var root = new Border { Width = 960, Height = 640, Child = grid };
        root.SetResourceReference(Border.BackgroundProperty, "AeBrush.Canvas");
        root.SetResourceReference(TextElement.ForegroundProperty, "AeBrush.TextPrimary");
        root.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");

        Add(columns[0], new TextBlock { Text = "按钮与输入", Style = Style("AeText.SectionTitle") });
        Add(columns[0], new Button { Content = "保存修改", Style = Style("AeButton.Primary") });
        Add(columns[0], new Button { Content = "选择文件", Style = Style("AeButton.Secondary") });
        Add(columns[0], new Button { Content = "暂不可用", IsEnabled = false });
        var editable = new TextBox { Text = "可编辑名称 · 帝国将军", Style = Style("AeInput.TextBox") };
        Add(columns[0], editable);
        var readOnly = new TextBox { Text = "原版资源（只读）", IsReadOnly = true, Style = Style("AeInput.TextBox") };
        Add(columns[0], readOnly);
        var invalid = new TextBox { Style = Style("AeInput.TextBox") };
        invalid.SetBinding(TextBox.TextProperty, new Binding { Source = "名称需要修改", Mode = BindingMode.OneWay });
        Validation.MarkInvalid(invalid.GetBindingExpression(TextBox.TextProperty)!,
            new ValidationError(new ExceptionValidationRule(), invalid.GetBindingExpression(TextBox.TextProperty)!, "名称无效", null));
        Add(columns[0], invalid);
        Add(columns[0], new TextBlock { Text = "名称无效", Style = Style("AeValidation.Message") });
        var combo = new ComboBox { ItemsSource = new[] { "全面战争：战锤 3", "全面战争：战锤 2" }, SelectedIndex = 0, Style = Style("AeInput.ComboBox") };
        Add(columns[0], combo);
        Add(columns[0], new CheckBox { Content = "显示依赖资源", IsChecked = true, Style = Style("AeInput.CheckBox") });
        Add(columns[0], new RadioButton { Content = "保留现有文件", IsChecked = true, Style = Style("AeInput.RadioButton") });

        Add(columns[1], new TextBlock { Text = "列表与标签页", Style = Style("AeText.SectionTitle") });
        var list = new ListBox
        {
            Height = 120, Style = Style("AeList.View"),
            ItemsSource = Enumerable.Range(1, 5000).Select(index => $"资源 {index:0000}"), SelectedIndex = 1,
        };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(list, true);
        Add(columns[1], list);
        var tree = new TreeView { Height = 136, Style = Style("AeTree.View"), ItemContainerStyle = Style("AeTree.Item") };
        var folder = new TreeViewItem { Header = "我的工程", IsExpanded = true, Style = Style("AeTree.Item") };
        folder.Items.Add(new TreeViewItem { Header = "variantmeshes", Style = Style("AeTree.Item"), IsSelected = true });
        folder.Items.Add(new TreeViewItem { Header = "ui", Style = Style("AeTree.Item") });
        tree.Items.Add(folder);
        Add(columns[1], tree);
        var tabs = new TabControl { Height = 132 };
        tabs.Items.Add(new TabItem { Header = "模型", Content = new TextBlock { Text = "当前编辑区域", Margin = new Thickness(12) } });
        tabs.Items.Add(new TabItem { Header = "材质", Content = "材质编辑区域" });
        Add(columns[1], tabs);
        var disabledTree = new TreeView { Height = 72, IsEnabled = false, Style = Style("AeTree.View") };
        disabledTree.Items.Add(new TreeViewItem { Header = "正在准备资源", Style = Style("AeTree.Item") });
        Add(columns[1], disabledTree);

        Add(columns[2], new TextBlock { Text = "菜单与反馈", Style = Style("AeText.SectionTitle") });
        var menu = new Menu();
        var menuItem = new MenuItem { Header = "文件" };
        var checkedItem = new MenuItem { Header = "显示资源", IsCheckable = true, IsChecked = true };
        menuItem.Items.Add(checkedItem);
        menu.Items.Add(menuItem);
        menu.Items.Add(new MenuItem { Header = "不可用", IsEnabled = false });
        Add(columns[2], menu);
        Add(columns[2], new Border
        {
            Style = Style("AeFeedback.Notice"),
            Child = new TextBlock { Text = "资源已准备就绪", TextWrapping = TextWrapping.Wrap },
        });
        Add(columns[2], new ProgressBar { Value = 64, Maximum = 100, Style = Style("AeProgress.Bar") });
        Add(columns[2], new TextBlock { Text = "64 / 100", Style = Style("AeText.Caption") });
        var table = new DataGrid
        {
            Height = 170, IsReadOnly = true, AutoGenerateColumns = false,
            Style = Style("AeTable.Grid"),
            ItemsSource = new[] { new { Name = "模型", Count = 12 }, new { Name = "纹理", Count = 36 } },
        };
        table.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("Name"), Width = 160 });
        table.Columns.Add(new DataGridTextColumn { Header = "数量", Binding = new Binding("Count"), Width = 64 });
        Add(columns[2], table);

        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
        Assert.Multiple(() =>
        {
            Assert.That(editable.ActualHeight, Is.GreaterThanOrEqualTo(26));
            Assert.That(combo.ActualHeight, Is.GreaterThanOrEqualTo(26));
            Assert.That(readOnly.IsReadOnly, Is.True);
            Assert.That(Validation.GetHasError(invalid), Is.True);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.Not.Null);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(4999), Is.Null, "Offscreen rows must remain virtualized.");
            Assert.That(checkedItem.StaysOpenOnClick, Is.True, "Default menu migration must preserve checked-menu behavior.");
            foreach (var column in columns)
                foreach (FrameworkElement child in column.Children)
                    Assert.That(child.TranslatePoint(new Point(0, child.ActualHeight), root).Y,
                        Is.LessThanOrEqualTo(root.Height), $"Clipped {child.GetType().Name} at {dpi} DPI.");
        });
        var bitmap = new RenderTargetBitmap((int)(root.Width * dpi / 96), (int)(root.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var sample = disabledTree.TranslatePoint(new Point(12, disabledTree.ActualHeight - 12), root);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(sample.X * dpi / 96), (int)(sample.Y * dpi / 96), 1, 1), pixel, 4, 0);
        var disabledSurface = ((SolidColorBrush)Application.Current.FindResource("AeBrush.Surface1")).Color;
        Assert.That(pixel, Is.EqualTo(new[] { disabledSurface.B, disabledSurface.G, disabledSurface.R, disabledSurface.A }),
            $"Disabled resource tree must retain its theme surface during startup handoff ({theme}, {dpi} DPI).");
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            using var file = File.Create(Path.Combine(output, $"controls-{theme}-{dpi}.png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
        }
    }

    private static Style Style(string key) => (Style)Application.Current.FindResource(key);

    private static void Add(Panel panel, FrameworkElement child)
    {
        child.Margin = new Thickness(0, 0, 0, 14);
        panel.Children.Add(child);
    }
}
