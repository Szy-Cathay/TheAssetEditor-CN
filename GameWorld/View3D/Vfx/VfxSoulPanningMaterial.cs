using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxSoulPanningMaterial
{
    // The source shader's structured buffer is 68 bytes; padding identifies this bridge layout.
    private static readonly (string Name, int Components)[] Fields =
    [
        ("depth_fade_distance", 1), ("emissive_strength", 1), ("fresnel_bias", 1), ("fresnel_power", 1),
        ("is_world_space", 1), ("uv_noise1panningdirection", 2), ("uv_noise1tiling", 2),
        ("uv_noise2panningdirection", 2), ("uv_noise2tiling", 2), ("uv_panningdirection", 2), ("uv_tiling", 2),
    ];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        var values = new List<float>(28);
        foreach (var (name, components) in Fields)
        {
            var nodes = root.Elements("params").Elements("param").Where(p =>
                string.Equals(p.Element("name")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var text = nodes[0].Element("value")?.Value ?? "";
            if (name == "is_world_space")
            {
                values.Add(text switch
                {
                    "false" or "0" => 0,
                    "true" or "1" => 1,
                    _ => throw new VfxMappingException(name, "Vfx.Preview.InvalidData"),
                });
                continue;
            }
            var parts = text.Split(',');
            if (parts.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var part in parts)
            {
                if (!VfxCurve.TryNumber(part, out var value) || Math.Abs(value) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                values.Add((float)value);
            }
        }
        if (values[0] < 0 || values[3] <= 0)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        var paths = new[] { "t_xml_base_colour", "t_xml_noise_tex_01", "t_xml_noise_tex_02", "t_xml_opacity_grad" }.Select(slot =>
        {
            var nodes = root.Elements("textures").Elements("texture").Where(t =>
                string.Equals(t.Element("slot")?.Value, slot, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        while (values.Count < 28) values.Add(0);
        return new(paths, values.ToArray());
    }
}
