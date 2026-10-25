using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxOneShotMeshMaterial
{
    // Order matches the shipped 216-byte AUTO_PARAMETERS buffer.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("colour_distortion_switch",1), ("colour_overlay_pan",2), ("colour_overlay_strength",1), ("colour_overlay_tile",2),
        ("depth_fade_amount",1), ("depth_fade_switch",1), ("global_mask_pan",2), ("global_mask_tile",2),
        ("glow_colour",3), ("glow_strength",1), ("normal_fade_in_out",1), ("normal_fade_power",1),
        ("oneshot_mask_direction",2), ("oneshot_mask_tiling",2),
        ("tex_1_brightness",1), ("tex_1_channels",4), ("tex_1_pan",2), ("tex_1_tile",2),
        ("tex_2_brightness",1), ("tex_2_channels",4), ("tex_2_pan",2), ("tex_2_tile",2),
        ("tex_3_brightness",1), ("tex_3_channels",4), ("tex_3_pan",2), ("tex_3_tile",2),
        ("texture_power",1), ("total_alpha_strength",1), ("vertex_alpha_contrast",1), ("vertex_colour",1), ("vfx_mesh_alpha",1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new List<float>(54);
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
        // The public renderer has six user texture slots. The zero-distortion path uses exactly six.
        if (parameters[0] != 0) throw new VfxMappingException("colour_distortion_switch", "Vfx.Preview.MaterialUnsupported");
        if (parameters[6] < 0 || parameters[17] < 0 || parameters[49] < 0 || parameters[51] < 0)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        var paths = new[] { "t_xml_tex_1", "t_xml_tex_2", "t_xml_tex_3", "t_xml_colour_overlay", "t_xml_oneshot_mask", "t_xml_global_mask" }.Select(slot =>
        {
            var nodes = root.Element("textures")?.Elements("texture").Where(n => string.Equals((string?)n.Element("slot"), slot, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace((string?)nodes[0].Element("source")))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, parameters.ToArray());
    }
}
