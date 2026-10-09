using Shared.Core.Services;

namespace Editors.VfxEditor;

// Guidance changes presentation only; choice keys remain the original game values.
internal static class VfxFieldGuidance
{
    private static readonly string[] CommonFields =
    [
        "reference_position", "reference_rotation", "reference_scale", "reference_start",
        "curve_colour", "tint_min", "tint_max", "ParticleScale", "ParticleInitialScale", "scale_min", "scale_max",
        "curve_alpha", "ParticleInitialAlpha", "EmissionRateCurve", "SpawnSpacing", "spawn_rate_scale_factor_min", "spawn_rate_scale_factor_max",
        "ParticleLifeTime", "LifeTime", "InfiniteLife", "infinite_life", "StartTime", "start_time_min", "start_time_max",
        "ParticleInitialSpeed", "EmitterPosition", "pos_x", "pos_y", "pos_z", "diffuse", "enable", "Enable"
    ];

    public static int CommonOrder(string name)
    {
        var index = Array.IndexOf(CommonFields, name);
        return index < 0 ? int.MaxValue : index;
    }

    public static IReadOnlyList<VfxChoice> Choices(string name, string current, LocalizationManager localization)
    {
        string[] values = name switch
        {
            "orientation_mode" => ["Camera-aligned with stretching factor", "Camera-facing velocity-aligned", "Horizontally-oriented"],
            "ParticleSimulationSpace" => ["LOCAL_SPACE_POSITION_ONLY", "LOCAL_SPACE_POSITION_ROTATION"],
            "ParticleRelativeSpawnOrientation" => ["RELATIVE_TO_TERRAIN_NORMAL", "RELATIVE_TO_VFX_UP_AXIS"],
            "ParticleRelativeSpawnDirection" => ["DISABLED", "RELATIVE_TO_TERRAIN_NORMAL"],
            "ParticleSpinDirectionX" or "ParticleSpinDirectionY" or "ParticleSpinDirectionZ" => ["CW", "CCW"],
            "uv_orientation" => ["untouched", "90_degrees_ccw"],
            "track_orientation_mode" => ["orientation", "orientation_by_velocity"],
            "time_mode" => ["fixed_animation_speed", "using_particle_lifetime"],
            "scene_view_type" => ["DEFERRED_DECAL", "DEFERRED_DECAL_EMISSIVE", "DEFERRED_DYNAMIC_LIGHT", "DISTORTION", "FULLRES_DEFERRED_ALPHA", "HALFRES_DEFERRED_ALPHA", "HIRES_VFXUI3D"],
            _ => []
        };
        if (values.Length == 0) return [];
        return values.Append(current).Distinct(StringComparer.Ordinal).Select(value => new VfxChoice(value,
            localization.GetOrDefault("Vfx.Option." + value, localization.GetFormat("Vfx.Option.Unrecognized", value)))).ToArray();
    }
}
