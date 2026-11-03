using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxNativeRigidDissolveMaterial
{
    internal const int SourceParameterCount = 44;
    internal const int VariantOffset = 58;

    public static VfxScrollingMeshMaterial Read(XElement root)
    {
        // The native 176-byte record omits the one-shot dissolve offset, but keeps all ten bindings.
        var adapted = new XElement(root);
        var parameters = adapted.Element("params");
        if (parameters == null)
            throw new VfxMappingException("params", "Vfx.Preview.MissingField");
        if (parameters.Elements("param").Any(parameter => string.Equals(parameter.Element("name")?.Value,
            "disintegrate_1_offset", StringComparison.OrdinalIgnoreCase)))
            throw new VfxMappingException("disintegrate_1_offset", "Vfx.Preview.AmbiguousField");
        parameters.Add(new XElement("param", new XElement("name", "disintegrate_1_offset"),
            new XElement("type", "float2"), new XElement("value", "0,0")));
        var result = VfxRigidDissolveMaterial.Read(adapted);
        // This lane is outside both the original parameter record and the packed atlas rectangles.
        result.Parameters[VariantOffset] = 1;
        return result;
    }
}
