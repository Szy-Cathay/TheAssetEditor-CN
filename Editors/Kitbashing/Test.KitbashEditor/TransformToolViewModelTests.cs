using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.MenuBarViews;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.KitbashEditor;

[TestFixture]
public class TransformToolViewModelTests
{
    [Test]
    public void SelectionChanged_BoneSelectionSetsButtonFromCurrentSelection()
    {
        var context = CreateContext();
        var objectSelection = new ObjectSelectionState();
        objectSelection.ModifySelectionSingleObject(context.Node, onlyRemove: false);
        context.SelectionManager.SetState(objectSelection);
        Assert.That(context.ViewModel.ButtonEnabled, Is.True);

        var emptyBoneSelection = CreateBoneSelection(
            context.Node,
            context.Skeleton,
            context.Animation,
            []);
        context.SelectionManager.SetState(emptyBoneSelection);
        Assert.That(context.ViewModel.ButtonEnabled, Is.False);

        var selectedBoneSelection = CreateBoneSelection(
            context.Node,
            context.Skeleton,
            context.Animation,
            [0]);
        context.SelectionManager.SetState(selectedBoneSelection);
        Assert.That(context.ViewModel.ButtonEnabled, Is.True);
    }

    [Test]
    public void RepeatedTypedBoneTransforms_DoNotRetainLineageSubscribers()
    {
        var context = CreateContext();
        context.ViewModel.SetMode(TransformToolViewModel.TransformMode.Translate);

        for (var operation = 0; operation < 3; operation++)
        {
            context.ViewModel.Vector3.Set(1);
            context.ViewModel.ApplyCommand.Execute(null);
            Assert.That(context.Selection.BoneModificationSubscriberCount, Is.Zero);
        }

        Assert.That(context.CommandExecutor.CanUndo(), Is.True);
    }

    [Test]
    public void TypedBoneTransform_RefreshesPausedDisplayedPose()
    {
        var context = CreateContext();
        context.ViewModel.SetMode(TransformToolViewModel.TransformMode.Translate);
        context.ViewModel.Vector3.Set(1);
        var displayedPose =
            context.Node.AnimationPlayer!.GetCurrentAnimationFrame();

        context.ViewModel.ApplyCommand.Execute(null);

        Assert.That(
            context.Node.AnimationPlayer.GetCurrentAnimationFrame(),
            Is.Not.SameAs(displayedPose));
    }

    [Test]
    public void TypedBoneTransform_WhenPreviewApplyThrows_CancelsAndDisposesWrapper()
    {
        var context = CreateContext();
        context.ViewModel.SetMode(TransformToolViewModel.TransformMode.Translate);
        context.ViewModel.Vector3.Set(1);
        var initialFrame =
            context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
        var expectedException = new InvalidOperationException("apply failed");
        BoneModifiedEvent throwingSubscriber = _ => throw expectedException;
        context.Selection.BoneModifiedEvent += throwingSubscriber;
        var baselineSubscribers =
            context.Selection.BoneModificationSubscriberCount;

        var actualException = Assert.Throws<InvalidOperationException>(
            () => context.ViewModel.ApplyCommand.Execute(null));

        Assert.Multiple(() =>
        {
            Assert.That(actualException, Is.SameAs(expectedException));
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.EqualTo(initialFrame.Position));
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Rotation,
                Is.EqualTo(initialFrame.Rotation));
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Scale,
                Is.EqualTo(initialFrame.Scale));
            Assert.That(
                context.Selection.BoneModificationSubscriberCount,
                Is.EqualTo(baselineSubscribers));
            Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        });

        context.Selection.BoneModifiedEvent -= throwingSubscriber;
    }

    [Test]
    public void TypedBoneTransform_WhenCommitNotificationThrows_DisposesWrapper()
    {
        var context = CreateContext();
        context.ViewModel.SetMode(TransformToolViewModel.TransformMode.Translate);
        context.ViewModel.Vector3.Set(1);
        var expectedException = new InvalidOperationException("commit failed");
        context.EventHub.CommandStackChangedException = expectedException;

        var actualException = Assert.Throws<InvalidOperationException>(
            () => context.ViewModel.ApplyCommand.Execute(null));

        Assert.Multiple(() =>
        {
            Assert.That(actualException, Is.SameAs(expectedException));
            Assert.That(context.Selection.BoneModificationSubscriberCount, Is.Zero);
            Assert.That(context.CommandExecutor.CanUndo(), Is.True);
        });
    }

    private static TestContext CreateContext()
    {
        var eventHub = new TestEventHub();
        var commandExecutor = new CommandExecutor(eventHub);
        var selectionManager = new SelectionManager(eventHub);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(TransformBoneCommand)))
            .Returns(() => new TransformBoneCommand(selectionManager));
        var commandFactory = new CommandFactory(
            serviceProvider.Object,
            commandExecutor);
        var scene = CreateScene();
        var selection = CreateBoneSelection(
            scene.Node,
            scene.Skeleton,
            scene.Animation,
            [0]);
        selectionManager.SetState(selection);
        var viewModel = new TransformToolViewModel(
            selectionManager,
            commandExecutor,
            commandFactory,
            eventHub);
        selectionManager.SetState(selection);
        return new TestContext(
            viewModel,
            selectionManager,
            commandExecutor,
            eventHub,
            scene.Node,
            selection,
            scene.Skeleton,
            scene.Animation);
    }

    private static BoneSelectionState CreateBoneSelection(
        Rmv2MeshNode node,
        GameSkeleton skeleton,
        AnimationClip animation,
        int[] selectedBones)
    {
        return new BoneSelectionState(node)
        {
            CurrentAnimation = animation,
            Skeleton = skeleton,
            CurrentFrame = 0,
            SelectedBones = selectedBones.ToList()
        };
    }

    private static BoneScene CreateScene()
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

        var clip = new AnimationClip();
        clip.DynamicFrames.Add(new AnimationClip.KeyFrame
        {
            Position = [Vector3.Zero],
            Rotation = [Quaternion.Identity],
            Scale = [Vector3.One]
        });
        clip.PlayTimeInSec = 1;
        player.SetAnimation(clip, skeleton);
        player.IsEnabled = true;
        player.Pause();
        player.Refresh();

        var graphics = new Mock<IGraphicsCardGeometry>();
        var mesh = new MeshObject(graphics.Object, string.Empty)
        {
            VertexArray = [],
            IndexArray = []
        };
        var material = new Mock<IRmvMaterial>();
        material.SetupProperty(value => value.ModelName, "TestMesh");
        material.SetupProperty(value => value.PivotPoint, Vector3.Zero);
        var node = new Rmv2MeshNode(mesh, material.Object, null, player);
        return new BoneScene(node, skeleton, clip);
    }

    private sealed class TestEventHub : IEventHub
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = [];
        public Exception? CommandStackChangedException { get; set; }

        public void PublishGlobalEvent<T>(T e) => Publish(e);

        public void Publish<T>(T e)
        {
            if (e is CommandStackChangedEvent &&
                CommandStackChangedException != null)
            {
                throw CommandStackChangedException;
            }

            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
                return;

            foreach (var subscriber in subscribers)
                ((Action<T>)subscriber)(e);
        }

        public void Register<T>(object owner, Action<T> action)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
            {
                subscribers = [];
                _subscribers.Add(typeof(T), subscribers);
            }

            subscribers.Add(action);
        }

        public void UnRegister(object owner)
        {
        }
    }

    private sealed record TestContext(
        TransformToolViewModel ViewModel,
        SelectionManager SelectionManager,
        CommandExecutor CommandExecutor,
        TestEventHub EventHub,
        Rmv2MeshNode Node,
        BoneSelectionState Selection,
        GameSkeleton Skeleton,
        AnimationClip Animation);

    private sealed record BoneScene(
        Rmv2MeshNode Node,
        GameSkeleton Skeleton,
        AnimationClip Animation);
}
