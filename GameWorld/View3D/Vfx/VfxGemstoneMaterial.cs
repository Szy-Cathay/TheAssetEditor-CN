using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxGemstoneMaterial
{
    // Order matches the shipped gemstone shader's 160-byte AUTO_PARAMETERS buffer.
    internal static readonly (string Name, int Components)[] Fields =
    [
        ("alpha",1), ("back_color",3), ("dissolve_color_lerp_strength",1), ("dissolve_end_color",3),
        ("dissolve_glow_edge",1), ("dissolve_glow_size",1), ("dissolve_glow_strength",1), ("dissolve_start_color",3),
        ("dissolve_uv_scale",2), ("fresnel_color",3), ("fresnel_glow_strength",1), ("fresnel_power_bias",2),
        ("refract_color",3), ("refract_distortion_amount",1), ("refract_uv_offset",2), ("refract_uv_tiling",2),
        ("surface_emissive_color",3), ("surface_emissive_strength",1), ("surface_emissive_uv_offset",2),
        ("surface_emissive_uv_tiling",2), ("surface_metalness",1), ("surface_roughness",1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new List<float>(40);
        foreach (var field in Fields)
        {
            var nodes = root.Element("params")?.Elements("param").Where(n => string.Equals((string?)n.Element("name"), field.Name, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (nodes.Length != 1) throw new VfxMappingException(field.Name, "Vfx.Preview.AmbiguousField");
            var values = ((string?)nodes[0].Element("value") ?? "").Split(',');
            if (values.Length != field.Components) throw new VfxMappingException(field.Name, "Vfx.Preview.InvalidData");
            foreach (var value in values)
            {
                if (!VfxCurve.TryNumber(value, out var number) || Math.Abs(number) > 1000000)
                    throw new VfxMappingException(field.Name, "Vfx.Preview.RangeUnsupported");
                parameters.Add((float)number);
            }
        }
        // Alpha one cannot fail the source's screen-space dither test, leaving six active inputs.
        if (parameters[0] != 1) throw new VfxMappingException("alpha", "Vfx.Preview.MaterialUnsupported");
        if (parameters[4] <= -1 || parameters[9] <= 0 || parameters[20] < 0)
            throw new VfxMappingException("dissolve/fresnel", "Vfx.Preview.RangeUnsupported");
        var paths = new[] { "dissolve_detail", "dissolve_map", "refract_distortion_texture", "refract_texture", "surface_emissive", "surface_normal" }.Select(name =>
        {
            var slot = "t_xml_" + name;
            var nodes = root.Element("textures")?.Elements("texture").Where(n => string.Equals((string?)n.Element("slot"), slot, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace((string?)nodes[0].Element("source")))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, parameters.ToArray());
    }
}
