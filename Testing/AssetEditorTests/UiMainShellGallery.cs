using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        string variant) => WithMainShellServices(
            editorDatabase => Render(theme, variant, editorDatabase));

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void EditorTabs_ShowAllWrappedRowsAndReturnToSingleRowAtWideWidth(
        ThemeType theme) => WithWrappedEditorTabs(
            theme,
            (window, _, editors) =>
            {
                var headerPanel = (TabPanel)editors.Template.FindName(
                    "HeaderPanel",
                    editors);
                var headerBand = (Border)VisualTreeHelper.GetParent(
                    headerPanel);
                var tabs = GetTabItems(editors);
                var wrappedRows = RowOffsets(tabs, headerBand);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        headerBand.ActualHeight,
                        Is.EqualTo(49).Within(0.1));
                    NUnitAssert.That(wrappedRows, Has.Count.EqualTo(2));
                    NUnitAssert.That(wrappedRows[0], Is.GreaterThanOrEqualTo(0));
                    NUnitAssert.That(
                        wrappedRows[^1] + tabs[^1].ActualHeight,
                        Is.LessThanOrEqualTo(headerBand.ActualHeight + 0.1));
                });
                SaveSnapshot(window, $"main-shell-wrapped-{theme}.png");

                editors.Width = 1400;
                window.UpdateLayout();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        headerBand.ActualHeight,
                        Is.EqualTo(25).Within(0.1));
                    NUnitAssert.That(
                        RowOffsets(tabs, headerBand),
                        Has.Count.EqualTo(1));
                });
            });

    [Test]
    public void EditorTabCloseButton_DoesNotSelectBeforeClosingExactEditor() =>
        WithWrappedEditorTabs(
            ThemeType.DarkTheme,
            (window, viewModel, editors) =>
            {
                var headerPanel = (TabPanel)editors.Template.FindName(
                    "HeaderPanel",
                    editors);
                var selectedEditor =
                    (ShellPreviewEditor)editors.SelectedItem;
                var selectedTab = (TabItem)editors.ItemContainerGenerator
                    .ContainerFromItem(selectedEditor);
                var selectedRow = selectedTab.TranslatePoint(
                    new Point(),
                    headerPanel).Y;
                var targetTab = GetTabItems(editors).First(tab =>
                    !tab.IsSelected &&
                    Math.Abs(tab.TranslatePoint(
                        new Point(),
                        headerPanel).Y - selectedRow) > 0.1);
                var targetEditor = (ShellPreviewEditor)targetTab.DataContext;
                var closeButton = FindVisualChild<Button>(targetTab)!;

                RaiseLeftPreviewMouseDown(closeButton);
                window.UpdateLayout();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        editors.SelectedItem,
                        Is.SameAs(selectedEditor));
                    NUnitAssert.That(
                        viewModel.EditorManager.CurrentEditorsList,
                        Has.Count.EqualTo(5));
                });

                InvokeButton(closeButton);
                window.UpdateLayout();

                var itemsHolder = (Panel)editors.Template.FindName(
                    "PART_ItemsHolder",
                    editors);
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        viewModel.EditorManager.CurrentEditorsList,
                        Has.Count.EqualTo(4));
                    NUnitAssert.That(
                        viewModel.EditorManager.CurrentEditorsList,
                        Does.Not.Contain(targetEditor));
                    NUnitAssert.That(
                        editors.SelectedItem,
                        Is.SameAs(selectedEditor));
                    NUnitAssert.That(
                        VisibleContent(itemsHolder),
                        Is.SameAs(selectedEditor));
                });

                var nextSelectedTab = GetTabItems(editors)
                    .First(tab => !tab.IsSelected);
                var nextSelectedEditor =
                    (ShellPreviewEditor)nextSelectedTab.DataContext;
                RaiseLeftPreviewMouseDown(
                    FindVisualChild<TextBlock>(nextSelectedTab)!);
                window.UpdateLayout();
                NUnitAssert.That(
                    editors.SelectedItem,
                    Is.SameAs(nextSelectedEditor));

                var selectedCloseButton =
                    FindVisualChild<Button>(nextSelectedTab)!;
                InvokeButton(selectedCloseButton);
                window.UpdateLayout();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        viewModel.EditorManager.CurrentEditorsList,
                        Has.Count.EqualTo(3));
                    NUnitAssert.That(
                        viewModel.EditorManager.CurrentEditorsList,
                        Does.Not.Contain(nextSelectedEditor));
                    NUnitAssert.That(editors.SelectedItem, Is.Not.Null);
                    NUnitAssert.That(
                        viewModel.EditorManager.CurrentEditorsList,
                        Does.Contain(editors.SelectedItem));
                    NUnitAssert.That(
                        VisibleContent(itemsHolder),
                        Is.SameAs(editors.SelectedItem));
                });
            });

    private static void WithWrappedEditorTabs(
        ThemeType theme,
        Action<MainWindow, ShellPreviewViewModel, CachedTabControl> verify) =>
        WithMainShellServices(editorDatabase =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            MainWindow? window = null;
            try
            {
                ThemesController.SetTheme(theme);
                var viewModel = new ShellPreviewViewModel(
                    editorDatabase,
                    includeEditors: false,
                    isLoading: false,
                    gitEnabled: true);
                foreach (var name in s_wrappedEditorNames)
                {
                    viewModel.EditorManager.CurrentEditorsList.Add(
                        new ShellPreviewEditor(name));
                }

                window = new MainWindow(
                    ((IAssetEditorMain)Application.Current).ServiceProvider)
                {
                    Width = 1800,
                    Height = 760,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    DataContext = viewModel,
                };
                window.Show();
                window.UpdateLayout();

                var editors = (CachedTabControl)window.FindName(
                    "EditorsTabControl");
                editors.Width = 720;
                editors.HorizontalAlignment = HorizontalAlignment.Left;
                editors.SelectedIndex = 0;
                editors.ApplyTemplate();
                window.UpdateLayout();
                verify(window, viewModel, editors);
            }
            finally
            {
                window?.Close();
                ThemesController.SetTheme(previousTheme);
            }
        });

    private static void WithMainShellServices(
        Action<IEditorDatabase> action)
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
            () => action(editorDatabase.Object));
    }

    private static readonly string[] s_wrappedEditorNames =
    {
        "3k_dlc05_unit_wood_bandit_gang.variantmeshdefinition",
        "3k_dlc05_unit_wood_tiger_guard.variantmeshdefinition",
        "3k_dlc05_unit_metal_bandit_warriors.variantmeshdefinition",
        "3k_dlc05_unit_water_bandit_hunters.variantmeshdefinition",
        "3k_dlc05_unit_earth_handmaid_guard.variantmeshdefinition",
    };

    private static List<TabItem> GetTabItems(CachedTabControl editors) =>
        Enumerable.Range(0, editors.Items.Count)
            .Select(index => (TabItem)editors.ItemContainerGenerator
                .ContainerFromIndex(index))
            .ToList();

    private static List<double> RowOffsets(
        IEnumerable<TabItem> tabs,
        UIElement relativeTo) => tabs
            .Select(tab => Math.Round(
                tab.TranslatePoint(new Point(), relativeTo).Y,
                1))
            .Distinct()
            .Order()
            .ToList();

    private static void RaiseLeftPreviewMouseDown(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
            Source = element,
        });

    private static void InvokeButton(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var provider = (IInvokeProvider)peer.GetPattern(
            PatternInterface.Invoke);
        provider.Invoke();
        button.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static object VisibleContent(Panel itemsHolder) =>
        itemsHolder.Children
            .OfType<ContentPresenter>()
            .Single(presenter =>
                presenter.Visibility == Visibility.Visible)
            .Content;

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

            var bitmap = RenderToBitmap(window);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
                NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
            });

            SaveSnapshot(
                window,
                $"main-shell-{variant}-{theme}.png");

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
                        viewModel.HistoryWorkspace.SelectedSidebarTabIndex,
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

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private static void SaveSnapshot(
        FrameworkElement element,
        string fileName)
    {
        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var bitmap = RenderToBitmap(element);

        using var stream = File.Create(Path.Combine(
            outputDirectory,
            fileName));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static RenderTargetBitmap RenderToBitmap(
        FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
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
        public ShellPreviewHistoryWorkspace HistoryWorkspace { get; }
        public ShellPreviewEditorManager EditorManager { get; }
        public IEditorDatabase ToolsFactory { get; }
        public ICommand CloseToolCommand { get; }
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
            HistoryWorkspace = new ShellPreviewHistoryWorkspace(gitEnabled);
            EditorManager = new ShellPreviewEditorManager();
            CloseToolCommand = new ShellPreviewCommand(editor =>
                EditorManager.CurrentEditorsList.Remove(editor));
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

    private sealed class ShellPreviewHistoryWorkspace
    {
        public int SelectedSidebarTabIndex { get; set; }
        public bool IsEnabled { get; }

        public ShellPreviewHistoryWorkspace(bool isEnabled) =>
            IsEnabled = isEnabled;
    }

    private sealed class ShellPreviewEditorManager
    {
        public ObservableCollection<ShellPreviewEditor>
            CurrentEditorsList { get; } = [];
        public int SelectedEditorIndex { get; set; }
    }

    private sealed class ShellPreviewCommand(
        Action<ShellPreviewEditor> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) =>
            parameter is ShellPreviewEditor;

        public void Execute(object? parameter)
        {
            if (parameter is ShellPreviewEditor editor)
                execute(editor);
        }
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
