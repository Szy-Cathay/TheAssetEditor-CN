using System.Windows.Input;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.LoadAndSave;

[TestFixture]
[NonParallelizable]
internal class MenuBarIntegrationTests : LoadAndSaveBase
{
    private AssetEditorTestRunner _runner = null!;
    private KitbasherViewModel _editor = null!;
    private Rmv2MeshNode _meshNode = null!;
    private SelectionManager SelectionManager =>
        _runner.GetRequiredServiceInCurrentEditorScope<SelectionManager>();

    [OneTimeSetUp]
    public void CreateEditor()
    {
        (_runner, _editor) = CreateKitbashTool(
            TestFiles.RomePack_MeshDecal);
        _meshNode = SceneNodeHelper
            .GetChildrenOfType<Rmv2MeshNode>(
                _editor.SceneExplorer.SceneManager.RootNode)
            .First();
    }

    [Test]
    public void Scene_EnablesVertexSelectionEdgeGradient()
    {
        Assert.That(
            SelectionManager.VertexSelectionEdgeGradientEnabled,
            Is.True);
    }

    [Test]
    public void GizmoScaleCommands_MatchTheirNames()
    {
        var commandFactory =
            _runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
        var gizmoComponent =
            _runner.GetRequiredServiceInCurrentEditorScope<GizmoComponent>();
        var initialScale = gizmoComponent.Gizmo.ScaleModifier;

        commandFactory.Create<ScaleGizmoUpCommand>().Execute();
        var increasedScale = gizmoComponent.Gizmo.ScaleModifier;
        commandFactory.Create<ScaleGizmoDownCommand>().Execute();
        var restoredScale = gizmoComponent.Gizmo.ScaleModifier;

        Assert.Multiple(() =>
        {
            Assert.That(increasedScale, Is.EqualTo(initialScale + 0.5f));
            Assert.That(restoredScale, Is.EqualTo(initialScale));
        });
    }

    [Test]
    public void FaceMode_SplitButtonIsEnabledForSelectedFaces()
    {
        SelectionManager.SetState(new FaceSelectionState
        {
            RenderObject = _meshNode,
            SelectedFaces = [0]
        });
        var splitButton = _editor.MenuBar.CustomButtons.SingleOrDefault(button =>
            button.ShowRule == ButtonVisibilityRule.FaceMode &&
            button.Action.ToolTipAttribute.Value.StartsWith(
                "将网格拆分",
                StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(splitButton, Is.Not.Null);
            Assert.That(
                splitButton?.Action.IsActionEnabled.Value,
                Is.True);
        });
    }

    [Test]
    public void VisibleToolbar_DoesNotContainAdjacentSeparators()
    {
        var selectionStates = new ISelectionState[]
        {
            CreateObjectSelection(),
            new FaceSelectionState
            {
                RenderObject = _meshNode,
                SelectedFaces = [0]
            },
            new VertexSelectionState(_meshNode, 0)
            {
                SelectedVertices = [0]
            },
            new EdgeSelectionState
            {
                RenderObject = _meshNode,
                SelectedEdges = [(0, 1)]
            }
        };

        foreach (var selectionState in selectionStates)
        {
            SelectionManager.SetState(selectionState);
            var visibleButtons = _editor.MenuBar.CustomButtons
                .Where(button => button.IsVisible.Value)
                .ToList();

            Assert.That(
                visibleButtons
                    .Zip(visibleButtons.Skip(1))
                    .Any(pair =>
                        pair.First.IsSeperator &&
                        pair.Second.IsSeperator),
                Is.False,
                selectionState.Mode.ToString());
        }
    }

    [Test]
    public void ReleaseMenu_DoesNotExposeDebugActions()
    {
        var menuNames = _editor.MenuBar.MenuItems
            .Select(item => item.NameAttribute.Value)
            .ToList();

#if DEBUG
        Assert.That(menuNames, Does.Contain("调试"));
#else
        Assert.That(menuNames, Does.Not.Contain("调试"));
#endif
    }

    [Test]
    public void ReleaseMenu_ContainsPrimitiveCreationSubmenu()
    {
        var toolsMenu = _editor.MenuBar.MenuItems.Single(
            item => item.NameAttribute.Value == "工具");
        var primitiveMenu = toolsMenu.Children.Single(
            item => item.NameAttribute.Value == "创建基础几何体");

        Assert.That(
            primitiveMenu.Children.Select(item => item.NameAttribute.Value),
            Is.EqualTo(new[] { "立方体", "平面", "球体" }));
    }

    [Test]
    public void PhotoStudio_IsAvailableFromChineseMenuAndToolbar()
    {
        var renderingMenu = _editor.MenuBar.MenuItems.Single(
            item => item.NameAttribute.Value == "渲染");
        var photoStudioMenuItems = renderingMenu.Children.Count(
            item => item.NameAttribute.Value.StartsWith(
                "照片工作室",
                StringComparison.Ordinal));
        var photoStudioButtons = _editor.MenuBar.CustomButtons.Count(
            button =>
                button.Action?.ToolTipAttribute?.Value?.StartsWith(
                    "打开照片工作室",
                    StringComparison.Ordinal) == true);
        Assert.Multiple(() =>
        {
            Assert.That(photoStudioMenuItems, Is.EqualTo(1));
            Assert.That(photoStudioButtons, Is.EqualTo(1));
        });
    }

    [Test]
    public void RenderingMenu_DoesNotExposeLegacyRenderSettingsWindow()
    {
        var renderingMenu = _editor.MenuBar.MenuItems.Single(
            item => item.NameAttribute.Value == "渲染");

        Assert.That(
            renderingMenu.Children.Select(
                item => item.NameAttribute.Value),
            Has.None.EqualTo("打开渲染设置"));
    }

    [Test]
    public void CreateBoxUiCommand_CreatesSelectableUndoableMesh()
    {
        var commandFactory =
            _runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
        var commandExecutor =
            _runner.GetRequiredServiceInCurrentEditorScope<CommandExecutor>();
        var mainNode = _editor.SceneExplorer.SceneManager
            .GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
        var originalMeshCount = mainNode
            .GetLodNodes()
            .Sum(lod => lod.GetAllModels(false).Count);

        try
        {
            commandFactory.Create<ConstructBoxUiCommand>().Execute();

            var createdMesh = mainNode
                .GetLodNodes()
                .SelectMany(lod => lod.GetAllModels(false))
                .Single(mesh => mesh.Name == "primitive_box");
            Assert.Multiple(() =>
            {
                Assert.That(
                    mainNode.GetLodNodes().Sum(lod => lod.GetAllModels(false).Count),
                    Is.EqualTo(originalMeshCount + 1));
                Assert.That(
                    SelectionManager
                        .GetState<ObjectSelectionState>()
                        .GetSingleSelectedObject(),
                    Is.SameAs(createdMesh));
                Assert.That(commandExecutor.CanUndo(), Is.True);
            });
        }
        finally
        {
            commandExecutor.Undo();
        }

        Assert.That(
            mainNode.GetLodNodes().Sum(lod => lod.GetAllModels(false).Count),
            Is.EqualTo(originalMeshCount));
    }

    [Test]
    public void FocusSelection_EdgeModeCentersSelectedEdge()
    {
        var camera =
            _runner.GetRequiredServiceInCurrentEditorScope<ArcBallCamera>();
        var focusService =
            _runner.GetRequiredServiceInCurrentEditorScope<
                FocusSelectableObjectService>();
        var sceneManager = _editor.SceneExplorer.SceneManager;
        var objectPosition =
            sceneManager.GetWorldPosition(_meshNode).Translation;
        var expected =
            (_meshNode.Geometry.GetVertexById(0) +
             _meshNode.Geometry.GetVertexById(1)) / 2 +
            objectPosition;
        camera.LookAt = new Microsoft.Xna.Framework.Vector3(
            123,
            456,
            789);
        SelectionManager.SetState(new EdgeSelectionState
        {
            RenderObject = _meshNode,
            SelectedEdges = [(0, 1)]
        });

        focusService.FocusSelection();

        Assert.That(camera.LookAt, Is.EqualTo(expected));
    }

    [Test]
    public void ClearKeyState_ReleasesModifierWithoutRunningHotkey()
    {
        var keyboard =
            _runner.GetRequiredServiceInCurrentEditorScope<WindowKeyboard>();
        keyboard.SetKeyDown(Key.LeftAlt, true);

        _editor.MenuBar.ClearKeyState(Key.System, Key.LeftAlt);

        Assert.That(keyboard.IsKeyDown(Key.LeftAlt), Is.False);
    }

    private ObjectSelectionState CreateObjectSelection()
    {
        var selection = new ObjectSelectionState();
        selection.ModifySelectionSingleObject(
            _meshNode,
            onlyRemove: false);
        return selection;
    }
}
