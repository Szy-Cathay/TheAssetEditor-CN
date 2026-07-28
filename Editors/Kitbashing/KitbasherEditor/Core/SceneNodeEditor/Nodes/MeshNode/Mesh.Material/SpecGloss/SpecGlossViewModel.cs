using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Commands;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.UserInterface;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.SpecGloss
{
    public partial class SpecGlossViewModel : ObservableObject
    {
        private readonly SpecGlossCapability _capability;
        private readonly IDocumentPropertyEditor _propertyEditor;

        [ObservableProperty] bool _useAlpha;

        [ObservableProperty] ShaderTextureViewModel _specularMap;
        [ObservableProperty] ShaderTextureViewModel _glossMap;
        [ObservableProperty] ShaderTextureViewModel _diffuseMap;
        [ObservableProperty] ShaderTextureViewModel _normalMap;
        [ObservableProperty] ShaderTextureViewModel _mask;

        public SpecGlossViewModel(
            SpecGlossCapability capability,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IScopedResourceLibrary resourceLibrary,
            IStandardDialogs packFileUiProvider,
            IDocumentPropertyEditor propertyEditor)
        {
            _capability = capability;
            _propertyEditor = propertyEditor;

            _specularMap = new ShaderTextureViewModel(
                capability.SpecularMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _glossMap = new ShaderTextureViewModel(
                capability.GlossMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _diffuseMap = new ShaderTextureViewModel(
                capability.DiffuseMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _normalMap = new ShaderTextureViewModel(
                capability.NormalMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _mask = new ShaderTextureViewModel(
                capability.Mask,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);

            _useAlpha = _capability.UseAlpha;
        }

        partial void OnUseAlphaChanged(bool value) =>
            _propertyEditor.Update(
                _capability.UseAlpha,
                value,
                newValue => _capability.UseAlpha = newValue,
                newValue => UseAlpha = newValue);
    }
}
