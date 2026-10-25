using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxGrowingVinesMaterial
{
    // Matches the shipped shader's 48-byte AUTO_PARAMETERS buffer.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("base_colour_tint", 3), ("emissive_panning_mask_direction", 2), ("emissive_panning_mask_scale", 2),
        ("emissiveness", 1), ("fresnel_power", 1), ("from_trunk_to_leaves", 1),
        ("tree_leaves_start_offset", 1), ("wangle_leaves", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var values = new List<float>(12);
        foreach (var (name, components) in Fields)
        {
            var nodes = root.Elements("params").Elements("param").Where(p =>
                string.Equals(p.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var parts = (nodes[0].Element("value")?.Value ?? "").Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (!VfxCurve.TryNumber(part, out var value) || Math.Abs(value) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                values.Add((float)value);
            }
        }
        if (values[8] < 0) throw new VfxMappingException("fresnel_power", "Vfx.Preview.RangeUnsupported");
        var paths = new[] { "diffuse", "emissive_panning_mask_r", "leaves_gradient_map", "material_map", "normal" }.Select(name =>
        {
            var slot = "t_xml_" + name;
            var nodes = root.Elements("textures").Elements("texture").Where(t =>
                string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, values.ToArray());
    }
}
