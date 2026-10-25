using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxGrowingVinesMaterialTests
{
    [Test]
    public void GrowingModel_UsesItsMaterialAndDepthInsteadOfTheOrdinaryDiffuseFallback()
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/growing.wsmodel");
        var bytes = document.Write();
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/growing.xml", path => path switch
        {
            @"vfx\growing.wsmodel" => Encoding.UTF8.GetBytes("<model><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/growing.xml.material</material></materials></model>"),
            @"vfx\growing.xml.material" => Encoding.UTF8.GetBytes(Material().ToString()),
            _ => VfxModelVariantTests.Resolve(path),
        }));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.MeshMaterials, Has.Count.EqualTo(1));
            Assert.That(result.Models.Select(m => m.OpaqueMaterial), Has.All.True);
            Assert.That(result.Emitters.Select(e => e.Render.MeshMaterialIndex), Has.All.GreaterThanOrEqualTo(0));
            Assert.That(result.TexturePaths, Has.Count.EqualTo(5));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void Material_KeepsTheNativeParameterOrderAndAllTextureRoles()
    {
        var material = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(Material().ToString()));
        Assert.Multiple(() =>
        {
            Assert.That(material.Parameters, Is.EqualTo(new float[] { .4f, 1.25f, .2f, -.35f, -.2f, 1, 1, .1f, 2, 1, .4f, .05f }));
            Assert.That(material.TexturePaths, Is.EqualTo(new[] { "diffuse.dds", "emissive_panning_mask_r.dds", "leaves_gradient_map.dds", "material_map.dds", "normal.dds" }));
            Assert.That(material.UsesVertexColour, Is.False);
        });
    }

    [TestCase("tree_leaves_start_offset")]
    [TestCase("from_trunk_to_leaves")]
    [TestCase("wangle_leaves")]
    public void MissingLifecycleParameter_IsReportedInsteadOfFallingBackToPlainColour(string name)
    {
        var root = Material(); root.Descendants("param").Single(p => p.Element("name")!.Value == name).Remove();
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(error!.Field, Is.EqualTo(name));
    }

    [Test]
    public void OtherSpecialMaterials_NameTheirUnimplementedShaderInsteadOfImplyingMaterialSupport()
    {
        const string shader = "shaders/unimplemented_dissolve.xml.shader";
        var source = VfxOpaqueModelMaterialTests.Source(Microsoft.Xna.Framework.Vector3.One, 1);
        var result = TotalWarVfxConverter.Convert(new(source, "vfx/fallback.xml",
            path => VfxOpaqueModelMaterialTests.Resolve(path, [1], "unimplemented", shader)));
        Assert.That(result.Emitters, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics.Any(d => d.Field == shader && d.ReasonKey == "Vfx.Preview.ModelMaterialFallback"), Is.True);
    }

    internal static XElement Material()
    {
        (string Name, string Value)[] values =
        [
            ("base_colour_tint", ".4,1.25,.2"), ("emissive_panning_mask_direction", "-.35,-.2"),
            ("emissive_panning_mask_scale", "1,1"), ("emissiveness", ".1"), ("fresnel_power", "2"),
            ("from_trunk_to_leaves", "1"), ("tree_leaves_start_offset", ".4"), ("wangle_leaves", ".05"),
        ];
        return new XElement("material", new XElement("name", "growing"), new XElement("shader", "shaders/vfx_growing_vines.xml.shader"),
            new XElement("textures", new[] { "diffuse", "emissive_panning_mask_r", "leaves_gradient_map", "material_map", "normal" }
                .Select(name => new XElement("texture", new XElement("slot", "t_xml_" + name), new XElement("source", name + ".dds")))),
            new XElement("params", values.Select(p => new XElement("param", new XElement("name", p.Name),
                new XElement("type", p.Value.Contains(',') ? "float" + p.Value.Split(',').Length : "float"), new XElement("value", p.Value)))));
    }
}
