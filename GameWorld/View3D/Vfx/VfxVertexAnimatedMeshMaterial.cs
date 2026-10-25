using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxVertexAnimatedMeshMaterial
{
    internal const int ParameterCount = 64;
    // Preserve the shipped 256-byte AUTO_PARAMETERS order, including unused Y/Z controls.
    internal static readonly (string Name, int Components)[] Fields =
    [
        ("Texture_A_channels", 4), ("Texture_B_Channels", 4), ("Texture_C_Channels", 4),
        ("colour_overlay_pan", 2), ("colour_overlay_strength", 1), ("colour_overlay_tile", 2),
        ("depth_fade_switch", 1), ("fog", 1), ("global_mask_pan", 2), ("global_mask_tiling", 2),
        ("glow_colour", 3), ("glow_strength", 1), ("normal_fade_in_out", 1), ("normal_fade_power", 1),
        ("pixel_depth_fade", 1), ("scrolling_brightness", 1), ("scrolling_brightness2", 1), ("scrolling_brightness3", 1),
        ("scrolling_colour_scroll", 2), ("scrolling_colour_scroll2", 2), ("scrolling_colour_scroll3", 2),
        ("scrolling_colour_tiling", 2), ("scrolling_colour_tiling2", 2), ("scrolling_colour_tiling3", 2),
        ("time_source", 1), ("total_alpha_strength", 1), ("vertex_anim", 1), ("vertex_anim_speed", 1), ("vertex_colour", 1),
        ("vertex_disp_scale", 1), ("vertex_disp_scroll", 2), ("vertex_disp_tiling", 2), ("vertex_displacement", 1),
        ("vertex_oscilation_scale", 1), ("vertex_x_amp", 1), ("vertex_x_freq", 1),
        ("vertex_y_amp", 1), ("vertex_y_freq", 1), ("vertex_z_amp", 1), ("vertex_z_freq", 1), ("vfx_mesh_alpha", 1),
    ];
    internal static readonly string[] Slots =
        ["Texture_A", "Texture_B", "Texture_C", "colour_overlay", "global_mask", "vertex_disp_texture"];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var values = new List<float>(ParameterCount);
        foreach (var (name, components) in Fields)
        {
            var nodes = root.Elements("params").Elements("param").Where(node =>
                string.Equals(node.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
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
        if (values.Count != ParameterCount) throw new InvalidOperationException("Invalid vertex-animation parameter layout");
        if (values[18] is < 0 or > 1) throw new VfxMappingException("fog", "Vfx.Preview.RangeUnsupported");
        if (values[28] < 0 || values[29] < 0) throw new VfxMappingException("normal_fade_power/pixel_depth_fade", "Vfx.Preview.RangeUnsupported");
        var paths = Slots.Select(slot =>
        {
            var name = "t_xml_" + slot;
            var nodes = root.Elements("textures").Elements("texture").Where(node =>
                string.Equals(node.Element("slot")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                throw new VfxMappingException(name, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, values.ToArray());
    }
}
