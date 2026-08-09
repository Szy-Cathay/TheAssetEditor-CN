using Moq;
using Shared.ByteParsing;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectContainerTests
{
    [Test]
    public void Create_WritesCnSettingsAndLoadsExistingFiles()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\units_tables\data.bin", [1, 2, 3]);

        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "测试工程",
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                File.Exists(
                    Path.Combine(
                        project.Path,
                        FolderProjectSettings.CnFileName)),
                Is.True);
            Assert.That(container.Name, Is.EqualTo("测试工程"));
            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\units_tables\data.bin" }));
            Assert.That(
                container.FileList[@"db\units_tables\data.bin"]
                    .DataSource
                    .ReadData(),
                Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(
                container.ProjectSettings
                    .EnablePackFileCorruptionDetection,
                Is.False);
        });
    }

    [Test]
    public void Open_OriginalSettings_ImportsWithoutOverwritingOriginal()
    {
        using var project = new TemporaryDirectory();
        var originalPath = Path.Combine(
            project.Path,
            FolderProjectSettings.OriginalFileName);
        File.WriteAllText(
            originalPath,
            """
            {
              "Name": "原版工程",
              "IgnoredPaths": [ "ignored\\file.bin" ]
            }
            """);
        project.Write(@"ignored\file.bin", [1]);
        project.Write(@"kept\file.bin", [2]);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(container.Name, Is.EqualTo("原版工程"));
            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(
                    new[]
                    {
                        @"ignored\file.bin",
                        @"kept\file.bin",
                    }));
            Assert.That(
                container.IsIgnored(@"ignored\file.bin"),
                Is.True);
            Assert.That(
                File.ReadAllText(originalPath),
                Does.Contain("\"Name\": \"原版工程\""));
            Assert.That(
                File.Exists(
                    Path.Combine(
                        project.Path,
                        FolderProjectSettings.CnFileName)),
                Is.True);
        });
    }

    [Test]
    public void Open_OriginalSettings_ImportsCorruptionDetectionSetting()
    {
        using var project = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(
                project.Path,
                FolderProjectSettings.OriginalFileName),
            """
            {
              "Name": "原版工程",
              "EnablePackFileCorruptionDetection": true
            }
            """);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.That(
            container.ProjectSettings
                .EnablePackFileCorruptionDetection,
            Is.True);
    }

    [Test]
    public void Open_CnSettingsWithoutCorruptionDetection_DefaultsDisabled()
    {
        using var project = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(
                project.Path,
                FolderProjectSettings.CnFileName),
            """
            {
              "Name": "旧国区工程"
            }
            """);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.That(
            container.ProjectSettings
                .EnablePackFileCorruptionDetection,
            Is.False);
    }

    [Test]
    public void Open_MaterializedCorruptionDetectionFiles_AreVirtualized()
    {
        using var project = new TemporaryDirectory();
        new FolderProjectSettings { Name = "旧工程" }
            .Save(project.Path);
        project.Write(
            @"!!!packfile_corruction_detection\packfile_corruction_detection_1.txt",
            [1]);
        project.Write(
            @"packfile_corruction_detection\packfile_corruction_detection_2.txt",
            [2]);
        project.Write(
            @"zzzz_packfile_corruction_detection\packfile_corruction_detection_3.txt",
            [3]);
        project.Write(@"db\keep.bin", [4]);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\keep.bin" }));
            Assert.That(
                container.ProjectSettings
                    .EnablePackFileCorruptionDetection,
                Is.True);
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        project.Path,
                        "!!!packfile_corruction_detection")),
                Is.False);
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        project.Path,
                        "packfile_corruction_detection")),
                Is.False);
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        project.Path,
                        "zzzz_packfile_corruction_detection")),
                Is.False);
        });
    }

    [Test]
    public void Open_CnSettingsWithoutSemanticChanges_PreservesOriginalBytes()
    {
        using var project = new TemporaryDirectory();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var original = """{"Name":"工程","IgnoredPaths":["db\\ignored.bin"]}"""u8
            .ToArray();
        File.WriteAllBytes(settingsPath, original);
        project.Write(@"db\item.bin", [1]);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.That(File.ReadAllBytes(settingsPath), Is.EqualTo(original));
    }

    [Test]
    public void Open_RecreatedConfiguredEmptyDirectory_PreservesSettingsBytes()
    {
        using var project = new TemporaryDirectory();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var original =
            """{"Name":"工程","EmptyDirectories":["empty\\nested"]}"""u8
                .ToArray();
        File.WriteAllBytes(settingsPath, original);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(
                    Path.Combine(project.Path, "empty", "nested")),
                Is.True);
            Assert.That(
                File.ReadAllBytes(settingsPath),
                Is.EqualTo(original));
        });
    }

    [Test]
    public void Open_ConfiguredEmptyDirectoryOccupiedByFile_UpdatesSettings()
    {
        using var project = new TemporaryDirectory();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var settings = new FolderProjectSettings
        {
            Name = "工程",
            EmptyDirectories = new HashSet<string>(
                ["collision"],
                StringComparer.OrdinalIgnoreCase),
        };
        settings.Save(project.Path);
        project.Write("collision", [1]);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(container.EmptyDirectories, Is.Empty);
            Assert.That(
                FolderProjectSettings.Load(project.Path).EmptyDirectories,
                Is.Empty);
        });
    }

    [Test]
    public void Open_NewPhysicalEmptyDirectory_UpdatesSettings()
    {
        using var project = new TemporaryDirectory();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var original = """{"Name":"工程"}"""u8.ToArray();
        File.WriteAllBytes(settingsPath, original);
        Directory.CreateDirectory(
            Path.Combine(project.Path, "empty", "external"));

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(settingsPath),
                Is.Not.EqualTo(original));
            Assert.That(
                FolderProjectSettings.Load(project.Path).EmptyDirectories,
                Does.Contain(@"empty\external"));
        });
    }

    [Test]
    public void RefreshFromDisk_AddChangeDelete_ReconcilesContainer()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\old.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        project.Write(@"db\old.bin", [4, 5]);
        project.Write(@"db\new.bin", [2, 3]);
        File.Delete(Path.Combine(project.Path, "db", "old.bin"));

        var changes = container.RefreshFromDisk();

        Assert.Multiple(() =>
        {
            Assert.That(changes.Added, Is.EqualTo(1));
            Assert.That(changes.Removed, Is.EqualTo(1));
            Assert.That(container.FileList.Keys, Is.EqualTo(new[] { @"db\new.bin" }));
            Assert.That(
                container.FileList[@"db\new.bin"].DataSource.ReadData(),
                Is.EqualTo(new byte[] { 2, 3 }));
        });
    }

    [Test]
    public void RefreshFromDisk_PreservesEmptyDirectories()
    {
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(
            Path.Combine(project.Path, "empty", "nested"));
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        Assert.That(
            container.EmptyDirectories,
            Does.Contain(@"empty\nested"));
    }

    [Test]
    public void RefreshFromDisk_UnchangedFile_PreservesObjectIdentity()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1, 2]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var original = container.FileList[@"db\item.bin"];

        container.RefreshFromDisk();

        Assert.That(
            container.FileList[@"db\item.bin"],
            Is.SameAs(original));
    }

    [Test]
    public void RefreshFromDisk_ChangedFile_UpdatesExistingObject()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var original = container.FileList[@"db\item.bin"];

        project.Write(@"db\item.bin", [9, 8, 7]);
        container.RefreshFromDisk();

        Assert.Multiple(() =>
        {
            Assert.That(
                container.FileList[@"db\item.bin"],
                Is.SameAs(original));
            Assert.That(
                original.DataSource.ReadData(),
                Is.EqualTo(new byte[] { 9, 8, 7 }));
        });
    }

    [Test]
    public void GetRelativePath_AfterMoveAndRename_ReturnsCurrentPath()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var file = container.FileList[@"db\item.bin"];

        container.MoveFileOnDisk(file, "moved");
        container.RenameFileOnDisk(file, "renamed.bin");

        Assert.That(
            container.GetRelativePath(file),
            Is.EqualTo(@"moved\renamed.bin"));
    }

    [Test]
    public void Create_NestedControlNames_AreLoadedAsResources()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\aeproject.json", [1]);
        project.Write(@"aeproject.json\nested.bin", [2]);

        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        Assert.That(
            container.FileList.Keys,
            Is.EquivalentTo(
                new[]
                {
                    @"db\aeproject.json",
                    @"aeproject.json\nested.bin",
                }));
    }

    [Test]
    public void Create_MetadataAndAtomicFiles_AreExcludedButOrdinaryTmpIsVisible()
    {
        using var project = new TemporaryDirectory();
        project.Write(@".git\config", [1]);
        project.Write(@"nested\.git\objects\item", [2]);
        project.Write(@".gitignore", [3]);
        project.Write(@"nested\.gitattributes", [4]);
        project.Write(
            @"db\item.0123456789abcdef0123456789abcdef.tmp",
            [5]);
        project.Write(@"db\ordinary.tmp", [6]);
        project.Write(
            @"db\asset.abcdefghijklmnopqrstuvwxyz123456.tmp",
            [7]);
        project.Write(@"dirs\.gitignore\nested.bin", [8]);
        project.Write(@"dirs\.gitattributes\nested.bin", [9]);

        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        Assert.That(
            container.FileList.Keys,
            Is.EquivalentTo(
                new[]
                {
                    @"db\ordinary.tmp",
                    @"db\asset.abcdefghijklmnopqrstuvwxyz123456.tmp",
                    @"dirs\.gitignore\nested.bin",
                    @"dirs\.gitattributes\nested.bin",
                }));
    }

    [Test]
    public void AddFiles_ReservedMetadataPath_RejectsWithoutWriting()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var file = PackFile.CreateFromBytes("config", [1]);

        Assert.That(
            () => container.AddFiles(
                [new NewPackFileEntry(@".git", file)]),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            Directory.Exists(Path.Combine(project.Path, ".git")),
            Is.False);
    }

    [Test]
    public void CreateDirectory_ReservedMetadataPath_RejectsWithoutWriting()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        Assert.That(
            () => container.CreateDirectoryOnDisk(@".git\objects"),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            Directory.Exists(Path.Combine(project.Path, ".git")),
            Is.False);
    }

    [Test]
    public void MoveAndRename_ReservedMetadataPath_RejectWithoutChangingDisk()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var file = container.FileList[@"db\item.bin"];

        Assert.That(
            () => container.MoveFileOnDisk(file, ".git"),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            () => container.RenameFileOnDisk(file, ".gitignore"),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            () => container.RenameDirectoryOnDisk("db", ".git"),
            Throws.TypeOf<InvalidDataException>());
        Assert.Multiple(() =>
        {
            Assert.That(
                File.Exists(Path.Combine(project.Path, "db", "item.bin")),
                Is.True);
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(project.Path, "db", ".gitignore")),
                Is.False);
        });
    }

    [Test]
    public void Open_RecreatesConfiguredEmptyDirectoriesBeforeScanning()
    {
        using var project = new TemporaryDirectory();
        var settings = new FolderProjectSettings
        {
            Name = "工程",
            IgnoredPaths = new HashSet<string>(
                [@"db\ignored.bin"],
                StringComparer.OrdinalIgnoreCase),
            EmptyDirectories = new HashSet<string>(
                [@"empty\nested"],
                StringComparer.OrdinalIgnoreCase),
        };
        settings.Save(project.Path);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(
                    Path.Combine(project.Path, "empty", "nested")),
                Is.True);
            Assert.That(
                container.EmptyDirectories,
                Does.Contain(@"empty\nested"));
            Assert.That(
                container.ProjectSettings.IgnoredPaths,
                Does.Contain(@"db\ignored.bin"));
        });
    }

    [Test]
    public void Open_RecreatesRootDirectoryNamedLikeLegacyControlFile()
    {
        using var project = new TemporaryDirectory();
        var settings = new FolderProjectSettings
        {
            Name = "工程",
            EmptyDirectories = new HashSet<string>(
                [FolderProjectSettings.OriginalFileName],
                StringComparer.OrdinalIgnoreCase),
        };
        settings.Save(project.Path);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        project.Path,
                        FolderProjectSettings.OriginalFileName)),
                Is.True);
            Assert.That(
                container.EmptyDirectories,
                Does.Contain(FolderProjectSettings.OriginalFileName));
        });
    }

    [Test]
    public void Open_InvalidMetadataEmptyDirectory_IsFilteredWithoutCreatingIt()
    {
        using var project = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(project.Path, FolderProjectSettings.CnFileName),
            """
            {
              "Name": "工程",
              "EmptyDirectories": [ ".git\\objects", "valid\\empty" ]
            }
            """);

        using var container = FolderProjectContainer.Open(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, "valid", "empty")),
                Is.True);
            Assert.That(
                container.EmptyDirectories,
                Is.EquivalentTo(new[] { @"valid\empty" }));
            Assert.That(
                File.ReadAllText(
                    Path.Combine(
                        project.Path,
                        FolderProjectSettings.CnFileName)),
                Does.Not.Contain(".git"));
        });
    }

    [Test]
    public void Create_OutputInsideProject_RejectsBeforeWritingSettings()
    {
        using var project = new TemporaryDirectory();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);

        Assert.That(
            () => FolderProjectContainer.Create(
                project.Path,
                new FolderProjectSettings
                {
                    Name = "工程",
                    OutputPackPath = Path.Combine(
                        project.Path,
                        "generated.pack"),
                }),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(File.Exists(settingsPath), Is.False);
    }

    [Test]
    public void AddFiles_ExistingPathWithoutApproval_PreservesOriginalFile()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1, 2, 3]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        Assert.That(
            () => container.AddFiles(
                [
                    new NewPackFileEntry(
                        "db",
                        PackFile.CreateFromBytes(
                            "new.bin",
                            [4, 5, 6])),
                    new NewPackFileEntry(
                        "db",
                        PackFile.CreateFromBytes(
                            "item.bin",
                            [9, 8, 7])),
                ]),
            Throws.TypeOf<FolderProjectFileConflictException>());
        Assert.That(
            File.ReadAllBytes(
                Path.Combine(project.Path, "db", "item.bin")),
            Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(
            File.Exists(
                Path.Combine(project.Path, "db", "new.bin")),
            Is.False);
    }

    [Test]
    public void AddFiles_ExistingPathWithApproval_ReplacesFile()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1, 2, 3]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });

        container.AddFiles(
            [
                new NewPackFileEntry(
                    "db",
                    PackFile.CreateFromBytes(
                        "item.bin",
                        [9, 8, 7])),
            ],
            overwriteExisting: true);

        Assert.That(
            File.ReadAllBytes(
                Path.Combine(project.Path, "db", "item.bin")),
            Is.EqualTo(new byte[] { 9, 8, 7 }));
    }

    [Test]
    public void AddFiles_TargetAppearsDuringSourceReadWithoutOverwrite_PreservesExternalFileAndRollsBackBatch()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var source = new BlockingDataSource([4, 5, 6]);
        var delayedFile = PackFile.CreateFromBytes(
            "late.bin",
            [4, 5, 6]);
        delayedFile.DataSource = source;

        var addTask = Task.Run(
            () => container.AddFiles(
            [
                new NewPackFileEntry(
                    "db",
                    PackFile.CreateFromBytes("new.bin", [1, 2, 3])),
                new NewPackFileEntry("db", delayedFile),
            ],
            overwriteExisting: false));
        try
        {
            Assert.That(
                source.ReadStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The add operation did not start reading its source.");
            project.Write(@"db\late.bin", [9, 8, 7]);
        }
        finally
        {
            source.ReleaseRead.Set();
        }

        var exception = Assert.Throws<FolderProjectFileConflictException>(
            () => addTask.GetAwaiter().GetResult());
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Paths,
                Is.EquivalentTo(new[] { @"db\late.bin" }));
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(project.Path, "db", "late.bin")),
                Is.EqualTo(new byte[] { 9, 8, 7 }));
            Assert.That(
                File.Exists(
                    Path.Combine(project.Path, "db", "new.bin")),
                Is.False);
        });
    }

    [Test]
    public void AddFiles_TargetAppearsDuringFinalCommitWithoutOverwrite_ReportsConflictAndRollsBackBatch()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var firstCommitted = new ManualResetEventSlim(false);
        var eventHub = new Mock<IGlobalEventHub>();
        var service = new PackFileService(eventHub.Object);
        container.CommitPreparedFile = (tempPath, fullPath) =>
        {
            if (Path.GetFileName(fullPath).Equals(
                    "new.bin",
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Move(tempPath, fullPath);
                firstCommitted.Set();
                return;
            }

            if (!firstCommitted.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    "The first file was not committed.");
            }
            File.WriteAllBytes(fullPath, [9, 8, 7]);
            File.Move(tempPath, fullPath);
        };

        var exception = Assert.Throws<FolderProjectFileConflictException>(
            () => service.AddFilesToPack(
                container,
                [
                    new NewPackFileEntry(
                        "db",
                        PackFile.CreateFromBytes("new.bin", [1, 2, 3])),
                    new NewPackFileEntry(
                        "db",
                        PackFile.CreateFromBytes("late.bin", [4, 5, 6])),
                ],
                overwriteExisting: false));
        var changeEvents = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>();

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Paths,
                Is.EquivalentTo(new[] { @"db\late.bin" }));
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(project.Path, "db", "late.bin")),
                Is.EqualTo(new byte[] { 9, 8, 7 }));
            Assert.That(
                File.Exists(
                    Path.Combine(project.Path, "db", "new.bin")),
                Is.False);
            Assert.That(container.FileList, Is.Empty);
            Assert.That(changeEvents, Is.Empty);
        });
    }

    [Test]
    public void MoveAndRename_IgnoredPathsFollowResource()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"old\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var file = container.FileList[@"old\item.bin"];
        container.SetIgnored(@"old\item.bin", true);

        container.MoveFileOnDisk(file, "moved");
        container.RenameFileOnDisk(file, "renamed.bin");
        container.RenameDirectoryOnDisk("moved", "final");

        Assert.Multiple(() =>
        {
            Assert.That(
                container.IsIgnored(@"final\renamed.bin"),
                Is.True);
            Assert.That(
                container.ProjectSettings.IgnoredPaths,
                Is.EquivalentTo(
                    new[] { @"final\renamed.bin" }));
        });
    }

    [Test]
    public void DeleteIgnoredFile_PersistsRuleRemovalBeforeDispose()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"ignored\item.bin", [1]);
        using (var container = FolderProjectContainer.Create(
                   project.Path,
                   new FolderProjectSettings { Name = "工程" }))
        {
            container.SetIgnored(@"ignored\item.bin", true);

            container.DeleteFileFromDisk(
                container.FileList[@"ignored\item.bin"]);
        }

        var settings = FolderProjectSettings.Load(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                File.Exists(
                    Path.Combine(
                        project.Path,
                        "ignored",
                        "item.bin")),
                Is.False);
            Assert.That(settings.IgnoredPaths, Is.Empty);
        });
    }

    [Test]
    public void DeleteIgnoredFolder_PersistsRuleRemovalBeforeDispose()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"ignored\folder\item.bin", [1]);
        using (var container = FolderProjectContainer.Create(
                   project.Path,
                   new FolderProjectSettings { Name = "工程" }))
        {
            container.SetIgnored(@"ignored\folder", true);
            container.SetIgnored(@"ignored\folder\item.bin", true);

            container.DeleteFolderFromDisk(@"ignored\folder");
        }

        var settings = FolderProjectSettings.Load(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(
                    Path.Combine(
                        project.Path,
                        "ignored",
                        "folder")),
                Is.False);
            Assert.That(settings.IgnoredPaths, Is.Empty);
        });
    }

    [Test]
    public void SetIgnored_IgnoredFolderRemainsVisibleAndPersists()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"ignored\a.bin", [1]);
        project.Write(@"kept\b.bin", [2]);
        using (var container = FolderProjectContainer.Create(
                   project.Path,
                   new FolderProjectSettings { Name = "工程" }))
        {
            container.SetIgnored(@"ignored", true);

            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(
                    new[]
                    {
                        @"ignored\a.bin",
                        @"kept\b.bin",
                    }));
            Assert.That(container.IsIgnored(@"ignored\a.bin"), Is.True);
        }

        using var reopened = FolderProjectContainer.Open(project.Path);
        Assert.That(
            reopened.FileList.Keys,
            Is.EquivalentTo(
                new[]
                {
                    @"ignored\a.bin",
                    @"kept\b.bin",
                }));
        Assert.That(reopened.IsIgnored(@"ignored\a.bin"), Is.True);
    }

    [Test]
    public void RefreshFromDisk_DuringDirectoryScan_DoesNotBlockReadOnlyStateQueries()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var scanStarted = new ManualResetEventSlim(false);
        using var releaseScan = new ManualResetEventSlim(false);
        var blocked = 0;
        container.EnumerateFileSystemEntries = directory =>
        {
            if (Interlocked.Exchange(ref blocked, 1) == 0)
            {
                scanStarted.Set();
                if (!releaseScan.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the scan.");
            }
            return Directory.EnumerateFileSystemEntries(directory);
        };

        var refreshTask = Task.Run(container.RefreshFromDisk);
        try
        {
            Assert.That(
                scanStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The refresh did not start scanning.");

            var readTask = Task.Run(
                () => container.IsIgnored(@"db\item.bin"));
            Assert.That(
                readTask.Wait(TimeSpan.FromSeconds(1)),
                Is.True,
                "A read-only query waited for the directory scan.");
            Assert.That(readTask.Result, Is.False);
        }
        finally
        {
            releaseScan.Set();
        }

        Assert.That(
            refreshTask.Wait(TimeSpan.FromSeconds(5)),
            Is.True,
            "The refresh did not finish after the scan was released.");
        refreshTask.GetAwaiter().GetResult();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-project-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Write(string relativePath, byte[] bytes)
        {
            var fullPath = System.IO.Path.Combine(
                Path,
                relativePath);
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }

    private sealed class BlockingDataSource(byte[] data) :
        IDataSource,
        IDisposable
    {
        public ManualResetEventSlim ReadStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseRead { get; } = new(false);

        public long Size => data.Length;
        public CompressionFormat CompressionFormat =>
            CompressionFormat.None;

        public byte[] ReadData()
        {
            ReadStarted.Set();
            if (!ReleaseRead.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the read.");
            return data.ToArray();
        }

        public byte[] PeekData(int size)
        {
            return data.Take(size).ToArray();
        }

        public ByteChunk ReadDataAsChunk()
        {
            return new ByteChunk(ReadData());
        }

        public void Dispose()
        {
            ReadStarted.Dispose();
            ReleaseRead.Dispose();
        }
    }
}
