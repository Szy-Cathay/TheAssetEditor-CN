using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxFogBloodMaterial
{
    internal const int FogParameterCount = 21;
    internal const int BloodParameterCount = 23;
    internal static bool IsFog(int count) => count == FogParameterCount;
    internal static bool IsBlood(int count) => count == BloodParameterCount;
    internal static bool IsFamily(int count) => IsFog(count) || IsBlood(count);
    internal static bool UsesVertexColour(float[] parameters) => IsFamily(parameters.Length);
    internal static bool Supports(string shader) => shader.Replace('\\', '/').ToLowerInvariant() is
        "shaders/vfx/vfx_particle_fog.xml.shader" or "shaders/vfx_blood_splash_1.xml.shader";

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var shader = root.Element("shader")?.Value.Replace('\\', '/').ToLowerInvariant();
        if (shader == "shaders/vfx/vfx_particle_fog.xml.shader")
        {
            var values = VfxMaterialXml.Parameters(root,
            [
                ("depth", 1), ("distortion_amount", 1), ("distortion_noise_amount", 1), ("distortion_scale", 2),
                ("invert_noise", 1), ("noise_amount", 1), ("noise_scale", 2), ("panning_direction", 2),
                ("panning_direction_distortion", 2), ("panning_scale", 2),
                ("particle_vfx_func_depth_fade_scale_strength", 1), ("particle_vfx_func_depth_fade_soft_factor", 1),
                ("particle_vfx_func_lighting_average_size", 1), ("particle_vfx_func_lighting_directionality", 1),
                ("particle_vfx_func_lighting_large_particles", 1), ("particle_vfx_func_lighting_small_particles", 1),
            ]);
            if (values[15] < 0 || values[16] < 0 || values[17] < 0 || Math.Abs(values[18]) >= 1
                || values[19] < 0 || values[20] < 0 || values[19] + values[20] <= 0)
                throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
            return new(VfxMaterialXml.Textures(root, "distortion_texture", "noise_texture", "particle_texture"), values);
        }
        if (shader != "shaders/vfx_blood_splash_1.xml.shader")
            throw new VfxMappingException(shader ?? "material", "Vfx.Preview.MaterialUnsupported");
        var source = VfxMaterialXml.Parameters(root, [("Normal_strength", 1),
            ("particle_vfx_func_depth_fade_scale_strength", 1), ("particle_vfx_func_depth_fade_soft_factor", 1)]);
        if (source[0] == 0 || source[1] < 0 || source[2] < 0)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        var fog = root.Elements("params").Elements("param").Where(p =>
            string.Equals(p.Element("name")?.Value, "particle_vfx_func_fog_enable", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fog.Length != 1) throw new VfxMappingException("particle_vfx_func_fog_enable", "Vfx.Preview.AmbiguousField");
        var enabled = fog[0].Element("value")?.Value;
        if (enabled is not ("true" or "false" or "0" or "1"))
            throw new VfxMappingException("particle_vfx_func_fog_enable", "Vfx.Preview.InvalidData");
        // The native source contains four floats; zero padding identifies the family in the existing material ABI.
        var parameters = new float[BloodParameterCount];
        source.CopyTo(parameters, 0); parameters[3] = enabled is "true" or "1" ? 1 : 0;
        return new(VfxMaterialXml.Textures(root, "Diffuse", "Material", "Normal"), parameters);
    }
}
