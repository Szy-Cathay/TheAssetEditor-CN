using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise;
using Moq;
using NAudio.Wave;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioPlaybackTests
    {
        [TestMethod]
        public void PlayStreamedWem_MissingFile_ReturnsWithoutThrowing()
        {
            var packFileService = new Mock<IPackFileService>();
            var repository = new Mock<IAudioRepository>();
            var soundEngine = new Mock<ISoundEngine>();
            var soundPlayer = new SoundPlayer(
                packFileService.Object,
                repository.Object,
                new VgStreamWrapper(),
                soundEngine.Object);

            var result = soundPlayer.Play(new AudioSource(123, 100));

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void PlayStreamedWem_UsesTheHircLanguageBeforeSharedPaths()
        {
            var packFileService = new Mock<IPackFileService>();
            var wemFile = PackFile.CreateFromBytes("123.wem", [0]);
            packFileService
                .Setup(x => x.FindFile(
                    "audio\\wwise\\french(france)\\123.wem",
                    It.IsAny<PackFileContainer?>()))
                .Returns(wemFile);
            var repository = new Mock<IAudioRepository>();
            var languageFound = true;
            repository
                .Setup(x => x.GetNameFromId(100, out languageFound))
                .Returns("french(france)");
            var soundEngine = new Mock<ISoundEngine>();
            var soundPlayer = new SoundPlayer(
                packFileService.Object,
                repository.Object,
                new VgStreamWrapper(),
                soundEngine.Object);

            soundPlayer.Play(new AudioSource(123, 100));

            packFileService.Verify(
                x => x.FindFile(
                    "audio\\wwise\\french(france)\\123.wem",
                    It.IsAny<PackFileContainer?>()),
                Times.Once);
            packFileService.Verify(
                x => x.FindFile(
                    "audio\\wwise\\chinese\\123.wem",
                    It.IsAny<PackFileContainer?>()),
                Times.Never);
        }

        [TestMethod]
        public void Play_LoadsConvertedWavIntoTheInternalSoundEngine()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var wavPath = Path.Combine(directory, "123.wav");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(wavPath, [1, 2, 3]);

            try
            {
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(x => x.FindFile(
                        "audio\\wwise\\123.wem",
                        It.IsAny<PackFileContainer?>()))
                    .Returns(PackFile.CreateFromBytes("123.wem", [4, 5, 6]));
                var repository = new Mock<IAudioRepository>();
                var languageFound = false;
                repository
                    .Setup(x => x.GetNameFromId(100, out languageFound))
                    .Returns(string.Empty);
                var converter = new Mock<VgStreamWrapper>();
                converter
                    .Setup(x => x.ConvertFileUsingVgStream(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                    .Returns(Result<string>.FromOk(wavPath));
                var soundEngine = new Mock<ISoundEngine>();
                var soundPlayer = new SoundPlayer(
                    packFileService.Object,
                    repository.Object,
                    converter.Object,
                    soundEngine.Object);

                var result = soundPlayer.Play(new AudioSource(123, 100));

                Assert.IsTrue(result);
                soundEngine.Verify(
                    x => x.LoadFromWavData(
                        It.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 1, 2, 3 }))),
                    Times.Once);
                soundEngine.Verify(x => x.PlayPause(), Times.Once);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Play_TheSameSourceAgainTogglesWithoutConvertingAgain()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var wavPath = Path.Combine(directory, "123.wav");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(wavPath, [1]);

            try
            {
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(x => x.FindFile(
                        "audio\\wwise\\123.wem",
                        It.IsAny<PackFileContainer?>()))
                    .Returns(PackFile.CreateFromBytes("123.wem", [2]));
                var repository = new Mock<IAudioRepository>();
                var languageFound = false;
                repository
                    .Setup(x => x.GetNameFromId(100, out languageFound))
                    .Returns(string.Empty);
                var converter = new Mock<VgStreamWrapper>();
                converter
                    .Setup(x => x.ConvertFileUsingVgStream(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                    .Returns(Result<string>.FromOk(wavPath));
                var soundEngine = new Mock<ISoundEngine>();
                var soundPlayer = new SoundPlayer(
                    packFileService.Object,
                    repository.Object,
                    converter.Object,
                    soundEngine.Object);
                var source = new AudioSource(123, 100);

                Assert.IsTrue(soundPlayer.Play(source));
                Assert.IsTrue(soundPlayer.Play(source));

                converter.Verify(
                    x => x.ConvertFileUsingVgStream(
                        It.IsAny<string>(),
                        It.IsAny<string>()),
                    Times.Once);
                soundEngine.Verify(x => x.PlayPause(), Times.Exactly(2));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Prepare_ConvertsAudioWithoutStartingPlayback()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var wavPath = Path.Combine(directory, "123.wav");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(wavPath, [1, 2, 3]);

            try
            {
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(x => x.FindFile(
                        "audio\\wwise\\123.wem",
                        It.IsAny<PackFileContainer?>()))
                    .Returns(PackFile.CreateFromBytes("123.wem", [4, 5, 6]));
                var repository = new Mock<IAudioRepository>();
                var languageFound = false;
                repository
                    .Setup(x => x.GetNameFromId(100, out languageFound))
                    .Returns(string.Empty);
                var converter = new Mock<VgStreamWrapper>();
                converter
                    .Setup(x => x.ConvertFileUsingVgStream(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                    .Returns(Result<string>.FromOk(wavPath));
                var soundEngine = new Mock<ISoundEngine>();
                var soundPlayer = new SoundPlayer(
                    packFileService.Object,
                    repository.Object,
                    converter.Object,
                    soundEngine.Object);

                var result = soundPlayer.Prepare(new AudioSource(123, 100));

                Assert.IsNotNull(result);
                CollectionAssert.AreEqual(
                    new byte[] { 1, 2, 3 },
                    result.WavData);
                soundEngine.Verify(
                    x => x.LoadFromWavData(It.IsAny<byte[]>()),
                    Times.Never);
                soundEngine.Verify(x => x.PlayPause(), Times.Never);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Seek_PreparedAudioLoadsItAndSetsPlaybackTime()
        {
            var soundEngine = new Mock<ISoundEngine>();
            var soundPlayer = new SoundPlayer(
                Mock.Of<IPackFileService>(),
                Mock.Of<IAudioRepository>(),
                new VgStreamWrapper(),
                soundEngine.Object);
            var playbackData = new AudioPlaybackData(
                new AudioSource(123, 100),
                [1, 2, 3]);

            var result = soundPlayer.Seek(
                playbackData,
                TimeSpan.FromSeconds(1.25));

            Assert.IsTrue(result);
            soundEngine.Verify(
                x => x.LoadFromWavData(
                    It.Is<byte[]>(bytes => bytes.SequenceEqual(
                        new byte[] { 1, 2, 3 }))),
                Times.Once);
            soundEngine.Verify(
                x => x.SetPlaybackTime(TimeSpan.FromSeconds(1.25)),
                Times.Once);
            soundEngine.Verify(x => x.PlayPause(), Times.Never);
        }

        [TestMethod]
        public void SoundEngine_SetPlaybackTimeWorksBeforeOutputDeviceExists()
        {
            using var soundEngine = new SoundEngine(
                Mock.Of<IPackFileService>());
            soundEngine.LoadFromWavData(CreateSilentWav(TimeSpan.FromSeconds(2)));

            soundEngine.SetPlaybackTime(TimeSpan.FromSeconds(1.25));

            Assert.AreEqual(
                TimeSpan.FromSeconds(1.25),
                soundEngine.CurrentPlaybackTime);

            soundEngine.Stop();

            Assert.AreEqual(TimeSpan.Zero, soundEngine.CurrentPlaybackTime);
        }

        [TestMethod]
        public async Task WaveformRenderer_RendersDirectlyFromWavData()
        {
            var renderer = new WaveformRendererService(
                Mock.Of<IPackFileService>());

            var result = await renderer.RenderAsync(
                CreateSilentWav(TimeSpan.FromSeconds(1)),
                320,
                CancellationToken.None);

            Assert.AreEqual(320, result.PixelWidth);
            Assert.AreEqual(
                TimeSpan.FromSeconds(1),
                result.TotalTime);
            Assert.IsTrue(result.Visualisation.PixelHeight > 0);
        }

        [TestMethod]
        public void Play_WhenTheSoundEngineFails_DoesNotReuseTheFailedLoad()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var wavPath = Path.Combine(directory, "123.wav");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(wavPath, [1]);

            try
            {
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(x => x.FindFile(
                        "audio\\wwise\\123.wem",
                        It.IsAny<PackFileContainer?>()))
                    .Returns(PackFile.CreateFromBytes("123.wem", [2]));
                var repository = new Mock<IAudioRepository>();
                var languageFound = false;
                repository
                    .Setup(x => x.GetNameFromId(100, out languageFound))
                    .Returns(string.Empty);
                var converter = new Mock<VgStreamWrapper>();
                converter
                    .Setup(x => x.ConvertFileUsingVgStream(
                        It.IsAny<string>(),
                        It.IsAny<string>()))
                    .Returns(Result<string>.FromOk(wavPath));
                var soundEngine = new Mock<ISoundEngine>();
                soundEngine
                    .SetupSequence(x => x.PlayPause())
                    .Throws(new InvalidOperationException())
                    .Pass();
                var soundPlayer = new SoundPlayer(
                    packFileService.Object,
                    repository.Object,
                    converter.Object,
                    soundEngine.Object);
                var source = new AudioSource(123, 100);

                Assert.IsFalse(soundPlayer.Play(source));
                Assert.IsTrue(soundPlayer.Play(source));

                converter.Verify(
                    x => x.ConvertFileUsingVgStream(
                        It.IsAny<string>(),
                        It.IsAny<string>()),
                    Times.Exactly(2));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ExportWem_WritesTheOriginalBytes()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var outputPath = Path.Combine(directory, "123.wem");
            Directory.CreateDirectory(directory);

            try
            {
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(x => x.FindFile(
                        "audio\\wwise\\123.wem",
                        It.IsAny<PackFileContainer?>()))
                    .Returns(PackFile.CreateFromBytes("123.wem", [1, 2, 3]));
                var repository = new Mock<IAudioRepository>();
                var languageFound = false;
                repository
                    .Setup(x => x.GetNameFromId(100, out languageFound))
                    .Returns(string.Empty);
                var soundPlayer = new SoundPlayer(
                    packFileService.Object,
                    repository.Object,
                    new VgStreamWrapper(),
                    Mock.Of<ISoundEngine>());

                var result = soundPlayer.Export(
                    new AudioSource(123, 100),
                    AudioExportFormat.Wem,
                    outputPath);

                Assert.IsTrue(result);
                CollectionAssert.AreEqual(
                    new byte[] { 1, 2, 3 },
                    File.ReadAllBytes(outputPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ExportWem_EmbeddedAudioOutsideTheBankBounds_ReturnsFalse()
        {
            var repository = new Mock<IAudioRepository>();
            var dataSoundbankFound = true;
            repository
                .Setup(x => x.GetNameFromId(456, out dataSoundbankFound))
                .Returns("data_bank");
            repository
                .SetupGet(x => x.PackFileByBnkName)
                .Returns(new Dictionary<string, PackFile>
                {
                    ["data_bank.bnk"] =
                        PackFile.CreateFromBytes("data_bank.bnk", [1, 2, 3])
                });
            var soundPlayer = new SoundPlayer(
                Mock.Of<IPackFileService>(),
                repository.Object,
                new VgStreamWrapper(),
                Mock.Of<ISoundEngine>());

            var result = soundPlayer.Export(
                new AudioSource(123, 100, 456, 2, 2),
                AudioExportFormat.Wem,
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wem"));

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ConvertFileUsingVgStream_DoesNotReuseAStaleTarget()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var sourcePath = Path.Combine(directory, "invalid.wem");
            var targetPath = Path.Combine(directory, "invalid.wav");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(sourcePath, [0]);
            File.WriteAllBytes(targetPath, [1, 2, 3]);

            try
            {
                var result = new VgStreamWrapper().ConvertFileUsingVgStream(sourcePath, targetPath);

                Assert.IsFalse(result.IsSuccess);
                Assert.IsFalse(File.Exists(targetPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ConvertFileUsingVgStream_PreCancelled_PreservesExistingTarget()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioTests-{Guid.NewGuid():N}");
            var sourcePath = Path.Combine(directory, "input.wem");
            var targetPath = Path.Combine(directory, "existing.wav");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(sourcePath, [0]);
            File.WriteAllBytes(targetPath, [1, 2, 3]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                Assert.ThrowsException<OperationCanceledException>(() =>
                    new VgStreamWrapper().ConvertFileUsingVgStream(
                        sourcePath,
                        targetPath,
                        cancellation.Token));

                CollectionAssert.AreEqual(
                    new byte[] { 1, 2, 3 },
                    File.ReadAllBytes(targetPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ExportFileToAEFolder_ReportsWriteFailure()
        {
            var packFileService = new Mock<IPackFileService>();
            var repository = new Mock<IAudioRepository>();
            var soundPlayer = new SoundPlayer(
                packFileService.Object,
                repository.Object,
                new VgStreamWrapper(),
                Mock.Of<ISoundEngine>());

            var result = soundPlayer.ExportFileToAEFolder("invalid\0.wem", []);

            Assert.IsFalse(result);
        }

        private static byte[] CreateSilentWav(TimeSpan duration)
        {
            const int sampleRate = 8000;
            var waveFormat = new WaveFormat(sampleRate, 16, 1);
            var sampleCount = (int)(sampleRate * duration.TotalSeconds);
            var data = new byte[sampleCount * waveFormat.BlockAlign];
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(stream, waveFormat))
            {
                writer.Write(data, 0, data.Length);
                writer.Flush();
            }

            return stream.ToArray();
        }
    }
}
