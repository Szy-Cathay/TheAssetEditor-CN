namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private void ActivateEvent(EmitterState state)
    {
        state.Activated = true;
        state.SpawnAnchor = EventAnchor(state, state.ActivationTime, out _);
        PrewarmEvent(state, state.ActivationTime);
    }

    private static bool WithinEventSubtree(EmitterState state, EmitterState? root)
    {
        if (root == null) return true;
        for (EmitterState? current = state; current != null; current = current.Parent?.Emitter)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private void PrewarmEvent(EmitterState state, double eventTime)
    {
        if (!(state.Start < eventTime)) return;
        var globalTime = _simulatedTime;
        var anchor = state.FrozenAnchor; var anchorValid = state.FrozenAnchorValid;
        var scale = state.FrozenScale; var tint = state.FrozenTint;
        // A newly created native instance prepares its negative offset at the event's current pose.
        var preparedAnchor = EventAnchor(state, eventTime, out var preparedValid);
        var preparedScale = EventScale(state, eventTime); var preparedTint = EventTint(state, eventTime);
        state.FrozenAnchor = preparedAnchor; state.FrozenAnchorValid = preparedValid;
        state.FrozenScale = preparedScale; state.FrozenTint = preparedTint;
        try
        {
            _simulatedTime = eventTime + Math.Floor((state.Start - eventTime) / Step) * Step;
            Tick(0, state);
            while (_simulatedTime + Step <= eventTime + .000001)
            {
                _simulatedTime += Step;
                Tick(Step, state);
            }
            foreach (var child in _emitters)
                if (WithinEventSubtree(child, state)) child.PreparedThroughTime = eventTime;
        }
        finally
        {
            _simulatedTime = globalTime;
            state.FrozenAnchor = anchor; state.FrozenAnchorValid = anchorValid;
            state.FrozenScale = scale; state.FrozenTint = tint;
        }
    }
}
