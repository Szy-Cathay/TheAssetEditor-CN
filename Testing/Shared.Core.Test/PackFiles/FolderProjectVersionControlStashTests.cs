using LibGit2Sharp;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectVersionControlStashTests
{
    private static readonly FolderProjectGitIdentity s_identity =
        new("AE 用户", "ae-user@example.invalid");

    [Test]
    public void StashChanges_CapturesAllChangesAndCleansRepository()
    {
        using var project = new TemporaryDirectory();
        var service = new FolderProjectVersionControlService();
        Write(project.Path, "staged.txt", "one");
        Write(project.Path, "working.txt", "one");
        service.Initialize(project.Path, s_identity);
        Write(project.Path, "staged.txt", "two");
        Write(project.Path, "working.txt", "two");
        Write(project.Path, "untracked.txt", "new");
        service.StageChanges(project.Path, ["staged.txt"]);

        var stash = service.StashChanges(project.Path, "切换前储藏");

        Assert.Multiple(() =>
        {
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
            Assert.That(stash.Index, Is.Zero);
            Assert.That(stash.Message, Does.Contain("切换前储藏"));
            Assert.That(
                stash.Paths,
                Is.EquivalentTo(
                    new[]
                    {
                        "staged.txt",
                        "working.txt",
                        "untracked.txt",
                    }));
            Assert.That(service.GetStashes(project.Path), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ApplyStash_RestoresIndexAndWorktreeWithoutRemovingEntry()
    {
        using var project = new TemporaryDirectory();
        var service = CreateStashedRepository(project.Path);

        service.ApplyStash(project.Path, 0);

        var changes = service.GetStatus(project.Path).Changes
            .ToDictionary(change => change.RepositoryPath);
        Assert.Multiple(() =>
        {
            Assert.That(
                changes["staged.txt"].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged),
                Is.True);
            Assert.That(
                changes["staged.txt"].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Unstaged),
                Is.False);
            Assert.That(
                changes["working.txt"].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Unstaged),
                Is.True);
            Assert.That(
                changes["untracked.txt"].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Untracked),
                Is.True);
            Assert.That(service.GetStashes(project.Path), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void PopStash_RestoresChangesAndRemovesEntry()
    {
        using var project = new TemporaryDirectory();
        var service = CreateStashedRepository(project.Path);

        service.PopStash(project.Path, 0);

        Assert.Multiple(() =>
        {
            Assert.That(service.GetStatus(project.Path).IsClean, Is.False);
            Assert.That(service.GetStashes(project.Path), Is.Empty);
        });
    }

    [Test]
    public void DeleteAndClearStashes_RemoveOnlyRequestedEntries()
    {
        using var project = new TemporaryDirectory();
        var service = new FolderProjectVersionControlService();
        Write(project.Path, "file.txt", "one");
        service.Initialize(project.Path, s_identity);
        Write(project.Path, "file.txt", "two");
        service.StashChanges(project.Path, "first");
        Write(project.Path, "file.txt", "three");
        service.StashChanges(project.Path, "second");

        service.DeleteStash(project.Path, 1);

        Assert.That(service.GetStashes(project.Path), Has.Count.EqualTo(1));

        service.ClearStashes(project.Path);

        Assert.That(service.GetStashes(project.Path), Is.Empty);
    }

    [Test]
    public void SwitchBranch_StashChanges_LeavesCleanTargetAndKeepsStash()
    {
        using var project = new TemporaryDirectory();
        var service = new FolderProjectVersionControlService();
        Write(project.Path, "file.txt", "one");
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "other");
        Write(project.Path, "file.txt", "two");
        Write(project.Path, "untracked.txt", "new");

        var branch = service.SwitchBranch(
            project.Path,
            "other",
            FolderProjectBranchSwitchMode.StashChanges,
            "WIP on master");

        Assert.Multiple(() =>
        {
            Assert.That(branch.Name, Is.EqualTo("other"));
            Assert.That(branch.IsCurrent, Is.True);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
            Assert.That(service.GetStashes(project.Path), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SwitchBranch_DiscardChanges_DropsChangesWithoutKeepingStash()
    {
        using var project = new TemporaryDirectory();
        var service = new FolderProjectVersionControlService();
        Write(project.Path, "file.txt", "one");
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "other");
        Write(project.Path, "file.txt", "two");
        Write(project.Path, "untracked.txt", "new");

        var branch = service.SwitchBranch(
            project.Path,
            "other",
            FolderProjectBranchSwitchMode.DiscardChanges);

        Assert.Multiple(() =>
        {
            Assert.That(branch.Name, Is.EqualTo("other"));
            Assert.That(branch.IsCurrent, Is.True);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
            Assert.That(service.GetStashes(project.Path), Is.Empty);
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, "file.txt")),
                Is.EqualTo("one"));
            Assert.That(
                File.Exists(Path.Combine(project.Path, "untracked.txt")),
                Is.False);
        });
    }

    private static FolderProjectVersionControlService CreateStashedRepository(
        string projectRoot)
    {
        var service = new FolderProjectVersionControlService();
        Write(projectRoot, "staged.txt", "one");
        Write(projectRoot, "working.txt", "one");
        service.Initialize(projectRoot, s_identity);
        Write(projectRoot, "staged.txt", "two");
        Write(projectRoot, "working.txt", "two");
        Write(projectRoot, "untracked.txt", "new");
        service.StageChanges(projectRoot, ["staged.txt"]);
        service.StashChanges(projectRoot, "切换前储藏");
        return service;
    }

    private static void Write(
        string projectRoot,
        string relativePath,
        string contents)
    {
        File.WriteAllText(Path.Combine(projectRoot, relativePath), contents);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-vcs-stash-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            foreach (var file in Directory.EnumerateFiles(
                         Path,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Path, true);
        }
    }
}
