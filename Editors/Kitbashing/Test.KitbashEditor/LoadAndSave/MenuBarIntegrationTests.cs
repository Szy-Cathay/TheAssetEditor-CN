using System.Windows.Input;
using Editors.KitbasherEditor.Components;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Services;
using Shared.Core.Settings;
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
    public void Scene_UsesKitbashExclusiveModelEditingComponents()
    {
        var scene = (WpfGameMock)_editor.Scene;
        Assert.Multiple(() =>
        {
            Assert.That(
                scene.Components,
                Has.Exactly(1)
                    .TypeOf<KitbashSelectionOverlayComponent>());
            Assert.That(
                scene.Components,
                Has.Exactly(1)
                    .TypeOf<KitbashSelectionInputComponent>());
            Assert.That(
                scene.Components,
                Has.Exactly(1)
                    .TypeOf<KitbashModelGizmoComponent>());
        });
    }

    [Test]
    public void GizmoScaleCommands_MatchTheirNames()
    {
        var commandFactory =
            _runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
        var gizmoComponent =
            _runner.GetRequiredServiceInCurrentEditorScope<
                KitbashSceneComponentSet>()
            .ModelGizmo;
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
    public void CircleTool_PreservesGizmoToolsAndOnlyChangesSelectionShape()
    {
        var components = _runner.GetRequiredServiceInCurrentEditorScope<KitbashSceneComponentSet>();
        var tools = _editor.MenuBar.SidebarButtons.OfType<MenuBarGroupButton>()
            .Where(button => button.GroupName == "Gizmo").ToList();
        SelectionManager.SetState(new VertexSelectionState(_meshNode, 0));
        Assert.That(_editor.MenuBar.CanUseMeshSelectionTools.Value, Is.True);
        Assert.That(_editor.MenuBar.SelectionSettings, Is.SameAs(components.SelectionInput.Settings));
        foreach (var tool in tools.Skip(1))
        {
            tool.Action.TriggerAction();
            var gizmoMode = components.ModelGizmo.Gizmo.ActiveMode;
            var transformVisible = _editor.MenuBar.TransformTool.IsVisible;
            components.SelectionSettings.IsCircleSelection = true;
            Assert.That(tool.IsChecked.Value, Is.True);
            Assert.That(components.ModelGizmo.Gizmo.ActiveMode, Is.EqualTo(gizmoMode));
            Assert.That(_editor.MenuBar.TransformTool.IsVisible, Is.EqualTo(transformVisible));
            tool.Action.TriggerAction();
            Assert.That(components.SelectionInput.Settings.IsCircleSelection, Is.True);
            Assert.That(tool.IsChecked.Value, Is.True);
            components.SelectionSettings.IsCircleSelection = false;
            Assert.That(tool.IsChecked.Value, Is.True);
            Assert.That(_editor.MenuBar.TransformTool.IsVisible, Is.EqualTo(transformVisible));
        }
        components.SelectionSettings.IsCircleSelection = true;
        SelectionManager.SetState(CreateObjectSelection());
        Assert.That(_editor.MenuBar.CanUseMeshSelectionTools.Value, Is.False);
        Assert.That(components.SelectionInput.Settings.IsCircleSelection, Is.False);
        tools[0].Action.TriggerAction();
    }

    [Test]
    public void FaceMode_SplitToolbarButtonIsEnabledForSelectedFaces()
    {
        SelectionManager.SetState(new FaceSelectionState
        {
            RenderObject = _meshNode,
            SelectedFaces = [0]
        });
        var splitButton = _editor.MenuBar.CustomButtons.SingleOrDefault(button =>
            button.IsVisible.Value &&
            button.Action is Editors.KitbasherEditor.Core.MenuBarViews.KitbasherMenuItem<DivideSubMeshCommand>);

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
    public void PhotoStudio_IsAvailableFromToolbarAndChineseMenu()
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
    public void SpecialistTools_AreDirectlyAvailableOnTheToolbarInTheirSelectionModes()
    {
        string[] VisibleCommands() => _editor.MenuBar.CustomButtons
            .Where(button => button.IsVisible.Value && !button.IsSeperator)
            .Select(button => button.Action.GetType().GetGenericArguments()[0].Name).ToArray();

        SelectionManager.SetState(CreateObjectSelection());
        Assert.That(VisibleCommands(), Is.SupersetOf(new[]
        {
            "DivideSubMeshCommand", "MergeObjectsCommand", "CreateStaticMeshCommand", "ReduceMeshCommand",
            "OpenSkeletonReshaperToolCommand", "OpenReriggingToolCommand", "OpenPinToolCommand",
            "AssignMaterialFromOtherMeshUiCommand", "OpenPhotoStudioCommand", "OpenBlenderShortcutsHelpCommand"
        }));

        SelectionManager.SetState(new FaceSelectionState { RenderObject = _meshNode, SelectedFaces = [0] });
        Assert.That(VisibleCommands(), Is.SupersetOf(new[]
        {
            "ConvertFaceToVertexCommand", "ExpandFaceSelectionCommand", "DivideSubMeshCommand",
            "DuplicateObjectCommand", "DeleteObjectCommand", "OpenPhotoStudioCommand", "OpenBlenderShortcutsHelpCommand"
        }));
        Assert.That(VisibleCommands(), Is.Unique);

        SelectionManager.SetState(new VertexSelectionState(_meshNode, 0) { SelectedVertices = [0] });
        Assert.That(VisibleCommands(), Does.Contain("OpenVertexDebuggerCommand"));
        Assert.That(VisibleCommands(), Does.Not.Contain("MergeObjectsCommand"));
    }

    [Test]
    public void StatisticsMenu_TogglesTheExistingViewportOverlay()
    {
        var statistics = _runner.GetRequiredServiceInCurrentEditorScope<FpsComponent>();
        var item = FlattenMenu(_editor.MenuBar.MenuItems).Single(item => item.Name == "显示统计信息");
        Assert.That(statistics.Visible, Is.False);
        try
        {
            item.Action.Command.Execute(null);
            Assert.That(statistics.Visible, Is.True);
            Assert.That(item.Name, Is.EqualTo("隐藏统计信息"));
            item.Action.Command.Execute(null);
            Assert.That(statistics.Visible, Is.False);
            Assert.That(item.Name, Is.EqualTo("显示统计信息"));
        }
        finally
        {
            if (statistics.Visible)
                item.Action.Command.Execute(null);
        }
    }

    [Test]
    public void ViewportToolbar_FollowsBackgroundSettingsWithoutEditingTheModel()
    {
        var settings = _runner.GetRequiredServiceInCurrentEditorScope<ApplicationSettingsService>();
        var eventHub = _runner.GetRequiredServiceInCurrentEditorScope<IEventHub>();
        var original = ViewportRenderSettings.From(settings.CurrentSettings);
        var dirty = _editor.HasUnsavedChanges;
        try
        {
            eventHub.Publish(new ViewportRenderSettingsChangedEvent(original with
            {
                BackgroundColour = BackgroundColour.Custom,
                CustomBackgroundColour = "255,255,255",
            }));
            Assert.That(_editor.MenuBar.ViewportBackground.Value, Is.EqualTo(System.Windows.Media.Colors.White));
            Assert.That(_editor.HasUnsavedChanges, Is.EqualTo(dirty));
        }
        finally
        {
            eventHub.Publish(new ViewportRenderSettingsChangedEvent(original));
        }
    }

    [Test]
    public void SceneLabels_LocalizeOnlySyntheticNodesAndFollowModelRenames()
    {
        var manager = _editor.SceneExplorer.SceneManager;
        var root = new KitbasherEditor.Views.SceneExplorerNode(manager.RootNode, false);
        var model = manager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
        var editable = new KitbasherEditor.Views.SceneExplorerNode(model, false);
        var userNode = new GroupNode("Root");
        var wrapper = new KitbasherEditor.Views.SceneExplorerNode(userNode, false);
        var notified = false;
        wrapper.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(wrapper.DisplayName);
        Assert.Multiple(() =>
        {
            Assert.That(root.DisplayName, Is.EqualTo("场景"));
            Assert.That(manager.RootNode.Name, Is.EqualTo("Root"));
            Assert.That(editable.DisplayName, Is.EqualTo("编辑中的模型"));
            Assert.That(model.Name, Is.EqualTo("Editable Model"));
            Assert.That(wrapper.DisplayName, Is.EqualTo("Root"));
        });
        userNode.Name = "renamed_mesh";
        Assert.That(wrapper.DisplayName, Is.EqualTo("renamed_mesh"));
        Assert.That(notified, Is.True);
    }

    private static IEnumerable<ToolbarItem> FlattenMenu(IEnumerable<ToolbarItem> items) =>
        items.SelectMany(item => new[] { item }.Concat(FlattenMenu(item.Children)));

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
        var pose = MeshPoseSnapshot.Capture(_meshNode);
        var expected =
            (pose.GetWorldPosition(0) + pose.GetWorldPosition(1)) / 2;
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
