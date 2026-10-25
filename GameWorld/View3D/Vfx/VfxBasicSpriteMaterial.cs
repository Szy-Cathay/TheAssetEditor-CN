using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxBasicSpriteMaterial
{
    // The shipped AUTO_PARAMETERS buffer contains 19 floats, padded to five float4 uniforms.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("colour_multiply_power", 1), ("colour_overlay_power_post", 1), ("emissive_colour_multiply", 3),
        ("emissive_distortion_direction", 2), ("emissive_distortion_strength", 1), ("emissive_distortion_uv_scale", 2),
        ("emissive_mask_power", 1), ("emissiveness", 1), ("fresnel_power", 1), ("lerp_mask_or_diff_for_emiss", 1),
        ("lerp_multiply_col_to_overlay_col", 1), ("no_fresnel_or_fresnel", 1),
        ("particle_vfx_func_depth_fade_scale_strength", 1), ("particle_vfx_func_depth_fade_soft_factor", 1),
        ("particle_vfx_func_fog_enable", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var values = new List<float>(20);
        foreach (var (name, components) in Fields)
        {
            var matches = root.Elements("params").Elements("param").Where(p => string.Equals(p.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var parts = (matches[0].Element("value")?.Value ?? "").Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (name == "particle_vfx_func_fog_enable" && bool.TryParse(part, out var flag)) { values.Add(flag ? 1 : 0); continue; }
                if (!VfxCurve.TryNumber(part, out var number) || Math.Abs(number) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                values.Add((float)number);
            }
        }
        if (values[15] != 0) throw new VfxMappingException("no_fresnel_or_fresnel", "Vfx.Preview.MaterialUnsupported");
        if (values[0] is <= 0 or > 16 || values[1] is <= 0 or > 16 || values[10] is <= 0 or > 16 || values[11] < 0
            || values[13] is < 0 or > 1 || values[14] is < 0 or > 1 || values[16] is < 0 or > 1 || values[17] < 0 || values[18] is not (0 or 1))
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        values.Add(0);
        var paths = new[] { "t_xml_emissive_distortion_texture_512_bc1", "t_xml_emissiveness_mask_1024_only", "t_xml_particle_diffuse_2048_only", "t_xml_particle_normal_1024_only" }
            .Select(slot =>
            {
                var matches = root.Elements("textures").Elements("texture").Where(p => string.Equals(p.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Element("source")?.Value))
                    throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
                return matches[0].Element("source")!.Value;
            }).ToArray();
        return new(paths, values.ToArray());
    }
}
