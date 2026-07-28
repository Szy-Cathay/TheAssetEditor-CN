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
using static Shared.GameFormats.RigidModel.MaterialHeaders.WeightedMaterial;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.DirtAndDecal
{
    public partial class AdvancedRmvMaterialViewModel : ObservableObject
    {
        private readonly AdvancedMaterialCapability _capability;
        private readonly IDocumentPropertyEditor _propertyEditor;

        [ObservableProperty] ShaderTextureViewModel _dirtMap;
        [ObservableProperty] ShaderTextureViewModel _dirtMask;
        [ObservableProperty] ShaderTextureViewModel _decalMask;
        [ObservableProperty] ShaderTextureViewModel _skinMask;

        [ObservableProperty] Vector2ViewModel _uvScale;
        [ObservableProperty] Vector4ViewModel _textureTransform;

        [ObservableProperty] bool _useDecal;
        [ObservableProperty] bool _useDirt;
        [ObservableProperty] bool _useSkin;

        [ObservableProperty] MaterialHintEnum[] _possibleMaterials = [MaterialHintEnum.Decal, MaterialHintEnum.Decal_Dirt, MaterialHintEnum.Skin_Dirt, MaterialHintEnum.Skin, MaterialHintEnum.Dirt];
        [ObservableProperty] MaterialHintEnum _selectedMaterial;

        [ObservableProperty] bool _showDirtMap;
        [ObservableProperty] bool _showDirtMask;
        [ObservableProperty] bool _showDecalMask;
        [ObservableProperty] bool _showSkinMask;
        [ObservableProperty] bool _showUvScale;
        [ObservableProperty] bool _showTextureTransform;

        public AdvancedRmvMaterialViewModel(
            AdvancedMaterialCapability capability,
            IUiCommandFactory uiCommandFactory,
            IPackFileService packFileService,
            IScopedResourceLibrary resourceLibrary,
            IStandardDialogs packFileUiProvider,
            IDocumentPropertyEditor propertyEditor)
        {
            _capability = capability;
            _propertyEditor = propertyEditor;

            _useDecal = capability.UseDecal;
            _useDirt = capability.UseDirt;
            _useSkin = capability.UseSkin;

            _dirtMap = new ShaderTextureViewModel(
                _capability.DirtMap,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _dirtMask = new ShaderTextureViewModel(
                _capability.DirtMask,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _decalMask = new ShaderTextureViewModel(
                _capability.DecalMask,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);
            _skinMask = new ShaderTextureViewModel(
                _capability.SkinMask,
                packFileService,
                uiCommandFactory,
                resourceLibrary,
                packFileUiProvider,
                propertyEditor);

            _uvScale = new Vector2ViewModel(
                _capability.UvScale,
                OnUvScaleChanged);
            _textureTransform = new Vector4ViewModel(
                _capability.TextureTransform,
                OnTextureTransformChanged);

            UpdateVisability();
        }

        void OnUvScaleChanged(Vector2 value) =>
            _propertyEditor.Update(
                _capability.UvScale,
                value,
                newValue => _capability.UvScale = newValue,
                newValue => UvScale.Set(newValue));

        void OnTextureTransformChanged(Vector4 value) =>
            _propertyEditor.Update(
                _capability.TextureTransform,
                value,
                newValue => _capability.TextureTransform = newValue,
                newValue => TextureTransform.Set(newValue));

        partial void OnUseDecalChanged(bool value) =>
            _propertyEditor.Update(
                _capability.UseDecal,
                value,
                newValue => _capability.UseDecal = newValue,
                newValue =>
                {
                    UseDecal = newValue;
                    UpdateVisability();
                });

        partial void OnUseDirtChanged(bool value) =>
            _propertyEditor.Update(
                _capability.UseDirt,
                value,
                newValue => _capability.UseDirt = newValue,
                newValue =>
                {
                    UseDirt = newValue;
                    UpdateVisability();
                });

        partial void OnUseSkinChanged(bool value) =>
            _propertyEditor.Update(
                _capability.UseSkin,
                value,
                newValue => _capability.UseSkin = newValue,
                newValue =>
                {
                    UseSkin = newValue;
                    UpdateVisability();
                });

        void UpdateVisability()
        {
            ShowDirtMap = false;
            ShowDirtMask = false;
            ShowDecalMask = false;
            ShowSkinMask = false;
            ShowUvScale = false;
            ShowTextureTransform = false;

            if (UseDecal)
            {
                ShowTextureTransform = true;
                ShowDecalMask = true;
            }

            if (UseDirt)
            {
                ShowDirtMap = true;
                ShowDirtMask = true;
                ShowUvScale = true;
            }

            if (UseSkin)
            {
                ShowSkinMask = true;
                ShowUvScale = true;
            }
        }
    }
}
