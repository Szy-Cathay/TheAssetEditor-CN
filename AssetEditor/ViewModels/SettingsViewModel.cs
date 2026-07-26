using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.ViewModels
{
    partial class SettingsViewModel : ObservableObject
    {
        private readonly ApplicationSettingsService _settingsService;

        public ObservableCollection<ThemeType> AvailableThemes { get; set; } = [];
        public ObservableCollection<BackgroundColour> RenderEngineBackgroundColours { get; set; } = [];
        public ObservableCollection<AppFontFamily> AvailableFonts { get; set; } = [];
        public ObservableCollection<string> AvailableFontWeights { get; set; } = [];
        public ObservableCollection<GameTypeEnum> Games { get; set; } = [];
        public ObservableCollection<GamePathItem> GameDirectores { get; set; } = [];

        [ObservableProperty] private ThemeType _currentTheme;
        partial void OnCurrentThemeChanged(ThemeType value)
        {
            ThemesController.SetTheme(value);
        }

        [ObservableProperty] private BackgroundColour _currentRenderEngineBackgroundColour;
        partial void OnCurrentRenderEngineBackgroundColourChanged(BackgroundColour value)
        {
            IsCustomBackgroundVisible = value == BackgroundColour.Custom;
        }
        [ObservableProperty] private bool _isCustomBackgroundVisible;
        [ObservableProperty] private string _customBackgroundR;
        [ObservableProperty] private string _customBackgroundG;
        [ObservableProperty] private string _customBackgroundB;
        [ObservableProperty] private AppFontFamily _selectedFont;
        partial void OnSelectedFontChanged(AppFontFamily value)
        {
            // Update available weights for the new font
            var weights = FontSettingsHelper.GetAvailableWeights(value);
            AvailableFontWeights.Clear();
            foreach (var w in weights)
                AvailableFontWeights.Add(w);

            // Select default weight if current is not available
            if (weights.Length > 0 && !weights.Contains(_selectedFontWeight))
                SelectedFontWeight = FontSettingsHelper.GetDefaultWeight(value);
            else if (weights.Length == 0)
                SelectedFontWeight = null;
        }

        [ObservableProperty] private string _selectedFontWeight;
        [ObservableProperty] private bool _startMaximised;
        [ObservableProperty] private GameTypeEnum _currentGame;
        [ObservableProperty] private bool _loadCaPacksByDefault;
        [ObservableProperty] private bool _showCAWemFiles;
        [ObservableProperty] private string _wwisePath;
        [ObservableProperty] private bool _onlyLoadLod0ForReferenceMeshes;

        // Auto-save & backup settings
        [ObservableProperty] private bool _enableAutoSave;
        [ObservableProperty] private int _autoSaveIntervalMinutes;
        [ObservableProperty] private string _backupPath;
        [ObservableProperty] private int _maxBackupCount;

        // Compression settings
        [ObservableProperty] private bool _useZstdCompression;

        public SettingsViewModel(ApplicationSettingsService settingsService)
        {
            _settingsService = settingsService;

            AvailableThemes = new ObservableCollection<ThemeType>((ThemeType[])Enum.GetValues(typeof(ThemeType)));
            CurrentTheme = _settingsService.CurrentSettings.Theme;
            RenderEngineBackgroundColours = new ObservableCollection<BackgroundColour>((BackgroundColour[])Enum.GetValues(typeof(BackgroundColour)));
            CurrentRenderEngineBackgroundColour = _settingsService.CurrentSettings.RenderEngineBackgroundColour;

            // Custom background colour (R,G,B string)
            var customRgb = _settingsService.CurrentSettings.CustomBackgroundColour ?? "50,50,50";
            var rgbParts = customRgb.Split(',');
            CustomBackgroundR = rgbParts.Length > 0 ? rgbParts[0].Trim() : "50";
            CustomBackgroundG = rgbParts.Length > 1 ? rgbParts[1].Trim() : "50";
            CustomBackgroundB = rgbParts.Length > 2 ? rgbParts[2].Trim() : "50";
            IsCustomBackgroundVisible = CurrentRenderEngineBackgroundColour == BackgroundColour.Custom;

            // Font settings
            AvailableFonts = new ObservableCollection<AppFontFamily>((AppFontFamily[])Enum.GetValues(typeof(AppFontFamily)));
            SelectedFont = _settingsService.CurrentSettings.AppFont;
            AvailableFontWeights = new ObservableCollection<string>(FontSettingsHelper.GetAvailableWeights(SelectedFont));
            SelectedFontWeight = _settingsService.CurrentSettings.AppFontWeight;
            // Ensure weight is valid for the selected font
            if (!AvailableFontWeights.Contains(SelectedFontWeight) && AvailableFontWeights.Count > 0)
                SelectedFontWeight = FontSettingsHelper.GetDefaultWeight(SelectedFont);

            StartMaximised = _settingsService.CurrentSettings.StartMaximised;
            Games = new ObservableCollection<GameTypeEnum>(GameInformationDatabase.Games.Values.OrderBy(game => game.DisplayName).Select(game => game.Type));
            CurrentGame = _settingsService.CurrentSettings.CurrentGame;
            LoadCaPacksByDefault = _settingsService.CurrentSettings.LoadCaPacksByDefault;
            ShowCAWemFiles = _settingsService.CurrentSettings.ShowCAWemFiles;
            OnlyLoadLod0ForReferenceMeshes = _settingsService.CurrentSettings.OnlyLoadLod0ForReferenceMeshes;
            foreach (var game in GameInformationDatabase.Games.Values.OrderBy(game => game.DisplayName))
            {
                GameDirectores.Add(
                    new GamePathItem()
                    {
                        GameName = $"{game.DisplayName}",
                        GameType = game.Type,
                        Path = _settingsService.CurrentSettings.GameDirectories.FirstOrDefault(x => x.Game == game.Type)?.Path
                    });
            }
            WwisePath = _settingsService.CurrentSettings.WwisePath;

            // Auto-save & backup settings
            EnableAutoSave = _settingsService.CurrentSettings.EnableAutoSave;
            AutoSaveIntervalMinutes = _settingsService.CurrentSettings.AutoSaveIntervalMinutes;
            BackupPath = _settingsService.CurrentSettings.BackupPath ?? "";
            MaxBackupCount = _settingsService.CurrentSettings.MaxBackupCount;

            // Compression settings
            UseZstdCompression = _settingsService.CurrentSettings.UseZstdCompression;
        }


        [RelayCommand]
        private void Save()
        {
            _settingsService.CurrentSettings.Theme = CurrentTheme;
            _settingsService.CurrentSettings.RenderEngineBackgroundColour = CurrentRenderEngineBackgroundColour;
            _settingsService.CurrentSettings.StartMaximised = StartMaximised;
            _settingsService.CurrentSettings.CurrentGame = CurrentGame;
            _settingsService.CurrentSettings.LoadCaPacksByDefault = LoadCaPacksByDefault;
            _settingsService.CurrentSettings.ShowCAWemFiles = ShowCAWemFiles;
            _settingsService.CurrentSettings.OnlyLoadLod0ForReferenceMeshes = OnlyLoadLod0ForReferenceMeshes;
            _settingsService.CurrentSettings.AppFont = SelectedFont;
            _settingsService.CurrentSettings.AppFontWeight = SelectedFontWeight;
            _settingsService.CurrentSettings.CustomBackgroundColour = $"{CustomBackgroundR},{CustomBackgroundG},{CustomBackgroundB}";
            _settingsService.CurrentSettings.GameDirectories.Clear();
            foreach (var item in GameDirectores)
                _settingsService.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair() { Game = item.GameType, Path = item.Path });
            _settingsService.CurrentSettings.WwisePath = WwisePath;

            // Auto-save & backup settings
            _settingsService.CurrentSettings.EnableAutoSave = EnableAutoSave;
            _settingsService.CurrentSettings.AutoSaveIntervalMinutes = AutoSaveIntervalMinutes;
            _settingsService.CurrentSettings.BackupPath = BackupPath;
            _settingsService.CurrentSettings.MaxBackupCount = MaxBackupCount;

            // Compression settings
            _settingsService.CurrentSettings.UseZstdCompression = UseZstdCompression;

            _settingsService.Save();
            MessageBox.Show(LocalizationManager.Instance.Get("Msg.RestartAfterSettings"));
        }

        [RelayCommand]
        private void Browse()
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Executable files (*.exe)|*.exe";
            dialog.Multiselect = false;
            if (dialog.ShowDialog() == DialogResult.OK)
                WwisePath = dialog.FileName;
        }

        [RelayCommand]
        private void BrowseBackupPath()
        {
            var dialog = new FolderBrowserDialog();
            dialog.Description = LocalizationManager.Instance.Get("SettingsWindow.BackupPath");
            if (dialog.ShowDialog() == DialogResult.OK)
                BackupPath = dialog.SelectedPath;
        }
    }

    class GamePathItem : NotifyPropertyChangedImpl
    {
        public GameTypeEnum GameType { get; set; }

        string _gameName;
        public string GameName { get => _gameName; set => SetAndNotify(ref _gameName, value); }

        string _path;
        public string Path { get => _path; set => SetAndNotify(ref _path, value); }

        public ICommand BrowseCommand { get; set; }

        public GamePathItem()
        {
            BrowseCommand = new RelayCommand(OnBrowse);
        }

        void OnBrowse()
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Path = dialog.SelectedPath;
                var files = Directory.GetFiles(Path);
                var packFiles = files.Count(x => System.IO.Path.GetExtension(x) == ".pack");
                var manifest = files.Count(x => x.Contains("manifest.txt"));

                if (packFiles == 0 && manifest == 0)
                    System.Windows.MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.NotGameDirectory", packFiles, manifest));
            }
        }
    }
}
