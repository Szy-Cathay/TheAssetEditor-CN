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
    private const string SkeletonPath = @"animations\skeletons\tech\test.anim";
    private const string MatrixPath = @"animations\skeletons\tech\test.bone_inv_trans_mats";

    [Test]
    public void LoadingSelectingAndPreviewingStayClean_RealBoneEditsTrackSavedContent()
    {
        WithSkeleton((editor, files, project) =>
        {
            Assert.That(editor.HasUnsavedChanges, Is.False);
            editor.SelectedBone = editor.Bones[0];
            editor.ShowBonesAsWorldTransform = false;
            editor.ShowSkeleton = false;
            editor.ShowRefMesh = false;
            editor.BoneVisualScale = 2;
            Assert.That(editor.HasUnsavedChanges, Is.False);
            var originalName = editor.SelectedBoneName;
            editor.SelectedBoneName = "changed_root";
            Assert.That(editor.HasUnsavedChanges, Is.True);
            editor.LoadFile(editor.CurrentFile);
            Assert.That(editor.HasUnsavedChanges, Is.True);
            editor.SelectedBoneName = originalName;
            Assert.That(editor.HasUnsavedChanges, Is.False);
            editor.SelectedBoneTranslationOffset.X.Value = 2;
            Assert.That(editor.Save(), Is.True);
            Assert.That(editor.CurrentFile, Is.SameAs(files.FindFile(SkeletonPath, project)));
            Assert.That(files.GetFullPath(editor.CurrentFile), Is.EqualTo(SkeletonPath));
            Assert.That(files.FindFile(MatrixPath, project), Is.Not.Null);
            Assert.That(editor.HasUnsavedChanges, Is.False);
            editor.BoneScale = 1.5f;
            Assert.That(editor.Save(), Is.False);
            Assert.That(editor.HasUnsavedChanges, Is.True);
            editor.BakeSkeletonAction();
            Assert.That(editor.Save(), Is.True);
            editor.CreateBoneAction();
            Assert.That(editor.HasUnsavedChanges, Is.True);
            Assert.That(editor.Save(), Is.True);
            Assert.That(AnimationFile.Create(editor.CurrentFile).Bones, Has.Length.EqualTo(2));
            Assert.That(editor.HasUnsavedChanges, Is.False);
        });
    }

    [Test]
    public void BlockedMatrixWritePreservesSkeletonAndDirtyStateUntilRetry()
    {
        WithSkeleton((editor, files, project) =>
        {
            var source = editor.CurrentFile;
            var originalBytes = source.DataSource.ReadData();
            editor.SelectedBone = editor.Bones[0];
            editor.SelectedBoneName = "edited_root";
            var blockedPath = Path.Combine(project.ProjectRoot, MatrixPath);
            Directory.CreateDirectory(blockedPath);
            try
            {
                Assert.That(editor.Save(), Is.False);
                Assert.That(files.FindFile(SkeletonPath, project), Is.Null);
                Assert.That(File.Exists(Path.Combine(project.ProjectRoot, SkeletonPath)), Is.False);
                Assert.That(editor.CurrentFile, Is.SameAs(source));
                Assert.That(source.DataSource.ReadData(), Is.EqualTo(originalBytes));
                Assert.That(editor.HasUnsavedChanges, Is.True);
                var manager = new EditorManager(Mock.Of<IGlobalEventHub>(), files, Mock.Of<IEditorDatabase>(),
                    (_, _, _) => System.Windows.MessageBoxResult.Cancel);
                manager.CurrentEditorsList.Add(editor);
                manager.CloseTool(editor);
                Assert.That(manager.CurrentEditorsList, Does.Contain(editor));
            }
            finally { Directory.Delete(blockedPath); }
            Assert.That(editor.Save(), Is.True);
            Assert.That(editor.HasUnsavedChanges, Is.False);
            Assert.That(AnimationFile.Create(editor.CurrentFile).Bones[0].Name, Is.EqualTo("edited_root"));
            Assert.That(File.Exists(blockedPath), Is.True);
        });
    }

    [Test]
    public void MissingEditableProjectKeepsPendingChangesForRetry()
    {
        WithSkeleton((editor, files, project) =>
        {
            editor.SelectedBone = editor.Bones[0];
            editor.SelectedBoneName = "edited_root";
            files.SetEditablePack(null);
            Assert.That(editor.Save(), Is.False);
            Assert.That(editor.HasUnsavedChanges, Is.True);
            files.SetEditablePack(project);
            Assert.That(editor.Save(), Is.True);
            Assert.That(editor.HasUnsavedChanges, Is.False);
        });
    }

    private static void WithSkeleton(Action<SkeletonEditorViewModel, IPackFileService, FolderProjectContainer> action)
    {
        var root = Directory.CreateTempSubdirectory("ae-skeleton-save-");
        try
        {
            using var project = FolderProjectContainer.Create(root.FullName, new FolderProjectSettings { Name = "Skeleton" });
            var files = new PackFileService(Mock.Of<IGlobalEventHub>());
            var sourcePack = new PackFileContainer("Reference") { IsCaPackFile = true, SystemFilePath = "reference.pack" };
            var skeleton = GameSkeleton.CreateFromAnimationFile(new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader { SkeletonName = "test" },
                Bones = [new AnimationFile.BoneInfo { Id = 0, ParentId = AnimationFile.BoneIndexNoParent, Name = "root" }],
            }, new AnimationPlayer());
            var animation = AnimationClip.CreateSkeletonAnimation(skeleton).ConvertToFileFormat(skeleton);
            var file = PackFile.CreateFromBytes("test.anim", AnimationFile.ConvertToBytes(animation));
            sourcePack.FileList.Add(SkeletonPath, file);
            files.AddContainer(sourcePack);
            files.AddEditableFolderProject(project);
            EditorTabDirtyStateTests.WithServices(services =>
            {
                using var scope = services.CreateScope();
                var editor = scope.ServiceProvider.GetRequiredService<SkeletonEditorViewModel>();
                try
                {
                    editor.LoadFile(file);
                    Assert.That(editor.IsTechSkeleton, Is.True);
                    Assert.That(editor.Bones, Is.Not.Empty);
                    action(editor, files, project);
                }
                finally { editor.Close(); }
            }, registrations => registrations.AddSingleton<IPackFileService>(files));
        }
        finally { root.Delete(true); }
    }
}
