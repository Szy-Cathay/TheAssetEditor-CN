using AnimationEditor.MountAnimationCreator;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.KitbasherEditor.Components;
using Editors.Shared.Core.Common;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace AssetEditorTests;

[TestClass]
public class ViewportModelInteractionTests
{
    [TestMethod]
    public void KitbashComposition_EnablesOnlyItsOwnViewport()
    {
        var services = new ServiceCollection();
        new GameWorld.Core.DependencyInjectionContainer().Register(services);
        using var provider = services.BuildServiceProvider();
        using var previewScope = provider.CreateScope();
        using var kitbashScope = provider.CreateScope();
        var preview = previewScope.ServiceProvider.GetRequiredService<ViewportInteractionCapabilities>();
        var kitbash = kitbashScope.ServiceProvider.GetRequiredService<ViewportInteractionCapabilities>();
        Assert.IsFalse(preview.ModelEditingEnabled);
        Assert.IsFalse(kitbash.ModelEditingEnabled);

        var events = Mock.Of<IEventHub>();
        using var selection = new SelectionManager(events);
        var components = new KitbashSceneComponentSet(events, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>(),
            null!, new CommandExecutor(events), null!, null!, null!, selection, null!, null!, null!, kitbash);

        Assert.IsTrue(kitbash.ModelEditingEnabled);
        Assert.IsFalse(preview.ModelEditingEnabled);
        CollectionAssert.IsSubsetOf(new IGameComponent[] { components.SelectionInput, components.ModelGizmo, components.SelectionOverlay },
            components.Components);
    }

    [TestMethod]
    public void PreviewOutline_DoesNotOutlineAProgrammaticallySelectedModel()
    {
        using var selection = new SelectionManager(Mock.Of<IEventHub>());
        var mesh = CreateMesh();
        var state = (ObjectSelectionState)selection.GetState();
        state.ModifySelection([mesh], false);
        using var outline = new ReferenceObjectSelectionOutlineComponent(selection, null!, new ViewportInteractionCapabilities());

        outline.Draw(new GameTime());

        Assert.AreSame(mesh, state.GetSingleSelectedObject());
    }

    [TestMethod]
    public void KeyframePicker_IgnoresModelsButRetainsBoneInteraction()
    {
        var mouse = new Mock<IMouseComponent>();
        mouse.Setup(item => item.IsMouseOwner(It.IsAny<IGameComponent>())).Returns(true);
        using var selection = new SelectionManager(Mock.Of<IEventHub>());
        using var picker = new AnimationBoneSelectionComponent(mouse.Object, Mock.Of<IKeyboardComponent>(),
            null!, selection, null!, null!, null!, null!, new ViewportInteractionCapabilities());

        picker.Update(new GameTime());
        mouse.Verify(item => item.IsMouseButtonPressed(It.IsAny<MouseButton>()), Times.Never);
        Assert.IsInstanceOfType(selection.GetState(), typeof(ObjectSelectionState));

        selection.CreateSelectionSate(GeometrySelectionMode.Bone, CreateMesh());
        picker.Update(new GameTime());
        mouse.Verify(item => item.IsMouseButtonPressed(MouseButton.Left), Times.Once);
        Assert.IsInstanceOfType(selection.GetState(), typeof(BoneSelectionState));
    }

    [TestMethod]
    public void MountAttachment_PicksOnlyMountVerticesWithoutSelectingModels()
    {
        using var selection = new SelectionManager(Mock.Of<IEventHub>());
        using var picker = CreateMountPicker(selection);
        var world = new GroupNode("world");
        var mountRoot = world.AddObject(new GroupNode("mount"));
        var mountMesh = mountRoot.AddObject(CreateMesh());
        var nearerRider = world.AddObject(CreateMesh());
        nearerRider.ModelMatrix = Matrix.CreateTranslation(0, 0, 2);
        var mount = new SceneObject("mount") { ModelNode = mountRoot };
        mount.IsSelectable = false;
        picker.SetMount(mount);
        var ray = new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ);
        Assert.IsNull(picker.SelectAttachmentMesh(ray));

        picker.ToggleVertexSelectionMode();
        Assert.IsInstanceOfType(selection.GetState(), typeof(VertexSelectionState));
        Assert.AreEqual(0, selection.GetState().SelectionCount());
        var vertices = picker.SelectAttachmentMesh(ray);
        Assert.IsNotNull(vertices);
        Assert.AreSame(mountMesh, vertices.RenderObject);
        Assert.IsFalse(mountMesh.IsSelectable);
        vertices.SetSelection([0]);
        CollectionAssert.AreEqual(new[] { 0 }, vertices.SelectedVertices);
        Assert.AreEqual(new Vector4(-1, -1, 0, 1), mountMesh.Geometry.VertexArray[0].Position);

        picker.ToggleVertexSelectionMode();
        Assert.IsInstanceOfType(selection.GetState(), typeof(ObjectSelectionState));
        Assert.AreEqual(0, selection.GetState().SelectionCount());
        Assert.IsNull(picker.SelectAttachmentMesh(ray));
    }

    [TestMethod]
    public void MountAttachment_UsesVisibleNearestMeshAndResetsWhenModelChanges()
    {
        using var selection = new SelectionManager(Mock.Of<IEventHub>());
        using var picker = CreateMountPicker(selection);
        var root = new GroupNode("mount");
        var farMesh = root.AddObject(CreateMesh());
        var nearMesh = root.AddObject(CreateMesh());
        nearMesh.ModelMatrix = Matrix.CreateTranslation(0, 0, 2);
        var mount = new SceneObject("mount") { ModelNode = root };
        picker.SetMount(mount);
        picker.ToggleVertexSelectionMode();
        var ray = new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ);
        Assert.AreSame(nearMesh, picker.SelectAttachmentMesh(ray)!.RenderObject);
        nearMesh.IsVisible = false;
        Assert.AreSame(farMesh, picker.SelectAttachmentMesh(ray)!.RenderObject);
        root.IsVisible = false;
        Assert.IsNull(picker.SelectAttachmentMesh(ray));
        root.IsVisible = true;
        mount.TriggerMeshChanged();
        Assert.AreEqual(0, selection.GetState().SelectionCount());
        Assert.IsInstanceOfType(selection.GetState(), typeof(ObjectSelectionState));
        Assert.IsNull(picker.SelectAttachmentMesh(ray));
    }

    [TestMethod]
    public void MountAttachment_LeavesAltCameraInputUnclaimed()
    {
        var mouse = new Mock<IMouseComponent>();
        mouse.Setup(item => item.IsMouseOwner(It.IsAny<IGameComponent>())).Returns(true);
        var keyboard = new Mock<IKeyboardComponent>();
        keyboard.Setup(item => item.IsKeyDown(Keys.LeftAlt)).Returns(true);
        using var selection = new SelectionManager(Mock.Of<IEventHub>());
        using var picker = new MountVertexSelectionComponent(selection, keyboard.Object, mouse.Object, null!, null!, null!, null!);
        picker.SetMount(new SceneObject("mount") { ModelNode = CreateMesh() });
        picker.ToggleVertexSelectionMode();

        picker.Update(new GameTime());

        mouse.Verify(item => item.Position(), Times.Never);
        mouse.Verify(item => item.IsMouseButtonPressed(It.IsAny<MouseButton>()), Times.Never);
        mouse.VerifySet(item => item.MouseOwner = It.IsAny<IGameComponent>(), Times.Never);
    }

    private static MountVertexSelectionComponent CreateMountPicker(SelectionManager selection) =>
        new(selection, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>(), null!, null!, null!, null!);

    private static Rmv2MeshNode CreateMesh()
    {
        var geometry = new MeshObject(Mock.Of<IGraphicsCardGeometry>(), "test")
        {
            VertexArray = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0) }
                .Select(position => new VertexPositionNormalTextureCustom
                {
                    Position = new Vector4(position, 1), BlendWeights = new Vector4(1, 0, 0, 0),
                }).ToArray(),
            IndexArray = [0, 1, 2],
        };
        geometry.ChangeVertexType(UiVertexFormat.Weighted, false);
        return new Rmv2MeshNode(geometry, new WeightedMaterial(), null!, null!);
    }
}
