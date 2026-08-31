using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public class AnimationWorkbenchTimelineEditingTests
{
    [TestMethod]
    public void Timeline_DefaultsToEditingAnchorsAndProjectsOnlyVisibleSamples()
    {
        using var document = CreateLoadedDocument(CreateClip(2_000));
        var controller = new AnimationWorkbenchTimelineController(document);
        var tracks = controller.Tracks;

        controller.SetViewport(600, 0);

        CollectionAssert.AreEqual(
            new[] { 0, 1_999 },
            controller.Timeline.EditingAnchorFrames.ToArray());
        CollectionAssert.AreEqual(
            new[] { 0 },
            controller.VisibleFrameIndices.ToArray());
        Assert.AreEqual(
            AnimationWorkbenchTimelineDisplayMode.EditingAnchors,
            controller.DisplayMode);

        controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.AllSamples);
        controller.ZoomAt(0.25, 0);

        Assert.IsTrue(controller.VisibleFrameIndices.Count <= 205);
        Assert.AreEqual(0, controller.VisibleFrameIndices[0]);
        Assert.IsTrue(controller.VisibleFrameIndices[^1] < 2_000);
        Assert.AreEqual(3, controller.PixelsPerFrame, 0.000001);
        controller.SelectFrame(20, extendRange: false, toggle: false);
        Assert.AreSame(tracks, controller.Tracks);
    }

    [TestMethod]
    public void Timeline_MouseKeyboardSelectionAndSnappingAreStable()
    {
        using var document = CreateLoadedDocument(CreateClip(12));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SetViewport(240, 0);
        controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.AllSamples);

        controller.SelectFrame(2, extendRange: false, toggle: false);
        controller.SelectFrame(5, extendRange: true, toggle: false);
        CollectionAssert.AreEqual(
            new[] { 2, 3, 4, 5 },
            controller.SelectedFrameIndices.ToArray());

        controller.SelectFrame(3, extendRange: false, toggle: true);
        CollectionAssert.AreEqual(
            new[] { 2, 4, 5 },
            controller.SelectedFrameIndices.ToArray());

        controller.NavigateSelection(-1, extendRange: false);
        CollectionAssert.AreEqual(
            new[] { 2 },
            controller.SelectedFrameIndices.ToArray());
        Assert.AreEqual(2, controller.FocusedFrameIndex);

        Assert.AreEqual(
            0,
            controller.SnapFrameFromViewportX(
                0.5 * controller.PixelsPerFrame));
        Assert.AreEqual(
            3,
            controller.SnapFrameFromViewportX(
                3.5 * controller.PixelsPerFrame));
        Assert.AreEqual(
            4,
            controller.SnapFrameFromViewportX(
                4.01 * controller.PixelsPerFrame));

        controller.BoxSelect(
            1.6 * controller.PixelsPerFrame,
            5.4 * controller.PixelsPerFrame,
            extendSelection: false);
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5 },
            controller.SelectedFrameIndices.ToArray());
    }

    [TestMethod]
    public void TimelineZoom_KeepsTheFrameUnderThePointerStable()
    {
        using var document = CreateLoadedDocument(CreateClip(100));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SetViewport(240, 240);
        var pointerX = 120d;
        var frameBefore = controller.FramePositionFromViewportX(pointerX);

        controller.ZoomAt(2, pointerX);

        Assert.AreEqual(
            frameBefore,
            controller.FramePositionFromViewportX(pointerX),
            0.000001);
    }

    [TestMethod]
    public void MoveSelection_PreviewsSnappedFramesAndCommitsOneHistoryEntry()
    {
        using var document = CreateLoadedDocument(CreateClip(6));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SetViewport(240, 0);
        controller.SetDisplayMode(
            AnimationWorkbenchTimelineDisplayMode.AllSamples);
        controller.SelectFrame(1, extendRange: false, toggle: false);
        controller.SelectFrame(2, extendRange: true, toggle: false);

        Assert.IsTrue(controller.BeginMoveSelection().Succeeded);
        var preview = controller.PreviewMoveSelectionByPixels(
            2.4 * controller.PixelsPerFrame);
        var previewClip = ResultClip(document);

        Assert.IsTrue(preview.Succeeded);
        Assert.IsTrue(preview.State.HasActiveTimelinePreview);
        CollectionAssert.AreEqual(
            new[] { 0f, 3f, 4f, 1f, 2f, 5f },
            RootPositions(previewClip));

        var committed = controller.CommitMoveSelection();
        Assert.IsTrue(committed.Succeeded);
        Assert.IsTrue(committed.State.CanUndo);
        Assert.IsFalse(committed.State.CanRedo);

        var undone = document.Undo();
        Assert.IsTrue(undone.Succeeded);
        Assert.IsFalse(undone.State.CanUndo);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 3f, 4f, 5f },
            RootPositions(ResultClip(document)));

        var redone = document.Redo();
        Assert.IsTrue(redone.Succeeded);
        CollectionAssert.AreEqual(
            new[] { 0f, 3f, 4f, 1f, 2f, 5f },
            RootPositions(ResultClip(document)));
    }

    [TestMethod]
    public void CancelMoveSelection_RestoresFramesAndSelection()
    {
        using var document = CreateLoadedDocument(CreateClip(6));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SelectFrame(1, extendRange: false, toggle: false);
        controller.SelectFrame(2, extendRange: true, toggle: false);
        Assert.IsTrue(controller.BeginMoveSelection().Succeeded);
        Assert.IsTrue(controller.PreviewMoveSelectionByPixels(
            2 * controller.PixelsPerFrame).Succeeded);

        var result = controller.CancelMoveSelection();

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            controller.SelectedFrameIndices.ToArray());
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 3f, 4f, 5f },
            RootPositions(ResultClip(document)));
    }

    [TestMethod]
    public void TrimAndSplit_UseHalfOpenRangesWithoutAnExtraEndFrame()
    {
        using var trimDocument = CreateLoadedDocument(CreateClip(6));
        Assert.IsTrue(trimDocument.BeginTimelinePreview().Succeeded);
        var trimmed = trimDocument.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(1, 4));

        Assert.IsTrue(trimmed.Succeeded);
        CollectionAssert.AreEqual(
            new[] { 1f, 2f, 3f },
            RootPositions(ResultClip(trimDocument)));
        Assert.AreEqual(
            TimeSpan.FromSeconds(0.15),
            ResultClip(trimDocument).Duration);

        Assert.IsTrue(trimDocument.CancelTimelinePreview().Succeeded);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 3f, 4f, 5f },
            RootPositions(ResultClip(trimDocument)));

        using var leadingDocument = CreateLoadedDocument(CreateClip(6));
        Assert.IsTrue(leadingDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(leadingDocument.PreviewSplitAt(
            3,
            AnimationWorkbenchSplitPart.Leading).Succeeded);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f },
            RootPositions(ResultClip(leadingDocument)));

        using var trailingDocument = CreateLoadedDocument(CreateClip(6));
        Assert.IsTrue(trailingDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(trailingDocument.PreviewSplitAt(
            3,
            AnimationWorkbenchSplitPart.Trailing).Succeeded);
        CollectionAssert.AreEqual(
            new[] { 3f, 4f, 5f },
            RootPositions(ResultClip(trailingDocument)));
    }

    [TestMethod]
    public void ReverseLoopAndStretch_KeepFrameCountAndDurationInOneTimeContract()
    {
        using var reverseDocument = CreateLoadedDocument(CreateClip(6));
        Assert.IsTrue(reverseDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(reverseDocument.PreviewReverseRange(
            new AnimationWorkbenchFrameRange(1, 5)).Succeeded);
        CollectionAssert.AreEqual(
            new[] { 0f, 4f, 3f, 2f, 1f, 5f },
            RootPositions(ResultClip(reverseDocument)));

        using var loopDocument = CreateLoadedDocument(CreateClip(6));
        Assert.IsTrue(loopDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(loopDocument.PreviewLoopRange(
            new AnimationWorkbenchFrameRange(1, 3),
            repeatCount: 3).Succeeded);
        var looped = ResultClip(loopDocument);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 1f, 2f, 1f, 2f, 3f, 4f, 5f },
            RootPositions(looped));
        Assert.AreEqual(10, looped.DynamicFrames.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(0.5), looped.Duration);

        using var stretchDocument = CreateLoadedDocument(CreateClip(6));
        Assert.IsTrue(stretchDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(stretchDocument.PreviewStretchRange(
            new AnimationWorkbenchFrameRange(1, 5),
            targetFrameCount: 2).Succeeded);
        var stretched = ResultClip(stretchDocument);
        Assert.AreEqual(4, stretched.DynamicFrames.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(0.2), stretched.Duration);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 3f, 5f },
            RootPositions(stretched));
    }

    [TestMethod]
    public void InterpolateAndMirror_ChangeAllAffectedBonesAtomically()
    {
        var clip = CreateClip(4);
        clip.DynamicFrames[1].Position[0] = new Vector3(99, 0, 0);
        clip.DynamicFrames[2].Position[0] = new Vector3(99, 0, 0);
        using var interpolationDocument = CreateLoadedDocument(clip);

        Assert.IsTrue(interpolationDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(interpolationDocument.PreviewInterpolateRange(
            new AnimationWorkbenchFrameRange(0, 4)).Succeeded);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 3f },
            RootPositions(ResultClip(interpolationDocument)));

        using var mirrorDocument = CreateLoadedDocument(
            CreateMirrorClip(),
            CreateMirrorSkeleton());
        Assert.IsTrue(mirrorDocument.BeginTimelinePreview().Succeeded);
        var mirrored = mirrorDocument.PreviewMirrorRange(
            new AnimationWorkbenchFrameRange(0, 1));
        var mirroredFrame = ResultClip(mirrorDocument).DynamicFrames[0];

        Assert.IsTrue(mirrored.Succeeded);
        Assert.AreEqual(new Vector3(-1, 0, 0), mirroredFrame.Position[0]);
        Assert.AreEqual(new Vector3(-3, 0, 0), mirroredFrame.Position[1]);
        Assert.AreEqual(new Vector3(-2, 0, 0), mirroredFrame.Position[2]);

        var committed = mirrorDocument.CommitTimelinePreview();
        Assert.IsTrue(committed.Succeeded);
        Assert.IsTrue(committed.State.CanUndo);
        Assert.IsTrue(mirrorDocument.Undo().Succeeded);
        Assert.AreEqual(
            new Vector3(2, 0, 0),
            ResultClip(mirrorDocument).DynamicFrames[0].Position[1]);
        Assert.IsTrue(mirrorDocument.Redo().Succeeded);
    }

    [TestMethod]
    public void Mirror_RecognizesLongAndEmbeddedSideTokens()
    {
        using var document = CreateLoadedDocument(
            CreateMirrorClip(),
            CreateSkeleton(
                ("root", AnimationFile.BoneIndexNoParent),
                ("hand_left", 0),
                ("hand_right", 0)));
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);

        var result = document.PreviewMirrorRange(
            new AnimationWorkbenchFrameRange(0, 1));
        var frame = ResultClip(document).DynamicFrames[0];

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(new Vector3(-3, 0, 0), frame.Position[1]);
        Assert.AreEqual(new Vector3(-2, 0, 0), frame.Position[2]);

        using var embeddedDocument = CreateLoadedDocument(
            CreateMirrorClip(),
            CreateSkeleton(
                ("root", AnimationFile.BoneIndexNoParent),
                ("leg_left_0", 0),
                ("leg_right_0", 0)));
        Assert.IsTrue(embeddedDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(embeddedDocument.PreviewMirrorRange(
            new AnimationWorkbenchFrameRange(0, 1)).Succeeded);
        var embeddedFrame = ResultClip(embeddedDocument).DynamicFrames[0];
        Assert.AreEqual(new Vector3(-3, 0, 0), embeddedFrame.Position[1]);
        Assert.AreEqual(new Vector3(-2, 0, 0), embeddedFrame.Position[2]);

        using var caDocument = CreateLoadedDocument(
            CreateMirrorClip(),
            CreateSkeleton(
                ("root", AnimationFile.BoneIndexNoParent),
                ("bn_leftarm", 0),
                ("bn_rightarm", 0)));
        Assert.IsTrue(caDocument.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(caDocument.PreviewMirrorRange(
            new AnimationWorkbenchFrameRange(0, 1)).Succeeded);
        var caFrame = ResultClip(caDocument).DynamicFrames[0];
        Assert.AreEqual(new Vector3(-3, 0, 0), caFrame.Position[1]);
        Assert.AreEqual(new Vector3(-2, 0, 0), caFrame.Position[2]);
    }

    [TestMethod]
    public void MovePreview_DeduplicatesMouseEventsWithinTheSameSnappedFrame()
    {
        using var document = CreateLoadedDocument(CreateClip(2_000));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SelectFrame(20, extendRange: false, toggle: false);
        Assert.IsTrue(controller.BeginMoveSelection().Succeeded);

        var first = controller.PreviewMoveSelectionByPixels(
            2.1 * controller.PixelsPerFrame);
        var duplicate = controller.PreviewMoveSelectionByPixels(
            20.4 * controller.PixelsPerFrame);

        Assert.AreSame(first, duplicate);
        CollectionAssert.AreEqual(
            new[] { 40 },
            controller.SelectedFrameIndices.ToArray());
        Assert.IsTrue(controller.CommitMoveSelection().Succeeded);
        Assert.AreEqual(
            20f,
            ResultClip(document).DynamicFrames[40].Position[0].X);
    }

    [TestMethod]
    public void Reload_ClearsSelectionHistoryFromThePreviousDocumentGeneration()
    {
        using var document = CreateLoadedDocument(CreateClip(8));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SelectFrame(1, extendRange: false, toggle: false);

        var skeleton = CreateSkeleton();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "replacement",
                CreateClip(4),
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));
        controller.Refresh();

        Assert.AreEqual(0, controller.SelectionCount);
        Assert.AreEqual(-1, controller.FocusedFrameIndex);
    }

    [TestMethod]
    public void RangeEdit_MapsSelectionAndRestoresItAcrossUndoRedo()
    {
        using var document = CreateLoadedDocument(CreateClip(8));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SelectFrame(2, extendRange: false, toggle: false);
        controller.SelectFrame(5, extendRange: true, toggle: false);

        Assert.IsTrue(controller.PreviewStretchSelection(2).Succeeded);
        CollectionAssert.AreEqual(
            new[] { 2, 3 },
            controller.SelectedFrameIndices.ToArray());
        Assert.IsTrue(controller.CommitPreview().Succeeded);

        Assert.IsTrue(controller.Undo().Succeeded);
        CollectionAssert.AreEqual(
            new[] { 2, 3, 4, 5 },
            controller.SelectedFrameIndices.ToArray());

        Assert.IsTrue(controller.Redo().Succeeded);
        CollectionAssert.AreEqual(
            new[] { 2, 3 },
            controller.SelectedFrameIndices.ToArray());
    }

    [TestMethod]
    public void InvalidRangeEdit_IsRejectedWithoutPartialPreviewOrHistory()
    {
        using var document = CreateLoadedDocument(CreateClip(4));
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);

        var invalid = document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(1, 5));

        Assert.IsFalse(invalid.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TimelineRangeInvalid,
            invalid.Diagnostics.Single().Code);
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 3f },
            RootPositions(ResultClip(document)));
        Assert.IsFalse(invalid.State.CanUndo);

        Assert.IsTrue(document.CancelTimelinePreview().Succeeded);
        Assert.IsFalse(document.SelectPreview(
            AnimationWorkbenchPreviewKind.Result).HasActiveTimelinePreview);
    }

    [TestMethod]
    public void DisjointSelection_IsRejectedBeforeStartingRangePreview()
    {
        using var document = CreateLoadedDocument(CreateClip(6));
        var controller = new AnimationWorkbenchTimelineController(document);
        controller.SelectFrame(1, extendRange: false, toggle: false);
        controller.SelectFrame(3, extendRange: false, toggle: true);

        var result = controller.PreviewReverseSelection();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TimelineSelectionInvalid,
            result.Diagnostics.Single().Code);
        Assert.IsFalse(document.GetState().HasActiveTimelinePreview);
    }

    [TestMethod]
    public void ExtremeStretchTarget_IsRejectedWithoutIntegerOverflow()
    {
        using var document = CreateLoadedDocument(CreateClip(4));
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);

        var result = document.PreviewStretchRange(
            new AnimationWorkbenchFrameRange(1, 3),
            int.MaxValue);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TimelineStretchInvalid,
            result.Diagnostics.Single().Code);
        Assert.AreEqual(4, ResultClip(document).DynamicFrames.Count);
    }

    [TestMethod]
    public void UndoDuringTimelinePreview_ReportsTheTimelinePreviewBoundary()
    {
        using var document = CreateLoadedDocument(CreateClip(4));
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);

        var result = document.Undo();
        var redo = document.Redo();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TimelinePreviewAlreadyActive,
            result.Diagnostics.Single().Code);
        Assert.IsFalse(redo.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TimelinePreviewAlreadyActive,
            redo.Diagnostics.Single().Code);
    }

    private static AnimationWorkbenchDocument CreateLoadedDocument(
        AnimationClip animation,
        GameSkeleton? skeleton = null)
    {
        skeleton ??= CreateSkeleton();
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animation,
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            null,
            GameTypeEnum.Warhammer3,
            skeleton));
        return document;
    }

    private static AnimationClip ResultClip(AnimationWorkbenchDocument document) =>
        document.SelectPreview(
            AnimationWorkbenchPreviewKind.Result).CurrentPreview!.Animation;

    private static float[] RootPositions(AnimationClip clip) =>
        clip.DynamicFrames.Select(frame => frame.Position[0].X).ToArray();

    private static AnimationClip CreateClip(int frameCount)
    {
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(frameCount * 0.05),
        };
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.Add(new Vector3(frameIndex, 0, 0));
            frame.Position.Add(new Vector3(frameIndex + 10, 0, 0));
            frame.Rotation.Add(Quaternion.Identity);
            frame.Rotation.Add(Quaternion.Identity);
            frame.Scale.Add(Vector3.One);
            frame.Scale.Add(Vector3.One);
            clip.DynamicFrames.Add(frame);
        }

        return clip;
    }

    private static AnimationClip CreateMirrorClip()
    {
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(0.05),
        };
        var frame = new AnimationClip.KeyFrame();
        frame.Position.AddRange(
        [
            new Vector3(1, 0, 0),
            new Vector3(2, 0, 0),
            new Vector3(3, 0, 0),
        ]);
        frame.Rotation.AddRange(
        [
            Quaternion.Identity,
            Quaternion.Identity,
            Quaternion.Identity,
        ]);
        frame.Scale.AddRange([Vector3.One, Vector3.One, Vector3.One]);
        clip.DynamicFrames.Add(frame);
        return clip;
    }

    private static GameSkeleton CreateSkeleton() => CreateSkeleton(
        ("root", AnimationFile.BoneIndexNoParent),
        ("child", 0));

    private static GameSkeleton CreateMirrorSkeleton() => CreateSkeleton(
        ("root", AnimationFile.BoneIndexNoParent),
        ("hand_l", 0),
        ("hand_r", 0));

    private static GameSkeleton CreateSkeleton(
        params (string Name, int ParentId)[] bones)
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "target_skeleton",
            },
            Bones = bones.Select((bone, index) =>
                new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = bone.Name,
                    ParentId = bone.ParentId,
                }).ToArray(),
        };
        var frame = new AnimationFile.Frame();
        foreach (var _ in bones)
        {
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }

        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts.Add(part);
        return new GameSkeleton(skeletonFile, new AnimationPlayer());
    }
}
