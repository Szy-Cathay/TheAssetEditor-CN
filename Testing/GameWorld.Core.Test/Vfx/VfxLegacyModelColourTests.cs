#nullable enable
using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxLegacyModelColourTests
{
    private const string Diffuse = "rigidmodels/buildings/textures/diagnostic_rubble_diffuse.dds";
    private const string BaseColour = "rigidmodels/buildings/textures/diagnostic_rubble_base_colour.dds";

    [TestCase("rigidmodels/buildings/textures/diagnostic_rubble_diffuse.dds", "rigidmodels/buildings/textures/diagnostic_rubble_base_colour.dds")]
    [TestCase(@"rigidmodels\buildings\textures\diagnostic_rubble_diffuse.dds", @"rigidmodels\buildings\textures\diagnostic_rubble_base_colour.dds")]
    [TestCase("rigidmodels/diagnostic_base_colour.dds", "rigidmodels/diagnostic_base_colour.dds")]
    [TestCase("rigidmodels/diagnostic_DIFFUSE.dds", "rigidmodels/diagnostic_DIFFUSE.dds")]
    [TestCase("rigidmodels/diagnostic_diffuse.DDS", "rigidmodels/diagnostic_diffuse.DDS")]
    [TestCase("rigidmodels/diagnostic_diffuse_other.dds", "rigidmodels/diagnostic_diffuse_other.dds")]
    [TestCase("rigidmodels/diffuse/diagnostic.dds", "rigidmodels/diffuse/diagnostic.dds")]
    [TestCase("rigidmodels/old_diffuse/diagnostic_diffuse.dds", "rigidmodels/old_diffuse/diagnostic_diffuse.dds")]
    [TestCase("rigidmodels/diagnostic_diffuse_diffuse.dds", "rigidmodels/diagnostic_diffuse_diffuse.dds")]
    [TestCase("", "")]
    public void VerifiedEmbeddedSlot_OnlyRewritesAnUnambiguousOrdinalSuffix(string path, string expected)
    {
        Assert.That(VfxLegacyModelColour.ResolveEmbeddedDiffuse(RmvVersionEnum.RMV2_V7, VertexFormat.Static,
            ModelMaterialEnum.ship_ambientmap, path), Is.EqualTo(expected));
    }

    [TestCase(RmvVersionEnum.RMV2_V5, VertexFormat.Static, ModelMaterialEnum.ship_ambientmap)]
    [TestCase(RmvVersionEnum.RMV2_V6, VertexFormat.Static, ModelMaterialEnum.ship_ambientmap)]
    [TestCase(RmvVersionEnum.RMV2_V8, VertexFormat.Static, ModelMaterialEnum.ship_ambientmap)]
    [TestCase(RmvVersionEnum.RMV2_V7, VertexFormat.Weighted, ModelMaterialEnum.ship_ambientmap)]
    [TestCase(RmvVersionEnum.RMV2_V7, VertexFormat.Static, ModelMaterialEnum.default_type)]
    [TestCase(RmvVersionEnum.RMV2_V7, VertexFormat.Static, ModelMaterialEnum.dirtmap)]
    public void UnverifiedModelScopes_RetainTheirAuthoredColourPath(RmvVersionEnum version, VertexFormat format, ModelMaterialEnum material)
    {
        Assert.That(VfxLegacyModelColour.ResolveEmbeddedDiffuse(version, format, material, Diffuse), Is.EqualTo(Diffuse));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DirectLegacyModel_UsesTheNativeColourEvenWhenTheOldDiffuseExists(bool oldExists)
    {
        // DIAG: a minimal static mesh with the verified legacy header and distinct resource bytes.
        var source = VfxModelVariantTests.Source(1); var model = Model(); var original = model.ToArray();
        var requests = new List<string>(); byte[] colour = [27, 19, 8]; byte[] old = [0, 4, 2];
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/legacy-diagnostic.xml", path =>
        {
            path = path.Replace('\\', '/'); requests.Add(path);
            return path switch
            {
                "vfx/model-0.rigid_model_v2" => model,
                BaseColour => colour,
                Diffuse => oldExists ? old : null,
                _ when path.EndsWith(".dds") => [33],
                _ => null,
            };
        }));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), Reasons(conversion));
            Assert.That(conversion.Models, Has.Count.EqualTo(1));
            Assert.That(conversion.Textures.Any(bytes => bytes.AsSpan().SequenceEqual(colour)), Is.True);
            Assert.That(conversion.Textures.Any(bytes => bytes.AsSpan().SequenceEqual(old)), Is.False);
            Assert.That(requests, Does.Contain(BaseColour));
            Assert.That(requests, Does.Not.Contain(Diffuse), "This is an unconditional native slot upgrade, not a missing-file heuristic.");
            Assert.That(model, Is.EqualTo(original));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void MissingNativeColour_IsDiagnosedWithoutInventingAColourOrFallingBackToTheOldDiffuse()
    {
        var model = Model(); var requests = new List<string>();
        var conversion = TotalWarVfxConverter.Convert(new(VfxModelVariantTests.Source(1), "vfx/legacy-diagnostic.xml", path =>
        {
            path = path.Replace('\\', '/'); requests.Add(path);
            return path switch { "vfx/model-0.rigid_model_v2" => model, BaseColour => null, _ when path.EndsWith(".dds") => [0, 4, 2], _ => null };
        }));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
            Assert.That(requests, Does.Contain(BaseColour));
            Assert.That(requests, Does.Not.Contain(Diffuse));
        });
    }

    [TestCase("t_xml_diffuse")]
    [TestCase("t_xml_base_colour")]
    public void ExplicitWsModelMaterial_RetainsItsAuthoredOverride(string slot)
    {
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        source.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/diagnostic.wsmodel");
        var model = Model(); var requests = new List<string>(); byte[] explicitColour = [78, 6, 2];
        var material = new XElement("material", new XElement("name", "diagnostic_override"), new XElement("shader", "shaders/rigid_character.xml.shader"),
            new XElement("textures", new XElement("texture", new XElement("slot", slot), new XElement("source", "vfx/explicit_diffuse.dds"))));
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "vfx/legacy-diagnostic.xml", path =>
        {
            path = path.Replace('\\', '/'); requests.Add(path);
            return path switch
            {
                "vfx/diagnostic.wsmodel" => Encoding.UTF8.GetBytes("<model version=\"1\"><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/override.xml.material</material></materials></model>"),
                "vfx/override.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
                "vfx/model-0.rigid_model_v2" => model,
                "vfx/explicit_diffuse.dds" => explicitColour,
                _ => null,
            };
        }));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), Reasons(conversion));
            Assert.That(conversion.Textures.Any(bytes => bytes.AsSpan().SequenceEqual(explicitColour)), Is.True);
            Assert.That(requests, Does.Contain("vfx/explicit_diffuse.dds"));
            Assert.That(requests, Does.Not.Contain(BaseColour));
            Assert.That(requests, Does.Not.Contain("vfx/explicit_base_colour.dds"));
        });
    }

    [TestCase(RmvVersionEnum.RMV2_V8, ModelMaterialEnum.ship_ambientmap)]
    [TestCase(RmvVersionEnum.RMV2_V7, ModelMaterialEnum.default_type)]
    public void DirectUnverifiedModel_RetainsTheExistingDiffusePriority(RmvVersionEnum version, ModelMaterialEnum material)
    {
        var model = Model(version, material); var requests = new List<string>(); byte[] original = [0, 4, 2];
        var conversion = TotalWarVfxConverter.Convert(new(VfxModelVariantTests.Source(1), "vfx/legacy-diagnostic.xml", path =>
        {
            path = path.Replace('\\', '/'); requests.Add(path);
            return path switch { "vfx/model-0.rigid_model_v2" => model, Diffuse => original, _ => null };
        }));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), Reasons(conversion));
            Assert.That(conversion.Textures.Any(bytes => bytes.AsSpan().SequenceEqual(original)), Is.True);
            Assert.That(requests, Does.Contain(Diffuse));
            Assert.That(requests, Does.Not.Contain(BaseColour));
        });
    }

    private static byte[] Model(RmvVersionEnum version = RmvVersionEnum.RMV2_V7, ModelMaterialEnum material = ModelMaterialEnum.ship_ambientmap)
    {
        var file = ModelFactory.Create().Load(VfxModelVariantTests.Model(0));
        var header = file.Header; header.Version = version; file.Header = header;
        file.LodHeaders = [LodHeaderFactory.Create().CreateEmpty(version, 0, 0, 0)];
        file.ModelList[0][0].Material.SetTexture(TextureType.Diffuse, Diffuse);
        file.RecalculateOffsets();
        var bytes = ModelFactory.Create().Save(file);
        // DIAG: material 64 uses the same serialized static layout but is not offered by the writer's factory.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)file.LodHeaders[0].FirstMeshOffset, 2), (ushort)material);
        return bytes;
    }

    private static string Reasons(VfxConversion conversion) => string.Join(';', conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey));
}
