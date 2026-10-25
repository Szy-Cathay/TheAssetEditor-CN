#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxRigidDissolveMaterialTests
{
    [Test]
    public void SharedResources_RetainAllTenLogicalBindingsAndOriginalParameters()
    {
        var material = Material();
        var before = material.ToString();
        var result = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(before));
        Assert.Multiple(() =>
        {
            Assert.That(result.Parameters, Has.Length.EqualTo(60));
            Assert.That(result.TexturePaths, Has.Length.EqualTo(6));
            Assert.That(result.Parameters[0], Is.EqualTo(.0470588f));
            Assert.That(result.Parameters[48], Is.EqualTo(0));
            Assert.That(result.Parameters[49], Is.EqualTo(1));
            Assert.That(result.Parameters[50], Is.EqualTo(2));
            Assert.That(result.Parameters[51], Is.EqualTo(3));
            Assert.That(result.Parameters[52], Is.EqualTo(2));
            Assert.That(result.Parameters[53], Is.EqualTo(2));
            Assert.That(result.Parameters[54], Is.EqualTo(4));
            Assert.That(result.Parameters[55], Is.EqualTo(4));
            Assert.That(result.Parameters[56], Is.EqualTo(4));
            Assert.That(result.Parameters[57], Is.EqualTo(4));
            Assert.That(result.UsesVertexColour, Is.False);
            Assert.That(material.ToString(), Is.EqualTo(before));
        });
    }

    [Test]
    public void DistinctResources_RetainAllBindingsForPackingWithoutChangingOriginalParameters()
    {
        var material = Material();
        var index = 0;
        foreach (var source in material.Descendants("source")) source.Value = $"unique-{index++}.dds";
        var before = material.ToString();
        var result = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(before));
        Assert.Multiple(() =>
        {
            Assert.That(result.TexturePaths, Is.EqualTo(Enumerable.Range(0, 10).Select(i => $"unique-{i}.dds")));
            Assert.That(result.Parameters.Take(46), Is.EqualTo(VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(Material().ToString())).Parameters.Take(46)));
            Assert.That(result.Parameters[59], Is.EqualTo(1));
            Assert.That(material.ToString(), Is.EqualTo(before));
        });
    }

    [Test]
    public void DistinctResources_KeepTheDissolveMaterialInsteadOfTheOpaqueFallback()
    {
        var material = Material();
        var index = 0;
        foreach (var source in material.Descendants("source")) source.Value = $"unique-{index++}.dds";
        var conversion = TotalWarVfxConverter.Convert(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/dissolve.xml",
            path => path.Replace('\\', '/') == "vfx/character.xml.material" ? Encoding.UTF8.GetBytes(material.ToString())
                : VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White))));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(conversion.Models, Has.Count.EqualTo(1));
            Assert.That(conversion.MeshMaterials, Has.Count.EqualTo(1));
            Assert.That(conversion.MeshMaterials[0].TextureIndexes, Has.Length.EqualTo(10));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.ModelMaterialFallback"), Is.False);
        });
    }

    internal static XElement Material()
    {
        var fields = new (string Name, string Value)[]
        {
            ("diffuse_tint","0.0470588,0.0470588,0.0470588"), ("disintegrate_1_direction","0,-2"),
            ("disintegrate_1_offset","0,3.5"), ("disintegrate_1_tiling","1,0.7"),
            ("disintegrate_mask_direction","-0.5,-10"), ("disintegrate_mask_tiling","2.5,0.1"),
            ("edge_emissive_colour","0.864706,0.45,2.25"), ("edge_emissive_intensity","1"),
            ("emissive_a_brightness","0.1"), ("emissive_a_channels","0,0,0,1"),
            ("emissive_a_direction","1,1"), ("emissive_a_tiling","1,1"),
            ("emissive_b_brightness","0.1"), ("emissive_b_channels","0,0,0,1"),
            ("emissive_b_direction","1,1"), ("emissive_b_tiling","1,1"),
            ("emissive_intensity","0"), ("emissive_tint","128,128,128"),
            ("global_mask_pan","0,-1"), ("global_mask_tiling","1,1"),
            ("manual_dither_wangle","0"), ("threshold_source","0"), ("threshold_value","0.3")
        };
        var slots = new[] { "base_colour", "disintegrate_1_texture", "disintegrate_mask_texture", "dither", "emissive_a", "emissive_b", "emissive_mask", "global_mask", "material_map", "normal" };
        var paths = new[] { "white.dds", "gradient.dds", "noise.dds", "dither.dds", "noise.dds", "noise.dds", "white.dds", "white.dds", "white.dds", "white.dds" };
        return new XElement("material", new XElement("shader", "shaders/wh3_vfx_rigid_emissive_dissolve_one_shot.xml.shader"),
            new XElement("params", fields.Select(f => new XElement("param", new XElement("name", f.Name),
                new XElement("type", f.Value.Contains(',') ? "float" + f.Value.Split(',').Length : "float"), new XElement("value", f.Value)))),
            new XElement("textures", slots.Select((s,i) => new XElement("texture", new XElement("slot", "t_xml_"+s), new XElement("source",paths[i])))));
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxRigidDissolveNativeTests
{
    [Test]
    public void PackedScrollingMasks_PreserveIndependentUvsAndVisibleMotion()
    {
        var first = Render(.8f, true, 1, patterned: true, time: .2f);
        var later = Render(.8f, true, 1, patterned: true, time: .7f);
        var packedFirst = Render(.8f, true, 1, distinct: true, patterned: true, time: .2f);
        var packedLater = Render(.8f, true, 1, distinct: true, patterned: true, time: .7f);
        Assert.Multiple(() =>
        {
            Assert.That(first.Zip(later).Count(p => Vector4.Distance(p.First, p.Second) > .005f), Is.GreaterThan(100));
            Assert.That(first.Zip(packedFirst).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThan(.002f));
            Assert.That(later.Zip(packedLater).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThan(.002f));
        });
    }

    [TestCase(.2f, false)]
    [TestCase(.8f, false)]
    [TestCase(.2f, true)]
    public void DistinctBindings_PreserveCutoutAndEmissionComparedWithTheAliasedMaterial(float alpha, bool constantThreshold)
    {
        var aliased = Render(alpha, constantThreshold, 1);
        var packed = Render(alpha, constantThreshold, 1, distinct: true);
        Assert.That(packed.Zip(aliased).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThan(.0001f));
    }

    [Test]
    public void AlphaThreshold_ClipsGeometryAndConstantThresholdIgnoresParticleAlpha()
    {
        var hidden = Render(.2f, false, 1);
        var visible = Render(.8f, false, 1);
        var constantHiddenAlpha = Render(.2f, true, 1);
        var constantVisibleAlpha = Render(.8f, true, 1);
        Assert.Multiple(() =>
        {
            Assert.That(hidden.Count(IsDrawn), Is.Zero);
            Assert.That(visible.Count(IsDrawn), Is.GreaterThan(100));
            Assert.That(constantHiddenAlpha, Is.EqualTo(constantVisibleAlpha));
            Assert.That(constantHiddenAlpha.Count(IsDrawn), Is.GreaterThan(100));
        });
    }

    [Test]
    public void EdgeEmission_PreservesAuthoredColourAndIntensityInTheFloatTarget()
    {
        var one = Render(.51f, false, 1);
        var two = Render(.51f, false, 2);
        var first = one.Where(IsDrawn).ToArray();
        var second = two.Where(IsDrawn).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(first.Length, Is.GreaterThan(100));
            Assert.That(second.Length, Is.EqualTo(first.Length));
            Assert.That(first.All(p => p.Z > p.X && p.X > p.Y), Is.True);
            Assert.That(second.Select(p => p.Z).Average() - .0470588f,
                Is.EqualTo(2 * (first.Select(p => p.Z).Average() - .0470588f)).Within(.001f));
        });
    }

    private static bool IsDrawn(Vector4 colour) => colour.Z > .01f;

    private static Vector4[] Render(float alpha, bool constantThreshold, float edgeIntensity, bool distinct = false, bool patterned = false, float time = .2f)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var material = VfxRigidDissolveMaterialTests.Material();
            VfxGemstoneMaterialTests.Set(material, "threshold_source", constantThreshold ? "1" : "0");
            VfxGemstoneMaterialTests.Set(material, "edge_emissive_intensity", edgeIntensity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (patterned)
            {
                VfxGemstoneMaterialTests.Set(material, "emissive_intensity", "1");
                VfxGemstoneMaterialTests.Set(material, "emissive_a_channels", "1,0,0,0");
                VfxGemstoneMaterialTests.Set(material, "emissive_b_direction", "-0.5,2");
            }
            var pattern = VfxModelVariantPixelTests.Dds(Color.White);
            for (var texel = 0; texel < 16; ++texel)
            {
                pattern[128 + texel * 4] = (byte)(20 + texel * 13);
                pattern[129 + texel * 4] = (byte)(160 + texel * 5);
            }
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
                "gradient.dds" => VfxModelVariantPixelTests.Dds(new Color(0, 0, 128)),
                "dither.dds" => VfxModelVariantPixelTests.Dds(new Color(128, 128, 128)),
                "noise.dds" when patterned => pattern,
                _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
            };
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, alpha), "vfx/dissolve.xml", Resolve));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey + ":" + d.Field)));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            preview.Seek(time, Matrix.Identity);
            var position = new Vector3(.5f, .5f, 10);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(position, new(.5f, .5f, 0), Vector3.Up),
                Matrix.CreateOrthographic(2, 2, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(camera); device.SetRenderTarget(null);
            var pixels = new Vector4[128 * 128]; target.GetData(pixels);
            Assert.That(pixels.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), Is.True);
            return pixels;
        }
        finally { graphics.Release(); }
    }
}
