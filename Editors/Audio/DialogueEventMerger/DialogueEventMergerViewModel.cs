using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.Generators;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Services;

namespace Editors.Audio.DialogueEventMerger
{
    public partial class DialogueEventMergerViewModel : ObservableObject
    {
        private readonly IAudioRepository _audioRepository;
        private readonly ISoundBankGeneratorService _soundBankGeneratorService;
        private readonly IStandardDialogs _standardDialogs;

        private readonly ILogger _logger =
            Logging.Create<DialogueEventMergerViewModel>();
        private Action _closeAction;
        private Func<Task> _completeProgressAction =
            () => Task.CompletedTask;

        [ObservableProperty] private string _soundBankSuffix;
        [ObservableProperty] private bool _isSoundBankSuffixSet;
        [ObservableProperty] private bool _isOkButtonEnabled;
        [ObservableProperty] private ObservableCollection<string> _selectedModdedSoundBanks = [];
        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private bool _isGenerating;
        [ObservableProperty] private string _loadStatus =
            LocalizationManager.Instance.Get(
                "DialogueEventMerger.Loading");
        [ObservableProperty] private string _progressDetail = string.Empty;
        [ObservableProperty] private int _progressValue;
        [ObservableProperty] private int _progressMaximum;
        [ObservableProperty] private bool _progressIsIndeterminate = true;
        [ObservableProperty] private string _soundBankSuffixError = string.Empty;
        public ObservableCollection<ModdedSoundBank> ModdedSoundBanks { get; } = [];
        private bool _isInitialised;
        private bool _isRepositoryLoaded;
        private CancellationToken _lifetimeCancellationToken;

        public bool IsBusy => IsLoading || IsGenerating;

        public DialogueEventMergerViewModel(
            IAudioRepository audioRepository,
            ISoundBankGeneratorService soundBankGeneratorService,
            IStandardDialogs standardDialogs)
        {
            _audioRepository = audioRepository;
            _soundBankGeneratorService = soundBankGeneratorService;
            _standardDialogs = standardDialogs;

            ModdedSoundBanks.CollectionChanged += OnModdedSoundBanksCollectionChanged;
        }

        public async Task InitializeAsync(
            CancellationToken cancellationToken)
        {
            _lifetimeCancellationToken = cancellationToken;
            if (_isInitialised)
                return;

            _isInitialised = true;
            IsLoading = true;
            LoadStatus = LocalizationManager.Instance.Get(
                "DialogueEventMerger.Loading");
            ProgressDetail = LoadStatus;
            ProgressValue = 0;
            ProgressMaximum = 0;
            ProgressIsIndeterminate = true;
            UpdateOkButtonIsEnabled();
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LoadStatus = LocalizationManager.Instance.Get(
                        "DialogueEventMerger.Cancelled");
                    return;
                }

                var progress = new Progress<AudioLoadProgress>(value =>
                {
                    ProgressDetail = Path.GetFileName(value.CurrentFile);
                    ProgressValue = value.Completed;
                    ProgressMaximum = value.Total;
                    ProgressIsIndeterminate = value.Total <= 0;
                });
                var loadResult = await Task.Run(() =>
                {
                    try
                    {
                        var soundBanks = _audioRepository
                            .LoadDialogueEventMergerData(
                                "for_merging",
                                progress,
                                cancellationToken);
                        return (Cancelled: false, SoundBanks: soundBanks);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        return (
                            Cancelled: true,
                            SoundBanks: new System.Collections.Generic.List<string>());
                    }
                });
                if (loadResult.Cancelled ||
                    cancellationToken.IsCancellationRequested)
                {
                    LoadStatus = LocalizationManager.Instance.Get(
                        "DialogueEventMerger.Cancelled");
                    return;
                }

                foreach (var path in loadResult.SoundBanks)
                {
                    ModdedSoundBanks.Add(
                        new ModdedSoundBank(path, isChecked: true));
                }

                SetSelectedModdedSoundBanks();
                _isRepositoryLoaded = true;
                LoadStatus = ModdedSoundBanks.Count == 0
                    ? LocalizationManager.Instance.Get(
                        "DialogueEventMerger.Empty")
                    : string.Empty;
            }
            catch (OperationCanceledException)
            {
                LoadStatus = LocalizationManager.Instance.Get(
                    "DialogueEventMerger.Cancelled");
            }
            catch (Exception exception)
            {
                LoadStatus = LocalizationManager.Instance.GetFormat(
                    "DialogueEventMerger.LoadFailed.Detail",
                    exception.Message);
            }
            finally
            {
                IsLoading = false;
                UpdateOkButtonIsEnabled();
            }
        }

        partial void OnSoundBankSuffixChanged(string value)
        {
            IsSoundBankSuffixSet = TryNormalizeSoundBankSuffix(
                value,
                out _);
            SoundBankSuffixError =
                string.IsNullOrWhiteSpace(value) ||
                IsSoundBankSuffixSet
                    ? string.Empty
                    : LocalizationManager.Instance.Get(
                        "DialogueEventMerger.InvalidSuffix");
            UpdateOkButtonIsEnabled();
        }

        private void UpdateOkButtonIsEnabled()
        {
            IsOkButtonEnabled =
                _isRepositoryLoaded &&
                !IsBusy &&
                IsSoundBankSuffixSet &&
                SelectedModdedSoundBanks.Any();
        }

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
            UpdateOkButtonIsEnabled();
        }

        partial void OnIsGeneratingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
            UpdateOkButtonIsEnabled();
        }

        private void OnModdedSoundBanksCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (ModdedSoundBank item in e.NewItems)
                    item.PropertyChanged += OnModdedSoundBankPropertyChanged;
            }

            if (e.OldItems != null)
            {
                foreach (ModdedSoundBank item in e.OldItems)
                    item.PropertyChanged -= OnModdedSoundBankPropertyChanged;
            }

            SetSelectedModdedSoundBanks();
        }

        private void OnModdedSoundBankPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModdedSoundBank.IsChecked))
                SetSelectedModdedSoundBanks();
        }

        private void SetSelectedModdedSoundBanks()
        {
            SelectedModdedSoundBanks = new ObservableCollection<string>(ModdedSoundBanks.Where(x => x.IsChecked).Select(x => x.FilePath));
            UpdateOkButtonIsEnabled();
        }

        [RelayCommand]
        public async Task GenerateMergedDialogueEventSoundBankAsync(
            CancellationToken cancellationToken)
        {
            if (!IsOkButtonEnabled ||
                !TryNormalizeSoundBankSuffix(
                    SoundBankSuffix,
                    out var normalizedSuffix))
            {
                return;
            }

            _logger.Here().Information(
                "Generating merged Dialogue Event SoundBanks");
            using var linkedCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellationToken);
            IsGenerating = true;
            LoadStatus = LocalizationManager.Instance.Get(
                "DialogueEventMerger.Generating");
            ProgressDetail = LoadStatus;
            ProgressValue = 0;
            ProgressMaximum = 0;
            ProgressIsIndeterminate = true;
            Exception generationException = null;
            try
            {
                var progress = new Progress<AudioOperationProgress>(
                    UpdateOperationProgress);
                var generated = _soundBankGeneratorService is
                    ISoundBankGeneratorProgressService progressService
                    ? await progressService
                        .GenerateMergedDialogueEventSoundBanksAsync(
                            SelectedModdedSoundBanks.ToList(),
                            normalizedSuffix,
                            progress,
                            linkedCancellation.Token)
                    : await _soundBankGeneratorService
                        .GenerateMergedDialogueEventSoundBanksAsync(
                        SelectedModdedSoundBanks.ToList(),
                        normalizedSuffix,
                        linkedCancellation.Token);
                if (generated)
                    CloseWindowAction();
                else
                    LoadStatus = LocalizationManager.Instance.Get(
                        "DialogueEventMerger.NotGenerated");
            }
            catch (OperationCanceledException)
            {
                LoadStatus = LocalizationManager.Instance.Get(
                    "DialogueEventMerger.Cancelled");
            }
            catch (Exception exception)
            {
                generationException = exception;
            }
            finally
            {
                IsGenerating = false;
            }

            if (generationException != null)
            {
                await _completeProgressAction();
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "DialogueEventMerger.GenerateFailed",
                        generationException.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
        }

        private void UpdateOperationProgress(AudioOperationProgress progress)
        {
            LoadStatus = LocalizationManager.Instance.Get(
                progress.StageResourceKey);
            ProgressDetail = progress.Detail;
            ProgressValue = progress.Completed;
            ProgressMaximum = progress.Total;
            ProgressIsIndeterminate = progress.Total <= 0;
        }

        private static bool TryNormalizeSoundBankSuffix(
            string value,
            out string normalizedSuffix)
        {
            normalizedSuffix = value?.Trim();
            if (!AudioProjectNameValidator.IsSafeFileNameSegment(
                    normalizedSuffix))
            {
                normalizedSuffix = null;
                return false;
            }

            return true;
        }

        [RelayCommand]
        public void SelectAll()
        {
            foreach (var soundBank in ModdedSoundBanks)
                soundBank.IsChecked = true;
        }

        [RelayCommand]
        public void SelectNone()
        {
            foreach (var soundBank in ModdedSoundBanks)
                soundBank.IsChecked = false;
        }

        [RelayCommand] public void CloseWindowAction() => _closeAction?.Invoke();

        public void SetCloseAction(Action closeAction) => _closeAction = closeAction;

        public void SetProgressCompletionAction(
            Func<Task> completeProgressAction) =>
            _completeProgressAction = completeProgressAction;
    }
}
