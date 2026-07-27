using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Serilog;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Commands.Object
{
    public class DeleteObjectsCommand : ICommand
    {
        private readonly ILogger _logger = Logging.Create<DeleteObjectsCommand>();
        private readonly SelectionManager _selectionManager;
        
        List<ISceneNode> _itemsToDelete = [];
        readonly Dictionary<ISceneNode, List<ISceneNode>> _originalChildOrders = new();
        ISelectionState? _oldState;

        public string HintText { get => "Delete Object"; }
        public bool IsMutation { get => true; }

        public DeleteObjectsCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Configure(List<ISelectable> itemsToDelete)
        {
            _itemsToDelete = new List<ISceneNode>(itemsToDelete.Select(x => x));
        }

        public void Configure(List<ISceneNode> itemsToDelete)
        {
            _itemsToDelete = new List<ISceneNode>(itemsToDelete);
        }

        public void Configure(ISceneNode itemToDelete)
        {
            _itemsToDelete = [itemToDelete];
        }

        public void Execute()
        {
            _oldState = _selectionManager.GetStateCopy();

            _logger.Here().Information($"Command info - Items[{string.Join(',', _itemsToDelete.Select(x => x.Name))}]");
            if (_originalChildOrders.Count == 0)
            {
                foreach (var parent in _itemsToDelete.Select(x => x.Parent).Where(x => x != null).Distinct())
                    _originalChildOrders[parent] = new List<ISceneNode>(parent.Children);
            }

            foreach (var item in _itemsToDelete)
            {
                if (item.Parent?.Children.Contains(item) == true)
                    item.Parent.RemoveObject(item);
            }

            _selectionManager.CreateSelectionSate(GeometrySelectionMode.Object, null);
        }

        public void Undo()
        {
            foreach (var childOrder in _originalChildOrders)
            {
                var additionalChildren = childOrder.Key.Children
                    .Except(childOrder.Value)
                    .ToList();
                foreach (var child in new List<ISceneNode>(childOrder.Key.Children))
                    childOrder.Key.RemoveObject(child);
                foreach (var child in childOrder.Value)
                    childOrder.Key.AddObject(child);
                foreach (var child in additionalChildren)
                    childOrder.Key.AddObject(child);
            }

            _selectionManager.SetState(_oldState!);
        }
    }
}
