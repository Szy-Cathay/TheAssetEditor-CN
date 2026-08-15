using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NUnit.Framework;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiCommonControlGallery
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void CommonControlGallery_RendersTheme(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                Window? window = null;
                try
                {
                    ThemesController.SetTheme(theme);
                    window = new Window
                    {
                        Width = 1180,
                        Height = 760,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                        Content = CreateGallery(theme),
                    };
                    window.Show();
                    window.UpdateLayout();

                    var dpi = VisualTreeHelper.GetDpi(window);
                    var bitmap = new RenderTargetBitmap(
                        (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
                        (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
                        dpi.PixelsPerInchX,
                        dpi.PixelsPerInchY,
                        PixelFormats.Pbgra32);
                    bitmap.Render(window);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
                        NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
                    });

                    var outputDirectory = Environment.GetEnvironmentVariable(
                        "AE_UI_QA_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                    {
                        Directory.CreateDirectory(outputDirectory);
                        var path = System.IO.Path.Combine(
                            outputDirectory,
                            $"common-controls-{theme}.png");
                        using var stream = File.Create(path);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                    }
                }
                finally
                {
                    window?.Close();
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    private static FrameworkElement CreateGallery(ThemeType theme)
    {
        var root = new Border
        {
            Padding = new Thickness(24),
        };
        root.SetResourceReference(Border.BackgroundProperty, "AeBrush.Canvas");

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel();
        heading.Children.Add(Text(
            "AE 公共控件层",
            "AeText.PageTitle"));
        heading.Children.Add(Text(
            $"{theme} · 中等偏紧凑 · 键控样式 · 真实 WPF 渲染",
            "AeText.Caption",
            new Thickness(0, 4, 0, 0)));
        Grid.SetColumnSpan(heading, 5);
        layout.Children.Add(heading);

        Add(layout, Section("操作与输入", CreateInputs()), 0);
        Add(layout, Section("导航与集合", CreateCollections()), 2);
        Add(layout, Section("菜单与反馈", CreateFeedback()), 4);
        root.Child = layout;
        return root;
    }

    private static FrameworkElement CreateInputs()
    {
        var stack = VerticalStack();
        stack.Children.Add(Text("按钮状态", "AeText.Label"));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) };
        buttons.Children.Add(Button("保存更改", "AeButton.Primary"));
        buttons.Children.Add(Button("生成 Pack", "AeButton.Secondary"));
        buttons.Children.Add(Button("更多操作", "AeButton.Quiet"));
        buttons.Children.Add(Button("删除", "AeButton.Danger"));
        var disabled = Button("正在加载", "AeButton.Secondary");
        disabled.IsEnabled = false;
        buttons.Children.Add(disabled);
        stack.Children.Add(buttons);

        stack.Children.Add(Text("输入与选择", "AeText.Label"));
        stack.Children.Add(Input("帝国将军.pack"));
        var combo = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            SelectedIndex = 0,
            Style = Style("AeInput.ComboBox"),
            ItemsSource = new[] { "全面战争：战锤 III", "全面战争：三国" },
        };
        stack.Children.Add(combo);

        var invalid = Input("C:\\项目\\输出");
        invalid.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(invalid);
        stack.Children.Add(Text(
            "输出 Pack 必须位于工程目录之外",
            "AeValidation.Message"));

        var choices = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        choices.Children.Add(new CheckBox
        {
            Content = "仅加载 LOD0",
            IsChecked = true,
            Margin = new Thickness(0, 0, 16, 0),
            Style = Style("AeInput.CheckBox"),
        });
        choices.Children.Add(new RadioButton
        {
            Content = "自动",
            IsChecked = true,
            Margin = new Thickness(0, 0, 16, 0),
            Style = Style("AeInput.RadioButton"),
        });
        choices.Children.Add(new ToggleButton
        {
            IsChecked = true,
            Style = Style("AeInput.Switch"),
        });
        stack.Children.Add(choices);
        return stack;
    }

    private static FrameworkElement CreateCollections()
    {
        var stack = VerticalStack();
        var tabs = new TabControl { Height = 92 };
        tabs.Items.Add(Tab("帝国将军模型", "当前资源：emp_general_variant_01"));
        tabs.Items.Add(Tab("材质编辑器", "材质参数"));
        tabs.Items.Add(Tab("动画片段", "动画轨道"));
        stack.Children.Add(tabs);

        var tree = new TreeView
        {
            Height = 152,
            Margin = new Thickness(0, 12, 0, 12),
            Style = Style("AeTree.View"),
        };
        var root = TreeItem("帝国将军.pack", true);
        var variants = TreeItem("variantmeshes", true);
        variants.IsSelected = true;
        variants.Items.Add(TreeItem("animations"));
        variants.Items.Add(TreeItem("textures"));
        variants.Items.Add(TreeItem("audio"));
        root.Items.Add(variants);
        tree.Items.Add(root);
        stack.Children.Add(tree);

        var table = new DataGrid
        {
            Height = 158,
            Style = Style("AeTable.Grid"),
            ItemsSource = new[]
            {
                new ResourceRow("emp_general_variant_01", "RigidModel", "已修改"),
                new ResourceRow("emp_general_body_diffuse", "Texture", "正常"),
                new ResourceRow("emp_general_animation", "Animation", "正常"),
            },
        };
        table.Columns.Add(new DataGridTextColumn { Header = "资源", Binding = new Binding("Name"), Width = 160 });
        table.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding("Type"), Width = 90 });
        table.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding("Status"), Width = 70 });
        stack.Children.Add(table);
        return stack;
    }

    private static FrameworkElement CreateFeedback()
    {
        var stack = VerticalStack();
        var menu = new Menu
        {
            Margin = new Thickness(0, 0, 0, 12),
            Style = Style("AeMenu.Bar"),
        };
        menu.Items.Add(MenuItem("文件"));
        menu.Items.Add(MenuItem("编辑"));
        menu.Items.Add(MenuItem("视图"));
        stack.Children.Add(menu);

        var tags = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        tags.Children.Add(Tag("帝国将军模型"));
        tags.Children.Add(Tag("材质编辑器"));
        stack.Children.Add(tags);

        stack.Children.Add(Notice(
            "AeFeedback.SuccessIcon",
            "Pack 已保存",
            "帝国将军.pack 已写入输出目录。",
            "M 3,8 L 7,12 L 14,4"));
        stack.Children.Add(Notice(
            "AeFeedback.WarningIcon",
            "存在未保存修改",
            "切换分支前请保存或放弃编辑器内容。",
            "M 8.5,4 L 8.5,10 M 8.5,13 L 8.5,13.2"));
        stack.Children.Add(Notice(
            "AeFeedback.DangerIcon",
            "资源加载失败",
            "文件格式不受支持，请检查日志详情。",
            "M 5,5 L 12,12 M 12,5 L 5,12"));

        var progressLabel = new Grid { Margin = new Thickness(0, 16, 0, 6) };
        progressLabel.ColumnDefinitions.Add(new ColumnDefinition());
        progressLabel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressLabel.Children.Add(Text("正在读取 CA Pack", "AeText.Caption"));
        var count = Text("1,428 / 2,100", "AeText.Technical");
        Grid.SetColumn(count, 1);
        progressLabel.Children.Add(count);
        stack.Children.Add(progressLabel);
        stack.Children.Add(new ProgressBar
        {
            Maximum = 2100,
            Value = 1428,
            Style = Style("AeProgress.Bar"),
        });

        var scrollbar = new ScrollBar
        {
            Margin = new Thickness(0, 20, 0, 0),
            Maximum = 100,
            Orientation = Orientation.Horizontal,
            Value = 36,
            Style = Style("AeScrollBar.Compact"),
        };
        stack.Children.Add(scrollbar);

        var empty = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Style = Style("AeEmptyState.Panel"),
        };
        var emptyCopy = VerticalStack();
        emptyCopy.VerticalAlignment = VerticalAlignment.Center;
        emptyCopy.Children.Add(Text("暂无搜索结果", "AeEmptyState.Title"));
        emptyCopy.Children.Add(Text(
            "调整筛选条件后重试。",
            "AeEmptyState.Description"));
        empty.Child = emptyCopy;
        stack.Children.Add(empty);
        return stack;
    }

    private static Border Section(string title, FrameworkElement content)
    {
        var border = new Border
        {
            Padding = new Thickness(8, 0, 8, 0),
        };
        var stack = VerticalStack();
        stack.Children.Add(Text(title, "AeText.SectionTitle", new Thickness(0, 0, 0, 12)));
        stack.Children.Add(content);
        border.Child = stack;
        return border;
    }

    private static Border Notice(
        string iconStyle,
        string title,
        string body,
        string geometry)
    {
        var notice = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Style = Style("AeFeedback.Notice"),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var icon = new Border { Style = Style(iconStyle) };
        var path = new System.Windows.Shapes.Path
        {
            Width = 10,
            Height = 10,
            Data = Geometry.Parse(geometry),
            Stretch = Stretch.Uniform,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeThickness = 1.7,
        };
        path.SetResourceReference(Shape.StrokeProperty, "AeBrush.Canvas");
        icon.Child = path;
        grid.Children.Add(icon);

        var copy = VerticalStack();
        copy.Children.Add(Text(title, "AeText.Label"));
        copy.Children.Add(Text(body, "AeText.Caption", new Thickness(0, 3, 0, 0)));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        notice.Child = grid;
        return notice;
    }

    private static TextBox Input(string value) => new()
    {
        Text = value,
        Style = Style("AeInput.TextBox"),
    };

    private static Button Button(string content, string styleKey) => new()
    {
        Content = content,
        Margin = new Thickness(0, 0, 8, 8),
        Style = Style(styleKey),
    };

    private static TabItem Tab(string header, string content) => new()
    {
        Header = header,
        Content = new TextBlock
        {
            Margin = new Thickness(10),
            Text = content,
        },
        Style = Style("AeTab.Item"),
    };

    private static TreeViewItem TreeItem(string header, bool expanded = false) => new()
    {
        Header = header,
        IsExpanded = expanded,
        Style = Style("AeTree.Item"),
    };

    private static MenuItem MenuItem(string header) => new()
    {
        Header = header,
        Style = Style("AeMenu.Item"),
    };

    private static Border Tag(string value)
    {
        var tag = new Border
        {
            Margin = new Thickness(0, 0, 8, 0),
            Style = Style("AeTag.Container"),
        };
        tag.Child = Text(value, "AeTag.Text");
        return tag;
    }

    private static TextBlock Text(
        string value,
        string styleKey,
        Thickness? margin = null) => new()
        {
            Text = value,
            Margin = margin ?? new Thickness(),
            Style = Style(styleKey),
        };

    private static StackPanel VerticalStack() => new()
    {
        Orientation = Orientation.Vertical,
    };

    private static Style Style(string key) =>
        (Style)Application.Current.FindResource(key);

    private static void Add(Grid grid, UIElement element, int column)
    {
        Grid.SetRow(element, 2);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private sealed record ResourceRow(string Name, string Type, string Status);
}
