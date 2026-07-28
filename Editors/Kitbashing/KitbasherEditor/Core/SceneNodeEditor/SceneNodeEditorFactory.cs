using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using Microsoft.Extensions.DependencyInjection;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor
{
    public interface ISceneNodeEditorFactory
    {
        ISceneNodeEditor? Create(ISceneNode node);
    }

    public sealed class SceneNodeEditorFactory : ISceneNodeEditorFactory
    {
        private static readonly IReadOnlyDictionary<Type, Type> ViewModelTypes =
            new Dictionary<Type, Type>
            {
                [typeof(MainEditableNode)] = typeof(MainEditableNodeViewModel),
                [typeof(Rmv2MeshNode)] = typeof(MeshEditorViewModel),
                [typeof(SkeletonNode)] = typeof(SkeletonSceneNodeViewModel),
                [typeof(GroupNode)] = typeof(GroupNodeViewModel)
            };

        private readonly IServiceProvider _serviceProvider;

        public SceneNodeEditorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ISceneNodeEditor? Create(ISceneNode node)
        {
            if (node is GroupNode groupNode &&
                (groupNode.Name == SpecialNodes.ReferenceMeshs ||
                 groupNode.Name == SpecialNodes.Root))
            {
                return null;
            }

            if (!ViewModelTypes.TryGetValue(node.GetType(), out var viewModelType))
                return null;

            var viewModel = ActivatorUtilities.CreateInstance(
                _serviceProvider,
                viewModelType) as ISceneNodeEditor;
            if (viewModel == null)
                throw new InvalidOperationException(
                    $"{viewModelType} is not of type {nameof(ISceneNodeEditor)}");

            viewModel.Initialize(node);
            return viewModel;
        }
    }
}
