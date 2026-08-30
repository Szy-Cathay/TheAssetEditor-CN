using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public class AnimationWorkbenchDocumentTests
{
    [TestMethod]
    public void Load_WithAnimationAAndCompleteTarget_ReturnsReadyPreviewState()
    {
        using var document = new AnimationWorkbenchDocument();
        var sourceSkeleton = CreateSkeleton("source_skeleton", "root");
        var targetSkeleton = CreateSkeleton("target_skeleton", "root");

        var state = document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(new Vector3(1, 2, 3)),
                sourceSkeleton),
            null,
            GameTypeEnum.Warhammer3,
            targetSkeleton));

        Assert.AreEqual("animation_a", state.AnimationA?.Name);
        Assert.AreEqual(1, state.AnimationA?.FrameCount);
        Assert.AreEqual(GameTypeEnum.Warhammer3, state.TargetGame);
        Assert.AreEqual("target_skeleton", state.TargetSkeleton?.Name);
        Assert.AreEqual(AnimationWorkbenchPreviewKind.AnimationA, state.SelectedPreview);
        Assert.IsNotNull(state.CurrentPreview);
        Assert.AreEqual(0, state.Diagnostics.Count);
    }

    [TestMethod]
    public void Load_WithoutRequiredTarget_ReturnsStructuredDiagnostics()
    {
        using var document = new AnimationWorkbenchDocument();
        var sourceSkeleton = CreateSkeleton("source_skeleton", "root");

        var state = document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(Vector3.Zero),
                sourceSkeleton),
            null,
            null,
            null));

        CollectionAssert.AreEquivalent(
            new[]
            {
                AnimationWorkbenchDiagnosticCode.TargetGameMissing,
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing,
            },
            state.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());
        Assert.IsTrue(state.Diagnostics.All(
            diagnostic =>
                diagnostic.Severity ==
                AnimationWorkbenchDiagnosticSeverity.Warning));
    }

    [TestMethod]
    public void Load_WithUnsupportedTargetGame_ReturnsErrorDiagnostic()
    {
        using var document = new AnimationWorkbenchDocument();
        var skeleton = CreateSkeleton("source_skeleton", "root");

        var state = document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(Vector3.Zero),
                skeleton),
            null,
            GameTypeEnum.Rome2,
            skeleton));

        Assert.AreEqual(1, state.Diagnostics.Count);
        var diagnostic = state.Diagnostics[0];
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TargetGameUnsupported,
            diagnostic.Code);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticSeverity.Error,
            diagnostic.Severity);
    }

    [TestMethod]
    public void Load_WithoutAnimationA_ReturnsMissingSourceDiagnostic()
    {
        using var document = new AnimationWorkbenchDocument();
        var targetSkeleton = CreateSkeleton("target_skeleton", "root");

        var state = document.Load(new AnimationWorkbenchLoadRequest(
            null,
            null,
            GameTypeEnum.Warhammer3,
            targetSkeleton));

        Assert.AreEqual(1, state.Diagnostics.Count);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.AnimationAMissing,
            state.Diagnostics[0].Code);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticSeverity.Warning,
            state.Diagnostics[0].Severity);
        Assert.IsNull(state.SelectedPreview);
        Assert.IsNull(state.CurrentPreview);
        Assert.IsNull(state.Result);
    }

    [TestMethod]
    public void Load_WithSourceBoneCountMismatch_ReturnsSourceErrorDiagnostic()
    {
        using var document = new AnimationWorkbenchDocument();
        var skeleton = CreateSkeleton("source_skeleton", "root");

        var state = document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(Vector3.Zero, Vector3.One),
                skeleton),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));

        Assert.AreEqual(1, state.Diagnostics.Count);
        var diagnostic = state.Diagnostics[0];
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.SourceSkeletonBoneCountMismatch,
            diagnostic.Code);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticSeverity.Error,
            diagnostic.Severity);
        Assert.AreEqual(AnimationWorkbenchSourceSlot.AnimationA, diagnostic.Source);
        Assert.AreEqual(1, diagnostic.ExpectedValue);
        Assert.AreEqual(2, diagnostic.ActualValue);
    }

    [TestMethod]
    public void SelectPreview_ReturnsIndependentAnimationAAnimationBAndResult()
    {
        using var document = new AnimationWorkbenchDocument();
        var skeleton = CreateSkeleton("source_skeleton", "root");
        var animationA = CreateClip(new Vector3(10, 0, 0));
        var animationB = CreateClip(new Vector3(20, 0, 0));

        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animationA,
                skeleton),
            new AnimationWorkbenchSourceInput(
                "animation_b",
                animationB,
                skeleton),
            GameTypeEnum.ThreeKingdoms,
            skeleton));

        animationA.DynamicFrames[0].Position[0] = new Vector3(99, 0, 0);
        skeleton.BoneNames[0] = "changed_root";

        var previewA = document.SelectPreview(
            AnimationWorkbenchPreviewKind.AnimationA).CurrentPreview!;
        var previewB = document.SelectPreview(
            AnimationWorkbenchPreviewKind.AnimationB).CurrentPreview!;
        previewB.Animation.DynamicFrames[0].Position[0] =
            new Vector3(88, 0, 0);
        previewB.Skeleton.BoneNames[0] = "changed_preview_root";
        var previewBAgain = document.SelectPreview(
            AnimationWorkbenchPreviewKind.AnimationB).CurrentPreview!;
        var result = document.SelectPreview(
            AnimationWorkbenchPreviewKind.Result).CurrentPreview!;

        Assert.AreEqual(10, previewA.Animation.DynamicFrames[0].Position[0].X);
        Assert.AreEqual("root", previewA.Skeleton.BoneNames[0]);
        Assert.AreNotSame(
            skeleton.AnimationPlayer,
            previewA.Skeleton.AnimationPlayer);
        Assert.AreEqual(20, previewBAgain.Animation.DynamicFrames[0].Position[0].X);
        Assert.AreEqual("root", previewBAgain.Skeleton.BoneNames[0]);
        Assert.AreEqual(10, result.Animation.DynamicFrames[0].Position[0].X);
        Assert.AreEqual("root", result.Skeleton.BoneNames[0]);
        Assert.AreNotSame(
            previewA.Skeleton.AnimationPlayer,
            result.Skeleton.AnimationPlayer);
    }

    [TestMethod]
    public void ReloadAndClose_CancelPendingPreviewAndReleasePreviewHost()
    {
        var previewHost = new RecordingPreviewHost();
        var document = new AnimationWorkbenchDocument(previewHost);
        var skeleton = CreateSkeleton("source_skeleton", "root");
        var request = new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(new Vector3(1, 0, 0)),
                skeleton),
            new AnimationWorkbenchSourceInput(
                "animation_b",
                CreateClip(new Vector3(2, 0, 0)),
                skeleton),
            GameTypeEnum.Warhammer3,
            skeleton);

        document.Load(request);
        var loadToken = previewHost.CurrentCancellationToken;

        document.SelectPreview(AnimationWorkbenchPreviewKind.AnimationB);
        var selectionToken = previewHost.CurrentCancellationToken;

        Assert.IsTrue(loadToken.IsCancellationRequested);
        Assert.AreEqual(
            AnimationWorkbenchPreviewKind.AnimationB,
            previewHost.CurrentPreview?.Kind);

        var replacementSourceSkeleton = CreateSkeleton(
            "replacement_source_skeleton",
            "replacement_root");
        var replacementTargetSkeleton = CreateSkeleton(
            "replacement_target_skeleton",
            "replacement_root");
        var replacementRequest = new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "replacement_animation_a",
                CreateClip(new Vector3(3, 0, 0)),
                replacementSourceSkeleton),
            null,
            GameTypeEnum.ThreeKingdoms,
            replacementTargetSkeleton);

        var reloadedState = document.Load(replacementRequest);
        var reloadToken = previewHost.CurrentCancellationToken;

        Assert.IsTrue(selectionToken.IsCancellationRequested);
        Assert.AreEqual("replacement_animation_a", reloadedState.AnimationA?.Name);
        Assert.IsNull(reloadedState.AnimationB);
        Assert.AreEqual(
            GameTypeEnum.ThreeKingdoms,
            reloadedState.TargetGame);
        Assert.AreEqual(
            "replacement_target_skeleton",
            reloadedState.TargetSkeleton?.Name);
        Assert.AreEqual(
            "replacement_animation_a",
            reloadedState.Result?.Name);
        Assert.AreEqual(
            AnimationWorkbenchPreviewKind.AnimationA,
            previewHost.CurrentPreview?.Kind);

        var closedState = document.Close();

        Assert.IsTrue(reloadToken.IsCancellationRequested);
        Assert.IsTrue(previewHost.IsDisposed);
        Assert.IsNull(previewHost.CurrentPreview);
        Assert.IsTrue(closedState.IsClosed);
        Assert.IsNull(closedState.CurrentPreview);
        Assert.AreEqual(0, closedState.Diagnostics.Count);
    }

    [TestMethod]
    public void Load_WhenReplacementCannotBePrepared_KeepsCurrentPreviewActive()
    {
        var previewHost = new RecordingPreviewHost();
        using var document = new AnimationWorkbenchDocument(previewHost);
        var skeleton = CreateSkeleton("source_skeleton", "root");
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                CreateClip(Vector3.Zero),
                skeleton),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));
        var currentToken = previewHost.CurrentCancellationToken;

        Assert.ThrowsException<ArgumentException>(() =>
            document.Load(new AnimationWorkbenchLoadRequest(
                new AnimationWorkbenchSourceInput(
                    " ",
                    CreateClip(Vector3.One),
                    skeleton),
                null,
                GameTypeEnum.ThreeKingdoms,
                skeleton)));

        Assert.IsFalse(currentToken.IsCancellationRequested);
        Assert.AreEqual("animation_a", previewHost.CurrentPreview?.Name);
        Assert.AreEqual(
            GameTypeEnum.Warhammer3,
            document.SelectPreview(
                AnimationWorkbenchPreviewKind.AnimationA).TargetGame);
    }

    private static AnimationClip CreateClip(params Vector3[] positions)
    {
        var frame = new AnimationClip.KeyFrame();
        foreach (var position in positions)
        {
            frame.Position.Add(position);
            frame.Rotation.Add(Quaternion.Identity);
            frame.Scale.Add(Vector3.One);
        }

        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(0.05),
        };
        clip.DynamicFrames.Add(frame);
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

    private sealed class RecordingPreviewHost : IAnimationWorkbenchPreviewHost
    {
        public AnimationWorkbenchPreviewSnapshot? CurrentPreview { get; private set; }

        public CancellationToken CurrentCancellationToken { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Show(
            AnimationWorkbenchPreviewSnapshot preview,
            CancellationToken cancellationToken)
        {
            CurrentPreview = preview;
            CurrentCancellationToken = cancellationToken;
        }

        public void Clear()
        {
            CurrentPreview = null;
        }

        public void Dispose()
        {
            Clear();
            IsDisposed = true;
        }
    }
}
