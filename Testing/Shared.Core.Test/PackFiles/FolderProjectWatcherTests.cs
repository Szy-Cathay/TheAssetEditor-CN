using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectWatcherTests
{
    [Test]
    public void Watcher_ExternalAddChangeDelete_ReconcilesAutomatically()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.StartWatching();

        project.Write(@"db\added.bin", [2]);
        Assert.That(
            WaitUntil(
                () => container.FileList.ContainsKey(@"db\added.bin")),
            Is.True);

        project.Write(@"db\added.bin", [3, 4]);
        Assert.That(
            WaitUntil(
                () => container.FileList[@"db\added.bin"]
                    .DataSource
                    .ReadData()
                    .SequenceEqual(new byte[] { 3, 4 })),
            Is.True);

        File.Delete(Path.Combine(project.Path, "db", "added.bin"));
        Assert.That(
            WaitUntil(
                () => !container.FileList.ContainsKey(@"db\added.bin")),
            Is.True);
    }

    [Test]
    public void Watcher_ExternalFileRename_MovesIgnoredPath()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\old.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.SetIgnored(@"db\old.bin", true);
        container.StartWatching();

        File.Move(
            Path.Combine(project.Path, "db", "old.bin"),
            Path.Combine(project.Path, "db", "new.bin"));

        Assert.That(
            WaitUntil(
                () =>
                    container.FileList.ContainsKey(@"db\new.bin") &&
                    container.IsIgnored(@"db\new.bin")),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(container.IsIgnored(@"db\old.bin"), Is.False);
            Assert.That(
                FolderProjectSettings.Load(project.Path).IgnoredPaths,
                Is.EquivalentTo(new[] { @"db\new.bin" }));
        });
    }

    [Test]
    public void Watcher_ExternalIgnoredFileRename_PersistsBeforeReconcile()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\old.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.SetIgnored(@"db\old.bin", true);
        container.StartWatching();

        File.Move(
            Path.Combine(project.Path, "db", "old.bin"),
            Path.Combine(project.Path, "db", "new.bin"));

        Assert.That(
            WaitUntil(
                () =>
                    !container.IsIgnored(@"db\old.bin") &&
                    container.IsIgnored(@"db\new.bin")),
            Is.True);

        var settings = FolderProjectSettings.Load(project.Path);
        Assert.That(
            settings.IgnoredPaths,
            Is.EquivalentTo(new[] { @"db\new.bin" }));
    }

    [Test]
    public void Watcher_ExternalIgnoredFileDelete_PersistsBeforeReconcile()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.SetIgnored(@"db\item.bin", true);
        container.StartWatching();

        File.Delete(Path.Combine(project.Path, "db", "item.bin"));

        Assert.That(
            WaitUntil(
                () => !container.IsIgnored(@"db\item.bin")),
            Is.True);

        var settings = FolderProjectSettings.Load(project.Path);
        Assert.That(settings.IgnoredPaths, Is.Empty);
    }

    [Test]
    public void Dispose_AfterExternalIgnoredFileRename_FlushesPendingSettings()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\old.bin", [1]);
        using (var container = FolderProjectContainer.Create(
                   project.Path,
                   new FolderProjectSettings { Name = "工程" }))
        {
            container.SetIgnored(@"db\old.bin", true);
            container.StartWatching();

            File.Move(
                Path.Combine(project.Path, "db", "old.bin"),
                Path.Combine(project.Path, "db", "new.bin"));

            Assert.That(
                WaitUntil(
                    () =>
                        !container.IsIgnored(@"db\old.bin") &&
                        container.IsIgnored(@"db\new.bin")),
                Is.True);
        }

        Assert.That(
            FolderProjectSettings.Load(project.Path).IgnoredPaths,
            Is.EquivalentTo(new[] { @"db\new.bin" }));
    }

    [Test]
    public void Dispose_AfterExternalIgnoredFileDelete_FlushesPendingSettings()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using (var container = FolderProjectContainer.Create(
                   project.Path,
                   new FolderProjectSettings { Name = "工程" }))
        {
            container.SetIgnored(@"db\item.bin", true);
            container.StartWatching();

            File.Delete(Path.Combine(project.Path, "db", "item.bin"));

            Assert.That(
                WaitUntil(
                    () => !container.IsIgnored(@"db\item.bin")),
                Is.True);
        }

        Assert.That(
            FolderProjectSettings.Load(project.Path).IgnoredPaths,
            Is.Empty);
    }

    [Test]
    public void Watcher_ExternalFileDeleteThenRecreate_RemovesIgnoredPath()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.SetIgnored(@"db\item.bin", true);
        container.StartWatching();

        File.Delete(Path.Combine(project.Path, "db", "item.bin"));
        Assert.That(
            WaitUntil(
                () => !container.FileList.ContainsKey(
                    @"db\item.bin")),
            Is.True);

        project.Write(@"db\item.bin", [2]);
        Assert.That(
            WaitUntil(
                () => container.FileList.ContainsKey(
                    @"db\item.bin")),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(container.IsIgnored(@"db\item.bin"), Is.False);
            Assert.That(
                FolderProjectSettings.Load(project.Path).IgnoredPaths,
                Is.Empty);
        });
    }

    [Test]
    public void Watcher_ExternalDirectoryDeleteThenRecreate_RemovesIgnoredDescendants()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\folder\existing.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                IgnoredPaths = new HashSet<string>(
                    [@"db\folder\missing.bin"],
                    StringComparer.OrdinalIgnoreCase),
            });
        container.StartWatching();

        Directory.Delete(
            Path.Combine(project.Path, "db", "folder"),
            true);
        Assert.That(
            WaitUntil(
                () => !container.FileList.ContainsKey(
                    @"db\folder\existing.bin")),
            Is.True);

        project.Write(@"db\folder\missing.bin", [2]);
        Assert.That(
            WaitUntil(
                () => container.FileList.ContainsKey(
                    @"db\folder\missing.bin")),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                container.IsIgnored(@"db\folder\missing.bin"),
                Is.False);
            Assert.That(
                FolderProjectSettings.Load(project.Path).IgnoredPaths,
                Is.Empty);
        });
    }

    [Test]
    public void Watcher_AtomicReplace_RefreshesSamePath()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1, 1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.StartWatching();

        var target = Path.Combine(project.Path, "db", "item.bin");
        var replacement = Path.Combine(project.Path, "db", "replacement.tmp");
        File.WriteAllBytes(replacement, [9, 8, 7]);
        File.Move(replacement, target, true);

        Assert.That(
            WaitUntil(
                () => container.FileList[@"db\item.bin"]
                    .DataSource
                    .ReadData()
                    .SequenceEqual(new byte[] { 9, 8, 7 })),
            Is.True);
    }

    [Test]
    public void StartWatching_ReconcilesChangesMadeAfterInitialScan()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        project.Write(@"db\late.bin", [5]);

        container.StartWatching();

        Assert.That(
            container.FileList.ContainsKey(@"db\late.bin"),
            Is.True);
    }

    [Test]
    public void Dispose_ReleasesWatcherAndProjectDirectory()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.StartWatching();
        container.Dispose();

        var renamed = project.Path + "-renamed";
        Directory.Move(project.Path, renamed);
        project.Adopt(renamed);

        Assert.That(Directory.Exists(renamed), Is.True);
    }

    [Test]
    public void Watcher_GitAndAtomicChurn_DoesNotPublishProjectChanges()
    {
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(project.Path, "db"));
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var reconciledCount = 0;
        container.FilesReconciled += (_, _) => reconciledCount++;
        container.StartWatching();

        project.Write(@".git\objects\one", [1]);
        project.Write(@".git\objects\two", [2]);
        project.Write(
            @"db\item.0123456789abcdef0123456789abcdef.tmp",
            [3]);

        Thread.Sleep(800);
        Assert.Multiple(() =>
        {
            Assert.That(reconciledCount, Is.Zero);
            Assert.That(container.FileList, Is.Empty);
        });
    }

    [Test]
    public void Watcher_RenameAcrossMetadataBoundary_RefreshesExactlyOnce()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        Directory.CreateDirectory(Path.Combine(project.Path, ".git"));
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.SetIgnored(@"db\item.bin", true);
        var reconciledCount = 0;
        container.FilesReconciled += (_, _) => reconciledCount++;
        container.StartWatching();

        File.Move(
            Path.Combine(project.Path, "db", "item.bin"),
            Path.Combine(project.Path, ".git", "item.bin"));

        Assert.That(
            WaitUntil(() => !container.FileList.ContainsKey(@"db\item.bin")),
            Is.True);
        Thread.Sleep(500);
        Assert.Multiple(() =>
        {
            Assert.That(reconciledCount, Is.EqualTo(1));
            Assert.That(container.IsIgnored(@"db\item.bin"), Is.False);
            Assert.That(
                FolderProjectSettings.Load(project.Path).IgnoredPaths,
                Is.Empty);
        });
    }

    [Test]
    public void Watcher_ExternalEmptyDirectoryChanges_PersistSettings()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                IgnoredPaths = new HashSet<string>(
                    [@"db\ignored.bin"],
                    StringComparer.OrdinalIgnoreCase),
            });
        container.StartWatching();

        Directory.CreateDirectory(Path.Combine(project.Path, "empty", "one"));
        Assert.That(
            WaitUntil(
                () => FolderProjectSettings.Load(project.Path)
                    .EmptyDirectories.Contains(@"empty\one")),
            Is.True);

        Directory.Delete(Path.Combine(project.Path, "empty"), true);
        Assert.That(
            WaitUntil(
                () => !FolderProjectSettings.Load(project.Path)
                    .EmptyDirectories.Contains(@"empty\one")),
            Is.True);
        Assert.That(
            FolderProjectSettings.Load(project.Path).IgnoredPaths,
            Does.Contain(@"db\ignored.bin"));
    }

    [Test]
    public void Watcher_DeleteGitPolicyNamedResourceDirectory_Reconciles()
    {
        using var project = new TemporaryDirectory();
        Directory.CreateDirectory(
            Path.Combine(project.Path, ".gitignore"));
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        Assert.That(
            container.EmptyDirectories.Contains(".gitignore"),
            Is.True);
        container.SetIgnored(@".gitignore\missing.bin", true);
        container.StartWatching();

        Directory.Delete(
            Path.Combine(project.Path, ".gitignore"),
            true);

        Assert.That(
            WaitUntil(
                () => !container.EmptyDirectories.Contains(
                    ".gitignore")),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                container.IsIgnored(@".gitignore\missing.bin"),
                Is.False);
            Assert.That(
                FolderProjectSettings.Load(project.Path).IgnoredPaths,
                Is.Empty);
        });
    }

    [Test]
    public void WatcherScan_UnauthorizedEnumeration_PreservesStateAndRecovers()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\existing.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var existingFile = container.FileList[@"db\existing.bin"];
        project.Write(@"db\added.bin", [2]);
        var enumerate = container.EnumerateFileSystemEntries;
        container.EnumerateFileSystemEntries =
            path => path.EndsWith(
                $"{Path.DirectorySeparatorChar}db",
                StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("denied")
                : enumerate(path);

        Assert.That(
            () => container.ReconcileAfterWatcherEvent(),
            Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\existing.bin" }));
            Assert.That(
                container.FileList[@"db\existing.bin"],
                Is.SameAs(existingFile));
        });

        container.EnumerateFileSystemEntries = enumerate;
        container.ReconcileAfterWatcherEvent();

        Assert.That(
            container.FileList.Keys,
            Is.EquivalentTo(
                new[] { @"db\existing.bin", @"db\added.bin" }));
    }

    [Test]
    public void WatcherScan_UnauthorizedAttributes_PreservesStateAndRecovers()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\existing.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var existingFile = container.FileList[@"db\existing.bin"];
        project.Write(@"db\added.bin", [2]);
        var getAttributes = container.GetFileAttributes;
        container.GetFileAttributes =
            path => path.EndsWith(
                $"{Path.DirectorySeparatorChar}db",
                StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("denied")
                : getAttributes(path);

        Assert.That(
            () => container.ReconcileAfterWatcherEvent(),
            Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(new[] { @"db\existing.bin" }));
            Assert.That(
                container.FileList[@"db\existing.bin"],
                Is.SameAs(existingFile));
        });

        container.GetFileAttributes = getAttributes;
        container.ReconcileAfterWatcherEvent();

        Assert.That(
            container.FileList.Keys,
            Is.EquivalentTo(
                new[] { @"db\existing.bin", @"db\added.bin" }));
    }

    [Test]
    public void WatcherSettingsPersistenceFailure_DoesNotEscapeAndRecovers()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\old.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        container.SetIgnored(@"db\old.bin", true);
        var oldPath = Path.Combine(project.Path, "db", "old.bin");
        var newPath = Path.Combine(project.Path, "db", "new.bin");
        File.Move(oldPath, newPath);
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var originalAttributes = File.GetAttributes(settingsPath);
        File.SetAttributes(
            settingsPath,
            originalAttributes | FileAttributes.ReadOnly);

        try
        {
            var change = new FolderProjectFileSystemChangedEventArgs(
                WatcherChangeTypes.Renamed,
                newPath,
                oldPath);
            Assert.That(
                () => container.ProcessWatcherChange(change),
                Throws.Nothing);
            Assert.Multiple(() =>
            {
                Assert.That(
                    container.FileList.Keys,
                    Is.EquivalentTo(new[] { @"db\old.bin" }));
                Assert.That(container.IsIgnored(@"db\new.bin"), Is.True);
                Assert.That(
                    FolderProjectSettings.Load(project.Path).IgnoredPaths,
                    Is.EquivalentTo(new[] { @"db\old.bin" }));
            });

            File.SetAttributes(settingsPath, originalAttributes);
            container.ProcessWatcherChange(change);
            container.ReconcileAfterWatcherEvent();

            Assert.Multiple(() =>
            {
                Assert.That(
                    container.FileList.Keys,
                    Is.EquivalentTo(new[] { @"db\new.bin" }));
                Assert.That(
                    FolderProjectSettings.Load(project.Path).IgnoredPaths,
                    Is.EquivalentTo(new[] { @"db\new.bin" }));
            });
        }
        finally
        {
            File.SetAttributes(settingsPath, originalAttributes);
        }
    }

    [Test]
    public void WatcherEmptyDirectorySettingsPersistenceFailure_RetriesOnNextReconcile()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        Directory.CreateDirectory(
            Path.Combine(project.Path, "empty", "external"));
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var originalAttributes = File.GetAttributes(settingsPath);
        File.SetAttributes(
            settingsPath,
            originalAttributes | FileAttributes.ReadOnly);

        try
        {
            Assert.That(
                () => container.ReconcileAfterWatcherEvent(),
                Throws.Nothing);
            Assert.Multiple(() =>
            {
                Assert.That(
                    container.EmptyDirectories,
                    Does.Contain(@"empty\external"));
                Assert.That(
                    FolderProjectSettings.Load(project.Path).EmptyDirectories,
                    Does.Not.Contain(@"empty\external"));
            });

            File.SetAttributes(settingsPath, originalAttributes);
            container.ReconcileAfterWatcherEvent();

            Assert.That(
                FolderProjectSettings.Load(project.Path).EmptyDirectories,
                Does.Contain(@"empty\external"));
        }
        finally
        {
            File.SetAttributes(settingsPath, originalAttributes);
        }
    }

    [Test]
    public void WatcherSettingsPersistenceFailure_PublishesPendingReconciliationOnce()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        project.Write(@"db\added.bin", [1]);
        Directory.CreateDirectory(
            Path.Combine(project.Path, "empty", "external"));
        var reconciledCount = 0;
        FolderProjectReconciledEventArgs? reconciled = null;
        container.FilesReconciled += (_, eventArgs) =>
        {
            reconciledCount++;
            reconciled = eventArgs;
        };
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var originalAttributes = File.GetAttributes(settingsPath);
        File.SetAttributes(
            settingsPath,
            originalAttributes | FileAttributes.ReadOnly);

        try
        {
            container.ReconcileAfterWatcherEvent();
            Assert.Multiple(() =>
            {
                Assert.That(reconciledCount, Is.Zero);
                Assert.That(
                    container.FileList.Keys,
                    Does.Contain(@"db\added.bin"));
                Assert.That(
                    container.EmptyDirectories,
                    Does.Contain(@"empty\external"));
            });

            File.SetAttributes(settingsPath, originalAttributes);
            container.ReconcileAfterWatcherEvent();
            container.ReconcileAfterWatcherEvent();

            Assert.Multiple(() =>
            {
                Assert.That(reconciledCount, Is.EqualTo(1));
                Assert.That(reconciled, Is.Not.Null);
                Assert.That(reconciled!.Changes.Added, Is.EqualTo(1));
                Assert.That(
                    reconciled.AddedFiles.Select(file => file.Name),
                    Is.EquivalentTo(new[] { "added.bin" }));
                Assert.That(reconciled.DirectoriesChanged, Is.True);
                Assert.That(
                    FolderProjectSettings.Load(project.Path).EmptyDirectories,
                    Does.Contain(@"empty\external"));
            });
        }
        finally
        {
            File.SetAttributes(settingsPath, originalAttributes);
        }
    }

    [Test]
    public void WatcherPendingAddThenDelete_PublishesOrderedDeltas()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var addedPath = Path.Combine(
            project.Path,
            "db",
            "added.bin");
        project.Write(@"db\added.bin", [1]);
        Directory.CreateDirectory(
            Path.Combine(project.Path, "empty", "external"));
        var reconciliations =
            new List<FolderProjectReconciledEventArgs>();
        container.FilesReconciled +=
            (_, eventArgs) => reconciliations.Add(eventArgs);
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var originalAttributes = File.GetAttributes(settingsPath);
        File.SetAttributes(
            settingsPath,
            originalAttributes | FileAttributes.ReadOnly);

        try
        {
            container.ReconcileAfterWatcherEvent();
            Assert.That(reconciliations, Is.Empty);

            File.Delete(addedPath);
            File.SetAttributes(settingsPath, originalAttributes);
            container.ReconcileAfterWatcherEvent();
            container.ReconcileAfterWatcherEvent();

            Assert.Multiple(() =>
            {
                Assert.That(reconciliations, Has.Count.EqualTo(2));
                Assert.That(
                    reconciliations[0].Changes,
                    Is.EqualTo(
                        new FolderProjectReconciliation(1, 0, 0)));
                Assert.That(
                    reconciliations[1].Changes,
                    Is.EqualTo(
                        new FolderProjectReconciliation(0, 1, 0)));
                Assert.That(
                    reconciliations[0].AddedFiles,
                    Has.Count.EqualTo(1));
                Assert.That(
                    reconciliations[0].RemovedFiles,
                    Is.Empty);
                Assert.That(
                    reconciliations[1].AddedFiles,
                    Is.Empty);
                Assert.That(
                    reconciliations[1].RemovedFiles,
                    Is.EquivalentTo(
                        reconciliations[0].AddedFiles));
                Assert.That(
                    reconciliations.Any(
                        item => item.AddedFiles
                            .Intersect(item.RemovedFiles)
                            .Any()),
                    Is.False);
                Assert.That(
                    container.FileList.ContainsKey(@"db\added.bin"),
                    Is.False);
            });
        }
        finally
        {
            File.SetAttributes(settingsPath, originalAttributes);
        }
    }

    private static bool WaitUntil(Func<bool> predicate)
    {
        return SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(5));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; private set; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-project-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Write(string relativePath, byte[] bytes)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }

        public void Adopt(string path)
        {
            Path = path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
