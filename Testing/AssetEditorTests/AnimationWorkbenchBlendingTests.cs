using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Moq;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public class AnimationWorkbenchBlendingTests
{
    private const float Tolerance = 0.0001f;

    [DataTestMethod]
    [DataRow(AnimationWorkbenchBlendCurve.Linear, 0.25f)]
    [DataRow(AnimationWorkbenchBlendCurve.Smooth, 0.103515625f)]
    [DataRow(AnimationWorkbenchBlendCurve.EaseInOut, 0.125f)]
    public void PreviewBlend_ThreeCurvesInterpolateLocalTransforms(
        AnimationWorkbenchBlendCurve curve,
        float expectedAmount)
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateConstantClip(4, 4, Vector3.Zero, Quaternion.Identity, Vector3.One),
            CreateConstantClip(
                5,
                4,
                new Vector3(10),
                Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0),
                new Vector3(3)));
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);

        var result = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            3,
            0,
            TimeSpan.FromSeconds(1),
            4,
            curve,
            new AnimationWorkbenchRootMotionOptions(false, false, false)));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(5, result.Impact?.OutputFrameCount);
        var frames = result.State.CurrentPreview!.Animation.DynamicFrames;
        Assert.AreEqual(0, frames[0].Position[0].X, Tolerance);
        var frame = frames[1];
        Assert.AreEqual(10 * expectedAmount, frame.Position[0].X, Tolerance);
        Assert.AreEqual(1 + 2 * expectedAmount, frame.Scale[0].X, Tolerance);
        AssertRotationEqual(
            Quaternion.Slerp(
                Quaternion.Identity,
                Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0),
                expectedAmount),
            frame.Rotation[0]);
    }

    [TestMethod]
    public void PreviewBlend_FullyOverlappedMultiFrameBIsRejected()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateConstantClip(
                4,
                4,
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One),
            CreateConstantClip(
                4,
                4,
                new Vector3(10),
                Quaternion.Identity,
                Vector3.One));
        var result = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            3,
            0,
            TimeSpan.FromSeconds(1),
            4,
            AnimationWorkbenchBlendCurve.Linear,
            new AnimationWorkbenchRootMotionOptions(false, false, false)));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendOverlapConsumesAnimationB,
            result.Diagnostics.Single().Code);
        Assert.IsFalse(result.State.HasActiveBlendPreview);
        Assert.IsFalse(result.State.CanUndo);
    }

    [TestMethod]
    public void PreviewBlend_RootMotionStrategiesCanBeDisabledIndependently()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        var animationA = CreateClip(
            1,
            new RootTransform(
                new Vector3(10, 5, 20),
                Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0)));
        var animationB = CreateClip(
            1,
            new RootTransform(new Vector3(1, 2, 3), Quaternion.Identity),
            new RootTransform(
                new Vector3(3, 4, 7),
                Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver4, 0, 0)));
        using var document = CreateDocument(skeleton, animationA, animationB);
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);

        var defaultResult = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default));
        var defaultFrames = defaultResult.State.CurrentPreview!.Animation.DynamicFrames;
        AssertVectorEqual(new Vector3(10, 5, 20), defaultFrames[1].Position[0]);
        AssertVectorEqual(new Vector3(14, 7, 18), defaultFrames[2].Position[0]);
        AssertRotationEqual(
            Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0),
            defaultFrames[1].Rotation[0]);

        var positionDisabled = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default with
            {
                AlignHorizontalPosition = false,
            }));
        Assert.AreEqual(
            1,
            positionDisabled.State.CurrentPreview!
                .Animation.DynamicFrames[1].Position[0].X,
            Tolerance);

        var yawDisabled = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default with
            {
                AlignYaw = false,
            }));
        AssertRotationEqual(
            Quaternion.Identity,
            yawDisabled.State.CurrentPreview!
                .Animation.DynamicFrames[1].Rotation[0]);

        var heightDisabled = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default with
            {
                PreserveSourceHeightChanges = false,
            }));
        Assert.AreEqual(
            2,
            heightDisabled.State.CurrentPreview!
                .Animation.DynamicFrames[1].Position[0].Y,
            Tolerance);
    }

    [TestMethod]
    public void PreviewBlend_DifferentSourceRatesReportExplicitOutputImpact()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(2, 2),
            CreateMovingClip(3, 3));

        var result = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            1,
            0,
            TimeSpan.FromSeconds(0.25),
            4,
            AnimationWorkbenchBlendCurve.Linear,
            new AnimationWorkbenchRootMotionOptions(false, false, false)));

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Impact);
        Assert.AreEqual(2, result.Impact.AnimationAFramesPerSecond, Tolerance);
        Assert.AreEqual(3, result.Impact.AnimationBFramesPerSecond, Tolerance);
        Assert.AreEqual(4, result.Impact.OutputFramesPerSecond, Tolerance);
        Assert.IsTrue(result.Impact.AnimationAWasResampled);
        Assert.IsTrue(result.Impact.AnimationBWasResampled);
        Assert.AreEqual(1, result.Impact.OverlapFrameCount);
        Assert.AreEqual(7, result.Impact.OutputFrameCount);
        Assert.AreEqual(1.75, result.Impact.OutputDuration.TotalSeconds, 0.000001);
    }

    [DataTestMethod]
    [DataRow(GameTypeEnum.Warhammer3)]
    [DataRow(GameTypeEnum.ThreeKingdoms)]
    public void PreviewBlend_SupportsBothTargetGamesInMemory(
        GameTypeEnum targetGame)
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(2, 20),
            CreateMovingClip(2, 20),
            targetGame: targetGame);

        var result = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            1,
            0,
            TimeSpan.FromMilliseconds(50),
            20,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(targetGame, result.State.TargetGame);
        Assert.AreEqual(3, result.Impact?.OutputFrameCount);
    }

    [TestMethod]
    public void PreviewBlend_EdgeCasesReturnExplicitDiagnostics()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(1, 1),
            CreateMovingClip(2, 2));

        var zeroOverlap = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            0,
            0,
            TimeSpan.Zero,
            2,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsTrue(zeroOverlap.Succeeded);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                AnimationWorkbenchDiagnosticCode.BlendZeroOverlap,
                AnimationWorkbenchDiagnosticCode.BlendSingleFrameSource,
                AnimationWorkbenchDiagnosticCode.BlendLoopSeamDiscontinuity,
            },
            zeroOverlap.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());

        var tinyOverlap = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            0,
            0,
            TimeSpan.FromMilliseconds(10),
            2,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsTrue(tinyOverlap.Succeeded);
        Assert.AreEqual(1, tinyOverlap.Impact?.OverlapFrameCount);
        Assert.IsTrue(tinyOverlap.Diagnostics.Any(
            diagnostic =>
                diagnostic.Code ==
                AnimationWorkbenchDiagnosticCode.BlendOverlapBelowOneFrame));
    }

    [TestMethod]
    public void PreviewBlend_EmptyOrDifferentSkeletonSourceIsRejectedAtomically()
    {
        var target = CreateSkeleton("shared_skeleton", "root");
        using var emptyDocument = CreateDocument(
            target,
            CreateMovingClip(2, 2),
            new AnimationClip());

        var emptyResult = emptyDocument.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsFalse(emptyResult.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendSourceEmpty,
            emptyResult.Diagnostics.Single().Code);
        Assert.AreEqual(2, emptyResult.State.Result?.FrameCount);
        Assert.IsFalse(emptyResult.State.CanUndo);

        using var mismatchDocument = CreateDocument(
            target,
            CreateMovingClip(2, 2),
            CreateMovingClip(2, 2),
            CreateSkeleton("different_skeleton", "other_root"));
        var mismatchResult = mismatchDocument.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsFalse(mismatchResult.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendSkeletonMismatch,
            mismatchResult.Diagnostics.Single().Code);
        Assert.IsFalse(mismatchResult.State.CanUndo);
    }

    [TestMethod]
    public void PreviewBlend_FailureClearsPreviousPreviewAndRestoresCommittedResult()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(2, 2),
            CreateMovingClip(2, 2));
        var valid = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default));

        var invalid = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            0,
            0,
            TimeSpan.FromSeconds(2),
            2,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsTrue(valid.Succeeded);
        Assert.IsFalse(invalid.Succeeded);
        Assert.IsFalse(invalid.State.HasActiveBlendPreview);
        Assert.AreEqual(2, invalid.State.Result?.FrameCount);
        Assert.AreEqual(
            2,
            invalid.State.CurrentPreview?.Animation.DynamicFrames.Count);
        Assert.IsFalse(invalid.State.CanUndo);
    }

    [TestMethod]
    public void PreviewBlend_SameNamesWithDifferentRestPoseAreRejected()
    {
        var target = CreateSkeleton("shared_skeleton", "root");
        var differentRestPose = CreateSkeleton("shared_skeleton", "root");
        differentRestPose.Translation[0] = new Vector3(0.25f, 0, 0);
        differentRestPose.RebuildSkeletonMatrix();
        using var document = CreateDocument(
            target,
            CreateMovingClip(2, 2),
            CreateMovingClip(2, 2),
            differentRestPose);

        var result = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendSkeletonMismatch,
            result.Diagnostics.Single().Code);
        Assert.IsFalse(result.State.HasActiveBlendPreview);
    }

    [TestMethod]
    public void PreviewBlend_ExtremeFiniteTransformsCannotCreateInvalidOutput()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        var extreme = CreateClip(
            2,
            new RootTransform(
                new Vector3(float.MaxValue, 0, 0),
                Quaternion.Identity),
            new RootTransform(
                new Vector3(-float.MaxValue, 0, 0),
                Quaternion.Identity));
        using var document = CreateDocument(
            skeleton,
            extreme,
            CreateMovingClip(2, 2));

        var result = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            1,
            0,
            TimeSpan.Zero,
            4,
            AnimationWorkbenchBlendCurve.Linear,
            new AnimationWorkbenchRootMotionOptions(false, false, false)));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendResultTransformInvalid,
            result.Diagnostics.Single().Code);
        Assert.IsFalse(result.State.HasActiveBlendPreview);
        Assert.IsFalse(result.State.CanUndo);
    }

    [TestMethod]
    public void PreviewBlend_OverflowingQuaternionIsRejectedAsInvalidSource()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        var invalidRotation = CreateMovingClip(2, 2);
        invalidRotation.DynamicFrames[0].Rotation[0] = new Quaternion(
            float.MaxValue,
            float.MaxValue,
            float.MaxValue,
            float.MaxValue);
        using var document = CreateDocument(
            skeleton,
            invalidRotation,
            CreateMovingClip(2, 2));

        var result = document.PreviewBlend(CreateRootRequest(
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendSourceTransformInvalid,
            result.Diagnostics.Single().Code);
        Assert.IsFalse(result.State.HasActiveBlendPreview);
    }

    [TestMethod]
    public void CommitBlend_PreviewsUndoRedoSaveAndReopenAsOneAtomicEdit()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(2, 2),
            CreateMovingClip(2, 2),
            sourceFormat: new AnimationWorkbenchSourceFormat(7, 1));
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var preview = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            1,
            0,
            TimeSpan.Zero,
            2,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default));

        Assert.IsTrue(preview.Succeeded);
        Assert.IsTrue(preview.State.HasActiveBlendPreview);
        Assert.AreEqual(4, preview.State.CurrentPreview?.Animation.DynamicFrames.Count);
        using var blockedCopy = new TemporaryDirectory();
        var blockedSave = document.ExportDiskCopy(
            Path.Combine(blockedCopy.Path, "preview.anim"));
        Assert.IsFalse(blockedSave.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.BlendPreviewAlreadyActive,
            blockedSave.Diagnostics.Single().Code);

        var committed = document.CommitBlendPreview();
        Assert.IsTrue(committed.Succeeded);
        Assert.IsFalse(committed.State.HasActiveBlendPreview);
        Assert.IsTrue(committed.State.CanUndo);
        Assert.IsTrue(committed.State.IsDirty);
        Assert.AreEqual(4, committed.State.Result?.FrameCount);

        Assert.IsTrue(document.Undo().Succeeded);
        Assert.AreEqual(2, document.GetState().Result?.FrameCount);
        Assert.IsTrue(document.Redo().Succeeded);
        Assert.AreEqual(4, document.GetState().Result?.FrameCount);

        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "融合测试工程" });
        var packFileService = new Mock<IPackFileService>();
        List<NewPackFileEntry>? capturedEntries = null;
        packFileService
            .Setup(service => service.AddFilesToPack(
                project,
                It.IsAny<List<NewPackFileEntry>>(),
                false))
            .Callback<PackFileContainer, List<NewPackFileEntry>, bool>(
                (_, entries, _) => capturedEntries = entries);

        var save = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\battle\blend_candidate.anim");

        Assert.IsTrue(save.Succeeded);
        Assert.IsFalse(save.State.IsDirty);
        var reopenedFile = AnimationFile.Create(new ByteChunk(
            capturedEntries!.Single().PackFile.DataSource.ReadData()));
        var reopenedClip = new AnimationClip(reopenedFile, skeleton);
        Assert.AreEqual(4, reopenedClip.DynamicFrames.Count);
        Assert.AreEqual(
            committed.State.Result?.Duration,
            reopenedClip.Duration);
        packFileService.VerifyAll();
    }

    [TestMethod]
    public void BlendController_UsesAnimationAFpsAndRefreshesPreviewOnChanges()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(6, 30),
            CreateMovingClip(4, 20));

        var controller = new AnimationWorkbenchBlendController(document);

        Assert.AreEqual(5, controller.AnimationAOutFrame);
        Assert.AreEqual(0, controller.AnimationBInFrame);
        Assert.AreEqual(30, controller.OutputFramesPerSecond, Tolerance);
        Assert.IsTrue(controller.AlignHorizontalPosition);
        Assert.IsTrue(controller.AlignYaw);
        Assert.IsTrue(controller.PreserveSourceHeightChanges);
        Assert.IsTrue(controller.HasActivePreview);
        Assert.IsTrue(controller.CanCommit);

        controller.Curve = AnimationWorkbenchBlendCurve.EaseInOut;
        controller.OutputFramesPerSecond = 24;

        Assert.AreEqual(AnimationWorkbenchBlendCurve.EaseInOut, controller.Curve);
        Assert.AreEqual(
            24,
            controller.Impact!.OutputFramesPerSecond,
            Tolerance);
        Assert.IsTrue(controller.Impact.AnimationAWasResampled);
        Assert.IsTrue(controller.Impact.AnimationBWasResampled);
        Assert.IsTrue(controller.CommitPreview().Succeeded);
        Assert.IsFalse(controller.HasActivePreview);
        Assert.IsTrue(document.GetState().CanUndo);
    }

    [TestMethod]
    public void BlendController_ReleasePreviewRestoresCommittedResult()
    {
        var skeleton = CreateSkeleton("shared_skeleton", "root");
        using var document = CreateDocument(
            skeleton,
            CreateMovingClip(2, 2),
            CreateMovingClip(2, 2));
        var controller = new AnimationWorkbenchBlendController(document);

        var released = controller.ReleasePreview();

        Assert.IsTrue(released?.Succeeded);
        Assert.IsFalse(controller.HasActivePreview);
        Assert.IsFalse(controller.CanCommit);
        Assert.AreEqual(2, document.GetState().Result?.FrameCount);
        Assert.IsFalse(document.GetState().CanUndo);
    }

    private static AnimationWorkbenchBlendRequest CreateRootRequest(
        AnimationWorkbenchRootMotionOptions options) => new(
        0,
        0,
        TimeSpan.Zero,
        1,
        AnimationWorkbenchBlendCurve.Smooth,
        options);

    private static AnimationWorkbenchDocument CreateDocument(
        GameSkeleton targetSkeleton,
        AnimationClip animationA,
        AnimationClip animationB,
        GameSkeleton? animationBSkeleton = null,
        AnimationWorkbenchSourceFormat? sourceFormat = null,
        GameTypeEnum targetGame = GameTypeEnum.Warhammer3)
    {
        var format = sourceFormat ?? new AnimationWorkbenchSourceFormat(7, 1);
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animationA,
                targetSkeleton,
                format),
            new AnimationWorkbenchSourceInput(
                "animation_b",
                animationB,
                animationBSkeleton ?? targetSkeleton,
                format),
            targetGame,
            targetSkeleton));
        return document;
    }

    private static AnimationClip CreateMovingClip(int frameCount, double fps)
    {
        var transforms = Enumerable.Range(0, frameCount)
            .Select(index => new RootTransform(
                new Vector3(index, 0, 0),
                Quaternion.Identity))
            .ToArray();
        return CreateClip(fps, transforms);
    }

    private static AnimationClip CreateConstantClip(
        int frameCount,
        double fps,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        var clip = CreateClip(
            fps,
            Enumerable.Repeat(
                new RootTransform(position, rotation),
                frameCount).ToArray());
        foreach (var frame in clip.DynamicFrames)
            frame.Scale[0] = scale;
        return clip;
    }

    private static AnimationClip CreateClip(
        double fps,
        params RootTransform[] transforms)
    {
        var clip = new AnimationClip
        {
            Duration = transforms.Length == 0
                ? TimeSpan.Zero
                : AnimationTimebase.FromFramesPerSecond(
                    transforms.Length,
                    fps).Duration,
        };
        foreach (var transform in transforms)
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.Add(transform.Position);
            frame.Rotation.Add(transform.Rotation);
            frame.Scale.Add(Vector3.One);
            clip.DynamicFrames.Add(frame);
        }
        return clip;
    }

    private static GameSkeleton CreateSkeleton(
        string skeletonName,
        string boneName)
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = skeletonName,
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = boneName,
                    ParentId = AnimationFile.BoneIndexNoParent,
                },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts.Add(part);
        return new GameSkeleton(skeletonFile, new AnimationPlayer());
    }

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, Tolerance);
        Assert.AreEqual(expected.Y, actual.Y, Tolerance);
        Assert.AreEqual(expected.Z, actual.Z, Tolerance);
    }

    private static void AssertRotationEqual(
        Quaternion expected,
        Quaternion actual)
    {
        expected.Normalize();
        actual.Normalize();
        Assert.AreEqual(1, MathF.Abs(Quaternion.Dot(expected, actual)), Tolerance);
    }

    private sealed record RootTransform(Vector3 Position, Quaternion Rotation);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"animation-workbench-blend-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
