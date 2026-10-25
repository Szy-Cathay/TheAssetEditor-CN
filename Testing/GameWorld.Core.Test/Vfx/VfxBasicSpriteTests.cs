#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxBasicSpriteTests
{
    [Test]
    public void CustomBasicSprite_LoadsItsFourResourcesAndKeepsTheSource()
    {
        var source = VfxAnimatedUvSpriteTests.Source();
        var result = TotalWarVfxConverter.Convert(new(source, "vfx/basic.xml", p => p.EndsWith(".material") ? Material() : VfxMaterialCompositingTests.Texture(255, 255)));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.TexturePaths, Has.Count.EqualTo(4));
            Assert.That(result.MeshMaterials.Single().Parameters, Has.Length.EqualTo(20));
            Assert.That(Shared.GameFormats.Vfx.VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(1, 0, 1)]
    [TestCase(.5f, 0, .5f)]
    [TestCase(1, .5f, 1.5f)]
    public void FloatPixels_PreserveTheShippedColourAlphaAndEmissiveTerms(float alpha, float emissive, float expected)
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try { Assert.That(Render(graphics.GraphicsDevice, Material(emissive), alpha)[64 * 128 + 64].X, Is.EqualTo(expected).Within(.004f)); }
        finally { graphics.Release(); }
    }

    [Test]
    public void DiffuseAlpha_UsesItsAuthoredGammaBeforeOpacityAndColourWeighting()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, Material(), texture: VfxMaterialCompositingTests.Texture(128, 128));
            var expected = 128f / 255 * MathF.Pow(128f / 255, .454f * 2);
            Assert.That(pixels[64 * 128 + 64].X, Is.EqualTo(expected).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void EmissiveMask_UsesItsRedChannelAndKeepsDiffuseColourSeparate()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, Material(1), mask: VfxMaterialCompositingTests.Texture(64, 0));
            Assert.That(pixels[64 * 128 + 64].X, Is.EqualTo(1 + 64f / 255).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OverlayColour_KeepsTheShippedGammaAndPostPower()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, Material(overlay: 1), texture: VfxMaterialCompositingTests.Texture(16, 255));
            var expected = MathF.Pow(Math.Min(1, 2 * MathF.Pow(16f / 255, 1 / 2.2f)), 2.2f);
            Assert.That(pixels[64 * 128 + 64].X, Is.EqualTo(expected).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void ScrollingDistortion_AnimatesTheEmissiveMaskWhileTheStaticControlIsStable()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var assembly = typeof(VfxBasicSpriteTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("NativeUvAsymmetric.dds", StringComparison.Ordinal)))!;
            using var memory = new MemoryStream(); stream.CopyTo(memory); var asymmetric = memory.ToArray();
            var fixedA = Render(graphics.GraphicsDevice, Material(1), time: .1f, mask: asymmetric, noise: asymmetric);
            var fixedB = Render(graphics.GraphicsDevice, Material(1), time: 1, mask: asymmetric, noise: asymmetric);
            var moving = Render(graphics.GraphicsDevice, Material(1, "0.15,0", .25f), time: 1, mask: asymmetric, noise: asymmetric);
            Assert.That(fixedA.Zip(fixedB).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThan(.00001f));
            Assert.That(fixedA.Zip(moving).Count(p => Vector4.Distance(p.First, p.Second) > .01f), Is.GreaterThan(100));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void UnconfirmedFresnelBranch_RemainsDiagnosed()
    {
        var result = TotalWarVfxConverter.Convert(new(VfxAnimatedUvSpriteTests.Source(), "vfx/basic.xml", p => p.EndsWith(".material") ? Material(fresnel: 1) : VfxMaterialCompositingTests.Texture(255, 255)));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported" && d.Field == "no_fresnel_or_fresnel"), Is.True);
    }

    internal static byte[] Material(float emissive = 0, string pan = "0,0", float distortion = 0, float overlay = 0, float fresnel = 0)
    {
        var inputs = new[] { ("t_xml_emissive_distortion_texture_512_bc1", "noise"), ("t_xml_emissiveness_mask_1024_only", "mask"), ("t_xml_particle_diffuse_2048_only", "diffuse"), ("t_xml_particle_normal_1024_only", "normal") };
        string Number(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parameters = new[] { ("colour_multiply_power", "1"), ("colour_overlay_power_post", "1"), ("emissive_colour_multiply", "1,1,1"), ("emissive_distortion_direction", pan), ("emissive_distortion_strength", Number(distortion)), ("emissive_distortion_uv_scale", "1,1"), ("emissive_mask_power", "1"), ("emissiveness", Number(emissive)), ("fresnel_power", "1"), ("lerp_mask_or_diff_for_emiss", "0"), ("lerp_multiply_col_to_overlay_col", Number(overlay)), ("no_fresnel_or_fresnel", Number(fresnel)), ("particle_vfx_func_depth_fade_scale_strength", "1"), ("particle_vfx_func_depth_fade_soft_factor", "0"), ("particle_vfx_func_fog_enable", "false") };
        return Encoding.UTF8.GetBytes(new XElement("material", new XElement("shader", "shaders/wh3_vfx_custom_basic.xml.shader"),
            new XElement("textures", inputs.Select(p => new XElement("texture", new XElement("slot", p.Item1), new XElement("source", "vfx/basic/" + p.Item2 + ".dds")))),
            new XElement("params", parameters.Select(p => new XElement("param", new XElement("name", p.Item1), new XElement("value", p.Item2))))).ToString(SaveOptions.DisableFormatting));
    }

    private static Vector4[] Render(GraphicsDevice device, byte[] material, float alpha = 1, float time = .5f, byte[]? texture = null, byte[]? mask = null, byte[]? noise = null)
    {
        using var preview = new EffekseerVfxPreview(device); var white = VfxMaterialCompositingTests.Texture(255, 255);
        preview.Reload(new(VfxAnimatedUvSpriteTests.Source(alpha), "vfx/basic.xml", p => p.EndsWith(".material") ? material
            : p.EndsWith("diffuse.dds") ? texture ?? white : p.EndsWith("mask.dds") ? mask ?? white : p.EndsWith("noise.dds") ? noise ?? white : white));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(",", preview.Diagnostics.Select(d => d.ReasonKey + ":" + d.Field)));
        preview.Seek(time, Matrix.Identity);
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black);
        try { preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(10, 10, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [])); }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
    }

    private static void RequireGpu() { if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in."); }
}
