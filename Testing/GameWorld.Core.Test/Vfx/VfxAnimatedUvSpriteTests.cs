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
public class VfxAnimatedUvSpriteTests
{
    [Test]
    public void CustomAnimatedUvSprite_LoadsItsSevenInputsWithoutChangingTheGameDocument()
    {
        var source = Source(); var material = Material();
        var result = TotalWarVfxConverter.Convert(new(source, "vfx/custom.xml", p => p.EndsWith(".material") ? material : VfxMaterialCompositingTests.Texture(255, 255)));
        Assert.That(result.Emitters, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters[0].Render.Kind, Is.Zero);
            Assert.That(result.MeshMaterials, Has.Count.EqualTo(1));
            Assert.That(result.TexturePaths, Has.Count.EqualTo(7));
            Assert.That(result.Models, Is.Empty);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(1f, .4f, true)]
    [TestCase(.5f, .15f, true)]
    [TestCase(1f, .4f, false)]
    public void WhiteInputs_RespectTheShippedColourAndOpacityContract(float alpha, float expected, bool depth)
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var actual = Render(graphics.GraphicsDevice, Source(alpha), Material(), .5f, depth: depth);
            Assert.That(actual[64 * 128 + 64].X, Is.EqualTo(expected).Within(.004f));
            Assert.That(actual[64 * 128 + 64].Y, Is.EqualTo(expected).Within(.004f));
            Assert.That(actual[64 * 128 + 64].Z, Is.EqualTo(expected).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OpacityUsesMaskRedAndPreservesSourceAlphaBeforeTheFinalSaturation()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, Source(2), Material(), .5f, globalTexture: VfxMaterialCompositingTests.Texture(64, 0));
            var opacity = 2f * 64 / 255; var expected = .2f * opacity * (1 + opacity);
            Assert.That(pixels[64 * 128 + 64].X, Is.EqualTo(expected).Within(.004f));
        }
        finally { graphics.Release(); }
    }

    [TestCase(2)]
    [TestCase(3)]
    public void PackedMasks_PreserveUnequalInputsAndBilinearRepeatAcrossEveryEdge(int count)
    {
        var first = Enumerable.Range(0, 2 * 3 * 4).Select(i => (byte)(i * 9)).ToArray();
        var second = Enumerable.Range(0, 3 * 1 * 4).Select(i => (byte)(255 - i * 13)).ToArray();
        var originalFirst = (byte[])first.Clone(); var originalSecond = (byte[])second.Clone();
        var third = Enumerable.Range(0, 4 * 2 * 4).Select(i => (byte)(i * 7)).ToArray();
        var originalThird = (byte[])third.Clone();
        var packed = count == 2 ? VfxMaterialMaskAtlas.Pack(first, 2, 3, second, 3, 1)
            : VfxMaterialMaskAtlas.PackThree((first, 2, 3), (second, 3, 1), (third, 4, 2));
        foreach (var input in new[] { (Pixels: first, Width: 2, Height: 3, Offset: 0), (Pixels: second, Width: 3, Height: 1, Offset: 4),
            (Pixels: third, Width: 4, Height: 2, Offset: 8) }.Take(count))
            foreach (var u in new[] { -1.001f, -.3f, 0, .02f, .5f, .999f, 1, 1.02f })
                foreach (var v in new[] { -.01f, 0, .45f, .999f, 1.03f })
                {
                    var rectangle = packed.Rectangles.AsSpan(input.Offset, 4);
                    var actual = Sample(packed.Pixels, packed.Width, packed.Height,
                        rectangle[0] + (u - MathF.Floor(u)) * rectangle[2], rectangle[1] + (v - MathF.Floor(v)) * rectangle[3]);
                    Assert.That(Vector4.Distance(actual, Sample(input.Pixels, input.Width, input.Height, u, v)), Is.LessThan(.00001f));
                }
        Assert.That(first, Is.EqualTo(originalFirst)); Assert.That(second, Is.EqualTo(originalSecond));
        Assert.That(third, Is.EqualTo(originalThird));

        static Vector4 Sample(byte[] pixels, int width, int height, float u, float v)
        {
            var x = (u - MathF.Floor(u)) * width - .5f; var y = (v - MathF.Floor(v)) * height - .5f;
            var left = (int)MathF.Floor(x); var top = (int)MathF.Floor(y);
            return Vector4.Lerp(Vector4.Lerp(Texel(left, top), Texel(left + 1, top), x - left),
                Vector4.Lerp(Texel(left, top + 1), Texel(left + 1, top + 1), x - left), y - top);
            Vector4 Texel(int column, int row)
            {
                var offset = (((row % height + height) % height) * width + (column % width + width) % width) * 4;
                return new Vector4(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]) / 255;
            }
        }
    }

    [Test]
    public void MainTexturePan_ChangesTheActualPixelsWhileAStationaryControlStaysFixed()
    {
        RequireGpu(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var assembly = typeof(VfxAnimatedUvSpriteTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("NativeUvAsymmetric.dds", StringComparison.Ordinal)))!;
            using var memory = new MemoryStream(); stream.CopyTo(memory); var asymmetric = memory.ToArray();
            var fixedA = Render(device, Source(), Material(), .1f, asymmetric);
            var fixedB = Render(device, Source(), Material(), 1f, asymmetric);
            var moving = Render(device, Source(), Material("0.15,0"), 1f, asymmetric);
            Assert.That(fixedA.Zip(fixedB).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThan(.00001f));
            Assert.That(fixedA.Zip(moving).Count(p => Vector4.Distance(p.First, p.Second) > .01f), Is.GreaterThan(100));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void MissingMaterialTexture_IsNotReplacedWithAWhiteSprite()
    {
        var result = TotalWarVfxConverter.Convert(new(Source(), "vfx/custom.xml", p => p.EndsWith(".material") ? Material() : null));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource" && d.Field.Contains("alpha1")), Is.True);
    }

    internal static byte[] Source(float alpha = 1)
    {
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0, alpha: alpha));
        var quality = doc.Root.Descendants("quality").Single();
        quality.Elements("modifier").Where(m => (string?)m.Attribute("fragment") is "ps_texture" or "ps_lighting").Remove();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "ps_custom_shader"), new XElement("property", new XAttribute("name", "material"), new XAttribute("type", "file"), new XAttribute("path", "vfx/custom.material"))));
        return doc.Write();
    }

    internal static byte[] Material(string pan = "0,0")
    {
        var inputs = new[] { ("t_xml_alpha_1", "alpha1"), ("t_xml_alpha_2", "alpha2"), ("t_xml_detail_tex", "detail"), ("t_xml_emissiveness_mask_1024_only", "noise"), ("t_xml_global_opacity", "global"), ("t_xml_main_tex_diffuse_1", "main1"), ("t_xml_main_tex_diffuse_2", "main2") };
        var parameters = new[] { ("alpha_direction_1", "float2", "0,0"), ("alpha_direction_2", "float2", "0,0"), ("alpha_scale_1", "float2", "1,1"), ("alpha_scale_2", "float2", "1,1"), ("detail_direction", "float2", "0,0"), ("detail_distortion", "float", "0"), ("detail_emission", "float", "0"), ("detail_scale", "float2", "1,1"), ("emissive", "float", "0.1"), ("main_tex_direction_1", "float2", pan), ("main_tex_direction_2", "float2", "0,0"), ("main_tex_scale_1", "float2", "1,1"), ("main_tex_scale_2", "float2", "1,1"), ("particle_vfx_func_depth_fade_scale_strength", "float", "1"), ("particle_vfx_func_depth_fade_soft_factor", "float", "0"), ("particle_vfx_func_fog_enable", "bool", "false"), ("tex_power", "float", "1") };
        return Encoding.UTF8.GetBytes(new XElement("material", new XElement("shader", "shaders/fx_animated_uvs_custom.xml.shader"),
            new XElement("textures", inputs.Select(p => new XElement("texture", new XElement("slot", p.Item1), new XElement("source", "vfx/custom/" + p.Item2 + ".dds")))),
            new XElement("params", parameters.Select(p => new XElement("param", new XElement("name", p.Item1), new XElement("type", p.Item2), new XElement("value", p.Item3))))).ToString(SaveOptions.DisableFormatting));
    }

    private static Vector4[] Render(GraphicsDevice device, byte[] source, byte[] material, float time, byte[]? movingTexture = null, byte[]? globalTexture = null, bool depth = true)
    {
        using var preview = new EffekseerVfxPreview(device);
        var white = VfxMaterialCompositingTests.Texture(255, 255);
        preview.Reload(new(source, "vfx/custom.xml", p => p.EndsWith(".material") ? material
            : p.EndsWith("main1.dds") && movingTexture != null ? movingTexture : p.EndsWith("global.dds") && globalTexture != null ? globalTexture : white));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(",", preview.Diagnostics.Select(d => d.ReasonKey + ":" + d.Field)));
        preview.Seek(time, Matrix.Identity);
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, depth ? DepthFormat.Depth24 : DepthFormat.None);
        device.SetRenderTarget(target); device.Clear(Color.Black);
        try
        {
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(10, 10, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
        }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
    }
    private static void RequireGpu() { if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in."); }
}
