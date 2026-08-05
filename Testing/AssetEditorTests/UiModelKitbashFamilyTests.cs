using System.Text.RegularExpressions;
using NUnit.Framework;
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
            NUnitAssert.That(styles, Does.Contain("x:Name=\"CheckMark\""));
            NUnitAssert.That(styles, Does.Contain("x:Key=\"Kitbash.PropertyTitle\""));
            NUnitAssert.That(styles, Does.Not.Contain("<Ellipse"));
            NUnitAssert.That(
                sceneExplorer,
                Does.Contain("Style=\"{StaticResource Kitbash.VisibilityToggle}\""));
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

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
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

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }
}
