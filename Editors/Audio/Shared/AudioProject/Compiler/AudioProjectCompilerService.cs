using System.Collections.Generic;
using System.Data;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.Dat;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.Generators;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;

namespace Editors.Audio.Shared.AudioProject.Compiler
{
    public readonly record struct AudioProjectCompileTarget
    {
        public static AudioProjectCompileTarget AllLanguages { get; } =
            new(null);

        public AudioProjectCompileTarget(Wh3Language? language)
        {
            if (language == Wh3Language.Sfx)
            {
                throw new ArgumentException(
                    "Use AllLanguages for output directly under audio\\wwise.",
                    nameof(language));
            }

            Language = language;
        }

        public Wh3Language? Language { get; }

        public string OutputDirectory => Language.HasValue
            ? $"audio\\wwise\\{Wh3LanguageInformation.GetLanguageAsString(Language.Value)}"
            : "audio\\wwise";
    }

    public interface IAudioProjectCompilerService
    {
        Task<bool> CompileAsync(
            AudioProjectFile audioProject,
            string audioProjectFileName,
            string audioProjectFilePath,
            AudioProjectCompileTarget compileTarget,
            CancellationToken cancellationToken = default);
    }

    public interface IAudioProjectCompilerProgressService
    {
        Task<bool> CompileAsync(
            AudioProjectFile audioProject,
            string audioProjectFileName,
            string audioProjectFilePath,
            AudioProjectCompileTarget compileTarget,
            IProgress<AudioOperationProgress> progress,
            CancellationToken cancellationToken = default);
    }

    public class AudioProjectCompilerService(
        ISoundBankGeneratorService soundBankGeneratorService,
        IWemGeneratorService wemGeneratorService,
        IDatGeneratorService datGeneratorService,
        IAudioPackOutputService audioPackOutputService) :
        IAudioProjectCompilerService,
        IAudioProjectCompilerProgressService
    {
        private readonly ISoundBankGeneratorService _soundBankGeneratorService = soundBankGeneratorService;
        private readonly IWemGeneratorService _wemGeneratorService = wemGeneratorService;
        private readonly IDatGeneratorService _datGeneratorService = datGeneratorService;
        private readonly IAudioPackOutputService _audioPackOutputService =
            audioPackOutputService;

        private readonly ILogger _logger = Logging.Create<AudioProjectCompilerService>();
        private static readonly SemaphoreSlim CompilationGate = new(1, 1);

        public Task<bool> CompileAsync(
            AudioProjectFile audioProject,
            string audioProjectFileName,
            string audioProjectFilePath,
            AudioProjectCompileTarget compileTarget,
            CancellationToken cancellationToken = default) =>
            CompileAsync(
                audioProject,
                audioProjectFileName,
                audioProjectFilePath,
                compileTarget,
                null,
                cancellationToken);

        public async Task<bool> CompileAsync(
            AudioProjectFile audioProject,
            string audioProjectFileName,
            string audioProjectFilePath,
            AudioProjectCompileTarget compileTarget,
            IProgress<AudioOperationProgress> progress,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (audioProject.SoundBanks.Count == 0)
                return true;

            try
            {
                await CompilationGate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            var workspacePath = Path.Combine(
                DirectoryHelper.Temp,
                "Audio",
                Guid.NewGuid().ToString("N"));
            try
            {
                _logger.Here().Information($"Compiling {audioProjectFileName}");
                DirectoryHelper.EnsureCreated(workspacePath);

                var audioProjectNameWithoutExtension =
                    Path.GetFileNameWithoutExtension(audioProjectFileName);
                var compilationResult = await Task.Run(async () =>
                {
                    var audioFiles = new List<AudioFile>();
                    var sounds = new List<Sound>();
                    var generatedOutputs =
                        new List<AudioPackOutput>();

                    try
                    {
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.Preparing",
                            audioProjectFileName,
                            0,
                            audioProject.SoundBanks.Count));
                        SetSoundBankData(
                            audioProject,
                            audioProjectNameWithoutExtension,
                            workspacePath,
                            compileTarget.OutputDirectory,
                            audioFiles,
                            sounds,
                            cancellationToken);
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.Preparing",
                            audioProjectFileName,
                            audioProject.SoundBanks.Count,
                            audioProject.SoundBanks.Count));
                        var wemCount = audioFiles
                            .DistinctBy(audioFile => audioFile.Id)
                            .Count();
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.Wems",
                            $"{wemCount} WEM",
                            0,
                            wemCount));
                        generatedOutputs.AddRange(
                            await GenerateWemsAsync(
                                audioProject,
                                audioFiles,
                                sounds,
                                workspacePath,
                                cancellationToken));
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.Wems",
                            $"{wemCount} WEM",
                            wemCount,
                            wemCount));
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return (
                                Outputs: generatedOutputs,
                                WasCancelled: true);
                        }

                        var soundBankCount = audioProject.SoundBanks.Count(
                            soundBank =>
                                soundBank.ActionEvents.Count != 0 ||
                                soundBank.DialogueEvents.Count != 0);
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.SoundBanks",
                            audioProjectFileName,
                            0,
                            soundBankCount));
                        generatedOutputs.AddRange(GenerateSoundBanks(
                            audioProject,
                            cancellationToken));
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.SoundBanks",
                            audioProjectFileName,
                            soundBankCount,
                            soundBankCount));
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.EventData",
                            audioProjectFileName,
                            0,
                            1));
                        generatedOutputs.AddRange(GenerateDatFiles(
                            audioProject,
                            audioProjectNameWithoutExtension,
                            cancellationToken));
                        progress?.Report(new AudioOperationProgress(
                            "AudioOperation.Compile.EventData",
                            audioProjectFileName,
                            1,
                            1));
                        return (
                            Outputs: generatedOutputs,
                            WasCancelled: false);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        return (
                            Outputs: generatedOutputs,
                            WasCancelled: true);
                    }
                });

                if (compilationResult.WasCancelled ||
                    cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                progress?.Report(new AudioOperationProgress(
                    "AudioOperation.Saving",
                    audioProjectFilePath,
                    0,
                    compilationResult.Outputs.Count));
                await Task.Run(() =>
                    _audioPackOutputService.SaveBatch(
                        compilationResult.Outputs));
                progress?.Report(new AudioOperationProgress(
                    "AudioOperation.Saving",
                    audioProjectFilePath,
                    compilationResult.Outputs.Count,
                    compilationResult.Outputs.Count));
                progress?.Report(new AudioOperationProgress(
                    "AudioOperation.Optimizing",
                    audioProjectFileName));
                await Task.Run(MemoryOptimiser.Optimise);
                progress?.Report(new AudioOperationProgress(
                    "AudioOperation.Completed",
                    audioProjectFileName,
                    1,
                    1));
                return true;
            }
            finally
            {
                TryDeleteWorkspace(workspacePath);
                CompilationGate.Release();
            }
        }

        private void TryDeleteWorkspace(string workspacePath)
        {
            try
            {
                if (Directory.Exists(workspacePath))
                    Directory.Delete(workspacePath, true);
            }
            catch (Exception exception)
            {
                _logger.Here().Warning(
                    exception,
                    "Failed to clean the audio compilation workspace {WorkspacePath}",
                    workspacePath);
            }
        }

        private void SetSoundBankData(
            AudioProjectFile audioProject,
            string audioProjectNameWithoutExtension,
            string workspacePath,
            string outputDirectory,
            List<AudioFile> audioFiles,
            List<Sound> sounds,
            CancellationToken cancellationToken)
        {
            _logger.Here().Information($"Setting SoundBank data");

            foreach (var soundBank in audioProject.SoundBanks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                soundBank.FileName = $"{soundBank.Name}.bnk";
                soundBank.FilePath =
                    $"{outputDirectory}\\{soundBank.FileName}";

                if (soundBank.DialogueEvents.Count != 0)
                {
                    // In WH3 .bnk files are loaded in descending name order. When a .bnk is loaded it overrides hircs with the same ID in .bnks loaded
                    // before it so the .bnk with the lowest alphanumeric name takes priority.
                    // Example load order:
                    // 1) campaign_vo__core.bnk
                    // 3) campaign_vo_1_project_name_for_testing.bnk
                    // 3) campaign_vo_0_audio_mixer.bnk
                    // So the dialogue events from campaign_vo_0_audio_mixer.bnk will be what take priority as they're loaded last.

                    var soundBankNameBase = soundBank.Name.Replace($"_{audioProjectNameWithoutExtension}", string.Empty);
                    soundBank.TestingFileName = $"{soundBankNameBase}_1_{audioProjectNameWithoutExtension}_for_testing.bnk";
                    soundBank.MergingFileName = $"{soundBank.Name}_for_merging.bnk";

                    soundBank.TestingFilePath =
                        $"{outputDirectory}\\{soundBank.TestingFileName}";
                    soundBank.MergingFilePath =
                        $"{outputDirectory}\\{soundBank.MergingFileName}";

                    soundBank.TestingId = WwiseHash.Compute(soundBank.TestingFileName.Replace(".bnk", string.Empty));
                    soundBank.MergingId = WwiseHash.Compute(soundBank.MergingFileName.Replace(".bnk", string.Empty));
                }

                if (soundBank.ActionEvents.Count != 0)
                    SetActionEventData(
                        audioProject,
                        audioFiles,
                        sounds,
                        soundBank,
                        workspacePath,
                        outputDirectory,
                        cancellationToken);

                if (soundBank.DialogueEvents.Count != 0)
                    SetDialogueEventData(
                        audioProject,
                        audioFiles,
                        sounds,
                        soundBank,
                        workspacePath,
                        outputDirectory,
                        cancellationToken);
            }
        }

        private static void SetActionEventData(
            AudioProjectFile audioProject,
            List<AudioFile> audioFiles,
            List<Sound> sounds,
            SoundBank soundBank,
            string workspacePath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            var playActionEvents = soundBank.GetPlayActionEvents();
            foreach (var playActionEvent in playActionEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var playAction in playActionEvent.Actions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (playAction.TargetHircTypeIsSound())
                    {
                        var sound = soundBank.GetSound(playAction.TargetHircId);
                        var audioFile = audioProject.GetAudioFile(sound.SourceId);

                        SetSoundData(
                            audioFile,
                            workspacePath,
                            outputDirectory);

                        audioFiles.Add(audioFile);
                        sounds.Add(sound);
                    }
                    else if (playAction.TargetHircTypeIsRandomSequenceContainer())
                    {
                        var randomSequenceContainer = soundBank.GetRandomSequenceContainer(playAction.TargetHircId);

                        SetRandomSequenceContainerData(
                            audioProject,
                            randomSequenceContainer,
                            soundBank,
                            workspacePath,
                            outputDirectory,
                            cancellationToken);

                        var randomSequenceContainerSounds = soundBank.GetSounds(randomSequenceContainer.Children);
                        audioFiles.AddRange(randomSequenceContainerSounds.Select(sound => audioProject.GetAudioFile(sound.SourceId)).ToList());
                        sounds.AddRange(randomSequenceContainerSounds);
                    }
                }
            }
        }

        private static void SetDialogueEventData(
            AudioProjectFile audioProject,
            List<AudioFile> audioFiles,
            List<Sound> sounds,
            SoundBank soundBank,
            string workspacePath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            foreach (var dialogueEvent in soundBank.DialogueEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var statePath in dialogueEvent.StatePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (statePath.TargetHircTypeIsSound())
                    {
                        var sound = soundBank.GetSound(statePath.TargetHircId);
                        var audioFile = audioProject.GetAudioFile(sound.SourceId);

                        SetSoundData(
                            audioFile,
                            workspacePath,
                            outputDirectory);

                        audioFiles.Add(audioFile);
                        sounds.Add(sound);
                    }
                    else if (statePath.TargetHircTypeIsRandomSequenceContainer())
                    {
                        var randomSequenceContainer = soundBank.GetRandomSequenceContainer(statePath.TargetHircId);

                        SetRandomSequenceContainerData(
                            audioProject,
                            randomSequenceContainer,
                            soundBank,
                            workspacePath,
                            outputDirectory,
                            cancellationToken);

                        var randomSequenceContainerSounds = soundBank.GetSounds(randomSequenceContainer.Children);
                        audioFiles.AddRange(randomSequenceContainerSounds.Select(sound => audioProject.GetAudioFile(sound.SourceId)).ToList());
                        sounds.AddRange(randomSequenceContainerSounds);
                    }
                }
            }
        }

        private static void SetRandomSequenceContainerData(
            AudioProjectFile audioProject,
            RandomSequenceContainer randomSequenceContainer,
            SoundBank soundBank,
            string workspacePath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            var sounds = soundBank.GetSounds(randomSequenceContainer.Children);
            foreach (var sound in sounds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var audioFile = audioProject.GetAudioFile(sound.SourceId);
                SetSoundData(
                    audioFile,
                    workspacePath,
                    outputDirectory);
            }
            randomSequenceContainer.Children = randomSequenceContainer.Children.OrderBy(soundId => soundId).ToList();
        }

        private static void SetSoundData(
            AudioFile audioFile,
            string workspacePath,
            string outputDirectory)
        {
            audioFile.WemPackFileName = $"{audioFile.Id}.wem";
            audioFile.WemDiskFilePath = Path.Combine(
                workspacePath,
                audioFile.WemPackFileName);
            
            audioFile.WemPackFilePath =
                $"{outputDirectory}\\{audioFile.WemPackFileName}";
        }

        private async Task<List<AudioPackOutput>> GenerateWemsAsync(
            AudioProjectFile audioProject,
            List<AudioFile> audioFiles,
            List<Sound> sounds,
            string workspacePath,
            CancellationToken cancellationToken)
        {
            var uniqueAudioFiles = audioFiles
                .DistinctBy(audioFile => audioFile.Id)
                .ToList();
            var uniqueSounds = sounds
                .DistinctBy(sound => sound.Id)
                .ToList();

            if (uniqueAudioFiles.Count > 0)
            {
                _logger.Here().Information($"Generating {uniqueAudioFiles.Count} WEMs");
                await _wemGeneratorService.GenerateWemsAsync(
                    uniqueAudioFiles,
                    workspacePath,
                    cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    return [];

                var outputs =
                    _wemGeneratorService.CreateWemOutputs(uniqueAudioFiles);
                UpdateSoundInMemoryMediaSize(
                    audioProject,
                    uniqueSounds,
                    cancellationToken);
                return outputs;
            }

            return [];
        }

        private static void UpdateSoundInMemoryMediaSize(
            AudioProjectFile audioProject,
            List<Sound> sounds,
            CancellationToken cancellationToken)
        {
            foreach (var sound in sounds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var audioFile = audioProject.GetAudioFile(sound.SourceId);
                var wemFileInfo = new FileInfo(audioFile.WemDiskFilePath);
                var fileSizeInBytes = wemFileInfo.Length;
                sound.InMemoryMediaSize = fileSizeInBytes;
            }
        }

        private List<AudioPackOutput> GenerateSoundBanks(
            AudioProjectFile audioProject,
            CancellationToken cancellationToken)
        {
            var outputs = new List<AudioPackOutput>();
            foreach (var soundBank in audioProject.SoundBanks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (soundBank.ActionEvents.Count != 0 || soundBank.DialogueEvents.Count != 0)
                {
                    _logger.Here().Information($"Generating SoundBank {soundBank.FilePath}");

                    // Create the .bnk that modders should keep
                    outputs.Add(
                        _soundBankGeneratorService
                            .GenerateSoundBankWithoutDialogueEvents(soundBank));

                    if (soundBank.DialogueEvents.Count != 0)
                    {
                        // Create a .bnk of the compiled Dialogue Events merged with vanilla Dialogue Events for modders to test
                        _logger.Here().Information($"Generating SoundBank {soundBank.TestingFilePath} and {soundBank.MergingFilePath}");
                        outputs.Add(
                            _soundBankGeneratorService
                                .GenerateDialogueEventsForTestingSoundBank(soundBank));

                        // Create the .bnk that modders should give to the merger
                        outputs.Add(
                            _soundBankGeneratorService
                                .GenerateMergingSoundBank(soundBank));
                    }
                }
            }

            return outputs;
        }

        private List<AudioPackOutput> GenerateDatFiles(
            AudioProjectFile audioProject,
            string audioProjectNameWithoutExtension,
            CancellationToken cancellationToken)
        {
            var outputs = new List<AudioPackOutput>();
            cancellationToken.ThrowIfCancellationRequested();

            // In WH3 .dat files only seem necessary for Action Events for movies or anything triggered via common.trigger_soundevent()
            // but without testing all the different types of Action Event sounds it's safer to just make a .dat for all.
            // We store States in there so we can display them in the Audio Explorer.
            var actionEvents = audioProject.GetActionEvents();
            var hasActionEvents = actionEvents != null && actionEvents.Count > 0;
            var hasStateGroups = audioProject.StateGroups.Count != 0 && audioProject.StateGroups.Count > 0;

            if (hasActionEvents && hasStateGroups)
            {
                _logger.Here().Information($"Generating event data .dat");
                outputs.Add(_datGeneratorService.GenerateEventDatFile(
                    audioProjectNameWithoutExtension,
                    actionEvents,
                    audioProject.StateGroups));
            }
            else if (hasActionEvents && !hasStateGroups)
            {
                _logger.Here().Information($"Generating event data .dat");
                outputs.Add(_datGeneratorService.GenerateEventDatFile(
                    audioProjectNameWithoutExtension,
                    actionEvents: actionEvents));
            }
            else if (!hasActionEvents && hasStateGroups)
            {
                _logger.Here().Information($"Generating event data .dat");
                outputs.Add(_datGeneratorService.GenerateEventDatFile(
                    audioProjectNameWithoutExtension,
                    stateGroups: audioProject.StateGroups));
            }

            return outputs;
        }
    }
}
