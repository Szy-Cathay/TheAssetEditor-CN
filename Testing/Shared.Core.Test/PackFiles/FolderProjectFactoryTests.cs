using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectFactoryTests
{
    [Test]
    public void ImportTargetValidation_EmptyDirectory_IsAccepted()
    {
        using var target = new TemporaryDirectory();

        var result = FolderProjectImportTargetValidator
            .ValidateEmptyTarget(target.Path);

        Assert.That(result, Is.EqualTo(Path.GetFullPath(target.Path)));
    }

    [TestCase("ordinary.bin")]
    [TestCase("nested\\")]
    [TestCase(FolderProjectSettings.CnFileName)]
    public void ImportTargetValidation_NonEmptyDirectory_IsRejected(
        string existingEntry)
    {
        using var target = new TemporaryDirectory();
        var existingPath = Path.Combine(target.Path, existingEntry);
        if (existingEntry.EndsWith("\\", StringComparison.Ordinal))
            Directory.CreateDirectory(existingPath);
        else
            File.WriteAllBytes(existingPath, [1]);

        Assert.That(
            () => FolderProjectImportTargetValidator
                .ValidateEmptyTarget(target.Path),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ImportTargetValidation_AccessDenied_IsNotApproved()
    {
        Assert.That(
            FolderProjectImportTargetValidator.IsEmptyTarget(
                () => throw new UnauthorizedAccessException()),
            Is.False);
    }

    [Test]
    public void ImportPack_ExtractsAllFilesAndCreatesCnSettings()
    {
        using var target = new TemporaryDirectory();
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-factory-{Guid.NewGuid():N}.pack");
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.Header.DependantFiles.AddRange(
            ["required.pack", "shared.pack"]);
        source.FileList[@"db\entry.bin"] =
            PackFile.CreateFromBytes("entry.bin", [1, 2, 3]);
        var factory = new FolderProjectFactory();

        using var project = factory.ImportPack(
            source,
            target.Path,
            new FolderProjectSettings
            {
                Name = "导入工程",
                OutputPackPath = outputPath,
                GameVersion = GameTypeEnum.Warhammer3,
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(target.Path, "db", "entry.bin")),
                Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(
                File.Exists(
                    Path.Combine(
                        target.Path,
                        FolderProjectSettings.CnFileName)),
                Is.True);
            Assert.That(
                project.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\entry.bin" }));
            Assert.That(
                project.ProjectSettings.PackFileVersion,
                Is.EqualTo(PackFileVersion.PFH5));
            Assert.That(
                project.ProjectSettings.DependantFiles,
                Is.EqualTo(
                    new[] { "required.pack", "shared.pack" }));
            Assert.That(
                project.Header.DependantFiles,
                Is.EqualTo(
                    new[] { "required.pack", "shared.pack" }));
            Assert.That(
                project.ProjectSettings
                    .EnablePackFileCorruptionDetection,
                Is.False);
        });
    }

    [Test]
    public void ImportPack_ReportsEveryFileBeforeExtraction()
    {
        using var target = new TemporaryDirectory();
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.FileList[@"audio\first.wav"] =
            PackFile.CreateFromBytes("first.wav", [1]);
        source.FileList[@"audio\second.wem"] =
            PackFile.CreateFromBytes("second.wem", [2]);
        var reported = new List<FolderProjectImportProgress>();
        var factory = new FolderProjectFactory();

        using var project = factory.ImportPack(
            source,
            target.Path,
            new FolderProjectSettings { Name = "进度测试" },
            reported.Add);

        Assert.That(
            reported.Select(item => (
                item.RelativePath,
                item.CurrentIndex,
                item.Total,
                item.IsCompressed)),
            Is.EqualTo(
                new (string, int, int, bool)[]
                {
                    ("audio/first.wav", 1, 2, false),
                    ("audio/second.wem", 2, 2, false),
                }));
    }

    [Test]
    public void ImportPack_InvalidPath_DoesNotLeavePartialProject()
    {
        using var target = new TemporaryDirectory();
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.FileList[@"safe\entry.bin"] =
            PackFile.CreateFromBytes("entry.bin", [1]);
        source.FileList[@"..\outside.bin"] =
            PackFile.CreateFromBytes("outside.bin", [2]);
        var factory = new FolderProjectFactory();

        Assert.That(
            () => factory.ImportPack(
                source,
                target.Path,
                new FolderProjectSettings
                {
                    OutputPackPath = Path.Combine(
                        Path.GetTempPath(),
                        $"ae-folder-invalid-{Guid.NewGuid():N}.pack"),
                }),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            Directory.EnumerateFileSystemEntries(target.Path),
            Is.Empty);
    }

    [Test]
    public void ImportPack_NonEmptyTarget_IsRejectedWithoutChanges()
    {
        using var target = new TemporaryDirectory();
        var existingPath = Path.Combine(target.Path, "existing.bin");
        File.WriteAllBytes(existingPath, [7]);
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.FileList["new.bin"] =
            PackFile.CreateFromBytes("new.bin", [1]);
        var factory = new FolderProjectFactory();

        Assert.That(
            () => factory.ImportPack(
                source,
                target.Path,
                new FolderProjectSettings
                {
                    OutputPackPath = Path.Combine(
                        Path.GetTempPath(),
                        $"ae-folder-nonempty-{Guid.NewGuid():N}.pack"),
                }),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(File.ReadAllBytes(existingPath), Is.EqualTo(new byte[] { 7 }));
        Assert.That(File.Exists(Path.Combine(target.Path, "new.bin")), Is.False);
    }

    [Test]
    public void ImportPack_MetadataEntries_AreNotExtracted()
    {
        using var target = new TemporaryDirectory();
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.FileList[@".git\config"] =
            PackFile.CreateFromBytes("config", [1]);
        source.FileList[@".gitignore"] =
            PackFile.CreateFromBytes(".gitignore", [2]);
        source.FileList[@"nested\.gitattributes"] =
            PackFile.CreateFromBytes(".gitattributes", [3]);
        source.FileList[
                @"db\item.0123456789abcdef0123456789abcdef.rename"] =
            PackFile.CreateFromBytes(
                "item.0123456789abcdef0123456789abcdef.rename",
                [4]);
        source.FileList[@"db\keep.bin"] =
            PackFile.CreateFromBytes("keep.bin", [5]);
        var factory = new FolderProjectFactory();

        using var project = factory.ImportPack(
            source,
            target.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                OutputPackPath = Path.Combine(
                    Path.GetTempPath(),
                    $"ae-folder-metadata-{Guid.NewGuid():N}.pack"),
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(Path.Combine(target.Path, ".git")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(target.Path, ".gitignore")),
                Is.False);
            Assert.That(
                File.Exists(
                    Path.Combine(target.Path, "nested", ".gitattributes")),
                Is.False);
            Assert.That(
                project.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\keep.bin" }));
        });
    }

    [Test]
    public void ImportPack_CorruptionDetectionEntries_RemainVirtual()
    {
        using var target = new TemporaryDirectory();
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.FileList[
                @"!!!packfile_corruction_detection\packfile_corruction_detection_1.txt"] =
            PackFile.CreateFromBytes(
                "packfile_corruction_detection_1.txt",
                [1]);
        source.FileList[
                @"packfile_corruction_detection\packfile_corruction_detection_2.txt"] =
            PackFile.CreateFromBytes(
                "packfile_corruction_detection_2.txt",
                [2]);
        source.FileList[
                @"zzzz_packfile_corruction_detection\packfile_corruction_detection_3.txt"] =
            PackFile.CreateFromBytes(
                "packfile_corruction_detection_3.txt",
                [3]);
        source.FileList[@"db\keep.bin"] =
            PackFile.CreateFromBytes("keep.bin", [4]);
        var factory = new FolderProjectFactory();

        using var project = factory.ImportPack(
            source,
            target.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                OutputPackPath = Path.Combine(
                    Path.GetTempPath(),
                    $"ae-folder-detection-{Guid.NewGuid():N}.pack"),
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                project.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\keep.bin" }));
            Assert.That(
                project.ProjectSettings
                    .EnablePackFileCorruptionDetection,
                Is.True);
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        target.Path,
                        "!!!packfile_corruction_detection")),
                Is.False);
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        target.Path,
                        "packfile_corruction_detection")),
                Is.False);
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        target.Path,
                        "zzzz_packfile_corruction_detection")),
                Is.False);
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-factory-root-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
