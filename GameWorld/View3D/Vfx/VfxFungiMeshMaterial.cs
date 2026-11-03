using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxFungiMeshMaterial
{
    internal const int ParameterCount = 80;
    internal const string Shader = "shaders/rigid_tinted_metal_emissive_vfx_alpha_mushroom.xml.shader";
    // Native AUTO_PARAMETERS is 116 bytes. Padding belongs only to the preview transport.
    internal static readonly (string Name, int Components)[] Fields =
    [
        ("emissive_distort_strength",1), ("emissive_pulse_speed",1), ("emissive_pulse_strength",1),
        ("emissive_speed",1), ("emissive_strength",1), ("emissive_tiling",2), ("emissive_tint",3),
        ("fade_in_out",1), ("fade_normal_power",1), ("metal_tint_variation",1), ("time_source",1),
        ("vertex_anim_speed",1), ("vertex_disp_scale",1), ("vertex_disp_scroll",2), ("vertex_disp_tiling",2),
        ("vertex_displacement",1), ("vertex_offset_amount",1), ("vertex_oscilation_scale",1),
        ("vertex_x_amp",1), ("vertex_x_freq",1), ("vertex_y_amp",1), ("vertex_y_freq",1),
        ("vertex_z_amp",1), ("vertex_z_freq",1),
    ];
    internal static readonly string[] Slots =
    [
        "base_colour", "dither", "emissive_direction", "emissive_distortion", "emissive_texture", "mask1",
        "material_map", "normal", "vertex_disp_texture",
    ];

    internal static bool Supports(string shader) => string.Equals(shader.Replace('\\', '/'), Shader, StringComparison.OrdinalIgnoreCase);

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var shader = root.Element("shader")?.Value ?? "";
        if (!Supports(shader)) throw new VfxMappingException(shader, "Vfx.Preview.MaterialUnsupported");
        var raw = VfxMaterialXml.Parameters(root, Fields);
        if (raw.Length != 29) throw new InvalidOperationException("Invalid native fungi parameter layout.");
        if (raw[11] < 0) throw new VfxMappingException("fade_normal_power", "Vfx.Preview.RangeUnsupported");
        var parameters = new float[ParameterCount];
        raw.CopyTo(parameters, 0);
        return new(VfxMaterialXml.Textures(root, Slots), parameters);
    }
}
