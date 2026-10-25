using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

internal static class VfxRigidBasicDitherMaterial
{
    internal const int ParameterCount = 8;
    private static readonly string[] Fields = ["dissolve_offset", "dissolve_scroll", "dissolve_tile"];
    private static readonly string[] Slots = ["base_colour", "dissolve", "material_map", "normal"];

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        // Six authored floats, followed by two padding floats for the native uniform layout.
        var parameters = new float[ParameterCount];
        for (var index = 0; index < Fields.Length; index++)
        {
            var field = Fields[index];
            var nodes = root.Elements("params").Elements("param").Where(node =>
                string.Equals(node.Element("name")?.Value, field, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1) throw new VfxMappingException(field, "Vfx.Preview.AmbiguousField");
            var values = (nodes[0].Element("value")?.Value ?? "").Split(',');
            if (values.Length != 2) throw new VfxMappingException(field, "Vfx.Preview.InvalidData");
            for (var axis = 0; axis < 2; axis++)
            {
                if (!VfxCurve.TryNumber(values[axis], out var value) || Math.Abs(value) > 1000000)
                    throw new VfxMappingException(field, "Vfx.Preview.RangeUnsupported");
                parameters[index * 2 + axis] = (float)value;
            }
        }
        var paths = Slots.Select(slot =>
        {
            var name = "t_xml_" + slot;
            var nodes = root.Elements("textures").Elements("texture").Where(node =>
                string.Equals(node.Element("slot")?.Value, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Element("source")?.Value))
                throw new VfxMappingException(name, "Vfx.Preview.MissingResource");
            return nodes[0].Element("source")!.Value;
        }).ToArray();
        return new(paths, parameters);
    }
}
