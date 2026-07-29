using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.GameInformation.Warhammer3;
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

        [ObservableProperty] private string _soundBankSuffix;
        [ObservableProperty] private bool _isSoundBankSuffixSet;
        [ObservableProperty] private bool _isOkButtonEnabled;
        [ObservableProperty] private ObservableCollection<string> _selectedModdedSoundBanks = [];
        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private bool _isGenerating;
        [ObservableProperty] private string _loadStatus =
            LocalizationManager.Instance.Get(
                "DialogueEventMerger.Loading");
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
            UpdateOkButtonIsEnabled();
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LoadStatus = LocalizationManager.Instance.Get(
                        "DialogueEventMerger.Cancelled");
                    return;
                }

                var loadWasCancelled = await Task.Run(() =>
                {
                    try
                    {
                        _audioRepository.Load(
                            Wh3LanguageInformation.GetAllLanguages(),
                            null,
                            cancellationToken);
                        return false;
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                });
                if (loadWasCancelled ||
                    cancellationToken.IsCancellationRequested)
                {
                    LoadStatus = LocalizationManager.Instance.Get(
                        "DialogueEventMerger.Cancelled");
                    return;
                }

                foreach (var path in _audioRepository
                             .GetModdedSoundBankFilePaths("for_merging"))
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
                LoadStatus = LocalizationManager.Instance.Get(
                    "DialogueEventMerger.LoadFailed");
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "DialogueEventMerger.LoadFailed.Detail",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
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
            try
            {
                var generated = await _soundBankGeneratorService
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
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.GetFormat(
                        "DialogueEventMerger.GenerateFailed",
                        exception.Message),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            finally
            {
                IsGenerating = false;
            }
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
    }
}
