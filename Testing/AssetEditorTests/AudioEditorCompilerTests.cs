using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.Dat;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.Generators;
using Editors.Audio.Shared.Wwise.Generators.Hirc.V136;
using Moq;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc.V136;
using AudioProjectAction = Editors.Audio.Shared.AudioProject.Models.Action;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioEditorCompilerTests
    {
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
                new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var commandTask = wrapper.RunExternalCommandAsync(
                    "/d /c ping 127.0.0.1 -n 10 > nul",
                    cancellationTokenSource.Token);

            Assert.IsFalse(commandTask.IsCompleted);
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

            var compileCompleted = await compiler.CompileAsync(
                project,
                "test.aproj",
                "audio\\test.aproj");

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
                cancellation.Token);

            Assert.IsFalse(compileCompleted);
            outputService.Verify(
                x => x.SaveBatch(
                    It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                    It.IsAny<bool>()),
                Times.Never);
        }
    }
}
