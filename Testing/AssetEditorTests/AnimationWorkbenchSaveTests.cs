using System.Text.Json;
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
public class AnimationWorkbenchSaveTests
{
    [TestMethod]
    public void SaveAsNewProjectResource_ValidWarhammer3Result_WritesValidatedCandidateWithoutOverwrite()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        List<NewPackFileEntry>? capturedEntries = null;
        packFileService
            .Setup(service => service.AddFilesToPack(
                project,
                It.IsAny<List<NewPackFileEntry>>(),
                false))
            .Callback<PackFileContainer, List<NewPackFileEntry>, bool>(
                (_, entries, _) => capturedEntries = entries);
        using var document = CreateLoadedDocument();

        var initialState = document.SelectPreview(
            AnimationWorkbenchPreviewKind.Result);
        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\battle\candidate.anim");

        Assert.IsTrue(initialState.IsDirty);
        Assert.IsTrue(saveResult.Succeeded);
        Assert.IsFalse(saveResult.State.IsDirty);
        Assert.AreEqual(
            @"animations\battle\candidate.anim",
            saveResult.State.ProjectResourcePath);
        Assert.AreEqual(0, saveResult.Diagnostics.Count);
        Assert.IsNotNull(capturedEntries);
        Assert.AreEqual(1, capturedEntries.Count);
        Assert.AreEqual(@"animations\battle", capturedEntries[0].DirectoyPath);
        Assert.AreEqual("candidate.anim", capturedEntries[0].PackFile.Name);

        var candidate = AnimationFile.Create(new ByteChunk(
            capturedEntries[0].PackFile.DataSource.ReadData()));
        Assert.AreEqual(7u, candidate.Header.Version);
        Assert.AreEqual("target_skeleton", candidate.Header.SkeletonName);
        Assert.AreEqual(1, candidate.AnimationParts.Count);
        Assert.IsNull(candidate.AnimationParts[0].StaticFrame);
        Assert.AreEqual(2, candidate.AnimationParts[0].DynamicFrames.Count);
        Assert.AreEqual(1, candidate.AnimationParts[0].TranslationMappings.Count);
        Assert.AreEqual(1, candidate.AnimationParts[0].RotationMappings.Count);
        Assert.AreEqual(
            AnimationFile.AnimationBoneMappingType.Dynamic,
            candidate.AnimationParts[0].TranslationMappings[0].MappingType);
        Assert.AreEqual(
            AnimationFile.AnimationBoneMappingType.Dynamic,
            candidate.AnimationParts[0].RotationMappings[0].MappingType);
        packFileService.VerifyAll();
    }

    [TestMethod]
    public void SaveAsNewProjectResource_CommittedTimelineEdit_WritesEditedFrames()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        List<NewPackFileEntry>? capturedEntries = null;
        packFileService
            .Setup(service => service.AddFilesToPack(
                project,
                It.IsAny<List<NewPackFileEntry>>(),
                false))
            .Callback<PackFileContainer, List<NewPackFileEntry>, bool>(
                (_, entries, _) => capturedEntries = entries);
        using var document = CreateLoadedDocument();
        Assert.IsTrue(document.BeginTimelinePreview().Succeeded);
        Assert.IsTrue(document.PreviewTrimRange(
            new AnimationWorkbenchFrameRange(0, 1)).Succeeded);
        Assert.IsTrue(document.CommitTimelinePreview().Succeeded);

        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\battle\timeline_candidate.anim");

        Assert.IsTrue(saveResult.Succeeded);
        Assert.IsNotNull(capturedEntries);
        var candidate = AnimationFile.Create(new ByteChunk(
            capturedEntries.Single().PackFile.DataSource.ReadData()));
        Assert.AreEqual(
            1,
            candidate.AnimationParts.Single().DynamicFrames.Count);
        packFileService.VerifyAll();
    }

    [DataTestMethod]
    [DataRow(8, 1, false, AnimationWorkbenchDiagnosticCode.SourceVersionEightReadOnly)]
    [DataRow(7, 2, false, AnimationWorkbenchDiagnosticCode.SourceMultiplePartsReadOnly)]
    [DataRow(7, 1, true, AnimationWorkbenchDiagnosticCode.SourceStaticFrameReadOnly)]
    [DataRow(9, 1, false, AnimationWorkbenchDiagnosticCode.SourceFormatUnsupported)]
    public void SaveAsNewProjectResource_UnverifiedSourceStructure_BlocksBeforeWrite(
        int version,
        int partCount,
        bool hasStaticFrame,
        AnimationWorkbenchDiagnosticCode expectedCode)
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>(MockBehavior.Strict);
        using var document = CreateLoadedDocument(
            new AnimationWorkbenchSourceFormat(
                (uint)version,
                partCount,
                hasStaticFrame));

        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\candidate.anim");

        Assert.IsFalse(saveResult.Succeeded);
        Assert.IsTrue(saveResult.State.IsDirty);
        Assert.IsNull(saveResult.State.ProjectResourcePath);
        Assert.IsTrue(saveResult.Diagnostics.Any(
            diagnostic => diagnostic.Code == expectedCode));
    }

    [TestMethod]
    public void SaveAsNewProjectResource_MissingSourceFormat_BlocksBeforeWrite()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>(MockBehavior.Strict);
        using var document = CreateLoadedDocument(omitFormat: true);

        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\candidate.anim");

        Assert.IsFalse(saveResult.Succeeded);
        Assert.IsTrue(saveResult.State.IsDirty);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.SourceFormatUnknown,
            saveResult.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void SaveAsNewProjectResource_ThreeKingdomsTarget_BlocksUntilItsWriterIsVerified()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>(MockBehavior.Strict);
        using var document = CreateLoadedDocument(
            targetGame: GameTypeEnum.ThreeKingdoms);

        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\candidate.anim");

        Assert.IsFalse(saveResult.Succeeded);
        Assert.IsTrue(saveResult.State.IsDirty);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.TargetGameSaveUnsupported,
            saveResult.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void SaveAsNewProjectResource_PoseDoesNotRoundTrip_BlocksBeforeWrite()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>(MockBehavior.Strict);
        var clip = CreateClip(new Vector3(1, 2, 3));
        clip.DynamicFrames[0].Scale[0] = new Vector3(2);
        using var document = CreateLoadedDocument(animation: clip);

        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\candidate.anim");

        Assert.IsFalse(saveResult.Succeeded);
        Assert.IsTrue(saveResult.State.IsDirty);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.CandidateRoundTripMismatch,
            saveResult.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void SaveAsNewProjectResource_WriteFails_KeepsDirtyAndResourceIdentityUnchanged()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.AddFilesToPack(
                project,
                It.IsAny<List<NewPackFileEntry>>(),
                false))
            .Throws(new IOException("simulated write failure"));
        using var document = CreateLoadedDocument();

        var saveResult = document.SaveAsNewProjectResource(
            packFileService.Object,
            project,
            @"animations\candidate.anim");

        Assert.IsFalse(saveResult.Succeeded);
        Assert.IsTrue(saveResult.State.IsDirty);
        Assert.IsNull(saveResult.State.ProjectResourcePath);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.DestinationWriteFailed,
            saveResult.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void SaveAsNewProjectResource_ExistingProjectFile_PreservesOriginalAndLeavesNoPartialFile()
    {
        using var projectRoot = new TemporaryDirectory();
        var destination = Path.Combine(
            projectRoot.Path,
            "animations",
            "candidate.anim");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var original = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(destination, original);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new PackFileService(null)
        {
            EnforceGameFilesMustBeLoaded = false,
        };
        packFileService.AddEditableFolderProject(project);
        using var document = CreateLoadedDocument();

        var saveResult = document.SaveAsNewProjectResource(
            packFileService,
            project,
            @"animations\candidate.anim");

        Assert.IsFalse(saveResult.Succeeded);
        Assert.IsTrue(saveResult.State.IsDirty);
        Assert.IsNull(saveResult.State.ProjectResourcePath);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.DestinationAlreadyExists,
            saveResult.Diagnostics.Single().Code);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(destination));
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                Path.GetDirectoryName(destination)!,
                "*.tmp").Length);
    }

    [TestMethod]
    public void ExportDiskCopy_ValidCandidate_WritesAtomicallyWithoutChangingDocumentIdentity()
    {
        using var output = new TemporaryDirectory();
        using var document = CreateLoadedDocument();
        var destination = Path.Combine(output.Path, "copy", "candidate.anim");

        var exportResult = document.ExportDiskCopy(destination);

        Assert.IsTrue(exportResult.Succeeded);
        Assert.IsTrue(exportResult.State.IsDirty);
        Assert.IsNull(exportResult.State.ProjectResourcePath);
        Assert.IsTrue(File.Exists(destination));
        Assert.AreEqual(7u, AnimationFile.Create(
            new ByteChunk(File.ReadAllBytes(destination))).Header.Version);
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                Path.GetDirectoryName(destination)!,
                "*.tmp").Length);
    }

    [TestMethod]
    public void ExportDiskCopy_DestinationExists_PreservesOriginalAndRemovesTemporaryFile()
    {
        using var output = new TemporaryDirectory();
        using var document = CreateLoadedDocument();
        var destination = Path.Combine(output.Path, "candidate.anim");
        var original = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(destination, original);

        var exportResult = document.ExportDiskCopy(destination);

        Assert.IsFalse(exportResult.Succeeded);
        Assert.IsTrue(exportResult.State.IsDirty);
        Assert.AreEqual(
            AnimationWorkbenchDiagnosticCode.DestinationAlreadyExists,
            exportResult.Diagnostics.Single().Code);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(destination));
        Assert.AreEqual(
            0,
            Directory.GetFiles(output.Path, "*.tmp").Length);
    }

    [TestMethod]
    public void SaveDiagnostics_HaveChineseLocalizationEntries()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Language_Cn.json")));
        var keys = Enum.GetValues<AnimationWorkbenchDiagnosticCode>();

        foreach (var code in keys)
        {
            var key = new AnimationWorkbenchDiagnostic(
                code,
                AnimationWorkbenchDiagnosticSeverity.Error).ReasonKey;
            Assert.IsTrue(json.RootElement.TryGetProperty(key, out var value));
            StringAssert.Contains(value.GetString(), "无法");
        }
    }

    private static AnimationWorkbenchDocument CreateLoadedDocument(
        AnimationWorkbenchSourceFormat? format = default,
        GameTypeEnum targetGame = GameTypeEnum.Warhammer3,
        AnimationClip? animation = null,
        bool omitFormat = false)
    {
        var sourceFormat = omitFormat
            ? null
            : format ?? new AnimationWorkbenchSourceFormat(7, 1);
        var sourceSkeleton = CreateSkeleton("source_skeleton", "root");
        var targetSkeleton = CreateSkeleton("target_skeleton", "root");
        var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            new AnimationWorkbenchSourceInput(
                "animation_a",
                animation ?? CreateClip(
                    new Vector3(1, 2, 3),
                    new Vector3(4, 5, 6)),
                sourceSkeleton,
                sourceFormat),
            null,
            targetGame,
            targetSkeleton));
        return document;
    }

    private static AnimationClip CreateClip(params Vector3[] positions)
    {
        var clip = new AnimationClip
        {
            Duration = TimeSpan.FromSeconds(positions.Length * 0.05),
        };
        foreach (var position in positions)
        {
            var frame = new AnimationClip.KeyFrame();
            frame.Position.Add(position);
            frame.Rotation.Add(Quaternion.Identity);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"animation-workbench-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
