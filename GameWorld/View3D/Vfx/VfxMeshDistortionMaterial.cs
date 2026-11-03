using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxMeshDistortionMaterial
{
    internal const int ParameterCount = 15;
    internal static bool Supports(string shader) => string.Equals(shader.Replace('\\', '/'),
        "shaders/wh3_vfx_distortion_mesh_02.xml.shader", StringComparison.OrdinalIgnoreCase);

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var parameters = new float[ParameterCount];
        VfxMaterialXml.Parameters(root, [("alpha_value", 1), ("depth_fade", 1),
            ("distortion_of_distortion_direction", 2), ("distortion_of_distortion_scale", 2),
            ("distortion_tex_strength", 1), ("panning_tex_direction", 2), ("panning_texture_scale", 2)]).CopyTo(parameters, 0);
        if (parameters[1] <= 0) throw new VfxMappingException("depth_fade", "Vfx.Preview.RangeUnsupported");
        return new(VfxMaterialXml.Textures(root, "distortion_for_panning_tex", "panning_texture"), parameters);
    }
}
