using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Importing.Presentation;
using Editors.ImportImport.Importing.Presentation.RmvToGltf;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Editors.Twui.Editor.ComponentEditor;
using Editors.Twui.Editor.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.BaseDialogs.SelectionListDialog;
using Shared.Ui.BaseDialogs.StandardDialog;
using Shared.Ui.Common.DataTemplates;
using Shared.Ui.Common.OperationProgress;
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
        "import-result",
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

    [TestCaseSource(nameof(GltfWorkflowDpiCases))]
    public void GltfImportWorkflow_RealWindowsUseRequiredDpi(
        ThemeType theme,
        string variant,
        int dpi)
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () => Render(theme, variant, dpi));
    }

    [Test]
    public void GltfImportWindow_ClickingImportRunsRealWorkflowAndShowsProgress()
    {
        var glbPath = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "ImportExportEditor",
            "Test.ImportExport",
            "TestData",
            "Gltf",
            "external_full_workflow.glb");
        var importerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var importerCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseImporter = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resultShown = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var destination = new PackFileContainer("actual-import-window");
        var dialogs = new Mock<IStandardDialogs>();
        var resultMessage = string.Empty;
        dialogs
            .Setup(service => service.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>(),
                UiMessageBoxIcon.Information))
            .Callback<string, string, UiMessageBoxIcon>((message, _, _) =>
            {
                resultMessage = message;
                resultShown.TrySetResult(true);
            });
        ImportWindow? importWindow = null;
        BlockingGltfImporterViewModel? importer = null;
        ImporterCoreViewModel? viewModel = null;
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        try
        {
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                RegisterApplicationResources();
                importer = CreateBlockingGltfImporter(
                    destination,
                    importerStarted,
                    importerCompleted,
                    releaseImporter);
                viewModel = new ImporterCoreViewModel(
                    [importer],
                    new ApplicationSettingsService(GameTypeEnum.Warhammer3));
                viewModel.Initialize(destination, "models", glbPath);
                importWindow = new ImportWindow(viewModel, dialogs.Object)
                {
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                };
                importWindow.Show();
                importWindow.UpdateLayout();
                FindVisualDescendants<Button>(importWindow)
                    .Single(button => button.IsDefault)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            NUnitAssert.That(
                importerStarted.Task.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "实际导入器没有从导入按钮启动。");
            NUnitAssert.That(
                SpinWait.SpinUntil(
                    () => HasOwnedProgressWindow(importWindow),
                    TimeSpan.FromSeconds(5)),
                Is.True,
                "统一进度窗口没有在实际导入期间显示。");
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                NUnitAssert.That(viewModel!.IsOperationActive, Is.True);
                var progressWindow = Application.Current.Windows
                    .OfType<OperationProgressWindow>()
                    .Single(window => window.Owner == importWindow);
                var progressView = FindVisualDescendants<OperationProgressView>(
                    progressWindow).Single();
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(progressWindow.IsVisible, Is.True);
                    NUnitAssert.That(
                        progressWindow.Title,
                        Is.EqualTo("正在导入 glTF/GLB"));
                    NUnitAssert.That(
                        progressView.StatusText,
                        Is.EqualTo("正在转换网格 1/2…"));
                    NUnitAssert.That(
                        progressView.CurrentDetailText,
                        Is.EqualTo("模型“body” · 顶点 25/100"));
                    NUnitAssert.That(progressView.ProgressValue, Is.EqualTo(25));
                    NUnitAssert.That(progressView.ProgressMaximum, Is.EqualTo(100));
                    NUnitAssert.That(progressView.IsProgressIndeterminate, Is.False);
                });
                Capture(
                    progressWindow,
                    ThemesController.CurrentTheme,
                    "import-live-progress",
                    null);
            });

            releaseImporter.TrySetResult(true);
            NUnitAssert.That(
                resultShown.Task.Wait(TimeSpan.FromSeconds(20)),
                Is.True,
                "实际导入完成后没有显示中文结果摘要。");
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(importer!.ExecuteCalled, Is.True);
                    NUnitAssert.That(viewModel!.IsOperationActive, Is.False);
                    NUnitAssert.That(importWindow!.IsVisible, Is.False);
                    NUnitAssert.That(resultMessage, Does.Contain("已写入资源"));
                    NUnitAssert.That(
                        resultMessage,
                        Does.Contain("external_full_workflow.rigid_model_v2"));
                    NUnitAssert.That(resultMessage, Does.Contain("蒙皮权重"));
                    NUnitAssert.That(resultMessage, Does.Contain("源模型正面方向"));
                    NUnitAssert.That(resultMessage, Does.Contain("+Z（标准 glTF）"));
                    NUnitAssert.That(
                        destination.FileList.Keys,
                        Has.Some.EndsWith(".rigid_model_v2"));
                    NUnitAssert.That(
                        destination.FileList.Keys.Count(path =>
                            path.EndsWith(".anim")),
                        Is.EqualTo(3));
                    NUnitAssert.That(
                        destination.FileList.Keys,
                        Has.Some.EndsWith(".dds"));
                });
            });
        }
        finally
        {
            releaseImporter.TrySetResult(true);
            if (importer?.ExecuteCalled == true)
            {
                importerCompleted.Task.Wait(TimeSpan.FromSeconds(20));
                SpinWait.SpinUntil(
                    () => viewModel?.IsOperationActive == false,
                    TimeSpan.FromSeconds(5));
            }
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                if (importWindow?.IsVisible == true)
                    importWindow.Close();
            });
        }
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

    private static IEnumerable<TestCaseData> GltfWorkflowDpiCases()
    {
        foreach (var theme in new[]
                 {
                     ThemeType.DarkTheme,
                     ThemeType.LightTheme,
                     ThemeType.HighContrastDark,
                     ThemeType.HighContrastLight,
                 })
        {
            foreach (var variant in new[]
                     {
                         "import-window",
                         "import-progress",
                         "import-result",
                     })
            {
                foreach (var dpi in new[] { 96, 120, 144 })
                    yield return new TestCaseData(theme, variant, dpi);
            }
        }
    }

    private static void Render(
        ThemeType theme,
        string variant,
        int? targetDpi = null)
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
                NUnitAssert.That(
                    PresentationSource.FromVisual(window),
                    Is.TypeOf<HwndSource>());
                if (targetDpi != null)
                {
                    ApplyWindowDpi(window, targetDpi.Value);
                    window.UpdateLayout();
                    NUnitAssert.That(
                        VisualTreeHelper.GetDpi(window).PixelsPerInchX,
                        Is.EqualTo(targetDpi.Value).Within(0.5));
                }
                AssertVisualContracts(window, variant);
                Capture(window, theme, variant, targetDpi);
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
                    NewSkeletonName = "ExternalArmature",
                    ImportMeshes = true,
                    ImportMaterials = true,
                    ImportAnimations = true,
                    ConvertFromBlenderMaterialMap = true,
                    ConvertNormalTextureToOrange = true,
                    AutoScaleHumanoid = true,
                    SourceForwardDirections = CreateSourceForwardDirections(),
                    SourceForwardDirection =
                        GltfSourceForwardDirection.PositiveX,
                    AutoDetectAnimationKeysPerSecond = true,
                    CanEditAnimationKeysPerSecond = false,
                    AnimationKeysPerSecond = 30,
                },
            },
            760,
            460),
        "import-result" => new MessageDialogWindow(
            "导入成功",
            GltfWorkflowResultMessage,
            MessageDialogButtonSet.Ok,
            MessageBoxImage.Information),
        "import-progress" => CreateImportProgressWindow(),
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

    private static Window CreateImportProgressWindow() =>
        new OperationProgressWindow(new OperationProgressWindowHost
        {
            WindowTitle = "正在导入 glTF/GLB",
            StatusText = "正在转换模型、骨架、动画与贴图",
            IsOperationActive = true,
            IsProgressIndeterminate = true,
        });

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
            case "import-progress":
                window.Title = "正在导入 glTF/GLB";
                window.Width = 640;
                break;
            case "import-result":
                window.Width = 720;
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

        if (variant == "import-gltf-rmv")
        {
            var autoScaleCheckBox = FindVisualDescendants<CheckBox>(window)
                .Single(checkBox => AutomationProperties.GetName(checkBox) ==
                    "自动缩放到 humanoid01 人形尺寸");
            var sourceForwardComboBox = FindVisualDescendants<ComboBox>(window)
                .Single(comboBox => AutomationProperties.GetName(comboBox) ==
                    "源模型正面方向");
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(autoScaleCheckBox.IsChecked, Is.True);
                NUnitAssert.That(
                    sourceForwardComboBox.SelectedValue,
                    Is.EqualTo(GltfSourceForwardDirection.PositiveX));
                NUnitAssert.That(
                    sourceForwardComboBox.Items
                        .Cast<KeyValuePair<string, GltfSourceForwardDirection>>()
                        .Select(option => option.Key),
                    Is.EqualTo(new[]
                    {
                        "+Z（标准 glTF）",
                        "+X（Unreal/PSK）",
                        "-X",
                        "-Z",
                    }));
            });
        }

        if (variant == "import-window")
        {
            var sourceForwardComboBox = FindVisualDescendants<ComboBox>(window)
                .Single(comboBox => AutomationProperties.GetName(comboBox) ==
                    "源模型正面方向");
            NUnitAssert.That(
                sourceForwardComboBox.SelectedValue,
                Is.EqualTo(GltfSourceForwardDirection.PositiveZ));
        }

        if (variant == "import-result")
        {
            var dialog = (MessageDialogWindow)window;
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(dialog.Message, Does.Contain("已写入资源"));
                NUnitAssert.That(dialog.Message, Does.Contain("最终倍率：0.5"));
                NUnitAssert.That(dialog.Message, Does.Contain("蒙皮权重"));
                NUnitAssert.That(dialog.Message, Does.Contain("MaskMaterial"));
                NUnitAssert.That(dialog.Message, Does.Contain("自发光（Emissive）"));
                NUnitAssert.That(dialog.Message, Does.Contain("环境遮蔽（Occlusion）"));
                NUnitAssert.That(dialog.Message, Does.Contain("源模型正面方向"));
                NUnitAssert.That(dialog.Message, Does.Contain("+X（Unreal/PSK）"));
            });
        }

        if (variant == "import-progress")
        {
            NUnitAssert.That(window, Is.TypeOf<OperationProgressWindow>());
            NUnitAssert.That(window.Title, Is.EqualTo("正在导入 glTF/GLB"));
            NUnitAssert.That(
                FindVisualDescendants<TextBlock>(window)
                    .Select(textBlock => textBlock.Text),
                Does.Contain("正在转换模型、骨架、动画与贴图"));
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
        string variant,
        int? targetDpi)
    {
        var actualDpi = VisualTreeHelper.GetDpi(window);
        var pixelsPerInch = actualDpi.PixelsPerInchX;
        var dpiScale = pixelsPerInch / 96.0;
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(
                window.ActualWidth * dpiScale)),
            Math.Max(1, (int)Math.Ceiling(
                window.ActualHeight * dpiScale)),
            pixelsPerInch,
            pixelsPerInch,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(bitmap.PixelWidth, Is.GreaterThan(0));
            NUnitAssert.That(bitmap.PixelHeight, Is.GreaterThan(0));
        });

        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            targetDpi == null
                ? $"remaining-editor-{variant}-{theme}.png"
                : $"gltf-workflow-{variant}-{theme}-{targetDpi}dpi.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);

    private static FontFamily FindFontFamily() =>
        (FontFamily)Application.Current.FindResource("AppFontFamily");

    private static void ApplyWindowDpi(Window window, int dpi)
    {
        var handle = new WindowInteropHelper(window).Handle;
        NUnitAssert.That(GetWindowRect(handle, out var suggestedRectangle), Is.True);
        var packedDpi = new IntPtr(dpi | (dpi << 16));
        SendMessage(handle, WmDpiChanged, packedDpi, ref suggestedRectangle);
    }

    private static bool HasOwnedProgressWindow(Window? owner) =>
        Application.Current.Dispatcher.Invoke(() =>
            Application.Current.Windows
                .OfType<OperationProgressWindow>()
                .Any(window => window.Owner == owner && window.IsVisible));

    private static BlockingGltfImporterViewModel CreateBlockingGltfImporter(
        PackFileContainer destination,
        TaskCompletionSource<bool> importerStarted,
        TaskCompletionSource<bool> importerCompleted,
        TaskCompletionSource<bool> releaseImporter)
    {
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetAllPackfileContainers())
            .Returns([]);
        packFileService
            .Setup(service => service.AddFilesToPack(
                destination,
                It.IsAny<List<NewPackFileEntry>>(),
                It.IsAny<bool>()))
            .Callback<PackFileContainer, List<NewPackFileEntry>, bool>(
                (container, entries, _) =>
                {
                    foreach (var entry in entries)
                    {
                        var path = string.IsNullOrWhiteSpace(entry.DirectoyPath)
                            ? entry.PackFile.Name
                            : $"{entry.DirectoyPath}\\{entry.PackFile.Name}";
                        container.FileList[path.ToLowerInvariant()] =
                            entry.PackFile;
                    }
                });
        var importer = new GltfImporter(
            packFileService.Object,
            Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new RmvMaterialBuilder());
        return new BlockingGltfImporterViewModel(
            importer,
            importerStarted,
            importerCompleted,
            releaseImporter)
        {
            AutoScaleHumanoid = false,
        };
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(
            NUnit.Framework.TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AssetEditor.CN.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 AssetEditor.CN.sln。");
    }

    private const int WmDpiChanged = 0x02E0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        ref NativeRectangle lParam);

    private const string GltfWorkflowResultMessage = """
        已写入资源
        • models\external_full_workflow.rigid_model_v2
        • animations\skeletons\externalworkflowarmature.anim
        • models\external_full_workflow_move.anim
        • models\external_full_workflow_nod.anim
        • models\tex\externalworkflowmesh_part1_base_colour.dds

        警告
        • 蒙皮权重：6 个顶点超过四骨权重，已保留最强四项并重新归一化；丢弃权重总量为 15.0%。
        • 缺少法线，已根据最终三角形重建。
        • 缺少切线，已根据最终三角形重建。

        材质导入摘要
        • MaskMaterial：透明遮罩阈值 0.4
        • MaskMaterial：跳过自发光（Emissive）
        • MaskMaterial：跳过环境遮蔽（Occlusion）

        自动人形缩放
        • 源人物高度：4
        • humanoid01 参考高度：2
        • 最终倍率：0.5
        • 已自动缩放到 humanoid01 人形尺寸。

        源模型正面方向
        • 实际采用：+X（Unreal/PSK）
        • 已将源 +X 确定性旋转到游戏 +Z 正面。
        """;

    private static IReadOnlyList<
        KeyValuePair<string, GltfSourceForwardDirection>>
        CreateSourceForwardDirections() =>
        [
            new("+Z（标准 glTF）", GltfSourceForwardDirection.PositiveZ),
            new("+X（Unreal/PSK）", GltfSourceForwardDirection.PositiveX),
            new("-X", GltfSourceForwardDirection.NegativeX),
            new("-Z", GltfSourceForwardDirection.NegativeZ),
        ];

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

    private sealed class BlockingGltfImporterViewModel :
        RmvToGltfImporterViewModel,
        IImporterViewModel
    {
        private readonly TaskCompletionSource<bool> _importerStarted;
        private readonly TaskCompletionSource<bool> _importerCompleted;
        private readonly TaskCompletionSource<bool> _releaseImporter;

        public BlockingGltfImporterViewModel(
            GltfImporter importer,
            TaskCompletionSource<bool> importerStarted,
            TaskCompletionSource<bool> importerCompleted,
            TaskCompletionSource<bool> releaseImporter)
            : base(importer)
        {
            _importerStarted = importerStarted;
            _importerCompleted = importerCompleted;
            _releaseImporter = releaseImporter;
        }

        public bool ExecuteCalled { get; private set; }

        ImportResult IImporterViewModel.Execute(
            PackFile importSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType) =>
            ExecuteCore(
                importSource,
                outputPath,
                packFileContainer,
                gameType,
                null);

        ImportResult IImporterViewModel.Execute(
            PackFile importSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType,
            IProgress<OperationProgressUpdate>? progress) =>
            ExecuteCore(
                importSource,
                outputPath,
                packFileContainer,
                gameType,
                progress);

        private ImportResult ExecuteCore(
            PackFile importSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType,
            IProgress<OperationProgressUpdate>? progress)
        {
            ExecuteCalled = true;
            progress?.Report(new OperationProgressUpdate(
                "正在转换网格 1/2…",
                "模型“body” · 顶点 25/100",
                25,
                100));
            _importerStarted.TrySetResult(true);
            try
            {
                if (!_releaseImporter.Task.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("等待实际导入窗口进度验收超时。");
                return base.Execute(
                    importSource,
                    outputPath,
                    packFileContainer,
                    gameType,
                    progress);
            }
            finally
            {
                _importerCompleted.TrySetResult(true);
            }
        }
    }

    private sealed class GalleryImporter :
        IImporterViewModel,
        IViewProvider<RmvToGltfImporterView>
    {
        public string DisplayName => "glTF → RMV2";
        public string OutputExtension => ".rigid_model_v2";
        public string[] InputExtensions => [".gltf", ".glb"];
        public string NewSkeletonName { get; set; } =
            "ExternalWorkflowArmature";
        public bool ImportMeshes { get; set; } = true;
        public bool ImportMaterials { get; set; } = true;
        public bool ConvertFromBlenderMaterialMap { get; set; } = true;
        public bool ConvertNormalTextureToOrange { get; set; } = true;
        public bool ImportAnimations { get; set; } = true;
        public bool AutoScaleHumanoid { get; set; } = true;
        public IReadOnlyList<KeyValuePair<string, GltfSourceForwardDirection>>
            SourceForwardDirections { get; } =
            CreateSourceForwardDirections();
        public GltfSourceForwardDirection SourceForwardDirection { get; set; } =
            GltfSourceForwardDirection.PositiveZ;
        public bool AutoDetectAnimationKeysPerSecond { get; set; } = true;
        public bool CanEditAnimationKeysPerSecond =>
            ImportAnimations && !AutoDetectAnimationKeysPerSecond;
        public float AnimationKeysPerSecond { get; set; } = 30;
        public ImportResult Execute(
            PackFile exportSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType)
        {
            return ImportResult.Success([]);
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
        public object? AutoScaleHumanoid { get; set; }
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
        public object? NewSkeletonName { get; set; }
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
        public object? SourceForwardDirection { get; set; }
        public object? SourceForwardDirections { get; set; }
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
