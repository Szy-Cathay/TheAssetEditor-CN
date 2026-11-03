#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxLegacyRigidDissolveMaterialTests
{
    [Test]
    public void LegacyRecord_UsesTheNative208ByteOffsetsWithoutAddingOneShotParameters()
    {
        var material = Material();
        var index = 0;
        foreach (var field in material.Elements("params").Elements("param"))
        {
            var components = field.Element("value")!.Value.Split(',').Length;
            field.Element("value")!.Value = string.Join(',', Enumerable.Range(index, components).Select(x =>
                (x + .25f).ToString(CultureInfo.InvariantCulture)));
            index += components;
        }
        var original = material.ToString();
        var result = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(original));
        Assert.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(52));
            Assert.That(result.Parameters, Has.Length.EqualTo(68));
            Assert.That(result.Parameters.Take(52), Is.EqualTo(Enumerable.Range(0, 52).Select(x => x + .25f)));
            Assert.That(result.Parameters[15], Is.EqualTo(15.25f), "Native emissive_a_brightness offset60.");
            Assert.That(result.Parameters[33], Is.EqualTo(33.25f), "Native emissive_c_brightness offset132.");
            Assert.That(result.Parameters[42], Is.EqualTo(42.25f), "Native emissive_tint offset168.");
            Assert.That(result.Parameters[50], Is.EqualTo(50.25f), "Native threshold_source offset200.");
            Assert.That(result.Parameters[66], Is.EqualTo(1));
            Assert.That(result.Parameters[67], Is.Zero);
            Assert.That(result.UsesVertexColour, Is.False, "The original pixel shader ignores particle RGB.");
            Assert.That(material.ToString(), Is.EqualTo(original));
        });
    }

    [Test]
    public void LegacyThirdEmissionBinding_RemainsIndependentWhenMaskPackingIsRequired()
    {
        var material = Material();
        var index = 0;
        foreach (var source in material.Descendants("source")) source.Value = $"unique-{index++}.dds";
        var before = material.ToString();
        var parsed = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(before));
        Assert.Multiple(() =>
        {
            Assert.That(parsed.TexturePaths, Is.EqualTo(Enumerable.Range(0, 10).Select(x => $"unique-{x}.dds")));
            Assert.That(parsed.Parameters[56 + 6], Is.EqualTo(6));
            Assert.That(parsed.Parameters[67], Is.EqualTo(1));
            Assert.That(material.ToString(), Is.EqualTo(before));
        });
    }

    [Test]
    public void OriginalLegacyShader_ConvertsAsOpaqueDissolveWithoutAMaterialFallback()
    {
        var material = Material();
        var conversion = TotalWarVfxConverter.Convert(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/legacy-dissolve-diag.xml",
            path => path.Replace('\\', '/') == "vfx/character.xml.material" ? Encoding.UTF8.GetBytes(material.ToString())
                : VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White))));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(conversion.Models.Single().OpaqueMaterial, Is.True);
            Assert.That(conversion.MeshMaterials.Single().Parameters, Has.Length.EqualTo(68));
            Assert.That(conversion.TextureUsages[0], Is.EqualTo(VfxTextureUsage.OpaqueDiffuse));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.ModelMaterialFallback"), Is.False);
        });
    }

    internal static XElement Material()
    {
        // DIAG: the native legacy record, with independent A/B/C emission channels.
        var fields = new (string Name, string Value)[]
        {
            ("diffuse_tint", "0,0,0"), ("disintegrate_1_direction", "0,0"), ("disintegrate_1_tiling", "1,1"),
            ("disintegrate_mask_direction", "0,0"), ("disintegrate_mask_tiling", "1,1"),
            ("edge_emissive_colour", "0,0,0"), ("edge_emissive_intensity", "0"),
            ("emissive_a_brightness", "1"), ("emissive_a_channels", "1,0,0,0"), ("emissive_a_direction", "0,0"), ("emissive_a_tiling", "1,1"),
            ("emissive_b_brightness", "1"), ("emissive_b_channels", "1,0,0,0"), ("emissive_b_direction", "0,0"), ("emissive_b_tiling", "1,1"),
            ("emissive_c_brightness", "1"), ("emissive_c_channels", "1,0,0,0"), ("emissive_c_direction", "0,0"), ("emissive_c_tiling", "1,1"),
            ("emissive_tint", "1,1,1"), ("global_mask_pan", "0,0"), ("global_mask_tiling", "1,1"),
            ("manual_dither_wangle", "0"), ("threshold_source", "1"), ("threshold_value", "0.3"),
        };
        var slots = new[] { "base_colour", "disintegrate_1_texture", "disintegrate_mask_texture", "dither",
            "emissive_a_texture", "emissive_b_texture", "emissive_c_texture", "global_mask", "material_map", "normal" };
        var paths = new[] { "base.dds", "dissolve.dds", "noise.dds", "dither.dds", "emission.dds", "emission.dds", "emission.dds", "noise.dds", "noise.dds", "noise.dds" };
        return new XElement("material", new XElement("shader", "shaders/vfx_rigid_emissive_dissolve.xml.shader"),
            new XElement("params", fields.Select(field => new XElement("param", new XElement("name", field.Name),
                new XElement("type", field.Value.Contains(',') ? "float" + field.Value.Split(',').Length : "float"), new XElement("value", field.Value)))),
            new XElement("textures", slots.Select((slot, i) => new XElement("texture", new XElement("slot", "t_xml_" + slot), new XElement("source", paths[i])))));
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxLegacyRigidDissolveNativeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MixedLegacyAndOneShotMaterials_KeepTheirOwnConstantsInEitherSubmissionOrder(bool reverse)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var legacy = VfxLegacyRigidDissolveMaterialTests.Material();
            VfxGemstoneMaterialTests.Set(legacy, "emissive_tint", "4,0,0");
            var oneShot = VfxRigidDissolveMaterialTests.Material();
            foreach (var field in new[] { "diffuse_tint", "edge_emissive_colour" }) VfxGemstoneMaterialTests.Set(oneShot, field, "0,0,0");
            foreach (var field in new[] { "emissive_a_brightness", "emissive_b_brightness", "emissive_intensity", "threshold_source" })
                VfxGemstoneMaterialTests.Set(oneShot, field, "1");
            foreach (var field in new[] { "emissive_a_channels", "emissive_b_channels" }) VfxGemstoneMaterialTests.Set(oneShot, field, "1,0,0,0");
            VfxGemstoneMaterialTests.Set(oneShot, "emissive_tint", "0,0,4");
            foreach (var source in oneShot.Descendants("source"))
                source.Value = source.Parent!.Element("slot")!.Value is "t_xml_emissive_a" or "t_xml_emissive_b" ? "emission.dds" : "white.dds";
            oneShot.Descendants("texture").Single(t => t.Element("slot")!.Value == "t_xml_dither").Element("source")!.Value = "dither.dds";
            var effects = new Dictionary<string, byte[]>();
            foreach (var name in new[] { "legacy", "one-shot" })
            {
                var document = VfxDocument.Read(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1));
                document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", $"vfx/{name}.wsmodel");
                effects.Add($"vfx/{name}.xml", document.Write());
            }
            var composition = VfxDocument.CreateComposition();
            foreach (var name in reverse ? new[] { "one-shot", "legacy" } : new[] { "legacy", "one-shot" })
            {
                var child = composition.AddReference(name, VfxDocument.Read(effects[$"vfx/{name}.xml"])); child.Apply();
                child.Selection.SetAttributeValue("pos_x", name == "legacy" ? -1.2f : .3f);
            }
            byte[]? Resolve(string path)
            {
                path = path.Replace('\\', '/');
                if (effects.TryGetValue(path, out var effect)) return effect;
                if (path is "vfx/legacy.wsmodel" or "vfx/one-shot.wsmodel")
                    return Encoding.UTF8.GetBytes($"<model version=\"1\"><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">{path.Replace(".wsmodel", ".material")}</material></materials></model>");
                return path switch
                {
                    "vfx/legacy.material" => Encoding.UTF8.GetBytes(legacy.ToString()),
                    "vfx/one-shot.material" => Encoding.UTF8.GetBytes(oneShot.ToString()),
                    "base.dds" or "dither.dds" => VfxModelVariantPixelTests.Dds(Color.Black),
                    "emission.dds" => VfxModelVariantPixelTests.Dds(new Color(128, 128, 128)),
                    _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
                };
            }
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(composition.Write(), "vfx/mixed-dissolve-diag.xml", Resolve));
            Assert.That(preview.EmitterCount, Is.EqualTo(2), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey + ":" + d.Field)));
            preview.Seek(.25f, Matrix.Identity);
            var camera = new Vector3(0, .5f, 10);
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(camera, new(0, .5f, 0), Vector3.Up),
                Matrix.CreateOrthographic(4, 2, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            device.SetRenderTarget(null); var pixels = new Vector4[128 * 128]; target.GetData(pixels);
            Assert.Multiple(() =>
            {
                Assert.That(pixels.Count(p => p.X > .001f && p.Z < .001f), Is.GreaterThan(100));
                Assert.That(pixels.Count(p => p.Z > .001f && p.X < .001f), Is.GreaterThan(100));
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ThirdEmissionChannel_MultipliesBothOtherChannelsAndIgnoresParticleRgb(bool packed)
    {
        var visible = Render(false, packed, Vector3.One);
        var noThirdChannel = Render(true, packed, Vector3.One);
        var blackParticle = Render(false, packed, Vector3.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(visible.Count(p => p.X > .0001f), Is.GreaterThan(100));
            Assert.That(noThirdChannel.Max(p => p.X), Is.LessThan(.0001f), "Native A*B*C vanishes when the third channel is zero.");
            Assert.That(visible.Zip(blackParticle).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.0001f));
        });
    }

    [Test]
    public void PackedAndAliasedBindings_ProduceTheSameLegacyShaderPixels()
    {
        var aliased = Render(false, false, Vector3.One);
        var packed = Render(false, true, Vector3.One);
        Assert.That(aliased.Zip(packed).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.002f));
    }

    private static Vector4[] Render(bool zeroThirdChannel, bool distinct, Vector3 particleColour)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var material = VfxLegacyRigidDissolveMaterialTests.Material();
            if (zeroThirdChannel) VfxGemstoneMaterialTests.Set(material, "emissive_c_channels", "0,0,0,0");
            var originalPaths = new Dictionary<string, string>();
            if (distinct)
                foreach (var source in material.Descendants("source"))
                {
                    var path = $"distinct-{originalPaths.Count}.dds";
                    originalPaths.Add(path, source.Value); source.Value = path;
                }
            byte[]? Resolve(string path) => originalPaths.GetValueOrDefault(path, path).Replace('\\', '/') switch
            {
                "vfx/character.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
                "base.dds" or "dither.dds" => VfxModelVariantPixelTests.Dds(Color.Black),
                "dissolve.dds" or "noise.dds" => VfxModelVariantPixelTests.Dds(Color.White),
                "emission.dds" => VfxModelVariantPixelTests.Dds(new Color(128, 128, 128)),
                _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
            };
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(VfxOpaqueModelMaterialTests.Source(particleColour, 1), "vfx/legacy-dissolve-diag.xml", Resolve));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey + ":" + d.Field)));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            preview.Seek(.25f, Matrix.Identity);
            var position = new Vector3(.5f, .5f, 10);
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(position, new(.5f, .5f, 0), Vector3.Up),
                Matrix.CreateOrthographic(2, 2, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(parameters); device.SetRenderTarget(null);
            var pixels = new Vector4[128 * 128]; target.GetData(pixels);
            Assert.That(pixels.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), Is.True);
            return pixels;
        }
        finally { graphics.Release(); }
    }
}
