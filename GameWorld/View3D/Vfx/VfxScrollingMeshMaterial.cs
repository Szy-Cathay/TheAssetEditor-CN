using System.IO;
using System.Xml;
using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

// The parameter order matches wh_vfx_mesh's shipped 196-byte AUTO_PARAMETERS layout.
internal sealed record VfxScrollingMeshMaterial(string[] TexturePaths, float[] Parameters)
{
    public static bool Supports(string shader) => shader.Replace('\\', '/').ToLowerInvariant()
        is "shaders/wh_vfx_mesh.xml.shader" or "shaders/wh2_vfx_mesh_colour.xml.shader" or "shaders/wh3_vfx_one_shot_mesh.xml.shader"
            or "shaders/wh3_vfx_basic_mesh.xml.shader" or "shaders/vfx_gemstone_crystal.xml.shader" or "shaders/fx_premultiply_animated_uvs.xml.shader" or "shaders/vfx_mesh_solid.xml.shader"
            or "shaders/vfx_soulpanning.xml.shader" or "shaders/vfx_soulpanning_w2.xml.shader" or "shaders/vfx_soulpanning_w4.xml.shader"
            or "shaders/vfx_growing_vines.xml.shader" or "shaders/wh3_vfx_rigid_emissive_dissolve_one_shot.xml.shader"
            or "shaders/vfx_rigid_basic_dither.xml.shader"
            or "shaders/wh2_vfx_cst_tidecall.xml.shader" or "shaders/wh_vfx_mesh_vertex_anim_battle.xml.shader";
    public bool UsesVertexColour => Parameters.Length is VfxTidecallMaterial.ParameterCount or VfxVertexAnimatedMeshMaterial.ParameterCount
        || Parameters.Length is not (8 or 12 or 28 or 32 or 40 or 60) && Parameters[Parameters.Length == 56 ? 48 : Parameters.Length == 55 ? 53 : Parameters.Length == 54 ? 52 : 47] != 0;
    internal static readonly (string Name, int Components)[] Fields =
    [
        ("Texture_A_channels", 4), ("Texture_B_Channels", 4), ("Texture_C_Channels", 4),
        ("colour_distortion_switch", 1), ("colour_overlay_pan", 2), ("colour_overlay_strength", 1),
        ("colour_overlay_tile", 2), ("depth_fade_switch", 1), ("global_mask_pan", 2), ("global_mask_tiling", 2),
        ("glow_colour", 3), ("glow_strength", 1), ("normal_fade_in_out", 1), ("normal_fade_power", 1),
        ("pixel_depth_fade", 1), ("scrolling_brightness", 1), ("scrolling_brightness2", 1), ("scrolling_brightness3", 1),
        ("scrolling_colour_scroll", 2), ("scrolling_colour_scroll2", 2), ("scrolling_colour_scroll3", 2),
        ("scrolling_colour_tiling", 2), ("scrolling_colour_tiling2", 2), ("scrolling_colour_tiling3", 2),
        ("total_alpha_strength", 1), ("vertex_alpha_contrast", 1), ("vertex_colour", 1), ("vfx_mesh_alpha", 1),
    ];

    public static VfxScrollingMeshMaterial Read(byte[] bytes, Func<string, byte[]>? resolve = null)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XElement.Load(reader);
        var shader = ((string?)root.Element("shader"))?.Replace('\\', '/').ToLowerInvariant() ?? "";
        if (shader == "shaders/vfx_rigid_basic_dither.xml.shader") return VfxRigidBasicDitherMaterial.Read(root);
        if (shader == "shaders/wh2_vfx_cst_tidecall.xml.shader") return VfxTidecallMaterial.Read(root);
        if (shader == "shaders/wh_vfx_mesh_vertex_anim_battle.xml.shader") return VfxVertexAnimatedMeshMaterial.Read(root);
        if (shader == "shaders/wh3_vfx_rigid_emissive_dissolve_one_shot.xml.shader") return VfxRigidDissolveMaterial.Read(root);
        if (shader == "shaders/vfx_growing_vines.xml.shader") return VfxGrowingVinesMaterial.Read(root);
        if (shader == "shaders/vfx_mesh_solid.xml.shader") return VfxSolidMeshMaterial.Read(root, resolve);
        if (shader is "shaders/vfx_soulpanning.xml.shader" or "shaders/vfx_soulpanning_w2.xml.shader" or "shaders/vfx_soulpanning_w4.xml.shader")
            return VfxSoulPanningMaterial.Read(root);
        var colourMesh = shader == "shaders/wh2_vfx_mesh_colour.xml.shader";
        if (!Supports(shader))
            throw new VfxMappingException("material", "Vfx.Preview.MaterialUnsupported");
        if (shader == "shaders/wh3_vfx_one_shot_mesh.xml.shader") return VfxOneShotMeshMaterial.Read(root);
        if (shader == "shaders/wh3_vfx_basic_mesh.xml.shader") return VfxBasicMeshMaterial.Read(root);
        if (shader == "shaders/vfx_gemstone_crystal.xml.shader") return VfxGemstoneMaterial.Read(root);
        if (shader == "shaders/fx_premultiply_animated_uvs.xml.shader") return VfxPremultiplyAnimatedMaterial.Read(root);
        var textures = root.Element("textures")?.Elements("texture").ToArray() ?? [];
        var paths = new[] { "t_xml_Texture_A", "t_xml_Texture_B", "t_xml_Texture_C", "t_xml_colour_overlay", "t_xml_distortion_texture", "t_xml_global_mask" }
            .Select(slot =>
            {
                // The colour variant has no distortion input; reuse an existing binding for the unused slot.
                if (colourMesh && slot == "t_xml_distortion_texture") slot = "t_xml_Texture_A";
                var candidates = textures.Where(t => string.Equals((string?)t.Element("slot"), slot, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length != 1 || string.IsNullOrWhiteSpace((string?)candidates[0].Element("source")))
                    throw new VfxMappingException(slot, "Vfx.Preview.MissingResource");
                return candidates[0].Element("source")!.Value;
            }).ToArray();
        var parameters = new List<float>(49);
        foreach (var (name, components) in Fields)
        {
            // The shipped colour variant omits distortion and squares vertex alpha directly.
            if (colourMesh && name is "colour_distortion_switch" or "vertex_alpha_contrast")
            {
                parameters.Add(name == "vertex_alpha_contrast" ? 2 : 0);
                continue;
            }
            var candidates = root.Element("params")?.Elements("param").Where(p => string.Equals((string?)p.Element("name"), name, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (candidates.Length != 1) throw new VfxMappingException(name, "Vfx.Preview.AmbiguousField");
            var values = ((string?)candidates[0].Element("value") ?? "").Split(',');
            if (values.Length != components) throw new VfxMappingException(name, "Vfx.Preview.InvalidData");
            foreach (var value in values)
            {
                if (!VfxCurve.TryNumber(value, out var number) || Math.Abs(number) > 1000000)
                    throw new VfxMappingException(name, "Vfx.Preview.RangeUnsupported");
                parameters.Add((float)number);
            }
        }
        if (parameters[28] < 0 || parameters[29] < 0 || parameters[46] < 0)
            throw new VfxMappingException("material", "Vfx.Preview.RangeUnsupported");
        return new(paths, parameters.ToArray());
    }
}

internal sealed record VfxScrollingMeshBinding(int[] TextureIndexes, float[] Parameters);
