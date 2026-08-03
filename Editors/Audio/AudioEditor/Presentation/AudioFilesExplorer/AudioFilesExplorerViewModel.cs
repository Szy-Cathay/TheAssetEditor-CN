using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Commands.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Events.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Events.WaveformVisualiser;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Editors.Audio.AudioEditor.Presentation.AudioFilesExplorer
{
    public partial class AudioFilesExplorerViewModel :
        ObservableObject,
        IDisposable
    {
        private readonly IGlobalEventHub _globalEventHub;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IPackFileService _packFileService;
        private readonly IAudioEditorStateService _audioEditorStateService;
        private readonly IAudioFilesTreeBuilderService _audioFilesTreeBuilder;
        private readonly IAudioFilesTreeSearchFilterService _audioFilesTreeFilter;
        private readonly IWaveformVisualisationCacheService _waveformVisualisationCacheService;

        [ObservableProperty] private string _audioFilesExplorerLabel;
        [ObservableProperty] private bool _isSetAudioFilesButtonEnabled = false;
        [ObservableProperty] private bool _isAddAudioFilesButtonEnabled = false;
        [ObservableProperty] private bool _isPlayAudioButtonEnabled = false;
        [ObservableProperty] private string _filterQuery;
        [ObservableProperty] private ObservableCollection<AudioFilesTreeNode> _audioFilesTree = [];
        [ObservableProperty] private ObservableCollection<AudioFilesTreeNode> _selectedTreeNodes = [];
        private CancellationTokenSource _filterQueryDebounceCancellationTokenSource;
        private PackFileContainer _editablePack;

        public AudioFilesExplorerViewModel(
            IGlobalEventHub globalEventHub,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IAudioEditorStateService audioEditorStateService,
            IAudioFilesTreeBuilderService audioFilesTreeBuilder,
            IAudioFilesTreeSearchFilterService audioFilesTreeFilter,
            IWaveformVisualisationCacheService waveformVisualisationCacheService)
        {
            _globalEventHub = globalEventHub;
            _eventHub = eventHub;
            _uiCommandFactory = uiCommandFactory;
            _packFileService = packFileService;
            _audioEditorStateService = audioEditorStateService;
            _audioFilesTreeBuilder = audioFilesTreeBuilder;
            _audioFilesTreeFilter = audioFilesTreeFilter;
            _waveformVisualisationCacheService = waveformVisualisationCacheService;

            SelectedTreeNodes.CollectionChanged += OnSelectedTreeNodesChanged;

            _eventHub.Register<AudioProjectExplorerNodeSelectedEvent>(this, OnAudioProjectExplorerNodeSelected);
            _eventHub.Register<AudioFilesChangedEvent>(this, OnAudioFilesChanged);
            _globalEventHub.Register<PackFileContainerSetAsMainEditableEvent>(this, x => OnPackFileContainerSetAsMainEditable(x.Container));
            _globalEventHub.Register<PackFileContainerFilesAddedEvent>(this, x => RefreshAudioFilesTreeIfWavChanged(x.Container, x.AddedFiles));
            _globalEventHub.Register<PackFileContainerFilesRemovedEvent>(this, x => RefreshAudioFilesTreeIfWavChanged(x.Container, x.RemovedFiles));
            _globalEventHub.Register<PackFileContainerFilesUpdatedEvent>(this, x => RefreshAudioFilesTreeIfWavChanged(x.Container, x.ChangedFiles));
            _globalEventHub.Register<FolderProjectChangedEvent>(
                this,
                x => RefreshAudioFilesTreeIfWavChanged(
                    x.Container,
                    x.ChangeSet.FileChanges.Select(change => change.File)));
            _globalEventHub.Register<PackFileContainerFolderRemovedEvent>(
                this,
                x => RefreshAudioFilesTreeIfCurrent(x.Container));

            AudioFilesExplorerLabel = LocalizationManager.Instance.Get("AudioEditor.Panel.AudioFilesExplorer");

            var editablePack = _packFileService.GetEditablePack();
            if (editablePack == null)
                return;

            _editablePack = editablePack;
            AudioFilesExplorerLabel =
                $"{LocalizationManager.Instance.Get("AudioEditor.Panel.AudioFilesExplorer")} - {WpfHelpers.DuplicateUnderscores(editablePack.Name)}";

            AudioFilesTree = _audioFilesTreeBuilder.BuildTree(editablePack);
            SetupIsExpandedHandlers(AudioFilesTree);
        }

        private void SetupIsExpandedHandlers(ObservableCollection<AudioFilesTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.NodeIsExpandedChanged -= OnNodeIsExpandedChanged;
                node.NodeIsExpandedChanged += OnNodeIsExpandedChanged;

                if (node.Children is { Count: > 0 })
                    SetupIsExpandedHandlers(node.Children);
            }
        }

        private void CacheRootWaveformVisualisations()
        {
            var wavFilePaths = new List<string>();
            foreach (var node in AudioFilesTree)
            {
                if (node.Parent == null && node.Type == AudioFilesTreeNodeType.WavFile)
                    wavFilePaths.Add(node.FilePath);
            }

            if (wavFilePaths.Count > 0)
                _eventHub.Publish(new CacheWaveformRequestedEvent(wavFilePaths));
        }

        private void OnNodeIsExpandedChanged(object sender, bool isExpanded)
        {
            var node = sender as AudioFilesTreeNode;
            if (node.Children != null)
            {
                var wavFilePaths = new List<string>();
                foreach (var child in node.Children)
                {
                    if (child.Type == AudioFilesTreeNodeType.WavFile)
                        wavFilePaths.Add(child.FilePath);
                }

                if (wavFilePaths.Count == 0)
                    return;

                if (isExpanded)
                    _eventHub.Publish(new CacheWaveformRequestedEvent(wavFilePaths));
                else
                    _eventHub.Publish(new DecacheWaveformRequestedEvent(wavFilePaths)); 
            }
        }

        private void OnAudioProjectExplorerNodeSelected(AudioProjectExplorerNodeSelectedEvent e)
        {
            IsSetAudioFilesButtonEnabled = false;
            IsAddAudioFilesButtonEnabled = false;
        }

        private void OnAudioFilesChanged(AudioFilesChangedEvent e) => SetButtonEnablement();

        private void OnPackFileContainerSetAsMainEditable(PackFileContainer packFileContainer)
        {
            if (packFileContainer == null)
            {
                _editablePack = null;
                SelectedTreeNodes.Clear();
                AudioFilesTree = [];
                AudioFilesExplorerLabel = LocalizationManager.Instance.Get(
                    "AudioEditor.Panel.AudioFilesExplorer");
                return;
            }

            _editablePack = packFileContainer;
            AudioFilesExplorerLabel =
                $"{LocalizationManager.Instance.Get("AudioEditor.Panel.AudioFilesExplorer")} - {WpfHelpers.DuplicateUnderscores(packFileContainer.Name)}";
            RefreshAudioFilesTree(packFileContainer);
        }

        private void RefreshAudioFilesTree(PackFileContainer packFileContainer)
        {
            if (!ReferenceEquals(packFileContainer, _editablePack))
                return;

            RemoveIsExpandedHandlers(AudioFilesTree);
            SelectedTreeNodes.Clear();
            _waveformVisualisationCacheService.Clear();
            _eventHub.Publish(new WaveformCacheInvalidatedEvent());
            AudioFilesTree = _audioFilesTreeBuilder.BuildTree(packFileContainer);
            SetupIsExpandedHandlers(AudioFilesTree);
            _audioFilesTreeFilter.Reset();
            if (!string.IsNullOrWhiteSpace(FilterQuery))
                _audioFilesTreeFilter.FilterTree(AudioFilesTree, FilterQuery);
            CacheRootWaveformVisualisations();
        }

        private void RefreshAudioFilesTreeIfCurrent(
            PackFileContainer packFileContainer)
        {
            if (ReferenceEquals(packFileContainer, _editablePack))
                RefreshAudioFilesTree(packFileContainer);
        }

        private void RefreshAudioFilesTreeIfWavChanged(
            PackFileContainer packFileContainer,
            IEnumerable<PackFile> changedFiles)
        {
            if (!ReferenceEquals(packFileContainer, _editablePack))
                return;

            if (changedFiles.Any(file => string.Equals(
                file.Extension,
                ".wav",
                StringComparison.OrdinalIgnoreCase)))
            {
                RefreshAudioFilesTree(packFileContainer);
            }
        }

        private void RemoveIsExpandedHandlers(
            IEnumerable<AudioFilesTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.NodeIsExpandedChanged -= OnNodeIsExpandedChanged;
                if (node.Children is { Count: > 0 })
                    RemoveIsExpandedHandlers(node.Children);
            }
        }

        public void Dispose()
        {
            _filterQueryDebounceCancellationTokenSource?.Cancel();
            _filterQueryDebounceCancellationTokenSource?.Dispose();
            SelectedTreeNodes.CollectionChanged -=
                OnSelectedTreeNodesChanged;
            RemoveIsExpandedHandlers(AudioFilesTree);
            _eventHub.UnRegister(this);
            _globalEventHub.UnRegister(this);
        }

        private void OnSelectedTreeNodesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var selectedWavNodes = GetSelectedWavNodes();

            var wavFilePaths = selectedWavNodes.Count == 1
                ? new List<string> { selectedWavNodes[0].FilePath }
                : [];
            _eventHub.Publish(new AudioFilesExplorerNodeSelectedEvent(wavFilePaths));

            SetButtonEnablement();
        }

        private List<AudioFilesTreeNode> GetSelectedWavNodes()
        {
            if (SelectedTreeNodes == null || SelectedTreeNodes.Count == 0)
                return [];

            var result = new List<AudioFilesTreeNode>();
            foreach (var node in SelectedTreeNodes)
                if (node.Type == AudioFilesTreeNodeType.WavFile)
                    result.Add(node);
            return result;
        }

        private void SetButtonEnablement()
        {
            var selectedWavNodes = GetSelectedWavNodes();
            IsPlayAudioButtonEnabled = selectedWavNodes.Count == 1;

            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode == null)
                return;

            if (selectedWavNodes.Count > 0)
            {
                if (selectedAudioProjectExplorerNode.IsActionEvent()
                    || selectedAudioProjectExplorerNode.IsDialogueEvent())
                {
                    IsSetAudioFilesButtonEnabled = true;
                    IsAddAudioFilesButtonEnabled = _audioEditorStateService.AudioFiles.Count > 0;
                    return;
                }
            }

            IsSetAudioFilesButtonEnabled = false;
            IsAddAudioFilesButtonEnabled = false;
        }

        partial void OnFilterQueryChanged(string value) => DebounceFilterAudioFilesTreeForFilterQuery();

        private void DebounceFilterAudioFilesTreeForFilterQuery()
        {
            _filterQueryDebounceCancellationTokenSource?.Cancel();
            _filterQueryDebounceCancellationTokenSource?.Dispose();

            _filterQueryDebounceCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _filterQueryDebounceCancellationTokenSource.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, cancellationToken);

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null)
                    {
                        _audioFilesTreeFilter.FilterTree(AudioFilesTree, FilterQuery);
                    }
                    else if (!dispatcher.HasShutdownStarted &&
                             !dispatcher.HasShutdownFinished)
                    {
                        dispatcher.Invoke(() =>
                            _audioFilesTreeFilter.FilterTree(
                                AudioFilesTree,
                                FilterQuery));
                    }
                }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException)
                    when (Application.Current?.Dispatcher
                              .HasShutdownStarted == true ||
                          Application.Current?.Dispatcher
                              .HasShutdownFinished == true)
                {
                }
            }, cancellationToken);
        }

        [RelayCommand] public void CollapseOrExpandTree()
        {
            if (AudioFilesTree == null || AudioFilesTree.Count == 0)
                return;

            var isVisibleAndExpanded = AudioFilesTree.Any(node => node.IsVisible && node.IsExpanded);
            foreach (var rootNode in AudioFilesTree)
                ToggleNodeExpansion(rootNode, !isVisibleAndExpanded);
        }

        private static void ToggleNodeExpansion(AudioFilesTreeNode node, bool shouldExpand)
        {
            if (node.IsVisible)
                node.IsExpanded = shouldExpand;

            foreach (var child in node.Children)
                ToggleNodeExpansion(child, shouldExpand);
        }

        [RelayCommand] public void SetAudioFiles()
        {
            var selectedWavs = GetSelectedWavNodes();
            if (selectedWavs.Count == 0)
                return;

            _uiCommandFactory.Create<SetAudioFilesCommand>().Execute(selectedWavs, false);
        }

        [RelayCommand] public void AddToAudioFiles()
        {
            var selectedWavs = GetSelectedWavNodes();
            if (selectedWavs.Count == 0)
                return;

            _uiCommandFactory.Create<SetAudioFilesCommand>().Execute(selectedWavs, true);
        }

        [RelayCommand] public void PlayWav()
        {
            var selectedWavNodes = GetSelectedWavNodes();
            if (selectedWavNodes == null || selectedWavNodes.Count != 1)
                return;

            var wavFilePaths = new List<string> { selectedWavNodes[0].FilePath };
            _eventHub.Publish(new PlayAudioRequestedEvent(wavFilePaths));
        }

        [RelayCommand] public void ClearText() => FilterQuery = string.Empty;
    }
}
