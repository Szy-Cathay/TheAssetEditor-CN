#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxDefaultTextureTests
{
    // Expected names/order come from the official52d36e..52d60d importer, not a basename heuristic.
    [TestCase("vfx/models/tex/test_gray.dds", true, "commontextures/default_grey_srgb.dds")]
    [TestCase("vfx/models/tex/test_gray.dds", false, "commontextures/default_grey.dds")]
    [TestCase(@"vfx\models\tex\test_black.dds", true, "commontextures/default_black_srgb.dds")]
    [TestCase("vfx/models/tex/test_black.dds", false, "commontextures/default_black.dds")]
    [TestCase("vfx/models/tex/test_mask.dds", true, "commontextures/default_black_srgb.dds")]
    [TestCase("vfx/models/tex/test_mask.dds", false, "commontextures/default_black.dds")]
    [TestCase("vfx/models/tex/test_white.dds", true, "commontextures/default_white_srgb.dds")]
    [TestCase("vfx/models/tex/test_white.dds", false, "commontextures/default_white.dds")]
    [TestCase("vfx/models/tex/test_gloss_map.dds", true, "commontextures/default_black_srgb.dds")]
    [TestCase("vfx/models/tex/test_gloss.dds", false, "commontextures/default_black.dds")]
    [TestCase("vfx/models/tex/flatnormal.dds", true, "commontextures/default_normal.dds")]
    [TestCase("vfx/models/tex/flatnormal.dds", false, "commontextures/default_normal.dds")]
    [TestCase("vfx/models/tex/test_material_map.dds", true, "commontextures/default_material_map.dds")]
    [TestCase("vfx/models/tex/test_material_map.dds", false, "commontextures/default_material_map.dds")]
    public void NativePlaceholderTable_UsesTheOriginalOrderedDefaultResource(string path, bool srgb, string expected)
        => Assert.That(VfxDefaultTexture.Resolve(path, srgb), Is.EqualTo(expected));

    [TestCase("vfx/test_white/test_gray_suffix.dds", "commontextures/default_grey_srgb.dds")]
    [TestCase("vfx/test_material_map/flatnormal", "commontextures/default_normal.dds")]
    [TestCase("vfx/test_grayish_other.dds", "commontextures/default_grey_srgb.dds")]
    [TestCase("vfx/test_gloss_map_other.tga", "commontextures/default_black_srgb.dds")]
    [TestCase("vfx/test_gray_folder/ordinary.dds", "commontextures/default_grey_srgb.dds")]
    [TestCase("/test_gray", "commontextures/default_grey_srgb.dds")]
    [TestCase("test_gray.dds", "test_gray.dds")]
    [TestCase("vfx/TEST_GRAY.dds", "vfx/TEST_GRAY.dds")]
    [TestCase("vfx/test_grey.dds", "vfx/test_grey.dds")]
    [TestCase("vfx/testgray.dds", "vfx/testgray.dds")]
    [TestCase("vfx/other_test_gray.dds", "vfx/other_test_gray.dds")]
    public void NativeSubstringSemantics_DoNotBecomeABasenameOrExtensionSearch(string path, string expected)
        => Assert.That(VfxDefaultTexture.Resolve(path, true), Is.EqualTo(expected));

    [TestCase("rigid/test_gray_diffuse.dds", "commontextures/default_grey_srgb.dds")]
    [TestCase("rigid/old_diffuse/test_white.dds", "commontextures/default_white_srgb.dds")]
    [TestCase("rigid/ordinary_diffuse.dds", "rigid/ordinary_base_colour.dds")]
    public void EmbeddedDiffuse_UpgradesItsNativeSlotBeforeApplyingPlaceholders(string path, string expected)
        => Assert.That(VfxLegacyModelColour.ResolveEmbeddedDiffuse(RmvVersionEnum.RMV2_V7, VertexFormat.Static,
            ModelMaterialEnum.default_type, path), Is.EqualTo(expected));

    [Test]
    public void UnverifiedEmbeddedScope_DoesNotAcquireAnUnprovedPlaceholderRewrite()
        => Assert.That(VfxLegacyModelColour.ResolveEmbeddedDiffuse(RmvVersionEnum.RMV2_V7, VertexFormat.Weighted,
            ModelMaterialEnum.default_type, "vfx/models/tex/test_gray.dds"), Is.EqualTo("vfx/models/tex/test_gray.dds"));

    [TestCase(VertexFormat.Weighted)]
    [TestCase(VertexFormat.Cinematic)]
    public void NativeWeightedImporter_UpgradesTheEmbeddedDiffuseBeforeResourceLookup(VertexFormat format)
        => Assert.That(VfxLegacyModelColour.ResolveEmbeddedDiffuse(RmvVersionEnum.RMV2_V7, format,
            ModelMaterialEnum.weighted, "vfx/models/tex/dwellers_below_hand_01_diffuse.dds"),
            Is.EqualTo("vfx/models/tex/dwellers_below_hand_01_base_colour.dds"));

    [TestCase(VertexFormat.Weighted)]
    [TestCase(VertexFormat.Cinematic)]
    public void NativeWeightedImporter_UsesTheSameOrderedPlaceholderTable(VertexFormat format)
        => Assert.That(VfxLegacyModelColour.ResolveEmbeddedDiffuse(RmvVersionEnum.RMV2_V7, format,
            ModelMaterialEnum.weighted, "vfx/models/tex/test_gray.dds"), Is.EqualTo("commontextures/default_grey_srgb.dds"));

    [TestCase("vfx/models/wh2_generic_cuboid.rigid_model_v2", "5ed55349815be07320267d9b5ba3bb82dff9b7bd52969df9f05e6a57ba1c3798")]
    [TestCase("vfx/models/1m_sphere.rigid_model_v2", "313ace384dbce031dffd6726205e0cbc8b6c29c1a7d8c1ddc83c9b3434a1cb18")]
    public void CompleteOfficialModels_ResolveTheirEmbeddedPlaceholderToTheExactNativeDefault(string modelPath, string expectedModelHash)
    {
        var (model, modelPack) = ReadOfficial("vfx_models.pack", modelPath);
        var (texture, texturePack) = ReadOfficial("commontextures.pack", "commontextures/default_grey_srgb.dds");
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(texture)), Is.EqualTo("b58a7f6972552bdd61f7286ed4e9aa175e21e71a4ea595404b4b6c4ab85a39da"));
        var originalHash = Convert.ToHexStringLower(SHA256.HashData(model));
        Assert.That(originalHash, Is.EqualTo(expectedModelHash));
        var file = ModelFactory.Create().Load(model);
        Assert.That(file.ModelList[0][0].Material.GetTexture(TextureType.Diffuse)!.Value.Path.Replace('\\', '/'), Is.EqualTo("vfx/models/tex/test_gray.dds"));
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        source.Root.Descendants("ModelFile").Single().SetAttributeValue("value", modelPath);
        var sourceBytes = source.Write();
        var requests = new List<string>();
        var conversion = TotalWarVfxConverter.Convert(new(sourceBytes, "DIAG/official-model-default-texture.xml", path =>
        {
            path = path.Replace('\\', '/'); requests.Add(path);
            return path == modelPath ? model : path == "commontextures/default_grey_srgb.dds" ? texture : null;
        }));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field + "/" + d.ReasonKey)));
            Assert.That(conversion.TexturePaths, Does.Contain("commontextures/default_grey_srgb.dds"));
            Assert.That(conversion.Textures.Any(bytes => bytes.AsSpan().SequenceEqual(texture)), Is.True);
            Assert.That(requests, Does.Not.Contain("vfx/models/tex/test_gray.dds"));
            Assert.That(requests, Does.Not.Contain("commontextures/test_gray.dds"));
            Assert.That(conversion.TextureUsages, Does.Contain(VfxTextureUsage.Diffuse));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(model)), Is.EqualTo(originalHash));
            Assert.That(VfxDocument.Read(sourceBytes).Write(), Is.EqualTo(sourceBytes));
        });
        TestContext.Out.WriteLine($"DIAG complete unmodified official model in isolated converter host: model={modelPath}; Pack={modelPack}; SHA256={originalHash}; default Pack={texturePack}; no Mod, generated texture or basename fallback. Complete original VFX roots are replayed separately.");
    }

    [Test]
    public void ExplicitWsModelColourOverride_KeepsItsAuthoredPlaceholderLikeName()
    {
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        source.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/explicit.wsmodel");
        var requests = new List<string>();
        var material = new XElement("material", new XElement("name", "diagnostic_override"), new XElement("shader", "shaders/rigid_character.xml.shader"),
            new XElement("textures", new XElement("texture", new XElement("slot", "t_xml_diffuse"), new XElement("source", "vfx/test_gray_override.dds"))));
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "DIAG/explicit-material-placeholder.xml", path =>
        {
            path = path.Replace('\\', '/'); requests.Add(path);
            return path switch
            {
                "vfx/explicit.wsmodel" => Encoding.UTF8.GetBytes("<model version=\"1\"><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/override.xml.material</material></materials></model>"),
                "vfx/override.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
                "vfx/model-0.rigid_model_v2" => VfxModelVariantTests.Model(0),
                "vfx/test_gray_override.dds" => [78, 6, 2],
                _ => null,
            };
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
        Assert.That(requests, Does.Contain("vfx/test_gray_override.dds"));
        Assert.That(requests.Any(path => path.StartsWith("commontextures/default_", StringComparison.Ordinal)), Is.False);
    }

    private static (byte[] Bytes, string Pack) ReadOfficial(string packName, string key)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("ASSETEDITOR_VFX_GAME_DATA must identify manifest-owned originals.");
        var manifest = Path.Combine(data!, "manifest.txt");
        if (!File.Exists(manifest)) Assert.Ignore("The official manifest is required; arbitrary Packs are not accepted.");
        var row = File.ReadLines(manifest).Select(line => line.Split('\t')).Single(line => line[0].Equals(packName, StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(data!, packName);
        Assert.That(new FileInfo(path).Length, Is.EqualTo(long.Parse(row[1], CultureInfo.InvariantCulture)));
        var pack = new PackFileContainerLoader(null!).Load(path)!;
        var file = pack.FileList.Single(pair => pair.Key.Replace('\\', '/').Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        return (file.DataSource.ReadData(), path);
    }
}
