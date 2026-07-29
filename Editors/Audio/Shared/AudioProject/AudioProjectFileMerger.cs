using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editors.Audio.Shared.AudioProject.Models;
using Shared.Core.Services;

namespace Editors.Audio.Shared.AudioProject
{
    public static class AudioProjectFileMerger
    {
        public static void Merge(AudioProjectFile baseAudioProject, AudioProjectFile mergingAudioProject, string baseFileName, string mergingFileName)
        {
            ArgumentNullException.ThrowIfNull(baseAudioProject);
            ArgumentNullException.ThrowIfNull(mergingAudioProject);

            if (!string.Equals(
                    baseAudioProject.Language,
                    mergingAudioProject.Language,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Audio Projects with different languages cannot be merged.");
            }

            MergeSoundBanks(baseAudioProject.SoundBanks, mergingAudioProject.SoundBanks, baseFileName, mergingFileName);
            MergeStateGroups(baseAudioProject.StateGroups, mergingAudioProject.StateGroups);
            MergeAudioFiles(baseAudioProject.AudioFiles, mergingAudioProject.AudioFiles);
        }

        private static void MergeSoundBanks(List<SoundBank> baseSoundBanks, List<SoundBank> mergingSoundBanks, string baseFileName, string mergingFileName)
        {
            foreach (var mergingSoundBank in mergingSoundBanks)
            {
                var mergingSoundBankBaseName = RemoveSoundBankProjectSuffix(mergingSoundBank.Name, mergingFileName);
                var baseSoundBank = FindSoundBankByBaseName(baseSoundBanks, baseFileName, mergingSoundBankBaseName);

                if (baseSoundBank == null)
                {
                    NormaliseSoundBankName(mergingSoundBank, baseFileName, mergingFileName);
                    baseSoundBanks.TryAdd(mergingSoundBank);
                    SortAudioProjectItems(mergingSoundBank);
                    continue;
                }

                MergeDialogueEvents(baseSoundBank, mergingSoundBank);
                MergeActionEvents(baseSoundBank, mergingSoundBank);

                foreach (var sound in mergingSoundBank.Sounds)
                {
                    var existingSound = baseSoundBank.Sounds
                        .FirstOrDefault(item => item.Id == sound.Id);
                    if (existingSound == null)
                        baseSoundBank.Sounds.TryAdd(sound);
                    else
                        EnsureEquivalent(existingSound, sound);
                }

                foreach (var randomSequenceContainer in mergingSoundBank.RandomSequenceContainers)
                {
                    var existingContainer = baseSoundBank
                        .RandomSequenceContainers
                        .FirstOrDefault(item =>
                            item.Id == randomSequenceContainer.Id);
                    if (existingContainer == null)
                    {
                        baseSoundBank.RandomSequenceContainers
                            .TryAdd(randomSequenceContainer);
                    }
                    else
                    {
                        EnsureEquivalent(
                            existingContainer,
                            randomSequenceContainer);
                    }
                }

                SortAudioProjectItems(baseSoundBank);
            }

            var sortedSoundBanks = baseSoundBanks
                .OrderBy(soundBank => soundBank?.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            baseSoundBanks.Clear();
            baseSoundBanks.AddRange(sortedSoundBanks);
        }

        private static SoundBank FindSoundBankByBaseName(List<SoundBank> baseSoundBanks, string baseAudioProjectNameWithoutExtension, string mergingSoundBankBaseName)
        {
            foreach (var baseSoundBank in baseSoundBanks)
            {
                var baseSoundBankBaseName = RemoveSoundBankProjectSuffix(baseSoundBank.Name, baseAudioProjectNameWithoutExtension);
                if (string.Equals(baseSoundBankBaseName, mergingSoundBankBaseName, StringComparison.OrdinalIgnoreCase))
                    return baseSoundBank;
            }

            return null;
        }

        private static void MergeDialogueEvents(SoundBank baseSoundBank, SoundBank mergingSoundBank)
        {
            foreach (var mergingDialogueEvent in mergingSoundBank.DialogueEvents)
            {
                var baseDialogueEvent = baseSoundBank.DialogueEvents.FirstOrDefault(dialogueEvent => dialogueEvent.Id == mergingDialogueEvent.Id);
                if (baseDialogueEvent == null)
                {
                    SortStatePaths(mergingDialogueEvent);
                    baseSoundBank.DialogueEvents.TryAdd(mergingDialogueEvent);
                    continue;
                }

                EnsureSameName(
                    baseDialogueEvent.Name,
                    mergingDialogueEvent.Name,
                    baseDialogueEvent.Id);
                foreach (var mergingStatePath in mergingDialogueEvent.StatePaths)
                {
                    var existingStatePath = baseDialogueEvent.StatePaths
                        .FirstOrDefault(statePath =>
                            string.Equals(
                                statePath.Name,
                                mergingStatePath.Name,
                                StringComparison.OrdinalIgnoreCase));
                    if (existingStatePath == null)
                    {
                        baseDialogueEvent.StatePaths
                            .TryAdd(mergingStatePath);
                    }
                    else if (existingStatePath.TargetHircId !=
                                 mergingStatePath.TargetHircId ||
                             existingStatePath.TargetHircType !=
                                 mergingStatePath.TargetHircType ||
                             !HaveEquivalentNodes(
                                 existingStatePath,
                                 mergingStatePath))
                    {
                        throw new InvalidOperationException(
                            LocalizationManager.Instance.GetFormat(
                                "AudioProjectMerger.StatePathConflict",
                                mergingStatePath.Name));
                    }
                }

                SortStatePaths(baseDialogueEvent);
            }

            SortDialogueEvents(baseSoundBank);
        }

        private static bool HaveEquivalentNodes(
            StatePath baseStatePath,
            StatePath mergingStatePath)
        {
            if (baseStatePath.Nodes.Count !=
                mergingStatePath.Nodes.Count)
            {
                return false;
            }

            for (var index = 0;
                 index < baseStatePath.Nodes.Count;
                 index++)
            {
                var baseNode = baseStatePath.Nodes[index];
                var mergingNode = mergingStatePath.Nodes[index];
                if (baseNode.StateGroup.Id !=
                        mergingNode.StateGroup.Id ||
                    baseNode.State.Id != mergingNode.State.Id)
                {
                    return false;
                }
            }

            return true;
        }

        private static void MergeActionEvents(SoundBank baseSoundBank, SoundBank mergingSoundBank)
        {
            foreach (var mergingActionEvent in mergingSoundBank.ActionEvents)
            {
                foreach (var action in mergingActionEvent.Actions)
                    action.BankId = baseSoundBank.Id;

                var existingActionEvent = baseSoundBank.ActionEvents
                    .FirstOrDefault(actionEvent =>
                        actionEvent.Id == mergingActionEvent.Id);
                if (existingActionEvent == null)
                {
                    baseSoundBank.ActionEvents.TryAdd(mergingActionEvent);
                }
                else
                {
                    EnsureSameName(
                        existingActionEvent.Name,
                        mergingActionEvent.Name,
                        existingActionEvent.Id);
                    EnsureEquivalent(
                        existingActionEvent,
                        mergingActionEvent);
                }
            }

            SortActionEvents(baseSoundBank);
        }

        private static void MergeStateGroups(List<StateGroup> baseStateGroups, List<StateGroup> mergingStateGroups)
        {
            foreach (var mergingStateGroup in mergingStateGroups)
            {
                var baseStateGroup = baseStateGroups.FirstOrDefault(stateGroup => stateGroup.Id == mergingStateGroup.Id);
                if (baseStateGroup == null)
                {
                    baseStateGroups.TryAdd(mergingStateGroup);
                    continue;
                }

                EnsureSameName(
                    baseStateGroup.Name,
                    mergingStateGroup.Name,
                    baseStateGroup.Id);
                foreach (var mergingState in mergingStateGroup.States)
                {
                    var existingState = baseStateGroup.States
                        .FirstOrDefault(state =>
                            state.Id == mergingState.Id);
                    if (existingState == null)
                        baseStateGroup.States.TryAdd(mergingState);
                    else
                    {
                        EnsureSameName(
                            existingState.Name,
                            mergingState.Name,
                            existingState.Id);
                    }
                }
            }
        }

        private static void NormaliseSoundBankName(SoundBank soundBankToNormalise, string baseFileName, string mergingFileName)
        {
            var soundBankBaseName = RemoveSoundBankProjectSuffix(soundBankToNormalise.Name, mergingFileName);
            soundBankToNormalise.Name = $"{soundBankBaseName}_{baseFileName}";
        }

        private static string RemoveSoundBankProjectSuffix(string soundBankName, string audioProjectNameWithoutExtension)
        {
            if (string.IsNullOrWhiteSpace(soundBankName))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(audioProjectNameWithoutExtension))
                return soundBankName;

            var suffix = $"_{audioProjectNameWithoutExtension}";
            if (soundBankName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return soundBankName.Substring(0, soundBankName.Length - suffix.Length);

            return soundBankName;
        }

        private static void SortAudioProjectItems(SoundBank soundBank)
        {
            SortDialogueEvents(soundBank);
            SortActionEvents(soundBank);
        }

        private static void SortDialogueEvents(SoundBank soundBank)
        {
            var sortedDialogueEvents = soundBank.DialogueEvents
                .OrderBy(dialogueEvent => dialogueEvent?.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            soundBank.DialogueEvents.Clear();
            soundBank.DialogueEvents.AddRange(sortedDialogueEvents);
        }

        private static void SortActionEvents(SoundBank soundBank)
        {
            var sortedActionEvents = soundBank.ActionEvents
                .OrderBy(actionEvent => actionEvent?.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            soundBank.ActionEvents.Clear();
            soundBank.ActionEvents.AddRange(sortedActionEvents);
        }

        private static void SortStatePaths(DialogueEvent dialogueEvent)
        {
            var sortedStatePaths = dialogueEvent.StatePaths
                .OrderBy(statePath => statePath?.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            dialogueEvent.StatePaths.Clear();
            dialogueEvent.StatePaths.AddRange(sortedStatePaths);
        }

        private static void MergeAudioFiles(List<AudioFile> baseAudioFiles, List<AudioFile> mergingAudioFiles)
        {
            if (baseAudioFiles == null || mergingAudioFiles == null)
                return;

            foreach (var mergingAudioFile in mergingAudioFiles)
            {
                var existingAudioFile = baseAudioFiles
                    .FirstOrDefault(audioFile =>
                        audioFile.Id == mergingAudioFile.Id);
                if (existingAudioFile == null)
                {
                    baseAudioFiles.TryAdd(mergingAudioFile);
                    continue;
                }

                if (!string.Equals(
                        existingAudioFile.WavPackFilePath,
                        mergingAudioFile.WavPackFilePath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        existingAudioFile.WavPackFileName,
                        mergingAudioFile.WavPackFileName,
                        StringComparison.OrdinalIgnoreCase) ||
                    existingAudioFile.Guid != mergingAudioFile.Guid)
                {
                    ThrowIdConflict(existingAudioFile.Id);
                }

                foreach (var soundId in mergingAudioFile.Sounds)
                {
                    if (!existingAudioFile.Sounds.Contains(soundId))
                        existingAudioFile.Sounds.Add(soundId);
                }

                existingAudioFile.Sounds.Sort();
            }

            var sortedAudioFiles = baseAudioFiles
                .OrderBy(audioFile => audioFile.Id)
                .ToList();

            baseAudioFiles.Clear();
            baseAudioFiles.AddRange(sortedAudioFiles);
        }

        private static void EnsureSameName(
            string baseName,
            string mergingName,
            uint id)
        {
            if (!string.Equals(
                    baseName,
                    mergingName,
                    StringComparison.OrdinalIgnoreCase))
            {
                ThrowIdConflict(id);
            }
        }

        private static void EnsureEquivalent<T>(
            T baseItem,
            T mergingItem)
            where T : AudioProjectItem
        {
            var baseElement =
                JsonSerializer.SerializeToElement(baseItem);
            var mergingElement =
                JsonSerializer.SerializeToElement(mergingItem);
            if (!JsonElement.DeepEquals(
                    baseElement,
                    mergingElement))
            {
                ThrowIdConflict(baseItem.Id);
            }
        }

        private static void ThrowIdConflict(uint id)
        {
            throw new InvalidOperationException(
                LocalizationManager.Instance.GetFormat(
                    "AudioProjectMerger.IdConflict",
                    id));
        }
    }
}
