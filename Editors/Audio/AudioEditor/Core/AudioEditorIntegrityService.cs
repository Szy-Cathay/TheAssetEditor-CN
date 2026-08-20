using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.AudioEditor.Core
{
    public interface IAudioEditorIntegrityService
    {
        void EnsureCorrectSoundBankNames(AudioProjectFile audioProject, string audioProjectNameWithoutExtension);
        void RefreshSourceIds(AudioProjectFile audioProject);
        void CheckDialogueEventInformationIntegrity(List<Wh3DialogueEventDefinition> dialogueEventData);
        void CheckAudioProjectDialogueEventIntegrity(AudioProjectFile audioProject);
        void CheckAudioProjectWavFilesIntegrity(AudioProjectFile audioProject);
        void CheckAudioProjectDataIntegrity(AudioProjectFile audioProject, string audioProjectNameWithoutExtension);
        string GetMergingSoundBanksIntegrityError(
            List<string> selectedModdedSoundBanks,
            CancellationToken cancellationToken);
    }

    public class AudioEditorIntegrityService(
        IPackFileService packFileService,
        IAudioRepository audioRepository) : IAudioEditorIntegrityService
    {
        private readonly IPackFileService _packFileService = packFileService;
        private readonly IAudioRepository _audioRepository = audioRepository;

        public void EnsureCorrectSoundBankNames(AudioProjectFile audioProject, string audioProjectNameWithoutExtension)
        {
            foreach (var soundBank in audioProject.SoundBanks)
            {
                var soundBankAudioProjectName = Wh3SoundBankInformation.GetAudioProjectNameFromSoundBankWithAudioProjectName(soundBank.Name);
                if (audioProjectNameWithoutExtension != soundBankAudioProjectName)
                {
                    var gameSoundBankName = Wh3SoundBankInformation.GetSoundBankNameFromSoundBankWithAudioProjectName(soundBank.Name);
                    var correctSoundBankName = $"{gameSoundBankName}_{audioProjectNameWithoutExtension}";
                    soundBank.Name = correctSoundBankName;
                }
            }
        }

        public void RefreshSourceIds(AudioProjectFile audioProject)
        {
            var usedSourceIds = IdGenerator.GetUsedSourceIds(_audioRepository, audioProject);

            var audioProjectSounds = audioProject.GetSounds();
            foreach (var audioFile in audioProject.AudioFiles)
            {
                var audioFileIds = IdGenerator.GenerateIds(usedSourceIds);
                usedSourceIds.Add(audioFile.Id);

                foreach (var sound in audioProjectSounds.Where(sound => sound.SourceId == audioFile.Id))
                {
                    sound.SourceId = audioFileIds.Id;
                }
                audioFile.Guid = audioFileIds.Guid;
                audioFile.Id = audioFileIds.Id;
            }

            MessageBox.Show(LocalizationManager.Instance.Get("Msg.DeleteAndRecompileWems"), LocalizationManager.Instance.Get("Msg.GeneralError"));
        }

        public void CheckDialogueEventInformationIntegrity(List<Wh3DialogueEventDefinition> information)
        {
            var exclusions = new List<string> { "New_Dialogue_Event", "Battle_Individual_Melee_Weapon_Hit" };
            var gameDialogueEvents = _audioRepository.StateGroupsByDialogueEvent.Keys.Except(exclusions).ToList();
            var audioEditorDialogueEvents = information.Select(item => item.Name).ToList();
            var areGameAndAudioEditorDialogueEventsMatching = new HashSet<string>(gameDialogueEvents).SetEquals(audioEditorDialogueEvents);
            if (!areGameAndAudioEditorDialogueEventsMatching)
            {
                var dialogueEventsOnlyInGame = gameDialogueEvents.Except(audioEditorDialogueEvents).ToList();
                var dialogueEventsOnlyInAudioEditor = audioEditorDialogueEvents.Except(gameDialogueEvents).ToList();

                var dialogueEventsOnlyInGameText = string.Join("\n - ", dialogueEventsOnlyInGame);
                var dialogueEventsOnlyInAudioEditorText = string.Join("\n - ", dialogueEventsOnlyInAudioEditor);

                var message = LocalizationManager.Instance.Get("Msg.DialogueEventInformationIntegrityFailed");
                var dialogueEventsOnlyInGameMessage = LocalizationManager.Instance.GetFormat(
                    "Msg.DialogueEventsOnlyInGame",
                    dialogueEventsOnlyInGameText);
                var dialogueEventsOnlyInAudioEditorMessage = LocalizationManager.Instance.GetFormat(
                    "Msg.DialogueEventsOnlyInAudioEditor",
                    dialogueEventsOnlyInAudioEditorText);

                if (dialogueEventsOnlyInGame.Count > 0)
                    message += dialogueEventsOnlyInGameMessage;

                if (dialogueEventsOnlyInAudioEditor.Count > 0)
                    message += dialogueEventsOnlyInAudioEditorMessage;

                MessageBox.Show(message, LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
        }

        public void CheckAudioProjectDialogueEventIntegrity(AudioProjectFile audioProject)
        {
            var audioProjectDialogueEventsWithStateGroups = new Dictionary<string, List<string>>();
            var dialogueEventsWithStateGroupsWithIntegrityError = new Dictionary<string, List<string>>();
            var hasIntegrityError = false;

            var message = LocalizationManager.Instance.Get("Msg.DialogueEventStateGroupsIntegrityFailed");

            foreach (var soundBank in audioProject.SoundBanks)
            {
                if (Wh3DialogueEventInformation.Contains(soundBank.GameSoundBank))
                {
                    foreach (var dialogueEvent in soundBank.DialogueEvents)
                    {
                        var firstStatePath = dialogueEvent.StatePaths.FirstOrDefault();
                        if (firstStatePath != null)
                        {
                            var gameDialogueEventStateGroups = _audioRepository.StateGroupsByDialogueEvent[dialogueEvent.Name];
                            var audioProjectDialogueEventStateGroups = firstStatePath.Nodes.Select(node => node.StateGroup.Name).ToList();
                            if (!gameDialogueEventStateGroups.SequenceEqual(audioProjectDialogueEventStateGroups))
                            {
                                if (!hasIntegrityError)
                                    hasIntegrityError = true;

                                message += LocalizationManager.Instance.GetFormat(
                                    "Msg.DialogueEventName",
                                    dialogueEvent.Name);

                                var audioProjectDialogueEventStateGroupsText = string.Join("-->", audioProjectDialogueEventStateGroups);
                                message += LocalizationManager.Instance.GetFormat(
                                    "Msg.OldStateGroups",
                                    audioProjectDialogueEventStateGroupsText);

                                var gameDialogueEventStateGroupsText = string.Join("-->", gameDialogueEventStateGroups);
                                message += LocalizationManager.Instance.GetFormat(
                                    "Msg.NewStateGroups",
                                    gameDialogueEventStateGroupsText);
                            }
                        }
                    }
                }
            }

            if (hasIntegrityError)
                MessageBox.Show(message, LocalizationManager.Instance.Get("Msg.GeneralError"));
        }

        public void CheckAudioProjectWavFilesIntegrity(AudioProjectFile audioProject)
        {
            var missingWavFiles = new List<string>();

            foreach (var audioFile in audioProject.AudioFiles)
            {
                if (string.IsNullOrWhiteSpace(audioFile.WavPackFilePath))
                    continue;

                var packFile = _packFileService.FindFile(audioFile.WavPackFilePath);
                if (packFile == null)
                    missingWavFiles.Add(audioFile.WavPackFilePath);
            }

            if (missingWavFiles.Count > 0)
            {
                var sortedMissingWavFiles = missingWavFiles.OrderBy(wavFilePath => wavFilePath).ToList();
                var missingWavFilesText = string.Join("\n - ", sortedMissingWavFiles);

                var message = LocalizationManager.Instance.GetFormat(
                    "Msg.WavFilesIntegrityFailed",
                    missingWavFilesText);

                MessageBox.Show(message, LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
        }

        public void CheckAudioProjectDataIntegrity(AudioProjectFile audioProject, string audioProjectNameWithoutExtension)
        {
            var usedHircIds = new HashSet<uint>();
            var usedSourceIds = new HashSet<uint>();

            var audioProjectGeneratableItemIds = audioProject.GetGeneratableItemIds();
            var audioProjectSourceIds = audioProject.GetAudioFileIds();

            var languageId = WwiseHash.Compute(audioProject.Language);
            var languageHircIds = _audioRepository.GetUsedVanillaHircIdsByLanguageId(languageId);
            var languageSourceIds = _audioRepository.GetUsedVanillaSourceIdsByLanguageId(languageId);

            // Reset and remove any AudioProjectItem IDs used in vanilla so we can handle them
            var hircIdConflicts = new List<uint>();
            var generatableItems = audioProject.GetGeneratableItems();
            foreach (var generatableItem in generatableItems)
            {
                if (generatableItem.Id == 0)
                    continue;

                if (languageHircIds.Contains(generatableItem.Id) && generatableItem is not DialogueEvent)
                {
                    hircIdConflicts.Add(generatableItem.Id);
                    audioProjectGeneratableItemIds.Remove(generatableItem.Id);
                    generatableItem.Guid = Guid.Empty;
                    generatableItem.Id = 0;
                }
            }

            if (hircIdConflicts.Count > 0)
            {
                MessageBox.Show(
                    LocalizationManager.Instance.GetFormat("Msg.HircIdConflictsDetected", string.Join(", ", hircIdConflicts)),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }

            // Reset and remove any Source IDs used in vanilla so we can handle them
            var sourceIdConflicts = new List<uint>();
            var audioProjectSounds = audioProject.GetSounds();
            foreach (var sound in audioProjectSounds)
            {
                if (sound.SourceId == 0)
                    continue;

                if (languageSourceIds.Contains(sound.SourceId))
                {
                    sourceIdConflicts.Add(sound.Id);
                    audioProjectSourceIds.Remove(sound.SourceId);
                    sound.SourceId = 0;
                }
            }

            if (sourceIdConflicts.Count > 0)
            {
                MessageBox.Show(
                    LocalizationManager.Instance.GetFormat("Msg.SourceIdConflictsDetected", string.Join(", ", sourceIdConflicts.Select(id => id + ".wem"))),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }

            usedHircIds.UnionWith(audioProjectGeneratableItemIds);
            usedHircIds.UnionWith(languageHircIds);
            usedSourceIds.UnionWith(audioProjectSourceIds);
            usedSourceIds.UnionWith(languageSourceIds);

            // TODO: Implement a get all references by ID to then replace them all for the clashing IDs

            foreach (var soundBank in audioProject.SoundBanks)
            {
                ResolveSoundBankDataIntegrity(audioProject, audioProjectNameWithoutExtension, soundBank);

                if (soundBank.ActionEvents.Count != 0)
                    ResolveActionEventDataIntegrity(usedHircIds, usedSourceIds, soundBank);

                if (soundBank.DialogueEvents.Count != 0)
                    ResolveDialogueEventDataIntegrity(usedHircIds, usedSourceIds, soundBank);
            }

            foreach (var audioFile in audioProject.AudioFiles)
                ResolveAudioFileDataIntegrity(audioFile);

            ResolveStateGroupDataIntegrity(audioProject);
        }

        private static void ResolveSoundBankDataIntegrity(AudioProjectFile audioProject, string audioProjectNameWithoutExtension, SoundBank soundBank)
        {
            if (soundBank == null)
                throw new InvalidOperationException("SoundBank should not be null.");

            if (string.IsNullOrWhiteSpace(soundBank.Name))
                throw new InvalidOperationException("SoundBank.Name should not be null or empty.");

            var gameSoundBankName = Wh3SoundBankInformation.GetSoundBankNameFromSoundBankWithAudioProjectName(soundBank.Name);
            var gameSoundBank = Wh3SoundBankInformation.GetSoundBank(gameSoundBankName);
            var correctSoundBankName = $"{gameSoundBankName}_{audioProjectNameWithoutExtension}";

            if (soundBank.Name != correctSoundBankName)
                throw new InvalidOperationException($"SoundBank.Name is incorrect. Expected '{correctSoundBankName}'.");

            if (soundBank.Id == 0)
                throw new InvalidOperationException("SoundBank.Id should not be 0.");

            if (soundBank.GameSoundBank != gameSoundBank)
                throw new InvalidOperationException($"SoundBank.GameSoundBank is incorrect for '{soundBank.Name}'.");

            if (string.IsNullOrWhiteSpace(soundBank.Language))
                throw new InvalidOperationException("SoundBank.Language should not be null or empty.");

            var requiredLanguage = Wh3SoundBankInformation.GetRequiredLanguage(soundBank.GameSoundBank);
            if (requiredLanguage != null)
            {
                var requiredLanguageAsString = Wh3LanguageInformation.GetLanguageAsString((Wh3Language)requiredLanguage);
                if (!string.Equals(soundBank.Language, requiredLanguageAsString, StringComparison.Ordinal))
                    throw new InvalidOperationException($"SoundBank.Language should be '{requiredLanguageAsString}'.");
            }
        }

        private static void ResolveActionEventDataIntegrity(HashSet<uint> usedHircIds, HashSet<uint> usedSourceIds, SoundBank soundBank)
        {
            foreach (var actionEvent in soundBank.ActionEvents)
            {
                if (string.IsNullOrWhiteSpace(actionEvent.Name))
                    throw new InvalidOperationException("The Action Event should have a name. Check for Action Events without names.");

                if (actionEvent.Id == 0)
                    throw new InvalidOperationException($"ActionEvent.Id should not be 0 for '{actionEvent.Name}'.");

                if (actionEvent.Actions.Count > 1)
                    throw new NotSupportedException("Multiple Actions are not supported.");

                var overrideBusId = Wh3ActionEventInformation.GetOverrideBusId(actionEvent.ActionEventType);
                var actorMixerId = Wh3ActionEventInformation.GetActorMixerId(actionEvent.ActionEventType);

                foreach (var action in actionEvent.Actions)
                {
                    if (action.Id == 0)
                        throw new InvalidOperationException($"Action.Id should not be 0.");

                    if (action.BankId == 0)
                        throw new InvalidOperationException($"Action.BankId should not be 0.");

                    if (action.TargetHircTypeIsSound())
                    {
                        var sound = soundBank.GetSound(action.TargetHircId);
                        if (sound == null)
                            throw new InvalidOperationException($"Action target Sound not found.");

                        if (action.IdExt == 0)
                            throw new InvalidOperationException($"Action.IdExt should not be 0.");

                        if (overrideBusId != 0 && sound.OverrideBusId == 0)
                            throw new InvalidOperationException($"Sound.OverrideBusId should not be 0 when OverrideBusId is required.");

                        if (actorMixerId != 0 && sound.DirectParentId == 0)
                            throw new InvalidOperationException($"Sound.DirectParentId should not be 0 when DirectParentId is required.");

                        ResolveSoundDataIntegrity(sound, soundBank, usedHircIds, usedSourceIds);
                    }
                    else if (action.TargetHircTypeIsRandomSequenceContainer())
                    {
                        var randomSequenceContainer = soundBank.GetRandomSequenceContainer(action.TargetHircId);
                        if (randomSequenceContainer == null)
                            throw new InvalidOperationException($"Action target RandomSequenceContainer not found.");

                        if (action.IdExt == 0)
                            throw new InvalidOperationException($"Action.IdExt should not be 0.");

                        if (overrideBusId != 0 && randomSequenceContainer.OverrideBusId == 0)
                            throw new InvalidOperationException("RandomSequenceContainer.OverrideBusId should not be 0 when OverrideBusId is required.");

                        if (actorMixerId != 0 && randomSequenceContainer.DirectParentId == 0)
                            throw new InvalidOperationException("RandomSequenceContainer.DirectParentId should not be 0 when DirectParentId is required.");

                        ResolveRandomSequenceContainerDataIntegrity(randomSequenceContainer, soundBank, usedHircIds, usedSourceIds);
                    }
                }
            }
        }

        private static void ResolveDialogueEventDataIntegrity(HashSet<uint> usedHircIds, HashSet<uint> usedSourceIds, SoundBank soundBank)
        {
            foreach (var dialogueEvent in soundBank.DialogueEvents)
            {
                if (string.IsNullOrWhiteSpace(dialogueEvent.Name))
                    throw new InvalidOperationException("DialogueEvent.Name should not be null or empty.");

                if (dialogueEvent.Id == 0)
                    throw new InvalidOperationException($"DialogueEvent.Id should not be 0 for '{dialogueEvent.Name}'.");

                var actorMixerId = Wh3DialogueEventInformation.GetActorMixerId(dialogueEvent.Name);

                foreach (var statePath in dialogueEvent.StatePaths)
                {
                    foreach (var statePathNode in statePath.Nodes)
                    {
                        if (string.IsNullOrWhiteSpace(statePathNode.StateGroup.Name))
                            throw new InvalidOperationException("StateGroup.Name should not be null or empty.");
                        if (statePathNode.StateGroup.Id == 0)
                            throw new InvalidOperationException($"StateGroup.Id should not be 0 for '{statePathNode.StateGroup.Name}'.");

                        if (statePathNode.State.Name == "Any" && statePathNode.State.Id != 0)
                            statePathNode.State.Id = 0;
                        else if (statePathNode.State.Name != "Any" && statePathNode.State.Id == 0)
                            throw new InvalidOperationException($"State.Id should not be 0 for '{statePathNode.State.Name}'.");
                    }

                    if (statePath.TargetHircTypeIsSound())
                    {
                        var sound = soundBank.GetSound(statePath.TargetHircId);
                        if (sound == null)
                            throw new InvalidOperationException($"StatePath target Sound not found for '{dialogueEvent.Name}'.");

                        if (actorMixerId != 0 && sound.DirectParentId == 0)
                            throw new InvalidOperationException($"Sound.DirectParentId should not be 0 for '{sound.Name}'.");

                        ResolveSoundDataIntegrity(sound, soundBank, usedHircIds, usedSourceIds);
                    }
                    else if (statePath.TargetHircTypeIsRandomSequenceContainer())
                    {
                        var randomSequenceContainer = soundBank.GetRandomSequenceContainer(statePath.TargetHircId);
                        if (randomSequenceContainer == null)
                            throw new InvalidOperationException($"StatePath target RandomSequenceContainer not found for '{dialogueEvent.Name}'.");

                        if (actorMixerId != 0 && randomSequenceContainer.DirectParentId == 0)
                            throw new InvalidOperationException("RandomSequenceContainer.DirectParentId should not be 0.");

                        ResolveRandomSequenceContainerDataIntegrity(randomSequenceContainer, soundBank, usedHircIds, usedSourceIds);
                    }
                }
            }
        }

        private static void ResolveStateGroupDataIntegrity(AudioProjectFile audioProject)
        {
            if (audioProject.StateGroups.Count != 0)
            {
                foreach (var stateGroup in audioProject.StateGroups)
                {
                    if (string.IsNullOrWhiteSpace(stateGroup.Name))
                        throw new InvalidOperationException("StateGroup.Name should not be null or empty.");
                    if (stateGroup.Id == 0)
                        throw new InvalidOperationException($"StateGroup.Id should not be 0 for '{stateGroup.Name}'.");

                    foreach (var state in stateGroup.States)
                    {
                        if (state.Name == "Any" && state.Id != 0)
                            state.Id = 0;
                        else if (state.Name != "Any" && state.Id == 0)
                            throw new InvalidOperationException($"State.Id should not be 0 for '{state.Name}'.");
                    }
                }
            }
        }

        private static void ResolveRandomSequenceContainerDataIntegrity(
            RandomSequenceContainer randomSequenceContainer,
            SoundBank soundBank,
            HashSet<uint> usedHircIds,
            HashSet<uint> usedSourceIds,
            StatePath statePath = null,
            uint overrideBusId = 0,
            uint directParentId = 0)
        {
            if (randomSequenceContainer == null)
                throw new InvalidOperationException("RandomSequenceContainer should not be null.");

            if (randomSequenceContainer.Guid == Guid.Empty)
                throw new InvalidOperationException("RandomSequenceContainer.Guid should not be empty.");

            if (randomSequenceContainer.Id == 0)
                throw new InvalidOperationException("RandomSequenceContainer.Id should not be 0.");

            if (overrideBusId != 0 && randomSequenceContainer.OverrideBusId == 0)
                throw new InvalidOperationException("RandomSequenceContainer.OverrideBusId should not be 0 when OverrideBusId is required.");

            if (directParentId != 0 && randomSequenceContainer.DirectParentId == 0)
                throw new InvalidOperationException("RandomSequenceContainer.DirectParentId should not be 0 when DirectParentId is required.");

            var playlistOrder = 0;
            var sounds = soundBank.GetSounds(randomSequenceContainer.Children);
            foreach (var sound in sounds)
            {
                playlistOrder++;
                ResolveSoundDataIntegrity(sound, soundBank, usedHircIds, usedSourceIds);

                if (sound.DirectParentId == 0)
                    throw new InvalidOperationException($"Sound.DirectParentId should not be 0 for '{sound?.Name}'.");

                if (sound.PlaylistOrder == 0)
                    throw new InvalidOperationException($"Sound.PlaylistOrder should not be 0 for '{sound?.Name}'. Expected '{playlistOrder}'.");
            }
        }

        private static void ResolveSoundDataIntegrity(
            Sound sound,
            SoundBank soundBank,
            HashSet<uint> usedHircIds,
            HashSet<uint> usedSourceIds,
            uint overrideBusId = 0,
            uint directParentId = 0,
            int playlistOrder = 0)
        {
            if (sound == null)
                throw new InvalidOperationException("Sound should not be null.");

            if (sound.Guid == Guid.Empty)
                throw new InvalidOperationException("Sound.Guid should not be empty.");

            if (sound.Id == 0)
                throw new InvalidOperationException("Sound.Id should not be 0.");

            if (string.IsNullOrWhiteSpace(sound.Language))
                throw new InvalidOperationException("Sound.Language should not be null or empty.");

            if (overrideBusId != 0 && sound.OverrideBusId == 0)
                throw new InvalidOperationException($"Sound.OverrideBusId should not be 0 for '{sound.Name}'.");

            if (directParentId != 0 && sound.DirectParentId == 0)
                throw new InvalidOperationException($"Sound.DirectParentId should not be 0 for '{sound.Name}'.");

            if (sound.SourceId == 0)
                throw new InvalidOperationException("SourceId should not be 0.");

            if (playlistOrder != 0 && sound.PlaylistOrder == 0)
                throw new InvalidOperationException($"Sound.PlaylistOrder should not be 0 for '{sound.Name}'.");
        }

        private static void ResolveAudioFileDataIntegrity(AudioFile audioFile)
        {
            if (audioFile == null)
                throw new InvalidOperationException("AudioFile should not be null.");

            if (audioFile.Id == 0)
                throw new InvalidOperationException("AudioFile.Id should not be 0.");

            if (audioFile.Guid == Guid.Empty)
                throw new InvalidOperationException("AudioFile.Guid should not be empty.");

            if (string.IsNullOrWhiteSpace(audioFile.WavPackFilePath))
                throw new InvalidOperationException("AudioFile.WavPackFilePath should not be null or empty.");

            if (string.IsNullOrWhiteSpace(audioFile.WavPackFileName))
                throw new InvalidOperationException("AudioFile.WavPackFileName should not be null or empty.");

            if (audioFile.Sounds == null)
                throw new InvalidOperationException("AudioFile.SoundReferences should not be null.");

            if (audioFile.Sounds.Count == 0)
                throw new InvalidOperationException("AudioFile.SoundReferences should not be empty.");
        }

        public string GetMergingSoundBanksIntegrityError(
            List<string> selectedModdedSoundBanks,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moddedHircsByBnkByLanguage = _audioRepository.GetModdedHircsByBnkByLanguage();
            var selectedSoundBanks = new HashSet<string>(
                selectedModdedSoundBanks,
                StringComparer.OrdinalIgnoreCase);

            var hasClashes = false;
            var wemHashByPackFile = new Dictionary<PackFile, string>(
                ReferenceEqualityComparer.Instance);
            var hircHashByHirc = new Dictionary<HircItem, string>(
                ReferenceEqualityComparer.Instance);
            var sourceHashByHirc = new Dictionary<HircItem, string>(
                ReferenceEqualityComparer.Instance);
            var dialogueIndexByEvent =
                new Dictionary<ICAkDialogueEvent, DialogueEventIndex>(
                    ReferenceEqualityComparer.Instance);
            var messageBuilder = new StringBuilder()
                .AppendLine(LocalizationManager.Instance.Get("Msg.MergingSoundBanksIdIntegrityFailed"))
                .AppendLine();

            foreach (var languageEntry in moddedHircsByBnkByLanguage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var languageName = languageEntry.Key;
                var hircsByBnkDictionary = languageEntry.Value
                    .Where(entry => selectedSoundBanks.Contains(entry.Key))
                    .ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value,
                        StringComparer.OrdinalIgnoreCase);

                var idsByBnk = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
                var bnksById = new Dictionary<uint, HashSet<string>>();

                var sourceIdsByBnk = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
                var bnksBySourceId = new Dictionary<uint, HashSet<string>>();

                foreach (var bnkEntry in hircsByBnkDictionary)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bnkName = bnkEntry.Key;

                    if (!idsByBnk.TryGetValue(bnkName, out var idSet))
                    {
                        idSet = [];
                        idsByBnk[bnkName] = idSet;
                    }

                    if (!sourceIdsByBnk.TryGetValue(bnkName, out var sourceIdSet))
                    {
                        sourceIdSet = [];
                        sourceIdsByBnk[bnkName] = sourceIdSet;
                    }

                    foreach (var hirc in bnkEntry.Value)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (hirc is ICAkDialogueEvent)
                            continue;

                        var id = hirc.Id;
                        if (idSet.Add(id))
                        {
                            if (!bnksById.TryGetValue(id, out var bnksContainingThisId))
                            {
                                bnksContainingThisId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                bnksById[id] = bnksContainingThisId;
                            }

                            bnksContainingThisId.Add(bnkName);
                        }

                        if (hirc is ICAkSound sound)
                        {
                            var sourceId = sound.GetSourceId();

                            if (sourceIdSet.Add(sourceId))
                            {
                                if (!bnksBySourceId.TryGetValue(sourceId, out var bnksContainingThisSourceId))
                                {
                                    bnksContainingThisSourceId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    bnksBySourceId[sourceId] = bnksContainingThisSourceId;
                                }

                                bnksContainingThisSourceId.Add(bnkName);
                            }
                        }
                    }
                }

                var clashingIdsById = bnksById
                    .Where(pair =>
                        pair.Value.Count > 1 &&
                        !SoundBanksUseIdenticalHirc(
                            pair.Key,
                            pair.Value,
                            hircsByBnkDictionary,
                            hircHashByHirc,
                            cancellationToken))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                var clashingSourceIdsById = bnksBySourceId
                    .Where(pair =>
                        pair.Value.Count > 1 &&
                        !SoundBanksUseIdenticalSource(
                            pair.Key,
                            pair.Value,
                            hircsByBnkDictionary,
                            wemHashByPackFile,
                            sourceHashByHirc,
                            cancellationToken))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                var conflictingDialogueEventsById =
                    FindConflictingDialogueEvents(
                        hircsByBnkDictionary,
                        dialogueIndexByEvent,
                        cancellationToken);

                if (clashingIdsById.Count == 0 &&
                    clashingSourceIdsById.Count == 0 &&
                    conflictingDialogueEventsById.Count == 0)
                    continue;

                hasClashes = true;
                messageBuilder.AppendLine(LocalizationManager.Instance.GetFormat("Msg.LanguageName", languageName));

                var conflictingBnks = clashingIdsById.Values
                    .SelectMany(bnkNames => bnkNames)
                    .Concat(clashingSourceIdsById.Values.SelectMany(bnkNames => bnkNames))
                    .Concat(conflictingDialogueEventsById.Values
                        .SelectMany(bnkNames => bnkNames))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

                foreach (var conflictingBnk in conflictingBnks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    messageBuilder.AppendLine(conflictingBnk);
                }

                messageBuilder.AppendLine();

                foreach (var sourceBnk in idsByBnk.Keys
                             .Union(sourceIdsByBnk.Keys, StringComparer.OrdinalIgnoreCase)
                             .Union(
                                 conflictingDialogueEventsById.Values
                                     .SelectMany(bnkNames => bnkNames),
                                 StringComparer.OrdinalIgnoreCase)
                             .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var anyConflicts = false;

                    if (idsByBnk.TryGetValue(sourceBnk, out var idsFromThisBnk))
                    {
                        var conflictingIdsFromThisBnk = idsFromThisBnk
                            .Where(clashingIdsById.ContainsKey)
                            .OrderBy(value => value);

                        foreach (var id in conflictingIdsFromThisBnk)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            anyConflicts = true;
                            var otherBnks = clashingIdsById[id]
                                .Where(other => !string.Equals(other, sourceBnk, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

                            messageBuilder.AppendLine(LocalizationManager.Instance.GetFormat(
                                "Msg.HircIdAlsoInSoundBanks",
                                id,
                                string.Join(", ", otherBnks)));
                        }
                    }

                    if (sourceIdsByBnk.TryGetValue(sourceBnk, out var sourceIdsFromThisBnk))
                    {
                        var conflictingSourceIdsFromThisBnk = sourceIdsFromThisBnk
                            .Where(clashingSourceIdsById.ContainsKey)
                            .OrderBy(value => value);

                        foreach (var sourceId in conflictingSourceIdsFromThisBnk)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            anyConflicts = true;
                            var otherBnks = clashingSourceIdsById[sourceId]
                                .Where(other => !string.Equals(other, sourceBnk, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

                            messageBuilder.AppendLine(LocalizationManager.Instance.GetFormat(
                                "Msg.SourceIdAlsoInSoundBanks",
                                sourceId,
                                string.Join(", ", otherBnks)));
                        }
                    }

                    foreach (var dialogueConflict in
                             conflictingDialogueEventsById
                                 .Where(entry => entry.Value.Contains(sourceBnk))
                                 .OrderBy(entry => entry.Key))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        anyConflicts = true;
                        var otherBnks = dialogueConflict.Value
                            .Where(other => !string.Equals(
                                other,
                                sourceBnk,
                                StringComparison.OrdinalIgnoreCase))
                            .OrderBy(
                                name => name,
                                StringComparer.OrdinalIgnoreCase);
                        messageBuilder.AppendLine(
                            LocalizationManager.Instance.GetFormat(
                                "Msg.DialogueEventConflict",
                                dialogueConflict.Key,
                                string.Join(", ", otherBnks)));
                    }

                    if (anyConflicts)
                        messageBuilder.AppendLine();
                }

                messageBuilder.AppendLine();
            }

            return hasClashes ? messageBuilder.ToString() : null;
        }

        private static bool SoundBanksUseIdenticalHirc(
            uint hircId,
            IEnumerable<string> soundBankPaths,
            IReadOnlyDictionary<string, List<HircItem>> hircsBySoundBank,
            Dictionary<HircItem, string> hircHashByHirc,
            CancellationToken cancellationToken)
        {
            var hircs = soundBankPaths
                .SelectMany(soundBankPath => hircsBySoundBank[soundBankPath]
                    .Where(hirc =>
                        hirc.Id == hircId &&
                        hirc is not ICAkDialogueEvent))
                .ToList();
            if (hircs.Count < 2)
                return false;

            string expectedHash = null;
            foreach (var hirc in hircs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetHircHash(hirc, hircHashByHirc, out var hash))
                    return false;

                if (expectedHash == null)
                    expectedHash = hash;
                else if (!string.Equals(
                             expectedHash,
                             hash,
                             StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetHircHash(
            HircItem hirc,
            Dictionary<HircItem, string> hircHashByHirc,
            out string hash)
        {
            if (hircHashByHirc.TryGetValue(hirc, out hash))
                return true;

            var originalSectionSize = hirc.SectionSize;
            try
            {
                hirc.UpdateSectionSize();
                hash = Convert.ToHexString(
                    SHA256.HashData(hirc.WriteData()));
                hircHashByHirc[hirc] = hash;
                return true;
            }
            catch (Exception)
            {
                hash = null;
                return false;
            }
            finally
            {
                hirc.SectionSize = originalSectionSize;
            }
        }

        private static Dictionary<uint, HashSet<string>>
            FindConflictingDialogueEvents(
                IReadOnlyDictionary<string, List<HircItem>>
                    hircsBySoundBank,
                Dictionary<ICAkDialogueEvent, DialogueEventIndex>
                    dialogueIndexByEvent,
                CancellationToken cancellationToken)
        {
            var dialogueEventsById = hircsBySoundBank
                .SelectMany(soundBankEntry => soundBankEntry.Value
                    .OfType<ICAkDialogueEvent>()
                    .Select(dialogueEvent => (
                        SoundBankPath: soundBankEntry.Key,
                        Hirc: (HircItem)dialogueEvent,
                        DialogueEvent: dialogueEvent)))
                .GroupBy(entry => entry.Hirc.Id);
            var conflicts = new Dictionary<uint, HashSet<string>>();

            foreach (var dialogueEventGroup in dialogueEventsById)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = dialogueEventGroup.ToList();
                for (var firstIndex = 0;
                     firstIndex < entries.Count - 1;
                     firstIndex++)
                {
                    for (var secondIndex = firstIndex + 1;
                         secondIndex < entries.Count;
                         secondIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var first = entries[firstIndex];
                        var second = entries[secondIndex];
                        if (string.Equals(
                                first.SoundBankPath,
                                second.SoundBankPath,
                                StringComparison.OrdinalIgnoreCase) ||
                            !DialogueEventsConflict(
                                first.DialogueEvent,
                                second.DialogueEvent,
                                dialogueIndexByEvent,
                                cancellationToken))
                        {
                            continue;
                        }

                        if (!conflicts.TryGetValue(
                                dialogueEventGroup.Key,
                                out var conflictingBanks))
                        {
                            conflictingBanks = new HashSet<string>(
                                StringComparer.OrdinalIgnoreCase);
                            conflicts[dialogueEventGroup.Key] =
                                conflictingBanks;
                        }

                        conflictingBanks.Add(first.SoundBankPath);
                        conflictingBanks.Add(second.SoundBankPath);
                    }
                }
            }

            return conflicts;
        }

        private static bool DialogueEventsConflict(
            ICAkDialogueEvent first,
            ICAkDialogueEvent second,
            Dictionary<ICAkDialogueEvent, DialogueEventIndex>
                dialogueIndexByEvent,
            CancellationToken cancellationToken)
        {
            var firstIndex = GetDialogueEventIndex(
                first,
                dialogueIndexByEvent,
                cancellationToken);
            var secondIndex = GetDialogueEventIndex(
                second,
                dialogueIndexByEvent,
                cancellationToken);
            if (!firstIndex.Arguments.SequenceEqual(secondIndex.Arguments) ||
                firstIndex.HasInternalConflict ||
                secondIndex.HasInternalConflict)
            {
                return true;
            }

            foreach (var secondLeaf in secondIndex.AudioNodeByPath)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (firstIndex.AudioNodeByPath.TryGetValue(
                        secondLeaf.Key,
                        out var firstAudioNodeId) &&
                    firstAudioNodeId != secondLeaf.Value)
                {
                    return true;
                }

                if (firstIndex.ProperLeafPrefixes.Contains(secondLeaf.Key))
                    return true;

                for (var length = 0;
                     length < secondLeaf.Key.Length;
                     length++)
                {
                    if (firstIndex.AudioNodeByPath.ContainsKey(
                            secondLeaf.Key[..length]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static DialogueEventIndex GetDialogueEventIndex(
            ICAkDialogueEvent dialogueEvent,
            Dictionary<ICAkDialogueEvent, DialogueEventIndex>
                dialogueIndexByEvent,
            CancellationToken cancellationToken)
        {
            if (dialogueIndexByEvent.TryGetValue(
                    dialogueEvent,
                    out var cachedIndex))
            {
                return cachedIndex;
            }

            var leaves = new List<DialogueLeaf>();
            AddDialogueLeaves(
                dialogueEvent.AkDecisionTree.GetDecisionTree(),
                [],
                leaves,
                isRoot: true,
                cancellationToken);
            var audioNodeByPath = new Dictionary<uint[], uint>(
                DialoguePathComparer.Instance);
            var properLeafPrefixes = new HashSet<uint[]>(
                DialoguePathComparer.Instance);
            var hasInternalConflict = false;
            foreach (var leaf in leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (audioNodeByPath.TryGetValue(
                        leaf.Path,
                        out var existingAudioNodeId) &&
                    existingAudioNodeId != leaf.AudioNodeId)
                {
                    hasInternalConflict = true;
                }
                else
                {
                    audioNodeByPath[leaf.Path] = leaf.AudioNodeId;
                }

                for (var length = 0;
                     length < leaf.Path.Length;
                     length++)
                {
                    properLeafPrefixes.Add(leaf.Path[..length]);
                }
            }

            var index = new DialogueEventIndex(
                dialogueEvent.Arguments
                    .Select(argument => (
                        argument.GroupId,
                        argument.GroupType))
                    .ToList(),
                audioNodeByPath,
                properLeafPrefixes,
                hasInternalConflict);
            dialogueIndexByEvent[dialogueEvent] = index;
            return index;
        }

        private static void AddDialogueLeaves(
            ICAkDialogueEvent.IAkDecisionNode node,
            List<uint> path,
            List<DialogueLeaf> leaves,
            bool isRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node == null)
                return;

            if (!isRoot)
                path.Add(node.GetKey());

            if (node.GetChildrenCount() == 0)
            {
                leaves.Add(new DialogueLeaf(
                    path.ToArray(),
                    node.GetAudioNodeId()));
            }
            else
            {
                for (var index = 0;
                     index < node.GetChildrenCount();
                     index++)
                {
                    AddDialogueLeaves(
                        node.GetChildAtIndex(index),
                        path,
                        leaves,
                        isRoot: false,
                        cancellationToken);
                }
            }

            if (!isRoot)
                path.RemoveAt(path.Count - 1);
        }

        private sealed record DialogueLeaf(
            uint[] Path,
            uint AudioNodeId);

        private sealed record DialogueEventIndex(
            List<(uint GroupId, AkGroupType GroupType)> Arguments,
            Dictionary<uint[], uint> AudioNodeByPath,
            HashSet<uint[]> ProperLeafPrefixes,
            bool HasInternalConflict);

        private sealed class DialoguePathComparer : IEqualityComparer<uint[]>
        {
            public static DialoguePathComparer Instance { get; } = new();

            public bool Equals(uint[] first, uint[] second) =>
                ReferenceEquals(first, second) ||
                first != null &&
                second != null &&
                first.AsSpan().SequenceEqual(second);

            public int GetHashCode(uint[] path)
            {
                var hash = new HashCode();
                foreach (var value in path)
                    hash.Add(value);
                return hash.ToHashCode();
            }
        }

        private bool SoundBanksUseIdenticalSource(
            uint sourceId,
            IEnumerable<string> soundBankPaths,
            IReadOnlyDictionary<string, List<HircItem>> hircsBySoundBank,
            Dictionary<PackFile, string> wemHashByPackFile,
            Dictionary<HircItem, string> sourceHashByHirc,
            CancellationToken cancellationToken)
        {
            string expectedHash = null;
            foreach (var soundBankPath in soundBankPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sounds = hircsBySoundBank[soundBankPath]
                    .Where(hirc =>
                        hirc is ICAkSound sound &&
                        sound.GetSourceId() == sourceId)
                    .ToList();
                if (sounds.Count == 0)
                {
                    return false;
                }

                foreach (var hirc in sounds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryGetSourceHash(
                            soundBankPath,
                            hirc,
                            (ICAkSound)hirc,
                            sourceId,
                            wemHashByPackFile,
                            sourceHashByHirc,
                            cancellationToken,
                            out var sourceHash))
                    {
                        return false;
                    }

                    if (expectedHash == null)
                        expectedHash = sourceHash;
                    else if (!string.Equals(
                                 expectedHash,
                                 sourceHash,
                                 StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return expectedHash != null;
        }

        private bool TryGetSourceHash(
            string soundBankPath,
            HircItem hirc,
            ICAkSound sound,
            uint sourceId,
            Dictionary<PackFile, string> wemHashByPackFile,
            Dictionary<HircItem, string> sourceHashByHirc,
            CancellationToken cancellationToken,
            out string sourceHash)
        {
            if (sourceHashByHirc.TryGetValue(hirc, out sourceHash))
                return true;

            var components = new List<string>
            {
                ((ushort)sound.GetStreamType()).ToString()
            };
            if (sound.GetStreamType() != AKBKSourceType.Streaming)
            {
                if (!_audioRepository.DidxAudioListById.TryGetValue(
                        sourceId,
                        out var embeddedCandidates))
                {
                    return false;
                }

                var embeddedHashes = embeddedCandidates
                    .Where(audio => string.Equals(
                        audio.OwnerFilePath,
                        soundBankPath,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(audio => Convert.ToHexString(
                        SHA256.HashData(audio.ByteArray)))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (embeddedHashes.Count != 1)
                    return false;

                components.Add(embeddedHashes[0]);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (sound.GetStreamType() != AKBKSourceType.Data_BNK)
            {
                if (!TryResolveWemFile(
                        soundBankPath,
                        sourceId,
                        wemHashByPackFile,
                        out var wemFile))
                {
                    return false;
                }

                components.Add(GetWemHash(wemFile, wemHashByPackFile));
            }

            sourceHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join("|", components))));
            sourceHashByHirc[hirc] = sourceHash;
            return true;
        }

        private bool TryResolveWemFile(
            string soundBankPath,
            uint sourceId,
            Dictionary<PackFile, string> wemHashByPackFile,
            out PackFile wemFile)
        {
            wemFile = null;
            if (!TryGetSoundBankPackFile(soundBankPath, out var soundBankFile))
                return false;

            var container = _packFileService.GetPackFileContainer(soundBankFile);
            if (container == null ||
                !ReferenceEquals(
                    _packFileService.FindFile(soundBankPath, container),
                    soundBankFile))
            {
                return false;
            }

            var normalisedBankPath = soundBankPath.Replace('/', '\\');
            var directorySeparatorIndex = normalisedBankPath.LastIndexOf('\\');
            var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"audio\\wwise\\{sourceId}.wem",
                $"audio\\{sourceId}.wem"
            };
            if (directorySeparatorIndex >= 0)
            {
                candidatePaths.Add(
                    $"{normalisedBankPath[..directorySeparatorIndex]}\\{sourceId}.wem");
            }

            var candidateFiles = candidatePaths
                .Select(path => _packFileService.FindFile(path, container))
                .Where(file => file != null)
                .Distinct()
                .ToList();
            if (candidateFiles.Count == 0)
                return false;

            var firstCandidate = candidateFiles[0];
            if (candidateFiles.Skip(1).Any(candidate =>
                    !ReferenceEquals(candidate, firstCandidate) &&
                    !string.Equals(
                        GetWemHash(candidate, wemHashByPackFile),
                        GetWemHash(firstCandidate, wemHashByPackFile),
                        StringComparison.Ordinal)))
            {
                return false;
            }

            wemFile = firstCandidate;
            return true;
        }

        private bool TryGetSoundBankPackFile(
            string soundBankPath,
            out PackFile soundBankFile)
        {
            var separatorIndex = Math.Max(
                soundBankPath.LastIndexOf('\\'),
                soundBankPath.LastIndexOf('/'));
            var soundBankFileName = separatorIndex >= 0
                ? soundBankPath[(separatorIndex + 1)..]
                : soundBankPath;

            if (_audioRepository.PackFileByBnkName.TryGetValue(
                    soundBankPath,
                    out soundBankFile) ||
                _audioRepository.PackFileByBnkName.TryGetValue(
                    soundBankFileName,
                    out soundBankFile))
            {
                return true;
            }

            soundBankFile = _audioRepository.PackFileByBnkName
                .FirstOrDefault(entry =>
                    string.Equals(
                        entry.Key,
                        soundBankPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        entry.Key,
                        soundBankFileName,
                        StringComparison.OrdinalIgnoreCase))
                .Value;
            return soundBankFile != null;
        }

        private static string GetWemHash(
            PackFile wemFile,
            Dictionary<PackFile, string> wemHashByPackFile)
        {
            if (!wemHashByPackFile.TryGetValue(wemFile, out var hash))
            {
                hash = Convert.ToHexString(
                    SHA256.HashData(wemFile.DataSource.ReadData()));
                wemHashByPackFile[wemFile] = hash;
            }

            return hash;
        }
    }
}
