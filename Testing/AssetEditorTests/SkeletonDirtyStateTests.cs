using AssetEditor.Services;
using Editor.VisualSkeletonEditor.SkeletonEditor;
using GameWorld.Core.Animation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class SkeletonDirtyStateTests
{
    [Test]
    public void LoadingSelectingAndPreviewingStayClean_RealBoneEditsTrackSavedContent()
    {
        WithSkeleton((editor, saveable, _, _) =>
        {
            Assert.That(saveable.HasUnsavedChanges, Is.False);
            editor.SelectedBone = editor.Bones[0];
            editor.ShowBonesAsWorldTransform = false;
            editor.ShowSkeleton = false;
            editor.ShowRefMesh = false;
            editor.BoneVisualScale = 2;
            Assert.That(saveable.HasUnsavedChanges, Is.False);
            var originalName = editor.SelectedBoneName;
            editor.SelectedBoneName = "changed_root";
            Assert.That(saveable.HasUnsavedChanges, Is.True);
            editor.LoadFile(editor.CurrentFile);
            Assert.That(saveable.HasUnsavedChanges, Is.True, "Re-selecting the same loaded skeleton must not clear pending edits.");
            editor.SelectedBoneName = originalName;
            Assert.That(saveable.HasUnsavedChanges, Is.False);
            editor.SelectedBoneTranslationOffset.X.Value = 2;
            Assert.That(saveable.HasUnsavedChanges, Is.True);
            Assert.That(saveable.Save(), Is.True);
            Assert.That(saveable.HasUnsavedChanges, Is.False);
            editor.BoneScale = 1.5f;
            Assert.That(saveable.HasUnsavedChanges, Is.True);
            Assert.That(saveable.Save(), Is.False, "Unbaked bone scale must not clear the dirty state.");
            Assert.That(saveable.HasUnsavedChanges, Is.True);
            editor.BakeSkeletonAction();
            Assert.That(saveable.Save(), Is.True);
            Assert.That(saveable.HasUnsavedChanges, Is.False);
            editor.CreateBoneAction();
            Assert.That(saveable.HasUnsavedChanges, Is.True);
            Assert.That(saveable.Save(), Is.True);
            Assert.That(saveable.HasUnsavedChanges, Is.False);
        });
    }

    [TestCase("skeleton-cancel")]
    [TestCase("matrix-cancel")]
    [TestCase("write-error")]
    public void SaveFailureKeepsDirtyAndCloseProtection(string failure)
    {
        WithSkeleton((editor, saveable, files, save) =>
        {
            editor.SelectedBone = editor.Bones[0];
            editor.SelectedBoneName = "edited_root";
            if (failure == "write-error")
                save.Setup(item => item.Save(It.IsAny<string>(), It.IsAny<byte[]>(), false)).Throws(new IOException("Test write failure"));
            else
                save.Setup(item => item.Save(failure == "skeleton-cancel" ? SkeletonPath : MatrixPath, It.IsAny<byte[]>(), false))
                    .Returns((PackFile?)null);
            if (failure == "write-error")
                Assert.Throws<IOException>(() => saveable.Save());
            else
                Assert.That(saveable.Save(), Is.False);
            Assert.That(saveable.HasUnsavedChanges, Is.True);
            var prompts = 0;
            var manager = new EditorManager(Mock.Of<IGlobalEventHub>(), files.Object, Mock.Of<IEditorDatabase>(), (_, _, _) =>
            {
                prompts++;
                return System.Windows.MessageBoxResult.Cancel;
            });
            manager.CurrentEditorsList.Add(editor);
            manager.CloseTool(editor);
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(manager.CurrentEditorsList, Does.Contain(editor));
            ConfigureSuccessfulSave(save);
            Assert.That(saveable.Save(), Is.True);
            Assert.That(saveable.HasUnsavedChanges, Is.False);
            manager.CloseTool(editor);
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(manager.CurrentEditorsList, Is.Empty);
        });
    }

    private const string SkeletonPath = @"animations\skeletons\test.anim";
    private const string MatrixPath = @"animations\skeletons\test.bone_inv_trans_mats";

    private static void WithSkeleton(Action<SkeletonEditorViewModel, ISaveableEditor, Mock<IPackFileService>, Mock<IFileSaveService>> action)
    {
        var files = new Mock<IPackFileService>();
        files.Setup(item => item.GetAllPackfileContainers()).Returns([]);
        var save = new Mock<IFileSaveService>();
        ConfigureSuccessfulSave(save);
        var skeleton = GameSkeleton.CreateFromAnimationFile(new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "test" },
            Bones = [new AnimationFile.BoneInfo { Id = 0, ParentId = AnimationFile.BoneIndexNoParent, Name = "root" }],
        }, new AnimationPlayer());
        var animation = AnimationClip.CreateSkeletonAnimation(skeleton).ConvertToFileFormat(skeleton);
        var file = PackFile.CreateFromBytes("test.anim", AnimationFile.ConvertToBytes(animation));
        files.Setup(item => item.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>())).Returns(SkeletonPath);
        files.Setup(item => item.FindFile(SkeletonPath, It.IsAny<PackFileContainer?>())).Returns(file);
        EditorTabDirtyStateTests.WithServices(services =>
        {
            using var scope = services.CreateScope();
            var editor = scope.ServiceProvider.GetRequiredService<SkeletonEditorViewModel>();
            try
            {
                Assert.That(editor, Is.InstanceOf<ISaveableEditor>(), "Editable skeleton data must participate in save/close protection.");
                var saveable = (ISaveableEditor)editor;
                Assert.That(saveable.HasUnsavedChanges, Is.False);
                editor.LoadFile(file);
                Assert.That(editor.Bones, Is.Not.Empty);
                Assert.That(saveable.HasUnsavedChanges, Is.False);
                action(editor, saveable, files, save);
            }
            finally { editor.Close(); }
        }, registrations =>
        {
            registrations.AddSingleton(files.Object);
            registrations.AddSingleton(save.Object);
        });
    }

    private static void ConfigureSuccessfulSave(Mock<IFileSaveService> save)
    {
        save.Reset();
        save.Setup(item => item.Save(It.IsAny<string>(), It.IsAny<byte[]>(), false))
            .Returns<string, byte[], bool>((path, bytes, _) => PackFile.CreateFromBytes(Path.GetFileName(path), bytes));
    }
}
