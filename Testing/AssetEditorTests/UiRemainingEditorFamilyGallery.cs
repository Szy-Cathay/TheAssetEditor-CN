using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using AssetEditor.Views.Updater;
using CommonControls.BaseDialogs;
using CommonControls.BaseDialogs.ToolSelector;
using CommonControls.FilterDialog;
using CommonControls.MathViews;
using CommonControls.SelectionListDialog;
using Editors.CscEditor.Views;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Presentation;
using Editors.ImportExport.Exporting.Presentation.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Presentation.DdsToNormalPng;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.Importing.Presentation;
using Editors.ImportExport.Misc;
using Editors.Twui.Editor.ComponentEditor;
using Editors.Twui.Editor.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.BaseDialogs.SelectionListDialog;
using Shared.Ui.Common.DataTemplates;
using Shared.Ui.Common.ValueConverters;
using TextureEditor.Views;
using DdsToPngView = Editors.ImportExport.Exporting.Exporters.DdsToPng.DdsToPngView;
using RmvToGltfImporterView = Editors.ImportExport.Importing.Presentation.RmvToGltf.RmvToGltfImporterView;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiRemainingEditorFamilyGallery
{
    private static readonly string[] Variants =
    [
        "updater-window",
        "csc-editor",
        "export-dds-material",
        "export-dds-normal",
        "export-dds-png",
        "export-window",
        "export-rmv-gltf",
        "import-gltf-rmv",
        "import-window",
        "texture-information",
        "texture-preview",
        "twui-component",
        "twui-hierarchy",
        "twui-main",
        "shared-attribute",
        "shared-auto-attribute",
        "shared-colour-picker",
        "shared-controller-host",
        "shared-collapsible-filter",
        "shared-filter",
        "shared-matrix",
        "shared-vector2",
        "shared-vector3",
        "shared-vector4",
        "shared-selection-list",
        "shared-selection-window",
        "shared-tool-selector",
    ];

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCaseSource(nameof(Cases))]
    public void RemainingEditorFamily_RendersRequiredThemeAndState(
        ThemeType theme,
        string variant)
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () => Render(theme, variant));
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var theme in new[]
                 {
                     ThemeType.DarkTheme,
                     ThemeType.LightTheme,
                     ThemeType.HighContrastDark,
                     ThemeType.HighContrastLight,
                 })
        {
            foreach (var variant in Variants)
                yield return new TestCaseData(theme, variant);
        }
    }

    private static void Render(ThemeType theme, string variant)
    {
        var previousTheme = ThemesController.CurrentTheme;
        try
        {
            ThemesController.SetTheme(theme);
            RegisterApplicationResources();
            var window = CreateWindow(variant);
            ConfigureWindow(window, variant);
            try
            {
                window.Show();
                window.UpdateLayout();
                AssertVisualContracts(window, variant);
                Capture(window, theme, variant);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            ThemesController.SetTheme(previousTheme);
        }
    }

    private static void RegisterApplicationResources()
    {
        Application.Current.Resources["BoolToCollapsedConverter"] =
            new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Visible,
                FalseValue = Visibility.Collapsed,
            };
        Application.Current.Resources["BoolToHiddenConverter"] =
            new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Visible,
                FalseValue = Visibility.Hidden,
            };
        Application.Current.Resources["InvBoolConverter"] =
            new InverseBooleanConverter();
        Application.Current.Resources["ViewTemplateDataSelector"] =
            new ViewTemplateDataSelector();
    }

    private static Window CreateWindow(string variant) => variant switch
    {
        "updater-window" => new UpdaterWindow
        {
            DataContext = CreateUpdaterModel(),
        },
        "csc-editor" => Host(
            new CscEditorView { DataContext = CreateCscModel() },
            1200,
            800),
        "export-dds-material" => Host(
            new DdsToMaterialPngView
            {
                DataContext = new GalleryModel { SwapBlender = true },
            },
            680,
            240),
        "export-dds-normal" => Host(
            new DdsToNormalPngView(),
            680,
            240),
        "export-dds-png" => Host(
            new DdsToPngView(),
            680,
            240),
        "export-window" => CreateExportWindow(),
        "export-rmv-gltf" => Host(
            new RmvToGltfExporterView
            {
                DataContext = CreateRmvToGltfExporterModel(),
            },
            720,
            420),
        "import-gltf-rmv" => Host(
            new RmvToGltfImporterView
            {
                DataContext = new GalleryModel
                {
                    ImportMeshes = true,
                    ImportMaterials = true,
                    ImportAnimations = true,
                    ConvertFromBlenderMaterialMap = true,
                    ConvertNormalTextureToOrange = true,
                    AutoDetectAnimationKeysPerSecond = true,
                    CanEditAnimationKeysPerSecond = false,
                    AnimationKeysPerSecond = 30,
                },
            },
            760,
            420),
        "import-window" => CreateImportWindow(),
        "texture-information" => Host(
            new TextureInformationView
            {
                DataContext = "尺寸：2048 × 2048\n格式：BC7\nMip：12",
            },
            680,
            360),
        "texture-preview" => Host(
            new TexturePreviewView
            {
                DataContext = CreateTextureModel(),
            },
            1000,
            700),
        "twui-component" => Host(
            new ComponentView { DataContext = CreateTwuiModel() },
            520,
            520),
        "twui-hierarchy" => Host(
            new HierarchyView { DataContext = CreateTwuiModel() },
            560,
            620),
        "twui-main" => Host(
            new TwuiMainView { DataContext = CreateTwuiModel() },
            1120,
            720),
        "shared-attribute" => Host(
            new AeAttribute
            {
                LabelText = "资源名称",
                InnerContent = new TextBox { Text = "empire_general" },
            },
            720,
            160),
        "shared-auto-attribute" => Host(
            new AutoAeAttribute
            {
                LabelText = "动画速度",
                InnerContent = "1.000",
            },
            720,
            180),
        "shared-colour-picker" => Host(
            CreateColourPickerPreview(),
            420,
            180),
        "shared-controller-host" => CreateControllerHost(),
        "shared-collapsible-filter" => Host(
            CreateCollapsibleFilter(),
            760,
            360),
        "shared-filter" => Host(
            CreateFilter(),
            640,
            480),
        "shared-matrix" => Host(
            new Matrix3x4View { MatrixData = CreateMatrixData() },
            620,
            280),
        "shared-vector2" => Host(
            new Vector2View
            {
                DataContext = new Vector2ViewModel(1.25f, -0.5f),
            },
            540,
            180),
        "shared-vector3" => Host(
            new Vector3View { Vector3 = new Vector3ViewModel(1, 2, 3) },
            540,
            180),
        "shared-vector4" => Host(
            new Vector4View { Vector4 = new Vector4ViewModel(1, 2, 3, 4) },
            620,
            180),
        "shared-selection-list" => Host(
            new SelectionListView { DataContext = CreateSelectionModel() },
            760,
            500),
        "shared-selection-window" => CreateSelectionWindow(),
        "shared-tool-selector" => CreateToolSelector(),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static Window CreateExportWindow()
    {
        var exporter = new GalleryExporter();
        var viewModel = new ExporterCoreViewModel([exporter])
        {
            SystemPath = "C:\\输出\\empire_general.gltf",
            SelectedExporter = exporter,
        };
        viewModel.PossibleExporters.Add(exporter);
        return new ExportWindow(viewModel, Mock.Of<IStandardDialogs>());
    }

    private static Window CreateImportWindow()
    {
        var importer = new GalleryImporter();
        var viewModel = new ImporterCoreViewModel(
            [importer],
            new ApplicationSettingsService(GameTypeEnum.Warhammer3))
        {
            SystemPath = "C:\\导入\\empire_general.glb",
            SelectedImporter = importer,
        };
        viewModel.PossibleImporters.Add(importer);
        return new ImportWindow(viewModel, Mock.Of<IStandardDialogs>());
    }

    private static Window CreateControllerHost()
    {
        var window = new ControllerHostWindow();
        ((Grid)window.Content).Children.Add(new Border
        {
            Width = 500,
            Height = 180,
            Padding = new Thickness(16),
            Background = FindBrush("AeBrush.Surface1"),
            BorderBrush = FindBrush("AeBrush.Border"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "共享控制器内容",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        });
        return window;
    }

    private static CollapsableFilterControl CreateCollapsibleFilter() => new()
    {
        LabelText = "骨骼",
        LabelTotalWidth = 90,
        DisplayMemberPath = nameof(GalleryModel.DisplayName),
        SearchItems = CreateSearchItems(),
        SelectedItem = CreateSearchItems()[0],
    };

    private static FilterUserControl CreateFilter() => new()
    {
        DisplayMemberPath = nameof(GalleryModel.DisplayName),
        SearchItems = CreateSearchItems(),
        SelectedItem = CreateSearchItems()[0],
        InnerContent = new TextBlock
        {
            Text = "双击结果以选择资源",
            Margin = new Thickness(6),
        },
    };

    private static FrameworkElement CreateColourPickerPreview() => new Border
    {
        Margin = new Thickness(20),
        Padding = new Thickness(16),
        Background = FindBrush("AeBrush.Surface1"),
        BorderBrush = FindBrush("AeBrush.Border"),
        BorderThickness = new Thickness(1),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "视口背景颜色",
                    Width = 120,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new ColourPickerButtonView
                {
                    Width = 32,
                    Height = 32,
                    DataContext = new ColourPickerViewModel(
                        new Microsoft.Xna.Framework.Vector3(0.18f, 0.48f, 0.78f)),
                },
            },
        },
    };

    private static GalleryModel[] CreateSearchItems() =>
    [
        new GalleryModel { DisplayName = "empire_general_body" },
        new GalleryModel { DisplayName = "empire_general_weapon" },
        new GalleryModel { DisplayName = "empire_general_animation" },
    ];

    private static SelectionListViewModel<string> CreateSelectionModel()
    {
        var model = new SelectionListViewModel<string>
        {
            WindowTitle = "选择要导出的资源",
        };
        model.ItemList.Add(new SelectionListViewModel<string>.Item
        {
            DisplayName = "empire_general_body",
            ItemValue = "body",
        });
        model.ItemList.Add(new SelectionListViewModel<string>.Item
        {
            DisplayName = "empire_general_weapon",
            ItemValue = "weapon",
        });
        model.ItemList[0].IsChecked.Value = true;
        return model;
    }

    private static Window CreateSelectionWindow()
    {
        var window = new SelectionListWindow();
        window.SetDataContextAndFilterConfig(CreateSelectionModel());
        return window;
    }

    private static Window CreateToolSelector()
    {
        var window = new ToolSelectorWindow();
        if (window.FindName("PossibleTools") is ListView list)
        {
            list.Items.Add("模型编辑器");
            list.Items.Add("动画编辑器");
            list.Items.Add("纹理预览器");
            list.SelectedIndex = 0;
        }
        return window;
    }

    private static FileMatrix3x4ViewData CreateMatrixData() => new()
    {
        Name = "变换矩阵",
        Matrix = new ObservableCollection<Vector4ViewModel>
        {
            new(1, 0, 0, 0),
            new(0, 1, 0, 0),
            new(0, 0, 1, 0),
        },
    };

    private static GalleryModel CreateUpdaterModel() => new()
    {
        UpdateInfo = "发现新版本 2.2.0",
        UpdateCommand = GalleryCommand.Instance,
        CloseWindowActionCommand = GalleryCommand.Instance,
        ReleaseNotesItems = new[]
        {
            new GalleryModel
            {
                ReleaseName = "## Asset Editor CN 2.2.0",
                PublishedAt = "2026-08-05",
                ReleaseNotes = "- 改进界面一致性\n- 修复高 DPI 对齐\n- 优化中文编辑体验",
            },
        },
    };

    private static GalleryModel CreateRmvToGltfExporterModel()
    {
        var options = new[]
        {
            new GalleryModel { DisplayName = @"animations\battle\humanoid23\stand_idle.anim" },
            new GalleryModel { DisplayName = @"animations\battle\humanoid23\run.anim" },
        };
        return new GalleryModel
        {
            ExportTextures = true,
            ConvertMaterialTextureToBlender = true,
            ConvertNormalTextureToBlue = true,
            HasSkeleton = true,
            ExportSkeleton = true,
            CanExportAnimations = true,
            ExportAnimations = true,
            AvailableAnimations = options,
            SelectedAvailableAnimation = options[1],
            AnimationFiles = new[] { options[0] },
            SelectedAnimation = options[0],
            CanAddAnimation = true,
            CanRemoveAnimation = true,
            AddAnimationCommand = GalleryCommand.Instance,
            RemoveAnimationCommand = GalleryCommand.Instance,
        };
    }

    private static GalleryModel CreateCscModel() => new()
    {
        CanUndo = true,
        CanRedo = true,
        HasSelection = false,
        IsLookingThroughCamera = false,
        Scene = new Border
        {
            Background = FindBrush("AeBrush.Surface1"),
            Child = new TextBlock
            {
                Text = "CSC 场景预览",
                Foreground = FindBrush("AeBrush.TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
        SceneRootItems = Array.Empty<object>(),
        IsElementSelected = false,
        IsSceneRootSelected = false,
        Duration = 4.5,
        CurrentTime = 1.25,
        IsPlaying = false,
        Playback = new GalleryModel { Loop = true },
        CurveSeriesList = Array.Empty<object>(),
        CurveStructureLocked = false,
        StatusText = "就绪",
        DisplayName = "empire_general_scene.csc",
        HasUnsavedChanges = true,
        PlayPauseCommand = GalleryCommand.Instance,
        StopCommand = GalleryCommand.Instance,
        DeleteSelectedCommand = GalleryCommand.Instance,
        FocusSelectedCommand = GalleryCommand.Instance,
        ResetViewCommand = GalleryCommand.Instance,
    };

    private static GalleryModel CreateTextureModel() => new()
    {
        ViewModel = new GalleryModel
        {
            UvChannelSelectedValue = new GalleryValue { Value = 0 },
            UvChannelPossibleValues = new[] { 0, 1, 2 },
            Format = new GalleryValue { Value = "BC7" },
            Width = new GalleryValue { Value = 2048 },
            Height = new GalleryValue { Value = 2048 },
            NumMipMaps = new GalleryValue { Value = 12 },
            ImagePath = new GalleryValue { Value = "textures/empire/general_body.dds" },
            PreviewImage = new object?[5],
            ActiveImage = new GalleryValue(),
            FormatACheckbox = true,
            FormatRCheckbox = true,
            FormatGCheckbox = true,
            FormatBCheckbox = true,
            FormatRgbaCheckbox = true,
        },
    };

    private static GalleryModel CreateTwuiModel()
    {
        var component = new GalleryModel
        {
            Name = "empire_panel",
            Priority = 10,
            ShowInPreviewRenderer = true,
            Children = new[]
            {
                new GalleryModel
                {
                    Name = "title_text",
                    Priority = 20,
                    ShowInPreviewRenderer = true,
                    Children = Array.Empty<object>(),
                },
            },
        };
        return new GalleryModel
        {
            ParsedTwuiFile = new GalleryModel
            {
                Componenets = new[] { component },
            },
            ComponentManager = new GalleryModel
            {
                SelectedComponent = component,
                ToggleSelectedCommand = GalleryCommand.Instance,
                SelectedComponentViewModel = new GalleryModel
                {
                    TestString = "标题",
                    TestFloat = 1.0,
                    TextVector2 = new Vector2ViewModel(10, 20),
                    TestBool = true,
                },
            },
            Scene = new Border
            {
                Margin = new Thickness(8),
                Background = FindBrush("AeBrush.Surface1"),
                BorderBrush = FindBrush("AeBrush.Border"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "TWUI 预览",
                    Foreground = FindBrush("AeBrush.TextMuted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
    }

    private static Window Host(
        FrameworkElement content,
        double width,
        double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            Background = FindBrush("AeBrush.Canvas"),
            Foreground = FindBrush("AeBrush.TextPrimary"),
            FontFamily = FindFontFamily(),
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Shared.Ui;component/Common/Styles/EditorWorkspaceStyles.xaml"),
        });
        return window;
    }

    private static void ConfigureWindow(Window window, string variant)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowInTaskbar = false;
        window.Title = $"AE UI · {variant}";

        switch (variant)
        {
            case "updater-window":
                window.Width = 1000;
                break;
            case "export-window":
                window.Width = 680;
                window.Height = 420;
                break;
            case "import-window":
                window.Width = 820;
                break;
            case "shared-selection-window":
                window.Width = 880;
                window.Height = 600;
                break;
            case "shared-tool-selector":
                window.Width = 420;
                window.Height = 360;
                break;
        }
    }

    private static void AssertVisualContracts(Window window, string variant)
    {
        var buttons = FindVisualDescendants<Button>(window).ToArray();
        var textBoxes = FindVisualDescendants<TextBox>(window).ToArray();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(window.ActualWidth, Is.GreaterThan(300));
            NUnitAssert.That(window.ActualHeight, Is.GreaterThan(80));
            NUnitAssert.That(buttons, Has.All.Matches<Button>(button =>
                button.ActualHeight >= 0 && button.Style is not null));
            NUnitAssert.That(textBoxes, Has.All.Matches<TextBox>(textBox =>
                textBox.ActualHeight >= 0 && textBox.Style is not null));
            NUnitAssert.That(
                FindVisualDescendants<FrameworkElement>(window),
                Has.None.Matches<FrameworkElement>(element =>
                    double.IsNaN(element.ActualWidth) ||
                    double.IsNaN(element.ActualHeight) ||
                    element.ActualWidth < 0 ||
                    element.ActualHeight < 0));
        });

        if (variant == "csc-editor")
        {
            NUnitAssert.That(
                FindVisualDescendants<CurveEditorControl>(window).Count(),
                Is.EqualTo(1));
        }

        if (variant is "shared-selection-list" or "shared-selection-window")
        {
            var visibleText = FindVisualDescendants<TextBlock>(window)
                .Select(textBlock => textBlock.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(visibleText, Does.Contain("empire_general_body"));
                NUnitAssert.That(visibleText, Does.Contain("empire_general_weapon"));
                NUnitAssert.That(
                    visibleText,
                    Has.None.Contains("SelectionListViewModel"));
            });
        }

        if (variant == "shared-colour-picker")
        {
            var picker = FindVisualDescendants<ColourPickerButtonView>(window)
                .Single();
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(picker.ActualWidth, Is.EqualTo(32).Within(0.5));
                NUnitAssert.That(picker.ActualHeight, Is.EqualTo(32).Within(0.5));
            });
        }
    }

    private static void Capture(
        Window window,
        ThemeType theme,
        string variant)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(
                window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(
                window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            $"remaining-editor-{variant}-{theme}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);

    private static FontFamily FindFontFamily() =>
        (FontFamily)Application.Current.FindResource("AppFontFamily");

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class GalleryCommand : ICommand
    {
        public static GalleryCommand Instance { get; } = new();

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class GalleryExporter :
        IExporterViewModel,
        IViewProvider<RmvToGltfExporterView>
    {
        public GalleryExporter()
        {
            SelectedAvailableAnimation = AvailableAnimations.FirstOrDefault();
        }

        public string DisplayName => "RMV2 → glTF";
        public string OutputExtension => ".gltf";
        public bool ExportTextures { get; set; } = true;
        public bool ConvertMaterialTextureToBlender { get; set; } = true;
        public bool ConvertNormalTextureToBlue { get; set; } = true;
        public bool HasSkeleton { get; set; } = true;
        public bool ExportSkeleton { get; set; } = true;
        public bool CanExportAnimations => HasSkeleton && ExportSkeleton;
        public bool ExportAnimations { get; set; } = true;
        public ObservableCollection<GalleryModel> AvailableAnimations { get; } =
        [
            new GalleryModel { DisplayName = @"animations\battle\humanoid23\stand_idle.anim" },
        ];
        public ObservableCollection<GalleryModel> AnimationFiles { get; } = [];
        public GalleryModel? SelectedAvailableAnimation { get; set; }
        public GalleryModel? SelectedAnimation { get; set; }
        public bool CanAddAnimation => true;
        public bool CanRemoveAnimation => SelectedAnimation != null;
        public ICommand AddAnimationCommand => GalleryCommand.Instance;
        public ICommand RemoveAnimationCommand => GalleryCommand.Instance;
        public bool Execute(
            PackFile exportSource,
            string outputPath)
        {
            return true;
        }
        public ExportSupportEnum CanExportFile(PackFile file) =>
            ExportSupportEnum.HighPriority;
    }

    private sealed class GalleryImporter :
        IImporterViewModel,
        IViewProvider<RmvToGltfImporterView>
    {
        public string DisplayName => "glTF → RMV2";
        public string OutputExtension => ".rigid_model_v2";
        public string[] InputExtensions => [".gltf", ".glb"];
        public bool ImportMeshes { get; set; } = true;
        public bool ImportMaterials { get; set; } = true;
        public bool ConvertFromBlenderMaterialMap { get; set; } = true;
        public bool ConvertNormalTextureToOrange { get; set; } = true;
        public bool ImportAnimations { get; set; } = true;
        public bool AutoDetectAnimationKeysPerSecond { get; set; } = true;
        public bool CanEditAnimationKeysPerSecond =>
            ImportAnimations && !AutoDetectAnimationKeysPerSecond;
        public float AnimationKeysPerSecond { get; set; } = 30;
        public bool Execute(
            PackFile exportSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType)
        {
            return true;
        }
        public ImportSupportEnum CanImportFile(PackFile file) =>
            ImportSupportEnum.HighPriority;
    }

    private sealed class GalleryValue
    {
        public object? Value { get; set; }
    }

#pragma warning disable CA1812
    private sealed class GalleryModel
    {
        public object? ActiveImage { get; set; }
        public object? AddAnimationCommand { get; set; }
        public object? AnimationFiles { get; set; }
        public object? AnimationKeysPerSecond { get; set; }
        public object? AvailableAnimations { get; set; }
        public object? AutoDetectAnimationKeysPerSecond { get; set; }
        public object? CanEditAnimationKeysPerSecond { get; set; }
        public object? CanAddAnimation { get; set; }
        public object? CanExportAnimations { get; set; }
        public object? CanRemoveAnimation { get; set; }
        public object? CanRedo { get; set; }
        public object? CanUndo { get; set; }
        public object? Children { get; set; }
        public object? CloseWindowActionCommand { get; set; }
        public object? Componenets { get; set; }
        public object? ComponentManager { get; set; }
        public object? ConvertFromBlenderMaterialMap { get; set; }
        public object? ConvertMaterialTextureToBlender { get; set; }
        public object? ConvertNormalTextureToBlue { get; set; }
        public object? ConvertNormalTextureToOrange { get; set; }
        public object? CurrentTime { get; set; }
        public object? CurveSeriesList { get; set; }
        public object? CurveStructureLocked { get; set; }
        public object? DeleteSelectedCommand { get; set; }
        public object? DisplayName { get; set; }
        public object? Duration { get; set; }
        public object? ExportAnimations { get; set; }
        public object? ExportSkeleton { get; set; }
        public object? ExportTextures { get; set; }
        public object? FocusSelectedCommand { get; set; }
        public object? Format { get; set; }
        public object? FormatACheckbox { get; set; }
        public object? FormatBCheckbox { get; set; }
        public object? FormatGCheckbox { get; set; }
        public object? FormatRCheckbox { get; set; }
        public object? FormatRgbaCheckbox { get; set; }
        public object? HasSelection { get; set; }
        public object? HasSkeleton { get; set; }
        public object? HasUnsavedChanges { get; set; }
        public object? Height { get; set; }
        public object? ImagePath { get; set; }
        public object? ImportAnimations { get; set; }
        public object? ImportMaterials { get; set; }
        public object? ImportMeshes { get; set; }
        public object? IsElementSelected { get; set; }
        public object? IsLookingThroughCamera { get; set; }
        public object? IsPlaying { get; set; }
        public object? IsSceneRootSelected { get; set; }
        public object? Loop { get; set; }
        public object? Name { get; set; }
        public object? NumMipMaps { get; set; }
        public object? ParsedTwuiFile { get; set; }
        public object? Playback { get; set; }
        public object? PlayPauseCommand { get; set; }
        public object? PreviewImage { get; set; }
        public object? Priority { get; set; }
        public object? PublishedAt { get; set; }
        public object? ReleaseName { get; set; }
        public object? ReleaseNotes { get; set; }
        public object? ReleaseNotesItems { get; set; }
        public object? RemoveAnimationCommand { get; set; }
        public object? ResetViewCommand { get; set; }
        public object? Scene { get; set; }
        public object? SceneRootItems { get; set; }
        public object? SelectedComponent { get; set; }
        public object? SelectedComponentViewModel { get; set; }
        public object? SelectedAnimation { get; set; }
        public object? SelectedAvailableAnimation { get; set; }
        public object? ShowInPreviewRenderer { get; set; }
        public object? StatusText { get; set; }
        public object? StopCommand { get; set; }
        public object? SwapBlender { get; set; }
        public object? TestBool { get; set; }
        public object? TestFloat { get; set; }
        public object? TestString { get; set; }
        public object? TextVector2 { get; set; }
        public object? ToggleSelectedCommand { get; set; }
        public object? UpdateCommand { get; set; }
        public object? UpdateInfo { get; set; }
        public object? UvChannelPossibleValues { get; set; }
        public object? UvChannelSelectedValue { get; set; }
        public object? ViewModel { get; set; }
        public object? Width { get; set; }
    }
#pragma warning restore CA1812
}
