using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.Common;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiMainShellGallery
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCase(ThemeType.DarkTheme, "normal")]
    [TestCase(ThemeType.LightTheme, "normal")]
    [TestCase(ThemeType.HighContrastDark, "normal")]
    [TestCase(ThemeType.HighContrastLight, "normal")]
    [TestCase(ThemeType.DarkTheme, "narrow")]
    [TestCase(ThemeType.LightTheme, "narrow")]
    [TestCase(ThemeType.HighContrastDark, "narrow")]
    [TestCase(ThemeType.HighContrastLight, "narrow")]
    [TestCase(ThemeType.DarkTheme, "focus")]
    [TestCase(ThemeType.LightTheme, "focus")]
    [TestCase(ThemeType.HighContrastDark, "focus")]
    [TestCase(ThemeType.HighContrastLight, "focus")]
    public void MainShell_RendersRequiredThemeAndWidth(
        ThemeType theme,
        string variant)
    {
        var settings = new ApplicationSettingsService();
        var autoSaveService = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        var editorDatabase = new Mock<IEditorDatabase>();
        editorDatabase
            .Setup(database => database.GetViewTypeFromViewModel(
                typeof(ShellPreviewEditor)))
            .Returns(typeof(MainShellPreviewEditorView));
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .AddSingleton(settings)
            .AddSingleton(autoSaveService)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () => Render(theme, variant, editorDatabase.Object));
    }

    private static void Render(
        ThemeType theme,
        string variant,
        IEditorDatabase editorDatabase)
    {
        var previousTheme = ThemesController.CurrentTheme;
        MainWindow? window = null;

        try
        {
            ThemesController.SetTheme(theme);
            var isNarrow = variant == "narrow";
            var showKeyboardFocus = variant == "focus";
            var viewModel = new ShellPreviewViewModel(
                editorDatabase,
                includeEditors: !isNarrow,
                isLoading: isNarrow,
                gitEnabled: !isNarrow);
            window = new MainWindow(
                ((IAssetEditorMain)Application.Current).ServiceProvider)
            {
                Width = isNarrow ? 960 : 1180,
                Height = isNarrow ? 640 : 760,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                ShowInTaskbar = false,
                DataContext = viewModel,
            };

            var resourceTree = (FileTreeView)window.FindName(
                "ResourceTreeView");
            resourceTree.Content = CreateResourceTree();

            window.Show();
            window.UpdateLayout();

            var activityBar = (ListBox)window.FindName("ActivityBar");
            var resourcesItem = (ListBoxItem)activityBar.Items[0];
            if (showKeyboardFocus)
            {
                resourcesItem.Focus();
                Keyboard.Focus(resourcesItem);
                window.UpdateLayout();
            }
            var workspaceSidebar = (TabControl)window.FindName(
                "WorkspaceSidebar");
            var editors = (CachedTabControl)window.FindName(
                "EditorsTabControl");
            var statusBar = (Border)window.FindName(
                "ApplicationStatusBar");
            editors.ApplyTemplate();
            var itemsHolder = editors.Template.FindName(
                "PART_ItemsHolder",
                editors);
            var emptyState = (FrameworkElement)editors.Template.FindName(
                "EmptyEditorState",
                editors);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    activityBar.ActualWidth,
                    Is.EqualTo(30).Within(0.1));
                NUnitAssert.That(activityBar.SelectedIndex, Is.EqualTo(0));
                NUnitAssert.That(
                    workspaceSidebar.SelectedIndex,
                    Is.EqualTo(activityBar.SelectedIndex));
                NUnitAssert.That(itemsHolder, Is.Not.Null);
                NUnitAssert.That(statusBar.ActualHeight, Is.GreaterThanOrEqualTo(24));
                NUnitAssert.That(
                    ((ListBoxItem)activityBar.Items[1]).IsEnabled,
                    Is.EqualTo(!isNarrow));
                NUnitAssert.That(
                    emptyState.Visibility,
                    Is.EqualTo(isNarrow
                        ? Visibility.Visible
                        : Visibility.Collapsed));
                if (showKeyboardFocus)
                {
                    NUnitAssert.That(
                        resourcesItem.IsKeyboardFocused,
                        Is.True);
                }
            });

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
                var path = Path.Combine(
                    outputDirectory,
                    $"main-shell-{variant}-{theme}.png");
                using var stream = File.Create(path);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }

            if (variant == "normal")
            {
                editors.SelectedIndex = 1;
                editors.GetBindingExpression(
                    TabControl.SelectedIndexProperty)?.UpdateSource();
                activityBar.SelectedIndex = 1;
                activityBar.GetBindingExpression(
                    ListBox.SelectedIndexProperty)?.UpdateSource();
                workspaceSidebar.GetBindingExpression(
                    TabControl.SelectedIndexProperty)?.UpdateTarget();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        viewModel.EditorManager.SelectedEditorIndex,
                        Is.EqualTo(1));
                    NUnitAssert.That(
                        viewModel.GitWorkspace.SelectedSidebarTabIndex,
                        Is.EqualTo(1));
                    NUnitAssert.That(
                        workspaceSidebar.SelectedIndex,
                        Is.EqualTo(1));
                });
            }
        }
        finally
        {
            window?.Close();
            ThemesController.SetTheme(previousTheme);
        }
    }

    private static TreeView CreateResourceTree()
    {
        var tree = new TreeView
        {
            Style = Style("AeTree.View"),
        };
        var root = TreeItem("帝国将军.pack", true);
        var variants = TreeItem("variantmeshes", true);
        variants.IsSelected = true;
        variants.Items.Add(TreeItem("animations"));
        variants.Items.Add(TreeItem("textures"));
        variants.Items.Add(TreeItem("audio"));
        root.Items.Add(variants);
        root.Items.Add(TreeItem("db"));
        root.Items.Add(TreeItem("ui"));
        tree.Items.Add(root);
        return tree;
    }

    private static TreeViewItem TreeItem(
        string header,
        bool expanded = false) => new()
        {
            Header = header,
            IsExpanded = expanded,
            Style = Style("AeTree.Item"),
        };

    private static Style Style(string key) =>
        (Style)Application.Current.FindResource(key);

    private sealed class ShellPreviewViewModel
    {
        public string ApplicationTitle { get; } =
            "Asset Editor 国区版 · 工作区";
        public string CurrentGame { get; } = "全面战争：战锤 III";
        public string EditablePackFile { get; } = "帝国将军.pack";
        public bool IsPackFileExplorerVisible { get; } = true;
        public GridLength FileTreeColumnWidth { get; } =
            new(310, GridUnitType.Pixel);
        public object? FileTree { get; }
        public object MenuBar { get; } = new();
        public ShellPreviewGitWorkspace GitWorkspace { get; }
        public ShellPreviewEditorManager EditorManager { get; }
        public IEditorDatabase ToolsFactory { get; }
        public bool IsLoadingPacks { get; }
        public string LoadingStatusText { get; } = "正在读取 CA Pack";
        public string LoadingProgressDetailText { get; } =
            "第 2/3 步 · 正在合并 data.pack";
        public int LoadingProgressValue { get; } = 2;
        public int LoadingProgressMaximum { get; } = 3;
        public bool LoadingProgressIsIndeterminate { get; }

        public ShellPreviewViewModel(
            IEditorDatabase editorDatabase,
            bool includeEditors,
            bool isLoading,
            bool gitEnabled)
        {
            ToolsFactory = editorDatabase;
            GitWorkspace = new ShellPreviewGitWorkspace(gitEnabled);
            EditorManager = new ShellPreviewEditorManager();
            IsLoadingPacks = isLoading;
            LoadingProgressIsIndeterminate = isLoading;

            if (!includeEditors)
                return;

            EditorManager.CurrentEditorsList.Add(
                new ShellPreviewEditor("帝国将军模型"));
            EditorManager.CurrentEditorsList.Add(
                new ShellPreviewEditor("variantmeshes.xml", true));
            EditorManager.CurrentEditorsList.Add(
                new ShellPreviewEditor("材质编辑器"));
        }
    }

    private sealed class ShellPreviewGitWorkspace
    {
        public int SelectedSidebarTabIndex { get; set; }
        public bool IsEnabled { get; }

        public ShellPreviewGitWorkspace(bool isEnabled) =>
            IsEnabled = isEnabled;
    }

    private sealed class ShellPreviewEditorManager
    {
        public ObservableCollection<ShellPreviewEditor>
            CurrentEditorsList { get; } = [];
        public int SelectedEditorIndex { get; set; }
    }
}

public sealed class ShellPreviewEditor : IEditorInterface, ISaveableEditor
{
    public string DisplayName { get; set; }
    public bool HasUnsavedChanges { get; set; }

    public ShellPreviewEditor(
        string displayName,
        bool hasUnsavedChanges = false)
    {
        DisplayName = displayName;
        HasUnsavedChanges = hasUnsavedChanges;
    }

    public bool Save() => true;

    public void Close()
    {
    }
}

public sealed class MainShellPreviewEditorView : Border
{
    public MainShellPreviewEditorView()
    {
        SetResourceReference(BackgroundProperty, "AeBrush.Canvas");
        Padding = new Thickness(24);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(16),
        });
        layout.RowDefinitions.Add(new RowDefinition());

        var heading = new StackPanel();
        var title = new TextBlock
        {
            Style = FindStyle("AeText.PageTitle"),
        };
        title.SetBinding(TextBlock.TextProperty, "DisplayName");
        heading.Children.Add(title);
        heading.Children.Add(Text(
            "variantmeshes / wh_variantmodels",
            "AeText.Technical",
            new Thickness(0, 4, 0, 0)));
        layout.Children.Add(heading);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(16),
        });
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        content.Children.Add(EditorPanel(
            "资源信息",
            "名称",
            "emp_general_variant_01",
            "类型",
            "RigidModel"));
        var preview = EditorPanel(
            "当前选择",
            "变体",
            "帝国将军",
            "状态",
            "已加载");
        Grid.SetColumn(preview, 2);
        content.Children.Add(preview);
        Grid.SetRow(content, 2);
        layout.Children.Add(content);

        Child = layout;
    }

    private static Border EditorPanel(
        string title,
        string firstLabel,
        string firstValue,
        string secondLabel,
        string secondValue)
    {
        var panel = new Border
        {
            Padding = new Thickness(16),
            Style = FindStyle("AeSurface.Panel"),
        };
        var stack = new StackPanel();
        stack.Children.Add(Text(
            title,
            "AeText.SectionTitle",
            new Thickness(0, 0, 0, 12)));
        stack.Children.Add(Text(firstLabel, "AeText.Label"));
        stack.Children.Add(Input(firstValue));
        stack.Children.Add(Text(
            secondLabel,
            "AeText.Label",
            new Thickness(0, 12, 0, 0)));
        stack.Children.Add(Input(secondValue));
        panel.Child = stack;
        return panel;
    }

    private static TextBox Input(string value) => new()
    {
        Margin = new Thickness(0, 4, 0, 0),
        IsReadOnly = true,
        Text = value,
        Style = FindStyle("AeInput.TextBox"),
    };

    private static TextBlock Text(
        string value,
        string styleKey,
        Thickness? margin = null) => new()
        {
            Margin = margin ?? new Thickness(),
            Text = value,
            Style = FindStyle(styleKey),
        };

    private static Style FindStyle(string key) =>
        (Style)Application.Current.FindResource(key);
}
