using AssetEditor.Services;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace AssetEditorTests;

public class RecentFilesTrackerTests
{
    [Test]
    public void InternalReattach_DoesNotReorderRecentProjectOrSave()
    {
        using var projectRoot = new TemporaryDirectory();
        var eventHub = new TestEventHub();
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settings.CurrentSettings.RecentFolderProjectPaths.Add(
            projectRoot.Path);
        settings.CurrentSettings.RecentFolderProjectPaths.Add(
            @"D:\projects\newer");
        var saveCount = 0;
        using var tracker = new RecentFilesTracker(
            eventHub,
            settings,
            () => saveCount++);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });

        eventHub.Publish(new PackFileContainerAddedEvent(
            project,
            PackFileContainerAddedReason.InternalReattach));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                settings.CurrentSettings.RecentFolderProjectPaths,
                Is.EqualTo(new[]
                {
                    projectRoot.Path,
                    @"D:\projects\newer",
                }));
            NUnitAssert.That(saveCount, Is.Zero);
        });
    }

    [Test]
    public void UserOpen_RecordsProjectAndSaves()
    {
        using var projectRoot = new TemporaryDirectory();
        var eventHub = new TestEventHub();
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settings.CurrentSettings.RecentFolderProjectPaths.Add(
            projectRoot.Path);
        settings.CurrentSettings.RecentFolderProjectPaths.Add(
            @"D:\projects\newer");
        var saveCount = 0;
        using var tracker = new RecentFilesTracker(
            eventHub,
            settings,
            () => saveCount++);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });

        eventHub.Publish(new PackFileContainerAddedEvent(project));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                settings.CurrentSettings.RecentFolderProjectPaths,
                Is.EqualTo(new[]
                {
                    @"D:\projects\newer",
                    projectRoot.Path,
                }));
            NUnitAssert.That(saveCount, Is.EqualTo(1));
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-recent-project-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
