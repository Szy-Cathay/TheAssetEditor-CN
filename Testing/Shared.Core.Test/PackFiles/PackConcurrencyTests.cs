using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Test.Shared.Core.PackFiles
{
    [NonParallelizable]
    internal class PackConcurrencyTests
    {
        private string _tempDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"AssetEditor-PackConcurrency-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        [Test]
        public void LoadAllCaFiles_ParallelManifestLoadsEveryPackOnEveryRun()
        {
            const int packCount = 256;
            const int loadCount = 10;
            var packBytes = CreatePackBytes();
            var manifestEntries = new string[packCount];

            for (var index = 0; index < packCount; index++)
            {
                var fileName = $"manifest_{index:D4}.pack";
                File.WriteAllBytes(Path.Combine(_tempDirectory, fileName), packBytes);
                manifestEntries[index] = $"{fileName}\t{packBytes.Length}";
            }

            File.WriteAllLines(Path.Combine(_tempDirectory, "manifest.txt"), manifestEntries);

            var settings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            settings.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair
            {
                Game = GameTypeEnum.Rome2,
                Path = _tempDirectory,
            });
            var loader = new PackFileContainerLoader(settings);

            for (var run = 0; run < loadCount; run++)
            {
                var loaded = loader.LoadAllCaFiles(GameTypeEnum.Rome2);

                Assert.That(loaded, Is.Not.Null, $"Load run {run} returned null.");
                Assert.That(loaded!.SourcePackFilePaths, Has.Count.EqualTo(packCount),
                    $"Load run {run} lost one or more manifest packs.");
            }
        }

        [Test]
        public async Task SavePackContainer_MultipleServiceInstancesSerializeSamePathSafely()
        {
            const int serviceCount = 8;
            var packPath = Path.Combine(_tempDirectory, "shared.pack");
            var gameInfo = GameInformationDatabase.GetGameById(GameTypeEnum.Rome2);
            var initialService = CreatePackFileService();
            initialService.SavePackContainer(CreatePackContainer([0, 0, 0, 0]), packPath, false, gameInfo);

            var loader = new PackFileContainerLoader(new ApplicationSettingsService(GameTypeEnum.Rome2));
            var saves = Enumerable.Range(0, serviceCount)
                .Select(index => (
                    Service: CreatePackFileService(),
                    Pack: CreatePackContainer(Enumerable.Repeat((byte)(index + 1), 4).ToArray())))
                .ToList();
            using var ready = new CountdownEvent(serviceCount);
            using var start = new ManualResetEventSlim();

            var tasks = saves.Select(save => Task.Factory.StartNew(() =>
            {
                ready.Signal();
                start.Wait();
                save.Service.SavePackContainer(save.Pack, packPath, false, gameInfo);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            var allSaves = Task.WhenAll(tasks);

            try
            {
                Assert.That(ready.Wait(TimeSpan.FromSeconds(10)), Is.True, "Save workers did not reach the start gate.");
                start.Set();
                await allSaves;

                var remainingFiles = Directory.GetFiles(_tempDirectory)
                    .Where(file => !string.Equals(file, packPath, StringComparison.OrdinalIgnoreCase));
                Assert.That(remainingFiles, Is.Empty, "A save left a temporary file beside the target pack.");

                var loaded = loader.Load(packPath);
                Assert.That(loaded, Is.Not.Null);
                try
                {
                    Assert.That(loaded!.FileList.Keys, Does.Contain("data\\entry.bin"));
                    var payload = loaded.FileList["data\\entry.bin"].DataSource.ReadData();
                    Assert.That(payload, Has.Length.EqualTo(4));
                    Assert.That(payload, Is.All.EqualTo(payload[0]));
                    Assert.That(payload[0], Is.InRange(1, serviceCount));
                }
                finally
                {
                    foreach (var source in loaded!.FileList.Values.Select(file => file.DataSource).OfType<PackedFileSource>())
                        source.Parent.CloseStream();
                }
            }
            finally
            {
                start.Set();
                try
                {
                    await allSaves;
                }
                catch
                {
                }
            }
        }

        [Test]
        public void TryAutoSavePackContainer_SavedEventRunsOnceAfterSaveGateIsReleased()
        {
            var firstPath = Path.Combine(_tempDirectory, "first.pack");
            var secondPath = Path.Combine(_tempDirectory, "second.pack");
            var gameInfo = GameInformationDatabase.GetGameById(GameTypeEnum.Rome2);
            var eventHub = new Mock<IGlobalEventHub>();
            var callbackHandled = 0;
            Task? nestedSave = null;
            PackFileService service = null!;

            eventHub
                .Setup(hub => hub.PublishGlobalEvent(It.IsAny<PackFileContainerSavedEvent>()))
                .Callback<PackFileContainerSavedEvent>(_ =>
                {
                    if (Interlocked.Exchange(ref callbackHandled, 1) != 0)
                        return;

                    nestedSave = Task.Factory.StartNew(
                        () => service.SavePackContainer(CreatePackContainer(), secondPath, false, gameInfo),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    Assert.That(nestedSave.Wait(TimeSpan.FromSeconds(2)), Is.True,
                        "The saved event callback still held the process-wide save gate.");
                });
            service = CreatePackFileService(eventHub.Object);
            var firstPack = CreatePackContainer();
            firstPack.SystemFilePath = firstPath;
            service.SetEditablePack(firstPack);

            try
            {
                Assert.That(service.TryAutoSavePackContainer(firstPack, firstPath, gameInfo), Is.True);
            }
            finally
            {
                if (nestedSave != null)
                    nestedSave.GetAwaiter().GetResult();
            }

            eventHub.Verify(
                hub => hub.PublishGlobalEvent(It.IsAny<PackFileContainerSavedEvent>()),
                Times.Exactly(2));
        }

        [Test]
        public void TryAutoSavePackContainer_AfterSaveAsDoesNotWriteTheOldPath()
        {
            var oldPath = Path.Combine(_tempDirectory, "old.pack");
            var newPath = Path.Combine(_tempDirectory, "new.pack");
            var gameInfo = GameInformationDatabase.GetGameById(GameTypeEnum.Rome2);
            var service = CreatePackFileService();
            var pack = CreatePackContainer();
            service.SetEditablePack(pack);
            service.SavePackContainer(pack, oldPath, false, gameInfo);
            var oldBytes = File.ReadAllBytes(oldPath);

            service.SavePackContainer(pack, newPath, false, gameInfo);
            pack.FileList["data\\entry.bin"].DataSource = new MemorySource([9, 9, 9, 9]);

            var saved = service.TryAutoSavePackContainer(pack, oldPath, gameInfo);

            Assert.That(saved, Is.False);
            Assert.That(pack.SystemFilePath, Is.EqualTo(newPath));
            Assert.That(File.ReadAllBytes(oldPath), Is.EqualTo(oldBytes));
            Assert.That(File.Exists(newPath), Is.True);
        }

        [Test]
        public async Task TryAutoSave_WhileSaveIsRunningReturnsFalseWithoutQueueing()
        {
            var packPath = Path.Combine(_tempDirectory, "autosave.pack");
            File.WriteAllBytes(packPath, [0]);
            var editablePack = CreatePackContainer();
            editablePack.SystemFilePath = packPath;

            using var saveEntered = new ManualResetEventSlim();
            using var releaseSave = new ManualResetEventSlim();
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.GetEditablePack()).Returns(editablePack);
            packFileService
                .Setup(service => service.TryAutoSavePackContainer(
                    editablePack,
                    packPath,
                    It.IsAny<GameInformation>()))
                .Returns(() =>
                {
                    saveEntered.Set();
                    releaseSave.Wait();
                    return true;
                });

            var settings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            var autoSaveService = new PackAutoSaveService(packFileService.Object, settings);
            var firstSave = Task.Run(autoSaveService.TryAutoSave);

            try
            {
                Assert.That(saveEntered.Wait(TimeSpan.FromSeconds(10)), Is.True, "The first auto-save did not start.");
                Assert.That(autoSaveService.TryAutoSave(), Is.False);
            }
            finally
            {
                releaseSave.Set();
                try
                {
                    await firstSave;
                }
                catch
                {
                }
            }

            Assert.That(await firstSave, Is.True);
            packFileService.Verify(service => service.TryAutoSavePackContainer(
                editablePack,
                packPath,
                It.IsAny<GameInformation>()), Times.Once);
        }

        private PackFileService CreatePackFileService(IGlobalEventHub? eventHub = null)
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            settings.CurrentSettings.UseZstdCompression = false;
            settings.CurrentSettings.BackupPath = Path.Combine(_tempDirectory, "backups");
            settings.CurrentSettings.MaxBackupCount = 2;
            return new PackFileService(eventHub) { SettingsService = settings };
        }

        private static PackFileContainer CreatePackContainer(byte[]? payload = null)
        {
            var container = new PackFileContainer("shared")
            {
                Header = new PFHeader(PackFileVersionConverter.ToString(PackFileVersion.PFH4), PackFileCAType.MOD),
            };
            container.FileList["data\\entry.bin"] = PackFile.CreateFromBytes("entry.bin", payload ?? [1, 2, 3, 4]);
            return container;
        }

        private static byte[] CreatePackBytes()
        {
            var gameInfo = GameInformationDatabase.GetGameById(GameTypeEnum.Rome2);
            var container = new PackFileContainer("manifest_template")
            {
                Header = new PFHeader(PackFileVersionConverter.ToString(PackFileVersion.PFH4), PackFileCAType.RELEASE),
            };
            container.FileList["data\\entry.bin"] = PackFile.CreateFromBytes("entry.bin", [1, 2, 3, 4]);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            PackFileSerializerWriter.SaveToByteArray("manifest_template.pack", container, writer, gameInfo, false);
            return stream.ToArray();
        }
    }
}
