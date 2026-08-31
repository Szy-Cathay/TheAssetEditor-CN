using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public class AnimationWorkbenchLayeringTests
{
    private const float Tolerance = 0.0001f;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PreviewLayer_OverrideSubtreeKeepsAnimationAOutsideMask()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip(
                [new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(3, 0, 0)]),
            CreateClip(
                [new Vector3(10, 0, 0), new Vector3(20, 0, 0), new Vector3(30, 0, 0)]));
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);

        var mask = document.CreateBoneMask(["spine"], includeDescendants: true);
        var result = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsTrue(mask.Succeeded);
        CollectionAssert.AreEqual(
            new[] { "spine", "hand_r" },
            mask.Mask!.BoneNames.ToArray());
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.State.HasActiveLayerPreview);
        var frame = result.State.CurrentPreview!.Animation.DynamicFrames[0];
        Assert.AreEqual(1, frame.Position[0].X, Tolerance);
        Assert.AreEqual(20, frame.Position[1].X, Tolerance);
        Assert.AreEqual(30, frame.Position[2].X, Tolerance);
        Assert.IsFalse(result.State.CanUndo);
    }

    [TestMethod]
    public void PreviewLayer_AdditiveUsesExplicitFirstFrameReferenceAndWeight()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateTwoFrameClip(
                [new Vector3(1), new Vector3(10), new Vector3(20)],
                [new Vector3(1), new Vector3(10), new Vector3(20)],
                Quaternion.Identity),
            CreateTwoFrameClip(
                [Vector3.Zero, new Vector3(2), new Vector3(4)],
                [Vector3.Zero, new Vector3(6), new Vector3(10)],
                Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0)));
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var mask = document.CreateBoneMask(["spine"], includeDescendants: true);

        var result = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Additive,
            0.5,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsTrue(result.Succeeded);
        var frames = result.State.CurrentPreview!.Animation.DynamicFrames;
        Assert.AreEqual(10, frames[0].Position[1].X, Tolerance);
        Assert.AreEqual(20, frames[0].Position[2].X, Tolerance);
        Assert.AreEqual(12, frames[1].Position[1].X, Tolerance);
        Assert.AreEqual(23, frames[1].Position[2].X, Tolerance);
        AssertRotationEqual(
            Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver4, 0, 0),
            frames[1].Rotation[1]);
        AssertRotationEqual(Quaternion.Identity, frames[1].Rotation[0]);
    }

    [TestMethod]
    public void CommitLayerPreview_IsOneUndoableHistoryStep()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip(
                [new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(3, 0, 0)]),
            CreateClip(
                [new Vector3(10, 0, 0), new Vector3(20, 0, 0), new Vector3(30, 0, 0)]));
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var mask = document.CreateBoneMask(["spine"], includeDescendants: true);

        var preview = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));
        var committed = document.CommitLayerPreview();
        var undone = document.Undo();
        var redone = document.Redo();

        Assert.IsTrue(preview.Succeeded);
        Assert.IsFalse(preview.State.CanUndo);
        Assert.IsTrue(committed.Succeeded);
        Assert.IsTrue(committed.State.IsDirty);
        Assert.IsTrue(committed.State.CanUndo);
        Assert.IsFalse(committed.State.HasActiveLayerPreview);
        Assert.IsTrue(undone.Succeeded);
        Assert.AreEqual(
            2,
            undone.State.CurrentPreview!.Animation.DynamicFrames[0]
                .Position[1].X,
            Tolerance);
        Assert.IsTrue(redone.Succeeded);
        Assert.AreEqual(
            20,
            redone.State.CurrentPreview!.Animation.DynamicFrames[0]
                .Position[1].X,
            Tolerance);
    }

    [TestMethod]
    public void PreviewLayer_UnmappedMaskedBoneIsRejectedWithoutHistory()
    {
        var targetSkeleton = CreateSkeleton();
        var sourceBSkeleton = CreateSkeleton("root", "pelvis", "hand_r");
        using var document = CreateDocument(
            targetSkeleton,
            CreateClip(
                [new Vector3(1), new Vector3(2), new Vector3(3)]),
            CreateClip(
                [new Vector3(10), new Vector3(20), new Vector3(30)]),
            sourceBSkeleton);
        var mask = document.CreateBoneMask(["spine"], includeDescendants: false);

        var result = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskBoneUnmapped,
            result.Diagnostics.Single().Code);
        Assert.AreEqual("spine", result.Diagnostics.Single().BoneName);
        Assert.IsFalse(result.State.HasActiveLayerPreview);
        Assert.IsFalse(result.State.CanUndo);
    }

    [TestMethod]
    public void CreateBoneMask_ReportsMissingAndDuplicateRootsExplicitly()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]));

        var missing = document.CreateBoneMask(
            ["missing"],
            includeDescendants: true);
        var duplicate = document.CreateBoneMask(
            ["spine", "spine"],
            includeDescendants: false);

        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskBoneMissing,
            missing.Diagnostics.Single().Code);
        Assert.AreEqual("missing", missing.Diagnostics.Single().BoneName);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
            duplicate.Diagnostics.Single().Code);
        Assert.AreEqual("spine", duplicate.Diagnostics.Single().BoneName);
    }

    [TestMethod]
    public void PreviewLayer_InvalidRequestClearsStalePreviewWithoutHistory()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var mask = document.CreateBoneMask(["root"], includeDescendants: true);
        var valid = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        var invalid = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            double.NaN,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsTrue(valid.Succeeded);
        Assert.IsFalse(invalid.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerWeightInvalid,
            invalid.Diagnostics.Single().Code);
        Assert.IsFalse(invalid.State.HasActiveLayerPreview);
        Assert.IsFalse(invalid.State.CanUndo);
    }

    [TestMethod]
    public void PreviewLayer_WorldTransformOverflowIsRejectedAtomically()
    {
        var skeleton = CreateSkeleton();
        var animationA = CreateClip(
            [Vector3.Zero, new Vector3(1e30f), Vector3.Zero]);
        animationA.DynamicFrames[0].Scale[0] = new Vector3(1e30f);
        using var document = CreateDocument(
            skeleton,
            animationA,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.One]));
        var historyRevision = document.GetState().HistoryRevision;
        var mask = document.CreateBoneMask(
            ["hand_r"],
            includeDescendants: true);

        var result = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.State.HasActiveLayerPreview);
        Assert.AreEqual(historyRevision, result.State.HistoryRevision);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode
                .LayerResultWorldTransformInvalid,
            result.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void PreviewLayer_RejectsUnknownModeAndReferencePose()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var mask = document.CreateBoneMask(["root"], includeDescendants: false);

        var mode = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            (AnimationWorkbenchLayerMode)99,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));
        var reference = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Additive,
            1,
            (AnimationWorkbenchAdditiveReferencePose)99));

        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerModeInvalid,
            mode.Diagnostics.Single().Code);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerReferencePoseInvalid,
            reference.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void OtherEditsAreBlockedWhileLayerPreviewIsActive()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var mask = document.CreateBoneMask(["root"], includeDescendants: true);
        var preview = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        var pose = document.BeginPosePreview(0);
        var timeline = document.BeginTimelinePreview();
        var undo = document.Undo();

        Assert.IsTrue(preview.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerPreviewAlreadyActive,
            pose.Diagnostics.Single().Code);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerPreviewAlreadyActive,
            timeline.Diagnostics.Single().Code);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerPreviewAlreadyActive,
            undo.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void BoneMaskStore_SavesListsAndLoadsMaskForExactSkeleton()
    {
        var filePath = Path.Combine(
            TestContext.TestRunDirectory!,
            $"animation-workbench-mask-{Guid.NewGuid():N}.json");
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var hierarchy = document.GetBoneHierarchy();
        var mask = document.CreateBoneMask(["spine"], includeDescendants: true);
        var store = AnimationWorkbenchBoneMaskStore.CreateForFile(filePath);

        var saved = store.Save("上半身", hierarchy, mask.Mask!);
        var listed = store.List(hierarchy);
        var loaded = store.Load("上半身", hierarchy);

        Assert.IsTrue(saved.Succeeded);
        Assert.IsTrue(listed.Succeeded);
        CollectionAssert.AreEqual(
            new[] { "上半身" },
            listed.MaskNames.ToArray());
        Assert.IsTrue(loaded.Succeeded);
        CollectionAssert.AreEqual(
            new[] { "spine", "hand_r" },
            loaded.Mask!.BoneNames.ToArray());
    }

    [TestMethod]
    public void BoneMaskStore_DoesNotLoadMaskForDifferentSkeletonFingerprint()
    {
        var filePath = Path.Combine(
            TestContext.TestRunDirectory!,
            $"animation-workbench-mask-{Guid.NewGuid():N}.json");
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var store = AnimationWorkbenchBoneMaskStore.CreateForFile(filePath);
        var hierarchy = document.GetBoneHierarchy();
        var mask = document.CreateBoneMask(["spine"], includeDescendants: true);
        Assert.IsTrue(store.Save("上半身", hierarchy, mask.Mask!).Succeeded);

        var changedSkeleton = CreateSkeleton("root", "spine", "weapon_r");
        using var changedDocument = CreateDocument(
            changedSkeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var loaded = store.Load(
            "上半身",
            changedDocument.GetBoneHierarchy());

        Assert.IsFalse(loaded.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskSavedSkeletonMismatch,
            loaded.Diagnostics.Single().Code);
        Assert.IsNull(loaded.Mask);
    }

    [TestMethod]
    public void BoneMaskStore_InvalidPathsReturnDiagnosticsAndCleanTemporaryFile()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var hierarchy = document.GetBoneHierarchy();
        var invalidRead = AnimationWorkbenchBoneMaskStore
            .CreateForFile("invalid\0path.json")
            .List(hierarchy);
        var targetDirectory = Path.Combine(
            TestContext.TestRunDirectory!,
            $"mask-store-directory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);
        var store = AnimationWorkbenchBoneMaskStore.CreateForFile(
            targetDirectory);
        var mask = document.CreateBoneMask(
            ["spine"],
            includeDescendants: true);

        var invalidWrite = store.Save("上半身", hierarchy, mask.Mask!);

        Assert.IsFalse(invalidRead.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskStoreReadFailed,
            invalidRead.Diagnostics.Single().Code);
        Assert.IsFalse(invalidWrite.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskStoreWriteFailed,
            invalidWrite.Diagnostics.Single().Code);
        Assert.IsFalse(Directory.EnumerateFiles(
            TestContext.TestRunDirectory!,
            $"{Path.GetFileName(targetDirectory)}.*.tmp").Any());
    }

    [TestMethod]
    public void LayerController_CorruptMaskStoreRemainsVisibleAfterFirstPreview()
    {
        var filePath = Path.Combine(
            TestContext.TestRunDirectory!,
            $"animation-workbench-mask-{Guid.NewGuid():N}.json");
        File.WriteAllText(filePath, "{ invalid json");
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));

        var controller = new AnimationWorkbenchLayerController(
            document,
            AnimationWorkbenchBoneMaskStore.CreateForFile(filePath));

        Assert.IsTrue(controller.HasActivePreview);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerMaskStoreReadFailed,
            controller.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void LayerController_ReloadInvalidatesOldHierarchyAndMaskStore()
    {
        var originalSkeleton = CreateSkeleton();
        using var document = CreateDocument(
            originalSkeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var maskPath = Path.Combine(
            TestContext.TestRunDirectory!,
            $"layer-reload-mask-{Guid.NewGuid():N}.json");
        var controller = new AnimationWorkbenchLayerController(
            document,
            AnimationWorkbenchBoneMaskStore.CreateForFile(maskPath));
        var replacementSkeleton = CreateSkeleton(
            "root",
            "spine",
            "hand_r",
            rootTranslationX: 3);
        var replacementFormat = new AnimationWorkbenchSourceFormat(7, 1);
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "replacement_a",
                CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
                replacementSkeleton,
                replacementFormat),
            new AnimationWorkbenchSourceInput(
                "replacement_b",
                CreateClip([Vector3.One, Vector3.One, Vector3.One]),
                replacementSkeleton,
                replacementFormat),
            GameTypeEnum.Warhammer3,
            replacementSkeleton));

        var preview = controller.RefreshPreview();
        var saved = controller.SaveMask("旧骨架");

        Assert.IsFalse(preview.Succeeded);
        Assert.IsFalse(preview.State.HasActiveLayerPreview);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerDocumentChanged,
            preview.Diagnostics.Single().Code);
        Assert.IsFalse(saved.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerDocumentChanged,
            saved.Diagnostics.Single().Code);
        Assert.IsFalse(File.Exists(maskPath));
    }

    [TestMethod]
    public void LayerController_SelectingBoneChangesItsSubtreeAndLivePreview()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([new Vector3(1), new Vector3(2), new Vector3(3)]),
            CreateClip([new Vector3(10), new Vector3(20), new Vector3(30)]));
        var controller = new AnimationWorkbenchLayerController(document);

        controller.RootBones.Single().Children.Single().IsSelected = false;

        Assert.AreEqual(1, controller.SelectedBoneCount);
        Assert.IsTrue(controller.HasActivePreview);
        Assert.AreEqual(1, controller.Impact!.MaskedBoneCount);
        var frame = controller.LastResult!.State.CurrentPreview!
            .Animation.DynamicFrames[0];
        Assert.AreEqual(10, frame.Position[0].X, Tolerance);
        Assert.AreEqual(2, frame.Position[1].X, Tolerance);
        Assert.AreEqual(3, frame.Position[2].X, Tolerance);
    }

    [TestMethod]
    public void LayerController_SearchKeepsMatchingBoneAndAncestorsVisible()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var controller = new AnimationWorkbenchLayerController(document);

        controller.SearchText = "HAND";

        var root = controller.RootBones.Single();
        var spine = root.Children.Single();
        var hand = spine.Children.Single();
        Assert.IsTrue(root.IsVisible);
        Assert.IsTrue(spine.IsVisible);
        Assert.IsTrue(hand.IsVisible);
        Assert.IsTrue(root.IsExpanded);
        Assert.IsTrue(spine.IsExpanded);
        Assert.AreEqual(3, controller.SelectedBoneCount);
    }

    [TestMethod]
    public void LayerController_SavedMaskRestoresExactSelection()
    {
        var filePath = Path.Combine(
            TestContext.TestRunDirectory!,
            $"animation-workbench-mask-{Guid.NewGuid():N}.json");
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var controller = new AnimationWorkbenchLayerController(
            document,
            AnimationWorkbenchBoneMaskStore.CreateForFile(filePath));
        controller.RootBones.Single().Children.Single().IsSelected = false;

        var saved = controller.SaveMask("仅根骨");
        controller.RootBones.Single().Children.Single().IsSelected = true;
        var loaded = controller.LoadMask("仅根骨");

        Assert.IsTrue(saved.Succeeded);
        Assert.IsTrue(loaded.Succeeded);
        Assert.AreEqual(1, controller.SelectedBoneCount);
        CollectionAssert.AreEqual(
            new[] { "仅根骨" },
            controller.SavedMaskNames.ToArray());
        Assert.AreEqual(1, controller.Impact!.MaskedBoneCount);
    }

    [TestMethod]
    public void LayerController_StaleControllerCannotCommitNewerPreview()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]));
        var first = new AnimationWorkbenchLayerController(document);
        var second = new AnimationWorkbenchLayerController(document);

        var staleCommit = first.CommitPreview();

        Assert.IsFalse(staleCommit.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerPreviewMissing,
            staleCommit.Diagnostics.Single().Code);
        Assert.IsTrue(second.HasActivePreview);
        Assert.IsFalse(document.GetState().CanUndo);
    }

    [TestMethod]
    public void PreviewLayer_ThreeKingdomsSupportsInMemoryLayering()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]),
            targetGame: GameTypeEnum.ThreeKingdoms);
        var mask = document.CreateBoneMask(["root"], includeDescendants: true);

        var result = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            AnimationWorkbenchLayerMode.Override,
            1,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.State.HasActiveLayerPreview);
    }

    [DataTestMethod]
    [DataRow(AnimationWorkbenchLayerMode.Override, 20f)]
    [DataRow(AnimationWorkbenchLayerMode.Additive, 22f)]
    public void LayerCommit_SaveAndReopen_PreservesMaskedResult(
        AnimationWorkbenchLayerMode mode,
        float expectedSpineX)
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([new Vector3(1), new Vector3(2), new Vector3(3)]),
            CreateClip([new Vector3(10), new Vector3(20), new Vector3(30)]));
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var mask = document.CreateBoneMask(["spine"], includeDescendants: true);
        var preview = document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask.Mask!,
            mode,
            1,
            AnimationWorkbenchAdditiveReferencePose.TargetSkeletonRestPose));
        var blockedPath = Path.Combine(
            TestContext.TestRunDirectory!,
            $"layer-preview-{Guid.NewGuid():N}.anim");
        var blocked = document.ExportDiskCopy(blockedPath);

        var committed = document.CommitLayerPreview();
        Assert.IsTrue(document.Undo().Succeeded);
        Assert.IsTrue(document.Redo().Succeeded);
        var destination = Path.Combine(
            TestContext.TestRunDirectory!,
            $"layer-result-{Guid.NewGuid():N}.anim");
        var saved = document.ExportDiskCopy(destination);
        var reopenedFile = AnimationFile.Create(
            new ByteChunk(File.ReadAllBytes(destination)));
        var reopened = new AnimationClip(reopenedFile, skeleton);

        Assert.IsTrue(preview.Succeeded);
        Assert.IsFalse(blocked.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.LayerPreviewAlreadyActive,
            blocked.Diagnostics.Single().Code);
        Assert.IsTrue(committed.Succeeded);
        Assert.IsTrue(saved.Succeeded);
        Assert.AreEqual(1, reopened.DynamicFrames.Count);
        Assert.AreEqual(
            1,
            reopened.DynamicFrames[0].Position[0].X,
            Tolerance);
        Assert.AreEqual(
            expectedSpineX,
            reopened.DynamicFrames[0].Position[1].X,
            Tolerance);
    }

    [TestMethod]
    public void LayerCommit_ThreeKingdomsSaveRemainsExplicitlyGated()
    {
        var skeleton = CreateSkeleton();
        using var document = CreateDocument(
            skeleton,
            CreateClip([Vector3.Zero, Vector3.Zero, Vector3.Zero]),
            CreateClip([Vector3.One, Vector3.One, Vector3.One]),
            targetGame: GameTypeEnum.ThreeKingdoms);
        var mask = document.CreateBoneMask(["root"], includeDescendants: true);
        Assert.IsTrue(document.PreviewLayer(
            new AnimationWorkbenchLayerRequest(
                mask.Mask!,
                AnimationWorkbenchLayerMode.Override,
                1,
                AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame))
            .Succeeded);
        Assert.IsTrue(document.CommitLayerPreview().Succeeded);
        var destination = Path.Combine(
            TestContext.TestRunDirectory!,
            $"three-kingdoms-layer-{Guid.NewGuid():N}.anim");

        var saved = document.ExportDiskCopy(destination);

        Assert.IsFalse(saved.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TargetGameSaveUnsupported,
            saved.Diagnostics.Single().Code);
        Assert.IsFalse(File.Exists(destination));
    }

    private static AnimationWorkbenchDocument CreateDocument(
        GameSkeleton skeleton,
        AnimationClip animationA,
        AnimationClip animationB,
        GameSkeleton? animationBSkeleton = null,
        GameTypeEnum targetGame = GameTypeEnum.Warhammer3)
    {
        var format = new AnimationWorkbenchSourceFormat(7, 1);
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animationA,
                skeleton,
                format),
            new AnimationWorkbenchSourceInput(
                "animation_b",
                animationB,
                animationBSkeleton ?? skeleton,
                format),
            targetGame,
            skeleton));
        return document;
    }

    private static AnimationClip CreateClip(IReadOnlyList<Vector3> positions)
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
            Duration = AnimationTimebase.FromFramesPerSecond(1, 1).Duration,
        };
        clip.DynamicFrames.Add(frame);
        return clip;
    }

    private static AnimationClip CreateTwoFrameClip(
        IReadOnlyList<Vector3> firstPositions,
        IReadOnlyList<Vector3> secondPositions,
        Quaternion secondSpineRotation)
    {
        var clip = new AnimationClip
        {
            Duration = AnimationTimebase.FromFramesPerSecond(2, 2).Duration,
        };
        foreach (var positions in new[] { firstPositions, secondPositions })
        {
            var frame = new AnimationClip.KeyFrame();
            for (var boneIndex = 0; boneIndex < positions.Count; boneIndex++)
            {
                frame.Position.Add(positions[boneIndex]);
                frame.Rotation.Add(
                    ReferenceEquals(positions, secondPositions) && boneIndex == 1
                        ? secondSpineRotation
                        : Quaternion.Identity);
                frame.Scale.Add(Vector3.One);
            }
            clip.DynamicFrames.Add(frame);
        }
        return clip;
    }

    private static GameSkeleton CreateSkeleton()
    {
        return CreateSkeleton("root", "spine", "hand_r");
    }

    private static GameSkeleton CreateSkeleton(
        string rootName,
        string spineName,
        string handName,
        float rootTranslationX = 0)
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "target_skeleton",
            },
            Bones =
            [
                CreateBone(0, rootName, AnimationFile.BoneIndexNoParent),
                CreateBone(1, spineName, 0),
                CreateBone(2, handName, 1),
            ],
        };
        var frame = new AnimationFile.Frame();
        for (var boneIndex = 0; boneIndex < skeletonFile.Bones.Length; boneIndex++)
        {
            frame.Transforms.Add(boneIndex == 0
                ? new RmvVector3(rootTranslationX, 0, 0)
                : new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts.Add(part);
        return new GameSkeleton(skeletonFile, new AnimationPlayer());
    }

    private static AnimationFile.BoneInfo CreateBone(
        int id,
        string name,
        int parentId) => new()
    {
        Id = id,
        Name = name,
        ParentId = parentId,
    };

    private static void AssertRotationEqual(
        Quaternion expected,
        Quaternion actual)
    {
        expected.Normalize();
        actual.Normalize();
        Assert.AreEqual(
            1,
            MathF.Abs(Quaternion.Dot(expected, actual)),
            Tolerance);
    }
}
