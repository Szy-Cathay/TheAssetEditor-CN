namespace Editors.AnimationMeta.SuperView.Visualisation;

internal static class EffectIdFilter
{
    internal static bool IsAllowed(int effectId, int currentId, bool enabled, IReadOnlyList<byte>? enabledGroupMask = null)
    {
        if (!enabled) return true;
        if (effectId is >= 1000 and <= 1099)
        {
            if (enabledGroupMask == null) return true;
            if (enabledGroupMask.Count < 13) throw new ArgumentException("A native Effect group mask requires 100 bits.", nameof(enabledGroupMask));
            var index = effectId - 1000;
            return (enabledGroupMask[index / 8] & (1 << (index % 8))) != 0;
        }
        // The native unsigned range checks leave 0 and 1100 unconditional.
        return effectId is 0 or 1100 || effectId == currentId;
    }
}
