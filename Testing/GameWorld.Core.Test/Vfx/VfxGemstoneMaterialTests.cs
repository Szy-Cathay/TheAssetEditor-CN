#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxGemstoneMaterialTests
{
    [Test]
    public void OpaqueMaterial_KeepsTheNative160ByteLayoutAndSixNonDitherInputs()
    {
        var root = Material(); var bytes = Encoding.UTF8.GetBytes(root.ToString());
        var material = VfxScrollingMeshMaterial.Read(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(material.Parameters, Has.Length.EqualTo(40));
            Assert.That(material.Parameters[0], Is.EqualTo(1));
            Assert.That(material.Parameters[38], Is.EqualTo(.75f));
            Assert.That(material.Parameters[39], Is.EqualTo(.25f));
            Assert.That(material.TexturePaths, Is.EqualTo(new[] { "dissolve_detail.dds", "dissolve_map.dds", "refract_distortion_texture.dds", "refract_texture.dds", "surface_emissive.dds", "surface_normal.dds" }));
            Assert.That(material.UsesVertexColour, Is.False);
            Assert.That(Encoding.UTF8.GetBytes(root.ToString()), Is.EqualTo(bytes));
        });
    }

    [TestCase("0")]
    [TestCase("0.5")]
    [TestCase("1.1")]
    public void MaterialAlphaOtherThanOne_RequiresTheUnimplementedScreenDitherInput(string value)
    {
        var root = Material(); Set(root, "alpha", value);
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(error!.Field, Is.EqualTo("alpha"));
        Assert.That(error.Reason, Is.EqualTo("Vfx.Preview.MaterialUnsupported"));
    }

    [Test]
    public void MissingEmissiveTexture_IsNotReplacedByTheRefractTexture()
    {
        var root = Material(); root.Descendants("texture").Single(e => e.Element("slot")!.Value == "t_xml_surface_emissive").Remove();
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(error!.Field, Is.EqualTo("t_xml_surface_emissive"));
    }

    internal static XElement Material()
    {
        // Offsets follow the shipped gemstone shader's 160-byte AUTO_PARAMETERS buffer.
        (string Name, string Value)[] values =
        [
            ("alpha","1"), ("back_color","0.1,0.2,0.3"), ("dissolve_color_lerp_strength","2.5"),
            ("dissolve_end_color","0,1,0"), ("dissolve_glow_edge","0.15"), ("dissolve_glow_size","0.3"),
            ("dissolve_glow_strength","4"), ("dissolve_start_color","1,0,0"), ("dissolve_uv_scale","1,1"),
            ("fresnel_color","0,0,0"), ("fresnel_glow_strength","0"), ("fresnel_power_bias","3,0"),
            ("refract_color","0,0,0"), ("refract_distortion_amount","0.3"), ("refract_uv_offset","0,0"),
            ("refract_uv_tiling","1,1"), ("surface_emissive_color","0,0,0"), ("surface_emissive_strength","1"),
            ("surface_emissive_uv_offset","0,0"), ("surface_emissive_uv_tiling","1,1"),
            ("surface_metalness","0.75"), ("surface_roughness","0.25"),
        ];
        return new XElement("material", new XElement("shader", "shaders/vfx_gemstone_crystal.xml.shader"),
            new XElement("textures", new[] { "dissolve_detail", "dissolve_map", "dither", "refract_distortion_texture", "refract_texture", "surface_emissive", "surface_normal" }
                .Select(name => new XElement("texture", new XElement("slot", "t_xml_" + name), new XElement("source", name + ".dds")))),
            new XElement("params", values.Select(p => new XElement("param", new XElement("name", p.Name), new XElement("value", p.Value)))));
    }

    internal static void Set(XElement root, string name, string value) => root.Descendants("param").Single(p => p.Element("name")!.Value == name).Element("value")!.Value = value;
}
