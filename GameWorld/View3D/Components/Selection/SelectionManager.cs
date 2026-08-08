using System;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;

namespace GameWorld.Core.Components.Selection
{
    public class SelectionChangedEvent
    {
        public ISelectionState NewState { get; internal set; }
    }

    public sealed class SelectionManager : IDisposable
    {
        private ISelectionState _currentState;
        private readonly IEventHub _eventHub;
        private float _vertexSelectionFalloff;

        public event Action<ISelectionState>? StateChanged;
        public event Action<ISelectionState>? SelectionChanged;

        public SelectionManager(IEventHub eventHub)
        {
            _eventHub = eventHub;
            _currentState = new ObjectSelectionState();
            _currentState.SelectionChanged +=
                SelectionManager_SelectionChanged;
        }

        public ISelectionState CreateSelectionSate(
            GeometrySelectionMode mode,
            ISelectable selectedObj,
            bool sendEvent = true)
        {
            _currentState.Clear();
            _currentState.SelectionChanged -=
                SelectionManager_SelectionChanged;

            _currentState = mode switch
            {
                GeometrySelectionMode.Object =>
                    new ObjectSelectionState(),
                GeometrySelectionMode.Face =>
                    new FaceSelectionState(),
                GeometrySelectionMode.Edge =>
                    new EdgeSelectionState(),
                GeometrySelectionMode.Vertex =>
                    new VertexSelectionState(
                        selectedObj,
                        _vertexSelectionFalloff),
                GeometrySelectionMode.Bone =>
                    new BoneSelectionState(selectedObj),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    null)
            };

            _currentState.SelectionChanged +=
                SelectionManager_SelectionChanged;
            SelectionManager_SelectionChanged(
                _currentState,
                sendEvent);
            StateChanged?.Invoke(_currentState);
            return _currentState;
        }

        public ISelectionState GetState() => _currentState;

        public State GetState<State>()
            where State : class, ISelectionState =>
            _currentState as State;

        public ISelectionState GetStateCopy() =>
            _currentState.Clone();

        public State GetStateCopy<State>()
            where State : class, ISelectionState =>
            GetState<State>().Clone() as State;

        public void SetState(ISelectionState state)
        {
            if (state == null)
                return;

            _currentState.SelectionChanged -=
                SelectionManager_SelectionChanged;
            _currentState = state;
            _currentState.SelectionChanged -=
                SelectionManager_SelectionChanged;
            _currentState.SelectionChanged +=
                SelectionManager_SelectionChanged;
            SelectionManager_SelectionChanged(
                _currentState,
                true);
            StateChanged?.Invoke(_currentState);
        }

        private void SelectionManager_SelectionChanged(
            ISelectionState state,
            bool sendEvent)
        {
            SelectionChanged?.Invoke(state);
            _eventHub.Publish(
                new SelectionChangedEvent
                {
                    NewState = state
                });
        }

        public void UpdateVertexSelectionFallof(float newValue)
        {
            var clampedValue = Math.Clamp(
                newValue,
                0,
                float.MaxValue);
            if (_vertexSelectionFalloff == clampedValue)
                return;

            _vertexSelectionFalloff = clampedValue;
            var vertexSelectionState =
                GetState<VertexSelectionState>();
            if (vertexSelectionState == null)
                return;

            vertexSelectionState.UpdateWeights(
                _vertexSelectionFalloff);
            SelectionManager_SelectionChanged(
                vertexSelectionState,
                true);
        }

        public float VertexSelectionFalloff =>
            _vertexSelectionFalloff;

        public void Dispose()
        {
            _eventHub.UnRegister(this);
            _currentState.SelectionChanged -=
                SelectionManager_SelectionChanged;
            _currentState.Clear();
        }
    }
}
