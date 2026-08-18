using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Editors.Audio.AudioEditor.Presentation.NewAudioProject
{
    public partial class NewAudioProjectViewModel : ObservableObject
    {
        private readonly IPackFileService _packFileService;
        private readonly IAudioEditorFileService _audioEditorFileService;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IStandardDialogs _standardDialogs;

        readonly ILogger _logger = Logging.Create<NewAudioProjectViewModel>();
        private System.Action _closeAction;
        private bool _closeWhenIdle;

        [ObservableProperty] private string _audioProjectFileName;
        [ObservableProperty] private string _audioProjectDirectory;
        [ObservableProperty] private Wh3Language _selectedLanguage;
        [ObservableProperty] private ObservableCollection<Wh3Language> _languages = new (
            Enum.GetValues<Wh3Language>()
                .Where(language => language != Wh3Language.Sfx));

        [ObservableProperty] private bool _isAudioProjectFileNameSet;
        [ObservableProperty] private bool _isAudioProjectDirectorySet;
        [ObservableProperty] private bool _isLanguageSelected;
        [ObservableProperty] private bool _isOkButtonEnabled;
        [ObservableProperty] private bool _isCreating;
        [ObservableProperty] private string _validationMessage = string.Empty;
        [ObservableProperty] private string _creationStatus = string.Empty;
        [ObservableProperty] private string _creationProgressDetail = string.Empty;
        [ObservableProperty] private int _creationProgressValue;
        [ObservableProperty] private int _creationProgressMaximum;
        [ObservableProperty] private bool _creationProgressIsIndeterminate = true;

        public NewAudioProjectViewModel(
            IPackFileService packFileService,
            IAudioEditorFileService audioEditorFileService,
            ApplicationSettingsService applicationSettingsService,
            IStandardDialogs standardDialogs)
        {
            _packFileService = packFileService;
            _audioEditorFileService = audioEditorFileService;
            _applicationSettingsService = applicationSettingsService;
            _standardDialogs = standardDialogs;

            AudioProjectDirectory = "audio\\audio_projects";
            SelectedLanguage = Wh3Language.EnglishUK;
        }

        partial void OnAudioProjectFileNameChanged(string value)
        {
            IsAudioProjectFileNameSet =
                AudioProjectNameValidator.TryNormalize(value, out _);
            ValidationMessage = IsAudioProjectFileNameSet
                ? string.Empty
                : LocalizationManager.Instance.Get(
                    "NewAudioProject.InvalidName");
            UpdateOkButtonIsEnabled();
        }

        partial void OnAudioProjectDirectoryChanged(string value)
        {
            IsAudioProjectDirectorySet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnSelectedLanguageChanged(Wh3Language value)
        {
            IsLanguageSelected = !string.IsNullOrEmpty(value.ToString());
            UpdateOkButtonIsEnabled();
        }

        private void UpdateOkButtonIsEnabled()
        {
            IsOkButtonEnabled =
                IsAudioProjectFileNameSet &&
                IsAudioProjectDirectorySet &&
                IsLanguageSelected &&
                !IsCreating;
        }

        partial void OnIsCreatingChanged(bool value) =>
            UpdateOkButtonIsEnabled();

        [RelayCommand] public void SetNewFileLocation()
        {
            var result = _standardDialogs.DisplayBrowseFolderDialog();
            if (result.Result)
            {
                var filePath = result.Folder;
                AudioProjectDirectory = filePath;
                _logger.Here().Information($"Audio Project directory set to: {filePath}");
            }
        }

        [RelayCommand(IncludeCancelCommand = true)]
        public async Task CreateAudioProject(
            CancellationToken cancellationToken = default)
        {
            if (_packFileService.GetEditablePack() == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("Msg.NoEditablePack"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

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
                    AudioProjectFileName,
                    out var normalizedName))
            {
                ValidationMessage = LocalizationManager.Instance.Get(
                    "NewAudioProject.InvalidName");
                return;
            }

            var fileName = $"{normalizedName}.aproj";
            var filePath = $"{AudioProjectDirectory}\\{fileName}";
            var language = Wh3LanguageInformation.GetLanguageAsString(SelectedLanguage);

            var audioProject = AudioProjectFile.CreateNew(
                language,
                normalizedName);
            var closeWhenComplete = false;
            IsCreating = true;
            CreationStatus = LocalizationManager.Instance.Get(
                "NewAudioProject.Creating");
            CreationProgressDetail = CreationStatus;
            CreationProgressValue = 0;
            CreationProgressMaximum = 0;
            CreationProgressIsIndeterminate = true;
            var progress = new Progress<AudioLoadProgress>(
                loadProgress =>
                {
                    CreationStatus = LocalizationManager.Instance.GetFormat(
                        "AudioEditor.Load.Progress",
                        loadProgress.Completed,
                        loadProgress.Total,
                        loadProgress.CurrentFile);
                    CreationProgressDetail = Path.GetFileName(
                        loadProgress.CurrentFile);
                    CreationProgressValue = loadProgress.Completed;
                    CreationProgressMaximum = loadProgress.Total;
                    CreationProgressIsIndeterminate =
                        loadProgress.Total <= 0;
                });
            try
            {
                if (!_audioEditorFileService.Save(
                        audioProject,
                        fileName,
                        filePath,
                        true))
                {
                    return;
                }

                closeWhenComplete = await _audioEditorFileService.LoadAsync(
                    audioProject,
                    fileName,
                    filePath,
                    false,
                    progress,
                    cancellationToken);
                if (closeWhenComplete)
                {
                    CreationStatus = LocalizationManager.Instance.Get(
                        "AudioEditor.Load.Completed");
                }
            }
            catch (OperationCanceledException)
            {
                CreationStatus = LocalizationManager.Instance.Get(
                    "AudioEditor.Load.Cancelled");
            }
            catch (Exception exception)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "Msg.AudioProjectCreateFailed",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            finally
            {
                IsCreating = false;
                if (closeWhenComplete || _closeWhenIdle)
                    CloseWindowAction();
            }
        }

        [RelayCommand]
        public void CancelOrClose()
        {
            if (IsCreating)
            {
                _closeWhenIdle = true;
                CreateAudioProjectCommand.Cancel();
                return;
            }

            CloseWindowAction();
        }

        [RelayCommand] public void CloseWindowAction() => _closeAction?.Invoke();

        public void SetCloseAction(System.Action closeAction) => _closeAction = closeAction;
    }
}
