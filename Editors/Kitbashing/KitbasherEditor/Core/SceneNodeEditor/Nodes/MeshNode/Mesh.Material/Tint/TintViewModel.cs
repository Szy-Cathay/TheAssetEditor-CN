using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Tint
{
    public partial class TintViewModel : ObservableObject
    {
        private readonly TintCapability _tintCapability;
        private readonly SceneRenderParametersStore _sceneRenderParameters;
        private readonly FactionColourSettingsService? _settingsService;

        [ObservableProperty] bool _applyFactionColours;
        [ObservableProperty] ColourPickerViewModel _factionColour0;
        [ObservableProperty] ColourPickerViewModel _factionColour1;
        [ObservableProperty] ColourPickerViewModel _factionColour2;

        public TintViewModel(
            TintCapability tintCapability,
            SceneRenderParametersStore sceneRenderParameters,
            FactionColourSettingsService? settingsService = null)
        {
            _tintCapability = tintCapability;
            _sceneRenderParameters = sceneRenderParameters;
            _settingsService = settingsService;
            _tintCapability.UseFactionColours =
                _sceneRenderParameters.FactionColoursEnabled;
            _applyFactionColours =
                _sceneRenderParameters.FactionColoursEnabled;
            _factionColour0 = new ColourPickerViewModel(
                _sceneRenderParameters.FactionColour0,
                value => OnFactionColourChanged(0, value));
            _factionColour1 = new ColourPickerViewModel(
                _sceneRenderParameters.FactionColour1,
                value => OnFactionColourChanged(1, value));
            _factionColour2 = new ColourPickerViewModel(
                _sceneRenderParameters.FactionColour2,
                value => OnFactionColourChanged(2, value));
        }

        partial void OnApplyFactionColoursChanged(bool value)
        {
            _sceneRenderParameters.FactionColoursEnabled = value;
            _tintCapability.UseFactionColours = value;
        }

        [RelayCommand(CanExecute = nameof(CanSaveFactionColourSettings))]
        private void SaveFactionColourSettings()
        {
            _settingsService!.Save(new FactionColourSettings(
                ApplyFactionColours,
                FactionColourSettingsService.ToRgbString(
                    _sceneRenderParameters.FactionColour0),
                FactionColourSettingsService.ToRgbString(
                    _sceneRenderParameters.FactionColour1),
                FactionColourSettingsService.ToRgbString(
                    _sceneRenderParameters.FactionColour2)));
        }

        private bool CanSaveFactionColourSettings() =>
            _settingsService != null;

        private void OnFactionColourChanged(int index, Vector3 value)
        {
            switch (index)
            {
                case 0:
                    _sceneRenderParameters.FactionColour0 = value;
                    break;
                case 1:
                    _sceneRenderParameters.FactionColour1 = value;
                    break;
                default:
                    _sceneRenderParameters.FactionColour2 = value;
                    break;
            }
        }
    }
}
