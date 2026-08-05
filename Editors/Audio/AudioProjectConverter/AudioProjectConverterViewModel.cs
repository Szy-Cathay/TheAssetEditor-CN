using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.HircExploration;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.AudioProjectConverter
{
    public partial class AudioProjectConverterViewModel : ObservableObject
    {
        private readonly IStandardDialogs _standardDialogs;
        private readonly IAudioPackOutputService _audioPackOutputService;
        private readonly IAudioRepository _audioRepository;
        private readonly IAudioEditorFileService _audioEditorFileService;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly VgStreamWrapper _vgStreamWrapper;

        private readonly ILogger _logger = Logging.Create<AudioProjectConverterViewModel>();
        private System.Action _closeAction;

        [ObservableProperty] private string _audioProjectName;
        [ObservableProperty] private string _outputDirectoryPath;
        [ObservableProperty] private string _wemsDirectoryPath;
        [ObservableProperty] private string _bnksDirectoryPath;
        [ObservableProperty] private string _vOActorSubstring;
        [ObservableProperty] private string _soundbanksInfoXmlPath;
        [ObservableProperty] private bool _isUsingWwiseProject;

        [ObservableProperty] private bool _isAudioProjectNameSet;
        [ObservableProperty] private bool _isOutputDirectoryPathSet;
        [ObservableProperty] private bool _isWemsDirectoryPathSet; 
        [ObservableProperty] private bool _isBnksDirectoryPathSet;
        [ObservableProperty] private bool _isVOActorSubstringSet;
        [ObservableProperty] private bool _isSoundbanksInfoXmlSet;
        [ObservableProperty] private bool _isOkButtonEnabled;
        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private bool _isProcessing;
        [ObservableProperty] private string _status =
            LocalizationManager.Instance.Get(
                "AudioProjectConverter.Loading");
        [ObservableProperty] private string _progressDetail = string.Empty;
        [ObservableProperty] private int _progressValue;
        [ObservableProperty] private int _progressMaximum;
        [ObservableProperty] private bool _progressIsIndeterminate = true;
        private bool _isInitialised;
        private bool _isRepositoryLoaded;
        private CancellationToken _lifetimeCancellationToken;

        public bool IsBusy => IsLoading || IsProcessing;

        public AudioProjectConverterViewModel(
            IStandardDialogs standardDialogs,
            IAudioPackOutputService audioPackOutputService,
            IAudioRepository audioRepository,
            IAudioEditorFileService audioEditorFileService,
            ApplicationSettingsService applicationSettingsService,
            VgStreamWrapper vgStreamWrapper)
        {
            _standardDialogs = standardDialogs;
            _audioPackOutputService = audioPackOutputService;
            _audioRepository = audioRepository;
            _audioEditorFileService = audioEditorFileService;
            _applicationSettingsService = applicationSettingsService;
            _vgStreamWrapper = vgStreamWrapper;

            OutputDirectoryPath = "audio\\audio_projects";
        }

        public async Task InitializeAsync(
            CancellationToken cancellationToken)
        {
            _lifetimeCancellationToken = cancellationToken;
            if (_isInitialised)
                return;

            _isInitialised = true;
            IsLoading = true;
            Status = LocalizationManager.Instance.Get(
                "AudioProjectConverter.Loading");
            ProgressDetail = Status;
            ProgressValue = 0;
            ProgressMaximum = 0;
            ProgressIsIndeterminate = true;
            UpdateOkButtonIsEnabled();
            try
            {
                var progress = new Progress<AudioLoadProgress>(value =>
                {
                    ProgressDetail = Path.GetFileName(value.CurrentFile);
                    ProgressValue = value.Completed;
                    ProgressMaximum = value.Total;
                    ProgressIsIndeterminate = value.Total <= 0;
                });
                await Task.Run(
                    () => _audioRepository.Load(
                        [
                            Wh3LanguageInformation.GetLanguageAsString(
                                Wh3Language.EnglishUK)
                        ],
                        progress,
                        cancellationToken),
                    cancellationToken);
                _isRepositoryLoaded = true;
                Status = string.Empty;
            }
            catch (OperationCanceledException)
            {
                Status = LocalizationManager.Instance.Get(
                    "AudioProjectConverter.Cancelled");
            }
            catch (Exception exception)
            {
                Status = LocalizationManager.Instance.Get(
                    "AudioProjectConverter.LoadFailed");
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "AudioProjectConverter.LoadFailed.Detail",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            finally
            {
                IsLoading = false;
                UpdateOkButtonIsEnabled();
            }
        }

        [RelayCommand]
        public async Task ProcessAudioProjectConversionAsync(
            CancellationToken cancellationToken)
        {
            if (!_isRepositoryLoaded || IsBusy)
                return;

            var currentGame = _applicationSettingsService.CurrentSettings.CurrentGame;
            if (currentGame != GameTypeEnum.Warhammer3)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "Msg.AudioEditorUnsupportedGame"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            if (!AudioProjectNameValidator.TryNormalize(
                    AudioProjectName,
                    out var normalizedName))
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "NewAudioProject.InvalidName"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            var voActorSubstrings = GetVOActorSubstrings(
                VOActorSubstring);
            if (voActorSubstrings.Length == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "AudioProjectConverter.InvalidVOActorSubstring"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            if (!Directory.Exists(WemsDirectoryPath) ||
                !Directory.Exists(BnksDirectoryPath))
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "AudioProjectConverter.InvalidDirectories"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            if (IsUsingWwiseProject &&
                !File.Exists(SoundbanksInfoXmlPath))
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "AudioProjectConverter.InvalidSoundbanksInfo"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            var fileName = $"{normalizedName}.aproj";
            var filePath = $"{OutputDirectoryPath}\\{fileName}";
            var soundBankPaths = Directory
                .GetFiles(
                    BnksDirectoryPath,
                    "*.bnk",
                    SearchOption.TopDirectoryOnly)
                .ToList();
            if (soundBankPaths.Count == 0)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "AudioProjectConverter.NoSoundBanks"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            var workspacePath = Path.Combine(
                DirectoryHelper.Temp,
                "Audio",
                $"converter-{Guid.NewGuid():N}");
            using var linkedCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellationToken);
            IsProcessing = true;
            Status = LocalizationManager.Instance.Get(
                "AudioProjectConverter.Processing");
            ProgressDetail = Status;
            ProgressValue = 0;
            ProgressMaximum = 0;
            ProgressIsIndeterminate = true;
            try
            {
                DirectoryHelper.EnsureCreated(workspacePath);
                var progress = new Progress<AudioOperationProgress>(
                    UpdateOperationProgress);
                var outputs = await Task.Run(
                    () => CreateConversionOutputs(
                        normalizedName,
                        fileName,
                        filePath,
                        soundBankPaths,
                        workspacePath,
                        voActorSubstrings,
                        progress,
                        linkedCancellation.Token),
                    linkedCancellation.Token);
                linkedCancellation.Token.ThrowIfCancellationRequested();

                UpdateOperationProgress(new AudioOperationProgress(
                    "AudioOperation.Saving",
                    filePath,
                    0,
                    outputs.Count));
                if (_audioPackOutputService.SaveBatch(
                        outputs,
                        true))
                {
                    UpdateOperationProgress(new AudioOperationProgress(
                        "AudioOperation.Saving",
                        filePath,
                        outputs.Count,
                        outputs.Count));
                    Status = string.Empty;
                    CloseWindowAction();
                }
            }
            catch (OperationCanceledException)
            {
                Status = LocalizationManager.Instance.Get(
                    "AudioProjectConverter.Cancelled");
            }
            catch (Exception exception)
            {
                _logger.Here().Error(
                    exception,
                    "Audio Project conversion failed");
                Status = LocalizationManager.Instance.Get(
                    "AudioProjectConverter.Failed");
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "AudioProjectConverter.Failed.Detail",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            finally
            {
                TryDeleteWorkspace(workspacePath);
                IsProcessing = false;
            }
        }

        private List<AudioPackOutput> CreateConversionOutputs(
            string normalizedName,
            string fileName,
            string filePath,
            List<string> soundBankPaths,
            string workspacePath,
            string[] voActorSubstrings,
            IProgress<AudioOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            const string language = "english(uk)";
            var audioProject = AudioProjectFile.CreateNew(
                language,
                normalizedName);
            var statesLookupByStateGroupByStateId = new Dictionary<string, Dictionary<uint, string>>();
            var dialogueEventsLookupByWemId = new Dictionary<uint, List<string>>();
            var statePathsLookupByDialogueEvent = new Dictionary<string, List<StatePathInfo>>();
            var dialogueEventsToProcess = new List<ICAkDialogueEvent>();
            var moddedStateGroups = new Dictionary<string, List<string>>();
            var globalBaseNameUsage = new Dictionary<string, int>();

            progress.Report(new AudioOperationProgress(
                "AudioOperation.Convert.Parsing",
                Path.GetFileName(soundBankPaths[0]),
                0,
                soundBankPaths.Count));
            var hircItems = GetHircItems(
                soundBankPaths,
                cancellationToken);
            progress.Report(new AudioOperationProgress(
                "AudioOperation.Convert.Parsing",
                Path.GetFileName(soundBankPaths[^1]),
                soundBankPaths.Count,
                soundBankPaths.Count));
            var hircLookupById = BuildHircLookupById(
                hircItems,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(SoundbanksInfoXmlPath))
            {
                BuildStateGroupStateLookup(
                    statesLookupByStateGroupByStateId,
                    cancellationToken);
            }

            var dialogueEvents = hircItems.OfType<ICAkDialogueEvent>().ToList();
            for (var dialogueEventIndex = 0;
                 dialogueEventIndex < dialogueEvents.Count;
                 dialogueEventIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialogueEvent = dialogueEvents[dialogueEventIndex];
                SetDialogueEventData(
                    dialogueEvent,
                    dialogueEventsLookupByWemId,
                    statePathsLookupByDialogueEvent,
                    statesLookupByStateGroupByStateId,
                    hircLookupById,
                    dialogueEventsToProcess,
                    moddedStateGroups,
                    voActorSubstrings,
                    cancellationToken);
                progress.Report(new AudioOperationProgress(
                    "AudioOperation.Convert.Analysing",
                    $"{dialogueEventIndex + 1} / {dialogueEvents.Count}",
                    dialogueEventIndex + 1,
                    dialogueEvents.Count));
            }

            var usedHircIds = IdGenerator.GetUsedHircIds(_audioRepository, audioProject);
            var usedSourceIds = IdGenerator.GetUsedSourceIds(_audioRepository, audioProject);
            var outputs = new List<AudioPackOutput>();

            for (var dialogueEventIndex = 0;
                 dialogueEventIndex < dialogueEventsToProcess.Count;
                 dialogueEventIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialogueEvent =
                    dialogueEventsToProcess[dialogueEventIndex];
                ProcessDialogueEvent(
                    audioProject,
                    dialogueEvent,
                    dialogueEventsLookupByWemId,
                    statePathsLookupByDialogueEvent,
                    globalBaseNameUsage,
                    usedHircIds,
                    usedSourceIds,
                    outputs,
                    workspacePath,
                    voActorSubstrings,
                    cancellationToken);
                progress.Report(new AudioOperationProgress(
                    "AudioOperation.Convert.Creating",
                    $"{dialogueEventIndex + 1} / {dialogueEventsToProcess.Count}",
                    dialogueEventIndex + 1,
                    dialogueEventsToProcess.Count));
            }

            ProcessModdedStateGroups(audioProject, moddedStateGroups);
            outputs.Add(_audioEditorFileService.CreateOutput(
                audioProject,
                fileName,
                filePath));
            return outputs;
        }

        private void UpdateOperationProgress(AudioOperationProgress progress)
        {
            Status = LocalizationManager.Instance.Get(
                progress.StageResourceKey);
            ProgressDetail = progress.Detail;
            ProgressValue = progress.Completed;
            ProgressMaximum = progress.Total;
            ProgressIsIndeterminate = progress.Total <= 0;
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
                    "Failed to clean the converter workspace {WorkspacePath}",
                    workspacePath);
            }
        }

        private static Dictionary<uint, List<HircItem>> BuildHircLookupById(
            List<HircItem> hircItems,
            CancellationToken cancellationToken)
        {
            var hircLookupById = new Dictionary<uint, List<HircItem>>();
            foreach (var hircItem in hircItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!hircLookupById.TryGetValue(hircItem.Id, out var hircItemList))
                {
                    hircItemList = [];
                    hircLookupById.TryAdd(hircItem.Id, hircItemList);
                }
                hircItemList.Add(hircItem);
            }

            return hircLookupById;
        }

        private static List<HircItem> GetHircItems(
            List<string> soundBankPaths,
            CancellationToken cancellationToken)
        {
            var parsedSoundBanks = new List<ParsedBnkFile>();

            foreach (var soundBankPath in soundBankPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var soundBankDataBytes = File
                    .ReadAllBytesAsync(
                        soundBankPath,
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                var soundBankPackFile = PackFile.CreateFromBytes(soundBankPath, soundBankDataBytes);
                var parsedSoundBank = BnkParser.Parse(soundBankPackFile, soundBankPath, false);
                parsedSoundBanks.Add(parsedSoundBank);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var hircItems = parsedSoundBanks
                .SelectMany(soundBank => soundBank.HircChunk.HircItems)
                .ToList();

            return hircItems;
        }

        private Dictionary<string, Dictionary<uint, string>>
            BuildStateGroupStateLookup(
                Dictionary<string, Dictionary<uint, string>>
                    stateLookupByStateGroup,
                CancellationToken cancellationToken)
        {
            var soundBanksInfo = XDocument.Load(SoundbanksInfoXmlPath);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var stateGroup in soundBanksInfo.Descendants("StateGroup"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stateGroupName = stateGroup.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(stateGroupName))
                    continue;

                if (!stateLookupByStateGroup.TryGetValue(stateGroupName, out var stateLookupByStateId))
                {
                    stateLookupByStateId = [];
                    stateLookupByStateGroup.TryAdd(stateGroupName, stateLookupByStateId);
                }

                foreach (var state in stateGroup.Element("States")?.Elements("State") ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stateName = state.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(stateName))
                    {
                        var stateHash = WwiseHash.Compute(stateName);
                        stateLookupByStateId.TryAdd(stateHash, stateName);
                    }
                }
            }

            return stateLookupByStateGroup;
        }

        private void SetDialogueEventData(
            ICAkDialogueEvent dialogueEvent,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<string, List<StatePathInfo>> statePathsLookupByDialogueEvent,
            Dictionary<string, Dictionary<uint, string>> statesLookupByStateGroupByStateId,
            Dictionary<uint, List<HircItem>> hircLookupById,
            List<ICAkDialogueEvent> dialogueEventsToProcess,
            Dictionary<string, List<string>> moddedStateGroups,
            string[] voActorSubstrings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stateGroup = "VO_Actor";
            var statesLookupByStateId = GetStatesLookupByStateId(
                statesLookupByStateGroupByStateId,
                stateGroup,
                cancellationToken);

            var dialogueEventHirc = dialogueEvent as HircItem;
            var dialogueEventName = _audioRepository.GetNameFromId(dialogueEventHirc.Id);

            var statePathParser = new StatePathParser(_audioRepository);
            var result = statePathParser.GetStatePaths(dialogueEvent);
            cancellationToken.ThrowIfCancellationRequested();

            var anyStatePathsContainRequiredPattern = result.StatePaths
                .Any(statePath => statePath.Items.Any(state =>
                    statesLookupByStateId.TryGetValue(state.Value, out var stateName) &&
                    voActorSubstrings.Any(substring => stateName.Contains(substring, StringComparison.CurrentCultureIgnoreCase))));

            if (!anyStatePathsContainRequiredPattern)
                return;

            // Store the dialogue event for use later
            dialogueEventsToProcess.Add(dialogueEvent);

            foreach (var statePath in result.StatePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var statePathContainsRequiredPattern = statePath.Items.Any(state => statesLookupByStateId.TryGetValue(state.Value, out var stateName) 
                    && voActorSubstrings.Any(substring => stateName.Contains(substring, StringComparison.CurrentCultureIgnoreCase)));

                if (!statePathContainsRequiredPattern)
                    continue;

                var wavFiles = new List<WavFile>();
                var statePathNodes = new List<StatePath.Node>();

                StoreStateGroupAndStateInfo(
                    dialogueEvent,
                    statesLookupByStateId,
                    statesLookupByStateGroupByStateId,
                    statePath,
                    statePathNodes,
                    moddedStateGroups,
                    cancellationToken);

                StoreDialogueEventsLookupByWemId(
                    dialogueEventsLookupByWemId,
                    hircLookupById,
                    wavFiles,
                    dialogueEventName,
                    statePath,
                    cancellationToken);

                StoreStatePathInfo(statePathsLookupByDialogueEvent, wavFiles, dialogueEventName, statePathNodes);
            }
        }

        private Dictionary<uint, string> GetStatesLookupByStateId(
            Dictionary<string, Dictionary<uint, string>>
                statesLookupByStateGroupByStateId,
            string stateGroupLookup,
            CancellationToken cancellationToken)
        {
            statesLookupByStateGroupByStateId.TryGetValue(stateGroupLookup, out var statesInfoFromWwiseProject);

            var statesLookupByStateId = new Dictionary<uint, string>();

            if (statesInfoFromWwiseProject != null)
            {
                foreach (var stateInfo in statesInfoFromWwiseProject)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    statesLookupByStateId.TryAdd(stateInfo.Key, stateInfo.Value);
                }
            }

            // Not all names are States but that doesn't matter
            foreach (var stateInfo in _audioRepository.NameById)
            {
                cancellationToken.ThrowIfCancellationRequested();
                statesLookupByStateId.TryAdd(stateInfo.Key, stateInfo.Value);
            }

            return statesLookupByStateId;
        }

        private void StoreStateGroupAndStateInfo(
            ICAkDialogueEvent dialogueEvent,
            Dictionary<uint, string> wwiseStatesIdLookup,
            Dictionary<string, Dictionary<uint, string>> statesLookupByStateGroupByStateId,
            StatePathParser.StatePath statePath,
            List<StatePath.Node> statePathNodes,
            Dictionary<string, List<string>> moddedStateGroups,
            CancellationToken cancellationToken)
        {
            var stateGroupIndex = 0;

            foreach (var statePathItem in statePath.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stateGroup = _audioRepository.GetNameFromId(dialogueEvent.Arguments[stateGroupIndex].GroupId);
                var statesLookupByStateId = GetStatesLookupByStateId(
                    statesLookupByStateGroupByStateId,
                    stateGroup,
                    cancellationToken);
                var state = string.Empty;

                if (statePathItem.Value == 0)
                    state = "Any";
                else
                {
                    if (statesLookupByStateId.TryGetValue(statePathItem.Value, out var unhashedState))
                        state = unhashedState;
                }

                var audioProjectstateGroup = StateGroup.CreateForStatePath(stateGroup);
                var audioProjectstate = new State(state);
                var statePathNode = new StatePath.Node(audioProjectstateGroup, audioProjectstate);
                statePathNodes.Add(statePathNode);

                // Store modded states info
                if (state != "Any" && !_audioRepository.NameById.ContainsValue(state))
                {
                    if (moddedStateGroups.TryGetValue(stateGroup, out var stateList))
                    {
                        if (!stateList.Contains(state))
                            stateList.Add(state);
                    }
                    else
                        moddedStateGroups.TryAdd(stateGroup, [state]);
                }

                stateGroupIndex++;
            }
        }

        private static void StoreDialogueEventsLookupByWemId(
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<uint, List<HircItem>> hircLookupById,
            List<WavFile> wavFiles,
            string dialogueEventName,
            StatePathParser.StatePath statePath,
            CancellationToken cancellationToken)
        {
            ProcessHircItem(
                statePath.ChildNodeId,
                dialogueEventsLookupByWemId,
                hircLookupById,
                wavFiles,
                dialogueEventName,
                [],
                cancellationToken);
        }

        private static void ProcessHircItem(
            uint childNodeId,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<uint, List<HircItem>> hircLookupById,
            List<WavFile> wavFiles,
            string dialogueEventName,
            HashSet<uint> visitedHircIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visitedHircIds.Add(childNodeId))
                return;

            if (!hircLookupById.TryGetValue(childNodeId, out var hircItems) || hircItems.Count == 0)
                return;

            var hircItem = hircItems.First();

            if (hircItem is ICAkSound soundHirc)
            {
                var wavFile = new WavFile()
                {
                    WemId = soundHirc.GetSourceId(),
                    DialogueEvent = dialogueEventName
                };
                wavFiles.Add(wavFile);

                if (dialogueEventsLookupByWemId.TryGetValue(wavFile.WemId, out var dialogueEventList))
                {
                    if (!dialogueEventList.Contains(dialogueEventName))
                        dialogueEventList.Add(dialogueEventName);
                }
                else
                    dialogueEventsLookupByWemId.TryAdd(wavFile.WemId, [dialogueEventName]);
            }
            else if (hircItem is ICAkRanSeqCntr ranSeqCntr)
            {
                foreach (var childId in ranSeqCntr.GetChildren())
                {
                    ProcessHircItem(
                        childId,
                        dialogueEventsLookupByWemId,
                        hircLookupById,
                        wavFiles,
                        dialogueEventName,
                        visitedHircIds,
                        cancellationToken);
                }
            }
        }

        private static void StoreStatePathInfo(
            Dictionary<string, List<StatePathInfo>> statePathsLookupByDialogueEvent,
            List<WavFile> wavFiles,
            string dialogueEventName,
            List<StatePath.Node> statePathNodes)
        {
            var joinedStatePath = string.Join(".", statePathNodes.Select(statePathNode => statePathNode.State.Name));

            var statePathInfo = new StatePathInfo
            {
                JoinedStatePath = joinedStatePath,
                StatePathNodes = statePathNodes,
                WavFiles = wavFiles
            };

            if (statePathsLookupByDialogueEvent.TryGetValue(dialogueEventName, out var statePath))
            {
                var containsJoinedStatePath = statePath.Any(statePathInfo => statePathInfo.JoinedStatePath == joinedStatePath);
                if (!containsJoinedStatePath)
                    statePath.Add(statePathInfo);
            }
            else
                statePathsLookupByDialogueEvent.TryAdd(dialogueEventName, [statePathInfo]);
        }

        private void ProcessDialogueEvent(
            AudioProjectFile audioProject,
            ICAkDialogueEvent dialogueEvent,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<string, List<StatePathInfo>> statePathsLookupByDialogueEvent,
            Dictionary<string, int> globalBaseNameUsage,
            HashSet<uint> usedHircIds,
            HashSet<uint> usedSourceIds,
            List<AudioPackOutput> outputs,
            string workspacePath,
            string[] voActorSubstrings,
            CancellationToken cancellationToken)
        {
            var dialogueEventHirc = dialogueEvent as HircItem;
            var dialogueEventName = _audioRepository.GetNameFromId(dialogueEventHirc.Id);
            var actorMixerId = Wh3DialogueEventInformation.GetActorMixerId(dialogueEventName);

            _logger.Here().Information($"Processing Dialogue Event: {dialogueEventName}");

            var dialogueEventAndSoundBank = audioProject.SoundBanks
                .Where(soundBank => soundBank.DialogueEvents.Count != 0)
                .SelectMany(
                    soundBank => soundBank.DialogueEvents,
                    (soundBank, dialogueEventItem) => new { SoundBank = soundBank, DialogueEvent = dialogueEventItem })
                .FirstOrDefault(pair => pair.DialogueEvent.Name == dialogueEventName);

            if (dialogueEventAndSoundBank == null)
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.GetFormat(
                        "AudioProjectConverter.UnsupportedDialogueEvent",
                        dialogueEventName));
            }

            var audioProjectDialogueEvent = dialogueEventAndSoundBank.DialogueEvent;
            var audioProjectDialogueEventSoundBank = dialogueEventAndSoundBank.SoundBank;

            foreach (var statePath in statePathsLookupByDialogueEvent[dialogueEventName].ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Here().Information($"Processing State Path: {statePath.JoinedStatePath}");

                var audioFiles = new List<AudioFile>();
                var statePathWavs = statePath.WavFiles;

                // Get the first VO_Actor as that should? always be the one we want
                var voActor = statePath.StatePathNodes
                    .Where(statePathNode => statePathNode.StateGroup.Name == "VO_Actor")
                    .FirstOrDefault()?.State.Name;

                if (voActor == null)
                    continue;

                // Stop if not containing the thing we're after
                if (!voActorSubstrings.Any(substring => voActor.Contains(substring, StringComparison.CurrentCultureIgnoreCase)))
                    continue;

                ProcessWavFiles(
                    audioProject,
                    statePathWavs,
                    audioFiles,
                    dialogueEventsLookupByWemId,
                    globalBaseNameUsage,
                    dialogueEventName,
                    voActor,
                    usedSourceIds,
                    outputs,
                    workspacePath,
                    cancellationToken);

                if (audioFiles.Count == 0)
                    continue;

                StatePath audioProjectStatePath;
                if (audioFiles.Count > 1)
                {
                    var children = new List<uint>();

                    var randomSequenceContainerIds = IdGenerator.GenerateIds(usedHircIds);
                    usedHircIds.Add(randomSequenceContainerIds.Id);

                    var playlistOrder = 0;
                    foreach (var audioFile in audioFiles)
                    {
                        playlistOrder++;
                        var soundIds = IdGenerator.GenerateIds(usedHircIds);
                        var sound = Sound.CreateContainerSound(soundIds.Guid, soundIds.Id, randomSequenceContainerIds.Id, playlistOrder, audioFile.Id, audioProject.Language);

                        usedHircIds.Add(soundIds.Id);
                        audioFile.Sounds.Add(sound.Id);
                        children.Add(sound.Id);
                        audioProjectDialogueEventSoundBank.Sounds.TryAdd(sound);
                    }

                    var randomSequenceContainerSettings = Shared.AudioProject.Models.HircSettings.CreateRecommendedRandomSequenceContainerSettings(audioFiles.Count);
                    var randomSequenceContainer = new RandomSequenceContainer(
                        randomSequenceContainerIds.Guid,
                        randomSequenceContainerIds.Id,
                        0,
                        actorMixerId,
                        randomSequenceContainerSettings,
                        children);
                    audioProjectStatePath = new StatePath(statePath.StatePathNodes, randomSequenceContainer.Id, AkBkHircType.RandomSequenceContainer);

                    audioProjectDialogueEventSoundBank.RandomSequenceContainers.TryAdd(randomSequenceContainer);
                }
                else
                {
                    var audioFile = audioFiles[0];
                    var soundIds = IdGenerator.GenerateIds(usedHircIds);
                    usedHircIds.Add(soundIds.Id);

                    var soundSettings = Shared.AudioProject.Models.HircSettings.CreateDefaultSoundSettings();
                    var sound = Sound.CreateTargetSound(soundIds.Guid, soundIds.Id, 0, actorMixerId, audioFile.Id, audioProject.Language, soundSettings);
                    audioFile.Sounds.Add(sound.Id);

                    audioProjectStatePath = new StatePath(statePath.StatePathNodes, sound.Id, AkBkHircType.Sound);

                    audioProjectDialogueEventSoundBank.Sounds.TryAdd(sound);
                }

                audioProjectDialogueEvent.StatePaths.InsertAlphabetically(audioProjectStatePath);

                // Remove the processed StatePath from the original list as, if we're processing multiple bnks,
                // Dialogue Events can occur several times so it would add duplicates of the same StatePath
                statePathsLookupByDialogueEvent[dialogueEventName].Remove(statePath);
            }
        }

        private void ProcessWavFiles(
            AudioProjectFile audioProject,
            List<WavFile> statePathWavs,
            List<AudioFile> audioFiles,
            Dictionary<uint, List<string>> dialogueEventsLookupByWemId,
            Dictionary<string, int> globalBaseNameUsage,
            string dialogueEventName,
            string voActor,
            HashSet<uint> usedSourceIds,
            List<AudioPackOutput> outputs,
            string workspacePath,
            CancellationToken cancellationToken)
        {
            const string voActorPrefix = "vo_actor_";
            var prefixIndex = voActor.IndexOf(
                voActorPrefix,
                StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.GetFormat(
                        "AudioProjectConverter.InvalidVOActor",
                        voActor));
            }

            var voActorSegment = voActor
                .Substring(prefixIndex + voActorPrefix.Length)
                .ToLowerInvariant();

            foreach (var wavFile in statePathWavs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chosenDialogueEventName = GetPreferredDialogueEventName(wavFile.WemId, dialogueEventName, dialogueEventsLookupByWemId).ToLower();
                var baseFileName = $"{voActorSegment}_{chosenDialogueEventName}".ToLower();

                // Ensure unique numbering across events globally
                if (!globalBaseNameUsage.TryGetValue(baseFileName, out var count))
                    count = 0;
                count++;
                globalBaseNameUsage[baseFileName] = count;

                wavFile.FileName = $"{baseFileName}_{count}.wav".ToLower();
                wavFile.FilePath = $"{OutputDirectoryPath}\\vo\\{voActorSegment}\\{wavFile.FileName}".ToLower();

                var audioFile = audioProject.GetAudioFile(wavFile.FilePath);
                if (audioFile == null)
                {
                    var audioFileIds = IdGenerator.GenerateIds(usedSourceIds);
                    audioFile = new AudioFile(audioFileIds.Guid, audioFileIds.Id, wavFile.FileName, wavFile.FilePath);

                    usedSourceIds.Add(audioFile.Id);
                    audioProject.AudioFiles.TryAdd(audioFile);
                }
                audioFiles.Add(audioFile);

                var wemFilePath = $"{WemsDirectoryPath}\\{wavFile.WemId}.wem";
                if (!File.Exists(wemFilePath))
                {
                    throw new FileNotFoundException(
                        LocalizationManager.Instance.GetFormat(
                            "AudioProjectConverter.MissingWem",
                            wemFilePath),
                        wemFilePath);
                }

                var wavTempFilePath = Path.Combine(
                    workspacePath,
                    wavFile.FileName);
                var wavPackOutputPath = $"{OutputDirectoryPath}\\vo\\{voActorSegment}\\{wavFile.FileName}";

                var conversionResult =
                    _vgStreamWrapper.ConvertFileUsingVgStream(
                        wemFilePath,
                        wavTempFilePath,
                        cancellationToken);
                if (conversionResult.Failed)
                    throw new Exception($"Failed to convert {wemFilePath} to {wavTempFilePath}");

                cancellationToken.ThrowIfCancellationRequested();
                var wavFileBytes = File
                    .ReadAllBytesAsync(
                        wavTempFilePath,
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                outputs.Add(new AudioPackOutput(
                    wavFile.FileName,
                    wavPackOutputPath,
                    wavFileBytes));
            }
        }

        private static void ProcessModdedStateGroups(AudioProjectFile audioProject, Dictionary<string, List<string>> moddedStateGroups)
        {
            foreach (var moddedStateGroup in moddedStateGroups)
            {
                var audioProjectStateGroup = audioProject.StateGroups
                    .FirstOrDefault(
                        stateGroup =>
                            stateGroup.Name == moddedStateGroup.Key);
                if (audioProjectStateGroup == null)
                {
                    audioProjectStateGroup =
                        StateGroup.CreateForAudioProjectFile(
                            moddedStateGroup.Key,
                            []);
                    audioProject.StateGroups.TryAdd(
                        audioProjectStateGroup);
                }

                foreach (var moddedState in moddedStateGroup.Value)
                {
                    var audioProjectModdedState = new State(moddedState);
                    audioProjectStateGroup.States.InsertAlphabetically(audioProjectModdedState);
                }
            }
        }

        private static string GetPreferredDialogueEventName(uint wemId, string fallbackDialogueEventName, Dictionary<uint, List<string>> dialogueEventsLookupByWemId)
        {
            if (dialogueEventsLookupByWemId.TryGetValue(wemId, out var dialogueEvents) && dialogueEvents != null && dialogueEvents.Count > 0)
            {
                // A list of Dialogue Events whose names I want to prioritise as the name of wems if they appear in them
                var priorityList = new List<string>
                {
                    "battle_vo_order_attack",
                    "battle_vo_order_move",
                    "battle_vo_order_select",
                    "battle_vo_order_halt",
                    "battle_vo_order_withdraw",
                    "battle_vo_order_withdraw_tactical",
                    "battle_vo_order_generic_response",
                    "campaign_vo_attack",
                    "campaign_vo_move",
                    "campaign_vo_selected",
                    "campaign_vo_yes",
                    "campaign_vo_no",
                };

                foreach (var priority in priorityList)
                {
                    if (dialogueEvents.Any(dialogueEvent => dialogueEvent.Equals(priority, StringComparison.OrdinalIgnoreCase)))
                        return dialogueEvents.First(dialogueEvent => dialogueEvent.Equals(priority, StringComparison.OrdinalIgnoreCase));
                }

                return dialogueEvents.OrderBy(dialogueEvent => dialogueEvent, StringComparer.OrdinalIgnoreCase).First();
            }
            return fallbackDialogueEventName;
        }

        partial void OnAudioProjectNameChanged(string value)
        {
            IsAudioProjectNameSet =
                AudioProjectNameValidator.TryNormalize(value, out _);
            UpdateOkButtonIsEnabled();
        }

        partial void OnOutputDirectoryPathChanged(string value)
        {
            IsOutputDirectoryPathSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnVOActorSubstringChanged(string value)
        {
            IsVOActorSubstringSet =
                GetVOActorSubstrings(value).Length > 0;
            UpdateOkButtonIsEnabled();
        }

        partial void OnWemsDirectoryPathChanged(string value)
        {
            IsWemsDirectoryPathSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnBnksDirectoryPathChanged(string value)
        {
            IsBnksDirectoryPathSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnSoundbanksInfoXmlPathChanged(string value)
        {
            IsSoundbanksInfoXmlSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnIsUsingWwiseProjectChanged(bool value)
        {
            UpdateOkButtonIsEnabled();
        }

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
            UpdateOkButtonIsEnabled();
        }

        partial void OnIsProcessingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
            UpdateOkButtonIsEnabled();
        }

        private void UpdateOkButtonIsEnabled()
        {
            IsOkButtonEnabled = _isRepositoryLoaded
                && !IsBusy
                && IsAudioProjectNameSet
                && IsOutputDirectoryPathSet
                && IsWemsDirectoryPathSet
                && IsBnksDirectoryPathSet
                && IsVOActorSubstringSet
                && ((IsUsingWwiseProject && IsSoundbanksInfoXmlSet) || !IsUsingWwiseProject);
        }

        private static string[] GetVOActorSubstrings(string value)
        {
            return (value ?? string.Empty)
                .Split(
                    [','],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Where(substring =>
                    !string.IsNullOrWhiteSpace(substring))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        [RelayCommand] public void SetOutputDirectoryPath()
        {
            var result = _standardDialogs.DisplayBrowseFolderDialog();
            if (result.Result)
            {
                var filePath = result.Folder;
                OutputDirectoryPath = filePath;
                _logger.Here().Information($"Audio Project directory set to: {filePath}");
            }
        }

        [RelayCommand] public void SetWemsDirectoryPath()
        {
            using var dialog = new FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                WemsDirectoryPath = dialog.SelectedPath;
        }

        [RelayCommand] public void SetBnksDirectoryPath()
        {
            using var dialog = new FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                BnksDirectoryPath = dialog.SelectedPath;
        }

        [RelayCommand] public void SetSoundbanksInfoXmlPath()
        {
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Title = LocalizationManager.Instance.Get("Title.SelectSoundbanksInfo");
            openFileDialog.Filter = "SoundbanksInfo.xml|SoundbanksInfo.xml";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
                SoundbanksInfoXmlPath = openFileDialog.FileName;
        }

        [RelayCommand] public void CloseWindowAction() => _closeAction?.Invoke();

        public void SetCloseAction(System.Action closeAction) =>_closeAction = closeAction;
    }
}
