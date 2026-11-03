using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxNativeColourMeshMaterial
{
    internal const int ParameterCount = 72, ModeIndex = 68;
    internal const int Bubble = 0, Lightning = 1, Tornado = 2, DistortedColour = 3;
    internal static readonly (string Name, int Components)[] BubbleFields =
    [
        ("Texture_A_channels",4), ("Texture_B_Channels",4), ("colour_overlay_pan",2),
        ("colour_overlay_strength",1), ("colour_overlay_tile",2), ("depth_fade_switch",1),
        ("global_mask_pan",2), ("global_mask_tiling",2), ("glow_colour",3), ("glow_strength",1),
        ("normal_fade_in_out",1), ("normal_fade_power",1), ("pixel_depth_fade",1),
        ("scrolling_brightness",1), ("scrolling_brightness2",1),
        ("scrolling_colour_scroll",2), ("scrolling_colour_scroll2",2),
        ("scrolling_colour_tiling",2), ("scrolling_colour_tiling2",2),
        ("total_alpha_strength",1), ("vfx_mesh_alpha",1),
    ];
    internal static readonly (string Name, int Components)[] ThreeMaskFields =
    [
        ("Texture_A_channels",4), ("Texture_B_Channels",4), ("Texture_C_Channels",4),
        ("colour_overlay_pan",2), ("colour_overlay_strength",1), ("colour_overlay_tile",2),
        ("depth_fade_switch",1), ("global_mask_pan",2), ("global_mask_tiling",2),
        ("glow_colour",3), ("glow_strength",1), ("normal_fade_in_out",1), ("normal_fade_power",1),
        ("pixel_depth_fade",1), ("scrolling_brightness",1), ("scrolling_brightness2",1), ("scrolling_brightness3",1),
        ("scrolling_colour_scroll",2), ("scrolling_colour_scroll2",2), ("scrolling_colour_scroll3",2),
        ("scrolling_colour_tiling",2), ("scrolling_colour_tiling2",2), ("scrolling_colour_tiling3",2),
        ("total_alpha_strength",1),
    ];

    internal static readonly (string Name, int Components)[] DistortedFields =
    [
        ("Texture_A_channels",4), ("Texture_B_Channels",4), ("Texture_C_Channels",4),
        ("colour_overlay_pan",2), ("colour_overlay_strength",1), ("colour_overlay_tile",2),
        ("depth_fade_switch",1), ("diffuse_distortion_scale",2), ("diffuse_distortion_speed",2),
        ("distortion_brightness",1), ("global_mask_pan",2), ("global_mask_tiling",2), ("global_mask_time",1),
        ("glow_colour",3), ("glow_strength",1), ("mix_colour",3),
        ("normal_fade_in_out",1), ("normal_fade_power",1), ("pixel_depth_fade",1),
        ("scrolling_brightness",1), ("scrolling_brightness2",1), ("scrolling_brightness3",1),
        ("scrolling_colour_scroll",2), ("scrolling_colour_scroll2",2), ("scrolling_colour_scroll3",2),
        ("scrolling_colour_tiling",2), ("scrolling_colour_tiling2",2), ("scrolling_colour_tiling3",2),
        ("total_alpha_strength",1), ("vertex_alpha_contrast",1), ("vertex_colour",1), ("vfx_mesh_alpha",1),
    ];

    internal static bool Supports(string shader) => shader.Replace('\\', '/').ToLowerInvariant() is
        "shaders/wh2_vfx_mesh_bubble.xml.shader" or "shaders/wh2_vfx_mesh_lightning.xml.shader" or "shaders/wh_vfx_mesh_tornado.xml.shader" or "shaders/wh2_vfx_mesh_distortion.xml.shader";

    internal static bool UsesRawParticleAlpha(float[] parameters) => parameters.Length is ParameterCount or VfxVertexAnimatedMeshMaterial.ParameterCount or VfxNativeSimpleMeshMaterial.ParameterCount or VfxRigidGlowMaterial.ParameterCount or VfxFungiMeshMaterial.ParameterCount or VfxMeshDistortionMaterial.ParameterCount or 54 or 55;

    internal static bool UsesVertexColour(float[] parameters) => parameters[ModeIndex] switch
    {
        Lightning => parameters[45] != 0,
        Tornado => parameters[46] != 0,
        DistortedColour => parameters[55] != 0,
        _ => false,
    };

    internal static VfxScrollingMeshMaterial Read(XElement root)
    {
        var shader = root.Element("shader")?.Value.Replace('\\', '/').ToLowerInvariant();
        var mode = shader switch
        {
            "shaders/wh2_vfx_mesh_bubble.xml.shader" => Bubble,
            "shaders/wh2_vfx_mesh_lightning.xml.shader" => Lightning,
            "shaders/wh_vfx_mesh_tornado.xml.shader" => Tornado,
            "shaders/wh2_vfx_mesh_distortion.xml.shader" => DistortedColour,
            _ => throw new VfxMappingException(shader ?? "material", "Vfx.Preview.MaterialUnsupported"),
        };
        var fields = mode == DistortedColour ? DistortedFields : mode == Bubble ? BubbleFields : ThreeMaskFields.Concat(mode == Tornado
            ? new[] { ("vertex_alpha_contrast",1), ("vertex_colour",1), ("vfx_mesh_alpha",1) }
            : new[] { ("vertex_colour",1), ("vfx_mesh_alpha",1) }).ToArray();
        var raw = VfxMaterialXml.Parameters(root, fields);
        var values = new float[ParameterCount]; raw.CopyTo(values, 0); values[ModeIndex] = mode;
        var power = mode == DistortedColour ? 36 : mode == Bubble ? 23 : 27;
        var depth = mode == DistortedColour ? 37 : mode == Bubble ? 24 : 28;
        if (values[power] < 0 || values[depth] < 0 || mode == Tornado && values[45] < 0 || mode == DistortedColour && values[54] < 0)
            throw new VfxMappingException("normal_fade_power/pixel_depth_fade/vertex_alpha_contrast", "Vfx.Preview.RangeUnsupported");
        var paths = mode == DistortedColour ? VfxMaterialXml.Textures(root, "Texture_A", "Texture_B", "Texture_C", "colour_overlay", "distortion", "global_mask")
            : mode == Bubble ? VfxMaterialXml.Textures(root, "Texture_A", "Texture_B", "colour_overlay", "global_mask")
            : VfxMaterialXml.Textures(root, "Texture_A", "Texture_B", "Texture_C", "colour_overlay", "global_mask");
        return new(paths, values);
    }
}
