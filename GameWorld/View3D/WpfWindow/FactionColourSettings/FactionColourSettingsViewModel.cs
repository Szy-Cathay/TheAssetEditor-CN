using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace GameWorld.Core.WpfWindow.FactionColourSettings
{
    public partial class FactionColourSettingsViewModel : ObservableObject
    {
        private readonly FactionColourSettingsService _settingsService;
        private readonly Services.FactionColourSettings _originalSettings;
        private bool _allowPreview;

        [ObservableProperty] private bool _enabled;

        public ColourPickerViewModel Colour0 { get; }
        public ColourPickerViewModel Colour1 { get; }
        public ColourPickerViewModel Colour2 { get; }

        public FactionColourSettingsViewModel(
            FactionColourSettingsService settingsService)
        {
            _settingsService = settingsService;
            _originalSettings = settingsService.Current;
            _enabled = _originalSettings.Enabled;
            Colour0 = CreatePicker(_originalSettings.Colour0);
            Colour1 = CreatePicker(_originalSettings.Colour1);
            Colour2 = CreatePicker(_originalSettings.Colour2);
            _allowPreview = true;
        }

        partial void OnEnabledChanged(bool value) => Preview();

        public void Save() => _settingsService.Save(CreateSettings());

        public void Cancel() =>
            _settingsService.Preview(_originalSettings);

        private ColourPickerViewModel CreatePicker(string rgb) =>
            new(
                ApplicationSettingsHelper
                    .ParseCustomBackgroundColour(rgb)
                    .ToVector3(),
                _ => Preview());

        private void Preview()
        {
            if (_allowPreview)
                _settingsService.Preview(CreateSettings());
        }

        private Services.FactionColourSettings CreateSettings() => new(
            Enabled,
            FactionColourSettingsService.ToRgbString(
                Colour0.SelectedColour),
            FactionColourSettingsService.ToRgbString(
                Colour1.SelectedColour),
            FactionColourSettingsService.ToRgbString(
                Colour2.SelectedColour));
    }
}
