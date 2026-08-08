using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Services;

[TestFixture]
public class FocusSelectableObjectServiceTests
{
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
        animation.PlayTimeInSec = 1;
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
