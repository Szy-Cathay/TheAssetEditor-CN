using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.Saving;
using Editors.Shared.Core.Common;
using GameWorld.Core.Services;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.Ui.Common.OperationProgress;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimatioReTarget;

public class BatchAnimationRetargetServiceTests
{
    private readonly string _inputPackFile =
        PathHelper.GetDataFile("Karl_and_celestialgeneral.pack");

    [Test]
    public void ExecuteBatchAsync_SelectedFolder_PreservesContainingFolder()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string expectedOutputPath =
            "animations\\battle\\batch-output\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);

        var batchTask = context.Editor.SaveManager.ExecuteBatchAsync(
            new BatchAnimationRetargetRequest(
                [new AnimationReference(sourceAnimationPath, context.SourceContainer)],
                BatchRetargetPathMode.SelectedFolder,
                "animations\\battle\\batch-output",
                OverwriteExisting: false),
            progress: null,
            CancellationToken.None);

        Assert.That(
            batchTask.Wait(TimeSpan.FromSeconds(5)),
            Is.True,
            "Batch retargeting did not return after writing the output file.");
        var result = batchTask.GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailureCount, Is.Zero);
            Assert.That(result.SkippedCount, Is.Zero);
            Assert.That(
                context.OutputProject.FileList,
                Does.ContainKey(expectedOutputPath));
            Assert.That(
                File.Exists(Path.Combine(
                    context.ProjectRoot.Path,
                    expectedOutputPath)),
                Is.True);
        });
    }

    [Test]
    public void ExecuteBatchAsync_SourcePath_MatchesSingleAnimationRule()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string expectedOutputPath =
            "animations\\battle\\humanoid01e\\2handed_hammer\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [new AnimationReference(
                        sourceAnimationPath,
                        context.SourceContainer)],
                    BatchRetargetPathMode.SourcePath,
                    TargetFolder: null,
                    OverwriteExisting: false),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailureCount, Is.Zero);
            Assert.That(
                context.OutputProject.FileList,
                Does.ContainKey(expectedOutputPath));
        });
    }

    [Test]
    public void ExecuteBatchAsync_ExistingOutput_SkipsByDefault()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string expectedOutputPath =
            "animations\\battle\\batch-output\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        byte[] existingContent = [1, 2, 3, 4];
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        context.OutputProject.ApplyFileWrites(
            [new PackFileWrite(expectedOutputPath, existingContent)]);

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [new AnimationReference(
                        sourceAnimationPath,
                        context.SourceContainer)],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: false),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.Zero);
            Assert.That(result.SkippedCount, Is.EqualTo(1));
            Assert.That(
                File.ReadAllBytes(Path.Combine(
                    context.ProjectRoot.Path,
                    expectedOutputPath)),
                Is.EqualTo(existingContent));
        });
    }

    [Test]
    public void ExecuteBatchAsync_ExistingOutput_OverwritesWhenRequested()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string expectedOutputPath =
            "animations\\battle\\batch-output\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        byte[] existingContent = [1, 2, 3, 4];
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        context.OutputProject.ApplyFileWrites(
            [new PackFileWrite(expectedOutputPath, existingContent)]);

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [new AnimationReference(
                        sourceAnimationPath,
                        context.SourceContainer)],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: true),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.SkippedCount, Is.Zero);
            Assert.That(
                File.ReadAllBytes(Path.Combine(
                    context.ProjectRoot.Path,
                    expectedOutputPath)),
                Is.Not.EqualTo(existingContent));
        });
    }

    [Test]
    public void ExecuteBatchAsync_DuplicateOutputs_SkipsLaterItemWhenOverwriting()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        var animation = new AnimationReference(
            sourceAnimationPath,
            context.SourceContainer);

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [animation, animation],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: true),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.SkippedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecuteBatchAsync_FailedAnimation_DoesNotStopRemainingItems()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string missingAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\attacks\\missing.anim";
        const string expectedOutputPath =
            "animations\\battle\\batch-output\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [
                        new AnimationReference(
                            missingAnimationPath,
                            context.SourceContainer),
                        new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer),
                    ],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: false),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.Items[0].SourcePath, Is.EqualTo(missingAnimationPath));
            Assert.That(
                context.OutputProject.FileList,
                Does.ContainKey(expectedOutputPath));
        });
    }

    [Test]
    public void ExecuteBatchAsync_InvalidOutputPath_DoesNotStopRemainingItems()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string invalidSourcePath = "orphan.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [
                        new AnimationReference(
                            invalidSourcePath,
                            context.SourceContainer),
                        new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer),
                    ],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: false),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.Items[0].SourcePath, Is.EqualTo(invalidSourcePath));
        });
    }

    [Test]
    public void ExecuteBatchAsync_TargetFolderOutsideCurrentProject_DoesNotWriteFiles()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);

        Assert.That(
            () => context.Editor.SaveManager.ExecuteBatchAsync(
                    new BatchAnimationRetargetRequest(
                        [new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer)],
                        BatchRetargetPathMode.SelectedFolder,
                        "animations\\battle\\not-in-project",
                        OverwriteExisting: false),
                    progress: null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            Throws.InvalidOperationException);
        Assert.That(context.OutputProject.FileList, Is.Empty);
    }

    [Test]
    public void ExecuteBatchAsync_CancelAfterFirstItem_ReportsRemainingItems()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string expectedOutputPath =
            "animations\\battle\\batch-output\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<OperationProgressUpdate>(update =>
        {
            if (update.Completed == 1)
                cancellation.Cancel();
        });

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [
                        new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer),
                        new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer),
                    ],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: true),
                progress,
                cancellation.Token)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.NotProcessedCount, Is.EqualTo(1));
            Assert.That(
                context.OutputProject.FileList,
                Does.ContainKey(expectedOutputPath));
        });
    }

    [Test]
    public void ExecuteBatchAsync_NoBoneMappings_DoesNotWriteFiles()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        foreach (var bone in context.Editor.BoneManager.FlatBoneList)
        {
            bone.HasMapping = false;
            bone.MappedIndex = -1;
        }

        Assert.That(
            () => context.Editor.SaveManager.ExecuteBatchAsync(
                    new BatchAnimationRetargetRequest(
                        [new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer)],
                        BatchRetargetPathMode.SelectedFolder,
                        "animations\\battle\\batch-output",
                        OverwriteExisting: false),
                    progress: null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            Throws.InvalidOperationException);
        Assert.That(context.OutputProject.FileList, Is.Empty);
    }

    [Test]
    public void ExecuteBatchAsync_InvalidSpeed_DoesNotWriteFiles()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        context.Editor.Settings.AnimationSpeedMult = 0;

        Assert.That(
            () => context.Editor.SaveManager.ExecuteBatchAsync(
                    new BatchAnimationRetargetRequest(
                        [new AnimationReference(
                            sourceAnimationPath,
                            context.SourceContainer)],
                        BatchRetargetPathMode.SelectedFolder,
                        "animations\\battle\\batch-output",
                        OverwriteExisting: false),
                    progress: null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            Throws.InvalidOperationException);
        Assert.That(context.OutputProject.FileList, Is.Empty);
    }

    [Test]
    public void GetBatchCandidates_ExcludesSkeletonFiles()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);

        var candidates = context.Editor.SaveManager.GetBatchCandidates();

        Assert.Multiple(() =>
        {
            Assert.That(
                candidates.Select(candidate => candidate.AnimationFile),
                Does.Contain(sourceAnimationPath));
            Assert.That(
                candidates.Select(candidate => candidate.AnimationFile),
                Has.None.Matches<string>(path =>
                    SaveManager.IsSkeletonAnimationPath(path)));
        });
    }

    [Test]
    public void IsSkeletonAnimationPath_MatchesOnlyAnimationsSkeletons()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                SaveManager.IsSkeletonAnimationPath(
                    "animations\\skeletons\\humanoid01.anim"),
                Is.True);
            Assert.That(
                SaveManager.IsSkeletonAnimationPath(
                    "animations\\battle\\humanoid16\\skeletons\\idle.anim"),
                Is.False);
        });
    }

    [Test]
    public void SkeletonScaleZero_DisablesPreviewAndBatchRetarget()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        context.Editor.Settings.SkeletonScale = 0;

        var canUpdate = context.Editor.CanUpdateAnimation(
            out var updateError);
        var canBatch = context.Editor.SaveManager.CanBatchRetarget(
            out var batchError);

        Assert.Multiple(() =>
        {
            Assert.That(canUpdate, Is.False);
            Assert.That(canBatch, Is.False);
            Assert.That(updateError, Is.Not.Empty);
            Assert.That(batchError, Is.EqualTo(updateError));
        });
    }

    [Test]
    public void BoneLengthMultiplierZero_DisablesPreviewAndBatchRetarget()
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        context.Editor.BoneManager.FlatBoneList[0].BoneLengthMult = 0;

        var canUpdate = context.Editor.CanUpdateAnimation(
            out var updateError);
        var canBatch = context.Editor.SaveManager.CanBatchRetarget(
            out var batchError);

        Assert.Multiple(() =>
        {
            Assert.That(canUpdate, Is.False);
            Assert.That(canBatch, Is.False);
            Assert.That(updateError, Is.Not.Empty);
            Assert.That(batchError, Is.EqualTo(updateError));
        });
    }

    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    public void ExecuteBatchAsync_UsesSelectedAnimationFormat(
        uint animationFormat)
    {
        const string sourceAnimationPath =
            "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
        const string expectedOutputPath =
            "animations\\battle\\batch-output\\stand\\prefix_hu1_2hh_stand_idle_01.anim";
        using var context = new BatchTestContext(
            _inputPackFile,
            sourceAnimationPath);
        context.Editor.SaveManager.Settings.AnimationFormat = animationFormat;

        var result = context.Editor.SaveManager.ExecuteBatchAsync(
                new BatchAnimationRetargetRequest(
                    [new AnimationReference(
                        sourceAnimationPath,
                        context.SourceContainer)],
                    BatchRetargetPathMode.SelectedFolder,
                    "animations\\battle\\batch-output",
                    OverwriteExisting: false),
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(
            result.SuccessCount,
            Is.EqualTo(1),
            result.Items.Single().ErrorMessage);
        var outputAnimation = AnimationFile.Create(
            context.OutputProject.FileList[expectedOutputPath]);

        Assert.That(
            outputAnimation.Header.Version,
            Is.EqualTo(animationFormat));
    }

    [Test]
    public void IsTechAnimation_MatchesOnlyTechPathSegment()
    {
        var container = new PackFileContainer("candidate source");

        Assert.Multiple(() =>
        {
            Assert.That(
                SaveManager.IsTechAnimation(new AnimationReference(
                    "animations\\battle\\humanoid16\\tech\\test.anim",
                    container)),
                Is.True);
            Assert.That(
                SaveManager.IsTechAnimation(new AnimationReference(
                    "animations\\battle\\humanoid16\\attacks\\technique.anim",
                    container)),
                Is.False);
        });
    }

    private sealed class BatchTestContext : IDisposable
    {
        public TemporaryDirectory ProjectRoot { get; }
        public FolderProjectContainer OutputProject { get; }
        public PackFileContainer SourceContainer { get; }
        public AnimationRetargetEditor Editor { get; }

        public BatchTestContext(
            string inputPackFile,
            string sourceAnimationPath)
        {
            ProjectRoot = new TemporaryDirectory();
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(
                inputPackFile,
                createOutputPackFile: false);
            SourceContainer = runner.PackFileService
                .GetAllPackfileContainers()
                .Single(container => !container.IsCaPackFile);
            OutputProject = FolderProjectContainer.Create(
                ProjectRoot.Path,
                new FolderProjectSettings { Name = "批量重定向测试" });
            OutputProject.CreateDirectoryOnDisk(
                "animations\\battle\\batch-output");
            runner.PackFileService.AddEditableFolderProject(OutputProject);
            Editor = (AnimationRetargetEditor)runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute(EditorEnums.AnimationRetarget_Editor);
            var sceneObjectEditor = runner
                .GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            var source = Editor
                .GetSceneObjectFromId(AnimationRetargetIds.Source)!
                .Data;
            var target = Editor
                .GetSceneObjectFromId(AnimationRetargetIds.Target)!
                .Data;
            sceneObjectEditor.SetMesh(
                source,
                runner.PackFileService.FindFile(
                    "variantmeshes\\wh_variantmodels\\hu1\\emp\\emp_karl_franz\\emp_karl_franz.wsmodel")!);
            sceneObjectEditor.SetAnimation(source, sourceAnimationPath);
            sceneObjectEditor.SetMesh(
                target,
                runner.PackFileService.FindFile(
                    "variantmeshes\\wh_variantmodels\\hu1e\\cth\\cth_celestial_general\\cth_celestial_general_body_02.wsmodel")!);
            Editor.BoneManager.ApplyDefaultMapping();
        }

        public void Dispose()
        {
            OutputProject.Dispose();
            ProjectRoot.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-batch-retarget-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         Path,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(Path, true);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
