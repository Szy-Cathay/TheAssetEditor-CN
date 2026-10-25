using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxAnimatedUvMaterial
{
    // The shipped 108-byte buffer is followed by two runtime mask rectangles.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("alpha_direction_1", 2), ("alpha_direction_2", 2), ("alpha_scale_1", 2), ("alpha_scale_2", 2),
        ("detail_direction", 2), ("detail_distortion", 1), ("detail_emission", 1), ("detail_scale", 2),
        ("emissive", 1), ("main_tex_direction_1", 2), ("main_tex_direction_2", 2),
        ("main_tex_scale_1", 2), ("main_tex_scale_2", 2), ("particle_vfx_func_depth_fade_scale_strength", 1),
        ("particle_vfx_func_depth_fade_soft_factor", 1), ("particle_vfx_func_fog_enable", 1), ("tex_power", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var shader = root.Element("shader")?.Value.Replace('\\', '/').ToLowerInvariant() ?? "";
        if (shader != "shaders/fx_animated_uvs_custom.xml.shader")
            throw new VfxMappingException(shader, "Vfx.Preview.MaterialUnsupported");
        var textures = root.Elements("textures").Elements("texture").ToArray();
        var paths = new[] { "t_xml_alpha_1", "t_xml_alpha_2", "t_xml_detail_tex", "t_xml_emissiveness_mask_1024_only", "t_xml_global_opacity", "t_xml_main_tex_diffuse_1", "t_xml_main_tex_diffuse_2" }
            .Select(slot =>
            {
                var matches = textures.Where(t => string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Element("source")?.Value))
                    throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
                return matches[0].Element("source")!.Value;
            }).ToArray();
        var values = new List<float>(36);
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
        if (values[11] < 0 || values[14] < 0 || values[23] is < 0 or > 1 || values[24] < 0
            || values[25] is not (0 or 1) || values[26] is <= 0 or > 16)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        while (values.Count < 36) values.Add(0);
        return new(paths, values.ToArray());
    }
}
