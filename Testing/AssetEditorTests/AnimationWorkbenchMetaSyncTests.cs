using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public sealed class AnimationWorkbenchMetaSyncTests
{
    [TestMethod]
    public void Trim_GeneratesIndependentMetaAndPreservesUnknownPayload()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var sourceBytes = CreateMetaBytes(
            parser,
            new Time
            {
                Name = "TIME",
                Version = 10,
                Data = [],
                StartTime = 0.2f,
                EndTime = 0.8f,
            },
            CreateUnknown("CODEX_UNKNOWN_TAG", 77, [0x10, 0x20, 0x30]));
        var originalBytes = sourceBytes.ToArray();
        using var document = CreateLoadedDocument(sourceBytes);

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        var resultBytes = document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray();
        var result = parser.ParseFile(resultBytes);
        var timed = result.GetItemsOfType<Time>().Single();
        var unknown = result.Attributes
            .OfType<ParsedUnknownMetadataAttribute>()
            .Single();

        Assert.AreEqual(0f, timed.StartTime, 0.000001f);
        Assert.AreEqual(0.5f, timed.EndTime, 0.000001f);
        CollectionAssert.AreEqual(
            new byte[] { 0x10, 0x20, 0x30 },
            unknown.Data.Skip(sizeof(int)).ToArray());
        CollectionAssert.AreEqual(originalBytes, sourceBytes);
        CollectionAssert.AreEqual(
            originalBytes,
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.AnimationA)!.Bytes.ToArray());
        Assert.AreEqual(
            "animation_a.anm.meta",
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.AnimationA)!.Name);
        Assert.AreEqual(
            "result.anm.meta",
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Name);
        Assert.IsTrue(document.GetState().IsMetaDataDirty);
    }

    [TestMethod]
    public void Blend_MapsBothSourcesAndReportsOverlappingConflicts()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var metaA = CreateMetaBytes(
            parser,
            CreateTime(0.5f, 0.6f));
        var metaB = CreateMetaBytes(
            parser,
            CreateTime(0.2f, 0.4f));
        using var document = CreateLoadedDocument(metaA, metaB);

        var preview = document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            AnimationAOutFrame: 5,
            AnimationBInFrame: 2,
            OverlapDuration: TimeSpan.FromSeconds(0.2),
            OutputFramesPerSecond: 10,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default));
        Assert.IsTrue(preview.Succeeded);
        Assert.IsTrue(document.CommitBlendPreview().Succeeded);

        var result = parser.ParseFile(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
        var events = result.GetItemsOfType<Time>()
            .OrderBy(item => item.StartTime)
            .ToArray();

        Assert.AreEqual(2, events.Length);
        Assert.AreEqual(0.4f, events[0].StartTime, 0.000001f);
        Assert.AreEqual(0.6f, events[0].EndTime, 0.000001f);
        Assert.AreEqual(0.5f, events[1].StartTime, 0.000001f);
        Assert.AreEqual(0.6f, events[1].EndTime, 0.000001f);
        Assert.IsTrue(document.GetState().MetaDataProblems.Any(problem =>
            problem.Code == AnimationWorkbenchMetaDataProblemCode.Conflict));
    }

    [TestMethod]
    public void DisabledSynchronization_ProducesAnimationOnlyWithWarning()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser, CreateTime(0.1f, 0.2f)),
            synchronizeMetaData: false);

        var state = document.GetState();

        Assert.IsFalse(state.IsMetaDataSynchronizationEnabled);
        Assert.IsFalse(state.IsMetaDataDirty);
        Assert.IsNull(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result));
        Assert.AreEqual(
            AnimationWorkbenchMetaDataProblemCode.SynchronizationDisabled,
            state.MetaDataProblems.Single().Code);
        var navigation = document.NavigateToMetaDataProblem(
            state.MetaDataProblems.Single());
        Assert.IsFalse(navigation.Succeeded);
        Assert.IsNull(navigation.Location);
    }

    [TestMethod]
    public void DisabledSynchronization_DoesNotTransformHiddenMetaData()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var sourceBytes = CreateMetaBytes(parser, CreateTime(0.2f, 0.8f));
        using var document = CreateLoadedDocument(
            sourceBytes,
            synchronizeMetaData: false);

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        Assert.IsNull(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result));
        document.SetMetaDataSynchronizationEnabled(true);
        CollectionAssert.AreEqual(
            sourceBytes,
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
    }

    [TestMethod]
    public void DisablingSynchronization_DiscardsPreviewMetaDataBeforeCommit()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var sourceBytes = CreateMetaBytes(parser, CreateTime(0.2f, 0.8f));
        using var document = CreateLoadedDocument(sourceBytes);

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);

        var disabled = document.SetMetaDataSynchronizationEnabled(false);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        Assert.IsFalse(disabled.IsMetaDataSynchronizationEnabled);
        Assert.AreEqual(
            AnimationWorkbenchMetaDataProblemCode.SynchronizationDisabled,
            disabled.MetaDataProblems.Single().Code);
        document.SetMetaDataSynchronizationEnabled(true);
        CollectionAssert.AreEqual(
            sourceBytes,
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
    }

    [TestMethod]
    public void DisablingSynchronization_PreservesCommittedProblemsAcrossAnimationOnlyCommit()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(CreateMetaBytes(
            parser,
            CreateTime(0.8f, 0.9f)));
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);
        var problem = document.GetState().MetaDataProblems.Single(item =>
            item.Code ==
                AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult);

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewReverseRange(
            new AnimationWorkbenchFrameRange(0, 5)).Succeeded);
        document.SetMetaDataSynchronizationEnabled(false);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);
        var enabled = document.SetMetaDataSynchronizationEnabled(true);

        Assert.AreEqual(problem, enabled.MetaDataProblems.Single(item =>
            item.Code ==
                AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult));
    }

    [TestMethod]
    public void DisablingSynchronization_DoesNotHideUnsavedMetaData()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateEditedDocument(parser);

        var state = document.SetMetaDataSynchronizationEnabled(false);

        Assert.IsFalse(state.IsMetaDataSynchronizationEnabled);
        Assert.IsTrue(state.IsMetaDataDirty);
        Assert.IsTrue(state.IsDirty);
    }

    [TestMethod]
    public void Load_DoesNotUseAnimationBMetaDataAsAnimationAResult()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(
            null,
            CreateMetaBytes(parser, CreateTime(0.2f, 0.4f)));

        Assert.IsNull(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result));
        Assert.IsNotNull(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.AnimationB));

        Assert.IsTrue(document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            5,
            0,
            TimeSpan.Zero,
            10,
            AnimationWorkbenchBlendCurve.Linear,
            AnimationWorkbenchRootMotionOptions.Default)).Succeeded);
        Assert.IsNotNull(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result));
    }

    [TestMethod]
    public void ProblemNavigation_LocatesSourceAndResultWithoutEditing()
    {
        new LocalizationManager().LoadLanguage();
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var previewHost = new RecordingPreviewHost();
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser, CreateTime(0.5f, 0.6f)),
            CreateMetaBytes(parser, CreateTime(0.2f, 0.4f)),
            previewHost: previewHost);
        Assert.IsTrue(document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            5,
            2,
            TimeSpan.FromSeconds(0.2),
            10,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default)).Succeeded);
        Assert.IsTrue(document.CommitBlendPreview().Succeeded);
        var beforeState = document.GetState();
        var beforeBytes = document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray();
        var problem = beforeState.MetaDataProblems.First(item =>
            item.Code == AnimationWorkbenchMetaDataProblemCode.Conflict &&
            item.Source == AnimationWorkbenchSourceSlot.AnimationB);

        var controller = new AnimationWorkbenchMetaDataController(document);
        AnimationWorkbenchMetaDataNavigationLocation? requestedLocation = null;
        controller.NavigationRequested += (_, location) =>
            requestedLocation = location;

        var navigation = controller.Navigate(
            controller.Problems.Single(item => item.Problem == problem));

        Assert.IsTrue(navigation.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchSourceSlot.AnimationB,
            navigation.Location!.Source);
        Assert.AreEqual(0, navigation.Location.SourceAttributeIndex);
        Assert.AreEqual(problem.ResultAttributeIndex,
            navigation.Location.ResultAttributeIndex);
        Assert.AreEqual(0.2f, navigation.Location.SourceTimeSeconds!.Value, 0.000001f);
        Assert.AreEqual(0.4f, navigation.Location.ResultTimeSeconds!.Value, 0.000001f);
        Assert.AreEqual(navigation.Location, requestedLocation);
        Assert.AreEqual(
            AnimationWorkbenchPreviewKind.AnimationB,
            navigation.State.SelectedPreview);
        Assert.AreEqual(TimeSpan.FromSeconds(0.2), previewHost.LastSeek);
        document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        Assert.AreEqual(TimeSpan.FromSeconds(0.4), previewHost.LastSeek);
        Assert.AreEqual(
            beforeState.HistoryRevision,
            navigation.State.HistoryRevision);
        Assert.AreEqual(beforeState.IsDirty, navigation.State.IsDirty);
        CollectionAssert.AreEqual(
            beforeBytes,
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
    }

    [TestMethod]
    public void Trim_PreservesUnsafeAttributesAndReportsProblems()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(CreateMetaBytes(
            parser,
            CreateTime(0.8f, 0.9f),
            CreateUnknown("CODEX_UNKNOWN_TAG", 77, [0x40, 0x50])));

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        var result = parser.ParseFile(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
        var timed = result.GetItemsOfType<Time>().Single();
        var unknown = result.Attributes
            .OfType<ParsedUnknownMetadataAttribute>()
            .Single();
        var problems = document.GetState().MetaDataProblems;

        Assert.AreEqual(0.8f, timed.StartTime, 0.000001f);
        Assert.AreEqual(0.9f, timed.EndTime, 0.000001f);
        CollectionAssert.AreEqual(
            new byte[] { 0x40, 0x50 },
            unknown.Data.Skip(sizeof(int)).ToArray());
        Assert.IsTrue(problems.Any(problem =>
            problem.Code ==
                AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult &&
            problem.SourceAttributeIndex == 0 &&
            problem.ResultAttributeIndex == 0));
        Assert.IsTrue(problems.Any(problem =>
            problem.Code ==
                AnimationWorkbenchMetaDataProblemCode.UnknownPayloadUnmapped &&
            problem.SourceAttributeIndex == 1 &&
                problem.ResultAttributeIndex == 1));
    }

    [TestMethod]
    public void Stretch_MapsMetaDataThroughUnifiedTimeline()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(CreateMetaBytes(
            parser,
            CreateTime(0.1f, 0.2f),
            CreateTime(0.3f, 0.5f),
            CreateTime(0.7f, 0.8f)));

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewStretchRange(
            new AnimationWorkbenchFrameRange(2, 6),
            targetFrameCount: 8).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        var events = parser.ParseFile(document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray())
            .GetItemsOfType<Time>()
            .ToArray();

        Assert.AreEqual(1.4, document.GetTimelineSnapshot().Duration.TotalSeconds,
            0.000001);
        Assert.AreEqual(0.1f, events[0].StartTime, 0.000001f);
        Assert.AreEqual(0.2f, events[0].EndTime, 0.000001f);
        Assert.AreEqual(0.4f, events[1].StartTime, 0.000001f);
        Assert.AreEqual(0.8f, events[1].EndTime, 0.000001f);
        Assert.AreEqual(1.1f, events[2].StartTime, 0.000001f);
        Assert.AreEqual(1.2f, events[2].EndTime, 0.000001f);
    }

    [TestMethod]
    public void Layer_CombinesBothMetaSourcesAndRetainsConflicts()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser, CreateTime(0.2f, 0.4f)),
            CreateMetaBytes(parser, CreateTime(0.3f, 0.5f)));
        var mask = document.CreateBoneMask(["root"], false).Mask!;

        Assert.IsTrue(document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask,
            AnimationWorkbenchLayerMode.Override,
            0.5,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame))
            .Succeeded);
        Assert.IsTrue(document.CommitLayerPreview().Succeeded);

        var events = parser.ParseFile(document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray())
            .GetItemsOfType<Time>()
            .OrderBy(item => item.StartTime)
            .ToArray();

        Assert.AreEqual(2, events.Length);
        Assert.AreEqual(0.2f, events[0].StartTime, 0.000001f);
        Assert.AreEqual(0.3f, events[1].StartTime, 0.000001f);
        Assert.IsTrue(document.GetState().MetaDataProblems.Any(problem =>
            problem.Code == AnimationWorkbenchMetaDataProblemCode.Conflict &&
            problem.Source == AnimationWorkbenchSourceSlot.AnimationB));
    }

    [TestMethod]
    public void Layer_UsesAbsoluteTimeForAnimationBAndReportsOutputOverflow()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser),
            CreateMetaBytes(
                parser,
                CreateTime(0.7f, 0.8f),
                CreateTime(1.5f, 1.6f)),
            animationA: CreateClip(durationSeconds: 1),
            animationB: CreateClip(
                frameCount: 20,
                durationSeconds: 2));
        var mask = document.CreateBoneMask(["root"], false).Mask!;

        Assert.IsTrue(document.PreviewLayer(new AnimationWorkbenchLayerRequest(
            mask,
            AnimationWorkbenchLayerMode.Override,
            0.5,
            AnimationWorkbenchAdditiveReferencePose.AnimationBFirstFrame))
            .Succeeded);
        Assert.IsTrue(document.CommitLayerPreview().Succeeded);

        var events = parser.ParseFile(document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray())
            .GetItemsOfType<Time>()
            .ToArray();
        Assert.AreEqual(0.7f, events[0].StartTime, 0.000001f);
        Assert.AreEqual(0.8f, events[0].EndTime, 0.000001f);
        Assert.AreEqual(1.5f, events[1].StartTime, 0.000001f);
        Assert.AreEqual(1.6f, events[1].EndTime, 0.000001f);
        Assert.IsTrue(document.GetState().MetaDataProblems.Any(problem =>
            problem.Code ==
                AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult &&
            problem.Source == AnimationWorkbenchSourceSlot.AnimationB &&
            problem.SourceAttributeIndex == 1));
    }

    [TestMethod]
    public void Retarget_MapsVerifiedBoneIndexFieldsAndPreservesWarningAfterTimelineEdit()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var sourceSkeleton = CreateSkeleton("root", "hand");
        var targetSkeleton = CreateSkeleton("root", "extra", "hand");
        using var document = CreateLoadedDocument(
            CreateMetaBytes(
                parser,
                new Dismember_v2
                {
                    Name = "DISMEMBER",
                    Version = 2,
                    Data = [],
                    StartTime = 0.2f,
                    EndTime = 0.3f,
                    BoneIndex = 1,
                    Direction = Vector3.UnitX,
                    Rotation = Vector3.Zero,
                },
                new Dismember_v2
                {
                    Name = "DISMEMBER",
                    Version = 2,
                    Data = [],
                    StartTime = 0.4f,
                    EndTime = 0.5f,
                    BoneIndex = 7,
                    Direction = Vector3.UnitY,
                    Rotation = Vector3.Zero,
                },
                new DockEquipmentRHand_v10
                {
                    Name = "DOCK_EQPT_RHAND",
                    Version = 10,
                    Data = [],
                    StartTime = 0,
                    EndTime = 0,
                    PropBoneId = 1,
                }),
            sourceSkeleton: sourceSkeleton,
            targetSkeleton: targetSkeleton);
        var mappings = new AnimationWorkbenchRetargetBoneMapping[]
        {
            new(0, "root", 0, "root",
                AnimationWorkbenchRetargetConfidence.Manual, true, true),
            new(1, "extra", null, null,
                AnimationWorkbenchRetargetConfidence.None, false, false),
            new(2, "hand", 1, "hand",
                AnimationWorkbenchRetargetConfidence.Manual, false, true),
        };

        Assert.IsTrue(document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationA,
                mappings)).Succeeded);
        Assert.IsTrue(document.CommitRetargetPreview().Succeeded);

        var result = parser.ParseFile(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());

        var entries = result.GetItemsOfType<Dismember_v2>().ToArray();
        Assert.AreEqual(2, entries[0].BoneIndex);
        Assert.AreEqual(7, entries[1].BoneIndex);
        Assert.AreEqual(1, result.GetItemsOfType<DockEquipmentRHand_v10>()
            .Single().PropBoneId);
        Assert.IsTrue(document.GetState().MetaDataProblems.Any(problem =>
            problem.Code == AnimationWorkbenchMetaDataProblemCode.BoneUnmapped &&
            problem.SourceAttributeIndex == 1));

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewStretchRange(
            new AnimationWorkbenchFrameRange(0, 10),
            targetFrameCount: 20).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);
        Assert.IsTrue(document.GetState().MetaDataProblems.Any(problem =>
            problem.Code == AnimationWorkbenchMetaDataProblemCode.BoneUnmapped &&
            problem.SourceAttributeIndex == 1 &&
            Math.Abs(problem.ResultStartTime!.Value - 0.8f) < 0.000001f));
    }

    [TestMethod]
    public void RetargetAnimationB_MapsUnmappedBoneWarningIntoBlendResult()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var skeleton = CreateSkeleton("root", "hand");
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser),
            CreateMetaBytes(
                parser,
                new Dismember_v2
                {
                    Name = "DISMEMBER",
                    Version = 2,
                    Data = [],
                    StartTime = 0.2f,
                    EndTime = 0.3f,
                    BoneIndex = 7,
                    Direction = Vector3.UnitX,
                    Rotation = Vector3.Zero,
                }),
            sourceSkeleton: skeleton,
            targetSkeleton: skeleton);
        var mappings = new AnimationWorkbenchRetargetBoneMapping[]
        {
            new(0, "root", 0, "root",
                AnimationWorkbenchRetargetConfidence.Manual, true, true),
            new(1, "hand", 1, "hand",
                AnimationWorkbenchRetargetConfidence.Manual, false, true),
        };

        Assert.IsTrue(document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                AnimationWorkbenchSourceSlot.AnimationB,
                mappings)).Succeeded);
        Assert.IsTrue(document.CommitRetargetPreview().Succeeded);
        var sourceProblem = document.GetState().MetaDataProblems.Single(
            problem => problem.Code ==
                AnimationWorkbenchMetaDataProblemCode.BoneUnmapped);
        Assert.AreEqual(
            AnimationWorkbenchSourceSlot.AnimationB,
            sourceProblem.Source);
        Assert.IsNull(sourceProblem.ResultAttributeIndex);

        Assert.IsTrue(document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            5,
            0,
            TimeSpan.Zero,
            10,
            AnimationWorkbenchBlendCurve.Linear,
            AnimationWorkbenchRootMotionOptions.Default)).Succeeded);
        var resultProblem = document.GetState().MetaDataProblems.Single(
            problem => problem.Code ==
                AnimationWorkbenchMetaDataProblemCode.BoneUnmapped);
        Assert.AreEqual(0, resultProblem.ResultAttributeIndex);
        Assert.IsTrue(resultProblem.ResultStartTime.HasValue);
    }

    [TestMethod]
    public void UndoRedo_RestoresAnimationAndMetaDataAtomically()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(CreateMetaBytes(
            parser,
            CreateTime(0.2f, 0.8f)));
        var originalBytes = document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray();

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);
        var trimmedBytes = document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray();

        Assert.IsTrue(document.Undo().Succeeded);
        Assert.AreEqual(1, document.GetTimelineSnapshot().Duration.TotalSeconds,
            0.000001);
        CollectionAssert.AreEqual(
            originalBytes,
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());

        Assert.IsTrue(document.Redo().Succeeded);
        Assert.AreEqual(0.5, document.GetTimelineSnapshot().Duration.TotalSeconds,
            0.000001);
        CollectionAssert.AreEqual(
            trimmedBytes,
            document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
    }

    [TestMethod]
    public void SavingOneResource_DoesNotClearTheOtherDirtyStateOnFailure()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var successfulService = new Mock<IPackFileService>();
        var failingService = new Mock<IPackFileService>();
        failingService.Setup(service => service.AddFilesToPack(
                project,
                It.IsAny<List<NewPackFileEntry>>(),
                false))
            .Throws(new IOException("simulated write failure"));

        using (var document = CreateEditedDocument(parser))
        {
            var animationSave = document.SaveAsNewProjectResource(
                successfulService.Object,
                project,
                @"animations\result.anim");
            var metaSave = document.SaveMetaDataAsNewProjectResource(
                failingService.Object,
                project,
                @"animations\result.anm.meta");

            Assert.IsTrue(animationSave.Succeeded);
            Assert.IsFalse(metaSave.Succeeded);
            Assert.IsFalse(metaSave.State.IsAnimationDirty);
            Assert.IsTrue(metaSave.State.IsMetaDataDirty);
            Assert.IsTrue(metaSave.State.IsDirty);
            Assert.AreEqual(@"animations\result.anim",
                metaSave.State.ProjectResourcePath);
            Assert.IsNull(metaSave.State.ProjectMetaDataResourcePath);
        }

        using (var document = CreateEditedDocument(parser))
        {
            var metaSave = document.SaveMetaDataAsNewProjectResource(
                successfulService.Object,
                project,
                @"animations\result_b.anm.meta");
            var animationSave = document.SaveAsNewProjectResource(
                failingService.Object,
                project,
                @"animations\result_b.anim");

            Assert.IsTrue(metaSave.Succeeded);
            Assert.IsFalse(animationSave.Succeeded);
            Assert.IsTrue(animationSave.State.IsAnimationDirty);
            Assert.IsFalse(animationSave.State.IsMetaDataDirty);
            Assert.IsTrue(animationSave.State.IsDirty);
            Assert.IsNull(animationSave.State.ProjectResourcePath);
            Assert.AreEqual(@"animations\result_b.anm.meta",
                animationSave.State.ProjectMetaDataResourcePath);
        }
    }

    [TestMethod]
    public void Blend_DistinguishesWholeAnimationAndInstantZeroRanges()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser),
            CreateMetaBytes(
                parser,
                new AllowDeltaScale_v10
                {
                    Name = "ALLOWED_DELTA_SCALE",
                    Version = 10,
                    Data = [],
                    StartTime = 0,
                    EndTime = 0,
                    Value = 1,
                },
                CreateTime(0, 0)));

        Assert.IsTrue(document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            5,
            0,
            TimeSpan.FromSeconds(0.2),
            10,
            AnimationWorkbenchBlendCurve.Smooth,
            AnimationWorkbenchRootMotionOptions.Default)).Succeeded);
        Assert.IsTrue(document.CommitBlendPreview().Succeeded);

        var result = parser.ParseFile(document.GetMetaDataSnapshot(
            AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray());
        var wholeAnimation = result
            .GetItemsOfType<AllowDeltaScale_v10>()
            .Single();
        var instant = result.GetItemsOfType<Time>().Single();

        Assert.AreEqual(0f, wholeAnimation.StartTime, 0.000001f);
        Assert.AreEqual(0f, wholeAnimation.EndTime, 0.000001f);
        Assert.AreEqual(0.4f, instant.StartTime, 0.000001f);
        Assert.AreEqual(0.4f, instant.EndTime, 0.000001f);
        Assert.IsTrue(MetaDataTimeRange.TryCreate(
            wholeAnimation,
            out var wholeAnimationRange));
        Assert.IsTrue(wholeAnimationRange.IsWholeAnimationRange);
        Assert.IsTrue(MetaDataTimeRange.TryCreate(
            instant,
            out var instantRange));
        Assert.IsFalse(instantRange.IsWholeAnimationRange);

        wholeAnimation.StartTime = 0.1f;
        wholeAnimation.EndTime = 0.2f;
        Assert.IsTrue(MetaDataTimeRange.TryCreate(
            wholeAnimation,
            out var nonZeroWholeAnimationRange));
        Assert.AreEqual(
            MetaDataZeroRangeBehavior.WholeAnimation,
            nonZeroWholeAnimationRange.ZeroRangeBehavior);
        Assert.IsFalse(nonZeroWholeAnimationRange.IsWholeAnimationRange);
    }

    [TestMethod]
    public void Trim_UsesHalfOpenBoundaryForIntervalsAndInstants()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(CreateMetaBytes(
            parser,
            CreateTime(0.1f, 0.2f),
            CreateTime(0.2f, 0.2f)));

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        var events = parser.ParseFile(document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray())
            .GetItemsOfType<Time>()
            .ToArray();

        Assert.AreEqual(0.1f, events[0].StartTime, 0.000001f);
        Assert.AreEqual(0.2f, events[0].EndTime, 0.000001f);
        Assert.AreEqual(0f, events[1].StartTime, 0.000001f);
        Assert.AreEqual(0f, events[1].EndTime, 0.000001f);
        Assert.IsTrue(document.GetState().MetaDataProblems.Any(problem =>
            problem.Code ==
                AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult &&
            problem.SourceAttributeIndex == 0));
    }

    [TestMethod]
    public void ThreeKingdoms_UsesTheSameMetaSynchronizationContract()
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        using var document = CreateLoadedDocument(
            CreateMetaBytes(parser, CreateTime(0.2f, 0.8f)),
            targetGame: GameTypeEnum.ThreeKingdoms);

        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        var timed = parser.ParseFile(document.GetMetaDataSnapshot(
                AnimationWorkbenchMetaDataKind.Result)!.Bytes.ToArray())
            .GetItemsOfType<Time>()
            .Single();
        Assert.AreEqual(0f, timed.StartTime, 0.000001f);
        Assert.AreEqual(0.5f, timed.EndTime, 0.000001f);
    }

    private static AnimationWorkbenchDocument CreateEditedDocument(
        MetaDataFileParser parser)
    {
        var document = CreateLoadedDocument(CreateMetaBytes(
            parser,
            CreateTime(0.2f, 0.8f)));
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(2, 7)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);
        return document;
    }

    private static AnimationWorkbenchDocument CreateLoadedDocument(
        byte[]? animationAMetaData,
        byte[]? animationBMetaData = null,
        bool synchronizeMetaData = true,
        GameSkeleton? sourceSkeleton = null,
        GameSkeleton? targetSkeleton = null,
        GameTypeEnum targetGame = GameTypeEnum.Warhammer3,
        AnimationClip? animationA = null,
        AnimationClip? animationB = null,
        IAnimationWorkbenchPreviewHost? previewHost = null)
    {
        var skeleton = sourceSkeleton ?? CreateSkeleton();
        var target = targetSkeleton ?? skeleton;
        var document = new AnimationWorkbenchDocument(previewHost);
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animationA ?? CreateClip(skeleton.BoneCount),
                skeleton,
                new AnimationWorkbenchSourceFormat(7, 1)),
            animationBMetaData == null
                ? null
                : new AnimationWorkbenchSourceInput(
                    "animation_b",
                    animationB ?? CreateClip(skeleton.BoneCount),
                    skeleton,
                    new AnimationWorkbenchSourceFormat(7, 1)),
            targetGame,
            target,
            animationAMetaData == null
                ? null
                : new AnimationWorkbenchMetaDataSourceInput(
                    "animation_a.anm.meta",
                    animationAMetaData),
            animationBMetaData == null
                ? null
                : new AnimationWorkbenchMetaDataSourceInput(
                    "animation_b.anm.meta",
                    animationBMetaData),
            SynchronizeMetaData: synchronizeMetaData));
        return document;
    }

    private sealed class RecordingPreviewHost :
        IAnimationWorkbenchPreviewHost
    {
        public TimeSpan? LastSeek { get; private set; }

        public IAnimationWorkbenchPreviewSession Show(
            AnimationWorkbenchPreviewSnapshot preview,
            CancellationToken cancellationToken) =>
            new RecordingPreviewSession(position => LastSeek = position);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingPreviewSession(Action<TimeSpan> seek) :
        IAnimationWorkbenchPreviewSession
    {
        public void Seek(TimeSpan position) => seek(position);

        public void Dispose()
        {
        }
    }

    private static byte[] CreateMetaBytes(
        MetaDataFileParser parser,
        params ParsedMetadataAttribute[] attributes) =>
        parser.GenerateBytes(
            2,
            new ParsedMetadataFile
            {
                Version = 2,
                Attributes = attributes.ToList(),
            });

    private static ParsedUnknownMetadataAttribute CreateUnknown(
        string name,
        int version,
        byte[] payload) => new()
        {
            Name = name,
            Version = version,
            Data = BitConverter.GetBytes(version).Concat(payload).ToArray(),
        };

    private static Time CreateTime(float startTime, float endTime) => new()
    {
        Name = "TIME",
        Version = 10,
        Data = [],
        StartTime = startTime,
        EndTime = endTime,
    };

    private static AnimationClip CreateClip(
        int boneCount = 1,
        int frameCount = 10,
        double durationSeconds = 1)
    {
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(durationSeconds),
        };
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new AnimationClip.KeyFrame();
            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                frame.Position.Add(new Vector3(frameIndex, boneIndex, 0));
                frame.Rotation.Add(Quaternion.Identity);
                frame.Scale.Add(Vector3.One);
            }
            clip.DynamicFrames.Add(frame);
        }

        return clip;
    }

    private static GameSkeleton CreateSkeleton(params string[] boneNames)
    {
        if (boneNames.Length == 0)
            boneNames = ["root"];
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "target_skeleton",
            },
            Bones = boneNames.Select((name, index) =>
                new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = name,
                    ParentId = index == 0
                        ? AnimationFile.BoneIndexNoParent
                        : 0,
                }).ToArray(),
        };
        var frame = new AnimationFile.Frame();
        foreach (var _ in boneNames)
        {
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return new GameSkeleton(file, new AnimationPlayer());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "animation-workbench-meta-sync-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
