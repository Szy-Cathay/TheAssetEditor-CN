using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxMaterialXml
{
    internal static float[] Parameters(XElement root, IEnumerable<(string Name, int Components)> fields)
    {
        var values = new List<float>();
        foreach (var (name, components) in fields)
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
        return values.ToArray();
    }

    internal static string[] Textures(XElement root, params string[] slots) => slots.Select(slot =>
    {
        var nodes = root.Elements("textures").Elements("texture").Where(node =>
            string.Equals(node.Element("slot")?.Value, "t_xml_" + slot, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
            throw new VfxMappingException("t_xml_" + slot, "Vfx.Preview.MissingResource");
        return nodes[0].Element("source")!.Value;
    }).ToArray();
}
