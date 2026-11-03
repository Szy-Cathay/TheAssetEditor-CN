using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxNativeSimpleMeshMaterial
{
    internal const int ParameterCount = 4;
    internal static bool Supports(string shader) => shader.Replace('\\', '/').ToLowerInvariant() is
        "shaders/rigid_flat_color_tinted.xml.shader" or "shaders/basic_additive.xml.shader";
    internal static bool IsOpaque(VfxScrollingMeshMaterial material) => material.Parameters.Length == ParameterCount && material.TexturePaths.Length == 0;

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var shader = root.Element("shader")?.Value.Replace('\\', '/').ToLowerInvariant();
        if (shader == "shaders/rigid_flat_color_tinted.xml.shader")
            return new([], new float[ParameterCount]);
        if (shader != "shaders/basic_additive.xml.shader")
            throw new VfxMappingException(shader ?? "material", "Vfx.Preview.MaterialUnsupported");
        return new(VfxMaterialXml.Textures(root, "base_glow_texture", "scrolling_detail"),
            VfxMaterialXml.Parameters(root, [("glow_colour",3), ("glow_strength",1)]));
    }
}
