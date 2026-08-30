using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace AssetEditorTests;

[TestClass]
public class AnimationWorkbenchPoseEditingTests
{
    [TestMethod]
    public void InsertCopyDeleteAndPaste_ChangeOnlyTheIndependentResult()
    {
        var source = CreateClip(
            (new Vector3(1, 0, 0), new Vector3(2, 0, 0)),
            (new Vector3(3, 0, 0), new Vector3(4, 0, 0)));
        using var document = CreateLoadedDocument(source);

        var copied = document.CopyPoseFrame(0, ["child"]);
        var inserted = document.InsertPoseFrame(1, 0);
        var pasted = document.PastePoseFrame(2, copied.Clipboard!);
        var deleted = document.DeletePoseFrame(1);
        var result = document.SelectPreview(
            AnimationWorkbenchPreviewKind.Result).CurrentPreview!.Animation;
        var animationA = document.SelectPreview(
            AnimationWorkbenchPreviewKind.AnimationA).CurrentPreview!.Animation;

        Assert.IsTrue(copied.Succeeded);
        Assert.IsFalse(copied.Clipboard!.IsCompletePose);
        Assert.IsTrue(inserted.Succeeded);
        Assert.IsTrue(pasted.Succeeded);
        Assert.IsTrue(deleted.Succeeded);
        Assert.AreEqual(2, result.DynamicFrames.Count);
        Assert.AreEqual(source.Duration, result.Duration);
        Assert.AreEqual(new Vector3(3, 0, 0), result.DynamicFrames[1].Position[0]);
        Assert.AreEqual(new Vector3(2, 0, 0), result.DynamicFrames[1].Position[1]);
        Assert.AreEqual(new Vector3(4, 0, 0), animationA.DynamicFrames[1].Position[1]);
        Assert.AreEqual(new Vector3(4, 0, 0), source.DynamicFrames[1].Position[1]);
    }

    [TestMethod]
    public void PasteWholePoseAcrossManyBones_CreatesOneUndoRedoHistoryEntry()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (new Vector3(1, 0, 0), new Vector3(2, 0, 0)),
            (new Vector3(3, 0, 0), new Vector3(4, 0, 0))));
        var clipboard = document.CopyPoseFrame(0).Clipboard!;

        var pasted = document.PastePoseFrame(1, clipboard);
        var afterPaste = ResultClip(document);

        Assert.IsTrue(pasted.Succeeded);
        Assert.IsTrue(pasted.State.CanUndo);
        Assert.IsFalse(pasted.State.CanRedo);
        Assert.AreEqual(new Vector3(1, 0, 0), afterPaste.DynamicFrames[1].Position[0]);
        Assert.AreEqual(new Vector3(2, 0, 0), afterPaste.DynamicFrames[1].Position[1]);

        var undone = document.Undo();
        var afterUndo = ResultClip(document);

        Assert.IsTrue(undone.Succeeded);
        Assert.IsFalse(undone.State.CanUndo);
        Assert.IsTrue(undone.State.CanRedo);
        Assert.AreEqual(new Vector3(3, 0, 0), afterUndo.DynamicFrames[1].Position[0]);
        Assert.AreEqual(new Vector3(4, 0, 0), afterUndo.DynamicFrames[1].Position[1]);

        var redone = document.Redo();
        var afterRedo = ResultClip(document);

        Assert.IsTrue(redone.Succeeded);
        Assert.IsTrue(redone.State.CanUndo);
        Assert.IsFalse(redone.State.CanRedo);
        Assert.AreEqual(new Vector3(1, 0, 0), afterRedo.DynamicFrames[1].Position[0]);
        Assert.AreEqual(new Vector3(2, 0, 0), afterRedo.DynamicFrames[1].Position[1]);
    }

    [TestMethod]
    public void DeleteOnlyPoseFrame_IsRejectedWithoutChangingResultOrHistory()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));

        var deletion = document.DeletePoseFrame(0);

        Assert.IsFalse(deletion.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.PoseLastFrameDeleteRejected,
            deletion.Diagnostics.Single().Code);
        Assert.AreEqual(1, ResultClip(document).DynamicFrames.Count);
        Assert.IsFalse(deletion.State.CanUndo);
    }

    [TestMethod]
    public void ExactPoseEdit_WithInvalidNumber_IsRejectedAtomically()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));
        var original = ResultClip(document);

        var edit = document.ApplyExactPoseTransforms(
            0,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["root"] = new(
                    new Vector3(float.NaN, 0, 0),
                    Quaternion.Identity,
                    Vector3.One),
                ["child"] = new(
                    new Vector3(99, 0, 0),
                    Quaternion.Identity,
                    Vector3.One),
            });

        Assert.IsFalse(edit.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.PoseTransformInvalid,
            edit.Diagnostics.Single().Code);
        AssertFrameEqual(original.DynamicFrames[0], ResultClip(document).DynamicFrames[0]);
        Assert.IsFalse(edit.State.CanUndo);
    }

    [TestMethod]
    public void ExactPoseEdit_WithNonUnitScale_IsRejectedBeforeHistory()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));
        var original = ResultClip(document);

        var edit = document.ApplyExactPoseTransforms(
            0,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["root"] = new(
                    Vector3.One,
                    Quaternion.Identity,
                    new Vector3(2, 1, 1)),
            });

        Assert.IsFalse(edit.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.PoseTransformInvalid,
            edit.Diagnostics.Single().Code);
        AssertFrameEqual(original.DynamicFrames[0], ResultClip(document).DynamicFrames[0]);
        Assert.IsFalse(edit.State.CanUndo);
    }

    [TestMethod]
    public void CopyPoseFrame_WithDuplicateBones_IsRejected()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));

        var copy = document.CopyPoseFrame(0, ["root", "root"]);

        Assert.IsFalse(copy.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete,
            copy.Diagnostics.Single().Code);
        Assert.IsNull(copy.Clipboard);
        Assert.IsFalse(copy.State.CanUndo);
    }

    [TestMethod]
    public void PastePose_WithMissingBone_IsRejectedAtomically()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));
        var clipboard = new AnimationWorkbenchPoseClipboard(
            false,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["missing"] = new(
                    new Vector3(99, 0, 0),
                    Quaternion.Identity,
                    Vector3.One),
            });

        var paste = document.PastePoseFrame(0, clipboard);

        Assert.IsFalse(paste.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.PoseBoneMissing,
            paste.Diagnostics.Single().Code);
        Assert.AreEqual("missing", paste.Diagnostics.Single().BoneName);
        Assert.AreEqual(Vector3.One, ResultClip(document).DynamicFrames[0].Position[0]);
        Assert.IsFalse(paste.State.CanUndo);
    }

    [TestMethod]
    public void PasteWholePose_WithIncompleteClipboard_IsRejectedAtomically()
    {
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));
        var clipboard = new AnimationWorkbenchPoseClipboard(
            true,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["root"] = new(
                    new Vector3(99, 0, 0),
                    Quaternion.Identity,
                    Vector3.One),
            });

        var paste = document.PastePoseFrame(0, clipboard);

        Assert.IsFalse(paste.Succeeded);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete,
            paste.Diagnostics.Single().Code);
        Assert.AreEqual(Vector3.One, ResultClip(document).DynamicFrames[0].Position[0]);
        Assert.IsFalse(paste.State.CanUndo);
    }

    [TestMethod]
    public void SaveBaseline_UndoToSavedClearsDirtyAndRedoRestoresDirty()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));

        var saved = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\saved.anim");
        var edited = document.ApplyExactPoseTransforms(
            0,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["root"] = new(
                    new Vector3(9, 0, 0),
                    Quaternion.Identity,
                    Vector3.One),
            });
        var undone = document.Undo();
        var redone = document.Redo();

        Assert.IsTrue(saved.Succeeded);
        Assert.IsFalse(saved.State.IsDirty);
        Assert.IsTrue(edited.State.IsDirty);
        Assert.IsFalse(undone.State.IsDirty);
        Assert.IsTrue(redone.State.IsDirty);
    }

    [TestMethod]
    public void PasteIdenticalPoseAfterSave_RemainsCleanWithoutHistoryEntry()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));

        var saved = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\saved.anim");
        var clipboard = document.CopyPoseFrame(0).Clipboard!;
        var pasted = document.PastePoseFrame(0, clipboard);

        Assert.IsTrue(saved.Succeeded);
        Assert.IsTrue(pasted.Succeeded);
        Assert.IsFalse(pasted.State.IsDirty);
        Assert.IsFalse(pasted.State.CanUndo);
    }

    [TestMethod]
    public void ExactEditBackToSavedPose_ClearsDirtyByContent()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        using var document = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));

        var saved = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\saved.anim");
        var changed = document.ApplyExactPoseTransforms(
            0,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["root"] = new(
                    new Vector3(9, 0, 0),
                    Quaternion.Identity,
                    Vector3.One),
            });
        var restored = document.ApplyExactPoseTransforms(
            0,
            new Dictionary<string, AnimationWorkbenchBoneTransform>
            {
                ["root"] = new(
                    Vector3.One,
                    Quaternion.Identity,
                    Vector3.One),
            });

        Assert.IsTrue(saved.Succeeded);
        Assert.IsTrue(changed.State.IsDirty);
        Assert.IsTrue(restored.Succeeded);
        Assert.IsFalse(restored.State.IsDirty);
        Assert.IsTrue(restored.State.CanUndo);
    }

    [TestMethod]
    public void PosePreview_CancelCommitAndExactEditProduceConsistentSaveResult()
    {
        using var previewDocument = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));
        using var exactDocument = CreateLoadedDocument(CreateClip(
            (Vector3.One, new Vector3(2))));
        var transforms = new Dictionary<string, AnimationWorkbenchBoneTransform>
        {
            ["root"] = new(
                new Vector3(9, 8, 7),
                Quaternion.CreateFromYawPitchRoll(0.2f, 0.3f, 0.4f),
                Vector3.One),
        };

        Assert.IsTrue(previewDocument.BeginPosePreview(0).Succeeded);
        var previewed = previewDocument.PreviewPoseTransforms(transforms);
        Assert.IsTrue(previewed.Succeeded);
        Assert.IsTrue(previewed.State.HasActivePosePreview);
        Assert.AreEqual(
            new Vector3(9, 8, 7),
            ResultClip(previewDocument).DynamicFrames[0].Position[0]);

        var cancelled = previewDocument.CancelPosePreview();
        Assert.IsTrue(cancelled.Succeeded);
        Assert.IsFalse(cancelled.State.HasActivePosePreview);
        Assert.AreEqual(Vector3.One, ResultClip(previewDocument).DynamicFrames[0].Position[0]);
        Assert.IsFalse(cancelled.State.CanUndo);

        Assert.IsTrue(previewDocument.BeginPosePreview(0).Succeeded);
        Assert.IsTrue(previewDocument.PreviewPoseTransforms(transforms).Succeeded);
        var committed = previewDocument.CommitPosePreview();
        var exact = exactDocument.ApplyExactPoseTransforms(0, transforms);

        Assert.IsTrue(committed.Succeeded);
        Assert.IsTrue(exact.Succeeded);
        Assert.IsTrue(committed.State.CanUndo);
        CollectionAssert.AreEqual(
            CaptureSavedBytes(previewDocument, "preview.anim"),
            CaptureSavedBytes(exactDocument, "exact.anim"));
    }

    private static AnimationWorkbenchDocument CreateLoadedDocument(AnimationClip animation)
    {
        var skeleton = CreateSkeleton();
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

    private static AnimationClip CreateClip(
        params (Vector3 Root, Vector3 Child)[] poses)
    {
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(poses.Length * 0.05),
        };
        foreach (var pose in poses)
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.Add(pose.Root);
            frame.Position.Add(pose.Child);
            frame.Rotation.Add(Quaternion.Identity);
            frame.Rotation.Add(Quaternion.Identity);
            frame.Scale.Add(Vector3.One);
            frame.Scale.Add(Vector3.One);
            clip.DynamicFrames.Add(frame);
        }

        return clip;
    }

    private static GameSkeleton CreateSkeleton()
    {
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "target_skeleton",
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = AnimationFile.BoneIndexNoParent,
                },
                new AnimationFile.BoneInfo
                {
                    Id = 1,
                    Name = "child",
                    ParentId = 0,
                },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts.Add(part);
        return new GameSkeleton(skeletonFile, new AnimationPlayer());
    }

    private static byte[] CaptureSavedBytes(
        AnimationWorkbenchDocument document,
        string fileName)
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        byte[]? bytes = null;
        packFileService
            .Setup(service => service.AddFilesToPack(
                project,
                It.IsAny<List<NewPackFileEntry>>(),
                false))
            .Callback<PackFileContainer, List<NewPackFileEntry>, bool>(
                (_, entries, _) =>
                    bytes = entries.Single().PackFile.DataSource.ReadData());

        var result = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            $@"animations\{fileName}");

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(bytes);
        return bytes;
    }

    private static void AssertFrameEqual(
        AnimationClip.KeyFrame expected,
        AnimationClip.KeyFrame actual)
    {
        CollectionAssert.AreEqual(expected.Position, actual.Position);
        CollectionAssert.AreEqual(expected.Rotation, actual.Rotation);
        CollectionAssert.AreEqual(expected.Scale, actual.Scale);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"animation-workbench-edit-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
