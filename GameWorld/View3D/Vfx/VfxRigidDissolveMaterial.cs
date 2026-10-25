using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxRigidDissolveMaterial
{
    // The first 46 floats match the shipped 184-byte AUTO_PARAMETERS buffer.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("diffuse_tint", 3), ("disintegrate_1_direction", 2), ("disintegrate_1_offset", 2), ("disintegrate_1_tiling", 2),
        ("disintegrate_mask_direction", 2), ("disintegrate_mask_tiling", 2),
        ("edge_emissive_colour", 3), ("edge_emissive_intensity", 1),
        ("emissive_a_brightness", 1), ("emissive_a_channels", 4), ("emissive_a_direction", 2), ("emissive_a_tiling", 2),
        ("emissive_b_brightness", 1), ("emissive_b_channels", 4), ("emissive_b_direction", 2), ("emissive_b_tiling", 2),
        ("emissive_intensity", 1), ("emissive_tint", 3), ("global_mask_pan", 2), ("global_mask_tiling", 2),
        ("manual_dither_wangle", 1), ("threshold_source", 1), ("threshold_value", 1),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new float[60];
        var index = 0;
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
                parameters[index++] = (float)value;
            }
        }
        var paths = new List<string>();
        var logicalPaths = new List<string>();
        var aliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var slots = new[] { "base_colour", "disintegrate_1_texture", "disintegrate_mask_texture", "dither", "emissive_a", "emissive_b", "emissive_mask", "global_mask", "material_map", "normal" };
        for (var slotIndex = 0; slotIndex < slots.Length; ++slotIndex)
        {
            var slot = "t_xml_" + slots[slotIndex];
            var nodes = root.Elements("textures").Elements("texture").Where(t =>
                string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            var path = nodes[0].Element("source")!.Value;
            logicalPaths.Add(path);
            // Colour and numeric views of one DDS must remain separate resources.
            var key = (slotIndex == 0 ? "colour:" : "value:") + path.Replace('\\', '/');
            if (!aliases.TryGetValue(key, out var alias))
            {
                alias = paths.Count;
                aliases.Add(key, alias);
                paths.Add(path);
            }
            parameters[48 + slotIndex] = alias;
        }
        if (paths.Count > 6)
        {
            // Pack the dissolve, noise and first emission masks after decoding. Keep all source bindings.
            parameters[59] = 1;
            return new(logicalPaths.ToArray(), parameters);
        }
        while (paths.Count < 6) paths.Add(paths[^1]);
        return new(paths.ToArray(), parameters);
    }
}
