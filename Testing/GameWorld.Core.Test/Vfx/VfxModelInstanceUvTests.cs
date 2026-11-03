#nullable enable
using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelInstanceUvTests
{
    [TestCase(0f, 0f, 0f)]
    [TestCase(1f, 0f, 0f)]
    [TestCase(.25f, .25f, .25f)]
    [TestCase(.00048828125f, .49462890625f, .11962890625f)]
    [TestCase(.0009765625f, .9892578125f, .2392578125f)]
    [TestCase(.998046875f, .021484375f, .521484375f)]
    public void NativeSeedMultipliers_PreserveBothIndependentFractionalCoordinates(float seed, float u, float v)
    {
        // Exact binary fractions exercise the native mulss/floor/subss sequence without an RNG oracle.
        Assert.That(VfxModelInstanceUv.FromSeed(seed), Is.EqualTo(new Vector2(u, v)));
    }

    [Test]
    public void ModelFrame_UsesItsExistingParticleUnaryWithoutChangingSourceOrParticleAlpha()
    {
        var bytes = VfxOpaqueModelMaterialTests.Source(Vector3.One, .8f);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/instance-uv-diag.xml",
            path => path.Replace('\\', '/') == "vfx/character.xml.material"
                ? Encoding.UTF8.GetBytes(VfxLegacyRigidDissolveMaterialTests.Material().ToString())
                : VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White))));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.25f, Matrix.Identity);
        var particle = Particle(simulation);
        particle.GetType().GetField("RandomAlpha")!.SetValue(particle, 1f / 2048);
        var frame = simulation.Frame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(frame.Layer, Is.Zero);
            Assert.That(frame.NormalRotation, Is.EqualTo(new Vector4(.49462890625f, .11962890625f, 0, 1)));
            Assert.That(frame.Colour.W, Is.EqualTo(.8f).Within(.00001f));
            Assert.That(Shared.GameFormats.Vfx.VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    internal static object Particle(TotalWarVfxSimulation simulation) => ((IEnumerable)typeof(TotalWarVfxSimulation)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!).Cast<object>().Single();

    internal static CommonShaderParameters Camera
    {
        get
        {
            var eye = new Vector3(.5f, .5f, 10);
            return new(Matrix.CreateLookAt(eye, new(.5f, .5f, 0), Vector3.Up), Matrix.CreateOrthographic(2, 2, .1f, 100),
                eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        }
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxModelInstanceUvNativeTests
{
    [TestCase(false, "b", true)]
    [TestCase(false, "b", false)]
    [TestCase(false, "c", true)]
    [TestCase(false, "c", false)]
    [TestCase(true, "b", true)]
    [TestCase(true, "b", false)]
    public void RandomInstanceUv_MatchesIndependentlyBakedMeshUvForEachAxis(bool oneShot, string channel, bool horizontal)
    {
        Verify(oneShot, channel, horizontal, appliesOffset: true);
    }

    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public void EmissionA_KeepsItsOriginalUvWhileTheOtherChannelsReceiveInstanceUv(bool oneShot, bool horizontal)
    {
        Verify(oneShot, "a", horizontal, appliesOffset: false);
    }

    private static void Verify(bool oneShot, string channel, bool horizontal, bool appliesOffset)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var baseUv = new Vector2(.125f, .375f);
            var offset = new Vector2(.49462890625f, .11962890625f);
            var actual = Render(graphics.GraphicsDevice, oneShot, channel, horizontal, 1f / 2048, baseUv);
            var expected = Render(graphics.GraphicsDevice, oneShot, channel, horizontal, 0, baseUv + (appliesOffset ? offset : Vector2.Zero));
            var wrongRoute = Render(graphics.GraphicsDevice, oneShot, channel, horizontal, 0, baseUv + (appliesOffset ? Vector2.Zero : offset));
            var drawn = expected.Select((pixel, index) => (pixel, index)).Where(item => item.pixel.X > .0001f).Select(item => item.index).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(drawn, Has.Length.GreaterThan(100), "The control must contain a visible model surface.");
                Assert.That(actual.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThan(.0001f),
                    "Native instance UV must match a separately baked geometry UV, including its axis order and shader channel routing.");
                Assert.That(drawn.Average(index => MathF.Abs(expected[index].X - wrongRoute[index].X)), Is.GreaterThan(.005f),
                    "The high-frequency texture must distinguish the intended route from the omitted or incorrectly added offset.");
            });
        }
        finally { graphics.Release(); }
    }

    private static Vector4[] Render(GraphicsDevice device, bool oneShot, string channel, bool horizontal, float seed, Vector2 uv)
    {
        var material = oneShot ? VfxRigidDissolveMaterialTests.Material() : VfxLegacyRigidDissolveMaterialTests.Material();
        foreach (var field in new[] { "diffuse_tint", "edge_emissive_colour" }) VfxGemstoneMaterialTests.Set(material, field, "0,0,0");
        foreach (var field in material.Elements("params").Elements("param").Where(field => field.Element("name")!.Value.EndsWith("_direction")))
            field.Element("value")!.Value = "0,0";
        foreach (var field in material.Elements("params").Elements("param").Where(field => field.Element("name")!.Value.EndsWith("_tiling")))
            field.Element("value")!.Value = "1,1";
        VfxGemstoneMaterialTests.Set(material, "global_mask_pan", "0,0");
        VfxGemstoneMaterialTests.Set(material, "edge_emissive_intensity", "0");
        VfxGemstoneMaterialTests.Set(material, "emissive_tint", "1,1,1");
        VfxGemstoneMaterialTests.Set(material, "threshold_source", "1");
        VfxGemstoneMaterialTests.Set(material, "threshold_value", "0");
        foreach (var name in new[] { "a", "b" }.Concat(oneShot ? Array.Empty<string>() : new[] { "c" }))
        {
            VfxGemstoneMaterialTests.Set(material, "emissive_" + name + "_brightness", "1");
            VfxGemstoneMaterialTests.Set(material, "emissive_" + name + "_channels", "1,0,0,0");
        }
        if (oneShot)
        {
            VfxGemstoneMaterialTests.Set(material, "disintegrate_1_offset", "0,0");
            VfxGemstoneMaterialTests.Set(material, "emissive_intensity", "1");
        }
        foreach (var texture in material.Elements("textures").Elements("texture"))
        {
            var slot = texture.Element("slot")!.Value;
            texture.Element("source")!.Value = slot == "t_xml_base_colour" || slot == "t_xml_dither" ? "black.dds"
                : slot == "t_xml_emissive_" + channel + (oneShot ? "" : "_texture") ? "pattern.dds" : "white.dds";
        }
        var model = ModelFactory.Create().Load(VfxModelVariantTests.Model(0));
        foreach (var vertex in model.ModelList[0][0].Mesh.VertexList) vertex.Uv = uv;
        var geometry = ModelFactory.Create().Save(model);
        var pattern = VfxModelVariantPixelTests.Dds(Color.White);
        for (var index = 0; index < 16; ++index)
        {
            var coordinate = horizontal ? index % 4 : index / 4;
            pattern[128 + index * 4] = new byte[] { 32, 224, 80, 160 }[coordinate];
        }
        byte[]? Resolve(string path) => path.Replace('\\', '/') switch
        {
            "vfx/character.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
            "vfx/model-0.rigid_model_v2" => geometry,
            "pattern.dds" => pattern,
            "black.dds" => VfxModelVariantPixelTests.Dds(Color.Black),
            _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
        };
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/instance-uv-pixels-diag.xml", Resolve));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(item => item.ReasonKey + ":" + item.Field)));
        preview.Seek(.25f, Matrix.Identity);
        var simulation = (TotalWarVfxSimulation)typeof(EffekseerVfxPreview).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        var particle = VfxModelInstanceUvTests.Particle(simulation);
        particle.GetType().GetField("RandomAlpha")!.SetValue(particle, seed);
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        preview.Draw(VfxModelInstanceUvTests.Camera); device.SetRenderTarget(null);
        var pixels = new Vector4[128 * 128]; target.GetData(pixels);
        Assert.That(pixels.All(pixel => float.IsFinite(pixel.X) && float.IsFinite(pixel.Y) && float.IsFinite(pixel.Z)), Is.True);
        return pixels;
    }
}
