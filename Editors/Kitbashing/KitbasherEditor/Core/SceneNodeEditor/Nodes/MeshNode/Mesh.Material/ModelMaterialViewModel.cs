using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.DirtAndDecal;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Emissive;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.SpecGloss;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor.Nodes.MeshNode.Mesh.WsMaterial.Tint;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.MeshSubViews
{
    public partial class WsMaterialViewModel : ObservableObject
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly SelectionManager _selectionManager;
        private readonly SceneManager _sceneManager;
        private readonly IPackFileService _packFileService;
        private readonly IScopedResourceLibrary _resourceLibrary;
        private readonly CapabilityMaterialFactory _materialFactory;
        private readonly IStandardDialogs _packFileUiProvider;
        private readonly SceneNodePropertyEditor _propertyEditor;
        private readonly SceneRenderParametersStore _sceneRenderParameters;
        Rmv2MeshNode? _currentNode;

        [ObservableProperty] List<CapabilityMaterialsEnum> _possibleMaterialTypes;
        [ObservableProperty] CapabilityMaterialsEnum? _currentMaterialType;

        [ObservableProperty] MetalRoughViewModel? _metalRough;
        [ObservableProperty] SpecGlossViewModel? _specGloss;
        [ObservableProperty] AdvancedRmvMaterialViewModel? _advanceRvmMaterial;
        [ObservableProperty] BloodViewModel? _blood;
        [ObservableProperty] EmissiveViewModel? _emissive;

        [ObservableProperty] public partial TintViewModel? Tint { get; set; }

        public WsMaterialViewModel(
            IUiCommandFactory uiCommandFactory,
            SelectionManager selectionManager,
            SceneManager sceneManager,
            IPackFileService packFileService,
            IScopedResourceLibrary resourceLibrary,
            CapabilityMaterialFactory abstractMaterialFactory,
            IStandardDialogs packFileUiProvider,
            SceneNodePropertyEditor propertyEditor,
            SceneRenderParametersStore sceneRenderParameters)
        {
            _uiCommandFactory = uiCommandFactory;
            _selectionManager = selectionManager;
            _sceneManager = sceneManager;
            _packFileService = packFileService;
            _resourceLibrary = resourceLibrary;
            _materialFactory = abstractMaterialFactory;
            _packFileUiProvider = packFileUiProvider;
            _propertyEditor = propertyEditor;
            _sceneRenderParameters = sceneRenderParameters;
            _possibleMaterialTypes = _materialFactory.GetPossibleMaterials();
        }

        internal void Initialize(Rmv2MeshNode node)
        {
            _currentNode = node;
            CurrentMaterialType = node.Material.Type;
            CreateCapabilityViews(node.Material);
        }

        partial void OnCurrentMaterialTypeChanged(
            CapabilityMaterialsEnum? oldValue,
            CapabilityMaterialsEnum? newValue)
        {
            Guard.IsNotNull(_currentNode);
            if (newValue == null || _currentNode.Material.Type == newValue.Value)
                return;

            var newMaterial = _materialFactory.ChangeMaterial(
                _currentNode.Material,
                newValue.Value);
            _propertyEditor.Update(
                _currentNode.Material,
                newMaterial,
                ApplyMaterial);
        }

        private void ApplyMaterial(CapabilityMaterial material)
        {
            if (_currentNode == null)
                return;

            _currentNode.Material = material;
            CurrentMaterialType = material.Type;
            CreateCapabilityViews(material);

            // Refresh selection so the renderer and capability panels use the new material.
            var oldState = _selectionManager.GetStateCopy();
            _selectionManager.CreateSelectionSate(GeometrySelectionMode.Object, null);
            _selectionManager.SetState(oldState);
        }

        private void CreateCapabilityViews(CapabilityMaterial material)
        {
            MetalRough = CreateCapabilityView<MetalRoughCapability, MetalRoughViewModel>(
                material,
                capability => new MetalRoughViewModel(
                    capability,
                    _uiCommandFactory,
                    _packFileService,
                    _resourceLibrary,
                    _packFileUiProvider,
                    _propertyEditor));
            SpecGloss = CreateCapabilityView<SpecGlossCapability, SpecGlossViewModel>(
                material,
                capability => new SpecGlossViewModel(
                    capability,
                    _uiCommandFactory,
                    _packFileService,
                    _resourceLibrary,
                    _packFileUiProvider,
                    _propertyEditor));
            AdvanceRvmMaterial = CreateCapabilityView<AdvancedMaterialCapability, AdvancedRmvMaterialViewModel>(
                material,
                capability => new AdvancedRmvMaterialViewModel(
                    capability,
                    _uiCommandFactory,
                    _packFileService,
                    _resourceLibrary,
                    _packFileUiProvider,
                    _propertyEditor));
            Blood = CreateCapabilityView<BloodCapability, BloodViewModel>(
                material,
                capability => new BloodViewModel(
                    capability,
                    _uiCommandFactory,
                    _packFileService,
                    _resourceLibrary,
                    _packFileUiProvider,
                    _propertyEditor));
            Emissive = CreateCapabilityView<EmissiveCapability, EmissiveViewModel>(
                material,
                capability => new EmissiveViewModel(
                    capability,
                    _uiCommandFactory,
                    _packFileService,
                    _resourceLibrary,
                    _packFileUiProvider,
                    _propertyEditor));
            Tint = CreateCapabilityView<TintCapability, TintViewModel>(
                material,
                capability => new TintViewModel(
                    capability,
                    _sceneRenderParameters));
        }

        TViewModel? CreateCapabilityView<T, TViewModel>(
            CapabilityMaterial material,
            Func<T, TViewModel> creator)
            where T : class, ICapability
            where TViewModel : class
        {
            var capability = material.TryGetCapability<T>();
            if (capability != null)
                return creator(capability);
            return null;
        }
    }
}
