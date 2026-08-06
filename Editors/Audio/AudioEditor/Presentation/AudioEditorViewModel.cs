using System.Windows.Input;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Commands.Dialogs;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Presentation.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Presentation.AudioProjectEditor;
using Editors.Audio.AudioEditor.Presentation.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Presentation.AudioProjectViewer;
using Editors.Audio.AudioEditor.Presentation.Settings;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.Storage;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace Editors.Audio.AudioEditor.Presentation
{
    public partial class AudioEditorViewModel :
        ObservableObject,
        IEditorInterface,
        ISaveableEditor
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IEventHub _eventHub;
        private readonly IAudioEditorStateService _audioEditorStateService;
        private readonly IAudioEditorFileService _audioEditorFileService;
        private readonly IAudioProjectCompilerService _audioProjectCompilerService;
        private readonly IAudioEditorIntegrityService _audioEditorIntegrityService;
        private readonly IShortcutService _shortcutService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ApplicationSettingsService _applicationSettingsService;

        [ObservableProperty] private bool _isAudioProjectLoaded = false;
        [ObservableProperty] private bool _isSettingsBorderVisible = false;
        [ObservableProperty] private bool _isCompiling = false;
        [ObservableProperty] private bool _isLoading = false;
        [ObservableProperty] private bool _isBusy = false;
        [ObservableProperty] private bool _isEditorIdle = true;
        [ObservableProperty] private bool _canEditAudioProject = false;
        [ObservableProperty] private string _compileStatus = string.Empty;
        [ObservableProperty] private string _operationStatus = string.Empty;
        [ObservableProperty] private string _operationDetail = string.Empty;
        [ObservableProperty] private int _operationProgressValue;
        [ObservableProperty] private int _operationProgressMaximum;
        [ObservableProperty] private bool _operationProgressIsIndeterminate = true;
        [ObservableProperty] private string _emptyStateText =
            LocalizationManager.Instance.Get("AudioEditor.Empty");
        [ObservableProperty] private bool _hasUnsavedChanges;

        public AudioEditorViewModel(
            IUiCommandFactory uiCommandFactory,
            IEventHub eventHub,
            IAudioEditorStateService audioEditorStateService,
            IAudioEditorFileService audioEditorFileService,
            IAudioProjectCompilerService audioProjectCompilerService,
            IAudioEditorIntegrityService audioEditorIntegrityService,
            IShortcutService shortcutService,
            IStandardDialogs standardDialogs,
            ApplicationSettingsService applicationSettingsService,
            AudioProjectExplorerViewModel audioProjectExplorerViewModel,
            AudioFilesExplorerViewModel audioFilesExplorerViewModel,
            AudioProjectEditorViewModel audioProjectEditorViewModel,
            AudioProjectViewerViewModel audioProjectViewerViewModel,
            SettingsViewModel settingsViewModel,
            WaveformVisualiserViewModel waveformVisualiserViewModel)
        {
            _uiCommandFactory = uiCommandFactory;
            _eventHub = eventHub;
            _audioEditorStateService = audioEditorStateService;
            _audioEditorFileService = audioEditorFileService;
            _audioProjectCompilerService = audioProjectCompilerService;
            _audioEditorIntegrityService = audioEditorIntegrityService;
            _shortcutService = shortcutService;
            _standardDialogs = standardDialogs;
            _applicationSettingsService = applicationSettingsService;

            AudioProjectExplorerViewModel = audioProjectExplorerViewModel;
            AudioFilesExplorerViewModel = audioFilesExplorerViewModel;
            AudioProjectEditorViewModel = audioProjectEditorViewModel;
            AudioProjectViewerViewModel = audioProjectViewerViewModel;
            SettingsViewModel = settingsViewModel;
            WaveformVisualiserViewModel = waveformVisualiserViewModel;

            _eventHub.Register<AudioProjectLoadedEvent>(this, OnAudioProjectLoaded);
            _eventHub.Register<AudioProjectChangedEvent>(
                this,
                _ => HasUnsavedChanges = true);
            _eventHub.Register<AudioProjectExplorerNodeSelectedEvent>(this, OnAudioProjectExplorerNodeSelected);
        }

        public AudioProjectExplorerViewModel AudioProjectExplorerViewModel { get; }
        public AudioFilesExplorerViewModel AudioFilesExplorerViewModel { get; }
        public AudioProjectEditorViewModel AudioProjectEditorViewModel { get; }
        public AudioProjectViewerViewModel AudioProjectViewerViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public WaveformVisualiserViewModel WaveformVisualiserViewModel { get; }

        public string DisplayName { get; set; } = LocalizationManager.Instance.Get("DisplayName.AudioEditor");

        private void OnAudioProjectLoaded(AudioProjectLoadedEvent e)
        {
            IsAudioProjectLoaded = true;
            if (!e.IsRecovery)
                HasUnsavedChanges = false;
        }

        partial void OnIsAudioProjectLoadedChanged(bool value) =>
            UpdateBusyState();

        partial void OnIsCompilingChanged(bool value) =>
            UpdateBusyState();

        partial void OnIsLoadingChanged(bool value) =>
            UpdateBusyState();

        private void UpdateBusyState()
        {
            IsBusy = IsCompiling || IsLoading;
            IsEditorIdle = !IsBusy;
            CanEditAudioProject = IsAudioProjectLoaded && !IsBusy;
        }

        private void OnAudioProjectExplorerNodeSelected(AudioProjectExplorerNodeSelectedEvent e)
        {
            if (e.TreeNode == null)
            {
                IsSettingsBorderVisible = false;
                return;
            }

            if (e.TreeNode.IsActionEvent() || e.TreeNode.IsDialogueEvent() || e.TreeNode.IsStateGroup())
                IsSettingsBorderVisible = true;
            else
                IsSettingsBorderVisible = false;
        }

        [RelayCommand]
        public void NewAudioProject()
        {
            if (IsEditorIdle &&
                IsSupportedGame() &&
                CanReplaceCurrentProject())
            {
                _uiCommandFactory.Create<OpenNewAudioProjectWindowCommand>().Execute();
            }
        }

        [RelayCommand] public void SaveAudioProject()
        {
            Save();
        }

        public bool Save()
        {
            if (!IsAudioProjectLoaded ||
                _audioEditorStateService.AudioProject == null)
            {
                return false;
            }

            var saved = _audioEditorFileService.Save(
                _audioEditorStateService.AudioProject,
                _audioEditorStateService.AudioProjectFileName,
                _audioEditorStateService.AudioProjectFilePath);
            if (saved)
                HasUnsavedChanges = false;

            return saved;
        }

        [RelayCommand(IncludeCancelCommand = true)]
        public async Task LoadAudioProject(
            CancellationToken cancellationToken = default)
        {
            if (!IsEditorIdle ||
                !IsSupportedGame() ||
                !CanReplaceCurrentProject())
            {
                return;
            }

            IsLoading = true;
            CompileStatus = LocalizationManager.Instance.Get(
                "AudioEditor.Load.InProgress");
            OperationStatus = LocalizationManager.Instance.Get(
                "OperationProgress.AudioLoad");
            OperationDetail = CompileStatus;
            OperationProgressValue = 0;
            OperationProgressMaximum = 0;
            OperationProgressIsIndeterminate = true;
            EmptyStateText = CompileStatus;
            var progress = new Progress<AudioLoadProgress>(
                loadProgress =>
                {
                    var status = LocalizationManager.Instance.GetFormat(
                        "AudioEditor.Load.Progress",
                        loadProgress.Completed,
                        loadProgress.Total,
                        loadProgress.CurrentFile);
                    CompileStatus = status;
                    EmptyStateText = status;
                    OperationDetail = Path.GetFileName(
                        loadProgress.CurrentFile);
                    OperationProgressValue = loadProgress.Completed;
                    OperationProgressMaximum = loadProgress.Total;
                    OperationProgressIsIndeterminate =
                        loadProgress.Total <= 0;
                });
            try
            {
                var loaded = await _audioEditorFileService
                    .LoadFromDialogAsync(progress, cancellationToken);
                if (loaded)
                {
                    CompileStatus = LocalizationManager.Instance.Get(
                        "AudioEditor.Load.Completed");
                }
                else
                {
                    CompileStatus = string.Empty;
                    EmptyStateText = LocalizationManager.Instance.Get(
                        "AudioEditor.Empty");
                }
            }
            catch (OperationCanceledException)
            {
                CompileStatus = LocalizationManager.Instance.Get(
                    "AudioEditor.Load.Cancelled");
                EmptyStateText = CompileStatus;
            }
            catch (Exception exception)
            {
                CompileStatus = LocalizationManager.Instance.Get(
                    "AudioEditor.Load.Failed");
                EmptyStateText = CompileStatus;
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "Msg.AudioProjectLoadFailed",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand(IncludeCancelCommand = true)]
        public async Task CompileAudioProject(
            CancellationToken cancellationToken = default)
        {
            if (!CanEditAudioProject)
                return;

            IsCompiling = true;
            CompileStatus = LocalizationManager.Instance.Get(
                "AudioEditor.Compile.InProgress");
            OperationStatus = LocalizationManager.Instance.Get(
                "OperationProgress.AudioCompile");
            OperationDetail = CompileStatus;
            OperationProgressValue = 0;
            OperationProgressMaximum = 0;
            OperationProgressIsIndeterminate = true;
            try
            {
                if (!Save())
                {
                    CompileStatus = LocalizationManager.Instance.Get(
                        "AudioEditor.Compile.Cancelled");
                    return;
                }

                var cleanAudioProject = _audioEditorStateService.AudioProject.Clean();
                var fileName = _audioEditorStateService.AudioProjectFileName;
                var filePath = _audioEditorStateService.AudioProjectFilePath;
                var progress = new Progress<AudioOperationProgress>(
                    UpdateOperationProgress);
                var compileCompleted =
                    _audioProjectCompilerService is
                        IAudioProjectCompilerProgressService progressService
                    ? await progressService.CompileAsync(
                        cleanAudioProject,
                        fileName,
                        filePath,
                        progress,
                        cancellationToken)
                    : await _audioProjectCompilerService.CompileAsync(
                        cleanAudioProject,
                        fileName,
                        filePath,
                        cancellationToken);
                CompileStatus = LocalizationManager.Instance.Get(
                    compileCompleted
                        ? "AudioEditor.Compile.Completed"
                        : "AudioEditor.Compile.Cancelled");
            }
            catch (OperationCanceledException)
            {
                CompileStatus = LocalizationManager.Instance.Get(
                    "AudioEditor.Compile.Cancelled");
            }
            catch (Exception exception)
            {
                CompileStatus = LocalizationManager.Instance.Get(
                    "AudioEditor.Compile.Failed");
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "Msg.AudioProjectCompileFailed",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            finally
            {
                IsCompiling = false;
            }
        }

        private void UpdateOperationProgress(AudioOperationProgress progress)
        {
            OperationStatus = LocalizationManager.Instance.Get(
                progress.StageResourceKey);
            OperationDetail = progress.Detail;
            OperationProgressValue = progress.Completed;
            OperationProgressMaximum = progress.Total;
            OperationProgressIsIndeterminate = progress.Total <= 0;
        }

        [RelayCommand]
        public void OpenDialogueEventMerger()
        {
            if (IsEditorIdle && IsSupportedGame())
            {
                _uiCommandFactory
                    .Create<OpenDialogueEventMergerWindowCommand>()
                    .Execute();
            }
        }

        [RelayCommand]
        public void OpenAudioProjectMerger()
        {
            if (IsEditorIdle && IsSupportedGame())
            {
                _uiCommandFactory
                    .Create<OpenAudioProjectMergerWindowCommand>()
                    .Execute();
            }
        }

        [RelayCommand]
        public void OpenAudioProjectConverter()
        {
            if (IsEditorIdle && IsSupportedGame())
            {
                _uiCommandFactory
                    .Create<OpenAudioProjectConverterWindowCommand>()
                    .Execute();
            }
        }

        [RelayCommand] public void RefreshSourceIds()
        {
            if (!CanEditAudioProject ||
                _audioEditorStateService.AudioProject == null)
            {
                return;
            }

            if (_standardDialogs.ShowYesNoBox(
                    LocalizationManager.Instance.Get(
                        "Msg.RefreshSourceIdsConfirmation"),
                    LocalizationManager.Instance.Get("Msg.AreYouSure")) !=
                ShowMessageBoxResult.OK)
            {
                return;
            }

            _audioEditorIntegrityService.RefreshSourceIds(_audioEditorStateService.AudioProject);
            _eventHub.Publish(new AudioProjectChangedEvent());
        }

        public void OnPreviewKeyDown(
            KeyEventArgs e,
            AudioEditorFocusTarget focusTarget,
            bool isTextInputFocussed,
            bool isSettingsAudioFilesListViewFocussed)
        {
            _shortcutService.HandleShortcut(
                e,
                focusTarget,
                isTextInputFocussed,
                isSettingsAudioFilesListViewFocussed);
        }

        public void Close()
        {
            LoadAudioProjectCommand.Cancel();
            CompileAudioProjectCommand.Cancel();
            _eventHub.UnRegister(this);
            _audioEditorStateService.Reset();
        }

        private bool CanReplaceCurrentProject()
        {
            if (!HasUnsavedChanges)
                return true;

            return _standardDialogs.ShowYesNoBox(
                LocalizationManager.Instance.Get(
                    "Msg.UnsavedAudioProjectChanges"),
                LocalizationManager.Instance.Get("Msg.AreYouSure")) ==
                ShowMessageBoxResult.OK;
        }

        private bool IsSupportedGame()
        {
            if (_applicationSettingsService.CurrentSettings.CurrentGame ==
                GameTypeEnum.Warhammer3)
            {
                return true;
            }

            _standardDialogs.ShowDialogBox(
                LocalizationManager.Instance.Get(
                    "Msg.AudioEditorUnsupportedGame"),
                LocalizationManager.Instance.Get("Msg.GeneralError"));
            return false;
        }
    }
}
