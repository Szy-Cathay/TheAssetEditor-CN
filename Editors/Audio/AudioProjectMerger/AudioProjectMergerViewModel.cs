using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.Audio.AudioProjectMerger
{
    public partial class AudioProjectMergerViewModel(
        IStandardDialogs standardDialogs,
        IPackFileService packFileService,
        IAudioProjectFileService audioProjectFileService,
        IAudioEditorFileService audioEditorFileService) : ObservableObject
    {
        private readonly IStandardDialogs _standardDialogs = standardDialogs;
        private readonly IPackFileService _packFileService = packFileService;
        private readonly IAudioProjectFileService _audioProjectFileService = audioProjectFileService;
        private readonly IAudioEditorFileService _audioEditorFileService = audioEditorFileService;

        private System.Action _closeAction;

        [ObservableProperty] private string _mergedAudioProjectName;
        [ObservableProperty] private string _outputDirectoryPath;
        [ObservableProperty] private string _baseAudioProjectPath;
        [ObservableProperty] private string _mergingAudioProjectPath;

        [ObservableProperty] private bool _isMergedAudioProjectNameSet;
        [ObservableProperty] private bool _isOutputDirectoryPathSet;
        [ObservableProperty] private bool _isBaseAudioProjectPathSet;
        [ObservableProperty] private bool _isMergingAudioProjectPathSet; 
        [ObservableProperty] private bool _isOkButtonEnabled;

        [RelayCommand] public void MergeAudioProjects()
        {
            try
            {
                if (!AudioProjectNameValidator.TryNormalize(
                        MergedAudioProjectName,
                        out var normalizedName))
                {
                    throw new InvalidDataException(
                        LocalizationManager.Instance.Get(
                            "NewAudioProject.InvalidName"));
                }

                if (string.Equals(
                        BaseAudioProjectPath,
                        MergingAudioProjectPath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        LocalizationManager.Instance.Get(
                            "AudioProjectMerger.SameInput"));
                }

                var baseAudioProjectFileName =
                    Path.GetFileNameWithoutExtension(
                        BaseAudioProjectPath);
                var baseAudioProjectPackFile =
                    _packFileService.FindFile(BaseAudioProjectPath) ??
                    throw new FileNotFoundException(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.PackFileNotFound",
                            BaseAudioProjectPath));
                var baseAudioProject =
                    _audioProjectFileService.DeserialiseAudioProject(
                        baseAudioProjectPackFile);

                var mergingAudioProjectFileName =
                    Path.GetFileNameWithoutExtension(
                        MergingAudioProjectPath);
                var mergingAudioProjectPackFile =
                    _packFileService.FindFile(MergingAudioProjectPath) ??
                    throw new FileNotFoundException(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.PackFileNotFound",
                            MergingAudioProjectPath));
                var mergingAudioProject =
                    _audioProjectFileService.DeserialiseAudioProject(
                        mergingAudioProjectPackFile);

                if (!string.Equals(
                        baseAudioProject.Language,
                        mergingAudioProject.Language,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        LocalizationManager.Instance.Get(
                            "AudioProjectMerger.LanguageMismatch"));
                }

                AudioProjectFileMerger.Merge(
                    baseAudioProject,
                    mergingAudioProject,
                    baseAudioProjectFileName,
                    mergingAudioProjectFileName);

                var mergedAudioProjectFileName =
                    $"{normalizedName}.aproj";
                var mergedAudioProjectFilePath =
                    $"{OutputDirectoryPath}\\{mergedAudioProjectFileName}";
                if (_audioEditorFileService.Save(
                        baseAudioProject,
                        mergedAudioProjectFileName,
                        mergedAudioProjectFilePath,
                        true))
                {
                    CloseWindowAction();
                }
            }
            catch (System.Exception exception)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "AudioProjectMerger.Failed",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
        }

        partial void OnMergedAudioProjectNameChanged(string value)
        {
            IsMergedAudioProjectNameSet =
                AudioProjectNameValidator.TryNormalize(value, out _);
            UpdateOkButtonIsEnabled();
        }

        partial void OnOutputDirectoryPathChanged(string value)
        {
            IsOutputDirectoryPathSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnBaseAudioProjectPathChanged(string value)
        {
            IsBaseAudioProjectPathSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        partial void OnMergingAudioProjectPathChanged(string value)
        {
            IsMergingAudioProjectPathSet =
                !string.IsNullOrWhiteSpace(value);
            UpdateOkButtonIsEnabled();
        }

        private void UpdateOkButtonIsEnabled()
        {
            IsOkButtonEnabled = IsMergedAudioProjectNameSet 
                && IsOutputDirectoryPathSet 
                && IsBaseAudioProjectPathSet 
                && IsMergingAudioProjectPathSet
                && !string.Equals(
                    BaseAudioProjectPath,
                    MergingAudioProjectPath,
                    System.StringComparison.OrdinalIgnoreCase);
        }

        [RelayCommand] public void SetOutputDirectoryPath()
        {
            var result = _standardDialogs.DisplayBrowseFolderDialog();
            if (result.Result)
            {
                var filePath = result.Folder;
                OutputDirectoryPath = filePath;
            }
        }

        [RelayCommand] public void SetBaseAudioProjectPath()
        {
            var result = _standardDialogs.DisplayBrowseDialog([".aproj"]);
            if (result.Result)
                BaseAudioProjectPath = _packFileService.GetFullPath(result.File);
        }

        [RelayCommand] public void SetMergingAudioProjectPath()
        {
            var result = _standardDialogs.DisplayBrowseDialog([".aproj"]);
            if (result.Result)
                MergingAudioProjectPath = _packFileService.GetFullPath(result.File);
        }

        [RelayCommand] public void CloseWindowAction() => _closeAction?.Invoke();

        public void SetCloseAction(System.Action closeAction) =>_closeAction = closeAction;
    }
}
