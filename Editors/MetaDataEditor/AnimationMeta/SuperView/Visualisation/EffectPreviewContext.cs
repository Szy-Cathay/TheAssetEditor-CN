using Shared.Core.PackFiles.Models;

namespace Editors.AnimationMeta.SuperView.Visualisation;

// Scene inputs supplied by the game at runtime; these are never serialized into META.
public sealed record EffectPreviewContext
{
    public int TerrainType { get; init; } = 1;
    public bool BypassTerrainSelection { get; init; }
    public bool ApplyRootMotionCompensation { get; init; } = true;
    public PackFile? AuraResource { get; init; }
    public PackFile? WeaponResource { get; init; }
    public bool SuppressDistanceCulledEffects { get; init; }
    public int DistanceCullingOverrideCount { get; init; }
    public bool ApplyIdFilter { get; init; }
    public int CurrentEffectId { get; init; }
    public IReadOnlyList<byte>? EffectGroupMask { get; init; }
}
