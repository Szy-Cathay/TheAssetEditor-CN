using AssetEditor.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
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

    [Test]
    public void UserOpen_InternalPackDoesNotBecomeRecentReference()
    {
        var eventHub = new TestEventHub();
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        var saveCount = 0;
        using var tracker = new RecentFilesTracker(
            eventHub,
            settings,
            () => saveCount++);
        var internalPack = new PackFileContainer("internal.pack")
        {
            SystemFilePath = @"D:\packs\internal.pack",
        };

        eventHub.Publish(new PackFileContainerAddedEvent(internalPack));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                settings.CurrentSettings.RecentPackFilePaths,
                Is.Empty);
            NUnitAssert.That(saveCount, Is.Zero);
        });
    }

    [Test]
    public void UserOpen_ReferencePackRecordsReferenceAndSaves()
    {
        var serviceProvider =
            new DependencyInjectionConfig(false).Build(true);
        try
        {
            var eventHub = serviceProvider
                .GetRequiredService<IGlobalEventHub>();
            var settings = new ApplicationSettingsService(
                GameTypeEnum.Warhammer3);
            var saveCount = 0;
            using var tracker = new RecentFilesTracker(
                eventHub,
                settings,
                () => saveCount++);
            var reference = new PackFileContainer("reference.pack")
            {
                SystemFilePath = @"D:\packs\reference.pack",
            };
            var packFileService = serviceProvider
                .GetRequiredService<IPackFileService>();
            packFileService.EnforceGameFilesMustBeLoaded = false;

            packFileService.AddReferencePack(reference);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    settings.CurrentSettings.RecentPackFilePaths,
                    Is.EqualTo(new[] { reference.SystemFilePath }));
                NUnitAssert.That(saveCount, Is.EqualTo(1));
            });
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
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
