using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.UserInterface;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.MeshSubViews
{
    public partial class MetalRoughViewModel : ObservableObject
    {
        private readonly MetalRoughCapability _defaultCapability;
        private readonly IDocumentPropertyEditor _propertyEditor;

        [ObservableProperty] bool _useAlpha;
        
        [ObservableProperty] ShaderTextureViewModel _baseColour;
        [ObservableProperty] ShaderTextureViewModel _materialMap;
        [ObservableProperty] ShaderTextureViewModel _normalMap;
        [ObservableProperty] ShaderTextureViewModel _mask;

        public MetalRoughViewModel(
            MetalRoughCapability defaultCapability,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IScopedResourceLibrary resourceLibrary,
            IStandardDialogs packFileUiProvider,
            IDocumentPropertyEditor propertyEditor)
        {
            _defaultCapability = defaultCapability;
            _propertyEditor = propertyEditor;

            _baseColour = new ShaderTextureViewModel(
                defaultCapability.BaseColour,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _materialMap = new ShaderTextureViewModel(
                defaultCapability.MaterialMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _normalMap = new ShaderTextureViewModel(
                defaultCapability.NormalMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _mask = new ShaderTextureViewModel(
                defaultCapability.Mask,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);

            _useAlpha = defaultCapability.UseAlpha;
        }

        partial void OnUseAlphaChanged(bool value) =>
            _propertyEditor.Update(
                _defaultCapability.UseAlpha,
                value,
                newValue => _defaultCapability.UseAlpha = newValue,
                newValue => UseAlpha = newValue);
    }
}
