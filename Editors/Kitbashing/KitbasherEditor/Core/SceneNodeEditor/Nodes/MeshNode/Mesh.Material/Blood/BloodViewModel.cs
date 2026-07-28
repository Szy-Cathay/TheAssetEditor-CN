using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.UserInterface;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.MathViews;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.MeshSubViews
{
    public partial class BloodViewModel : ObservableObject
    {
        private readonly BloodCapability _bloodCapability;
        private readonly IDocumentPropertyEditor _propertyEditor;

        [ObservableProperty] bool _useBlood;
        [ObservableProperty] ShaderTextureViewModel _bloodMap;
        [ObservableProperty] Vector2ViewModel _bloodUvScale;
        [ObservableProperty] FloatViewModel _bloodPreview;

        public BloodViewModel(
            BloodCapability bloodCapability,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IScopedResourceLibrary resourceLibrary,
            IStandardDialogs packFileUiProvider,
            IDocumentPropertyEditor propertyEditor)
        {
            _bloodCapability = bloodCapability;
            _propertyEditor = propertyEditor;

            _bloodMap = new ShaderTextureViewModel(
                bloodCapability.BloodMask,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _useBlood = _bloodCapability.UseBlood;
            _bloodUvScale = new Vector2ViewModel(
                _bloodCapability.UvScale,
                OnBloodUvScaleChanged);
            _bloodPreview = new FloatViewModel(
                _bloodCapability.PreviewBlood,
                OnBloodPreviewChanged);
        }

        void OnBloodUvScaleChanged(Vector2 value) =>
            _propertyEditor.Update(
                _bloodCapability.UvScale,
                value,
                newValue => _bloodCapability.UvScale = newValue,
                newValue => BloodUvScale.Set(newValue));

        partial void OnUseBloodChanged(bool value) =>
            _propertyEditor.Update(
                _bloodCapability.UseBlood,
                value,
                newValue => _bloodCapability.UseBlood = newValue,
                newValue => UseBlood = newValue);

        void OnBloodPreviewChanged(float value) =>
            _propertyEditor.Update(
                _bloodCapability.PreviewBlood,
                value,
                newValue => _bloodCapability.PreviewBlood = newValue,
                newValue => BloodPreview.Value = newValue);
    }
}
