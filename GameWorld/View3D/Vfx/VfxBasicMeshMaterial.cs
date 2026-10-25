using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxBasicMeshMaterial
{
    // Order matches wh3_vfx_basic_mesh's shipped 220-byte AUTO_PARAMETERS buffer.
    internal static readonly (string Name, int Components)[] Fields =
    [
        ("colour_overlay_pan",2), ("colour_overlay_strength",1), ("colour_overlay_tile",2),
        ("depth_fade_amount",1), ("depth_fade_switch",1),
        ("diffuse_1_brightness",1), ("diffuse_1_channels",4), ("diffuse_1_pan",2), ("diffuse_1_tile",2),
        ("diffuse_2_brightness",1), ("diffuse_2_channels",4), ("diffuse_2_pan",2), ("diffuse_2_tile",2),
        ("diffuse_3_brightness",1), ("diffuse_3_channels",4), ("diffuse_3_pan",2), ("diffuse_3_tile",2),
        ("distortion_colour_switch",1), ("glow_colour",3), ("glow_strength",1),
        ("mask_1_pan",2), ("mask_1_tile",2), ("mask_2_pan",2), ("mask_2_tile",2), ("mask_strength",1),
        ("normal_fade_in_out",1), ("normal_fade_power",1), ("texture_power",1), ("total_alpha_strength",1),
        ("vertex_alpha_contrast",1), ("vertex_colour",1), ("vfx_mesh_alpha",1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new List<float>(55);
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
        // Six public user texture slots suffice only when colour distortion is disabled.
        if (parameters[34] != 0) throw new VfxMappingException("distortion_colour_switch", "Vfx.Preview.MaterialUnsupported");
        if (parameters[5] < 0 || parameters[49] < 0 || parameters[50] < 0 || parameters[52] < 0)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        var paths = new[] { "t_xml_diffuse_1", "t_xml_diffuse_2", "t_xml_diffuse_3", "t_xml_colour_overlay", "t_xml_mask_1", "t_xml_mask_2" }.Select(slot =>
        {
            var nodes = root.Element("textures")?.Elements("texture").Where(n => string.Equals((string?)n.Element("slot"), slot, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace((string?)nodes[0].Element("source")))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, parameters.ToArray());
    }
}
