using Moq;
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
            initialService.SavePackContainer(CreatePackContainer(), packPath, false, gameInfo);

            var loader = new PackFileContainerLoader(new ApplicationSettingsService(GameTypeEnum.Rome2));
            var saves = Enumerable.Range(0, serviceCount)
                .Select(_ => (Service: CreatePackFileService(), Pack: loader.Load(packPath)!))
                .ToList();
            using var ready = new CountdownEvent(serviceCount);
            using var start = new ManualResetEventSlim();

            var tasks = saves.Select(save => Task.Factory.StartNew(() =>
            {
                ready.Signal();
                start.Wait();
                save.Service.SavePackContainer(save.Pack, packPath, false, gameInfo);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            Assert.That(ready.Wait(TimeSpan.FromSeconds(10)), Is.True, "Save workers did not reach the start gate.");
            start.Set();
            await Task.WhenAll(tasks);

            var remainingFiles = Directory.GetFiles(_tempDirectory)
                .Where(file => !string.Equals(file, packPath, StringComparison.OrdinalIgnoreCase));
            Assert.That(remainingFiles, Is.Empty, "A save left a temporary file beside the target pack.");

            var loaded = loader.Load(packPath);
            Assert.That(loaded, Is.Not.Null);
            try
            {
                Assert.That(loaded!.FileList.Keys, Does.Contain("data\\entry.bin"));
                Assert.That(loaded.FileList["data\\entry.bin"].DataSource.ReadData(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            }
            finally
            {
                foreach (var source in loaded!.FileList.Values.Select(file => file.DataSource).OfType<PackedFileSource>())
                    source.Parent.CloseStream();
            }
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
                .Setup(service => service.SavePackContainer(
                    editablePack,
                    packPath,
                    false,
                    It.IsAny<GameInformation>()))
                .Callback(() =>
                {
                    saveEntered.Set();
                    releaseSave.Wait();
                });

            var settings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            var autoSaveService = new PackAutoSaveService(packFileService.Object, settings);
            var firstSave = Task.Run(autoSaveService.TryAutoSave);

            Assert.That(saveEntered.Wait(TimeSpan.FromSeconds(10)), Is.True, "The first auto-save did not start.");
            var secondSave = Task.Run(autoSaveService.TryAutoSave);
            try
            {
                Assert.That(secondSave.Wait(TimeSpan.FromMilliseconds(500)), Is.True,
                    "The second auto-save queued instead of returning immediately.");
                Assert.That(await secondSave, Is.False);
            }
            finally
            {
                releaseSave.Set();
            }

            Assert.That(await firstSave, Is.True);
            packFileService.Verify(service => service.SavePackContainer(
                editablePack,
                packPath,
                false,
                It.IsAny<GameInformation>()), Times.Once);
        }

        private PackFileService CreatePackFileService()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            settings.CurrentSettings.UseZstdCompression = false;
            settings.CurrentSettings.BackupPath = Path.Combine(_tempDirectory, "backups");
            settings.CurrentSettings.MaxBackupCount = 2;
            return new PackFileService(null) { SettingsService = settings };
        }

        private static PackFileContainer CreatePackContainer()
        {
            var container = new PackFileContainer("shared")
            {
                Header = new PFHeader(PackFileVersionConverter.ToString(PackFileVersion.PFH4), PackFileCAType.MOD),
            };
            container.FileList["data\\entry.bin"] = PackFile.CreateFromBytes("entry.bin", [1, 2, 3, 4]);
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
