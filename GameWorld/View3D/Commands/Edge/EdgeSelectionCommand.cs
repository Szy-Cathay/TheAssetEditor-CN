using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using Serilog;
using Shared.Core.ErrorHandling;
using System.Collections.Generic;

namespace GameWorld.Core.Commands.Edge
{
    public class EdgeSelectionCommand : ICommand
    {
        ILogger _logger = Logging.Create<EdgeSelectionCommand>();
        SelectionManager _selectionManager;
        ISelectionState _oldState;

        bool _isAdd;
        bool _isRemove;
        List<(int v0, int v1)> _selectedEdges;

        public string HintText { get => "Select Edge"; }
        public bool IsMutation { get => true; }
        public bool AffectsDocument => false;

        public void Configure(List<(int v0, int v1)> selectedEdges, bool isAdd, bool isRemove)
        {
            _selectedEdges = selectedEdges;
            _isAdd = isAdd;
            _isRemove = isRemove;
        }

        public EdgeSelectionCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Execute()
        {
            _oldState = _selectionManager.GetStateCopy();
            var currentState = _selectionManager.GetState() as EdgeSelectionState;
            _logger.Here().Information($"Command info - Add[{_isAdd}] Edges[{_selectedEdges.Count}]");

            if (!(_isAdd || _isRemove))
                currentState.Clear();

            currentState.ModifySelection(_selectedEdges, _isRemove);
        }

        public void Undo()
        {
            _selectionManager.SetState(_oldState);
        }
    }
}
