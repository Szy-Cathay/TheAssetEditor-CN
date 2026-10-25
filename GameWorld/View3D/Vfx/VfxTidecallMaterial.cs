using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxTidecallMaterial
{
    internal const int SourceParameterCount = 92, ParameterCount = 104;
    // The first 92 floats preserve the shipped 368-byte structured AUTO_PARAMETERS layout.
    internal static readonly (string Name, int Components)[] Fields =
    [
        ("depth_fade_switch", 1), ("disintegrate1_texture_channels", 4),
        ("disintegrate_1_direction", 2), ("disintegrate_1_tiling", 2),
        ("disintegrate_mask_direction", 2), ("disintegrate_mask_texture_channels", 4), ("disintegrate_mask_tiling", 2),
        ("displacement_pan", 2), ("displacement_scale", 1), ("displacement_tiling", 2),
        ("foam_a_brightness", 1), ("foam_a_pan", 2), ("foam_a_texture_channels", 4), ("foam_a_tiling", 2),
        ("foam_b_brightness", 1), ("foam_b_pan", 2), ("foam_b_texture_channels", 4), ("foam_b_tiling", 2),
        ("foam_colour", 4), ("foam_colour_scale", 1), ("foam_edge_noise_contrast", 1), ("foam_mask_tiling", 2),
        ("mesh_alpha", 1), ("noise_a_brightess", 1), ("noise_a_pan", 2), ("noise_a_texture_channels", 4), ("noise_a_tiling", 2),
        ("noise_b_brightness", 1), ("noise_b_pan", 2), ("noise_b_texture_channels", 4), ("noise_b_tiling", 2),
        ("normal_pan", 2), ("normal_tiling", 2), ("pixel_depth_fade", 1), ("sea_colour", 4),
        ("threshold_source", 1), ("threshold_value", 1), ("total_alpha_strength", 1),
        ("wave_mask_pan", 2), ("wave_mask_tiling", 2), ("wave_mask_uv_offset", 2),
        ("y_edge_mask_invert", 1), ("y_edge_mask_offset", 2), ("y_edge_mask_pan", 2), ("y_edge_mask_tiling", 2),
    ];
    internal static readonly string[] Slots =
    [
        "disintegrate_1_texture", "disintegrate_mask_texture", "displacement_texture", "dither_texture",
        "foam_a_texture", "foam_b_texture", "foam_mask", "noise_a_texture", "noise_b_texture", "normal",
        "wave_mask_texture", "y_edge_mask",
    ];

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new float[ParameterCount];
        var offset = 0;
        foreach (var (name, components) in Fields)
        {
            var matches = root.Elements("params").Elements("param").Where(node =>
                string.Equals(node.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var parts = (matches[0].Element("value")?.Value ?? "").Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (!VfxCurve.TryNumber(part, out var value) || Math.Abs(value) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                parameters[offset++] = (float)value;
            }
        }
        if (offset != SourceParameterCount) throw new InvalidOperationException("Invalid tidecall parameter layout");
        var paths = Slots.Select(slot =>
        {
            var name = "t_xml_" + slot;
            var matches = root.Elements("textures").Elements("texture").Where(node =>
                string.Equals(node.Element("slot")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Element("source")?.Value))
                throw new VfxMappingException(name, "Vfx.Preview.MissingResource");
            return matches[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, parameters);
    }
}
