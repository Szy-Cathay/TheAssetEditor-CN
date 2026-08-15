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

        private static async Task<List<string>> CompileAndCaptureOutputPaths(
            AudioProjectCompileTarget compileTarget)
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
            var soundBank = new SoundBank(
                "battle_individual_magic_target",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            soundBank.Sounds.Add(sound);
            soundBank.ActionEvents.Add(new ActionEvent(
                44,
                "target_event",
                [AudioProjectAction.CreatePlay(
                    43,
                    AkBkHircType.Sound,
                    sound.Id,
                    soundBank.Id)],
                Wh3ActionEventType.BattleAbilities));
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

        private sealed class RecordingProgress<T> : IProgress<T>
        {
            public List<T> Values { get; } = [];

            public void Report(T value) => Values.Add(value);
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
