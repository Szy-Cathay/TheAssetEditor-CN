namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private readonly bool _hasExplicitModelStops = conversion.Emitters.Any(emitter => emitter.Kind == 1 && emitter.DestroyOnExplicitStop);

    private bool ExplicitModelStopped(EmitterState emitter, double time)
        => _hasExplicitModelStops && emitter.Parent == null && emitter.Definition.Kind == 1
            && emitter.Definition.DestroyOnExplicitStop && _emissionEndTimeSeconds is { } stop && time >= stop;

    private void ClearExplicitlyStoppedModels(double time)
    {
        if (!_hasExplicitModelStops || _emissionEndTimeSeconds is not { } stop || time < stop) return;
        var changed = false;
        for (var index = _particles.Count - 1; index >= 0; index--)
        {
            var particle = _particles[index];
            if (!ExplicitModelStopped(particle.Emitter, time)) continue;
            if (particle.EventInstances is { } instances)
                foreach (var child in instances) FreezeEvent(child, stop);
            // Native owner stop clears this controller; it does not invoke ordinary particle-death events.
            particle.Alive = false;
            particle.Emitter.ActiveCount--;
            _particles.RemoveAt(index);
            changed = true;
        }
        if (changed) ClearAppearanceEntries();
    }
}
