using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.UserInterface;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using Shared.Ui.BaseDialogs.MathViews;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Emissive
{
    public partial class EmissiveViewModel : ObservableObject
    {
        private readonly EmissiveCapability _emissiveCapability;
        private readonly IDocumentPropertyEditor _propertyEditor;

        [ObservableProperty] ShaderTextureViewModel _emissiveTexture;
        [ObservableProperty] ShaderTextureViewModel _emissiveDistortionTexture;
        [ObservableProperty] Vector2ViewModel _emissiveDirection;
        [ObservableProperty] float _emissiveDistortStrength;
        [ObservableProperty] float _emissiveFresnelStrength;
        [ObservableProperty] ColourPickerViewModel _emissiveTint;
        [ObservableProperty] ColourPickerViewModel _gradient0;
        [ObservableProperty] ColourPickerViewModel _gradient1;
        [ObservableProperty] ColourPickerViewModel _gradient2;
        [ObservableProperty] ColourPickerViewModel _gradient3;
        [ObservableProperty] float _gradientTime0;
        [ObservableProperty] float _gradientTime1;
        [ObservableProperty] float _gradientTime2;
        [ObservableProperty] float _gradientTime3;

        [ObservableProperty] float _emissiveSpeed;
        [ObservableProperty] float _emissivePulseSpeed;
        [ObservableProperty] float _emissivePulseStrength;
        [ObservableProperty] float _emissiveStrength;
        [ObservableProperty] Vector2ViewModel _emissiveTiling;

        public EmissiveViewModel(
            EmissiveCapability emissiveCapability,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IScopedResourceLibrary resourceLibrary,
            IStandardDialogs packFileUiProvider,
            IDocumentPropertyEditor propertyEditor)
        {
            _emissiveCapability = emissiveCapability;
            _propertyEditor = propertyEditor;

            _emissiveTexture = new ShaderTextureViewModel(
                emissiveCapability.Emissive,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _emissiveDistortionTexture = new ShaderTextureViewModel(
                emissiveCapability.EmissiveDistortion,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);

            _emissiveDirection = new Vector2ViewModel(
                emissiveCapability.EmissiveDirection,
                OnEmissiveDirectionChanged);
            _emissiveDistortStrength = emissiveCapability.EmissiveDistortStrength;
            _emissiveFresnelStrength = emissiveCapability.EmissiveFresnelStrength;

            _emissiveTint = new ColourPickerViewModel(
                emissiveCapability.EmissiveTint,
                OnEmissiveTintChanged);

            _gradient0 = new ColourPickerViewModel(
                emissiveCapability.GradientColours[0],
                value => OnGradientColourChanged(0, value));
            _gradient1 = new ColourPickerViewModel(
                emissiveCapability.GradientColours[1],
                value => OnGradientColourChanged(1, value));
            _gradient2 = new ColourPickerViewModel(
                emissiveCapability.GradientColours[2],
                value => OnGradientColourChanged(2, value));
            _gradient3 = new ColourPickerViewModel(
                emissiveCapability.GradientColours[3],
                value => OnGradientColourChanged(3, value));

            _gradientTime0 = emissiveCapability.GradientTimes[0];
            _gradientTime1 = emissiveCapability.GradientTimes[1];
            _gradientTime2 = emissiveCapability.GradientTimes[2];
            _gradientTime3 = emissiveCapability.GradientTimes[3];

            _emissiveSpeed = emissiveCapability.EmissiveSpeed;
            _emissivePulseSpeed = emissiveCapability.EmissivePulseSpeed;
            _emissivePulseStrength = emissiveCapability.EmissivePulseStrength;
            _emissiveStrength = emissiveCapability.EmissiveStrength;

            _emissiveTiling = new Vector2ViewModel(
                emissiveCapability.EmissiveTiling,
                OnEmissiveTilingChanged);
        }

        partial void OnEmissiveDistortStrengthChanged(float value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveDistortStrength,
                value,
                newValue => _emissiveCapability.EmissiveDistortStrength = newValue,
                newValue => EmissiveDistortStrength = newValue);

        partial void OnEmissiveSpeedChanged(float value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveSpeed,
                value,
                newValue => _emissiveCapability.EmissiveSpeed = newValue,
                newValue => EmissiveSpeed = newValue);

        partial void OnEmissivePulseStrengthChanged(float value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissivePulseStrength,
                value,
                newValue => _emissiveCapability.EmissivePulseStrength = newValue,
                newValue => EmissivePulseStrength = newValue);

        partial void OnEmissivePulseSpeedChanged(float value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissivePulseSpeed,
                value,
                newValue => _emissiveCapability.EmissivePulseSpeed = newValue,
                newValue => EmissivePulseSpeed = newValue);

        partial void OnEmissiveStrengthChanged(float value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveStrength,
                value,
                newValue => _emissiveCapability.EmissiveStrength = newValue,
                newValue => EmissiveStrength = newValue);

        partial void OnEmissiveFresnelStrengthChanged(float value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveFresnelStrength,
                value,
                newValue => _emissiveCapability.EmissiveFresnelStrength = newValue,
                newValue => EmissiveFresnelStrength = newValue);

        partial void OnGradientTime0Changed(float value) =>
            OnGradientTimeChanged(0, value);

        partial void OnGradientTime1Changed(float value) =>
            OnGradientTimeChanged(1, value);

        partial void OnGradientTime2Changed(float value) =>
            OnGradientTimeChanged(2, value);

        partial void OnGradientTime3Changed(float value) =>
            OnGradientTimeChanged(3, value);

        void OnEmissiveDirectionChanged(Vector2 value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveDirection,
                value,
                newValue => _emissiveCapability.EmissiveDirection = newValue,
                newValue => EmissiveDirection.Set(newValue));

        void OnEmissiveTintChanged(Vector3 value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveTint,
                value,
                newValue => _emissiveCapability.EmissiveTint = newValue,
                newValue => EmissiveTint.Set(newValue));

        void OnEmissiveTilingChanged(Vector2 value) =>
            _propertyEditor.Update(
                _emissiveCapability.EmissiveTiling,
                value,
                newValue => _emissiveCapability.EmissiveTiling = newValue,
                newValue => EmissiveTiling.Set(newValue));

        private void OnGradientColourChanged(int index, Vector3 value) =>
            _propertyEditor.Update(
                _emissiveCapability.GradientColours[index],
                value,
                newValue => _emissiveCapability.GradientColours[index] = newValue,
                newValue => GetGradient(index).Set(newValue));

        private void OnGradientTimeChanged(int index, float value) =>
            _propertyEditor.Update(
                _emissiveCapability.GradientTimes[index],
                value,
                newValue => _emissiveCapability.GradientTimes[index] = newValue,
                newValue => SetGradientTime(index, newValue));

        private ColourPickerViewModel GetGradient(int index) =>
            index switch
            {
                0 => Gradient0,
                1 => Gradient1,
                2 => Gradient2,
                _ => Gradient3
            };

        private void SetGradientTime(int index, float value)
        {
            switch (index)
            {
                case 0:
                    GradientTime0 = value;
                    break;
                case 1:
                    GradientTime1 = value;
                    break;
                case 2:
                    GradientTime2 = value;
                    break;
                default:
                    GradientTime3 = value;
                    break;
            }
        }
    }
}
