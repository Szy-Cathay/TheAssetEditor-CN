using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Editors.Audio.AudioEditor.Core
{
    public interface IAudioEditorFileService
    {
        bool Save(
            AudioProjectFile audioProject,
            string fileName,
            string filePath,
            bool promptOnConflict = false);
        AudioPackOutput CreateOutput(
            AudioProjectFile audioProject,
            string fileName,
            string filePath);
        void Load(AudioProjectFile audioProject, string fileName, string filePath, bool isNotLoadedFromDialog = true);
        void LoadFromDialog();
        Task<bool> LoadAsync(
            AudioProjectFile audioProject,
            string fileName,
            string filePath,
            bool isNotLoadedFromDialog = true,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default);
        Task<bool> LoadFromDialogAsync(
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default,
            System.Action loadStarted = null);
    }

    public class AudioEditorFileService(
        IEventHub eventHub,
        IAudioProjectFileService audioProjectFileService,
        IAudioEditorStateService audioEditorStateService,
        IAudioPackOutputService audioPackOutputService,
        IAudioRepository audioRepository,
        IAudioEditorIntegrityService audioEditorIntegrityService,
        ApplicationSettingsService applicationSettingsService) : IAudioEditorFileService
    {
        private readonly IEventHub _eventHub = eventHub;
        private readonly IAudioProjectFileService _audioProjectFileService = audioProjectFileService;
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IAudioPackOutputService _audioPackOutputService =
            audioPackOutputService;
        private readonly IAudioRepository _audioRepository = audioRepository;
        private readonly IAudioEditorIntegrityService _audioEditorIntegrityService = audioEditorIntegrityService;
        private readonly ApplicationSettingsService _applicationSettingsService = applicationSettingsService;

        public bool Save(
            AudioProjectFile audioProject,
            string fileName,
            string filePath,
            bool promptOnConflict = false)
        {
            var output = CreateOutput(audioProject, fileName, filePath);
            return _audioPackOutputService.SaveBatch(
                [output],
                promptOnConflict);
        }

        public AudioPackOutput CreateOutput(
            AudioProjectFile audioProject,
            string fileName,
            string filePath)
        {
            var cleanedAudioProject = audioProject.Clean();
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };
            var audioProjectJson = JsonSerializer.Serialize(cleanedAudioProject, options);

            return new AudioPackOutput(
                fileName,
                filePath,
                Encoding.UTF8.GetBytes(audioProjectJson));
        }

        public void LoadFromDialog()
        {
            var result = _audioProjectFileService.LoadFromDialog();
            if (result != null)
                Load(result.AudioProject, result.FileName, result.FilePath, false);
        }

        public void Load(AudioProjectFile audioProject, string fileName, string filePath, bool isNotLoadedFromDialog = true)
        {
            if (!ValidateFileName(fileName))
                return;

            var preparedLoad = PrepareLoad(
                audioProject,
                fileName,
                filePath,
                isNotLoadedFromDialog,
                null,
                CancellationToken.None);
            CompleteLoad(
                preparedLoad,
                fileName,
                filePath,
                CancellationToken.None);
        }

        public async Task<bool> LoadFromDialogAsync(
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default,
            System.Action loadStarted = null)
        {
            var result = _audioProjectFileService.LoadFromDialog();
            if (result == null)
                return false;

            loadStarted?.Invoke();
            return await LoadAsync(
                result.AudioProject,
                result.FileName,
                result.FilePath,
                false,
                progress,
                cancellationToken);
        }

        public async Task<bool> LoadAsync(
            AudioProjectFile audioProject,
            string fileName,
            string filePath,
            bool isNotLoadedFromDialog = true,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!ValidateFileName(fileName))
                return false;

            var preparedLoad = await Task.Run(
                () => PrepareLoad(
                    audioProject,
                    fileName,
                    filePath,
                    isNotLoadedFromDialog,
                    progress,
                    cancellationToken),
                cancellationToken);
            CompleteLoad(
                preparedLoad,
                fileName,
                filePath,
                cancellationToken);
            return true;
        }

        private PreparedAudioProjectLoad PrepareLoad(
            AudioProjectFile audioProject,
            string fileName,
            string filePath,
            bool isNotLoadedFromDialog,
            IProgress<AudioLoadProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isNotLoadedFromDialog)
            {
                var result = _audioProjectFileService.Load(
                    fileName,
                    filePath);
                audioProject = result.AudioProject;
            }

            var fileNameWithoutExtension =
                Path.GetFileNameWithoutExtension(fileName);
            _audioEditorIntegrityService.EnsureCorrectSoundBankNames(
                audioProject,
                fileNameWithoutExtension);

            var currentGame =
                _applicationSettingsService.CurrentSettings.CurrentGame;
            if (currentGame != GameTypeEnum.Warhammer3)
            {
                throw new NotImplementedException(
                    "The Audio Editor does not support the selected game.");
            }

            var dirtyAudioProject = AudioProjectFile.CreateNew(
                audioProject.Language,
                fileNameWithoutExtension);
            AudioProjectFileMerger.Merge(
                dirtyAudioProject,
                audioProject,
                fileNameWithoutExtension,
                fileNameWithoutExtension);
            var repositorySnapshot = _audioRepository.CreateSnapshot();
            try
            {
                _audioRepository.Load(
                    [audioProject.Language],
                    progress,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                return new PreparedAudioProjectLoad(
                    dirtyAudioProject,
                    repositorySnapshot,
                    fileNameWithoutExtension);
            }
            catch
            {
                _audioRepository.Restore(repositorySnapshot);
                throw;
            }
        }

        private void CompleteLoad(
            PreparedAudioProjectLoad preparedLoad,
            string fileName,
            string filePath,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _audioEditorIntegrityService
                    .CheckDialogueEventInformationIntegrity(
                        Wh3DialogueEventInformation.Information);
                cancellationToken.ThrowIfCancellationRequested();
                _audioEditorIntegrityService
                    .CheckAudioProjectDialogueEventIntegrity(
                        preparedLoad.AudioProject);
                cancellationToken.ThrowIfCancellationRequested();
                _audioEditorIntegrityService
                    .CheckAudioProjectWavFilesIntegrity(
                        preparedLoad.AudioProject);
                cancellationToken.ThrowIfCancellationRequested();
                _audioEditorIntegrityService
                    .CheckAudioProjectDataIntegrity(
                        preparedLoad.AudioProject,
                        preparedLoad.FileNameWithoutExtension);
                cancellationToken.ThrowIfCancellationRequested();
                CommitLoad(preparedLoad.AudioProject, fileName, filePath);
            }
            catch
            {
                _audioRepository.Restore(
                    preparedLoad.RepositorySnapshot);
                throw;
            }
        }

        private void CommitLoad(
            AudioProjectFile audioProject,
            string fileName,
            string filePath)
        {
            var previousAudioProject =
                _audioEditorStateService.AudioProject;
            var previousFileName =
                _audioEditorStateService.AudioProjectFileName;
            var previousFilePath =
                _audioEditorStateService.AudioProjectFilePath;
            try
            {
                _audioEditorStateService.StoreAudioProject(audioProject);
                _audioEditorStateService.StoreAudioProjectFileName(fileName);
                _audioEditorStateService.StoreAudioProjectFilePath(filePath);
                _eventHub.Publish(new AudioProjectLoadedEvent());
            }
            catch
            {
                _audioEditorStateService.StoreAudioProject(
                    previousAudioProject);
                _audioEditorStateService.StoreAudioProjectFileName(
                    previousFileName);
                _audioEditorStateService.StoreAudioProjectFilePath(
                    previousFilePath);
                throw;
            }
        }

        private static bool ValidateFileName(string fileName)
        {
            if (!fileName.Contains(' '))
                return true;

            MessageBox.Show(
                LocalizationManager.Instance.Get(
                    "Msg.AudioProjectNameSpaces"),
                LocalizationManager.Instance.Get("Msg.GeneralError"));
            return false;
        }

        private sealed record PreparedAudioProjectLoad(
            AudioProjectFile AudioProject,
            AudioRepositorySnapshot RepositorySnapshot,
            string FileNameWithoutExtension);
    }
}
