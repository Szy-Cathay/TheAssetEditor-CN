using System.Collections.Generic;
using GameWorld.Core.SceneNodes;
using Shared.Core.Services;

namespace GameWorld.Core.Commands.Object
{
    public class SortSceneNodesCommand : ICommand
    {
        readonly Dictionary<ISceneNode, List<ISceneNode>> _originalOrders = new();
        ISceneNode _rootNode;

        public string HintText => LocalizationManager.Instance?.Get("Kitbash.CommandHint.SortSceneNodes")
            ?? "排序场景节点";
        public bool IsMutation => true;

        public void Configure(ISceneNode rootNode)
        {
            _rootNode = rootNode;
        }

        public void Execute()
        {
            if (_originalOrders.Count == 0)
                CaptureOriginalOrder(_rootNode);

            SortChildren(_rootNode);
        }

        public void Undo()
        {
            foreach (var nodeOrder in _originalOrders)
                SetChildOrder(nodeOrder.Key, nodeOrder.Value);
        }

        void CaptureOriginalOrder(ISceneNode node)
        {
            _originalOrders[node] = new List<ISceneNode>(node.Children);

            foreach (var child in node.Children)
            {
                if (child is GroupNode)
                    CaptureOriginalOrder(child);
            }
        }

        static void SortChildren(ISceneNode node)
        {
            var children = new List<ISceneNode>(node.Children);
            children.Sort((left, right) => left.Name.CompareTo(right.Name));
            SetChildOrder(node, children);

            foreach (var child in children)
            {
                if (child is GroupNode)
                    SortChildren(child);
            }
        }

        static void SetChildOrder(ISceneNode node, IReadOnlyList<ISceneNode> children)
        {
            foreach (var child in new List<ISceneNode>(node.Children))
                node.RemoveObject(child);

            foreach (var child in children)
                node.AddObject(child);
        }
    }
}
