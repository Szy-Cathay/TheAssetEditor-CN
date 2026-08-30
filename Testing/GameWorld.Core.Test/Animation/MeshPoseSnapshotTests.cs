using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Animation;

public class MeshPoseSnapshotTests
{
    private const float Epsilon = 0.0001f;

    [Test]
    public void PausedAnimationPlayer_WhenPausedRefreshDisabled_ReusesCurrentPose()
    {
        var node = CreatePausedAnimatedNode();
        var player = node.AnimationPlayer!;
        player.RefreshWhilePaused = false;
        var currentPose = player.GetCurrentAnimationFrame();

        player.Update(
            new GameTime(
                TimeSpan.FromMilliseconds(16),
                TimeSpan.FromMilliseconds(16)));

        Assert.That(
            player.GetCurrentAnimationFrame(),
            Is.SameAs(currentPose));
    }

    [Test]
    public void PausedAnimationPlayer_DefaultUpdateRefreshesCurrentPose()
    {
        var node = CreatePausedAnimatedNode();
        var player = node.AnimationPlayer!;
        var currentPose = player.GetCurrentAnimationFrame();

        player.Update(
            new GameTime(
                TimeSpan.FromMilliseconds(16),
                TimeSpan.FromMilliseconds(16)));

        Assert.That(
            player.GetCurrentAnimationFrame(),
            Is.Not.SameAs(currentPose));
    }

    [Test]
    public void PausedAnimationPlayer_ManualFrameChangeRefreshesPose()
    {
        var node = CreatePausedAnimatedNode();
        var player = node.AnimationPlayer!;
        var currentPose = player.GetCurrentAnimationFrame();

        player.CurrentFrame = 1;

        Assert.That(
            player.GetCurrentAnimationFrame(),
            Is.Not.SameAs(currentPose));
    }

    [Test]
    public void RebuildSkeletonMatrix_UpdatesInverseWorldTransform()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(
            player,
            new Vector3(1, 0, 0));

        Assert.That(
            skeleton.GetInverseWorldTransform(0),
            Is.EqualTo(Matrix.CreateTranslation(-1, 0, 0)));

        skeleton.Translation[0] = new Vector3(3, 0, 0);
        skeleton.RebuildSkeletonMatrix();

        Assert.That(
            skeleton.GetInverseWorldTransform(0),
            Is.EqualTo(Matrix.CreateTranslation(-3, 0, 0)));
    }

    [Test]
    public void Create_EvaluatesSkinnedWorldPositionWithoutChangingBindGeometry()
    {
        var mesh = CreateWeightedMesh(new Vector3(1, 0, 0));
        var originalVertex = mesh.VertexArray[0];
        var boneTransform =
            Matrix.CreateRotationZ(MathHelper.PiOver2) *
            Matrix.CreateTranslation(3, 0, 0);

        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.CreateTranslation(0, 2, 0),
            [boneTransform],
            applyAnimation: true);

        Assert.Multiple(() =>
        {
            AssertVector3(
                pose.GetWorldPosition(0),
                new Vector3(3, 3, 0));
            Assert.That(mesh.VertexArray[0], Is.EqualTo(originalVertex));
        });
    }

    [Test]
    public void ConservativeAnimatedBounds_ContainsSkinnedPositionOutsideBindPose()
    {
        var mesh = CreateWeightedMesh(new Vector3(1, 0, 0));
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateTranslation(5, 0, 0)],
            applyAnimation: true);

        var bounds = pose.GetConservativeAnimatedBounds();
        var skinnedPosition = pose.GetWorldPosition(0);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Min.X, Is.LessThanOrEqualTo(1));
            Assert.That(bounds.Max.X, Is.GreaterThanOrEqualTo(6));
            Assert.That(
                bounds.Contains(skinnedPosition),
                Is.Not.EqualTo(ContainmentType.Disjoint));
        });
    }

    [Test]
    public void PoseReplayPlan_MapsVisibleTranslationBackToBindGeometryAndSupportsUndo()
    {
        var mesh = CreateWeightedMesh(new Vector3(1, 0, 0));
        var originalVertex = mesh.VertexArray[0];
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateRotationZ(MathHelper.PiOver2)],
            applyAnimation: true);
        var selectable = new TestSelectableNode { Geometry = mesh };
        var selection = new VertexSelectionState(selectable, 0)
        {
            SelectedVertices = [0],
            VertexWeights = [1]
        };
        var plan = VertexTransformOperationApplier.CreateEmptyReplayPlan(
            selection,
            falloffWeights: null,
            new Dictionary<MeshObject, MeshPoseSnapshot>
            {
                [mesh] = pose
            });

        var appended = VertexTransformOperationApplier.TryAppendOperation(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            plan,
            new VertexTransformOperation(
                VertexTransformOperationMode.Translate,
                Matrix.CreateTranslation(1, 0, 0),
                pose.GetWorldPosition(0)),
            out var translatedPlan);

        Assert.That(appended, Is.True);

        VertexTransformOperationApplier.ApplyReplayPlan(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            translatedPlan,
            inverse: false);

        Assert.Multiple(() =>
        {
            AssertVector3(
                pose.GetWorldPosition(0),
                new Vector3(1, 1, 0));
            AssertVector3(
                mesh.GetVertexById(0),
                new Vector3(1, -1, 0));
            Assert.That(
                mesh.GetVertexById(0),
                Is.Not.EqualTo(pose.GetWorldPosition(0)),
                "The paused pose must not be baked into bind geometry.");
        });

        VertexTransformOperationApplier.ApplyReplayPlan(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            translatedPlan,
            inverse: true);

        Assert.That(mesh.VertexArray[0], Is.EqualTo(originalVertex));
    }

    [Test]
    public void PoseReplayPlan_ScaleTransformsVisibleBasisAndUndoRestoresBindBasis()
    {
        var mesh = CreateWeightedMesh(new Vector3(1, 0, 0));
        mesh.VertexArray[0].Normal =
            Vector3.Normalize(new Vector3(1, 2, 3)) * 2;
        mesh.VertexArray[0].Tangent =
            Vector3.Normalize(new Vector3(3, 1, 2)) * 2;
        mesh.VertexArray[0].BiNormal =
            Vector3.Normalize(new Vector3(2, 3, 1)) * 2;
        var originalVertex = mesh.VertexArray[0];
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateRotationZ(MathHelper.PiOver2)],
            applyAnimation: true);
        var vertexToWorld = pose.GetVertexToWorldTransform(0);
        var visibleNormal = Vector3.Normalize(
            Vector3.TransformNormal(
                originalVertex.Normal,
                vertexToWorld));
        var scale = Matrix.CreateScale(2, 0.5f, 1);
        var expectedVisibleNormal = Vector3.Normalize(
            Vector3.TransformNormal(
                visibleNormal,
                Matrix.Transpose(Matrix.Invert(scale))));
        var selectable = new TestSelectableNode { Geometry = mesh };
        var selection = new VertexSelectionState(selectable, 0)
        {
            SelectedVertices = [0],
            VertexWeights = [1]
        };
        var plan = VertexTransformOperationApplier.CreateEmptyReplayPlan(
            selection,
            falloffWeights: null,
            new Dictionary<MeshObject, MeshPoseSnapshot>
            {
                [mesh] = pose
            });

        var appended = VertexTransformOperationApplier.TryAppendOperation(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            plan,
            new VertexTransformOperation(
                VertexTransformOperationMode.Scale,
                scale,
                pose.GetWorldPosition(0)),
            out var scaledPlan);

        Assert.That(appended, Is.True);

        VertexTransformOperationApplier.ApplyReplayPlan(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            scaledPlan,
            inverse: false);

        Assert.Multiple(() =>
        {
            AssertVector3(
                Vector3.Normalize(
                    Vector3.TransformNormal(
                        mesh.VertexArray[0].Normal,
                        vertexToWorld)),
                expectedVisibleNormal);
            Assert.That(
                mesh.VertexArray[0].Normal.Length(),
                Is.EqualTo(originalVertex.Normal.Length())
                    .Within(Epsilon));
        });

        VertexTransformOperationApplier.ApplyReplayPlan(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            scaledPlan,
            inverse: true);

        AssertVertex(mesh.VertexArray[0], originalVertex);
    }

    [Test]
    public void CreateFromSelectionState_UsesCurrentPausedPoseForGizmoCenter()
    {
        var node = CreatePausedAnimatedNode();
        var selection = CreateVertexSelection(node);

        using var wrapper =
            TransformGizmoWrapper.CreateFromSelectionState(
                selection,
                null!);

        AssertVector3(
            wrapper!.Position,
            new Vector3(3, 3, 0));
    }

    [Test]
    public void ExistingSelection_RefreshesGizmoCenterWhenPlaybackPauses()
    {
        var node = CreatePausedAnimatedNode();
        var selection = CreateVertexSelection(node);
        using var wrapper =
            TransformGizmoWrapper.CreateFromSelectionState(
                selection,
                null!)!;
        var originalPosition = wrapper.Position;

        node.AnimationPlayer!.Play();
        wrapper.RefreshDisplayForAnimationState();
        node.AnimationPlayer.CurrentFrame = 1;
        node.AnimationPlayer.Pause();
        node.AnimationPlayer.Refresh();
        var expectedPosition =
            MeshPoseSnapshot.Capture(node).GetWorldPosition(0);

        wrapper.RefreshDisplayForAnimationState();

        Assert.Multiple(() =>
        {
            Assert.That(
                Vector3.Distance(
                    originalPosition,
                    expectedPosition),
                Is.GreaterThan(Epsilon));
            AssertVector3(wrapper.Position, expectedPosition);
        });
    }

    [Test]
    public void RenderCache_ReusesAnimationBufferAcrossMeshesAndFrames()
    {
        var firstNode = CreatePausedAnimatedNode();
        var secondNode = new Rmv2MeshNode(
            firstNode.Geometry,
            firstNode.RmvMaterial,
            firstNode.Material,
            firstNode.AnimationPlayer!);
        var cache = new MeshPoseRenderCache();

        var firstPose = cache.Capture(firstNode);
        var secondPose = cache.Capture(secondNode);
        var firstFrameTransform =
            firstPose.AnimationTransforms[0];

        firstNode.AnimationPlayer!.CurrentFrame = 1;
        var nextFramePose = cache.Capture(firstNode);

        Assert.Multiple(() =>
        {
            Assert.That(
                secondPose.AnimationTransforms,
                Is.SameAs(firstPose.AnimationTransforms));
            Assert.That(
                nextFramePose.AnimationTransforms,
                Is.SameAs(firstPose.AnimationTransforms));
            Assert.That(
                nextFramePose,
                Is.SameAs(firstPose));
            Assert.That(
                nextFramePose.AnimationTransforms[0],
                Is.Not.EqualTo(firstFrameTransform));
        });
    }

    [Test]
    public void RenderCache_RepeatedCaptureDoesNotAllocatePoseBuffers()
    {
        var node = CreatePausedAnimatedNode();
        var cache = new MeshPoseRenderCache();
        cache.Capture(node);
        var allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
            cache.Capture(node);

        var allocated =
            GC.GetAllocatedBytesForCurrentThread() -
            allocatedBefore;

        Assert.That(allocated, Is.LessThan(1024));
    }

    [Test]
    public void DenseEditOverlay_UsesGpuPathForAnimatedPoseWhetherPausedOrPlaying()
    {
        var node = CreatePausedAnimatedNode();
        var pausedPose = MeshPoseSnapshot.Capture(node);

        var whilePaused = !pausedPose.ApplyAnimation;
        node.AnimationPlayer!.Play();
        var whilePlaying =
            !MeshPoseSnapshot.Capture(node).ApplyAnimation;

        Assert.Multiple(() =>
        {
            Assert.That(whilePaused, Is.False);
            Assert.That(whilePlaying, Is.False);
        });
    }

    [Test]
    public void CreateEmptyReplayPlan_PrecomputesPausedPoseMappings()
    {
        var node = CreatePausedAnimatedNode();
        var selection = new ObjectSelectionState();
        selection.ModifySelection([node], onlyRemove: false);
        var pose = MeshPoseSnapshot.Capture(node);

        var plan =
            VertexTransformOperationApplier.CreateEmptyReplayPlan(
                selection,
                falloffWeights: null,
                new Dictionary<MeshObject, MeshPoseSnapshot>
                {
                    [node.Geometry] = pose
                });
        var poseMappingsProperty =
            plan.GetType().GetProperty("PoseMappings");

        Assert.That(
            poseMappingsProperty,
            Is.Not.Null,
            "Paused-pose vertex mappings must be built once when the gesture starts.");

        var poseMappings =
            poseMappingsProperty!.GetValue(plan) as
                System.Collections.IDictionary;
        Assert.That(poseMappings, Is.Not.Null);
        Assert.That(poseMappings!.Count, Is.EqualTo(1));
        var initialMappings =
            poseMappings[node.Geometry] as Array;
        Assert.That(
            initialMappings,
            Has.Length.EqualTo(node.Geometry.VertexCount()));

        var appended =
            VertexTransformOperationApplier.TryAppendOperation(
                [node.Geometry],
                selection,
                affectedVertexIndices: null,
                falloffWeights: null,
                plan,
                new VertexTransformOperation(
                    VertexTransformOperationMode.Translate,
                    Matrix.CreateTranslation(1, 0, 0),
                    Vector3.Zero),
                out var candidatePlan);

        Assert.Multiple(() =>
        {
            Assert.That(appended, Is.True);
            Assert.That(
                candidatePlan.PoseMappings[node.Geometry],
                Is.SameAs(initialMappings));
        });
    }

    [Test]
    public void CreateEmptyReplayPlan_RigidPoseDoesNotAllocatePerVertexMappings()
    {
        var mesh = CreateWeightedMesh(Vector3.Zero);
        var template = mesh.VertexArray[0];
        mesh.VertexArray =
            new VertexPositionNormalTextureCustom[10_000];
        Array.Fill(mesh.VertexArray, template);
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.CreateRotationZ(MathHelper.PiOver4) *
            Matrix.CreateTranslation(3, 2, 1),
            Array.Empty<Matrix>(),
            applyAnimation: false);
        var selection = new ObjectSelectionState();
        var snapshots =
            new Dictionary<MeshObject, MeshPoseSnapshot>
            {
                [mesh] = pose
            };

        VertexTransformOperationApplier.CreateEmptyReplayPlan(
            selection,
            falloffWeights: null,
            new Dictionary<MeshObject, MeshPoseSnapshot>());
        var allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();

        var plan =
            VertexTransformOperationApplier.CreateEmptyReplayPlan(
                selection,
                falloffWeights: null,
                snapshots);

        var allocated =
            GC.GetAllocatedBytesForCurrentThread() -
            allocatedBefore;
        GC.KeepAlive(plan);
        Assert.That(allocated, Is.LessThan(128 * 1024));
    }

    [Test]
    public void CreateEmptyReplayPlan_LargeAnimatedPoseBuildsEveryMapping()
    {
        var mesh = CreateWeightedMesh(Vector3.Zero);
        var template = mesh.VertexArray[0];
        mesh.VertexArray =
            new VertexPositionNormalTextureCustom[4096];
        Array.Fill(mesh.VertexArray, template);
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateTranslation(1, 2, 3)],
            applyAnimation: true);

        var plan =
            VertexTransformOperationApplier.CreateEmptyReplayPlan(
                new ObjectSelectionState(),
                falloffWeights: null,
                new Dictionary<MeshObject, MeshPoseSnapshot>
                {
                    [mesh] = pose
                });

        Assert.Multiple(() =>
        {
            Assert.That(plan.HasInvalidPoseMappings, Is.False);
            Assert.That(plan.PoseMappings[mesh], Has.Length.EqualTo(4096));
            Assert.That(
                plan.PoseMappings[mesh].All(mapping => mapping.IsValid),
                Is.True);
        });
    }

    [Test]
    public void RigidPoseTransform_MovesEveryVertexInWorldSpace()
    {
        var mesh = CreateWeightedMesh(new Vector3(1, 0, 0));
        var secondVertex = mesh.VertexArray[0];
        secondVertex.Position =
            new Vector4(0, 2, 0, 1);
        mesh.VertexArray =
            [mesh.VertexArray[0], secondVertex];
        var originalVertices =
            mesh.VertexArray.ToArray();
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.CreateRotationZ(MathHelper.PiOver2) *
            Matrix.CreateTranslation(3, 2, 0),
            Array.Empty<Matrix>(),
            applyAnimation: false);
        var originalWorldPositions =
            pose.GetWorldPositions();
        var selection = new ObjectSelectionState();
        var plan =
            VertexTransformOperationApplier.CreateEmptyReplayPlan(
                selection,
                falloffWeights: null,
                new Dictionary<MeshObject, MeshPoseSnapshot>
                {
                    [mesh] = pose
                });

        Assert.That(
            VertexTransformOperationApplier.TryAppendOperation(
                [mesh],
                selection,
                affectedVertexIndices: null,
                falloffWeights: null,
                plan,
                new VertexTransformOperation(
                    VertexTransformOperationMode.Translate,
                    Matrix.CreateTranslation(1, 0, 0),
                    Vector3.Zero),
                out var translatedPlan),
            Is.True);

        VertexTransformOperationApplier.ApplyReplayPlan(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            translatedPlan,
            inverse: false);

        Assert.Multiple(() =>
        {
            AssertVector3(
                pose.GetWorldPosition(0),
                originalWorldPositions[0] + Vector3.UnitX);
            AssertVector3(
                pose.GetWorldPosition(1),
                originalWorldPositions[1] + Vector3.UnitX);
        });

        VertexTransformOperationApplier.ApplyReplayPlan(
            [mesh],
            selection,
            affectedVertexIndices: null,
            falloffWeights: null,
            translatedPlan,
            inverse: true);

        Assert.Multiple(() =>
        {
            AssertVertex(mesh.VertexArray[0], originalVertices[0]);
            AssertVertex(mesh.VertexArray[1], originalVertices[1]);
        });
    }

    [Test]
    public void PausedPoseTransform_MovesVisibleVertexAndKeepsUndoRedoReversible()
    {
        var node = CreatePausedAnimatedNode();
        var mesh = node.Geometry;
        var originalVertex = mesh.VertexArray[0];
        var originalPosePosition =
            MeshPoseSnapshot.Capture(node).GetWorldPosition(0);
        var selection = CreateVertexSelection(node);
        var eventHub = new Mock<IEventHub>();
        var selectionManager = new SelectionManager(
            eventHub.Object);
        selectionManager.SetState(selection);
        var commandExecutor = new CommandExecutor(eventHub.Object);
        var services = new Mock<IServiceProvider>();
        services
            .Setup(provider =>
                provider.GetService(typeof(TransformVertexCommand)))
            .Returns(() =>
                new TransformVertexCommand(selectionManager));
        var commandFactory = new CommandFactory(
            services.Object,
            commandExecutor);
        using var wrapper =
            TransformGizmoWrapper.CreateFromSelectionState(
                selection,
                commandFactory)!;

        wrapper.BeginTransform();
        wrapper.GizmoTranslateEvent(
            new Vector3(1, 0, 0),
            PivotType.ObjectCenter);

        AssertVector3(
            MeshPoseSnapshot.Capture(node).GetWorldPosition(0),
            originalPosePosition + Vector3.UnitX);

        wrapper.CommitTransform(commandExecutor);

        Assert.Multiple(() =>
        {
            AssertVector3(
                MeshPoseSnapshot.Capture(node).GetWorldPosition(0),
                originalPosePosition + Vector3.UnitX);
            Assert.That(
                mesh.GetVertexById(0),
                Is.Not.EqualTo(originalPosePosition + Vector3.UnitX),
                "The paused pose must not replace bind geometry.");
        });

        commandExecutor.Undo();
        AssertVertex(mesh.VertexArray[0], originalVertex);

        commandExecutor.Redo();
        AssertVector3(
            MeshPoseSnapshot.Capture(node).GetWorldPosition(0),
            originalPosePosition + Vector3.UnitX);
    }

    [Test]
    public void StaticWorldTransform_MovesVisibleVertexInWorldSpaceAndSupportsUndo()
    {
        var player = new AnimationPlayer
        {
            IsEnabled = false
        };
        var material = new Mock<IRmvMaterial>();
        material
            .SetupProperty(value => value.ModelName, "test_mesh");
        material
            .SetupProperty(value => value.PivotPoint, Vector3.Zero);
        var mesh = CreateWeightedMesh(new Vector3(1, 0, 0));
        mesh.ChangeVertexType(
            UiVertexFormat.Static,
            updateMesh: false);
        var node = new Rmv2MeshNode(
            mesh,
            material.Object,
            null!,
            player);
        var parent = new GroupNode("parent")
        {
            ModelMatrix =
                Matrix.CreateRotationZ(MathHelper.PiOver2)
        };
        parent.AddObject(node);
        var originalVertex = mesh.VertexArray[0];
        var originalWorldPosition =
            MeshPoseSnapshot.Capture(node).GetWorldPosition(0);
        var selection = CreateVertexSelection(node);
        var eventHub = new Mock<IEventHub>();
        var selectionManager = new SelectionManager(
            eventHub.Object);
        selectionManager.SetState(selection);
        var commandExecutor = new CommandExecutor(eventHub.Object);
        var services = new Mock<IServiceProvider>();
        services
            .Setup(provider =>
                provider.GetService(typeof(TransformVertexCommand)))
            .Returns(() =>
                new TransformVertexCommand(selectionManager));
        var commandFactory = new CommandFactory(
            services.Object,
            commandExecutor);
        using var wrapper =
            TransformGizmoWrapper.CreateFromSelectionState(
                selection,
                commandFactory)!;

        wrapper.BeginTransform();
        wrapper.GizmoTranslateEvent(
            Vector3.UnitX,
            PivotType.ObjectCenter);
        wrapper.CommitTransform(commandExecutor);

        AssertVector3(
            MeshPoseSnapshot.Capture(node).GetWorldPosition(0),
            originalWorldPosition + Vector3.UnitX);

        commandExecutor.Undo();
        AssertVertex(mesh.VertexArray[0], originalVertex);
    }

    private static MeshObject CreateWeightedMesh(Vector3 position)
    {
        var mesh = new MeshObject(
            new TestGraphicsCardGeometry(),
            "test_skeleton")
        {
            VertexArray =
            [
                new VertexPositionNormalTextureCustom
                {
                    Position = new Vector4(position, 1),
                    Normal = Vector3.UnitZ,
                    Tangent = Vector3.UnitX,
                    BiNormal = Vector3.UnitY,
                    BlendIndices = Vector4.Zero,
                    BlendWeights = new Vector4(1, 0, 0, 0)
                }
            ],
            IndexArray = []
        };
        mesh.ChangeVertexType(
            UiVertexFormat.Weighted,
            updateMesh: false);
        mesh.BuildBoundingBox();
        return mesh;
    }

    private static Rmv2MeshNode CreatePausedAnimatedNode()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = new AnimationClip();
        clip.DynamicFrames.Add(
            new AnimationClip.KeyFrame
            {
                Position = [new Vector3(3, 0, 0)],
                Rotation =
                [
                    Quaternion.CreateFromAxisAngle(
                        Vector3.UnitZ,
                        MathHelper.PiOver2)
                ],
                Scale = [Vector3.One]
            });
        clip.DynamicFrames.Add(
            new AnimationClip.KeyFrame
            {
                Position = [new Vector3(5, 0, 0)],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One]
            });
        clip.Duration = TimeSpan.FromSeconds(1);
        player.SetAnimation(clip, skeleton);
        player.IsEnabled = true;
        player.Pause();
        player.Refresh();

        var material = new Mock<IRmvMaterial>();
        material
            .SetupProperty(value => value.ModelName, "test_mesh");
        material
            .SetupProperty(value => value.PivotPoint, Vector3.Zero);
        var node = new Rmv2MeshNode(
            CreateWeightedMesh(new Vector3(1, 0, 0)),
            material.Object,
            null!,
            player);
        var parent = new GroupNode("parent")
        {
            ModelMatrix = Matrix.CreateTranslation(0, 2, 0)
        };
        parent.AddObject(node);
        return node;
    }

    private static GameSkeleton CreateSkeleton(
        AnimationPlayer player,
        Vector3? rootTranslation = null)
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test_skeleton"
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Name = "root",
                    ParentId = -1
                }
            ]
        };
        var skeletonFrame = new AnimationFile.Frame();
        var translation = rootTranslation ?? Vector3.Zero;
        skeletonFrame.Transforms.Add(
            new RmvVector3(
                translation.X,
                translation.Y,
                translation.Z));
        skeletonFrame.Quaternion.Add(
            new RmvVector4(0, 0, 0, 1));
        var skeletonPart = new AnimationFile.AnimationPart();
        skeletonPart.DynamicFrames.Add(skeletonFrame);
        skeletonFile.AnimationParts.Add(skeletonPart);
        return new GameSkeleton(skeletonFile, player);
    }

    private static VertexSelectionState CreateVertexSelection(
        Rmv2MeshNode node)
    {
        return new VertexSelectionState(node, 0)
        {
            SelectedVertices = [0],
            VertexWeights = [1]
        };
    }

    private static void AssertVector3(
        Vector3 actual,
        Vector3 expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Epsilon));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Epsilon));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Epsilon));
        });
    }

    private static void AssertVertex(
        VertexPositionNormalTextureCustom actual,
        VertexPositionNormalTextureCustom expected)
    {
        Assert.Multiple(() =>
        {
            AssertVector3(actual.Position3(), expected.Position3());
            AssertVector3(actual.Normal, expected.Normal);
            AssertVector3(actual.Tangent, expected.Tangent);
            AssertVector3(actual.BiNormal, expected.BiNormal);
        });
    }

    private sealed class TestSelectableNode : SceneNode, ISelectable
    {
        public MeshObject Geometry { get; set; } = null!;
        public bool IsSelectable { get; set; } = true;

        public override ISceneNode CreateCopyInstance()
        {
            return new TestSelectableNode();
        }
    }
}
