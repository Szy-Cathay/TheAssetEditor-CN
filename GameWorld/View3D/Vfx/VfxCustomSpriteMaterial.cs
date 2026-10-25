using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace GameWorld.Core.Vfx;

internal static class VfxCustomSpriteMaterial
{
    public static (VfxScrollingMeshMaterial Material, float SoftDistance, string Diagnostic) Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XElement.Load(reader);
        var shader = root.Element("shader")?.Value.Replace('\\', '/').ToLowerInvariant() ?? "";
        if (shader == "shaders/fx_animated_uvs_custom.xml.shader")
        {
            var material = VfxAnimatedUvMaterial.Read(root);
            return (material, material.Parameters[24], "Vfx.Preview.AnimatedUvMaterialCandidate");
        }
        if (shader == "shaders/wh3_vfx_custom_basic.xml.shader")
        {
            var material = VfxBasicSpriteMaterial.Read(root);
            return (material, material.Parameters[17], "Vfx.Preview.BasicSpriteMaterialCandidate");
        }
        if (shader == "shaders/wh3_vfx_custom_basic_pre_lit.xml.shader")
        {
            var material = VfxPrelitSpriteMaterial.Read(root);
            return (material, material.Parameters[20], "Vfx.Preview.PrelitSpriteMaterialCandidate");
        }
        throw new VfxMappingException(shader, "Vfx.Preview.MaterialUnsupported");
    }
}
