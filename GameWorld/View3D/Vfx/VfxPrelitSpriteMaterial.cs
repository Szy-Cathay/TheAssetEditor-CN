using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxPrelitSpriteMaterial
{
    // Matches the shipped 84-byte AUTO_PARAMETERS buffer, padded to six float4 uniforms.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("colour_multiply_power", 1), ("colour_overlay_power_post", 1), ("emissive_colour", 3),
        ("emissive_colour_0_to_sample", 1), ("emissive_colour_tint_multiplier", 3),
        ("emissive_distortion_direction", 2), ("emissive_distortion_strength", 1),
        ("emissive_distortion_uv_scale", 2), ("emissive_mask_power", 1), ("emissiveness", 1),
        ("fog_strength", 1), ("lerp_glowy_to_diffuse_scale_emissive_col", 1),
        ("lerp_multiply_col_to_overlay_col", 1), ("particle_vfx_func_depth_fade_scale_strength", 1),
        ("particle_vfx_func_depth_fade_soft_factor", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var values = new List<float>(24);
        foreach (var (name, components) in Fields)
        {
            var nodes = root.Elements("params").Elements("param").Where(p =>
                string.Equals(p.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var parts = (nodes[0].Element("value")?.Value ?? "").Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (!VfxCurve.TryNumber(part, out var number) || Math.Abs(number) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                values.Add((float)number);
            }
        }
        if (values[17] != 0) throw new VfxMappingException("lerp_glowy_to_diffuse_scale_emissive_col", "Vfx.Preview.MaterialUnsupported");
        if (values[0] is <= 0 or > 16 || values[1] is <= 0 or > 16 || values[5] is < 0 or > 1
            || values[14] is <= 0 or > 16 || values[15] < 0 || values[16] is < 0 or > 1
            || values[18] is < 0 or > 1 || values[19] is < 0 or > 1 || values[20] < 0)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        var paths = new[] { "t_xml_emissive_distortion_texture_512_bc1", "t_xml_emissiveness_mask_1024_only", "t_xml_material_map", "t_xml_particle_diffuse_2048_only", "t_xml_particle_normal_1024_only" }
            .Select(slot =>
            {
                var nodes = root.Elements("textures").Elements("texture").Where(t =>
                    string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                    throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
                return nodes[0].Element("source")!.Value;
            }).ToArray();
        while (values.Count < 24) values.Add(0);
        return new(paths, values.ToArray());
    }
}
