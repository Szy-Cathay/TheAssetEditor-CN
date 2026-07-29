using Editors.Audio.Shared.Storage;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioStorageTests
    {
        [TestMethod]
        public void LoadBnkFiles_CollectsEveryParallelResult()
        {
            const int bankCount = 4000;
            var container = new PackFileContainer("audio");
            for (var i = 0; i < bankCount; i++)
            {
                var fileName = $"audio\\test_{i}.bnk";
                container.FileList[fileName] = PackFile.CreateFromBytes($"test_{i}.bnk", []);
            }

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var loader = new TestBnkLoader(packFileService.Object);

            var result = loader.LoadBnkFiles([]);

            Assert.AreEqual(bankCount, result.PackFileByBnkName.Count);
        }

        [TestMethod]
        public void LoadBnkFiles_ReportsProgressForEveryBank()
        {
            const int bankCount = 20;
            var container = new PackFileContainer("audio");
            for (var i = 0; i < bankCount; i++)
            {
                var fileName = $"audio\\progress_{i}.bnk";
                container.FileList[fileName] = PackFile.CreateFromBytes($"progress_{i}.bnk", []);
            }

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var progress = new RecordingProgress<AudioLoadProgress>();
            var loader = new TestBnkLoader(packFileService.Object);

            loader.LoadBnkFiles([], progress, CancellationToken.None);

            Assert.AreEqual(bankCount, progress.Values.Count);
            Assert.AreEqual(bankCount, progress.Values.Max(value => value.Completed));
            Assert.IsTrue(progress.Values.All(value => value.Total == bankCount));
        }

        [TestMethod]
        public void LoadBnkFiles_HonoursCancellation()
        {
            var container = new PackFileContainer("audio");
            container.FileList["audio\\cancel.bnk"] =
                PackFile.CreateFromBytes("cancel.bnk", []);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            var loader = new TestBnkLoader(packFileService.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsException<OperationCanceledException>(
                () => loader.LoadBnkFiles([], null, cancellation.Token));
        }

        [TestMethod]
        public void AudioRepository_UnsupportedGameExposesEmptyCollections()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer2);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));

            repository.Load([]);

            Assert.IsFalse(repository.IsCurrentGameSupported);
            Assert.AreEqual(0, repository.GetHircsByHircType(AkBkHircType.Event).Count);
            Assert.AreEqual(0, repository.GetHircs(1).Count);
            Assert.AreEqual(0, repository.PackFileByBnkName.Count);
        }

        [TestMethod]
        public void AudioRepository_ReloadsWhenReturningToAnEarlierLanguage()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var container = new PackFileContainer("audio");
            container.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                PackFile.CreateFromBytes("english.bnk", []);
            container.FileList["audio\\wwise\\french(france)\\french.bnk"] =
                PackFile.CreateFromBytes("french.bnk", []);

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            Assert.IsTrue(repository.IsCurrentGameSupported);
            repository.Load(["english(uk)"]);
            repository.Load(["french(france)"]);
            repository.Load(["english(uk)"]);

            Assert.AreEqual(3, loader.LoadCount);
        }

        [TestMethod]
        public void AudioRepository_AlwaysIncludesSharedSfxBanks()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var container = new PackFileContainer("audio");
            container.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                PackFile.CreateFromBytes("english.bnk", []);
            container.FileList["audio\\wwise\\french(france)\\french.bnk"] =
                PackFile.CreateFromBytes("french.bnk", []);
            container.FileList["audio\\wwise\\loading_screen_sfx__core.bnk"] =
                PackFile.CreateFromBytes("loading_screen_sfx__core.bnk", []);

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            repository.Load(["english(uk)"]);

            Assert.AreEqual(2, loader.LoadCount);
        }

        private sealed class TestBnkLoader(IPackFileService packFileService) : BnkLoader(packFileService)
        {
            private int _loadCount;
            public int LoadCount => _loadCount;

            public override ParsedBnkFile LoadBnkFile(
                PackFile bnkFile,
                string bnkFilePath,
                bool isCAHircItem,
                bool printData = false)
            {
                Interlocked.Increment(ref _loadCount);
                return new ParsedBnkFile();
            }
        }

        private sealed class RecordingProgress<T> : IProgress<T>
        {
            private readonly object _lock = new();
            public List<T> Values { get; } = [];

            public void Report(T value)
            {
                lock (_lock)
                    Values.Add(value);
            }
        }
    }
}
