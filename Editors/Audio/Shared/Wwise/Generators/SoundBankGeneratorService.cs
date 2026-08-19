using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.Generators.Bkhd;
using Editors.Audio.Shared.Wwise.Generators.Hirc;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Bkhd;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace Editors.Audio.Shared.Wwise.Generators
{
    public interface ISoundBankGeneratorService
    {
        AudioPackOutput GenerateSoundBankWithoutDialogueEvents(
            SoundBank soundBank);
        AudioPackOutput GenerateDialogueEventsForTestingSoundBank(
            SoundBank soundBank);
        AudioPackOutput GenerateMergingSoundBank(SoundBank soundBank);
        Task<bool> GenerateMergedDialogueEventSoundBanksAsync(
            List<string> moddedSoundBanks,
            string soundBankSuffix,
            CancellationToken cancellationToken);
    }

    public interface ISoundBankGeneratorProgressService
    {
        Task<bool> GenerateMergedDialogueEventSoundBanksAsync(
            List<string> moddedSoundBanks,
            string soundBankSuffix,
            IProgress<AudioOperationProgress> progress,
            CancellationToken cancellationToken);
    }

    public class SoundBankGeneratorService :
        ISoundBankGeneratorService,
        ISoundBankGeneratorProgressService
    {
        private readonly IAudioPackOutputService _audioPackOutputService;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IAudioRepository _audioRepository;
        private readonly HircGeneratorServiceFactory _hircGeneratorServiceFactory;
        private readonly IAudioEditorIntegrityService _audioEditorIntegrityService;

        private readonly ILogger _logger = Logging.Create<SoundBankGeneratorService>();

        public SoundBankGeneratorService(
            IAudioPackOutputService audioPackOutputService,
            ApplicationSettingsService applicationSettingsService,
            IAudioRepository audioRepository,
            IAudioEditorIntegrityService audioEditorIntegrityService)
        {
            _audioPackOutputService = audioPackOutputService;
            _applicationSettingsService = applicationSettingsService;
            _audioRepository = audioRepository;
            _audioEditorIntegrityService = audioEditorIntegrityService;

            var bankGeneratorVersion = (uint)GameInformationDatabase.GetGameById(_applicationSettingsService.CurrentSettings.CurrentGame).BankGeneratorVersion;
            _hircGeneratorServiceFactory = HircGeneratorServiceFactory.CreateFactory(bankGeneratorVersion);
        }

        public AudioPackOutput GenerateSoundBankWithoutDialogueEvents(
            SoundBank soundBank)
        {
            var actionEventToHircLookup = new Dictionary<ActionEvent, HircItem>();
            var hircItems = new List<HircItem>();

            if (soundBank.ActionEvents.Count != 0)
            {
                var actionEventHircs = GenerateActionEventHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionEventHircs);

                var actionHircs = GenerateActionHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionHircs);

                var sourceHircsFromPlay = GenerateSourceHircsFromPlayActions(soundBank);
                hircItems.AddRange(sourceHircsFromPlay);
            }

            if (soundBank.DialogueEvents.Count != 0)
            {
                // We don't generate the Dialogue Events in this .bnk, we keep them in the merging SoundBank instead
                var sourceHircsFromDialogue = GenerateSourceHircsFromDialogueEvents(soundBank);
                hircItems.AddRange(sourceHircsFromDialogue);
                AddBattleVoiceActorMixer(soundBank, sourceHircsFromDialogue, hircItems);
            }

            SortHircs(hircItems);

            return WriteSoundBank(
                soundBank.Id,
                soundBank.LanguageId,
                soundBank.FileName,
                soundBank.FilePath,
                hircItems);
        }

        public AudioPackOutput GenerateDialogueEventsForTestingSoundBank(
            SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            var dialogueEventHircs = GenerateDialogueEventHircs(soundBank);
            hircItems.AddRange(dialogueEventHircs);

            var vanillaDialogueEvents = _audioRepository.GetHircsByType<ICAkDialogueEvent>()
                .Select(hircItem => hircItem as HircItem)
                .Where(hircItem => hircItem.IsCAHircItem == true);

            foreach (var hircItem in hircItems)
            {
                var vanillaDialogueEvent = vanillaDialogueEvents.FirstOrDefault(dialogueEvent => dialogueEvent.Id == hircItem.Id) as CAkDialogueEvent_V136;
                var vanillaDecisionTree = vanillaDialogueEvent.AkDecisionTree as AkDecisionTree_V136;

                var compilerDialogueEvent = hircItem as CAkDialogueEvent_V136;
                var compilerDecisionTree = compilerDialogueEvent.AkDecisionTree as AkDecisionTree_V136;

                // Merge the vanilla decision tree into the compiled decision tree so the compiled decision tree takes priority
                var decisionTree = AkDecisionTree_V136.MergeDecisionTrees(compilerDecisionTree.DecisionTree, vanillaDecisionTree.DecisionTree);
                var nodes = AkDecisionTree_V136.FlattenDecisionTree(decisionTree);
                var mergedDecisionTree = new AkDecisionTree_V136
                {
                    DecisionTree = decisionTree,
                    Nodes = nodes
                };
                compilerDialogueEvent.AkDecisionTree = mergedDecisionTree;
                compilerDialogueEvent.TreeDataSize = mergedDecisionTree.GetSize();
                compilerDialogueEvent.UpdateSectionSize();
            }

            SortHircs(hircItems);

            return WriteSoundBank(
                soundBank.TestingId,
                soundBank.LanguageId,
                soundBank.TestingFileName,
                soundBank.TestingFilePath,
                hircItems);
        }

        public AudioPackOutput GenerateMergingSoundBank(
            SoundBank soundBank)
        {
            // We generate all hircs in the merging SoundBank as when merging we check for ID clashes so we can report them to modders
            var actionEventToHircLookup = new Dictionary<ActionEvent, HircItem>();
            var hircItems = new List<HircItem>();

            if (soundBank.ActionEvents.Count != 0)
            {
                var actionEventHircs = GenerateActionEventHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionEventHircs);

                var actionHircs = GenerateActionHircs(soundBank, actionEventToHircLookup);
                hircItems.AddRange(actionHircs);

                var sourceHircsFromPlay = GenerateSourceHircsFromPlayActions(soundBank);
                hircItems.AddRange(sourceHircsFromPlay);
            }

            if (soundBank.DialogueEvents.Count != 0)
            {
                var dialogueEventHircs = GenerateDialogueEventHircs(soundBank);
                hircItems.AddRange(dialogueEventHircs);

                var sourceHircsFromDialogueEvents = GenerateSourceHircsFromDialogueEvents(soundBank);
                hircItems.AddRange(sourceHircsFromDialogueEvents);
            }

            SortHircs(hircItems);

            return WriteSoundBank(
                soundBank.MergingId,
                soundBank.LanguageId,
                soundBank.MergingFileName,
                soundBank.MergingFilePath,
                hircItems);
        }

        public Task<bool> GenerateMergedDialogueEventSoundBanksAsync(
            List<string> moddedSoundBanks,
            string soundBankSuffix,
            CancellationToken cancellationToken) =>
            GenerateMergedDialogueEventSoundBanksAsync(
                moddedSoundBanks,
                soundBankSuffix,
                null,
                cancellationToken);

        public async Task<bool> GenerateMergedDialogueEventSoundBanksAsync(
            List<string> moddedSoundBanks,
            string soundBankSuffix,
            IProgress<AudioOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AudioOperationProgress(
                "AudioOperation.Merge.Analysing",
                soundBankSuffix,
                0,
                moddedSoundBanks.Count));
            var integrityError = await Task.Run(
                () => _audioEditorIntegrityService
                    .GetMergingSoundBanksIntegrityError(
                        moddedSoundBanks,
                        cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (integrityError != null)
                throw new InvalidDataException(integrityError);

            progress?.Report(new AudioOperationProgress(
                "AudioOperation.Merge.Analysing",
                soundBankSuffix,
                moddedSoundBanks.Count,
                moddedSoundBanks.Count));
            progress?.Report(new AudioOperationProgress(
                "AudioOperation.Merge.Creating",
                soundBankSuffix,
                0,
                0));

            var outputs = await Task.Run(
                () => CreateMergedDialogueEventSoundBankOutputs(
                    moddedSoundBanks,
                    soundBankSuffix,
                    cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new AudioOperationProgress(
                "AudioOperation.Merge.Creating",
                soundBankSuffix,
                outputs.Count,
                outputs.Count));
            progress?.Report(new AudioOperationProgress(
                "AudioOperation.Saving",
                soundBankSuffix,
                0,
                outputs.Count));

            var saved = outputs.Count > 0 &&
                _audioPackOutputService.SaveBatch(
                    outputs,
                    promptOnConflict: true);
            if (saved)
            {
                progress?.Report(new AudioOperationProgress(
                    "AudioOperation.Saving",
                    soundBankSuffix,
                    outputs.Count,
                    outputs.Count));
            }

            return saved;
        }

        private List<AudioPackOutput> CreateMergedDialogueEventSoundBankOutputs(
            List<string> moddedSoundBanks,
            string soundBankSuffix,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vanillaDialogueEventsByBnkByLanguage = _audioRepository.GetVanillaDialogueEventsByBnkByLanguage();
            var moddedDialogueEventsByLanguage = _audioRepository.GetModdedDialogueEventsByLanguage(moddedSoundBanks);
            var outputs = new List<AudioPackOutput>();

            var soundBanksToGenerateByLanguage = moddedDialogueEventsByLanguage
                .ToDictionary(
                    languageEntry => languageEntry.Key,
                    languageEntry => languageEntry.Value
                        .Select(hircItem => _audioRepository.GetNameFromId(hircItem.Id))
                        .Select(dialogueEventName => Wh3DialogueEventInformation.GetSoundBank(dialogueEventName))
                        .Distinct()
                        .ToList());
            var expectedTargets = soundBanksToGenerateByLanguage
                .SelectMany(languageEntry => languageEntry.Value.Select(
                    soundBank => (languageEntry.Key, soundBank)))
                .ToHashSet();
            var generatedTargets = new HashSet<(string, Wh3SoundBank)>();

            foreach (var bnkByLanguage in vanillaDialogueEventsByBnkByLanguage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var language = bnkByLanguage.Key;
                if (!moddedDialogueEventsByLanguage.ContainsKey(language))
                    continue;

                var soundBanksToGenerate = soundBanksToGenerateByLanguage[language];
                foreach (var hircsByBnk in bnkByLanguage.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var firstDialogueEventName = hircsByBnk.Value.Select(hircItem => _audioRepository.GetNameFromId(hircItem.Id)).FirstOrDefault();
                    var currentSoundBank = Wh3DialogueEventInformation.GetSoundBank(firstDialogueEventName);
                    if (!soundBanksToGenerate.Contains(currentSoundBank))
                        continue;
                    if (!generatedTargets.Add((language, currentSoundBank)))
                    {
                        throw new InvalidDataException(
                            LocalizationManager.Instance.Get(
                                "DialogueEventMerger.IncompleteOutput"));
                    }

                    _logger.Here().Information($"Merging SoundBanks for language {language}");

                    outputs.Add(
                        GenerateMergedDialogueEventSoundBank(
                            hircsByBnk.Value,
                            moddedDialogueEventsByLanguage,
                            language,
                            currentSoundBank,
                            soundBankSuffix,
                            cancellationToken));
                }
            }

            if (!generatedTargets.SetEquals(expectedTargets))
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "DialogueEventMerger.IncompleteOutput"));
            }

            return outputs;
        }

        private List<HircItem> GenerateActionEventHircs(SoundBank soundBank, Dictionary<ActionEvent, HircItem> actionEventToHircLookup)
        {
            var hircItems = new List<HircItem>();
            foreach (var actionEvent in soundBank.ActionEvents)
            {
                var actionEventHirc = _hircGeneratorServiceFactory.GenerateHirc(actionEvent);
                hircItems.Add(actionEventHirc);
                actionEventToHircLookup[actionEvent] = actionEventHirc;
            }
            return hircItems;
        }

        private List<HircItem> GenerateActionHircs(SoundBank soundBank, Dictionary<ActionEvent, HircItem> actionEventToHircLookup)
        {
            var hircItems = new List<HircItem>();

            foreach (var actionEvent in soundBank.ActionEvents)
            {
                if (actionEvent.Actions.Count > 1)
                    throw new NotSupportedException("Multiple Actions are not supported");

                var actionEventHirc = actionEventToHircLookup[actionEvent];

                foreach (var action in actionEvent.Actions)
                {
                    var actionHirc = _hircGeneratorServiceFactory.GenerateHirc(action, soundBank);
                    hircItems.Add(actionHirc);

                    if (actionEventHirc.HircChildren == null)
                        actionEventHirc.HircChildren = [];
                    actionEventHirc.HircChildren.Add(actionHirc);
                }
            }

            return hircItems;
        }

        private List<HircItem> GenerateSourceHircsFromPlayActions(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            foreach (var actionEvent in soundBank.ActionEvents)
            {
                foreach (var action in actionEvent.Actions)
                {
                    if (action.ActionType == AkActionType.Play)
                    {
                        if (action.TargetHircTypeIsSound())
                        {
                            var sound = soundBank.GetSound(action.TargetHircId);
                            var soundTargetHircs = GenerateSoundTargetHircs(soundBank, sound);
                            hircItems.AddRange(soundTargetHircs);
                        }
                        else if (action.TargetHircTypeIsRandomSequenceContainer())
                        {
                            var randomSequenceContainer = soundBank.GetRandomSequenceContainer(action.TargetHircId);
                            var randomSequenceContainerTargetHircs = GenerateRandomSequenceContainerTargetHircs(soundBank, randomSequenceContainer);
                            hircItems.AddRange(randomSequenceContainerTargetHircs);
                        }
                    }
                }
            }

            return hircItems;
        }

        private List<HircItem> GenerateDialogueEventHircs(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();
            foreach (var dialogueEvent in soundBank.DialogueEvents)
            {
                var dialogueEventHirc = _hircGeneratorServiceFactory.GenerateHirc(dialogueEvent);
                hircItems.Add(dialogueEventHirc);
            }
            return hircItems;
        }

        private List<HircItem> GenerateSourceHircsFromDialogueEvents(SoundBank soundBank)
        {
            var hircItems = new List<HircItem>();

            foreach (var dialogueEvent in soundBank.DialogueEvents)
            {
                foreach (var statePath in dialogueEvent.StatePaths)
                {

                    if (statePath.TargetHircTypeIsSound())
                    {
                        var sound = soundBank.GetSound(statePath.TargetHircId);
                        var soundTargetHircs = GenerateSoundTargetHircs(soundBank, sound);
                        hircItems.AddRange(soundTargetHircs);
                    }
                    else if (statePath.TargetHircTypeIsRandomSequenceContainer())
                    {
                        var randomSequenceContainer = soundBank.GetRandomSequenceContainer(statePath.TargetHircId);
                        var randomSequenceContainerTargetHircs = GenerateRandomSequenceContainerTargetHircs(soundBank, randomSequenceContainer);
                        hircItems.AddRange(randomSequenceContainerTargetHircs);
                    }
                }
            }

            return hircItems;
        }

        private List<HircItem> GenerateSoundTargetHircs(SoundBank soundBank, Sound sound)
        {
            var hircItems = new List<HircItem>();

            var soundHirc = _hircGeneratorServiceFactory.GenerateHirc(sound);
            soundHirc.IsTarget = true;
            hircItems.Add(soundHirc);
            return hircItems;
        }

        private void AddBattleVoiceActorMixer(
            SoundBank soundBank,
            List<HircItem> sourceHircs,
            List<HircItem> hircItems)
        {
            var actorMixerId = soundBank.GameSoundBank switch
            {
                Wh3SoundBank.BattleVO => Wh3ActorMixerInformation.BattleVOOrders,
                Wh3SoundBank.BattleVOConversational => Wh3ActorMixerInformation.BattleVOConversational,
                _ => 0u
            };

            if (actorMixerId == 0)
                return;

            var childIds = sourceHircs
                .Where(hircItem => hircItem.IsTarget)
                .Select(hircItem => hircItem.Id)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            if (childIds.Count == 0)
                return;

            var template = _audioRepository.GetHircs(actorMixerId)
                .OfType<CAkActorMixer_V136>()
                .FirstOrDefault(hircItem => hircItem.IsCAHircItem);
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"Could not find the vanilla ActorMixer {actorMixerId} required to compile spatial battle dialogue audio.");
            }

            var actorMixer = new CAkActorMixer_V136
            {
                HircType = AkBkHircType.ActorMixer,
                Id = actorMixerId,
                LanguageId = soundBank.LanguageId,
                BnkFilePath = soundBank.FilePath,
                NodeBaseParams = template.NodeBaseParams,
                Children = new Children_V136 { ChildIds = childIds }
            };
            actorMixer.UpdateSectionSize();
            hircItems.Add(actorMixer);
        }

        private List<HircItem> GenerateRandomSequenceContainerTargetHircs(SoundBank soundBank, RandomSequenceContainer randomSequenceContainer)
        {
            var hircItems = new List<HircItem>();

            var randomSequenceContainerHirc = _hircGeneratorServiceFactory.GenerateHirc(randomSequenceContainer, soundBank);
            hircItems.Add(randomSequenceContainerHirc);

            randomSequenceContainerHirc.IsTarget = true;
            if (randomSequenceContainerHirc.HircChildren == null)
                randomSequenceContainerHirc.HircChildren = [];

            var sounds = soundBank.GetSounds(randomSequenceContainer.Children);
            foreach (var sound in sounds)
            {
                var soundHirc = _hircGeneratorServiceFactory.GenerateHirc(sound);
                hircItems.Add(soundHirc);
                randomSequenceContainerHirc.HircChildren.Add(soundHirc);
            }

            return hircItems;
        }

        private AudioPackOutput GenerateMergedDialogueEventSoundBank(
            List<HircItem> vanillaHircs,
            Dictionary<string, List<HircItem>> moddedDialogueEventsByLanguage,
            string language,
            Wh3SoundBank currentSoundBank, 
            string soundBankSuffix,
            CancellationToken cancellationToken)
        {
            var dialogueEvents = new List<HircItem>();
            var targetHircsById = new Dictionary<uint, HircItem>();

            var soundBankNameBase = Wh3SoundBankInformation.GetName(currentSoundBank);
            var soundBankNameWithoutExtension = $"{soundBankNameBase}_0_{soundBankSuffix}";
            var soundBankFileName = $"{soundBankNameWithoutExtension}.bnk";

            var soundBankFilePath = $"audio\\wwise\\{language}\\{soundBankFileName}";
            if (language == Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx))
                soundBankFilePath = $"audio\\wwise\\{soundBankFileName}";

            var soundBankId = WwiseHash.Compute(soundBankNameWithoutExtension);
            var languageId = WwiseHash.Compute(language);

            _logger.Here().Information($"Merging Dialogue Events for SoundBank {soundBankFilePath}");

            foreach (var vanillaHirc in vanillaHircs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Clone it as we shouldn't modify the original
                var vanillaDialogueEvent = vanillaHirc as CAkDialogueEvent_V136;
                var mergedDialogueEvent = vanillaDialogueEvent.Clone();

                var matchingModdedDialogueEvents = moddedDialogueEventsByLanguage[language]
                    .Where(moddedHircItem => moddedHircItem.Id == vanillaDialogueEvent.Id)
                    .ToList();

                if (matchingModdedDialogueEvents.Count == 0)
                    continue;

                _logger.Here().Information($"Merging Dialogue Event {_audioRepository.GetNameFromId(vanillaHirc.Id)}");

                foreach (var moddedDialogueEventHirc in matchingModdedDialogueEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.Here().Information($"Merging decision tree from {Path.GetFileName(moddedDialogueEventHirc.BnkFilePath)}");
                    AddDialogueTargetHircs(
                        moddedDialogueEventHirc,
                        targetHircsById,
                        cancellationToken);

                    var currentDecisionTree = mergedDialogueEvent.AkDecisionTree as AkDecisionTree_V136;
                    var moddedDialogueEvent = moddedDialogueEventHirc as CAkDialogueEvent_V136;
                    var moddedDecisionTree = moddedDialogueEvent.AkDecisionTree as AkDecisionTree_V136;

                    var mergedDecisionTree = new AkDecisionTree_V136();
                    mergedDecisionTree.DecisionTree = AkDecisionTree_V136.MergeDecisionTrees(currentDecisionTree.DecisionTree, moddedDecisionTree.DecisionTree);
                    mergedDecisionTree.Nodes = AkDecisionTree_V136.FlattenDecisionTree(mergedDecisionTree.DecisionTree);

                    mergedDialogueEvent.AkDecisionTree = mergedDecisionTree;
                    mergedDialogueEvent.TreeDataSize = mergedDecisionTree.GetSize();
                    mergedDialogueEvent.UpdateSectionSize();
                }

                dialogueEvents.Add(mergedDialogueEvent);
            }

            SortHircs(dialogueEvents);
            var hircItems = OrderTargetHircs(
                    targetHircsById,
                    cancellationToken)
                .Concat(dialogueEvents)
                .ToList();

            return WriteSoundBank(
                soundBankId,
                languageId,
                soundBankFileName,
                soundBankFilePath,
                hircItems);
        }

        private void AddDialogueTargetHircs(
            HircItem dialogueEventHirc,
            Dictionary<uint, HircItem> targetHircsById,
            CancellationToken cancellationToken)
        {
            var dialogueEvent = (ICAkDialogueEvent)dialogueEventHirc;
            foreach (var targetId in GetDialogueTargetIds(
                         dialogueEvent.AkDecisionTree.GetDecisionTree()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddReachableTargetHirc(
                    targetId,
                    dialogueEventHirc.BnkFilePath,
                    targetHircsById,
                    [],
                    cancellationToken);
            }
        }

        private void AddReachableTargetHirc(
            uint hircId,
            string soundBankPath,
            Dictionary<uint, HircItem> targetHircsById,
            HashSet<uint> visitingIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visitingIds.Contains(hircId))
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "DialogueEventMerger.IncompleteOutput"));
            }
            if (targetHircsById.ContainsKey(hircId))
                return;

            visitingIds.Add(hircId);

            var targetHirc = _audioRepository
                .GetHircs(hircId, soundBankPath)
                .FirstOrDefault(hircItem =>
                    hircItem is not ICAkDialogueEvent);
            if (targetHirc == null)
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "DialogueEventMerger.IncompleteOutput"));
            }
            if (targetHirc is not ICAkSound &&
                targetHirc is not ICAkRanSeqCntr)
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.Get(
                        "DialogueEventMerger.IncompleteOutput"));
            }

            targetHircsById[hircId] = targetHirc;
            if (targetHirc is ICAkRanSeqCntr randomSequenceContainer)
            {
                foreach (var childId in randomSequenceContainer
                             .GetChildren()
                             .OrderBy(id => id))
                {
                    AddReachableTargetHirc(
                        childId,
                        soundBankPath,
                        targetHircsById,
                        visitingIds,
                        cancellationToken);
                }
            }

            visitingIds.Remove(hircId);
        }

        private static List<HircItem> OrderTargetHircs(
            IReadOnlyDictionary<uint, HircItem> targetHircsById,
            CancellationToken cancellationToken)
        {
            var orderedHircs = new List<HircItem>(targetHircsById.Count);
            var addedIds = new HashSet<uint>();
            foreach (var hircItem in targetHircsById.Values
                         .OrderBy(item => item.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddOrderedTargetHirc(
                    hircItem,
                    targetHircsById,
                    addedIds,
                    orderedHircs,
                    cancellationToken);
            }

            return orderedHircs;
        }

        private static void AddOrderedTargetHirc(
            HircItem hircItem,
            IReadOnlyDictionary<uint, HircItem> targetHircsById,
            HashSet<uint> addedIds,
            List<HircItem> orderedHircs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (addedIds.Contains(hircItem.Id))
                return;
            if (hircItem is ICAkRanSeqCntr randomSequenceContainer)
            {
                foreach (var childId in randomSequenceContainer
                             .GetChildren()
                             .OrderBy(id => id))
                {
                    if (targetHircsById.TryGetValue(
                            childId,
                            out var childHirc))
                    {
                        AddOrderedTargetHirc(
                            childHirc,
                            targetHircsById,
                            addedIds,
                            orderedHircs,
                            cancellationToken);
                    }
                }
            }

            if (addedIds.Add(hircItem.Id))
                orderedHircs.Add(hircItem);
        }

        private static IEnumerable<uint> GetDialogueTargetIds(
            ICAkDialogueEvent.IAkDecisionNode node)
        {
            if (node.GetChildrenCount() == 0)
            {
                if (node.GetAudioNodeId() != 0)
                    yield return node.GetAudioNodeId();
                yield break;
            }

            for (var index = 0;
                 index < node.GetChildrenCount();
                 index++)
            {
                foreach (var targetId in GetDialogueTargetIds(
                             node.GetChildAtIndex(index)))
                {
                    yield return targetId;
                }
            }
        }

        private static void SortHircs(List<HircItem> hircItems)
        {
            var targetHircs = hircItems
                .Where(hircItem => hircItem.IsTarget)
                .ToList();

            var eventHircs = hircItems
                .Where(hircItem => hircItem.HircType == AkBkHircType.Event || hircItem.HircType == AkBkHircType.Dialogue_Event)
                .ToList();

            var actorMixerHircs = hircItems
                .Where(hircItem => hircItem.HircType == AkBkHircType.ActorMixer)
                .ToList();

            var sortedHircItems = new List<HircItem>();

            foreach (var targetHirc in targetHircs.OrderBy(sourceHirc => sourceHirc.Id))
            {
                if (targetHirc.HircType == AkBkHircType.RandomSequenceContainer)
                {
                    var soundHircs = targetHirc.HircChildren.OrderBy(soundHirc => soundHirc.Id);
                    sortedHircItems.AddRange(soundHircs);
                    sortedHircItems.Add(targetHirc);
                }
                else
                    sortedHircItems.Add(targetHirc);
            }

            sortedHircItems.AddRange(actorMixerHircs.OrderBy(hircItem => hircItem.Id));

            foreach (var eventHirc in eventHircs.OrderBy(eventHirc => eventHirc.Id))
            {
                if (eventHirc.HircType == AkBkHircType.Event)
                {
                    var actionHircs = eventHirc.HircChildren.OrderBy(eventHirc => eventHirc.Id);
                    sortedHircItems.AddRange(actionHircs);
                    sortedHircItems.Add(eventHirc);
                }
                else
                    sortedHircItems.Add(eventHirc);
            }

            hircItems.Clear();
            hircItems.AddRange(sortedHircItems);
        }

        private AudioPackOutput WriteSoundBank(
            uint id,
            uint languageId,
            string fileName,
            string filePath,
            List<HircItem> hircItems)
        {
            var gameInformation = GameInformationDatabase.GetGameById(_applicationSettingsService.CurrentSettings.CurrentGame);
            var bankGeneratorVersion = (uint)gameInformation.BankGeneratorVersion;
            var wwiseProjectId = (uint)gameInformation.WwiseProjectId;

            var bkhdChunk = BkhdChunkGenerator.GenerateBkhdChunk(bankGeneratorVersion, id, languageId, wwiseProjectId);
            var bkhdChunkBytes = BkhdChunk.WriteData(bkhdChunk);

            var hircChunk = HircChunkGenerator.GenerateHircChunk(hircItems);
            var hircChunkBytes = HircChunk.WriteData(hircChunk, bankGeneratorVersion);

            using var memStream = new MemoryStream();
            memStream.Write(bkhdChunkBytes);
            memStream.Write(hircChunkBytes);
            var bytes = memStream.ToArray();

            var bnkPackFile = new PackFile(fileName, new MemorySource(bytes));
            var reparsedSanityFile = BnkParser.Parse(bnkPackFile, "test\\fakefilename.bnk", true);

            return new AudioPackOutput(
                fileName,
                filePath,
                bnkPackFile.DataSource.ReadData());
        }
    }
}
