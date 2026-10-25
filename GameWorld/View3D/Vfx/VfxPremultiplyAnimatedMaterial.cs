using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxPremultiplyAnimatedMaterial
{
    // Matches the shipped 116-byte AUTO_PARAMETERS buffer; the last three floats are padding.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("Emission", 1), ("depth_fade", 1), ("diffuse_direction_01", 2), ("diffuse_direction_02", 2),
        ("diffuse_power", 1), ("diffuse_scale_01", 2), ("diffuse_scale_02", 2), ("distortion_strenght_01", 1),
        ("fog_strenght", 1), ("main_colour", 3), ("mask_direction_01", 2), ("mask_direction_02", 2),
        ("mask_scale_01", 2), ("mask_scale_02", 2), ("mask_strenght", 1), ("metallic", 1),
        ("opacity_power", 1), ("opacity_strenght", 1), ("roughness", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var textures = root.Elements("textures").Elements("texture").ToArray();
        var paths = new[] { "t_xml_diffuse_01", "t_xml_diffuse_02", "t_xml_distortion_01", "t_xml_global_opacity", "t_xml_mask_01", "t_xml_mask_02" }
            .Select(slot =>
            {
                var matches = textures.Where(t => string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Element("source")?.Value))
                    throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
                return matches[0].Element("source")!.Value;
            }).ToArray();
        var values = new List<float>(32);
        foreach (var (name, components) in Fields)
        {
            var matches = root.Elements("params").Elements("param").Where(p => string.Equals(p.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var parts = (matches[0].Element("value")?.Value ?? "").Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (!VfxCurve.TryNumber(part, out var number) || Math.Abs(number) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                values.Add((float)number);
            }
        }
        if (values[25] != 0) throw new VfxMappingException("metallic", "Vfx.Preview.MaterialUnsupported");
        if (values[0] < 0 || values[1] < 0 || values[6] is <= 0 or > 16 || values[24] < 0
            || values[26] is < 0 or > 16 || values[27] < 0 || values[28] is < 0 or > 1)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        while (values.Count < 32) values.Add(0);
        return new(paths, values.ToArray());
    }
}
