using GameWorld.Core.Commands.Face;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Serilog;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Commands.Object
{
    public class DuplicateObjectCommand : IRedoableCommand
    {
        readonly ILogger _logger = Logging.Create<FaceSelectionCommand>();
        List<ISceneNode> _objectsToCopy;
        readonly List<ISceneNode> _clonedObjects = new List<ISceneNode>();
        readonly SelectionManager _selectionManager;

        ISelectionState _oldState;

        public string HintText { get => "Duplicate Object"; }
        public bool IsMutation { get => true; }

        public void Configure(List<ISceneNode> objectsToCopy)
        {
            _objectsToCopy = new List<ISceneNode>(objectsToCopy);
        }

        public DuplicateObjectCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Execute()
        {
            _logger.Here().Information($"Command info - Items[{string.Join(',', _objectsToCopy.Select(x => x.Name))}]");

            _oldState = _selectionManager.GetStateCopy();

            foreach (var item in _objectsToCopy)
            {
                var clonedItem = SceneNodeHelper.CloneNode(item);
                clonedItem.Id = item.Id + Guid.NewGuid().ToString();
                _clonedObjects.Add(clonedItem);
            }

            Redo();
        }

        public void Redo()
        {
            var objectState = new ObjectSelectionState();
            foreach (var item in _clonedObjects)
            {
                item.Parent.AddObject(item);
                if (item is ISelectable selectableNode)
                    objectState.ModifySelectionSingleObject(selectableNode, false);
            }
            _selectionManager.SetState(objectState);
        }

        public void Undo()
        {
            foreach (var item in _clonedObjects)
                item.Parent.RemoveObject(item);

            _selectionManager.SetState(_oldState);
        }
    }
}
