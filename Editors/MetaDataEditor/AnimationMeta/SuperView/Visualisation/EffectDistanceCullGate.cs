namespace Editors.AnimationMeta.SuperView.Visualisation;

internal static class EffectDistanceCullGate
{
    // The host supplies actor LOD state; emitter distances and VFX quality are separate inputs.
    internal static bool ShouldCull(bool effectFlag, bool actorLodCondition, int overrideCount = 0)
        => overrideCount == 0 && effectFlag && actorLodCondition;
}
