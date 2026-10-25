#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxPrelitSpriteTests
{
    [Test]
    public void OriginalLayout_KeepsFiveBindingsAndTwentyOneParametersWithoutRewritingSource()
    {
        var source = VfxAnimatedUvSpriteTests.Source();
        var converted = TotalWarVfxConverter.Convert(new(source, "vfx/prelit.xml", p => p.EndsWith(".material") ? Material() : VfxMaterialCompositingTests.Texture(255, 255)));
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters, Has.Count.EqualTo(1));
            Assert.That(converted.TexturePaths, Has.Count.EqualTo(5));
            Assert.That(converted.TexturePaths[2], Does.EndWith("material.dds"));
            Assert.That(converted.TextureUsages[3], Is.EqualTo(VfxTextureUsage.Diffuse));
            Assert.That(converted.MeshMaterials.Single().Parameters, Has.Length.EqualTo(24));
            Assert.That(converted.MeshMaterials.Single().Parameters[20], Is.Zero);
            Assert.That(converted.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.PrelitSpriteMaterialCandidate"), Is.True);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(0, 0)]
    [TestCase(.5f, .5f)]
    [TestCase(1, 1)]
    [TestCase(3, 1)]
    public void FloatPixels_PreserveRawParticleAlphaAndClampCoverageAfterTexture(float alpha, float expected)
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try { Assert.That(Render(graphics.GraphicsDevice, Material(), alpha)[64 * 128 + 64].X, Is.EqualTo(expected).Within(.004f)); }
        finally { graphics.Release(); }
    }

    [Test]
    public void TextureAlpha_IsGammaAdjustedBeforeBothColourWeightAndCoverage()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, Material(), diffuse: VfxMaterialCompositingTests.Texture(128, 128));
            var linear = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
            Assert.That(pixels[64 * 128 + 64].X, Is.EqualTo(linear * MathF.Pow(128f / 255, .908f)).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void ParticleColour_TintsTheIntrinsicTextureInsteadOfBeingIgnored()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, Material(), tint: Vector3.UnitX);
            Assert.That(Vector3.Distance(new(pixels[64 * 128 + 64].X, pixels[64 * 128 + 64].Y, pixels[64 * 128 + 64].Z), Vector3.UnitX), Is.LessThan(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void EmissiveSample_UsesTheShippedGreenMaskAndTintIndependentlyOfDiffuseColour()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var green = VfxMaterialCompositingTests.Texture(0, 255);
            var red = VfxMaterialCompositingTests.Texture(0, 255);
            for (var offset = 128; offset < green.Length; offset += 4) { green[offset + 1] = 255; red[offset] = 255; }
            var bright = Render(graphics.GraphicsDevice, Material(sample: 1), mask: green)[64 * 128 + 64];
            var dark = Render(graphics.GraphicsDevice, Material(sample: 1), mask: red)[64 * 128 + 64];
            Assert.That(bright.X - dark.X, Is.EqualTo(1).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void MissingBinding_OnlyRejectsItsDependentEmitter()
    {
        var xml = XElement.Parse(Encoding.UTF8.GetString(Material()));
        xml.Element("textures")!.Elements().Single(e => e.Element("slot")!.Value == "t_xml_material_map").Remove();
        var result = TotalWarVfxConverter.Convert(new(VfxAnimatedUvSpriteTests.Source(), "vfx/prelit.xml", _ => Encoding.UTF8.GetBytes(xml.ToString())));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d => d.Field == "t_xml_material_map" && d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
    }

    [Test]
    public void UncalibratedSunScaledEmission_IsExplicitlyRejected()
    {
        var result = TotalWarVfxConverter.Convert(new(VfxAnimatedUvSpriteTests.Source(), "vfx/prelit.xml", _ => Material(sun: 1)));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d => d.Field == "lerp_glowy_to_diffuse_scale_emissive_col" && d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True);
    }

    internal static byte[] Material(float sample = 0, float sun = 0)
    {
        var slots = new[] { "t_xml_emissive_distortion_texture_512_bc1", "t_xml_emissiveness_mask_1024_only", "t_xml_material_map", "t_xml_particle_diffuse_2048_only", "t_xml_particle_normal_1024_only" };
        var names = new[] { "noise", "mask", "material", "diffuse", "normal" };
        var fields = new[] { ("colour_multiply_power", "1"), ("colour_overlay_power_post", "1"), ("emissive_colour", "1,1,1"), ("emissive_colour_0_to_sample", sample.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("emissive_colour_tint_multiplier", "1,1,1"), ("emissive_distortion_direction", "0,0"), ("emissive_distortion_strength", "0"), ("emissive_distortion_uv_scale", "0,0"), ("emissive_mask_power", "1"), ("emissiveness", "0"), ("fog_strength", "1"), ("lerp_glowy_to_diffuse_scale_emissive_col", sun.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("lerp_multiply_col_to_overlay_col", "0"), ("particle_vfx_func_depth_fade_scale_strength", "0"), ("particle_vfx_func_depth_fade_soft_factor", "0") };
        return Encoding.UTF8.GetBytes(new XElement("material", new XElement("shader", "shaders/wh3_vfx_custom_basic_pre_lit.xml.shader"),
            new XElement("textures", slots.Select((s, i) => new XElement("texture", new XElement("slot", s), new XElement("source", "vfx/prelit/" + names[i] + ".dds")))),
            new XElement("params", fields.Select(f => new XElement("param", new XElement("name", f.Item1), new XElement("value", f.Item2))))).ToString(SaveOptions.DisableFormatting));
    }

    private static Vector4[] Render(GraphicsDevice device, byte[] material, float alpha = 1, byte[]? diffuse = null, byte[]? mask = null, Vector3? tint = null)
    {
        var source = VfxDocument.Read(VfxAnimatedUvSpriteTests.Source(alpha));
        if (tint.HasValue)
        {
            var original = VfxDocument.Read(VfxMaterialCompositingTests.Source(tint.Value, false, 0));
            source.Root.Descendants().Single(e => (string?)e.Attribute("name") == "curve_colour").Value = original.Root.Descendants().Single(e => (string?)e.Attribute("name") == "curve_colour").Value;
        }
        using var preview = new EffekseerVfxPreview(device); var white = VfxMaterialCompositingTests.Texture(255, 255);
        preview.Reload(new(source.Write(), "vfx/prelit.xml", p => p.EndsWith(".material") ? material : p.EndsWith("diffuse.dds") ? diffuse ?? white : p.EndsWith("mask.dds") ? mask ?? white : p.EndsWith("material.dds") ? VfxMaterialCompositingTests.Texture(0, 255) : white));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(',', preview.Diagnostics.Select(d => d.ReasonKey + ":" + d.Field)));
        preview.Seek(.5f, Matrix.Identity);
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black);
        try { preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(10, 10, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [])); }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
    }

    private static void RequireGpu() { if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in."); }
}
