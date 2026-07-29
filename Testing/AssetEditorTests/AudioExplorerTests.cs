using System.Windows.Media;
using System.Windows.Media.Imaging;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.AudioExplorer;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.UI.ValueConverters;
using Editors.Audio.Shared.Utilities;
using Moq;
using NAudio.Wave;
using Shared.Core.Services;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioExplorerTests
    {
        [TestMethod]
        public async Task LoadLanguages_RefreshesTheCurrentExplorerList()
        {
            var dialogueEvent = new CAkDialogueEvent_V136
            {
                Id = 42,
                HircType = AkBkHircType.Dialogue_Event,
                BnkFilePath = "audio\\test.bnk"
            };
            var repository = CreateRepositoryMock();
            repository
                .Setup(x => x.GetHircsByHircType(AkBkHircType.Dialogue_Event))
                .Returns([dialogueEvent]);
            repository
                .Setup(x => x.GetNameFromId(dialogueEvent.Id))
                .Returns("test_event");

            var viewModel = CreateViewModel(repository.Object);

            await viewModel.LoadAudioRepositoryForSelectedLanguagesAsync();

            Assert.AreEqual(1, viewModel.ExplorerFilter.ExplorerList.Values.Count);
            Assert.AreSame(dialogueEvent, viewModel.ExplorerFilter.ExplorerList.Values[0].HircItem);
            viewModel.Close();
        }

        [TestMethod]
        public async Task LoadLanguages_RunsRepositoryWorkOffTheCallingThread()
        {
            var repository = CreateRepositoryMock();
            var loadThreadId = 0;
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => loadThreadId = Environment.CurrentManagedThreadId);
            var viewModel = CreateViewModel(repository.Object);
            loadThreadId = 0;
            var callingThreadId = Environment.CurrentManagedThreadId;

            await viewModel.LoadAudioRepositoryForSelectedLanguagesAsync();

            Assert.AreNotEqual(0, loadThreadId);
            Assert.AreNotEqual(callingThreadId, loadThreadId);
            viewModel.Close();
        }

        [TestMethod]
        public async Task InitializeAsync_LoadsOnlyOnce()
        {
            var repository = CreateRepositoryMock();
            var viewModel = CreateViewModel(repository.Object);

            await viewModel.InitializeAsync();
            await viewModel.InitializeAsync();

            repository.Verify(
                x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            viewModel.Close();
        }

        [TestMethod]
        public async Task InitializeAsync_UnsupportedGameShowsMessageAndDoesNotLoad()
        {
            var repository = CreateRepositoryMock();
            repository.SetupGet(x => x.IsCurrentGameSupported).Returns(false);
            var viewModel = CreateViewModel(repository.Object);

            await viewModel.InitializeAsync();

            Assert.AreEqual("当前游戏不支持音频浏览器。", viewModel.LoadStatus);
            repository.Verify(
                x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            viewModel.Close();
        }

        [TestMethod]
        public async Task LoadLanguages_WhenCancelledShowsStatusWithoutRefreshing()
        {
            var repository = CreateRepositoryMock();
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());
            var viewModel = CreateViewModel(repository.Object);

            await viewModel.LoadAudioRepositoryForSelectedLanguagesAsync();

            Assert.AreEqual("已取消加载。", viewModel.LoadStatus);
            Assert.AreEqual(0, viewModel.ExplorerFilter.ExplorerList.Values.Count);
            viewModel.Close();
        }

        [TestMethod]
        public void EnumConverter_UsesChineseLanguageNames()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            var converter = new EnumConverter();

            var value = converter.Convert(
                Wh3Language.EnglishUK,
                typeof(string),
                null,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual("英语（英国）", value);
        }

        [TestMethod]
        public async Task PlayAudio_UsesThePreparedMusicTrackSourceAndLanguage()
        {
            var repository = CreateRepositoryMock();
            var soundPlayer = new Mock<ISoundPlayer>();
            var source = new AudioSource(202, 100);
            var playbackData = new AudioPlaybackData(source, [1, 2, 3]);
            soundPlayer
                .Setup(x => x.Prepare(source))
                .Returns(playbackData);
            soundPlayer
                .Setup(x => x.Play(playbackData))
                .Returns(true);
            soundPlayer
                .SetupGet(x => x.PlaybackState)
                .Returns(PlaybackState.Playing);
            var waveformRenderer = CreateWaveformRendererMock(
                playbackData,
                TimeSpan.FromSeconds(3));
            var viewModel = CreateViewModel(
                repository.Object,
                soundPlayer.Object,
                waveformRenderer: waveformRenderer.Object);
            viewModel.SelectedNode = new HircTreeNode
            {
                Hirc = new CAkMusicTrack_V136
                {
                    HircType = AkBkHircType.Music_Track,
                    LanguageId = 100
                },
                SourceId = 202
            };

            await viewModel.PlayAudioAsync();

            soundPlayer.Verify(
                x => x.Prepare(source),
                Times.Once);
            soundPlayer.Verify(
                x => x.Play(playbackData),
                Times.Once);
            Assert.IsTrue(viewModel.IsAudioPlaybackVisible);
            Assert.IsTrue(viewModel.IsStopAudioButtonEnabled);
            Assert.AreEqual(3, viewModel.TotalPlaybackSeconds);

            viewModel.SelectedNode = new HircTreeNode();

            Assert.IsFalse(viewModel.IsAudioPlaybackVisible);
            Assert.IsFalse(viewModel.IsStopAudioButtonEnabled);
            viewModel.Close();
        }

        [TestMethod]
        public void SelectingPlayableAudio_ShowsPlaybackPanelUntilSelectionIsCleared()
        {
            var repository = CreateRepositoryMock();
            var viewModel = CreateViewModel(repository.Object);

            viewModel.SelectedNode = CreateMusicTrackNode(101);

            Assert.IsTrue(viewModel.IsAudioPlaybackVisible);

            viewModel.SelectedNode = null!;

            Assert.IsFalse(viewModel.IsAudioPlaybackVisible);
            Assert.IsFalse(viewModel.IsPlayAudioButtonEnabled);
            viewModel.Close();
        }

        [TestMethod]
        public async Task ChangingPlaybackPosition_SeeksThePreparedAudio()
        {
            var repository = CreateRepositoryMock();
            var soundPlayer = new Mock<ISoundPlayer>();
            var source = new AudioSource(101, 100);
            var playbackData = new AudioPlaybackData(source, [1, 2, 3]);
            soundPlayer
                .Setup(x => x.Prepare(source))
                .Returns(playbackData);
            soundPlayer
                .Setup(x => x.Seek(
                    playbackData,
                    TimeSpan.FromSeconds(1.25)))
                .Returns(true);
            var waveformRenderer = CreateWaveformRendererMock(
                playbackData,
                TimeSpan.FromSeconds(3));
            var viewModel = CreateViewModel(
                repository.Object,
                soundPlayer.Object,
                waveformRenderer: waveformRenderer.Object);

            viewModel.SelectedNode = CreateMusicTrackNode(101);
            await WaitForAsync(() => viewModel.IsPlayAudioButtonEnabled);
            viewModel.PlaybackPositionSeconds = 1.25;

            soundPlayer.Verify(
                x => x.Seek(
                    playbackData,
                    TimeSpan.FromSeconds(1.25)),
                Times.Once);
            Assert.AreEqual(
                TimeSpan.FromSeconds(1.25),
                viewModel.CurrentPlaybackTime);
            viewModel.Close();
        }

        [TestMethod]
        public async Task RapidSelectionChange_DoesNotApplyThePreviousPreview()
        {
            var repository = CreateRepositoryMock();
            var soundPlayer = new Mock<ISoundPlayer>();
            var firstSource = new AudioSource(101, 100);
            var secondSource = new AudioSource(202, 100);
            var firstPlaybackData = new AudioPlaybackData(firstSource, [1]);
            var secondPlaybackData = new AudioPlaybackData(secondSource, [2]);
            using var firstPreparationStarted = new ManualResetEventSlim();
            using var releaseFirstPreparation = new ManualResetEventSlim();
            soundPlayer
                .Setup(x => x.Prepare(firstSource))
                .Returns(() =>
                {
                    firstPreparationStarted.Set();
                    releaseFirstPreparation.Wait(TimeSpan.FromSeconds(5));
                    return firstPlaybackData;
                });
            soundPlayer
                .Setup(x => x.Prepare(secondSource))
                .Returns(secondPlaybackData);
            var waveformRenderer = new Mock<IWaveformRendererService>();
            waveformRenderer
                .Setup(x => x.RenderAsync(
                    firstPlaybackData.WavData,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateWaveformResult(TimeSpan.FromSeconds(1)));
            waveformRenderer
                .Setup(x => x.RenderAsync(
                    secondPlaybackData.WavData,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateWaveformResult(TimeSpan.FromSeconds(2)));
            var viewModel = CreateViewModel(
                repository.Object,
                soundPlayer.Object,
                waveformRenderer: waveformRenderer.Object);

            try
            {
                viewModel.SelectedNode = CreateMusicTrackNode(101);
                Assert.IsTrue(
                    firstPreparationStarted.Wait(TimeSpan.FromSeconds(5)));

                viewModel.SelectedNode = CreateMusicTrackNode(202);
                await WaitForAsync(() => viewModel.IsPlayAudioButtonEnabled);
                releaseFirstPreparation.Set();
                await Task.Delay(50);

                Assert.AreEqual(2, viewModel.TotalPlaybackSeconds);
            }
            finally
            {
                releaseFirstPreparation.Set();
                viewModel.Close();
            }
        }

        [TestMethod]
        public async Task ExportSelectedBranch_ExportsEveryDistinctPlayableDescendant()
        {
            var outputFolder = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioExplorerTests-{Guid.NewGuid():N}");
            var repository = CreateRepositoryMock();
            var soundPlayer = new Mock<ISoundPlayer>();
            soundPlayer
                .Setup(x => x.Export(
                    It.IsAny<AudioSource>(),
                    AudioExportFormat.Wav,
                    It.IsAny<string>()))
                .Returns(true);
            var dialogs = new Mock<IAudioExportDialogService>();
            dialogs
                .Setup(x => x.SelectOutputFolder())
                .Returns(outputFolder);
            var viewModel = CreateViewModel(
                repository.Object,
                soundPlayer.Object,
                dialogs.Object);
            var branch = new HircTreeNode
            {
                DisplayName = "branch",
                Children =
                [
                    CreateMusicTrackNode(101),
                    CreateMusicTrackNode(202),
                    CreateMusicTrackNode(101)
                ]
            };
            viewModel.TreeList.Add(branch);
            viewModel.SelectedNode = branch;

            await viewModel.ExportAudioAsync(
                new AudioExportCommandOptions(
                    AudioExportScope.SelectedBranch,
                    AudioExportFormat.Wav));

            soundPlayer.Verify(
                x => x.Export(
                    new AudioSource(101, 100),
                    AudioExportFormat.Wav,
                    Path.Combine(outputFolder, "101.wav")),
                Times.Once);
            soundPlayer.Verify(
                x => x.Export(
                    new AudioSource(202, 100),
                    AudioExportFormat.Wav,
                    Path.Combine(outputFolder, "202.wav")),
                Times.Once);
            Assert.AreEqual(
                "导出完成：成功 2 个，跳过 1 个，失败 0 个。",
                viewModel.LoadStatus);
            viewModel.Close();
        }

        [TestMethod]
        public async Task ExportCurrentResults_ExportsAllVisibleRoots()
        {
            var outputFolder = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioExplorerTests-{Guid.NewGuid():N}");
            var repository = CreateRepositoryMock();
            var soundPlayer = new Mock<ISoundPlayer>();
            soundPlayer
                .Setup(x => x.Export(
                    It.IsAny<AudioSource>(),
                    AudioExportFormat.Wem,
                    It.IsAny<string>()))
                .Returns(true);
            var dialogs = new Mock<IAudioExportDialogService>();
            dialogs
                .Setup(x => x.SelectOutputFolder())
                .Returns(outputFolder);
            var viewModel = CreateViewModel(
                repository.Object,
                soundPlayer.Object,
                dialogs.Object);
            viewModel.TreeList.Add(CreateMusicTrackNode(101));
            viewModel.TreeList.Add(CreateMusicTrackNode(202));

            await viewModel.ExportAudioAsync(
                new AudioExportCommandOptions(
                    AudioExportScope.CurrentResults,
                    AudioExportFormat.Wem));

            soundPlayer.Verify(
                x => x.Export(
                    It.IsAny<AudioSource>(),
                    AudioExportFormat.Wem,
                    It.IsAny<string>()),
                Times.Exactly(2));
            viewModel.Close();
        }

        [TestMethod]
        public async Task ExportSelectedBranch_DoesNotOverwriteExistingFiles()
        {
            var outputFolder = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioExplorerTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputFolder);
            File.WriteAllBytes(Path.Combine(outputFolder, "101.wem"), [1]);

            try
            {
                var repository = CreateRepositoryMock();
                var soundPlayer = new Mock<ISoundPlayer>();
                soundPlayer
                    .Setup(x => x.Export(
                        It.IsAny<AudioSource>(),
                        AudioExportFormat.Wem,
                        It.IsAny<string>()))
                    .Returns(true);
                var dialogs = new Mock<IAudioExportDialogService>();
                dialogs
                    .Setup(x => x.SelectOutputFolder())
                    .Returns(outputFolder);
                var viewModel = CreateViewModel(
                    repository.Object,
                    soundPlayer.Object,
                    dialogs.Object);
                var branch = new HircTreeNode
                {
                    Children = [CreateMusicTrackNode(101)]
                };
                viewModel.SelectedNode = branch;

                await viewModel.ExportAudioAsync(
                    new AudioExportCommandOptions(
                        AudioExportScope.SelectedBranch,
                        AudioExportFormat.Wem));

                soundPlayer.Verify(
                    x => x.Export(
                        new AudioSource(101, 100),
                        AudioExportFormat.Wem,
                        Path.Combine(outputFolder, "101_2.wem")),
                    Times.Once);
                viewModel.Close();
            }
            finally
            {
                Directory.Delete(outputFolder, true);
            }
        }

        [TestMethod]
        public async Task ExportSelectedAudio_UsesTheChosenFilePath()
        {
            var outputPath = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditorAudioExplorerTests-{Guid.NewGuid():N}.wav");
            var repository = CreateRepositoryMock();
            var soundPlayer = new Mock<ISoundPlayer>();
            soundPlayer
                .Setup(x => x.Export(
                    It.IsAny<AudioSource>(),
                    AudioExportFormat.Wav,
                    outputPath))
                .Returns(true);
            var dialogs = new Mock<IAudioExportDialogService>();
            dialogs
                .Setup(x => x.SelectOutputFile(
                    "101.wav",
                    AudioExportFormat.Wav))
                .Returns(outputPath);
            var viewModel = CreateViewModel(
                repository.Object,
                soundPlayer.Object,
                dialogs.Object);
            viewModel.SelectedNode = CreateMusicTrackNode(101);

            await viewModel.ExportAudioAsync(
                new AudioExportCommandOptions(
                    AudioExportScope.SelectedAudio,
                    AudioExportFormat.Wav));

            soundPlayer.Verify(
                x => x.Export(
                    new AudioSource(101, 100),
                    AudioExportFormat.Wav,
                    outputPath),
                Times.Once);
            viewModel.Close();
        }

        [TestMethod]
        public void ClearingSelectedNode_ResetsTheWwiseObjectLabel()
        {
            var repository = CreateRepositoryMock();
            var viewModel = CreateViewModel(repository.Object);
            viewModel.SelectedNode = new HircTreeNode
            {
                DisplayName = "test",
                Hirc = new UnknownHircItem
                {
                    Id = 1,
                    HircType = AkBkHircType.None
                }
            };

            viewModel.SelectedNode = null!;

            Assert.AreEqual("Wwise 对象数据", viewModel.WwiseObjectLabel);
            viewModel.Close();
        }

        [TestMethod]
        public void SelectingDuplicateIdFromAnotherBankBuildsTheNewHierarchy()
        {
            var first = new UnknownHircItem
            {
                Id = 7,
                HircType = AkBkHircType.None,
                BnkFilePath = "first.bnk",
                ByteIndexInFile = 10
            };
            var second = new UnknownHircItem
            {
                Id = first.Id,
                HircType = AkBkHircType.None,
                BnkFilePath = "second.bnk",
                ByteIndexInFile = 20
            };
            var repository = CreateRepositoryMock();
            repository
                .SetupGet(x => x.HircsById)
                .Returns(new Dictionary<uint, List<HircItem>>
                {
                    [first.Id] = [first, second]
                });
            var viewModel = CreateViewModel(repository.Object);
            viewModel.SearchByHircId = true;
            viewModel.SelectedNode = new HircTreeNode { Hirc = first };
            var secondItem = viewModel.ExplorerFilter.ExplorerList.Values
                .Single(item => ReferenceEquals(item.HircItem, second));

            viewModel.ExplorerFilter.ExplorerList.SelectedItem = secondItem;

            Assert.AreSame(second, viewModel.TreeList.Single().Hirc);
            viewModel.Close();
        }

        [TestMethod]
        public void SearchByVoActor_MissingStateGroupShowsAnEmptyList()
        {
            var repository = CreateRepositoryMock();
            var viewModel = CreateViewModel(repository.Object);

            viewModel.SearchByVOActor = true;

            Assert.AreEqual(0, viewModel.ExplorerFilter.ExplorerList.Values.Count);
            viewModel.Close();
        }

        [TestMethod]
        public void SelectingVoActor_DoesNotBlockWhileAllTreesAreBuilt()
        {
            using var repositoryEntered = new ManualResetEventSlim();
            using var releaseRepository = new ManualResetEventSlim();
            var repository = CreateRepositoryMock();
            repository
                .SetupGet(x => x.StatesByStateGroup)
                .Returns(new Dictionary<string, List<string>>
                {
                    ["VO_Actor"] = ["actor"]
                });
            var viewModel = CreateViewModel(repository.Object);
            viewModel.SearchByVOActor = true;
            repository
                .Setup(x => x.GetHircsByHircType(AkBkHircType.Dialogue_Event))
                .Callback(() =>
                {
                    repositoryEntered.Set();
                    releaseRepository.Wait();
                })
                .Returns([]);
            var actor = viewModel.ExplorerFilter.ExplorerList.Values.Single();

            var selection = Task.Run(
                () => viewModel.ExplorerFilter.ExplorerList.SelectedItem = actor);

            try
            {
                Assert.IsTrue(repositoryEntered.Wait(TimeSpan.FromSeconds(2)));
                Assert.IsTrue(selection.Wait(TimeSpan.FromMilliseconds(500)));
            }
            finally
            {
                releaseRepository.Set();
                selection.Wait(TimeSpan.FromSeconds(2));
                viewModel.Close();
            }
        }

        [TestMethod]
        public async Task SelectingVoActor_WithOnlyMissingReferencesShowsOneUnavailableSummary()
        {
            const uint actorId = 10;
            const uint missingHircId = 20;
            var dialogueEvent = new CAkDialogueEvent_V136
            {
                Id = 30,
                HircType = AkBkHircType.Dialogue_Event,
                Arguments =
                [
                    new AkGameSync_V136
                    {
                        GroupId = 40,
                        GroupType = AkGroupType.State
                    }
                ],
                AkDecisionTree = new AkDecisionTree_V136
                {
                    DecisionTree = new AkDecisionTree_V136.Node_V136
                    {
                        Nodes =
                        [
                            new AkDecisionTree_V136.Node_V136
                            {
                                Key = actorId,
                                AudioNodeId = missingHircId
                            }
                        ]
                    }
                }
            };
            var repository = CreateRepositoryMock();
            repository
                .SetupGet(x => x.StatesByStateGroup)
                .Returns(new Dictionary<string, List<string>>
                {
                    ["VO_Actor"] = ["actor"]
                });
            repository
                .Setup(x => x.GetHircsByHircType(AkBkHircType.Dialogue_Event))
                .Returns([dialogueEvent]);
            repository
                .Setup(x => x.GetHircs(missingHircId))
                .Returns([]);
            repository
                .Setup(x => x.GetNameFromId(It.IsAny<uint>()))
                .Returns((uint id) => id switch
                {
                    actorId => "actor",
                    40 => "VO_Actor",
                    _ => id.ToString()
                });
            var viewModel = CreateViewModel(repository.Object);
            viewModel.SearchByVOActor = true;

            viewModel.ExplorerFilter.ExplorerList.SelectedItem =
                viewModel.ExplorerFilter.ExplorerList.Values.Single();
            await WaitForAsync(
                () => viewModel.LoadStatus.Contains(
                    "没有可播放音频",
                    StringComparison.Ordinal));

            Assert.AreEqual(0, viewModel.TreeList.Count);
            Assert.AreEqual(
                "该语音角色在当前游戏文件中没有可播放音频，已识别 1 条失效引用。",
                viewModel.LoadStatus);
            viewModel.Close();
        }

        private static Mock<IAudioRepository> CreateRepositoryMock()
        {
            var repository = new Mock<IAudioRepository>();
            repository.SetupGet(x => x.IsCurrentGameSupported).Returns(true);
            repository.SetupGet(x => x.HircsById).Returns([]);
            repository.SetupGet(x => x.DidxAudioListById).Returns([]);
            repository.SetupGet(x => x.PackFileByBnkName).Returns([]);
            repository.SetupGet(x => x.NameById).Returns([]);
            repository.SetupGet(x => x.StateGroupsByDialogueEvent).Returns([]);
            repository.SetupGet(x => x.QualifiedStateGroupByStateGroupByDialogueEvent).Returns([]);
            repository.SetupGet(x => x.StatesByStateGroup).Returns([]);
            repository
                .Setup(x => x.GetHircsByHircType(It.IsAny<AkBkHircType>()))
                .Returns([]);
            return repository;
        }

        private static AudioExplorerViewModel CreateViewModel(
            IAudioRepository repository,
            ISoundPlayer? soundPlayer = null,
            IAudioExportDialogService? exportDialogs = null,
            IWaveformRendererService? waveformRenderer = null)
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            soundPlayer ??= new Mock<ISoundPlayer>().Object;
            exportDialogs ??= new Mock<IAudioExportDialogService>().Object;
            waveformRenderer ??= new Mock<IWaveformRendererService>().Object;
            return new AudioExplorerViewModel(
                repository,
                soundPlayer,
                exportDialogs,
                waveformRenderer);
        }

        private static Mock<IWaveformRendererService> CreateWaveformRendererMock(
            AudioPlaybackData playbackData,
            TimeSpan totalTime)
        {
            var renderer = new Mock<IWaveformRendererService>();
            renderer
                .Setup(x => x.RenderAsync(
                    playbackData.WavData,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateWaveformResult(totalTime));
            return renderer;
        }

        private static WaveformRenderResult CreateWaveformResult(
            TimeSpan totalTime)
        {
            var baseImage = CreateBitmapImage();
            var overlayImage = CreateBitmapImage();
            return new WaveformRenderResult(
                WaveformVisualisation.Create(baseImage, overlayImage),
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

        private static HircTreeNode CreateMusicTrackNode(uint sourceId)
        {
            return new HircTreeNode
            {
                DisplayName = $"{sourceId}.wem",
                Hirc = new CAkMusicTrack_V136
                {
                    HircType = AkBkHircType.Music_Track,
                    LanguageId = 100
                },
                SourceId = sourceId
            };
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow >= timeout)
                    Assert.Fail("Timed out waiting for the Audio Explorer operation.");

                await Task.Delay(20);
            }
        }
    }
}
