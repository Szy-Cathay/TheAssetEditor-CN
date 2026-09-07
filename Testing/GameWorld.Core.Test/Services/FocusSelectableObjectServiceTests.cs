using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Services;

[TestFixture]
[NonParallelizable]
public class FocusSelectableObjectServiceTests
{
    private (GraphicsDevice Device, Viewport Viewport)? _originalViewport;

    [TearDown]
    public void RestoreViewport()
    {
        if (_originalViewport is { } original) original.Device.Viewport = original.Viewport;
        _originalViewport = null;
    }

    [Test, Combinatorial]
    public void FocusSelection_FramesDisplayedGeometryInBothProjections(
        [Values(GeometrySelectionMode.Object, GeometrySelectionMode.Vertex, GeometrySelectionMode.Edge, GeometrySelectionMode.Face)] GeometrySelectionMode mode,
        [Values(false, true)] bool orthographic,
        [Values(false, true)] bool animated,
        [Values(400, 1200)] int width)
    {
        var game = new WpfGameMock();
        var resolver = new Mock<IDeviceResolver>();
        resolver.SetupGet(value => value.Device).Returns(game.GraphicsDevice);
        _originalViewport = (game.GraphicsDevice, game.GraphicsDevice.Viewport);
        game.GraphicsDevice.Viewport = new Viewport(0, 0, width, 800);
        var mouse = new Mock<IMouseComponent>();
        mouse.Setup(value => value.GetScreenSize()).Returns(new Vector2(width, 800));
        using var camera = new ArcBallCamera(resolver.Object, Mock.Of<IKeyboardComponent>(), mouse.Object);
        camera.Initialize();
        camera.Yaw = 0.4f;
        camera.Pitch = -0.2f;
        camera.Zoom = 100;
        camera.OrthoSize = 100;
        camera.CurrentProjectionType = orthographic ? ProjectionType.Orthographic : ProjectionType.Perspective;
        var (node, _, _) = CreateBoneScene();
        var positions = new[] { new Vector3(-2, -1, 0), new Vector3(2, -1, 0), new Vector3(1, 1, 0) };
        node.Geometry.VertexArray = positions.Select(position => new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(position, 1), BlendIndices = Vector4.Zero, BlendWeights = new Vector4(1, 0, 0, 0)
        }).ToArray();
        node.Geometry.IndexArray = [0, 1, 2];
        node.Geometry.ChangeVertexType(animated ? UiVertexFormat.Weighted : UiVertexFormat.Static, updateMesh: false);
        node.Geometry.BuildBoundingBox();
        node.Position = new Vector3(9, -3, 4);
        node.Scale = new Vector3(2, 0.5f, 1);
        node.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        var parent = new GroupNode { ModelMatrix = Matrix.CreateTranslation(-2, 4, 1) };
        parent.AddObject(node);
        var events = Mock.Of<IEventHub>();
        var selection = new SelectionManager(events);
        ISelectionState state = mode switch
        {
            GeometrySelectionMode.Vertex => new VertexSelectionState(node, 0) { SelectedVertices = [0, 1, 2] },
            GeometrySelectionMode.Edge => new EdgeSelectionState { RenderObject = node, SelectedEdges = [(0, 1), (1, 2)] },
            GeometrySelectionMode.Face => new FaceSelectionState { RenderObject = node, SelectedFaces = [0] },
            _ => new ObjectSelectionState()
        };
        if (state is ObjectSelectionState objects) objects.ModifySelectionSingleObject(node, false);
        selection.SetState(state);
        var original = node.Geometry.VertexArray.ToArray();
        var expectedPositions = positions.Select(position => Vector3.Transform(
            position + (animated ? new Vector3(2, 4, 6) : Vector3.Zero),
            Matrix.CreateScale(node.Scale) * Matrix.CreateFromQuaternion(node.Orientation) *
            Matrix.CreateTranslation(node.Position) * parent.ModelMatrix)).ToArray();
        var bounds = BoundingBox.CreateFromPoints(expectedPositions);
        var service = new FocusSelectableObjectService(selection, camera, new SceneManager(null!, camera, events));

        service.FocusSelection();

        Assert.That(Vector3.Distance(camera.LookAt, (bounds.Min + bounds.Max) / 2), Is.LessThan(0.0001), "Frame the displayed pose, including object and parent transforms.");
        var screen = expectedPositions.Select(point => camera.InputViewport.Project(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity)).ToArray();
        foreach (var point in screen)
        {
            Assert.That(point.X, Is.InRange(width * 0.05f, width * 0.95f));
            Assert.That(point.Y, Is.InRange(40f, 760f));
            Assert.That(point.Z, Is.InRange(0f, 1f));
        }
        var occupancy = Math.Max((screen.Max(point => point.X) - screen.Min(point => point.X)) / width,
            (screen.Max(point => point.Y) - screen.Min(point => point.Y)) / 800);
        Assert.That(occupancy, Is.GreaterThan(0.35f), "Frame Selected must also zoom in on a small selection.");
        Assert.That(node.Geometry.VertexArray, Is.EqualTo(original));
    }

    [TestCase(GeometrySelectionMode.Vertex)]
    [TestCase(GeometrySelectionMode.Edge)]
    [TestCase(GeometrySelectionMode.Face)]
    public void FocusSelection_EmptyEditSelection_DoesNotMoveCamera(GeometrySelectionMode mode)
    {
        var events = Mock.Of<IEventHub>();
        var selection = new SelectionManager(events);
        var camera = new ArcBallCamera(null!, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        var (node, _, _) = CreateBoneScene();
        selection.CreateSelectionSate(mode, node);
        camera.LookAt = new Vector3(10, 20, 30);
        var service = new FocusSelectableObjectService(selection, camera, new SceneManager(null!, camera, events));
        service.FocusSelection();
        Assert.That(camera.LookAt, Is.EqualTo(new Vector3(10, 20, 30)));
    }

    [Test]
    public void FocusObjects_MultipleTransformedObjects_FramesTheirCombinedWorldBounds()
    {
        var events = Mock.Of<IEventHub>();
        var camera = new ArcBallCamera(null!, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        var service = new FocusSelectableObjectService(new SelectionManager(events), camera, new SceneManager(null!, camera, events));
        var (first, _, _) = CreateBoneScene();
        var (second, _, _) = CreateBoneScene();
        first.Geometry.VertexArray = [new VertexPositionNormalTextureCustom { Position = new Vector4(1, 2, 0, 1) }];
        second.Geometry.VertexArray = [new VertexPositionNormalTextureCustom { Position = new Vector4(1, 2, 0, 1) }];
        first.Geometry.ChangeVertexType(UiVertexFormat.Static, updateMesh: false);
        second.Geometry.ChangeVertexType(UiVertexFormat.Static, updateMesh: false);
        first.Position = new Vector3(-10, 0, 0);
        second.Position = new Vector3(10, 2, 0);
        service.FocusObjects([first, second]);
        Assert.That(camera.LookAt, Is.EqualTo(new Vector3(1, 3, 0)));
        Assert.That(camera.Zoom, Is.GreaterThan(10));
    }

    [Test]
    public void ResetCamera_AlsoRestoresOrthographicScale()
    {
        var events = Mock.Of<IEventHub>();
        var camera = new ArcBallCamera(null!, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>())
        {
            CurrentProjectionType = ProjectionType.Orthographic, OrthoSize = 500, Zoom = 100, LookAt = Vector3.One
        };
        var service = new FocusSelectableObjectService(new SelectionManager(events), camera, new SceneManager(null!, camera, events));
        service.ResetCamera();
        Assert.That(camera.LookAt, Is.EqualTo(Vector3.Zero));
        Assert.That(camera.Zoom, Is.EqualTo(10));
        Assert.That(camera.OrthoSize, Is.EqualTo(camera.PerspectiveViewHeight));
    }

    [Test]
    public void FocusSelection_BoneModeCentersSelectedBone()
    {
        var eventHub = Mock.Of<IEventHub>();
        var selectionManager = new SelectionManager(eventHub);
        var camera = new ArcBallCamera(
            null!,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.LookAt = new Vector3(100, 200, 300);
        var sceneManager = new SceneManager(null!, camera, eventHub);
        var (node, skeleton, animation) = CreateBoneScene();
        node.Position = new Vector3(10, 0, 0);
        selectionManager.SetState(new BoneSelectionState(node)
        {
            CurrentAnimation = animation,
            Skeleton = skeleton,
            CurrentFrame = 0,
            SelectedBones = [0]
        });
        var service = new FocusSelectableObjectService(
            selectionManager,
            camera,
            sceneManager);

        service.FocusSelection();

        Assert.That(camera.LookAt, Is.EqualTo(new Vector3(12, 4, 6)));
    }

    [Test]
    public void FocusSelection_BoneModeWithoutAnimationCentersObject()
    {
        var eventHub = Mock.Of<IEventHub>();
        var selectionManager = new SelectionManager(eventHub);
        var camera = new ArcBallCamera(
            null!,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        var sceneManager = new SceneManager(null!, camera, eventHub);
        var (node, _, _) = CreateBoneScene();
        node.Position = new Vector3(10, 20, 30);
        selectionManager.SetState(new BoneSelectionState(node)
        {
            SelectedBones = [0]
        });
        var service = new FocusSelectableObjectService(
            selectionManager,
            camera,
            sceneManager);

        service.FocusSelection();

        Assert.That(camera.LookAt, Is.EqualTo(node.Position));
    }

    private static (
        Rmv2MeshNode Node,
        GameSkeleton Skeleton,
        AnimationClip Animation) CreateBoneScene()
    {
        var player = new AnimationPlayer();
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "TestSkeleton"
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Name = "bone_0",
                    ParentId = -1
                }
            ]
        };
        var skeletonFrame = new AnimationFile.Frame();
        skeletonFrame.Transforms.Add(new RmvVector3());
        skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var skeletonPart = new AnimationFile.AnimationPart();
        skeletonPart.DynamicFrames.Add(skeletonFrame);
        skeletonFile.AnimationParts.Add(skeletonPart);
        var skeleton = new GameSkeleton(skeletonFile, player);

        var animation = new AnimationClip();
        animation.DynamicFrames.Add(new AnimationClip.KeyFrame
        {
            Position = [new Vector3(2, 4, 6)],
            Rotation = [Quaternion.Identity],
            Scale = [Vector3.One]
        });
        animation.Duration = TimeSpan.FromSeconds(1);
        player.SetAnimation(animation, skeleton);
        player.IsEnabled = true;
        player.Pause();
        player.Refresh();

        var mesh = new MeshObject(
            Mock.Of<IGraphicsCardGeometry>(),
            string.Empty)
        {
            VertexArray = [],
            IndexArray = []
        };
        var material = new Mock<IRmvMaterial>();
        material.SetupProperty(value => value.ModelName, "TestMesh");
        material.SetupProperty(value => value.PivotPoint, Vector3.Zero);
        var node = new Rmv2MeshNode(
            mesh,
            material.Object,
            null!,
            player);
        return (node, skeleton, animation);
    }
}
