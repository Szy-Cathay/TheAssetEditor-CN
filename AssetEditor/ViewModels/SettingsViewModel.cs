using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using AssetEditor.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace AssetEditor.ViewModels
{
    partial class SettingsViewModel : ObservableObject
    {
        private readonly ApplicationSettingsService _settingsService;
        private readonly ApplicationSettingsApplier _settingsApplier;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ThemeType _originalTheme;
        private readonly AppFontFamily _originalFont;
        private readonly string _originalFontWeight;
        private bool _allowPreview;

        public bool IsSaved { get; private set; }

        public ObservableCollection<ThemeType> AvailableThemes { get; set; } = [];
        public ObservableCollection<BackgroundColour> RenderEngineBackgroundColours { get; set; } = [];
        public ObservableCollection<AppFontFamily> AvailableFonts { get; set; } = [];
        public ObservableCollection<string> AvailableFontWeights { get; set; } = [];
        public ObservableCollection<GameTypeEnum> Games { get; set; } = [];
        public ObservableCollection<GamePathItem> GameDirectores { get; set; } = [];
        public ColourPickerViewModel CustomBackgroundColourPicker
            { get; private set; } = null!;
        public ColourPickerViewModel ViewportGridColourPicker
            { get; private set; } = null!;

        [ObservableProperty] private ThemeType _currentTheme;
        partial void OnCurrentThemeChanged(ThemeType value)
        {
            if (_allowPreview)
                ThemesController.SetTheme(value);
        }

        [ObservableProperty] private BackgroundColour _currentRenderEngineBackgroundColour;
        partial void OnCurrentRenderEngineBackgroundColourChanged(BackgroundColour value)
        {
            IsCustomBackgroundVisible = value == BackgroundColour.Custom;
            PreviewViewportIfValid();
        }
        [ObservableProperty] private bool _isCustomBackgroundVisible;
        [ObservableProperty] private string _customBackgroundR;
        [ObservableProperty] private string _customBackgroundG;
        [ObservableProperty] private string _customBackgroundB;
        partial void OnCustomBackgroundRChanged(string value) =>
            PreviewViewportIfValid();
        partial void OnCustomBackgroundGChanged(string value) =>
            PreviewViewportIfValid();
        partial void OnCustomBackgroundBChanged(string value) =>
            PreviewViewportIfValid();
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

            ApplyFontPreview();
        }

        [ObservableProperty] private string? _selectedFontWeight;
        partial void OnSelectedFontWeightChanged(string? value) =>
            ApplyFontPreview();
        [ObservableProperty] private bool _startMaximised;
        [ObservableProperty] private GameTypeEnum _currentGame;
        [ObservableProperty] private bool _showCAWemFiles;
        [ObservableProperty] private string _wwisePath;
        [ObservableProperty] private bool _onlyLoadLod0ForReferenceMeshes;

        [ObservableProperty] private bool _simulateGameBackfaces;
        partial void OnSimulateGameBackfacesChanged(bool value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private bool _showViewportGrid;
        partial void OnShowViewportGridChanged(bool value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportGridColourR;
        partial void OnViewportGridColourRChanged(string value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportGridColourG;
        partial void OnViewportGridColourGChanged(string value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportGridColourB;
        partial void OnViewportGridColourBChanged(string value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportLightIntensity;
        partial void OnViewportLightIntensityChanged(string value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportEnvironmentLightRotationY;
        partial void OnViewportEnvironmentLightRotationYChanged(string value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportDirectLightRotationX;
        partial void OnViewportDirectLightRotationXChanged(string value) =>
            PreviewViewportIfValid();
        [ObservableProperty] private string _viewportDirectLightRotationY;
        partial void OnViewportDirectLightRotationYChanged(string value) =>
            PreviewViewportIfValid();

        public SettingsViewModel(
            ApplicationSettingsService settingsService,
            ApplicationSettingsApplier settingsApplier,
            IStandardDialogs standardDialogs)
        {
            _settingsService = settingsService;
            _settingsApplier = settingsApplier;
            _standardDialogs = standardDialogs;
            _originalTheme = settingsService.CurrentSettings.Theme;
            _originalFont = settingsService.CurrentSettings.AppFont;
            _originalFontWeight =
                settingsService.CurrentSettings.AppFontWeight;

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
            CustomBackgroundColourPicker = new ColourPickerViewModel(
                CreateColourVector(
                    CustomBackgroundR,
                    CustomBackgroundG,
                    CustomBackgroundB),
                _ => ApplyCustomBackgroundPickerColour());
            IsCustomBackgroundVisible = CurrentRenderEngineBackgroundColour == BackgroundColour.Custom;

            SimulateGameBackfaces =
                _settingsService.CurrentSettings.SimulateGameBackfaces;
            ShowViewportGrid =
                _settingsService.CurrentSettings.ShowViewportGrid;
            var gridRgb = (_settingsService.CurrentSettings
                    .ViewportGridColour ?? "0,0,0")
                .Split(',');
            ViewportGridColourR = gridRgb.Length > 0
                ? gridRgb[0].Trim()
                : "0";
            ViewportGridColourG = gridRgb.Length > 1
                ? gridRgb[1].Trim()
                : "0";
            ViewportGridColourB = gridRgb.Length > 2
                ? gridRgb[2].Trim()
                : "0";
            ViewportGridColourPicker = new ColourPickerViewModel(
                CreateColourVector(
                    ViewportGridColourR,
                    ViewportGridColourG,
                    ViewportGridColourB),
                _ => ApplyGridPickerColour());
            ViewportLightIntensity =
                _settingsService.CurrentSettings.ViewportLightIntensity
                    .ToString(CultureInfo.InvariantCulture);
            ViewportEnvironmentLightRotationY =
                _settingsService.CurrentSettings
                    .ViewportEnvironmentLightRotationY
                    .ToString(CultureInfo.InvariantCulture);
            ViewportDirectLightRotationX =
                _settingsService.CurrentSettings
                    .ViewportDirectLightRotationX
                    .ToString(CultureInfo.InvariantCulture);
            ViewportDirectLightRotationY =
                _settingsService.CurrentSettings
                    .ViewportDirectLightRotationY
                    .ToString(CultureInfo.InvariantCulture);

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
            ShowCAWemFiles = _settingsService.CurrentSettings.ShowCAWemFiles;
            OnlyLoadLod0ForReferenceMeshes = _settingsService.CurrentSettings.OnlyLoadLod0ForReferenceMeshes;
            foreach (var game in GameInformationDatabase.Games.Values.OrderBy(game => game.DisplayName))
            {
                GameDirectores.Add(
                    new GamePathItem()
                    {
                        GameName = $"{game.DisplayName}",
                        GameType = game.Type,
                        Path = _settingsService.CurrentSettings
                            .GameDirectories.FirstOrDefault(
                                x => x.Game == game.Type)?.Path ?? ""
                    });
            }
            WwisePath = _settingsService.CurrentSettings.WwisePath;

            _allowPreview = true;
        }


        [RelayCommand]
        private void Save()
        {
            if (!TryCreateViewportSettings(out var viewportSettings))
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "Msg.SettingsInvalidViewportValues"),
                    LocalizationManager.Instance.Get(
                        "SettingsWindow.Title"));
                return;
            }

            _settingsService.CurrentSettings.Theme = CurrentTheme;
            _settingsService.CurrentSettings.RenderEngineBackgroundColour = CurrentRenderEngineBackgroundColour;
            _settingsService.CurrentSettings.StartMaximised = StartMaximised;
            _settingsService.CurrentSettings.CurrentGame = CurrentGame;
            _settingsService.CurrentSettings.ShowCAWemFiles = ShowCAWemFiles;
            _settingsService.CurrentSettings.OnlyLoadLod0ForReferenceMeshes = OnlyLoadLod0ForReferenceMeshes;
            _settingsService.CurrentSettings.AppFont = SelectedFont;
            _settingsService.CurrentSettings.AppFontWeight =
                SelectedFontWeight ??
                FontSettingsHelper.GetDefaultWeight(SelectedFont);
            _settingsService.CurrentSettings.CustomBackgroundColour = $"{CustomBackgroundR},{CustomBackgroundG},{CustomBackgroundB}";
            _settingsService.CurrentSettings.SimulateGameBackfaces =
                viewportSettings.SimulateGameBackfaces;
            _settingsService.CurrentSettings.ShowViewportGrid =
                viewportSettings.ShowGrid;
            _settingsService.CurrentSettings.ViewportGridColour =
                viewportSettings.GridColour;
            _settingsService.CurrentSettings.ViewportLightIntensity =
                viewportSettings.LightIntensity;
            _settingsService.CurrentSettings
                .ViewportEnvironmentLightRotationY =
                    viewportSettings.EnvironmentLightRotationY;
            _settingsService.CurrentSettings
                .ViewportDirectLightRotationX =
                    viewportSettings.DirectLightRotationX;
            _settingsService.CurrentSettings
                .ViewportDirectLightRotationY =
                    viewportSettings.DirectLightRotationY;
            _settingsService.CurrentSettings.GameDirectories.Clear();
            foreach (var item in GameDirectores)
                _settingsService.CurrentSettings.GameDirectories.Add(new ApplicationSettings.GamePathPair() { Game = item.GameType, Path = item.Path });
            _settingsService.CurrentSettings.WwisePath = WwisePath;

            var result = _settingsApplier.CompleteSave();
            IsSaved = true;
            if (result.RequiresApplicationRestart)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "Msg.SettingsRestartRequired"),
                    LocalizationManager.Instance.Get(
                        "SettingsWindow.Title"));
            }
        }

        public void Cancel()
        {
            if (IsSaved)
                return;

            if (CurrentTheme != _originalTheme)
                ThemesController.SetTheme(_originalTheme);
            if (SelectedFont != _originalFont ||
                SelectedFontWeight != _originalFontWeight)
            {
                ThemesController.ApplyCustomFont(
                    FontSettingsHelper.GetFontFamily(_originalFont),
                    FontSettingsHelper.GetFontWeight(
                        _originalFontWeight));
            }
            _settingsApplier.RestoreViewportPreview();
        }

        private void ApplyFontPreview()
        {
            if (!_allowPreview)
                return;

            ThemesController.ApplyCustomFont(
                FontSettingsHelper.GetFontFamily(SelectedFont),
                FontSettingsHelper.GetFontWeight(SelectedFontWeight));
        }

        private void PreviewViewportIfValid()
        {
            if (_allowPreview &&
                TryCreateViewportSettings(out var settings))
            {
                _settingsApplier.PreviewViewport(settings);
            }
        }

        private bool TryCreateViewportSettings(
            out ViewportRenderSettings settings)
        {
            settings = default!;
            if (!TryCreateRgb(
                    CustomBackgroundR,
                    CustomBackgroundG,
                    CustomBackgroundB,
                    out var backgroundColour) ||
                !TryCreateRgb(
                    ViewportGridColourR,
                    ViewportGridColourG,
                    ViewportGridColourB,
                    out var gridColour) ||
                !TryCreateFloat(
                    ViewportLightIntensity,
                    out var lightIntensity) ||
                lightIntensity < 0 ||
                !TryCreateFloat(
                    ViewportEnvironmentLightRotationY,
                    out var environmentLightRotationY) ||
                !TryCreateFloat(
                    ViewportDirectLightRotationX,
                    out var directLightRotationX) ||
                !TryCreateFloat(
                    ViewportDirectLightRotationY,
                    out var directLightRotationY))
            {
                return false;
            }

            settings = ViewportRenderSettings.From(
                _settingsService.CurrentSettings) with
            {
                BackgroundColour =
                    CurrentRenderEngineBackgroundColour,
                CustomBackgroundColour = backgroundColour,
                SimulateGameBackfaces = SimulateGameBackfaces,
                ShowGrid = ShowViewportGrid,
                GridColour = gridColour,
                LightIntensity = lightIntensity,
                EnvironmentLightRotationY =
                    environmentLightRotationY,
                DirectLightRotationX = directLightRotationX,
                DirectLightRotationY = directLightRotationY
            };
            return true;
        }

        private static bool TryCreateRgb(
            string red,
            string green,
            string blue,
            out string value)
        {
            value = "";
            if (!byte.TryParse(red, out var r) ||
                !byte.TryParse(green, out var g) ||
                !byte.TryParse(blue, out var b))
            {
                return false;
            }

            value = $"{r},{g},{b}";
            return true;
        }

        private static Vector3 CreateColourVector(
            string red,
            string green,
            string blue)
        {
            byte.TryParse(red, out var r);
            byte.TryParse(green, out var g);
            byte.TryParse(blue, out var b);
            return new Vector3(r / 255f, g / 255f, b / 255f);
        }

        private void ApplyCustomBackgroundPickerColour()
        {
            var colour = CustomBackgroundColourPicker.PickedColor;
            CustomBackgroundR = colour.R.ToString(
                CultureInfo.InvariantCulture);
            CustomBackgroundG = colour.G.ToString(
                CultureInfo.InvariantCulture);
            CustomBackgroundB = colour.B.ToString(
                CultureInfo.InvariantCulture);
        }

        private void ApplyGridPickerColour()
        {
            var colour = ViewportGridColourPicker.PickedColor;
            ViewportGridColourR = colour.R.ToString(
                CultureInfo.InvariantCulture);
            ViewportGridColourG = colour.G.ToString(
                CultureInfo.InvariantCulture);
            ViewportGridColourB = colour.B.ToString(
                CultureInfo.InvariantCulture);
        }

        private static bool TryCreateFloat(
            string value,
            out float result)
        {
            return float.TryParse(
                       value,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out result) &&
                   float.IsFinite(result);
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
                    MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.NotGameDirectory", packFiles, manifest));
            }
        }
    }
}
