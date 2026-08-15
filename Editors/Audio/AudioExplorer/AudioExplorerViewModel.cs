using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise.HircExploration;
using NAudio.Wave;
using Shared.Core.ErrorHandling;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V112;
using Shared.Ui.Common;

namespace Editors.Audio.AudioExplorer
{
    public enum AudioExportScope
    {
        SelectedAudio,
        SelectedBranch,
        CurrentResults
    }

    public sealed record AudioExportCommandOptions(
        AudioExportScope Scope,
        AudioExportFormat Format);

    public partial class AudioLanguage(Wh3Language language, bool isChecked = false) : ObservableObject
    {
        public Wh3Language Language { get; } = language;
        [ObservableProperty] private bool _isChecked = isChecked;
    }

    public partial class AudioExplorerViewModel : ObservableObject, IEditorInterface
    {
        private sealed record AudioExportPlan(
            List<AudioSource> Sources,
            int SkippedCount);

        private readonly IAudioRepository _audioRepository;
        private readonly ISoundPlayer _soundPlayer;
        private readonly IAudioExportDialogService _audioExportDialogService;
        private readonly IWaveformRendererService _waveformRendererService;
        private readonly DispatcherTimer _playbackTimer;
        private readonly Serilog.ILogger _logger = Logging.Create<AudioExplorerViewModel>();
        private bool _isInitialized;
        private bool _suppressAutomaticLoadProgressWindow;
        private bool _isUpdatingPlaybackPosition;
        private int _hiddenUnavailableReferenceCount;
        private int _playbackSelectionVersion;
        private double _waveformDisplayWidth;
        private AudioPlaybackData _selectedPlaybackData;
        private Task _playbackPreparationTask = Task.CompletedTask;
        private CancellationTokenSource _playbackPreparationCancellationTokenSource;
        private CancellationTokenSource _treeBuildCancellationTokenSource;

        [ObservableProperty] private ExplorerListSelectionFilter _explorerFilter;
        [ObservableProperty] private ObservableCollection<HircTreeNode> _treeList = [];
        [ObservableProperty] private HircTreeNode _selectedNode;
        [ObservableProperty] private string _selectedNodeText = string.Empty; 
        [ObservableProperty] private string _wwiseObjectLabel;
        [ObservableProperty] private ObservableCollection<AudioLanguage> _languages = [];
        [ObservableProperty] private ObservableCollection<Wh3Language> _selectedLanguages = [];
        [ObservableProperty] private bool _searchByActionEvent = false;
        [ObservableProperty] private bool _searchByDialogueEvent = true;
        [ObservableProperty] private bool _searchByHircId = false;
        [ObservableProperty] private bool _searchByVOActor = false;
        [ObservableProperty] private bool _isPlayAudioButtonEnabled = false;
        [ObservableProperty] private bool _isStopAudioButtonEnabled = false;
        [ObservableProperty] private bool _isAudioPlaybackVisible = false;
        [ObservableProperty] private bool _isAudioPreviewLoading = false;
        [ObservableProperty] private int _audioPreviewProgressValue = 0;
        [ObservableProperty] private int _audioPreviewProgressMaximum = 2;
        [ObservableProperty] private string _audioPreviewProgressDetail = string.Empty;
        [ObservableProperty] private ImageSource _audioWaveformBaseImageSource;
        [ObservableProperty] private ImageSource _audioWaveformOverlayImageSource;
        [ObservableProperty] private Rect _audioWaveformOverlayClip = Rect.Empty;
        [ObservableProperty] private TimeSpan _currentPlaybackTime = TimeSpan.Zero;
        [ObservableProperty] private TimeSpan _totalPlaybackTime = TimeSpan.Zero;
        [ObservableProperty] private double _playbackPositionSeconds = 0;
        [ObservableProperty] private double _totalPlaybackSeconds = 0;
        [ObservableProperty] private bool _isLoading = false;
        [ObservableProperty] private bool _isLoadProgressWindowActive = false;
        [ObservableProperty] private int _loadProgress = 0;
        [ObservableProperty] private int _loadProgressValue = 0;
        [ObservableProperty] private int _loadProgressMaximum = 0;
        [ObservableProperty] private string _loadProgressDetail = string.Empty;
        [ObservableProperty] private bool _loadProgressIsIndeterminate = true;
        [ObservableProperty] private string _loadStatus = string.Empty;
        [ObservableProperty] private bool _isExporting = false;
        [ObservableProperty] private int _exportProgress = 0;
        [ObservableProperty] private int _exportProgressValue = 0;
        [ObservableProperty] private int _exportProgressMaximum = 0;
        [ObservableProperty] private string _exportProgressDetail = string.Empty;
        [ObservableProperty] private bool _isExportSelectedAudioEnabled = false;
        [ObservableProperty] private bool _isExportSelectedBranchEnabled = false;
        [ObservableProperty] private bool _isExportCurrentResultsEnabled = false;

        public string DisplayName { get; set; } = LocalizationManager.Instance.Get("DisplayName.AudioExplorer");

        public AudioExportCommandOptions ExportSelectedAudioWav { get; } =
            new(AudioExportScope.SelectedAudio, AudioExportFormat.Wav);
        public AudioExportCommandOptions ExportSelectedAudioWem { get; } =
            new(AudioExportScope.SelectedAudio, AudioExportFormat.Wem);
        public AudioExportCommandOptions ExportSelectedBranchWav { get; } =
            new(AudioExportScope.SelectedBranch, AudioExportFormat.Wav);
        public AudioExportCommandOptions ExportSelectedBranchWem { get; } =
            new(AudioExportScope.SelectedBranch, AudioExportFormat.Wem);
        public AudioExportCommandOptions ExportCurrentResultsWav { get; } =
            new(AudioExportScope.CurrentResults, AudioExportFormat.Wav);
        public AudioExportCommandOptions ExportCurrentResultsWem { get; } =
            new(AudioExportScope.CurrentResults, AudioExportFormat.Wem);

        public AudioExplorerViewModel(
            IAudioRepository audioRepository,
            ISoundPlayer soundPlayer,
            IAudioExportDialogService audioExportDialogService,
            IWaveformRendererService waveformRendererService)
        {
            _audioRepository = audioRepository;
            _soundPlayer = soundPlayer;
            _audioExportDialogService = audioExportDialogService;
            _waveformRendererService = waveformRendererService;
            _playbackTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Background,
                OnPlaybackTimerTick,
                Dispatcher.CurrentDispatcher);
            _playbackTimer.Stop();
            _soundPlayer.PlaybackStopped += OnPlaybackStopped;

            // Remove SFX as we don't allow for filtering it out in the AudioRepository so we don't need to display it
            var languages = Enum.GetValues<Wh3Language>()
                .Where(language => language != Wh3Language.Sfx)
                .ToArray();
            Languages = new ObservableCollection<AudioLanguage>(
                languages.Select(language => new AudioLanguage(language, language == Wh3Language.EnglishUK))
            );

            Languages.CollectionChanged += OnLanguagesCollectionChanged;
            foreach (var language in Languages)
                language.PropertyChanged += OnAudioLanguageChanged;

            SetSelectedLanguages();

            ExplorerFilter = new ExplorerListSelectionFilter(_audioRepository, SearchByActionEvent, SearchByDialogueEvent, SearchByHircId, SearchByVOActor);
            ExplorerFilter.ExplorerList.SelectedItemChanged += OnEventSelected;

            WwiseObjectLabel = LocalizationManager.Instance.Get("AudioExplorer.WwiseObjectData");
        }

        partial void OnSearchByActionEventChanged(bool value)
        {
            Reset();

            if (SearchByActionEvent)
            {
                SearchByDialogueEvent = false;
                SearchByHircId = false;
                SearchByVOActor = false;
            }

            RefreshList();
        }

        partial void OnSearchByDialogueEventChanged(bool value)
        {
            Reset();

            if (SearchByDialogueEvent)
            {
                SearchByActionEvent = false;
                SearchByHircId = false;
                SearchByVOActor = false;
            }

            RefreshList();
        }

        partial void OnSearchByHircIdChanged(bool value)
        {
            Reset();

            if (SearchByHircId)
            {
                SearchByActionEvent = false;
                SearchByDialogueEvent = false;
                SearchByVOActor = false;
            }

            RefreshList();
        }

        partial void OnSearchByVOActorChanged(bool value)
        {
            Reset();

            if (SearchByVOActor)
            {
                SearchByActionEvent = false;
                SearchByDialogueEvent = false;
                SearchByHircId = false;
            }

            RefreshList();
        }

        partial void OnIsLoadingChanged(bool value) => RefreshExportAvailability();

        partial void OnIsExportingChanged(bool value) => RefreshExportAvailability();

        partial void OnSelectedNodeChanged(HircTreeNode value) => OnNodeSelected(value);

        partial void OnPlaybackPositionSecondsChanged(double value)
        {
            if (_isUpdatingPlaybackPosition ||
                _selectedPlaybackData == null ||
                IsAudioPreviewLoading)
            {
                return;
            }

            var playbackTime = TimeSpan.FromSeconds(Math.Clamp(
                value,
                0,
                TotalPlaybackSeconds));
            if (_soundPlayer.Seek(_selectedPlaybackData, playbackTime))
            {
                SetPlaybackPosition(playbackTime);
                IsStopAudioButtonEnabled = playbackTime > TimeSpan.Zero;
            }
        }

        private void OnNodeSelected(HircTreeNode selectedNode)
        {
            SelectAudioForPlayback(selectedNode);
            RefreshExportAvailability();

            SelectedNodeText = string.Empty;

            if (selectedNode == null || selectedNode.Hirc == null)
            {
                WwiseObjectLabel = LocalizationManager.Instance.Get("AudioExplorer.WwiseObjectData");
                return;
            }

            var nodeName = selectedNode.DisplayName;
            if (nodeName.Contains("_"))
                nodeName = WpfHelpers.DuplicateUnderscores(nodeName);
            WwiseObjectLabel = LocalizationManager.Instance.GetFormat("AudioExplorer.WwiseObjectDataNamed", nodeName);

            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() }, WriteIndented = true };
            var hircAsString = JsonSerializer.Serialize((object)selectedNode.Hirc, options);
            SelectedNodeText = hircAsString;

            if (selectedNode.Hirc.HircType == AkBkHircType.Sound)
            {
                var parentStructures = SoundParentStructureParser.Compute(selectedNode.Hirc, _audioRepository);

                SelectedNodeText += $"\n\n{LocalizationManager.Instance.Get("AudioExplorer.ParentStructure")}\n";
                foreach (var parentStruct in parentStructures)
                {
                    SelectedNodeText += "\t" + parentStruct.Description + "\n";
                    foreach (var graphItem in parentStruct.GraphItems)
                        SelectedNodeText += "\t\t" + graphItem.Description + "\n";

                    SelectedNodeText += "\n";
                }
            }

            ExpandNodes(selectedNode);
        }

        private void SelectAudioForPlayback(HircTreeNode selectedNode)
        {
            var selectionVersion = ++_playbackSelectionVersion;
            var previousCancellationTokenSource = Interlocked.Exchange(
                ref _playbackPreparationCancellationTokenSource,
                null);
            previousCancellationTokenSource?.Cancel();
            previousCancellationTokenSource?.Dispose();

            _playbackTimer.Stop();
            _soundPlayer.Stop();
            _selectedPlaybackData = null;
            IsStopAudioButtonEnabled = false;
            IsAudioPreviewLoading = false;
            AudioWaveformBaseImageSource = null;
            AudioWaveformOverlayImageSource = null;
            TotalPlaybackTime = TimeSpan.Zero;
            TotalPlaybackSeconds = 0;
            SetPlaybackPosition(TimeSpan.Zero);

            if (!TryCreateAudioSource(selectedNode, out var source))
            {
                IsAudioPlaybackVisible = false;
                _playbackPreparationTask = Task.CompletedTask;
                return;
            }

            IsAudioPlaybackVisible = true;
            AudioPreviewProgressValue = 0;
            AudioPreviewProgressMaximum = 2;
            AudioPreviewProgressDetail = string.Format(
                LocalizationManager.Instance.Get(
                    "OperationProgress.AudioPreview.Decode"),
                source.SourceId);
            IsAudioPreviewLoading = true;
            var cancellationTokenSource = new CancellationTokenSource();
            _playbackPreparationCancellationTokenSource =
                cancellationTokenSource;
            _playbackPreparationTask = PrepareSelectedAudioAsync(
                source,
                selectionVersion,
                cancellationTokenSource.Token);
        }

        private async Task PrepareSelectedAudioAsync(
            AudioSource source,
            int selectionVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                var playbackData = await Task.Run(
                    () => _soundPlayer.Prepare(source),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (playbackData == null)
                    throw new InvalidOperationException(
                        $"Unable to prepare audio '{source.SourceId}'.");

                AudioPreviewProgressValue = 1;
                AudioPreviewProgressDetail = string.Format(
                    LocalizationManager.Instance.Get(
                        "OperationProgress.AudioPreview.Waveform"),
                    source.SourceId);
                var waveform = await _waveformRendererService.RenderAsync(
                    playbackData.WavData,
                    1000,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (selectionVersion != _playbackSelectionVersion)
                    return;

                _selectedPlaybackData = playbackData;
                AudioWaveformBaseImageSource =
                    waveform.Visualisation.BaseImage;
                AudioWaveformOverlayImageSource =
                    waveform.Visualisation.OverlayImage;
                TotalPlaybackTime = waveform.TotalTime;
                TotalPlaybackSeconds = waveform.TotalTime.TotalSeconds;
                SetPlaybackPosition(TimeSpan.Zero);
                AudioPreviewProgressValue = 2;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _logger.Here().Error(
                    $"Unable to prepare Audio Explorer preview: {e}");
                if (selectionVersion == _playbackSelectionVersion)
                {
                    LoadStatus = LocalizationManager.Instance.Get(
                        "AudioExplorer.PlaybackFailed");
                }
            }
            finally
            {
                if (selectionVersion == _playbackSelectionVersion)
                {
                    IsAudioPreviewLoading = false;
                    RefreshExportAvailability();
                }
            }
        }

        private static void ExpandNodes(HircTreeNode selectedNode)
        {
            // Expand ancestors and collapse siblings at branching levels
            var currentNode = selectedNode;
            while (currentNode.Parent != null)
            {
                var parentNode = currentNode.Parent;

                parentNode.IsExpanded = true;

                if (parentNode.Children != null && parentNode.Children.Count > 1)
                {
                    foreach (var siblingNode in parentNode.Children)
                        siblingNode.IsExpanded = false;
                }

                currentNode.IsExpanded = true;
                currentNode = parentNode;
            }

            // Expand where there's only one child
            currentNode = selectedNode;
            while (currentNode.Children != null && currentNode.Children.Count == 1)
            {
                currentNode.IsExpanded = true;
                currentNode = currentNode.Children[0];
            }

            if (currentNode.Children != null && currentNode.Children.Count > 0)
                currentNode.IsExpanded = true;
        }

        private void OnLanguagesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (AudioLanguage item in e.NewItems)
                    item.PropertyChanged += OnAudioLanguageChanged;
            }

            if (e.OldItems != null)
            {
                foreach (AudioLanguage item in e.OldItems)
                    item.PropertyChanged -= OnAudioLanguageChanged;
            }

            SetSelectedLanguages();
        }

        private void OnAudioLanguageChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioLanguage.IsChecked))
                SetSelectedLanguages();
        }

        private void RefreshList() => ExplorerFilter.Refresh(SearchByActionEvent, SearchByDialogueEvent, SearchByHircId, SearchByVOActor);

        private void SetSelectedLanguages()
        {
            SelectedLanguages = new ObservableCollection<Wh3Language>(Languages
                .Where(audioLanguage => audioLanguage.IsChecked)
                .Select(audioLanguage => audioLanguage.Language));
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _suppressAutomaticLoadProgressWindow = true;
            try
            {
                await LoadAudioRepositoryForSelectedLanguagesCommand
                    .ExecuteAsync(null);
            }
            finally
            {
                _suppressAutomaticLoadProgressWindow = false;
            }
        }

        [RelayCommand(IncludeCancelCommand = true)]
        public async Task LoadAudioRepositoryForSelectedLanguagesAsync(CancellationToken cancellationToken = default)
        {
            if (!_audioRepository.IsCurrentGameSupported)
            {
                LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.UnsupportedGame");
                return;
            }

            var languages = SelectedLanguages.Select(Wh3LanguageInformation.GetLanguageAsString).ToList();
            if (languages.Count == 0)
            {
                LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.SelectLanguage");
                return;
            }

            IsLoading = true;
            IsLoadProgressWindowActive =
                !_suppressAutomaticLoadProgressWindow;
            LoadProgress = 0;
            LoadProgressValue = 0;
            LoadProgressMaximum = 0;
            LoadProgressDetail = string.Empty;
            LoadProgressIsIndeterminate = true;
            LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.Loading");
            CancelTreeBuild();

            var progress = new Progress<AudioLoadProgress>(value =>
            {
                LoadProgress = value.Total == 0
                    ? 0
                    : (int)Math.Round(value.Completed * 100d / value.Total);
                LoadProgressValue = value.Completed;
                LoadProgressMaximum = value.Total;
                LoadProgressDetail = Path.GetFileName(value.CurrentFile);
                LoadProgressIsIndeterminate = value.Total <= 0;
                LoadStatus = LocalizationManager.Instance.GetFormat(
                    "AudioExplorer.LoadingProgress",
                    value.Completed,
                    value.Total,
                    Path.GetFileName(value.CurrentFile));
            });

            try
            {
                await Task.Run(
                    () => _audioRepository.Load(languages, progress, cancellationToken),
                    cancellationToken);

                Reset();
                RefreshList();
                LoadProgress = 100;
                LoadStatus = LocalizationManager.Instance.GetFormat(
                    "AudioExplorer.LoadComplete",
                    ExplorerFilter.ExplorerList.Values.Count);
            }
            catch (OperationCanceledException)
            {
                LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.LoadCancelled");
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Unable to load Audio Explorer data: {e}");
                LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.LoadFailed");
            }
            finally
            {
                IsLoadProgressWindowActive = false;
                IsLoading = false;
            }
        }

        private void OnEventSelected(ExplorerListItem newValue)
        {
            if (newValue == null)
                return;

            if (newValue.HircItem != null &&
                ReferenceEquals(newValue.HircItem, SelectedNode?.Hirc))
                return;

            CancelTreeBuild();
            SelectedNode = null;
            TreeList.Clear();

            if (SearchByVOActor)
            {
                _treeBuildCancellationTokenSource = new CancellationTokenSource();
                _ = BuildVoActorTreesAsync(
                    newValue.DisplayName,
                    _treeBuildCancellationTokenSource);
                return;
            }
            else
            {
                var wwiseTreeParserChildren = new HircTreeChildrenParser(_audioRepository);

                var rootNode = wwiseTreeParserChildren.BuildHierarchy(newValue.HircItem);
                rootNode.IsExpanded = true;

                TreeList.Add(rootNode);
                RefreshExportAvailability();
            }
        }

        private async Task BuildVoActorTreesAsync(
            string voActor,
            CancellationTokenSource cancellationSource)
        {
            var cancellationToken = cancellationSource.Token;
            LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.BuildingTree");

            try
            {
                var result = await Task.Run(() =>
                {
                    var parser = new HircTreeChildrenParser(_audioRepository);
                    var dialogueEvents = _audioRepository.GetHircsByHircType(AkBkHircType.Dialogue_Event);
                    var matchingRoots = new List<HircTreeNode>();
                    var unavailableReferenceCount = 0;
                    var playableAudioCount = 0;

                    foreach (var dialogueEvent in dialogueEvents)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var root = parser.BuildHierarchy(dialogueEvent);
                        if (!FilterTreeByVOActor(root, voActor, cancellationToken))
                            continue;

                        unavailableReferenceCount += CountMissingReferences(root);
                        if (!PruneBranchesWithoutPlayableAudio(root))
                            continue;

                        playableAudioCount += CountPlayableAudio(root);
                        matchingRoots.Add(root);
                    }

                    return (
                        Roots: matchingRoots,
                        UnavailableReferenceCount: unavailableReferenceCount,
                        PlayableAudioCount: playableAudioCount);
                }, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_treeBuildCancellationTokenSource, cancellationSource))
                    return;

                _hiddenUnavailableReferenceCount = result.UnavailableReferenceCount;
                foreach (var root in result.Roots)
                    TreeList.Add(root);

                if (result.Roots.Count == 0 &&
                    result.UnavailableReferenceCount != 0)
                {
                    LoadStatus = LocalizationManager.Instance.GetFormat(
                        "AudioExplorer.VoActorUnavailable",
                        result.UnavailableReferenceCount);
                }
                else if (result.UnavailableReferenceCount != 0)
                {
                    LoadStatus = LocalizationManager.Instance.GetFormat(
                        "AudioExplorer.TreeReadyWithUnavailable",
                        result.Roots.Count,
                        result.PlayableAudioCount,
                        result.UnavailableReferenceCount);
                }
                else
                {
                    LoadStatus = LocalizationManager.Instance.GetFormat(
                        "AudioExplorer.TreeReady",
                        result.Roots.Count);
                }

                RefreshExportAvailability();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Unable to build Audio Explorer trees: {e}");
                if (ReferenceEquals(_treeBuildCancellationTokenSource, cancellationSource))
                    LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.TreeBuildFailed");
            }
            finally
            {
                if (ReferenceEquals(_treeBuildCancellationTokenSource, cancellationSource))
                    _treeBuildCancellationTokenSource = null;
                cancellationSource.Dispose();
            }
        }

        private static bool FilterTreeByVOActor(
            HircTreeNode currentNode,
            string voActor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentNodeMatches = currentNode.DisplayName.Contains(voActor, StringComparison.OrdinalIgnoreCase);
            if (currentNodeMatches)
                return true;

            if (currentNode.Children == null || currentNode.Children.Count == 0)
                return false;

            var anyMatches = false;
            for (var i = currentNode.Children.Count - 1; i >= 0; i--)
            {
                var childNode = currentNode.Children[i];

                var isMatch = FilterTreeByVOActor(childNode, voActor, cancellationToken);
                if (!isMatch)
                    currentNode.Children.RemoveAt(i);
                else
                    anyMatches = true;
            }

            return anyMatches;
        }

        private static bool PruneBranchesWithoutPlayableAudio(
            HircTreeNode currentNode)
        {
            if (currentNode == null || currentNode.IsMissingReference)
                return false;

            if (TryCreateAudioSource(currentNode, out _))
                return true;

            if (currentNode.Children == null || currentNode.Children.Count == 0)
                return false;

            var containsPlayableAudio = false;
            for (var i = currentNode.Children.Count - 1; i >= 0; i--)
            {
                if (!PruneBranchesWithoutPlayableAudio(currentNode.Children[i]))
                    currentNode.Children.RemoveAt(i);
                else
                    containsPlayableAudio = true;
            }

            return containsPlayableAudio;
        }

        private static int CountPlayableAudio(HircTreeNode currentNode)
        {
            if (currentNode == null)
                return 0;

            var count = TryCreateAudioSource(currentNode, out _) ? 1 : 0;
            if (currentNode.Children == null)
                return count;

            foreach (var child in currentNode.Children)
                count += CountPlayableAudio(child);

            return count;
        }

        private static int CountMissingReferences(HircTreeNode currentNode)
        {
            if (currentNode == null)
                return 0;

            var count = currentNode.IsMissingReference ? 1 : 0;
            if (currentNode.Children == null)
                return count;

            foreach (var child in currentNode.Children)
                count += CountMissingReferences(child);

            return count;
        }

        [RelayCommand]
        public async Task PlayAudioAsync()
        {
            if (IsLoading || IsExporting)
                return;

            var selectionVersion = _playbackSelectionVersion;
            var preparationTask = _playbackPreparationTask;
            await preparationTask;

            if (selectionVersion != _playbackSelectionVersion)
                return;

            if (_selectedPlaybackData == null)
            {
                LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.PlaybackFailed");
                return;
            }

            if (_soundPlayer.Play(_selectedPlaybackData))
            {
                IsStopAudioButtonEnabled = true;
                if (_soundPlayer.PlaybackState == PlaybackState.Playing)
                    _playbackTimer.Start();
                else
                    _playbackTimer.Stop();
            }
            else
                LoadStatus = LocalizationManager.Instance.Get("AudioExplorer.PlaybackFailed");
        }

        [RelayCommand]
        public void StopAudio()
        {
            _playbackTimer.Stop();
            _soundPlayer.Stop();
            IsStopAudioButtonEnabled = false;
            SetPlaybackPosition(TimeSpan.Zero);
            LoadStatus = LocalizationManager.Instance.Get(
                "AudioExplorer.PlaybackStopped");
        }

        private void OnPlaybackTimerTick(object sender, EventArgs e)
        {
            if (_soundPlayer.PlaybackState != PlaybackState.Playing)
            {
                _playbackTimer.Stop();
                return;
            }

            SetPlaybackPosition(_soundPlayer.CurrentPlaybackTime);
        }

        private void OnPlaybackStopped(
            object sender,
            StoppedEventArgs e)
        {
            _playbackTimer.Stop();
            IsStopAudioButtonEnabled = false;
            SetPlaybackPosition(TimeSpan.Zero);
        }

        private void SetPlaybackPosition(TimeSpan playbackTime)
        {
            var clampedSeconds = Math.Clamp(
                playbackTime.TotalSeconds,
                0,
                TotalPlaybackSeconds);
            CurrentPlaybackTime = TimeSpan.FromSeconds(clampedSeconds);

            _isUpdatingPlaybackPosition = true;
            try
            {
                PlaybackPositionSeconds = clampedSeconds;
            }
            finally
            {
                _isUpdatingPlaybackPosition = false;
            }

            UpdateWaveformOverlayClip();
        }

        public void SetWaveformDisplayWidth(double width)
        {
            _waveformDisplayWidth = Math.Max(0, width);
            UpdateWaveformOverlayClip();
        }

        private void UpdateWaveformOverlayClip()
        {
            var ratio = TotalPlaybackSeconds <= 0
                ? 0
                : PlaybackPositionSeconds / TotalPlaybackSeconds;
            AudioWaveformOverlayClip = new Rect(
                0,
                0,
                Math.Clamp(ratio, 0, 1) * _waveformDisplayWidth,
                64);
        }

        [RelayCommand(IncludeCancelCommand = true)]
        public async Task ExportAudioAsync(
            AudioExportCommandOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null || IsLoading || IsExporting)
                return;

            var plan = CreateExportPlan(options.Scope);
            if (plan.Sources.Count == 0)
            {
                LoadStatus = LocalizationManager.Instance.GetFormat(
                    "AudioExplorer.NoExportableAudio",
                    plan.SkippedCount);
                return;
            }

            var extension = options.Format == AudioExportFormat.Wav
                ? ".wav"
                : ".wem";
            string selectedPath;
            if (options.Scope == AudioExportScope.SelectedAudio)
            {
                selectedPath = _audioExportDialogService.SelectOutputFile(
                    $"{plan.Sources[0].SourceId}{extension}",
                    options.Format);
            }
            else
                selectedPath = _audioExportDialogService.SelectOutputFolder();

            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            IsExporting = true;
            ExportProgress = 0;
            ExportProgressValue = 0;
            ExportProgressMaximum = plan.Sources.Count;
            ExportProgressDetail = string.Empty;
            var exportedCount = 0;
            var failedCount = 0;
            var skippedCount = plan.SkippedCount;
            var synchronizationContext = SynchronizationContext.Current;
            void ReportProgress(int completed, int total, uint sourceId)
            {
                void UpdateProgress()
                {
                    ExportProgress = total == 0
                        ? 0
                        : (int)Math.Round(
                            completed * 100d / total);
                    ExportProgressValue = completed;
                    ExportProgressMaximum = total;
                    ExportProgressDetail = sourceId.ToString();
                    LoadStatus = LocalizationManager.Instance.GetFormat(
                        "AudioExplorer.ExportingProgress",
                        completed,
                        total,
                        sourceId);
                }

                if (synchronizationContext == null ||
                    ReferenceEquals(
                        synchronizationContext,
                        SynchronizationContext.Current))
                {
                    UpdateProgress();
                }
                else
                    synchronizationContext.Send(_ => UpdateProgress(), null);
            }

            try
            {
                await Task.Run(() =>
                {
                    var reservedPaths = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < plan.Sources.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var source = plan.Sources[i];
                        var outputPath = options.Scope ==
                            AudioExportScope.SelectedAudio
                                ? selectedPath
                                : GetAvailableOutputPath(
                                    selectedPath,
                                    source.SourceId,
                                    extension,
                                    reservedPaths);

                        if (_soundPlayer.Export(
                            source,
                            options.Format,
                            outputPath))
                        {
                            exportedCount++;
                        }
                        else
                            failedCount++;

                        ReportProgress(
                            i + 1,
                            plan.Sources.Count,
                            source.SourceId);
                    }
                }, cancellationToken);

                ExportProgress = 100;
                LoadStatus = LocalizationManager.Instance.GetFormat(
                    "AudioExplorer.ExportComplete",
                    exportedCount,
                    skippedCount,
                    failedCount);
            }
            catch (OperationCanceledException)
            {
                LoadStatus = LocalizationManager.Instance.GetFormat(
                    "AudioExplorer.ExportCancelled",
                    exportedCount,
                    skippedCount,
                    failedCount);
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Unable to export audio: {e}");
                LoadStatus = LocalizationManager.Instance.Get(
                    "AudioExplorer.ExportFailed");
            }
            finally
            {
                IsExporting = false;
            }
        }

        private AudioExportPlan CreateExportPlan(AudioExportScope scope)
        {
            IEnumerable<HircTreeNode> roots = scope switch
            {
                AudioExportScope.SelectedAudio when SelectedNode != null =>
                    [SelectedNode],
                AudioExportScope.SelectedBranch when SelectedNode != null =>
                    [SelectedNode],
                AudioExportScope.CurrentResults => TreeList,
                _ => []
            };

            var sources = new List<AudioSource>();
            var seenSources = new HashSet<AudioSource>();
            var skippedCount = scope == AudioExportScope.CurrentResults
                ? _hiddenUnavailableReferenceCount
                : 0;

            foreach (var node in EnumerateNodes(roots))
            {
                if (node.IsMissingReference)
                {
                    skippedCount++;
                    continue;
                }

                if (!TryCreateAudioSource(node, out var source))
                    continue;

                if (seenSources.Add(source))
                    sources.Add(source);
                else
                    skippedCount++;
            }

            if (scope == AudioExportScope.SelectedAudio &&
                sources.Count > 1)
            {
                sources.RemoveRange(1, sources.Count - 1);
            }

            return new AudioExportPlan(sources, skippedCount);
        }

        private static IEnumerable<HircTreeNode> EnumerateNodes(
            IEnumerable<HircTreeNode> roots)
        {
            foreach (var root in roots)
            {
                if (root == null)
                    continue;

                yield return root;
                if (root.Children == null)
                    continue;

                foreach (var child in EnumerateNodes(root.Children))
                    yield return child;
            }
        }

        private static bool TryCreateAudioSource(
            HircTreeNode node,
            out AudioSource source)
        {
            source = null;
            if (node?.Hirc is ICAkSound sound)
            {
                if (sound.GetStreamType() == AKBKSourceType.Data_BNK &&
                    sound is CAkSound_V112 soundV112)
                {
                    source = new AudioSource(
                        soundV112.AkBankSourceData.AkMediaInformation.SourceId,
                        node.Hirc.LanguageId,
                        soundV112.AkBankSourceData.AkMediaInformation.FileId,
                        (int)soundV112.AkBankSourceData.AkMediaInformation.FileOffset,
                        (int)soundV112.AkBankSourceData.AkMediaInformation.InMemoryMediaSize);
                }
                else
                {
                    source = new AudioSource(
                        sound.GetSourceId(),
                        node.Hirc.LanguageId);
                }

                return true;
            }

            if (node?.Hirc is ICAkMusicTrack &&
                node.SourceId is uint sourceId)
            {
                source = new AudioSource(sourceId, node.Hirc.LanguageId);
                return true;
            }

            return false;
        }

        private static string GetAvailableOutputPath(
            string outputFolder,
            uint sourceId,
            string extension,
            HashSet<string> reservedPaths)
        {
            var basePath = Path.Combine(
                outputFolder,
                $"{sourceId}{extension}");
            var candidate = basePath;
            var suffix = 2;

            while (File.Exists(candidate) || !reservedPaths.Add(candidate))
            {
                candidate = Path.Combine(
                    outputFolder,
                    $"{sourceId}_{suffix++}{extension}");
            }

            return candidate;
        }

        private void RefreshExportAvailability()
        {
            var isBusy = IsLoading || IsExporting;
            IsPlayAudioButtonEnabled =
                !isBusy &&
                _selectedPlaybackData != null;
            IsExportSelectedAudioEnabled =
                !isBusy &&
                TryCreateAudioSource(SelectedNode, out _);
            IsExportSelectedBranchEnabled =
                !isBusy &&
                SelectedNode != null &&
                CountPlayableAudio(SelectedNode) != 0;
            IsExportCurrentResultsEnabled =
                !isBusy &&
                TreeList.Any(root => CountPlayableAudio(root) != 0);
        }

        private void Reset()
        {
            CancelTreeBuild();
            _soundPlayer.Stop();
            _playbackTimer.Stop();
            IsStopAudioButtonEnabled = false;
            _hiddenUnavailableReferenceCount = 0;
            if (ExplorerFilter != null)
            {
                ExplorerFilter.ExplorerList.SelectedItemChanged -= OnEventSelected;
                ExplorerFilter.ExplorerList.SelectedItem = null;
                ExplorerFilter.ExplorerList.Filter = string.Empty;
                ExplorerFilter.ExplorerList.SelectedItemChanged += OnEventSelected;
                ExplorerFilter.ExplorerList.UpdatePossibleValues([]);
            }

            SelectedNode = null;
            SelectedNodeText = string.Empty;
            TreeList.Clear();
            RefreshExportAvailability();
        }

        private void CancelTreeBuild()
        {
            _treeBuildCancellationTokenSource?.Cancel();
            _treeBuildCancellationTokenSource = null;
        }

        public void Close()
        {
            LoadAudioRepositoryForSelectedLanguagesCancelCommand.Execute(null);
            ExportAudioCancelCommand.Execute(null);
            CancelTreeBuild();
            _playbackSelectionVersion++;
            _playbackPreparationCancellationTokenSource?.Cancel();
            _playbackPreparationCancellationTokenSource?.Dispose();
            _playbackPreparationCancellationTokenSource = null;
            _playbackTimer.Stop();
            _soundPlayer.PlaybackStopped -= OnPlaybackStopped;
            _soundPlayer.Stop();
            ExplorerFilter.ExplorerList.SelectedItemChanged -= OnEventSelected;

            Languages.CollectionChanged -= OnLanguagesCollectionChanged;
            foreach (var item in Languages)
                item.PropertyChanged -= OnAudioLanguageChanged;
        }
    }
}
