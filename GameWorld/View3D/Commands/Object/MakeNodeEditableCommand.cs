using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.SceneNodes;
using Shared.Core.Services;

namespace GameWorld.Core.Commands.Object
{
    public class MakeNodeEditableCommand : ICommand
    {
        readonly Dictionary<ISceneNode, List<ISceneNode>> _originalChildOrders = new();
        readonly Dictionary<ISceneNode, NodeState> _originalNodeStates = new();
        readonly List<Rmv2MeshNode> _meshes = new();
        Rmv2ModelNode _mainNode;
        ISceneNode _selectedNode;
        ISceneNode _selectedNodeParent;
        Rmv2LodNode _editableLod;
        bool _createdEditableLod;
        bool _isConfigured;

        public string HintText => LocalizationManager.Instance?.Get("Kitbash.CommandHint.MakeNodeEditable")
            ?? "使节点可编辑";
        public bool IsMutation => true;

        public void Configure(Rmv2ModelNode mainNode, ISceneNode selectedNode)
        {
            _mainNode = mainNode;
            _selectedNode = selectedNode;
            CaptureOriginalState();
            _isConfigured = true;
        }

        public void Execute()
        {
            if (!_isConfigured)
                return;

            if (!_mainNode.Children.Contains(_editableLod))
                _mainNode.AddObject(_editableLod);

            foreach (var mesh in _meshes)
            {
                if (mesh.Parent != _editableLod)
                {
                    mesh.Parent?.RemoveObject(mesh);
                    _editableLod.AddObject(mesh);
                }

                mesh.IsEditable = true;
                mesh.IsSelectable = true;
            }

            if (_selectedNode is not Rmv2MeshNode)
            {
                _selectedNode.Parent?.RemoveObject(_selectedNode);
                _selectedNode.ForeachNodeRecursive(SetEditable);
            }
        }

        public void Undo()
        {
            if (!_isConfigured)
                return;

            if (_selectedNode is not Rmv2MeshNode &&
                _selectedNodeParent != null &&
                !_selectedNodeParent.Children.Contains(_selectedNode))
            {
                _selectedNodeParent.AddObject(_selectedNode);
            }

            foreach (var mesh in _meshes)
            {
                var originalParent = _originalChildOrders
                    .First(x => x.Value.Contains(mesh))
                    .Key;

                if (mesh.Parent != originalParent)
                {
                    mesh.Parent?.RemoveObject(mesh);
                    originalParent.AddObject(mesh);
                }
            }

            foreach (var nodeState in _originalNodeStates)
            {
                nodeState.Key.IsEditable = nodeState.Value.IsEditable;
                if (nodeState.Key is ISelectable selectable && nodeState.Value.IsSelectable.HasValue)
                    selectable.IsSelectable = nodeState.Value.IsSelectable.Value;
            }

            foreach (var childOrder in _originalChildOrders)
                RestoreChildOrder(childOrder.Key, childOrder.Value);

            if (_createdEditableLod && _editableLod.Children.Count == 0)
                _mainNode.RemoveObject(_editableLod);
        }

        void CaptureOriginalState()
        {
            _selectedNodeParent = _selectedNode.Parent;
            CaptureNodeState(_selectedNode);

            var lods = _mainNode.GetLodNodes();
            if (lods.Count == 0)
            {
                _editableLod = new Rmv2LodNode("Lod 0", 0);
                _createdEditableLod = true;
            }
            else
            {
                _editableLod = lods[0];
            }

            _meshes.AddRange(GetMeshesToMove(_selectedNode));
            foreach (var mesh in _meshes)
            {
                CaptureNodeState(mesh);
                CaptureChildOrder(mesh.Parent);
            }

            CaptureChildOrder(_selectedNodeParent);
        }

        void CaptureNodeState(ISceneNode node)
        {
            node.ForeachNodeRecursive(child =>
            {
                if (_originalNodeStates.ContainsKey(child))
                    return;

                bool? isSelectable = child is ISelectable selectable
                    ? selectable.IsSelectable
                    : null;
                _originalNodeStates[child] = new NodeState(child.IsEditable, isSelectable);
            });
        }

        void CaptureChildOrder(ISceneNode node)
        {
            if (node != null && !_originalChildOrders.ContainsKey(node))
                _originalChildOrders[node] = new List<ISceneNode>(node.Children);
        }

        static List<Rmv2MeshNode> GetMeshesToMove(ISceneNode selectedNode)
        {
            if (selectedNode is Rmv2MeshNode mesh)
                return [mesh];

            if (selectedNode is Rmv2LodNode lod)
                return SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(lod);

            if (selectedNode is Rmv2ModelNode model)
                return GetFirstLodMeshes(model);

            if (selectedNode is WsModelGroup wsModelGroup)
            {
                var modelNode = wsModelGroup.Children.OfType<Rmv2ModelNode>().FirstOrDefault();
                return modelNode == null ? [] : GetFirstLodMeshes(modelNode);
            }

            return [];
        }

        static List<Rmv2MeshNode> GetFirstLodMeshes(Rmv2ModelNode modelNode)
        {
            var lod = modelNode.GetLodNodes()
                .OrderBy(x => x.LodValue)
                .FirstOrDefault();
            return lod == null
                ? []
                : SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(lod);
        }

        static void SetEditable(ISceneNode node)
        {
            node.IsEditable = true;
            if (node is Rmv2MeshNode mesh)
                mesh.IsSelectable = true;
        }

        static void RestoreChildOrder(ISceneNode parent, IReadOnlyList<ISceneNode> children)
        {
            foreach (var child in new List<ISceneNode>(parent.Children))
                parent.RemoveObject(child);

            foreach (var child in children)
                parent.AddObject(child);
        }

        readonly record struct NodeState(bool IsEditable, bool? IsSelectable);
    }
}
