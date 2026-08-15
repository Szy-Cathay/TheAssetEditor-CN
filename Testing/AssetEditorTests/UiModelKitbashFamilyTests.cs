using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using CommonControls.FilterDialog;
using CommonControls.Editors.BoneMapping.View;
using Editors.KitbasherEditor.ChildEditors.PhotoStudio;
using KitbasherEditor.Views.EditorViews;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Ui.Common.ValueConverters;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class UiModelKitbashFamilyTests
{
    private static readonly string[] ProductXamlPaths =
    [
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/BmiEditor/BmiView.xaml",
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/MeshFitter/MeshFitterWindow.xaml",
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/PhotoStudio/PhotoStudioWindow.xaml",
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/PinTool/Presentation/PinToolWindow.xaml",
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/ReRiggingTool/ReRiggingWindow.xaml",
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/SaveDialog/SaveDialogWindow.xaml",
        "Editors/Kitbashing/KitbasherEditor/ChildEditors/VertexDebugger/VertexDebuggerWindow.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/Animation/AnimationView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/KitbasherView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/MenuBarViews/MenuBarView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneExplorer/SceneExplorerView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/GroupNode/GroupView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MainEditableNode/MainEditableNodeView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Animation/AnimationView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Geometry/MeshView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/AdvancedRmvMaterial/AdvancedRmvMaterialView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/Blood/BloodView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/Emissive/EmissiveView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/MetalRough/MetalRoughView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/ModelMaterialResources.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/ModelMaterialView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/SpecGloss/SpecGlossView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.Material/Tint/TintView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/Mesh.WeighterMaterial/WeightedMaterialView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/MeshNode/MeshEditorView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/Nodes/SkeletonNode/SkeletonView.xaml",
        "Editors/Kitbashing/KitbasherEditor/Core/SceneNodeEditor/SceneNodeEditorView.xaml",
        "Editors/Kitbashing/KitbasherEditor/UiCommands/BlenderShortcutsHelpWindow.xaml",
        "Editors/SkeletonEditor/Editor.VisualSkeletonEditor/SkeletonEditor/EditorView.xaml",
        "GameWorld/View3D/Utility/UserInterface/ShaderTextureView.xaml",
    ];

    private static readonly Regex LegacyThemeResource = new(
        @"\{DynamicResource\s+(?:ABrush\.|ToolBarTrayBackground|GroupBox\.Header\.Static\.Background|TreeViewItem\.Selected\.Background|TreeView\.Static\.Background|\{x:Static\s+SystemColors\.)",
        RegexOptions.CultureInvariant);

    private static readonly Regex HardcodedThemeColor = new(
        "(?:Background|Foreground|BorderBrush|Fill|Stroke)\\s*=\\s*\"(?:#|White\"|Black\"|Gray\"|Red\")",
        RegexOptions.CultureInvariant);

    [Test]
    public void ModelKitbashFamily_UsesSemanticThemeAndTypographyResources()
    {
        var sources = ReadProductSources();
        var combined = string.Join(Environment.NewLine, sources);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sources.Count, Is.EqualTo(30));
            NUnitAssert.That(combined, Does.Contain("AeBrush."));
            NUnitAssert.That(combined, Does.Contain("AppFontFamily"));
            NUnitAssert.That(combined, Does.Contain("AppFontWeight"));
            NUnitAssert.That(LegacyThemeResource.IsMatch(combined), Is.False);
            NUnitAssert.That(HardcodedThemeColor.IsMatch(combined), Is.False);
        });
    }

    [Test]
    public void ModelKitbashFamily_UsesSharedInteractiveControlFamilies()
    {
        var combined = string.Join(
            Environment.NewLine,
            ReadProductSources().Append(ReadKitbashStyleSource()));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Quiet"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Danger"));
            NUnitAssert.That(combined, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(combined, Does.Contain("AeInput.ComboBox"));
            NUnitAssert.That(combined, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(combined, Does.Contain("AeTree.View"));
            NUnitAssert.That(combined, Does.Contain("AeList.View"));
            NUnitAssert.That(combined, Does.Contain("AeTable.Grid"));
            NUnitAssert.That(combined, Does.Contain("AeMenu.Context"));
        });
    }

    [Test]
    public void ModelKitbashFamily_PreservesEditingAndViewportContracts()
    {
        var combined = string.Join(Environment.NewLine, ReadProductSources());

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("TreeViewItem.Drop"));
            NUnitAssert.That(combined, Does.Contain("SelectionManager"));
            NUnitAssert.That(combined, Does.Contain("NodeContextMenu"));
            NUnitAssert.That(combined, Does.Contain("DataGridNumericColumn"));
            NUnitAssert.That(combined, Does.Contain("TextBoxInputBehavior"));
            NUnitAssert.That(combined, Does.Contain("ViewTemplateDataSelector"));
            NUnitAssert.That(combined, Does.Contain("HandlePreviewTextureCommand"));
        });
    }

    [Test]
    public void SkeletonEditor_UsesFlatAlignedPropertySections()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "SkeletonEditor",
            "Editor.VisualSkeletonEditor",
            "SkeletonEditor",
            "EditorView.xaml");
        var document = XDocument.Load(path);
        var skeletonPath = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBox" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Text" &&
                attribute.Value.Contains(
                    "SkeletonName",
                    StringComparison.Ordinal) &&
                !attribute.Value.Contains(
                    "SourceSkeletonName",
                    StringComparison.Ordinal)));
        var referenceMeshPath = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBox" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Text" &&
                attribute.Value.Contains(
                    "RefMeshName",
                    StringComparison.Ordinal)));
        var sectionTitles = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style" &&
                    attribute.Value.Contains(
                        "AeText.SectionTitle",
                        StringComparison.Ordinal)))
            .ToArray();
        var treePanel = document.Descendants().Single(element =>
            element.Name.LocalName == "Border" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Style" &&
                attribute.Value.Contains(
                    "AeSurface.Panel",
                    StringComparison.Ordinal)) &&
            element.Descendants().Any(descendant =>
                descendant.Name.LocalName == "TreeView"));
        var loadButtons = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "Button" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Content" &&
                    attribute.Value.Contains(
                        "General.Load",
                        StringComparison.Ordinal)))
            .ToArray();
        var resourceGrid = skeletonPath.Parent!;
        var visibilityToggles = resourceGrid.Elements()
            .Where(element =>
                element.Name.LocalName == "ToggleButton" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style" &&
                    attribute.Value.Contains(
                        "AeButton.VisibilityToggle",
                        StringComparison.Ordinal)))
            .ToArray();
        var visibilityBindings = visibilityToggles
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName == "IsChecked")
            .Select(attribute => attribute.Value)
            .ToArray();
        var resourceColumnWidths = resourceGrid.Elements()
            .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .Select(element => element.Attribute("Width")?.Value)
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                document.Descendants().Any(element =>
                    element.Name.LocalName == "GroupBox"),
                Is.False);
            NUnitAssert.That(sectionTitles, Has.Length.EqualTo(3));
            NUnitAssert.That(
                new[] { skeletonPath, referenceMeshPath }.All(element =>
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "IsReadOnly" &&
                        attribute.Value.Equals(
                            "True",
                            StringComparison.OrdinalIgnoreCase))),
                Is.True);
            NUnitAssert.That(treePanel, Is.Not.Null);
            NUnitAssert.That(loadButtons, Has.Length.EqualTo(2));
            NUnitAssert.That(
                loadButtons.All(button =>
                    button.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Width" &&
                        attribute.Value == "72")),
                Is.True);
            NUnitAssert.That(
                resourceGrid.Elements().Any(element =>
                    element.Name.LocalName == "CheckBox"),
                Is.False);
            NUnitAssert.That(visibilityToggles, Has.Length.EqualTo(2));
            NUnitAssert.That(
                visibilityBindings.Any(binding => binding.Contains(
                    "ShowSkeleton",
                    StringComparison.Ordinal)),
                Is.True);
            NUnitAssert.That(
                visibilityBindings.Any(binding => binding.Contains(
                    "ShowRefMesh",
                    StringComparison.Ordinal)),
                Is.True);
            NUnitAssert.That(
                resourceColumnWidths,
                Is.EqualTo(new[] { "120", "*", "72", "Auto" }));
        });
    }

    [Test]
    [NonParallelizable]
    public void PhotoStudio_UsesTheSharedWindowShellWithoutPrivateChrome()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        using var services = new ServiceCollection()
            .AddSingleton(localizationManager)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                using var window = new PhotoStudioWindow(null!)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var source = File.ReadAllText(Path.Combine(
                        FindSolutionRoot(),
                        "Editors",
                        "Kitbashing",
                        "KitbasherEditor",
                        "ChildEditors",
                        "PhotoStudio",
                        "PhotoStudioWindow.xaml.cs"));

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            window.SizeToContent,
                            Is.EqualTo(SizeToContent.Height));
                        NUnitAssert.That(window.Width, Is.EqualTo(474));
                        NUnitAssert.That(source, Does.Not.Contain("DllImport"));
                        NUnitAssert.That(
                            source,
                            Does.Not.Contain("OnSourceInitialized"));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    [NonParallelizable]
    public void BoneMappingActions_UseTwoReadableRows()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        using var services = new ServiceCollection()
            .AddSingleton(localizationManager)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new BoneMappingView();
                var window = new Window
                {
                    Content = view,
                    Width = 1000,
                    Height = 900,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var expectedLabels = new[]
                    {
                        "BoneMapping.AutoMapByName",
                        "BoneMapping.AutoMapByHierarchy",
                        "BoneMapping.DeleteSelf",
                        "BoneMapping.DeleteSelfAndChildren",
                        "BoneMapping.CopyToAllChildren",
                    }.Select(localizationManager.Get).ToHashSet();
                    var actions = FindVisualDescendants<Button>(view)
                        .Where(button =>
                            button.Content is string label &&
                            expectedLabels.Contains(label))
                        .ToArray();
                    var rowOffsets = actions
                        .Select(button => Math.Round(
                            button.TranslatePoint(new Point(), view).Y,
                            2))
                        .Distinct()
                        .ToArray();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(actions, Has.Length.EqualTo(5));
                        NUnitAssert.That(rowOffsets, Has.Length.EqualTo(2));
                        NUnitAssert.That(
                            actions.Select(button => button.ActualWidth),
                            Has.All.GreaterThanOrEqualTo(150));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void ModelKitbashFamily_UsesCompactThemeNativeChrome()
    {
        var styles = ReadKitbashStyleSource();
        var root = FindSolutionRoot();
        var kitbasherView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "KitbasherView.xaml"));
        var menuBarView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "MenuBarViews",
            "MenuBarView.xaml"));
        var sceneNodeEditor = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "SceneNodeEditorView.xaml"));
        var combinedViews = string.Join(
            Environment.NewLine,
            kitbasherView,
            menuBarView);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"Kitbash.ToolRadioButton\""));
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"Kitbash.Expander\""));
            NUnitAssert.That(styles, Does.Not.Contain("<Ellipse"));
            NUnitAssert.That(
                styles,
                Does.Contain(
                    "TargetType=\"{x:Type ToolBar}\" BasedOn=\"{StaticResource {x:Type ToolBar}}\""));
            NUnitAssert.That(
                combinedViews,
                Does.Not.Contain(
                    "Style=\"{StaticResource {x:Type ToggleButton}}\""));
            NUnitAssert.That(
                menuBarView,
                Does.Contain(
                    "Style=\"{StaticResource AeButton.DropdownArrow}\""));
            NUnitAssert.That(menuBarView, Does.Not.Contain("Content=\"▾\""));
            NUnitAssert.That(
                kitbasherView,
                Does.Contain("<ColumnDefinition Width=\"34\"/>"));
            NUnitAssert.That(
                combinedViews,
                Does.Contain("Height=\"20\" Width=\"20\""));
            NUnitAssert.That(
                sceneNodeEditor,
                Does.Contain(
                    "Background=\"{DynamicResource AeBrush.Surface1}\""));
        });
    }

    [Test]
    public void Kitbash_UsesClearVisibilityChecksAndCompactPropertyChrome()
    {
        var root = FindSolutionRoot();
        var styles = ReadKitbashStyleSource();
        var sceneExplorer = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneExplorer",
            "SceneExplorerView.xaml"));
        var iconConverter = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "ValueConverters",
            "SceneNodeToIconKindConverter.cs"));
        var sceneNodeEditor = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "SceneNodeEditorView.xaml"));
        var propertyViews = new[]
        {
            "Nodes/SkeletonNode/SkeletonView.xaml",
            "Nodes/GroupNode/GroupView.xaml",
            "Nodes/MainEditableNode/MainEditableNodeView.xaml",
        }.Select(path => File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            path.Replace('/', Path.DirectorySeparatorChar))));
        var propertySource = string.Join(Environment.NewLine, propertyViews);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"Kitbash.VisibilityToggle\""));
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"Kitbash.SceneTreeItem\""));
            NUnitAssert.That(styles, Does.Contain("Margin=\"16,0,0,0\""));
            NUnitAssert.That(
                styles,
                Does.Contain(
                    "Property=\"FocusVisualStyle\" Value=\"{StaticResource AeFocus.Keyboard}\""));
            NUnitAssert.That(styles, Does.Not.Contain("IsKeyboardFocused"));
            NUnitAssert.That(styles, Does.Not.Contain("IsKeyboardFocusWithin"));
            NUnitAssert.That(styles, Does.Contain("x:Name=\"CheckMark\""));
            NUnitAssert.That(
                styles,
                Does.Contain(
                    "Setter TargetName=\"CheckMark\" Property=\"Opacity\" Value=\"1\""));
            NUnitAssert.That(
                styles,
                Does.Contain(
                    "Stroke=\"{DynamicResource AeBrush.Accent}\""));
            NUnitAssert.That(styles, Does.Contain("x:Key=\"Kitbash.PropertyTitle\""));
            NUnitAssert.That(styles, Does.Not.Contain("<Ellipse"));
            NUnitAssert.That(
                sceneExplorer,
                Does.Contain("Style=\"{StaticResource Kitbash.VisibilityToggle}\""));
            NUnitAssert.That(
                sceneExplorer,
                Does.Contain(
                    "BasedOn=\"{StaticResource Kitbash.SceneTreeItem}\""));
            NUnitAssert.That(styles, Does.Contain("x:Key=\"Kitbash.SceneIcon\""));
            NUnitAssert.That(styles, Does.Contain("x:Key=\"Kitbash.SceneLockIcon\""));
            NUnitAssert.That(
                styles,
                Does.Contain("<Trigger Property=\"Content\""));
            NUnitAssert.That(
                styles,
                Does.Contain("Stroke=\"{DynamicResource AeBrush.TextSecondary}\""));
            NUnitAssert.That(
                styles,
                Does.Contain("Fill=\"{DynamicResource AeBrush.Accent}\""));
            NUnitAssert.That(sceneExplorer, Does.Contain("x:Name=\"NodeStatus\""));
            NUnitAssert.That(sceneExplorer, Does.Contain("x:Name=\"LockState\""));
            NUnitAssert.That(
                sceneExplorer,
                Does.Match("x:Name=\"NodeStatus\"[\\s\\S]*?Orientation=\"Horizontal\""));
            NUnitAssert.That(sceneExplorer, Does.Not.Contain("Grid.Column=\"3\""));
            NUnitAssert.That(sceneExplorer, Does.Not.Contain("ImageBrush"));
            NUnitAssert.That(iconConverter, Does.Contain("SceneNodeIconKind.Lod"));
            NUnitAssert.That(iconConverter, Does.Contain("SceneNodeIconKind.Mesh"));
            NUnitAssert.That(iconConverter, Does.Not.Contain("IconLibrary"));
            NUnitAssert.That(sceneExplorer, Does.Contain("Content.IsVisible"));
            NUnitAssert.That(propertySource, Does.Not.Contain("FontSize=\"20\""));
            NUnitAssert.That(
                propertySource,
                Does.Contain("Style=\"{DynamicResource Kitbash.PropertyTitle}\""));
            NUnitAssert.That(
                sceneNodeEditor,
                Does.Contain("HorizontalContentAlignment=\"Stretch\""));
            NUnitAssert.That(
                sceneNodeEditor,
                Does.Contain("Background=\"{DynamicResource AeBrush.Surface1}\""));
        });
    }

    [Test]
    public void KitbashAnimationBar_UsesTheSharedCompactEditorLanguage()
    {
        var root = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "Animation",
            "AnimationView.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                source,
                Does.Contain("Common/Styles/EditorWorkspaceStyles.xaml"));
            NUnitAssert.That(source, Does.Contain("AeInput.Switch"));
            NUnitAssert.That(source, Does.Contain("AeButton.Icon"));
            NUnitAssert.That(source, Does.Contain("AeEditor.PlaybackToggle"));
            NUnitAssert.That(source, Does.Contain("IsChecked=\"{Binding IsPlaying"));
            NUnitAssert.That(source, Does.Contain("AeBrush.Surface2"));
            NUnitAssert.That(source, Does.Contain("<Path"));
            NUnitAssert.That(source, Does.Not.Contain("<GroupBox"));
            NUnitAssert.That(source, Does.Not.Contain("FontSize=\"20\""));
            NUnitAssert.That(source, Does.Not.Contain("⏪"));
        });
    }

    [Test]
    [NonParallelizable]
    public void KitbashAnimationBar_LoadsWithReadOnlyDurationAndKeepsSelectors()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        using var services = new ServiceCollection()
            .AddSingleton(localizationManager)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                const string converterKey = "BoolToCollapsedConverter";
                var resources = Application.Current.Resources;
                var hadConverter = resources.Contains(converterKey);
                var previousConverter = hadConverter
                    ? resources[converterKey]
                    : null;
                resources[converterKey] = new BoolToVisibilityConverter
                {
                    TrueValue = Visibility.Visible,
                    FalseValue = Visibility.Collapsed,
                };
                try
                {
                    var view = new AnimationPlayerView
                    {
                        DataContext = new KitbashAnimationPlayerProbe(),
                    };
                    var window = new Window
                    {
                        Content = view,
                        Width = 1000,
                        Height = 500,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                    };
                    try
                    {
                        window.Show();
                        window.UpdateLayout();

                        var timeline = FindVisualDescendants<Slider>(view)
                            .Single();
                        var timeText = FindVisualDescendants<TextBlock>(view)
                            .Select(textBlock => textBlock.Text)
                            .First(text => text.Contains(
                                localizationManager.Get(
                                    "SharedAnimPlayer.Seconds"),
                                StringComparison.Ordinal));
                        NUnitAssert.Multiple(() =>
                        {
                            NUnitAssert.That(timeline.Maximum, Is.EqualTo(2.3));
                            NUnitAssert.That(timeText, Does.Contain("2.30"));
                            NUnitAssert.That(
                                FindLogicalDescendants<CollapsableFilterControl>(
                                    view).Count(),
                                Is.EqualTo(2));
                        });
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                finally
                {
                    if (hadConverter)
                    {
                        resources[converterKey] = previousConverter!;
                    }
                    else
                    {
                        resources.Remove(converterKey);
                    }
                }
            });
    }

    [Test]
    [NonParallelizable]
    public void KitbashAnimationSelectors_ShowAvailableItemsWhenExpandedAndBrowsed()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        using var services = new ServiceCollection()
            .AddSingleton(localizationManager)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                const string converterKey = "BoolToCollapsedConverter";
                var resources = Application.Current.Resources;
                var hadConverter = resources.Contains(converterKey);
                var previousConverter = hadConverter
                    ? resources[converterKey]
                    : null;
                resources[converterKey] = new BoolToVisibilityConverter
                {
                    TrueValue = Visibility.Visible,
                    FalseValue = Visibility.Collapsed,
                };
                try
                {
                    var view = new AnimationPlayerView
                    {
                        DataContext = new KitbashAnimationPlayerProbe
                        {
                            SkeletonList = ["animations\\skeletons\\humanoid17.anim"],
                            AnimationsForCurrentSkeleton = ["animations\\battle\\test.anim"],
                        },
                    };
                    var window = new Window
                    {
                        Content = view,
                        Width = 1000,
                        Height = 700,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                    };
                    try
                    {
                        window.Show();
                        window.UpdateLayout();

                        var selectorArea = FindVisualDescendants<Expander>(view)
                            .Single();
                        selectorArea.IsExpanded = true;
                        window.UpdateLayout();

                        var selectors = FindVisualDescendants<CollapsableFilterControl>(
                                view)
                            .ToArray();
                        NUnitAssert.That(selectors, Has.Length.EqualTo(2));

                        var aeListItemStyle = (Style)Application.Current
                            .FindResource("AeList.Item");
                        var expectedItems = new[]
                        {
                            "animations\\skeletons\\humanoid17.anim",
                            "animations\\battle\\test.anim",
                        };
                        for (var index = 0; index < selectors.Length; index++)
                        {
                            var selector = selectors[index];
                            var browseButton = FindVisualDescendants<Button>(selector)
                                .First(button => Equals(
                                    button.Content,
                                    localizationManager.Get("General.Browse")));
                            browseButton.RaiseEvent(new RoutedEventArgs(
                                Button.ClickEvent));
                            window.UpdateLayout();

                            var filter = FindVisualDescendants<FilterUserControl>(
                                    selector)
                                .Single();
                            var results = FindVisualDescendants<ListView>(filter)
                                .Single();
                            var itemContainer = (ListViewItem?)results
                                .ItemContainerGenerator.ContainerFromIndex(0);
                            var itemStyleBasedOn = itemContainer?.Style.BasedOn;
                            var visibleItemText = itemContainer is null
                                ? Array.Empty<string>()
                                : FindVisualDescendants<TextBlock>(itemContainer)
                                    .Select(textBlock => textBlock.Text)
                                    .Where(text => !string.IsNullOrWhiteSpace(text))
                                    .ToArray();
                            NUnitAssert.Multiple(() =>
                            {
                                NUnitAssert.That(filter.Visibility, Is.EqualTo(
                                    Visibility.Visible));
                                NUnitAssert.That(results.Items.Count, Is.EqualTo(1));
                                NUnitAssert.That(results.ActualHeight, Is.GreaterThan(0));
                                NUnitAssert.That(itemContainer, Is.Not.Null);
                                NUnitAssert.That(
                                    itemStyleBasedOn,
                                    Is.SameAs(aeListItemStyle));
                                NUnitAssert.That(
                                    visibleItemText,
                                    Does.Contain(expectedItems[index]));
                            });
                        }
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                finally
                {
                    if (hadConverter)
                    {
                        resources[converterKey] = previousConverter!;
                    }
                    else
                    {
                        resources.Remove(converterKey);
                    }
                }
            });
    }

    [Test]
    public void DynamicContextMenuSeparators_UseCompactRows()
    {
        var root = FindSolutionRoot();
        var sources = new[]
        {
            "Editors/Kitbashing/KitbasherEditor/Core/SceneExplorer/SceneExplorerView.xaml",
            "Shared/SharedUI/BaseDialogs/PackFileTree/PackFileBrowserView.xaml",
        }.Select(path => File.ReadAllText(Path.Combine(
            root,
            path.Replace('/', Path.DirectorySeparatorChar))));

        NUnitAssert.Multiple(() =>
        {
            foreach (var source in sources)
            {
                NUnitAssert.That(
                    source,
                    Does.Contain("Property=\"MinHeight\" Value=\"0\""));
                NUnitAssert.That(
                    source,
                    Does.Contain("Property=\"Height\" Value=\"5\""));
                NUnitAssert.That(
                    source,
                    Does.Contain("Property=\"Padding\" Value=\"0\""));
            }
        });
    }

    [Test]
    public void MainEditableSkeletonSelector_KeepsLabelOnTheInputRowWhenExpanded()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                const string converterKey = "BoolToCollapsedConverter";
                var resources = Application.Current.Resources;
                var hadConverter = resources.Contains(converterKey);
                var previousConverter = hadConverter
                    ? resources[converterKey]
                    : null;
                resources[converterKey] = new BoolToVisibilityConverter
                {
                    TrueValue = Visibility.Visible,
                    FalseValue = Visibility.Collapsed,
                };
                try
                {
                    var view = new MainEditableNodeView();
                    var window = new Window
                    {
                        Content = view,
                        Width = 640,
                        Height = 720,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                    };
                    try
                    {
                        window.Show();
                        window.UpdateLayout();

                        var selector = FindVisualDescendants<
                                CollapsableFilterControl>(view)
                            .Single();
                        var browseButton = FindVisualDescendants<Button>(selector)
                            .First(button => button.Name == "BrowseButton");
                        browseButton.RaiseEvent(new RoutedEventArgs(
                            Button.ClickEvent));
                        window.UpdateLayout();

                        var visibleLabels = FindVisualDescendants<Label>(view)
                            .Where(label =>
                                label.Visibility == Visibility.Visible &&
                                Equals(label.Content, selector.LabelText))
                            .ToArray();
                        var selectorLabel = FindVisualDescendants<Label>(selector)
                            .Single(label =>
                                Equals(label.Content, selector.LabelText));
                        var selectedFileName = FindVisualDescendants<TextBox>(selector)
                            .Single(textBox => textBox.Name == "SelectedFileName");
                        var labelTop = selectorLabel.TransformToAncestor(selector)
                            .Transform(new Point()).Y;
                        var inputTop = selectedFileName.TransformToAncestor(selector)
                            .Transform(new Point()).Y;

                        NUnitAssert.Multiple(() =>
                        {
                            NUnitAssert.That(selector.ShowLabel, Is.True);
                            NUnitAssert.That(selector.LabelTotalWidth,
                                Is.EqualTo(150));
                            NUnitAssert.That(visibleLabels,
                                Has.Length.EqualTo(1));
                            NUnitAssert.That(selectorLabel.Visibility,
                                Is.EqualTo(Visibility.Visible));
                            NUnitAssert.That(labelTop,
                                Is.EqualTo(inputTop).Within(1));
                        });
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                finally
                {
                    if (hadConverter)
                        resources[converterKey] = previousConverter!;
                    else
                        resources.Remove(converterKey);
                }
            });
    }

    private static IReadOnlyList<string> ReadProductSources()
    {
        var root = FindSolutionRoot();
        return ProductXamlPaths
            .Select(path => File.ReadAllText(Path.Combine(
                root,
                path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
    }

    private static string ReadKitbashStyleSource() => File.ReadAllText(
        Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "KitbashUiStyles.xaml"));

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

    private static IEnumerable<T> FindLogicalDescendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent)
                     .OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class KitbashAnimationPlayerProbe
    {
        public bool IsEnabled { get; set; } = true;
        public VisibilityValue AnimationControllerVisability { get; } = new();
        public int CurrentFrame { get; set; } = 19;
        public int MaxFrames { get; set; } = 47;
        public bool IsPlaying { get; } = true;
        public double PlaybackPositionSeconds { get; set; } = 0.89;
        public double MaxTimeSeconds { get; } = 2.3;
        public string HeaderText { get; set; } = "测试动画";
        public object? SelectedAnimation { get; set; }
        public object? SelectedSkeleton { get; set; }
        public object[] SkeletonList { get; set; } = [];
        public object[] AnimationsForCurrentSkeleton { get; set; } = [];
    }

    private sealed class VisibilityValue
    {
        public Visibility Value { get; set; } = Visibility.Visible;
    }

    private static string FindSolutionRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(
            "AE_SOLUTION_ROOT");
        foreach (var startingPath in new[]
                 {
                     configuredRoot,
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            DirectoryInfo? directory = new(startingPath!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "AssetEditor.CN.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }

}
