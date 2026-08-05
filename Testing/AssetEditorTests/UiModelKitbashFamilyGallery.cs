using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using Editor.VisualSkeletonEditor.SkeletonEditor;
using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using Editors.KitbasherEditor.ChildEditors.PhotoStudio;
using Editors.KitbasherEditor.ChildEditors.VertexDebugger;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels.PinTool;
using Editors.KitbasherEditor.ViewModels.SaveDialog;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.SpecGloss;
using GameWorld.Core.Utility.UserInterface;
using KitbasherEditor.Views;
using KitbasherEditor.Views.EditorViews;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common.DataTemplates;
using Shared.Ui.Common.ValueConverters;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiModelKitbashFamilyGallery
{
    private static readonly string[] Variants =
    [
        "kitbasher-workspace",
        "animation-player",
        "menu-bar",
        "scene-explorer",
        "scene-item-editor",
        "bmi-editor",
        "group-node",
        "main-editable-node",
        "mesh-editor",
        "mesh-animation",
        "mesh-geometry",
        "model-material",
        "advanced-rmv-material",
        "blood-material",
        "emissive-material",
        "metal-rough-material",
        "spec-gloss-material",
        "tint-material",
        "weighted-material",
        "skeleton-node",
        "skeleton-editor",
        "mesh-fitter",
        "rerigging",
        "photo-studio",
        "pin-tool",
        "save-dialog",
        "vertex-debugger",
        "shader-texture",
        "shortcut-help",
    ];

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCaseSource(nameof(Cases))]
    public void ModelKitbashFamily_RendersRequiredThemeAndState(
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
            Application.Current.Resources["BoolToCollapsedConverter"] =
                new BoolToVisibilityConverter
                {
                    TrueValue = Visibility.Visible,
                    FalseValue = Visibility.Collapsed,
                };
            Application.Current.Resources["ViewTemplateDataSelector"] =
                new ViewTemplateDataSelector();
            Application.Current.Resources["NullToVisibilityConverter"] =
                new NullVisibilityConverter();
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

    private static Window CreateWindow(string variant) => variant switch
    {
        "kitbasher-workspace" => Host(CreateKitbasherWorkspace(), 1280, 760),
        "animation-player" => Host(
            new AnimationPlayerView { DataContext = new GalleryModel() },
            1100,
            260),
        "menu-bar" => Host(
            new MenuBarView { DataContext = CreateMenuBarModel() },
            1100,
            180),
        "scene-explorer" => Host(
            new SceneExplorerView { DataContext = new GalleryModel() },
            520,
            680),
        "scene-item-editor" => Host(
            new SceneItemEditorView
            {
                DataContext = new GalleryModel
                {
                    EmptyStateText = "选择场景节点以编辑属性",
                },
            },
            620,
            680),
        "bmi-editor" => Host(
            new BmiView { DataContext = CreateBmiModel() },
            760,
            620),
        "group-node" => Host(
            new KitbasherEditor.Views.EditorViews.GroupView
            {
                DataContext = new GalleryModel { Name = "帝国将军组件组" },
            },
            620,
            420),
        "main-editable-node" => Host(
            new KitbasherEditor.Views.EditorViews.MainEditableNodeView
            {
                DataContext = CreateMaterialModel(),
            },
            640,
            720),
        "mesh-editor" => Host(
            new KitbasherEditor.Views.EditorViews.Rmv2.MeshEditorView
            {
                DataContext = CreateMaterialModel(),
            },
            640,
            720),
        "mesh-animation" => Host(
            new KitbasherEditor.Views.EditorViews.Rmv2.AnimationView
            {
                DataContext = CreateMaterialModel(),
            },
            640,
            720),
        "mesh-geometry" => Host(
            new KitbasherEditor.Views.EditorViews.Rmv2.MeshView
            {
                DataContext = CreateMaterialModel(),
            },
            640,
            720),
        "model-material" => Host(
            new Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes
                .MeshSubViews.WsModelMaterial.ModelMaterialView
                { DataContext = CreateMaterialModel() },
            640,
            720),
        "advanced-rmv-material" => Host(
            new Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes
                .MeshNode.Mesh.WsMaterial.DirtAndDecal
                .AdvancedRmvMaterialView
                { DataContext = CreateMaterialModel() },
            640,
            720),
        "blood-material" => Host(
            new Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes
                .MeshNode.Mesh.WsMaterial.Blood.BloodView
                { DataContext = CreateMaterialModel() },
            640,
            720),
        "emissive-material" => Host(
            new Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes
                .MeshNode.Mesh.WsMaterial.Emissive.EmissiveView
                { DataContext = CreateMaterialModel() },
            640,
            720),
        "metal-rough-material" => Host(
            new Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes
                .MeshNode.Mesh.WsMaterial.MetalRough.MetalRoughView
                { DataContext = CreateMaterialModel() },
            640,
            720),
        "spec-gloss-material" => Host(
            new SpecGlossView { DataContext = CreateMaterialModel() },
            640,
            760),
        "tint-material" => Host(
            new Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes
                .MeshNode.Mesh.WsMaterial.Tint.TintView
                { DataContext = CreateMaterialModel() },
            640,
            720),
        "weighted-material" => Host(
            new KitbasherEditor.Views.EditorViews.Rmv2.WeightedMaterialView
            {
                DataContext = CreateMaterialModel(),
            },
            640,
            720),
        "skeleton-node" => Host(
            new KitbasherEditor.Views.EditorViews.SkeletonView
            {
                DataContext = CreateSkeletonModel(),
            },
            640,
            720),
        "skeleton-editor" => Host(
            new EditorView { DataContext = CreateSkeletonModel() },
            760,
            780),
        "mesh-fitter" => CreateMeshFitterWindow(),
        "rerigging" => new ReRiggingWindow(
            new Editors.KitbasherEditor.ChildEditors.ReRiggingTool
                .ReRiggingViewModel(null!)),
        "photo-studio" => new PhotoStudioWindow(null!)
        {
            DataContext = CreatePhotoStudioModel(),
        },
        "pin-tool" => new PinToolWindow(null!)
        {
            DataContext = CreatePinToolModel(),
        },
        "save-dialog" => new SaveDialogWindow(null!)
        {
            DataContext = CreateSaveDialogModel(),
        },
        "vertex-debugger" => CreateVertexDebuggerWindow(),
        "shader-texture" => Host(
            new ShaderTextureView
            {
                DataContext = new GalleryModel
                {
                    Path = @"variantmeshes\\empire\\general_diffuse.dds",
                    ShouldRenderTexture = true,
                },
            },
            920,
            120),
        "shortcut-help" => new BlenderShortcutsHelpWindow(),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static KitbasherView CreateKitbasherWorkspace()
    {
        var viewport = new Border
        {
            Margin = new Thickness(8),
            Background = (Brush)Application.Current.FindResource(
                "AeBrush.Surface2"),
            BorderBrush = (Brush)Application.Current.FindResource(
                "AeBrush.Border"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "3D 模型视口 · 帝国将军",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.FindResource(
                    "AeBrush.TextMuted"),
            },
        };
        return new KitbasherView
        {
            DataContext = new GalleryModel
            {
                LeftColumnWidth = new GridLength(3, GridUnitType.Star),
                RightColumnWidth = new GridLength(1, GridUnitType.Star),
                Scene = viewport,
                MenuBar = new GalleryModel
                {
                    MenuItems = Array.Empty<object>(),
                    CustomButtons = Array.Empty<object>(),
                    SidebarButtons = Array.Empty<object>(),
                    TransformTool = new GalleryModel
                    {
                        IsVisible = new GalleryModel { Value = false },
                    },
                    ProportionalEditing = new GalleryModel
                    {
                        IsVisible = new GalleryModel { Value = false },
                    },
                },
                SceneExplorer = new GalleryModel(),
                SceneNodeEditor = new GalleryModel
                {
                    CurrentEditor = null,
                    EmptyStateText = "从场景树选择模型、网格或材质以编辑属性",
                },
                Animation = new GalleryModel(),
            },
        };
    }

    private static MeshFitterWindow CreateMeshFitterWindow()
    {
        var viewModel = new MeshFitterViewModel(null!, null!, null!);
        typeof(MeshFitterViewModel)
            .GetField("_isDisposed", BindingFlags.Instance |
                BindingFlags.NonPublic)!
            .SetValue(viewModel, true);
        return new MeshFitterWindow(viewModel);
    }

    private static GalleryModel CreateMenuBarModel() => new()
    {
        MenuItems = Array.Empty<object>(),
        CustomButtons = Array.Empty<object>(),
        ProportionalEditing = new GalleryModel
        {
            IsVisible = new GalleryModel { Value = true },
            IsEnabled = false,
            FalloffDistance = 1.25,
        },
    };

    private static VertexDebuggerWindow CreateVertexDebuggerWindow()
    {
        var viewModel = (VertexDebuggerViewModel)
            System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(VertexDebuggerViewModel));
        viewModel.VertexList = [];
        viewModel.DebugScale = new DoubleViewModel(0.03);
        return new VertexDebuggerWindow(viewModel, new GalleryWpfGame());
    }

    private static Window Host(
        FrameworkElement content,
        double width,
        double height)
    {
        var window = new Window
        {
            Content = content,
            Width = width,
            Height = height,
            Background = (Brush)Application.Current.FindResource(
                "AeBrush.Canvas"),
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/Editors.KitbasherEditor;component/KitbashUiStyles.xaml",
                UriKind.Relative),
        });
        return window;
    }

    private static void ConfigureWindow(Window window, string variant)
    {
        window.Title = variant;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.Manual;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;

        switch (variant)
        {
            case "mesh-fitter":
                window.Width = 1000;
                window.Height = 620;
                break;
            case "photo-studio":
                window.Width = 520;
                window.Height = 720;
                break;
            case "pin-tool":
                window.Width = 500;
                window.Height = 650;
                break;
            case "save-dialog":
                window.Width = 980;
                window.Height = 520;
                break;
            case "vertex-debugger":
                window.Width = 1040;
                window.Height = 720;
                break;
            case "shortcut-help":
                window.Width = 560;
                window.Height = 640;
                break;
        }
    }

    private static GalleryModel CreateBmiModel()
    {
        var child = new GalleryModel
        {
            BoneName = "bip_spine_01",
            IsChecked = true,
            IsUsedByCurrentModel = true,
            IsVisible = true,
            Children = Array.Empty<object>(),
        };
        return new GalleryModel
        {
            Bones = new[]
            {
                new GalleryModel
                {
                    BoneName = "root",
                    IsChecked = true,
                    IsUsedByCurrentModel = true,
                    IsVisible = true,
                    Children = new[] { child },
                },
            },
            SelectedBone = child,
            CheckButtonsEnabled = true,
            ScaleFactor = new GalleryModel { TextValue = "1.000" },
        };
    }

    private static GalleryModel CreateMaterialModel() => new()
    {
        Name = "empire_general_body",
        Alpha = 1.0,
        Roughness = 0.42,
        Metalness = 0.08,
        DiffuseTexture = new GalleryModel
        {
            Path = @"variantmeshes\\empire\\general_diffuse.dds",
            ShouldRenderTexture = true,
        },
        NormalTexture = new GalleryModel
        {
            Path = @"variantmeshes\\empire\\general_normal.dds",
            ShouldRenderTexture = true,
        },
    };

    private static GalleryModel CreateSkeletonModel()
    {
        var child = new GalleryModel
        {
            BoneName = "bip_head",
            BoneIndex = 12,
            Children = Array.Empty<object>(),
        };
        return new GalleryModel
        {
            ShowSkeleton = true,
            ShowRefMesh = true,
            SkeletonName = "empire_general_skeleton",
            RefMeshName = "empire_general_body",
            Bones = new[]
            {
                new GalleryModel
                {
                    BoneName = "root",
                    BoneIndex = 0,
                    Children = new[] { child },
                },
            },
            SelectedBone = child,
            SourceSkeletonName = "humanoid01",
            BoneVisualScale = "1.0",
            SelectedBoneName = "bip_head",
            BoneScale = "1.0",
            ShowBonesAsWorldTransform = false,
            IsTechSkeleton = false,
        };
    }

    private static GalleryModel CreatePhotoStudioModel() => new()
    {
        CameraPosition = new Vector3ViewModel(0, 1.5f, 4),
        CameraLookAt = new Vector3ViewModel(0, 1, 0),
        CameraYaw = 15f,
        CameraPitch = -8f,
        CameraZoom = 4.2f,
        LightIntensity = 1.25f,
        EnvLightRotationY = 25f,
        DirectLightRotationX = 35f,
        DirectLightRotationY = -20f,
        DoubleImageResolution = true,
    };

    private static GalleryModel CreatePinToolModel() => new()
    {
        SelectedRiggingMode = "Pin",
        AffectedMeshCollection = new[]
        {
            new GalleryModel { Name = "general_body_lod0" },
            new GalleryModel { Name = "general_cape_lod0" },
        },
        PinMode = new GalleryModel
        {
            Description = "general_body_lod0 · 顶点 842",
        },
        SkinWrapMode = new GalleryModel
        {
            Description = "尚未选择源网格",
        },
    };

    private static GalleryModel CreateSaveDialogModel()
    {
        var meshStrategies = new[]
        {
            new GalleryModel
            {
                DisplayName = "保留原始模型格式",
                Description = "按源模型格式保存",
            },
        };
        var wsStrategies = new[]
        {
            new GalleryModel
            {
                DisplayName = "生成 WSModel",
                Description = "同时写入材质引用",
            },
        };
        var lodStrategies = new[]
        {
            new GalleryModel
            {
                DisplayName = "自动生成 LOD",
                Description = "根据距离生成三级细节",
            },
        };
        return new GalleryModel
        {
            OutputPath = @"D:\\Modding\\output\\empire_general.rigid_model_v2",
            MeshStrategies = meshStrategies,
            SelectedMeshStrategy = meshStrategies[0],
            WsStrategies = wsStrategies,
            SelectedWsModelStrategy = wsStrategies[0],
            LodStrategies = lodStrategies,
            SelectedLodStrategy = lodStrategies[0],
            PossibleLodNumbers = new[] { 1, 2, 3, 4 },
            NumberOfLodsToGenerate = 3,
            OnlySaveVisible = true,
            LodNodes = new[]
            {
                new GalleryModel
                {
                    LodIndex = 0,
                    CameraDistance = 0d,
                    QualityLvl = 0,
                    LodReductionFactor = 1d,
                    OptimizeLod_Alpha = true,
                    OptimizeLod_Vertex = true,
                    PolygonCount = new GalleryModel { Value = 42816 },
                    MeshCount = new GalleryModel { Value = 6 },
                    TextureCount = new GalleryModel { Value = 14 },
                },
                new GalleryModel
                {
                    LodIndex = 1,
                    CameraDistance = 35d,
                    QualityLvl = 1,
                    LodReductionFactor = 0.55d,
                    OptimizeLod_Alpha = true,
                    OptimizeLod_Vertex = true,
                    PolygonCount = new GalleryModel { Value = 23548 },
                    MeshCount = new GalleryModel { Value = 6 },
                    TextureCount = new GalleryModel { Value = 14 },
                },
            },
        };
    }

    private static void AssertVisualContracts(Window window, string variant)
    {
        var buttons = FindVisualDescendants<Button>(window).ToArray();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(window.ActualWidth, Is.GreaterThan(300));
            NUnitAssert.That(window.ActualHeight, Is.GreaterThan(80));
            NUnitAssert.That(buttons, Has.All.Matches<Button>(button =>
                button.ActualHeight >= 0 && button.Style is not null));
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
            $"model-kitbash-{variant}-{theme}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

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

#pragma warning disable CA1812
    private sealed class GalleryModel
    {
        public object? AffectedMeshCollection { get; set; }
        public object? Animation { get; set; }
        public object? BoneIndex { get; set; }
        public object? BoneName { get; set; }
        public object? Bones { get; set; }
        public object? BoneScale { get; set; }
        public object? BoneVisualScale { get; set; }
        public object? CameraDistance { get; set; }
        public object? CameraLookAt { get; set; }
        public object? CameraPitch { get; set; }
        public object? CameraPosition { get; set; }
        public object? CameraYaw { get; set; }
        public object? CameraZoom { get; set; }
        public object? CheckButtonsEnabled { get; set; }
        public object? Children { get; set; }
        public object? ClearAffectedMeshCollectionCommand { get; set; } = GalleryCommand.Instance;
        public object? CurrentEditor { get; set; }
        public object? CustomButtons { get; set; }
        public object? DiffuseTexture { get; set; }
        public object? DirectLightRotationX { get; set; }
        public object? DirectLightRotationY { get; set; }
        public object? Description { get; set; }
        public object? DisplayName { get; set; }
        public object? DoubleImageResolution { get; set; }
        public object? EmptyStateText { get; set; }
        public object? EnvLightRotationY { get; set; }
        public object? HandleBrowseLocationCommand { get; set; } = GalleryCommand.Instance;
        public object? HandleBrowseTextureCommand { get; set; } = GalleryCommand.Instance;
        public object? HandleClearTextureCommand { get; set; } = GalleryCommand.Instance;
        public object? HandlePreviewTextureCommand { get; set; } = GalleryCommand.Instance;
        public object? IsChecked { get; set; }
        public object? IsEnabled { get; set; }
        public object? IsTechSkeleton { get; set; }
        public object? IsUsedByCurrentModel { get; set; }
        public object? IsVisible { get; set; }
        public object? LeftColumnWidth { get; set; }
        public object? LightIntensity { get; set; }
        public object? LodIndex { get; set; }
        public object? LodNodes { get; set; }
        public object? LodReductionFactor { get; set; }
        public object? LodStrategies { get; set; }
        public object? MenuBar { get; set; }
        public object? MenuItems { get; set; }
        public object? MeshCount { get; set; }
        public object? MeshStrategies { get; set; }
        public object? Name { get; set; }
        public object? NormalTexture { get; set; }
        public object? NumberOfLodsToGenerate { get; set; }
        public object? OnlySaveVisible { get; set; }
        public object? OptimizeLod_Alpha { get; set; }
        public object? OptimizeLod_Vertex { get; set; }
        public object? OutputPath { get; set; }
        public object? Path { get; set; }
        public object? PinMode { get; set; }
        public object? PolygonCount { get; set; }
        public object? PossibleLodNumbers { get; set; }
        public object? ProportionalEditing { get; set; }
        public object? FalloffDistance { get; set; }
        public object? QualityLvl { get; set; }
        public object? RefMeshName { get; set; }
        public object? RightColumnWidth { get; set; }
        public object? Roughness { get; set; }
        public object? ScaleFactor { get; set; }
        public object? Scene { get; set; }
        public object? SceneExplorer { get; set; }
        public object? SceneNodeEditor { get; set; }
        public object? SelectedBone { get; set; }
        public object? SelectedBoneName { get; set; }
        public object? SelectedLodStrategy { get; set; }
        public object? SelectedMeshStrategy { get; set; }
        public object? SelectedRiggingMode { get; set; }
        public object? SelectedWsModelStrategy { get; set; }
        public object? ShouldRenderTexture { get; set; }
        public object? ShowBonesAsWorldTransform { get; set; }
        public object? ShowRefMesh { get; set; }
        public object? ShowSkeleton { get; set; }
        public object? SidebarButtons { get; set; }
        public object? SkeletonName { get; set; }
        public object? SkinWrapMode { get; set; }
        public object? SourceSkeletonName { get; set; }
        public object? TextValue { get; set; }
        public object? TextureCount { get; set; }
        public object? TransformTool { get; set; }
        public object? Value { get; set; }
        public object? WsStrategies { get; set; }
        public object? Metalness { get; set; }
        public object? Alpha { get; set; }
    }
#pragma warning restore CA1812

    private sealed class GalleryWpfGame : IWpfGame
    {
        public ContentManager Content { get; set; } = null!;
        public GraphicsDevice GraphicsDevice => null!;

        public void ForceEnsureCreated()
        {
        }

        public FrameworkElement GetFocusElement() => new Border();

        public T AddComponent<T>(T comp) where T : IGameComponent => comp;

        public void RemoveComponent<T>(T comp) where T : IGameComponent
        {
        }
    }
}
