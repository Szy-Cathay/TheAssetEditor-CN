using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events.WaveformVisualiser;
using Editors.Audio.AudioEditor.Presentation.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.Shared.Wwise;
using Moq;
using NAudio.Wave;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioEditorWaveformTests
    {
        [TestInitialize]
        public void LoadLocalization()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
        }

        [TestMethod]
        public async Task RapidSelectionChange_DoesNotCacheThePreviousWaveformUnderTheNewPath()
        {
            var firstStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstResult = new TaskCompletionSource<WaveformRenderResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondResult = new TaskCompletionSource<WaveformRenderResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var renderer = new Mock<IWaveformRendererService>();
            renderer
                .Setup(x => x.RenderAsync(
                    "first.wav",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => firstStarted.TrySetResult())
                .Returns(firstResult.Task);
            renderer
                .Setup(x => x.RenderAsync(
                    "second.wav",
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => secondStarted.TrySetResult())
                .Returns(secondResult.Task);
            var soundEngine = new Mock<ISoundEngine>();
            soundEngine.SetupGet(x => x.PlaybackState).Returns(PlaybackState.Stopped);
            var cachedResults = new Dictionary<string, WaveformRenderResult>(
                StringComparer.OrdinalIgnoreCase);
            var cache = new Mock<IWaveformVisualisationCacheService>();
            cache
                .Setup(x => x.GetWaveformVisualisation(
                    It.IsAny<string>(),
                    It.IsAny<int>()))
                .Returns((string path, int width) =>
                    cachedResults.TryGetValue(path, out var result) &&
                    result.PixelWidth == width
                        ? result
                        : null!);
            cache
                .Setup(x => x.Store(
                    It.IsAny<string>(),
                    It.IsAny<WaveformRenderResult>()))
                .Callback((string path, WaveformRenderResult result) =>
                    cachedResults[path] = result);
            cache
                .Setup(x => x.PreloadWaveformVisualisationsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<int>(),
                    It.IsAny<IWaveformRendererService>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var viewModel = new WaveformVisualiserViewModel(
                new TestEventHub(),
                soundEngine.Object,
                renderer.Object,
                cache.Object);

            viewModel.SetSelectedPlaylist(["first.wav"]);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            viewModel.SetSelectedPlaylist(["second.wav"]);
            firstResult.SetResult(CreateRenderResult(TimeSpan.FromSeconds(1)));

            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            secondResult.SetResult(CreateRenderResult(TimeSpan.FromSeconds(2)));
            await WaitForAsync(
                () => viewModel.TotalPlaybackTime == TimeSpan.FromSeconds(2));

            Assert.AreEqual(
                TimeSpan.FromSeconds(2),
                viewModel.TotalPlaybackTime);
        }

        [TestMethod]
        public void ReplacingThePlaylist_DoesNotAutoAdvanceThePreviousPlaylist()
        {
            var playbackState = PlaybackState.Stopped;
            var soundEngine = new Mock<ISoundEngine>();
            soundEngine
                .SetupGet(x => x.PlaybackState)
                .Returns(() => playbackState);
            soundEngine
                .Setup(x => x.Stop())
                .Callback(() =>
                {
                    if (playbackState == PlaybackState.Stopped)
                        return;

                    playbackState = PlaybackState.Stopped;
                    soundEngine.Raise(
                        x => x.PlaybackStopped += null,
                        new StoppedEventArgs());
                });
            var viewModel = new WaveformVisualiserViewModel(
                new TestEventHub(),
                soundEngine.Object,
                Mock.Of<IWaveformRendererService>(),
                new WaveformVisualisationCacheService());
            viewModel.SetSelectedPlaylist(["old-one.wav", "old-two.wav"]);
            playbackState = PlaybackState.Playing;

            viewModel.SetSelectedPlaylist(["new-one.wav", "new-two.wav"]);

            soundEngine.Verify(x => x.PlayPause(), Times.Never);
        }

        [TestMethod]
        public void SeekToRatio_LoadsTheSelectedAudioAndUpdatesPlaybackTime()
        {
            var soundEngine = new Mock<ISoundEngine>();
            soundEngine
                .SetupGet(x => x.PlaybackState)
                .Returns(PlaybackState.Stopped);
            var cache = new Mock<IWaveformVisualisationCacheService>();
            cache
                .Setup(x => x.GetWaveformVisualisation(
                    "audio.wav",
                    It.IsAny<int>()))
                .Returns(CreateRenderResult(TimeSpan.FromSeconds(10)));
            var viewModel = new WaveformVisualiserViewModel(
                new TestEventHub(),
                soundEngine.Object,
                Mock.Of<IWaveformRendererService>(),
                cache.Object);
            viewModel.SetSelectedPlaylist(["audio.wav"]);
            viewModel.TotalPlaybackTime = TimeSpan.FromSeconds(10);

            viewModel.SeekToRatio(0.25);

            soundEngine.Verify(
                x => x.LoadFromFilePath("audio.wav"),
                Times.Once);
            soundEngine.Verify(
                x => x.SetPlaybackTime(TimeSpan.FromSeconds(2.5)),
                Times.Once);
            Assert.AreEqual(
                TimeSpan.FromSeconds(2.5),
                viewModel.CurrentPlaybackTime);
        }

        [TestMethod]
        public async Task RemovingDuringPreload_DoesNotRestoreTheStaleWaveform()
        {
            var firstStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstResult = new TaskCompletionSource<WaveformRenderResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondResult = new TaskCompletionSource<WaveformRenderResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var callCount = 0;
            var renderer = new Mock<IWaveformRendererService>();
            renderer
                .Setup(x => x.RenderAsync(
                    "audio.wav",
                    800,
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref callCount) == 1)
                    {
                        firstStarted.TrySetResult();
                        return firstResult.Task;
                    }

                    secondStarted.TrySetResult();
                    return secondResult.Task;
                });
            var cache = new WaveformVisualisationCacheService();

            var firstPreload = cache.PreloadWaveformVisualisationsAsync(
                ["audio.wav"],
                800,
                renderer.Object,
                CancellationToken.None);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cache.Remove("audio.wav");
            var secondPreload = cache.PreloadWaveformVisualisationsAsync(
                ["audio.wav"],
                800,
                renderer.Object,
                CancellationToken.None);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            firstResult.SetResult(CreateRenderResult(TimeSpan.FromSeconds(1)));
            await firstPreload;

            Assert.IsNull(cache.GetWaveformVisualisation("audio.wav", 800));

            secondResult.SetResult(CreateRenderResult(TimeSpan.FromSeconds(2)));
            await secondPreload;
            Assert.AreEqual(
                TimeSpan.FromSeconds(2),
                cache.GetWaveformVisualisation("audio.wav", 800).TotalTime);
        }

        [TestMethod]
        public async Task RefreshingAudioFilesTree_ReappliesFilterAndExpansionHandlers()
        {
            var pack = new PackFileContainer("test");
            var initialTree = CreateAudioFilesTree("initial.wav");
            var refreshedTree = CreateAudioFilesTree("refreshed.wav");
            var treeBuilder = new Mock<IAudioFilesTreeBuilderService>();
            treeBuilder
                .SetupSequence(x => x.BuildTree(pack))
                .Returns(initialTree)
                .Returns(refreshedTree);
            var filter = new Mock<IAudioFilesTreeSearchFilterService>();
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(x => x.GetEditablePack()).Returns(pack);
            var eventHub = new TestEventHub();
            var cachedPaths = new List<string>();
            var cache = new Mock<IWaveformVisualisationCacheService>();
            eventHub.Register<CacheWaveformRequestedEvent>(
                this,
                e => cachedPaths.AddRange(e.FilePaths));
            var viewModel = new AudioFilesExplorerViewModel(
                eventHub,
                eventHub,
                Mock.Of<IUiCommandFactory>(),
                packFileService.Object,
                new AudioEditorStateService(),
                treeBuilder.Object,
                filter.Object,
                cache.Object)
            {
                FilterQuery = "refreshed"
            };
            await Task.Delay(350);
            filter.Invocations.Clear();

            eventHub.Publish(new PackFileContainerFilesUpdatedEvent(
                pack,
                [PackFile.CreateFromBytes("refreshed.wav", [1])]));

            filter.Verify(
                x => x.FilterTree(refreshedTree, "refreshed"),
                Times.Once);
            cache.Verify(x => x.Clear(), Times.Once);
            refreshedTree[0].IsExpanded = true;
            CollectionAssert.Contains(cachedPaths, "folder\\refreshed.wav");
        }

        [TestMethod]
        public void NonWavPackChanges_DoNotRebuildTheAudioFilesTree()
        {
            var pack = new PackFileContainer("test");
            var treeBuilder = new Mock<IAudioFilesTreeBuilderService>();
            treeBuilder
                .Setup(x => x.BuildTree(pack))
                .Returns(CreateAudioFilesTree("audio.wav"));
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(x => x.GetEditablePack()).Returns(pack);
            var eventHub = new TestEventHub();
            var cache = new Mock<IWaveformVisualisationCacheService>();
            _ = new AudioFilesExplorerViewModel(
                eventHub,
                eventHub,
                Mock.Of<IUiCommandFactory>(),
                packFileService.Object,
                new AudioEditorStateService(),
                treeBuilder.Object,
                Mock.Of<IAudioFilesTreeSearchFilterService>(),
                cache.Object);

            eventHub.Publish(new PackFileContainerFilesAddedEvent(
                pack,
                [PackFile.CreateFromBytes("compiled.wem", [1])]));

            treeBuilder.Verify(x => x.BuildTree(pack), Times.Once);
            cache.Verify(x => x.Clear(), Times.Never);
        }

        [TestMethod]
        public void FolderProjectChangeSetWithWav_RebuildsAudioTreeOnce()
        {
            var projectRoot = Path.Combine(
                Path.GetTempPath(),
                $"ae-audio-tree-{Guid.NewGuid():N}");
            Directory.CreateDirectory(projectRoot);
            try
            {
                using var pack = FolderProjectContainer.Create(
                    projectRoot,
                    new FolderProjectSettings { Name = "工程" });
                var treeBuilder = new Mock<IAudioFilesTreeBuilderService>();
                treeBuilder
                    .Setup(x => x.BuildTree(pack))
                    .Returns(CreateAudioFilesTree("audio.wav"));
                var packFileService = new Mock<IPackFileService>();
                packFileService.Setup(x => x.GetEditablePack()).Returns(pack);
                var eventHub = new TestEventHub();
                _ = new AudioFilesExplorerViewModel(
                    eventHub,
                    eventHub,
                    Mock.Of<IUiCommandFactory>(),
                    packFileService.Object,
                    new AudioEditorStateService(),
                    treeBuilder.Object,
                    Mock.Of<IAudioFilesTreeSearchFilterService>(),
                    Mock.Of<IWaveformVisualisationCacheService>());

                eventHub.Publish(new FolderProjectChangedEvent(
                    pack,
                    new FolderProjectChangeSet(
                        1,
                        [
                            new FolderProjectFileChange(
                                @"audio\changed.wav",
                                FolderProjectFileChangeKind.Added,
                                PackFile.CreateFromBytes("changed.wav", [1])),
                        ])));

                treeBuilder.Verify(x => x.BuildTree(pack), Times.Exactly(2));
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [TestMethod]
        public async Task InvalidatingTheCache_RendersTheCurrentWaveformAgain()
        {
            var eventHub = new TestEventHub();
            var soundEngine = new Mock<ISoundEngine>();
            soundEngine.SetupGet(x => x.PlaybackState).Returns(PlaybackState.Stopped);
            var renderer = new Mock<IWaveformRendererService>();
            renderer
                .SetupSequence(x => x.RenderAsync(
                    "audio.wav",
                    800,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateRenderResult(TimeSpan.FromSeconds(1)))
                .ReturnsAsync(CreateRenderResult(TimeSpan.FromSeconds(2)));
            var cache = new Mock<IWaveformVisualisationCacheService>();
            cache
                .Setup(x => x.PreloadWaveformVisualisationsAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<int>(),
                    It.IsAny<IWaveformRendererService>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var viewModel = new WaveformVisualiserViewModel(
                eventHub,
                soundEngine.Object,
                renderer.Object,
                cache.Object);
            viewModel.SetSelectedPlaylist(["audio.wav"]);
            await WaitForAsync(
                () => viewModel.TotalPlaybackTime == TimeSpan.FromSeconds(1));

            eventHub.Publish(new WaveformCacheInvalidatedEvent());

            await WaitForAsync(
                () => viewModel.TotalPlaybackTime == TimeSpan.FromSeconds(2));
            renderer.Verify(
                x => x.RenderAsync(
                    "audio.wav",
                    800,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [TestMethod]
        public async Task MissingWaveformSource_ThrowsAUsefulFileNotFoundError()
        {
            var renderer = new WaveformRendererService(
                Mock.Of<IPackFileService>());

            var exception =
                await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                    () => renderer.RenderAsync(
                        "missing.wav",
                        800,
                        CancellationToken.None));

            StringAssert.Contains(exception.Message, "missing.wav");
        }

        [TestMethod]
        public async Task BrokenWaveform_DoesNotStopOtherPreloads()
        {
            var renderer = new Mock<IWaveformRendererService>();
            renderer
                .Setup(x => x.RenderAsync(
                    "broken.wav",
                    800,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidDataException("broken data"));
            renderer
                .Setup(x => x.RenderAsync(
                    "valid.wav",
                    800,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateRenderResult(
                    TimeSpan.FromSeconds(2)));
            var cache = new WaveformVisualisationCacheService();

            await cache.PreloadWaveformVisualisationsAsync(
                ["broken.wav", "valid.wav"],
                800,
                renderer.Object,
                CancellationToken.None);

            Assert.IsNull(
                cache.GetWaveformVisualisation(
                    "broken.wav",
                    800));
            Assert.AreEqual(
                TimeSpan.FromSeconds(2),
                cache.GetWaveformVisualisation(
                    "valid.wav",
                    800).TotalTime);
        }

        [TestMethod]
        public async Task BrokenCurrentWaveform_ShowsAStatusInsteadOfFaulting()
        {
            var renderer = new Mock<IWaveformRendererService>();
            renderer
                .Setup(x => x.RenderAsync(
                    "broken.wav",
                    800,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidDataException("broken data"));
            var soundEngine = new Mock<ISoundEngine>();
            soundEngine
                .SetupGet(x => x.PlaybackState)
                .Returns(PlaybackState.Stopped);
            var viewModel = new WaveformVisualiserViewModel(
                new TestEventHub(),
                soundEngine.Object,
                renderer.Object,
                new WaveformVisualisationCacheService());

            viewModel.SetSelectedPlaylist(["broken.wav"]);

            await WaitForAsync(() =>
                viewModel.PlaybackStatus.Contains(
                    "broken data",
                    StringComparison.Ordinal));
        }

        private static ObservableCollection<AudioFilesTreeNode> CreateAudioFilesTree(
            string fileName)
        {
            var directory = AudioFilesTreeNode.CreateContainerNode(
                "folder",
                AudioFilesTreeNodeType.Directory);
            directory.FilePath = "folder";
            var file = AudioFilesTreeNode.CreateChildNode(
                fileName,
                AudioFilesTreeNodeType.WavFile,
                directory);
            file.FilePath = $"folder\\{fileName}";
            directory.Children.Add(file);
            return [directory];
        }

        private static WaveformRenderResult CreateRenderResult(TimeSpan totalTime)
        {
            var image = CreateBitmapImage();
            return new WaveformRenderResult(
                WaveformVisualisation.Create(image, image),
                totalTime,
                800);
        }

        private static BitmapImage CreateBitmapImage()
        {
            var source = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[4],
                4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private static async Task WaitForAsync(
            Func<bool> predicate,
            int timeoutMilliseconds = 2000)
        {
            var started = Environment.TickCount64;
            while (!predicate())
            {
                if (Environment.TickCount64 - started > timeoutMilliseconds)
                    Assert.Fail("Timed out waiting for the expected condition.");

                await Task.Delay(10);
            }
        }
    }
}
