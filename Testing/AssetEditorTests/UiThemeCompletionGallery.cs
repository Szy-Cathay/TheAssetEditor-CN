using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs;
using WindowHandling;
using NUnitAssert = NUnit.Framework.Assert;
using IOPath = System.IO.Path;

namespace AssetEditorTests;

[NonParallelizable]
public class UiThemeCompletionGallery
{
    private static readonly string[] Variants =
    [
        "palette",
        "optional-radio",
        "splitters",
        "dense-chinese",
        "window-font",
    ];

    [TestCaseSource(nameof(Cases))]
    public void ThemeCompletion_RendersEverySelectableTheme(
        ThemeType theme,
        string variant)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => Render(theme, variant));
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var theme in Enum.GetValues<ThemeType>())
        {
            foreach (var variant in Variants)
                yield return new TestCaseData(theme, variant);
        }
    }

    private static void Render(ThemeType theme, string variant)
    {
        var previousTheme = ThemesController.CurrentTheme;
        AssetEditorWindow? window = null;
        try
        {
            ThemesController.SetTheme(theme);
            window = CreateWindow(theme, variant);
            window.Show();
            window.UpdateLayout();
            if (variant == "optional-radio")
            {
                FindVisualDescendants<OptionalRadioButton>(window)
                    .First(button => button.Tag as string == "focus")
                    .Focus();
                window.UpdateLayout();
            }

            AssertVisualContracts(window);
            Capture(window, theme, variant);
        }
        finally
        {
            window?.Close();
            ThemesController.SetTheme(previousTheme);
        }
    }

    private static AssetEditorWindow CreateWindow(
        ThemeType theme,
        string variant)
    {
        var window = new AssetEditorWindow
        {
            Width = variant == "dense-chinese" ? 1120 : 960,
            Height = variant == "dense-chinese" ? 720 : 640,
            Title = $"AE UI Phase 7 · {theme} · {variant}",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Shared.Ui;component/BaseDialogs/OptionalRadioButtonStyle.xaml"),
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Shared.Ui;component/Common/Styles/GridSplitterStyles.xaml"),
        });
        window.Content = CreatePage(theme, variant);
        return window;
    }

    private static FrameworkElement CreatePage(ThemeType theme, string variant)
    {
        var root = new Border
        {
            Padding = new Thickness(24),
            Background = Brush("AeBrush.Canvas"),
        };
        var stack = new StackPanel();
        stack.Children.Add(Text("AE 主题与遗留层收口", "AeText.PageTitle"));
        stack.Children.Add(Text(
            $"{theme} · {variant} · {Font().Source}",
            "AeText.Caption",
            new Thickness(0, 4, 0, 18)));
        stack.Children.Add(variant switch
        {
            "palette" => CreatePalette(),
            "optional-radio" => CreateOptionalRadios(),
            "splitters" => CreateSplitters(),
            "dense-chinese" => CreateDenseChinese(),
            "window-font" => CreateWindowFont(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        });
        root.Child = stack;
        return root;
    }

    private static FrameworkElement CreatePalette()
    {
        var panel = new WrapPanel();
        foreach (var key in new[]
                 {
                     "AeBrush.Canvas", "AeBrush.Surface1",
                     "AeBrush.Surface2", "AeBrush.Surface3",
                     "AeBrush.SurfaceHover", "AeBrush.Border",
                     "AeBrush.BorderStrong", "AeBrush.TextPrimary",
                     "AeBrush.TextSecondary", "AeBrush.TextMuted",
                     "AeBrush.Accent", "AeBrush.AccentHover",
                     "AeBrush.AccentSoft", "AeBrush.Success",
                     "AeBrush.Warning", "AeBrush.Danger",
                 })
        {
            var swatch = new Border
            {
                Width = 210,
                Height = 64,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(10),
                Background = Brush(key),
                BorderBrush = Brush("AeBrush.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = Text(key, "AeText.Technical"),
            };
            panel.Children.Add(swatch);
        }
        return panel;
    }

    private static FrameworkElement CreateOptionalRadios()
    {
        var card = Card();
        var stack = new StackPanel();
        stack.Children.Add(Text("可取消单选按钮", "AeText.SectionTitle"));
        stack.Children.Add(Text(
            "选中项再次点击可清空；键盘焦点、悬停与禁用状态使用同一语义色。",
            "AeText.Body",
            new Thickness(0, 6, 0, 18)));
        stack.Children.Add(Optional("帝国步兵", true));
        stack.Children.Add(Optional("矮人远程部队", false));
        stack.Children.Add(Optional("键盘焦点状态", false, "focus"));
        stack.Children.Add(Optional("禁用的已选项", true, isEnabled: false));
        card.Child = stack;
        return card;
    }

    private static FrameworkElement CreateSplitters()
    {
        var card = Card();
        var grid = new Grid { Height = 420 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        grid.RowDefinitions.Add(new RowDefinition());

        AddPane(grid, "资源树\nvariantmeshes\nanimations\ntextures", 0, 0);
        AddPane(grid, "属性编辑器\n名称：empire_general\n状态：已修改", 2, 0);
        AddPane(grid, "时间轴与日志\n00:01:24  已载入动画片段", 0, 2, 3);

        var vertical = new GridSplitter
        {
            Style = SharedStyle(
                "Common/Styles/GridSplitterStyles.xaml",
                "AeVerticalGridSplitterStyle"),
            ResizeDirection = GridResizeDirection.Columns,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(vertical, 1);
        Grid.SetRow(vertical, 0);
        grid.Children.Add(vertical);

        var horizontal = new GridSplitter
        {
            Style = SharedStyle(
                "Common/Styles/GridSplitterStyles.xaml",
                "AeHorizontalGridSplitterStyle"),
            ResizeDirection = GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumnSpan(horizontal, 3);
        Grid.SetRow(horizontal, 1);
        grid.Children.Add(horizontal);
        card.Child = grid;
        return card;
    }

    private static FrameworkElement CreateDenseChinese()
    {
        var card = Card();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var tree = new TreeView { Style = Style("AeTree.View") };
        var root = TreeItem("帝国将军.pack", true);
        var folder = TreeItem("variantmeshes", true);
        folder.Items.Add(TreeItem("animations · 动画资源"));
        folder.Items.Add(TreeItem("textures · 材质与贴图"));
        folder.Items.Add(TreeItem("audio · 战役语音"));
        root.Items.Add(folder);
        tree.Items.Add(root);
        grid.Children.Add(tree);

        var right = new StackPanel
        {
            Width = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        right.Children.Add(Text("密集中文属性编辑器", "AeText.SectionTitle"));
        right.Children.Add(Field("资源名称", "empire_general_variant_01"));
        right.Children.Add(Field("输出目录", "C:\\战锤模组\\帝国将军\\输出"));
        right.Children.Add(Field("动画说明", "保持盾牌姿态并使用战役地图的站立循环动画"));
        var table = new DataGrid
        {
            Height = 230,
            Margin = new Thickness(0, 16, 0, 0),
            Style = Style("AeTable.Grid"),
            AutoGenerateColumns = false,
            ItemsSource = new[]
            {
                new Row("emp_general_body", "刚性模型", "已修改"),
                new Row("emp_general_diffuse", "纹理", "正常"),
                new Row("emp_general_idle", "动画片段", "待保存"),
            },
        };
        table.Columns.Add(new DataGridTextColumn { Header = "资源", Binding = new Binding(nameof(Row.Name)), Width = 480 });
        table.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding(nameof(Row.Type)), Width = 110 });
        table.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding(nameof(Row.State)), Width = 90 });
        right.Children.Add(table);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    private static FrameworkElement CreateWindowFont()
    {
        var card = Card();
        var stack = new StackPanel();
        stack.Children.Add(Text("窗口字体资源已贯通", "AeText.SectionTitle"));
        stack.Children.Add(Text(
            "默认字体：Segoe UI Variable Text、Segoe UI、Microsoft YaHei UI",
            "AeText.Body",
            new Thickness(0, 8, 0, 4)));
        stack.Children.Add(Text(
            "中文排版检查：模型、材质、动画、Pack、文件夹工程与版本控制。",
            "AeText.Body"));
        stack.Children.Add(Text(
            "empire_general_variant_01 · 0123456789 · Ctrl+Shift+S",
            "AeText.Technical",
            new Thickness(0, 12, 0, 20)));
        var buttons = new WrapPanel();
        buttons.Children.Add(Button("保存更改", "AeButton.Primary"));
        buttons.Children.Add(Button("生成 Pack", "AeButton.Secondary"));
        buttons.Children.Add(Button("放弃修改", "AeButton.Danger"));
        stack.Children.Add(buttons);
        card.Child = stack;
        return card;
    }

    private static Border Card() => new()
    {
        Padding = new Thickness(18),
        Style = Style("AeSurface.Panel"),
    };

    private static OptionalRadioButton Optional(
        string content,
        bool isChecked,
        string? tag = null,
        bool isEnabled = true) => new()
        {
            Content = content,
            IsChecked = isChecked,
            IsEnabled = isEnabled,
            Tag = tag,
            Margin = new Thickness(0, 0, 0, 10),
            Style = SharedStyle(
                "BaseDialogs/OptionalRadioButtonStyle.xaml",
                "OptionalRadioButtonStyle"),
        };

    private static FrameworkElement Field(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Text(label, "AeText.Label"));
        var input = new TextBox
        {
            Text = value,
            Style = Style("AeInput.TextBox"),
        };
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return grid;
    }

    private static void AddPane(
        Grid grid,
        string value,
        int column,
        int row,
        int columnSpan = 1)
    {
        var border = new Border
        {
            Padding = new Thickness(16),
            Background = Brush("AeBrush.Surface2"),
            BorderBrush = Brush("AeBrush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = Text(value, "AeText.Body"),
        };
        Grid.SetColumn(border, column);
        Grid.SetColumnSpan(border, columnSpan);
        Grid.SetRow(border, row);
        grid.Children.Add(border);
    }

    private static TreeViewItem TreeItem(string header, bool expanded = false) => new()
    {
        Header = header,
        IsExpanded = expanded,
        Style = Style("AeTree.Item"),
    };

    private static Button Button(string content, string styleKey) => new()
    {
        Content = content,
        Margin = new Thickness(0, 0, 8, 0),
        Style = Style(styleKey),
    };

    private static TextBlock Text(
        string value,
        string styleKey,
        Thickness? margin = null) => new()
        {
            Text = value,
            Margin = margin ?? new Thickness(),
            Style = Style(styleKey),
        };

    private static void AssertVisualContracts(AssetEditorWindow window)
    {
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(window.ActualWidth, Is.GreaterThan(800));
            NUnitAssert.That(window.ActualHeight, Is.GreaterThan(500));
            NUnitAssert.That(window.FontFamily.Source, Is.EqualTo(Font().Source));
            NUnitAssert.That(
                FindVisualDescendants<FrameworkElement>(window),
                Has.None.Matches<FrameworkElement>(element =>
                    double.IsNaN(element.ActualWidth) ||
                    double.IsNaN(element.ActualHeight) ||
                    element.ActualWidth < 0 ||
                    element.ActualHeight < 0));
        });
    }

    private static void Capture(
        Window window,
        ThemeType theme,
        string variant)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var outputDirectory = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        using var stream = File.Create(IOPath.Combine(
            outputDirectory,
            $"theme-completion-{variant}-{theme}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static Brush Brush(string key) =>
        (Brush)Application.Current.FindResource(key);

    private static FontFamily Font() =>
        (FontFamily)Application.Current.FindResource("AppFontFamily");

    private static Style Style(string key) =>
        (Style)Application.Current.FindResource(key);

    private static Style SharedStyle(string source, string key)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Shared.Ui;component/{source}"),
        };
        return (Style)dictionary[key];
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed record Row(string Name, string Type, string State);
}
