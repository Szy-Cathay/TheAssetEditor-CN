using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.MeshSubViews;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2;
using GameWorld.Core.SceneNodes;
using KitbasherEditor.Views.EditorViews.Rmv2;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.Ui.Common.DataTemplates;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes
{
    public partial class MeshEditorViewModel : ObservableObject, ISceneNodeEditor, IViewProvider<MeshEditorView>
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationSettingsService _applicationSettingsService;

        [ObservableProperty] MeshViewModel? _mesh;
        [ObservableProperty] AnimationViewModel? _animation;
        [ObservableProperty] WeightedMaterialViewModel? _material;
        [ObservableProperty] WsMaterialViewModel? _wsMaterial;

        public MeshEditorViewModel(
            IServiceProvider serviceProvider,
            ApplicationSettingsService applicationSettingsService)
        {
            _serviceProvider = serviceProvider;
            _applicationSettingsService = applicationSettingsService;
        }

        public void Initialize(ISceneNode node)
        {
            var typedNode = node as Rmv2MeshNode;
            Guard.IsNotNull(typedNode);

            Mesh = ActivatorUtilities.CreateInstance<MeshViewModel>(_serviceProvider);
            Mesh.Initialize(typedNode);

            Animation = ActivatorUtilities.CreateInstance<AnimationViewModel>(_serviceProvider);
            Animation.Initialize(typedNode);

            WsMaterial = ActivatorUtilities.CreateInstance<WsMaterialViewModel>(_serviceProvider);
            WsMaterial.Initialize(typedNode);
            
            if (typedNode.RmvMaterial is WeightedMaterial)
            {
                Material = ActivatorUtilities.CreateInstance<WeightedMaterialViewModel>(_serviceProvider);
                Material.Initialize(typedNode);
            }
        }

        public void Dispose()
        {
            Animation?.Dispose();
        }
    }
}
