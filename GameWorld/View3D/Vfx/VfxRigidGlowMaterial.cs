using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxRigidGlowMaterial
{
    internal const int ParameterCount = 16;
    internal static bool Supports(string shader) => string.Equals(shader.Replace('\\', '/'),
        "shaders/rigid_glow_vertexalpha.xml.shader", StringComparison.OrdinalIgnoreCase);

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new float[ParameterCount];
        VfxMaterialXml.Parameters(root, [("colour", 3), ("emissive_strength", 1)]).CopyTo(parameters, 0);
        return new(VfxMaterialXml.Textures(root, "dither", "ba602a21_c5ea_445c_bed4_92f79937d1f6", "22978e9e_9de2_416d_a786_7d34d7d64e47"), parameters);
    }
}
