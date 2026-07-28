using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using Microsoft.Xna.Framework;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Tint
{
    public partial class TintViewModel : ObservableObject
    {
        private readonly TintCapability _tintCapability;
        private readonly SceneRenderParametersStore _sceneRenderParameters;

        [ObservableProperty] bool _applyFactionColours;
        [ObservableProperty] ColourPickerViewModel _factionColour0;
        [ObservableProperty] ColourPickerViewModel _factionColour1;
        [ObservableProperty] ColourPickerViewModel _factionColour2;

        public TintViewModel(
            TintCapability tintCapability,
            SceneRenderParametersStore sceneRenderParameters)
        {
            _tintCapability = tintCapability;
            _sceneRenderParameters = sceneRenderParameters;
            _tintCapability.UseFactionColours = true;
            _applyFactionColours = _tintCapability.ApplyCapability;
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
            _tintCapability.ApplyCapability = value;
            _tintCapability.UseFactionColours = true;
        }

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
