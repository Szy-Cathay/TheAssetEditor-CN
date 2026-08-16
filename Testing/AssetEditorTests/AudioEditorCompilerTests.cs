using System.Collections.ObjectModel;
using System.Windows.Data;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.Dat;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.Generators;
using Editors.Audio.Shared.Wwise.Generators.Hirc.V136;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using AudioProjectAction = Editors.Audio.Shared.AudioProject.Models.Action;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioEditorCompilerTests
    {
        [TestMethod]
        public async Task Compile_SavesOutputsOnCallingDispatcherThread()
        {
            Task<bool>? compilationTask = null;
            var sourceCollection = new ObservableCollection<int>();
            ListCollectionView? collectionView = null;
            var callingThreadId = 0;
            var savingThreadId = 0;

            WpfTestApplicationHost.Invoke(_ =>
            {
                callingThreadId = Environment.CurrentManagedThreadId;
                collectionView = new ListCollectionView(sourceCollection);
                var outputService = new Mock<IAudioPackOutputService>();
                outputService
                    .Setup(x => x.SaveBatch(
                        It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                        It.IsAny<bool>()))
                    .Callback((
                        IReadOnlyCollection<AudioPackOutput> _,
                        bool _) =>
                    {
                        savingThreadId = Environment.CurrentManagedThreadId;
                        sourceCollection.Add(1);
                    })
                    .Returns(true);
                var compiler = new AudioProjectCompilerService(
                    Mock.Of<ISoundBankGeneratorService>(),
                    Mock.Of<IWemGeneratorService>(),
                    Mock.Of<IDatGeneratorService>(),
                    outputService.Object);
                var project = new AudioProjectFile
                {
                    SoundBanks =
                    [
                        new SoundBank(
                            "thread_affinity_test",
                            Wh3SoundBank.BattleIndividualMagic,
                            "sfx")
                    ]
                };

                compilationTask = compiler.CompileAsync(
                    project,
                    "thread_affinity_test.aproj",
                    "audio\\thread_affinity_test.aproj",
                    AudioProjectCompileTarget.AllLanguages);
            });

            var completed = await compilationTask!;

            Assert.IsTrue(completed);
            Assert.AreEqual(callingThreadId, savingThreadId);
            Assert.AreEqual(1, sourceCollection.Count);
            GC.KeepAlive(collectionView);
        }

        [TestMethod]
        public void GenerateSoundBank_BattleConversational_IncludesSpatialActorMixer()
        {
            AssertCompiledBattleVoiceActorMixer(
                Wh3SoundBank.BattleVOConversational,
                Wh3ActorMixerInformation.BattleVOConversational,
                0x03,
                0x0A);
        }

        [TestMethod]
        public void GenerateSoundBank_BattleOrders_IncludesSpatialActorMixer()
        {
            AssertCompiledBattleVoiceActorMixer(
                Wh3SoundBank.BattleVO,
                Wh3ActorMixerInformation.BattleVOOrders,
                0x07,
                0x1A);
        }

        private static void AssertCompiledBattleVoiceActorMixer(
            Wh3SoundBank gameSoundBank,
            uint actorMixerId,
            byte bitsPositioning,
            byte bits3D)
        {
            const uint targetId = 42;
            const uint sourceId = 771;
            const uint containerId = 43;
            const uint childSoundId = 44;
            var sound = Sound.CreateTargetSound(
                Guid.NewGuid(),
                targetId,
                0,
                actorMixerId,
                sourceId,
                "english(uk)",
                Editors.Audio.Shared.AudioProject.Models.HircSettings
                    .CreateDefaultSoundSettings());
            var childSound = Sound.CreateContainerSound(
                Guid.NewGuid(),
                childSoundId,
                containerId,
                0,
                sourceId + 1,
                "english(uk)");
            var container = new RandomSequenceContainer(
                Guid.NewGuid(),
                containerId,
                0,
                actorMixerId,
                new Editors.Audio.Shared.AudioProject.Models.HircSettings(),
                [childSoundId]);
            var soundBank = new SoundBank(
                "battle_voice_spatial_test",
                gameSoundBank,
                "english(uk)")
            {
                FileName = "battle_voice_spatial_test.bnk",
                FilePath = "audio\\wwise\\english(uk)\\battle_voice_spatial_test.bnk"
            };
            soundBank.Sounds.Add(sound);
            soundBank.Sounds.Add(childSound);
            soundBank.RandomSequenceContainers.Add(container);
            var dialogueEvent = new DialogueEvent("battle_vo_conversation_diplomacy_generic");
            dialogueEvent.StatePaths.Add(
                new StatePath([], targetId, AkBkHircType.Sound));
            dialogueEvent.StatePaths.Add(
                new StatePath([], containerId, AkBkHircType.RandomSequenceContainer));
            soundBank.DialogueEvents.Add(dialogueEvent);

            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.GetHircs(actorMixerId))
                .Returns([
                    CreateSpatialActorMixerTemplate(
                        actorMixerId,
                        bitsPositioning,
                        bits3D)
                ]);
            var generator = new SoundBankGeneratorService(
                Mock.Of<IAudioPackOutputService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                repository.Object,
                Mock.Of<IAudioEditorIntegrityService>());

            var output = generator.GenerateSoundBankWithoutDialogueEvents(soundBank);
            var parsed = BnkParser.Parse(
                new PackFile(output.FileName, new MemorySource(output.Data)),
                output.FilePath,
                true);

            var actorMixer = parsed.HircChunk.HircItems
                .OfType<CAkActorMixer_V136>()
                .Single(item => item.Id == actorMixerId);
            Assert.AreEqual(
                bitsPositioning,
                actorMixer.NodeBaseParams.PositioningParams.BitsPositioning);
            Assert.AreEqual(
                bits3D,
                actorMixer.NodeBaseParams.PositioningParams.Bits3D);
            CollectionAssert.AreEqual(
                new uint[] { targetId, containerId },
                actorMixer.Children.ChildIds);
            Assert.AreEqual(
                1,
                actorMixer.NodeBaseParams.InitialRtpc.RtpcList.Count);
            Assert.AreEqual(
                3482134062u,
                actorMixer.NodeBaseParams.AuxParams.AuxBus2);
            var graphPoint = actorMixer.NodeBaseParams.InitialRtpc.RtpcList
                .Single()
                .RtpcMgr
                .Single();
            Assert.AreEqual(-1f, graphPoint.To);
            Assert.AreEqual(2u, graphPoint.Interp);
        }

        [TestMethod]
        public void RandomSequenceContainerGenerator_PreservesFractionalTransitionDuration()
        {
            var container = new RandomSequenceContainer(
                Guid.NewGuid(),
                1,
                0,
                0,
                new Editors.Audio.Shared.AudioProject.Models.HircSettings
                {
                    TransitionDuration = 0.5m
                },
                []);
            var soundBank = new SoundBank(
                "test",
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            var generator = new CAkRanSeqCntrGenerator_V136();

            var result = (CAkRanSeqCntr_V136)generator.GenerateHirc(
                container,
                soundBank);

            Assert.AreEqual(500f, result.TransitionTime);
        }

        [TestMethod]
        public void CreateWsourcesFile_RequestsLoudnessAnalysisForEverySource()
        {
            var tempDirectory = Path.Combine(
                Path.GetTempPath(),
                $"AssetEditor.CN.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var wsourcesPath = Path.Combine(
                    tempDirectory,
                    "loudness-analysis.wsources");
                var wrapper = new WSourcesWrapper(
                    new ApplicationSettingsService());

                wrapper.CreateWsourcesFile(
                    ["first.wav", "second.wav"],
                    tempDirectory,
                    wsourcesPath);

                var document = System.Xml.Linq.XDocument.Load(
                    wsourcesPath);
                var sources = document
                    .Root!
                    .Elements("Source")
                    .ToList();

                Assert.AreEqual(2, sources.Count);
                foreach (var source in sources)
                {
                    Assert.AreEqual(
                        "2",
                        source.Attribute("AnalysisTypes")?.Value);
                }
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [TestMethod]
        public async Task WwiseCommand_NonZeroExitCode_Throws()
        {
            var settings = new ApplicationSettingsService();
            settings.CurrentSettings.WwisePath =
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var wrapper = new WSourcesWrapper(settings);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => wrapper.RunExternalCommandAsync("/d /c exit 7"));

            StringAssert.Contains(exception.Message, "7");
        }

        [TestMethod]
        public async Task WwiseCommand_WhenCancelled_StopsWaiting()
        {
            var settings = new ApplicationSettingsService();
            settings.CurrentSettings.WwisePath =
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var wrapper = new WSourcesWrapper(settings);
            using var cancellationTokenSource =
                new CancellationTokenSource();

            var commandTask = wrapper.RunExternalCommandAsync(
                    "/d /c ping 127.0.0.1 -n 10 > nul",
                    cancellationTokenSource.Token);

            Assert.IsFalse(commandTask.IsCompleted);
            cancellationTokenSource.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => commandTask);
        }

        [TestMethod]
        public async Task Compile_AlwaysRegeneratesReferencedWav()
        {
            var sourceId = 987654321u;
            var audioFile = new AudioFile(
                Guid.NewGuid(),
                sourceId,
                "current.wav",
                "audio\\current.wav");
            var sound = Sound.CreateTargetSound(
                Guid.NewGuid(),
                42,
                0,
                0,
                sourceId,
                "sfx",
                Editors.Audio.Shared.AudioProject.Models.HircSettings.CreateDefaultSoundSettings());
            var soundBank = new SoundBank(
                "battle_individual_magic_test",
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            soundBank.Sounds.Add(sound);
            soundBank.ActionEvents.Add(new ActionEvent(
                44,
                "test_event",
                [AudioProjectAction.CreatePlay(43, AkBkHircType.Sound, sound.Id, soundBank.Id)],
                Wh3ActionEventType.BattleAbilities));
            var project = new AudioProjectFile
            {
                Language = "sfx",
                AudioFiles = [audioFile],
                SoundBanks = [soundBank]
            };
            var soundBankGenerator = new Mock<ISoundBankGeneratorService>();
            var wemGenerator = new Mock<IWemGeneratorService>();
            wemGenerator
                .Setup(x => x.GenerateWemsAsync(
                    It.IsAny<List<AudioFile>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            wemGenerator
                .Setup(x => x.CreateWemOutputs(It.IsAny<List<AudioFile>>()))
                .Returns((List<AudioFile> audioFiles) =>
                {
                    var outputs = new List<AudioPackOutput>();
                    foreach (var item in audioFiles)
                    {
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(item.WemDiskFilePath)!);
                        File.WriteAllBytes(item.WemDiskFilePath, [1]);
                        outputs.Add(new AudioPackOutput(
                            item.WemPackFileName,
                            item.WemPackFilePath,
                            [1]));
                    }
                    return outputs;
                });
            soundBankGenerator
                .Setup(x => x.GenerateSoundBankWithoutDialogueEvents(
                    It.IsAny<SoundBank>()))
                .Returns(new AudioPackOutput(
                    "test.bnk",
                    "audio\\wwise\\test.bnk",
                    [1]));
            var datGenerator = new Mock<IDatGeneratorService>();
            datGenerator
                .Setup(x => x.GenerateEventDatFile(
                    It.IsAny<string>(),
                    It.IsAny<List<ActionEvent>>(),
                    It.IsAny<List<StateGroup>>()))
                .Returns(new AudioPackOutput(
                    "test.dat",
                    "audio\\test.dat",
                    [1]));
            var compiler = new AudioProjectCompilerService(
                soundBankGenerator.Object,
                wemGenerator.Object,
                datGenerator.Object,
                Mock.Of<IAudioPackOutputService>());
            var progress = new RecordingProgress<AudioOperationProgress>();

            var compileCompleted = await ((IAudioProjectCompilerProgressService)
                compiler).CompileAsync(
                project,
                "test.aproj",
                "audio\\test.aproj",
                AudioProjectCompileTarget.AllLanguages,
                progress);

            Assert.IsTrue(compileCompleted);
            wemGenerator.Verify(
                x => x.GenerateWemsAsync(
                    It.Is<List<AudioFile>>(files =>
                        files.Count == 1 && files[0].Id == sourceId),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.IsFalse(Directory.Exists(
                Path.GetDirectoryName(audioFile.WemDiskFilePath)));
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "AudioOperation.Compile.Preparing",
                    "AudioOperation.Compile.Wems",
                    "AudioOperation.Compile.SoundBanks",
                    "AudioOperation.Saving",
                    "AudioOperation.Completed",
                },
                progress.Values
                    .Select(value => value.StageResourceKey)
                    .Distinct()
                    .ToArray());
            Assert.IsTrue(progress.Values
                .Where(value => value.Total > 0)
                .All(value =>
                    value.Completed >= 0 &&
                    value.Completed <= value.Total));
            Assert.IsTrue(progress.Values.Any(value =>
                value.StageResourceKey ==
                    "AudioOperation.Compile.SoundBanks" &&
                value.Detail.EndsWith(
                    "battle_individual_magic_test.bnk",
                    StringComparison.OrdinalIgnoreCase) &&
                value.Completed == 1 &&
                value.Total == 1));
        }

        [TestMethod]
        public async Task WemGenerator_ReportsEachExportedWavWithExactProgress()
        {
            var workspacePath = Path.Combine(
                Path.GetTempPath(),
                $"ae-wem-progress-{Guid.NewGuid():N}");
            var audioFiles = new List<AudioFile>
            {
                new(
                    Guid.NewGuid(),
                    101,
                    "first.wav",
                    "audio\\first.wav"),
                new(
                    Guid.NewGuid(),
                    102,
                    "second.wav",
                    "audio\\second.wav"),
            };
            var packFiles = audioFiles.ToDictionary(
                audioFile => audioFile.WavPackFilePath,
                audioFile => PackFile.CreateFromBytes(
                    audioFile.WavPackFileName,
                    [1, 2, 3]),
                StringComparer.OrdinalIgnoreCase);
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.FindFile(
                    It.IsAny<string>(),
                    It.IsAny<PackFileContainer?>()))
                .Returns((string path, PackFileContainer? _) =>
                    packFiles.GetValueOrDefault(path));
            using var cancellationTokenSource =
                new CancellationTokenSource();
            var progress = new RecordingProgress<AudioOperationProgress>(
                value =>
                {
                    if (value.Completed == audioFiles.Count)
                        cancellationTokenSource.Cancel();
                });
            var generator = new WemGeneratorService(
                packFileService.Object,
                new WSourcesWrapper(null!));

            try
            {
                await generator.GenerateWemsAsync(
                    audioFiles,
                    workspacePath,
                    progress,
                    cancellationTokenSource.Token);
            }
            finally
            {
                if (Directory.Exists(workspacePath))
                    Directory.Delete(workspacePath, recursive: true);
            }

            var wavProgress = progress.Values.Where(value =>
                    value.StageResourceKey ==
                        "AudioOperation.Compile.Wems" &&
                    value.Total > 0)
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { "audio\\first.wav", "audio\\second.wav" },
                wavProgress.Select(value => value.Detail).ToArray());
            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                wavProgress.Select(value => value.Completed).ToArray());
            Assert.IsTrue(wavProgress.All(value => value.Total == 2));
        }

        [TestMethod]
        public async Task Compile_WhenWemGenerationIsCancelled_DoesNotSavePartialOutputs()
        {
            var sourceId = 123456789u;
            var audioFile = new AudioFile(
                Guid.NewGuid(),
                sourceId,
                "cancel.wav",
                "audio\\cancel.wav");
            var sound = Sound.CreateTargetSound(
                Guid.NewGuid(),
                42,
                0,
                0,
                sourceId,
                "sfx",
                Editors.Audio.Shared.AudioProject.Models.HircSettings
                    .CreateDefaultSoundSettings());
            var soundBank = new SoundBank(
                "battle_individual_magic_cancel",
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            soundBank.Sounds.Add(sound);
            soundBank.ActionEvents.Add(new ActionEvent(
                44,
                "cancel_event",
                [
                    AudioProjectAction.CreatePlay(
                        43,
                        AkBkHircType.Sound,
                        sound.Id,
                        soundBank.Id)
                ],
                Wh3ActionEventType.BattleAbilities));
            var project = new AudioProjectFile
            {
                Language = "sfx",
                AudioFiles = [audioFile],
                SoundBanks = [soundBank]
            };
            using var cancellation = new CancellationTokenSource();
            var wemGenerator = new Mock<IWemGeneratorService>();
            wemGenerator
                .Setup(x => x.GenerateWemsAsync(
                    It.IsAny<List<AudioFile>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    List<AudioFile> _,
                    string _,
                    CancellationToken cancellationToken) =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled(cancellationToken);
                });
            var outputService = new Mock<IAudioPackOutputService>();
            var compiler = new AudioProjectCompilerService(
                Mock.Of<ISoundBankGeneratorService>(),
                wemGenerator.Object,
                Mock.Of<IDatGeneratorService>(),
                outputService.Object);

            var compileCompleted = await compiler.CompileAsync(
                project,
                "cancel.aproj",
                "audio\\cancel.aproj",
                AudioProjectCompileTarget.AllLanguages,
                cancellation.Token);

            Assert.IsFalse(compileCompleted);
            outputService.Verify(
                x => x.SaveBatch(
                    It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                    It.IsAny<bool>()),
                Times.Never);
        }

        [TestMethod]
        public async Task Compile_ReportsEachPreparedSoundBankWithExactProgress()
        {
            var project = new AudioProjectFile
            {
                Language = "sfx",
                SoundBanks =
                [
                    new SoundBank(
                        "bank_one",
                        Wh3SoundBank.BattleIndividualMagic,
                        "sfx"),
                    new SoundBank(
                        "bank_two",
                        Wh3SoundBank.BattleIndividualMagic,
                        "sfx"),
                ],
            };
            var compiler = new AudioProjectCompilerService(
                Mock.Of<ISoundBankGeneratorService>(),
                Mock.Of<IWemGeneratorService>(),
                Mock.Of<IDatGeneratorService>(),
                Mock.Of<IAudioPackOutputService>());
            var progress = new RecordingProgress<AudioOperationProgress>();

            var completed = await ((IAudioProjectCompilerProgressService)
                compiler).CompileAsync(
                project,
                "test.aproj",
                "audio\\test.aproj",
                AudioProjectCompileTarget.AllLanguages,
                progress);

            Assert.IsTrue(completed);
            Assert.IsTrue(progress.Values.Any(value =>
                value.StageResourceKey == "AudioOperation.Compile.Preparing" &&
                value.Detail?.EndsWith("bank_one.bnk") == true &&
                value.Completed == 1 &&
                value.Total == 2));
            Assert.IsTrue(progress.Values.Any(value =>
                value.StageResourceKey == "AudioOperation.Compile.Preparing" &&
                value.Detail?.EndsWith("bank_two.bnk") == true &&
                value.Completed == 2 &&
                value.Total == 2));
        }

        [TestMethod]
        public async Task Compile_SelectedLanguage_WritesBnkAndWemUnderThatLanguageDirectory()
        {
            var outputPaths = await CompileAndCaptureOutputPaths(
                new AudioProjectCompileTarget(Wh3Language.Chinese));

            CollectionAssert.Contains(
                outputPaths,
                "audio\\wwise\\chinese\\battle_individual_magic_target.bnk");
            CollectionAssert.Contains(
                outputPaths,
                "audio\\wwise\\chinese\\771.wem");
        }

        [TestMethod]
        public async Task Compile_AllLanguages_WritesBnkAndWemDirectlyUnderWwise()
        {
            var outputPaths = await CompileAndCaptureOutputPaths(
                AudioProjectCompileTarget.AllLanguages);

            CollectionAssert.Contains(
                outputPaths,
                "audio\\wwise\\battle_individual_magic_target.bnk");
            CollectionAssert.Contains(
                outputPaths,
                "audio\\wwise\\771.wem");
        }

        [TestMethod]
        public async Task Compile_FrontendVo_WritesBnkToEveryVoiceLanguageRegardlessOfTarget()
        {
            var allLanguagesOutputPaths = await CompileAndCaptureOutputPaths(
                AudioProjectCompileTarget.AllLanguages,
                Wh3SoundBank.FrontendVO);
            var selectedLanguageOutputPaths = await CompileAndCaptureOutputPaths(
                new AudioProjectCompileTarget(Wh3Language.Chinese),
                Wh3SoundBank.FrontendVO);
            string[] voiceLanguages =
            [
                "chinese",
                "english(uk)",
                "french(france)",
                "german",
                "italian",
                "polish",
                "russian",
                "spanish(spain)"
            ];
            string[] soundBankFileNames =
            [
                "frontend_vo_target.bnk",
                "frontend_vo_1_target_for_testing.bnk",
                "frontend_vo_target_for_merging.bnk"
            ];

            foreach (var outputPaths in new[]
                     {
                         allLanguagesOutputPaths,
                         selectedLanguageOutputPaths
                     })
            {
                var soundBankPaths = outputPaths
                    .Where(path => path.EndsWith(
                        ".bnk",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
                Assert.AreEqual(24, soundBankPaths.Count);

                foreach (var language in voiceLanguages)
                {
                    foreach (var soundBankFileName in soundBankFileNames)
                    {
                        CollectionAssert.Contains(
                            soundBankPaths,
                            $"audio\\wwise\\{language}\\{soundBankFileName}");
                    }
                }

                foreach (var soundBankFileName in soundBankFileNames)
                {
                    CollectionAssert.DoesNotContain(
                        soundBankPaths,
                        $"audio\\wwise\\{soundBankFileName}");
                }
            }

            CollectionAssert.Contains(
                allLanguagesOutputPaths,
                "audio\\wwise\\771.wem");
            CollectionAssert.Contains(
                selectedLanguageOutputPaths,
                "audio\\wwise\\chinese\\771.wem");
        }

        private static async Task<List<string>> CompileAndCaptureOutputPaths(
            AudioProjectCompileTarget compileTarget,
            Wh3SoundBank gameSoundBank = Wh3SoundBank.BattleIndividualMagic)
        {
            const uint sourceId = 771;
            var audioFile = new AudioFile(
                Guid.NewGuid(),
                sourceId,
                "target.wav",
                "audio\\target.wav");
            var sound = Sound.CreateTargetSound(
                Guid.NewGuid(),
                42,
                0,
                0,
                sourceId,
                "english(uk)",
                Editors.Audio.Shared.AudioProject.Models.HircSettings
                    .CreateDefaultSoundSettings());
            var soundBankName =
                $"{Wh3SoundBankInformation.GetName(gameSoundBank)}_target";
            var soundBank = new SoundBank(
                soundBankName,
                gameSoundBank,
                "english(uk)");
            soundBank.Sounds.Add(sound);
            if (gameSoundBank == Wh3SoundBank.FrontendVO)
            {
                var dialogueEvent = new DialogueEvent(
                    "frontend_vo_character_select");
                dialogueEvent.StatePaths.Add(
                    new StatePath([], sound.Id, AkBkHircType.Sound));
                soundBank.DialogueEvents.Add(dialogueEvent);
            }
            else
            {
                soundBank.ActionEvents.Add(new ActionEvent(
                    44,
                    "target_event",
                    [AudioProjectAction.CreatePlay(
                        43,
                        AkBkHircType.Sound,
                        sound.Id,
                        soundBank.Id)],
                    Wh3ActionEventType.BattleAbilities));
            }
            var project = new AudioProjectFile
            {
                Language = "english(uk)",
                AudioFiles = [audioFile],
                SoundBanks = [soundBank]
            };
            var wemGenerator = new Mock<IWemGeneratorService>();
            wemGenerator
                .Setup(x => x.GenerateWemsAsync(
                    It.IsAny<List<AudioFile>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    List<AudioFile> audioFiles,
                    string _,
                    CancellationToken _) =>
                {
                    foreach (var item in audioFiles)
                    {
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(item.WemDiskFilePath)!);
                        File.WriteAllBytes(item.WemDiskFilePath, [1]);
                    }

                    return Task.CompletedTask;
                });
            wemGenerator
                .Setup(x => x.CreateWemOutputs(It.IsAny<List<AudioFile>>()))
                .Returns((List<AudioFile> audioFiles) => audioFiles
                    .Select(item => new AudioPackOutput(
                        item.WemPackFileName,
                        item.WemPackFilePath,
                        [1]))
                    .ToList());
            var soundBankGenerator = new Mock<ISoundBankGeneratorService>();
            soundBankGenerator
                .Setup(x => x.GenerateSoundBankWithoutDialogueEvents(
                    It.IsAny<SoundBank>()))
                .Returns((SoundBank bank) => new AudioPackOutput(
                    bank.FileName,
                    bank.FilePath,
                    [1]));
            soundBankGenerator
                .Setup(x => x.GenerateDialogueEventsForTestingSoundBank(
                    It.IsAny<SoundBank>()))
                .Returns((SoundBank bank) => new AudioPackOutput(
                    bank.TestingFileName,
                    bank.TestingFilePath,
                    [1]));
            soundBankGenerator
                .Setup(x => x.GenerateMergingSoundBank(
                    It.IsAny<SoundBank>()))
                .Returns((SoundBank bank) => new AudioPackOutput(
                    bank.MergingFileName,
                    bank.MergingFilePath,
                    [1]));
            var savedOutputs = new List<AudioPackOutput>();
            var outputService = new Mock<IAudioPackOutputService>();
            outputService
                .Setup(x => x.SaveBatch(
                    It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                    It.IsAny<bool>()))
                .Callback((
                    IReadOnlyCollection<AudioPackOutput> outputs,
                    bool _) => savedOutputs.AddRange(outputs))
                .Returns(true);
            var datGenerator = new Mock<IDatGeneratorService>();
            datGenerator
                .Setup(x => x.GenerateEventDatFile(
                    It.IsAny<string>(),
                    It.IsAny<List<ActionEvent>>(),
                    It.IsAny<List<StateGroup>>()))
                .Returns(new AudioPackOutput(
                    "event_data__target.dat",
                    "audio\\wwise\\event_data__target.dat",
                    [1]));
            var compiler = new AudioProjectCompilerService(
                soundBankGenerator.Object,
                wemGenerator.Object,
                datGenerator.Object,
                outputService.Object);

            var compileCompleted = await compiler.CompileAsync(
                project,
                "target.aproj",
                "audio\\target.aproj",
                compileTarget);

            Assert.IsTrue(compileCompleted);
            return savedOutputs.Select(output => output.FilePath).ToList();
        }

        private sealed class RecordingProgress<T>(
            Action<T>? onReport = null) : IProgress<T>
        {
            public List<T> Values { get; } = [];

            public void Report(T value)
            {
                Values.Add(value);
                onReport?.Invoke(value);
            }
        }

        private static CAkActorMixer_V136 CreateSpatialActorMixerTemplate(
            uint actorMixerId,
            byte bitsPositioning,
            byte bits3D)
        {
            return new CAkActorMixer_V136
            {
                HircType = AkBkHircType.ActorMixer,
                Id = actorMixerId,
                IsCAHircItem = true,
                BnkFilePath = "audio\\wwise\\english(uk)\\battle_vo_conversational_template.bnk",
                NodeBaseParams = new NodeBaseParams_V136
                {
                    NodeInitialFxParams = new NodeInitialFxParams_V136
                    {
                        IsOverrideParentFx = 1,
                        NumFx = 0
                    },
                    OverrideBusId = 160824738,
                    BitVector = 3,
                    NodeInitialParams = new NodeInitialParams_V136(),
                    PositioningParams = new PositioningParams_V136
                    {
                        BitsPositioning = bitsPositioning,
                        Bits3D = bits3D
                    },
                    AuxParams = new AuxParams_V136
                    {
                        BitVector = 0x0B,
                        AuxBus2 = 3482134062,
                        AuxBus3 = 3482134061
                    },
                    AdvSettingsParams = new AdvSettingsParams_V136
                    {
                        BitVector = 0x0C,
                        VirtualQueueBehavior = 1,
                        BelowThresholdBehavior = 2,
                        BitVector2 = 4
                    },
                    StateChunk = new StateChunk_V136(),
                    InitialRtpc = new InitialRtpc_V136
                    {
                        RtpcList =
                        [
                            new InitialRtpc_V136.Rtpc_V136
                            {
                                RtpcId = 205544051,
                                RtpcType = 0,
                                RtpcAccum = 2,
                                ParamId = 0,
                                RtpcCurveId = 828889223,
                                Scaling = 2,
                                RtpcMgr =
                                [
                                    new AkRtpcGraphPoint_V136
                                    {
                                        From = 0,
                                        To = -1,
                                        Interp = 2
                                    }
                                ]
                            }
                        ]
                    }
                },
                Children = new Children_V136
                {
                    ChildIds = [999]
                }
            };
        }
    }
}
