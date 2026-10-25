using System.Xml.Linq;
using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSharedTexturePixelTests
{
    [Test]
    public void SharedTextureSlots_KeepLinearAndSrgbViewsSeparateAndRejectInvalidSources()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            Assert.That(EffekseerNative.AecnVfxTextureAlias(handle, 0), Is.Zero);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [128, 128, 128, 255], 4, 1, 1, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, [128, 128, 128, 255], 4, 1, 1, 1));
            Assert.That(EffekseerNative.AecnVfxTextureAlias(handle, -1), Is.Zero);
            Assert.That(EffekseerNative.AecnVfxTextureAlias(handle, 2), Is.Zero);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureAlias(handle, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureAlias(handle, 1));
            var linear = EffekseerLayer.Create(); linear.TextureIndex = 2;
            var srgb = EffekseerLayer.Create(); srgb.TextureIndex = 3;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [linear, srgb], 2));
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreateOrthographic(2, 2, .1f, 100);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black);
            var particles = new[]
            {
                new EffekseerParticle { Layer = 0, Transform = Matrix.CreateScale(.75f) * Matrix.CreateTranslation(-.5f, 0, 0), Colour = Vector4.One, UV = new(0, 0, 1, 1) },
                new EffekseerParticle { Layer = 1, Transform = Matrix.CreateScale(.75f) * Matrix.CreateTranslation(.5f, 0, 0), Colour = Vector4.One, UV = new(0, 0, 1, 1) },
            };
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view), Values(projection), particles, particles.Length));
            device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels);
            Assert.That(pixels[32 * 64 + 16].X, Is.EqualTo(128f / 255).Within(.01));
            Assert.That(pixels[32 * 64 + 48].X, Is.EqualTo(.21586f).Within(.01));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void DuplicateInputs_ReloadsChangedResourceContentWithoutKeepingAnOldSharedTexture()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var effect = document.Root.Element("vfx")!;
        var emitter = new XElement(effect.Element("emitters")!.Element("emitter")!); emitter.SetAttributeValue("id", "second");
        effect.Element("emitters")!.Add(emitter);
        var chain = new XElement(effect.Element("behaviour_chains")!.Element("behaviour_chain")!); chain.SetAttributeValue("id", "second");
        chain.Descendants("texture_property").Single().SetAttributeValue("path", "vfx/other-name.dds");
        effect.Element("behaviour_chains")!.Add(chain);
        var bytes = document.Write(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Vector3(0, 0, 10);
            var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(2, 2, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            foreach (var colour in new[] { Color.Red, Color.Lime })
            {
                preview.Reload(new(bytes, "vfx/shared.xml", _ => VfxModelVariantPixelTests.Dds(colour)));
                Assert.That(preview.EmitterCount, Is.EqualTo(2));
                Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
                preview.Seek(.2f, Matrix.Identity); device.SetRenderTarget(target); device.Clear(Color.Black);
                preview.Draw(parameters); device.SetRenderTarget(null); var pixels = new Color[4096]; target.GetData(pixels);
                Assert.That(pixels.Count(p => colour == Color.Red ? p.R > 100 && p.G < 20 : p.G > 100 && p.R < 20), Is.GreaterThan(100));
            }
            Assert.That(document.Write(), Is.EqualTo(bytes));
        }
        finally { graphics.Release(); }
    }

    [TestCase("t_xml_Texture_A")]
    [TestCase("t_xml_colour_overlay")]
    public void MaterialValues_RespectExplicitDdsColourSpaceAndKeepLegacyTexturesLinearOnReload(string slot)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/material.wsmodel");
        var source = document.Write();
        var material = new XElement("material", new XElement("shader", "shaders/wh_vfx_mesh.xml.shader"),
            new XElement("textures", new[] { "t_xml_Texture_A", "t_xml_Texture_B", "t_xml_Texture_C",
                "t_xml_colour_overlay", "t_xml_distortion_texture", "t_xml_global_mask" }.Select(name =>
                new XElement("texture", new XElement("slot", name), new XElement("source", name == slot ? "value.dds" : "white.dds")))),
            new XElement("params", VfxScrollingMeshMaterial.Fields.Select(field =>
                new XElement("param", new XElement("name", field.Name),
                    new XElement("type", field.Components == 1 ? "float" : "float" + field.Components), new XElement("value", field.Name switch
                {
                    "Texture_A_channels" or "Texture_B_Channels" or "Texture_C_Channels" => "1,0,0,0",
                    "colour_distortion_switch" or "depth_fade_switch" or "normal_fade_power" or "vertex_colour" => "0",
                    _ => string.Join(',', Enumerable.Repeat(field.Name.Contains("pan") || field.Name.Contains("_scroll") ? "0" : "1", field.Components)),
                })))));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            var position = new Vector3(0, 0, 10);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(position, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(4, 4, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            // The real material mixes legacy DXT5 and explicitly tagged BC1. Reuse the path to exercise cache invalidation.
            foreach (var format in new[] { 0, 72, 71, 72 })
            {
                var texture = MaterialDds(format);
                var original = texture.ToArray();
                preview.Reload(new(source, "vfx/material.xml", path => path.Replace('\\', '/') switch
                {
                    "vfx/material.wsmodel" => Encoding.UTF8.GetBytes("<model version=\"1\"><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/material.xml.material</material></materials></model>"),
                    "vfx/material.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
                    "vfx/model-0.rigid_model_v2" => VfxModelVariantTests.Model(0),
                    "value.dds" => texture,
                    _ => VfxModelVariantPixelTests.Dds(Color.White),
                }));
                Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
                preview.Seek(.2f, Matrix.Identity);
                device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[64 * 64]; target.GetData(pixels);
                var linear = 132f / 255; // The red endpoint of RGB565 0x8410.
                var expected = format == 72 ? MathF.Pow((linear + .055f) / 1.055f, 2.4f) : linear;
                Assert.That(pixels.Max(pixel => pixel.X), Is.EqualTo(expected).Within(.002f), $"slot={slot}, DDS format={format}");
                Assert.That(texture, Is.EqualTo(original));
            }
        }
        finally { graphics.Release(); }
    }

    private static byte[] MaterialDds(int format)
    {
        var header = format == 0 ? 128 : 148;
        var bytes = new byte[header + (format == 0 ? 16 : 8)];
        void U(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        U(0, 0x20534444); U(4, 124); U(8, 0x81007); U(12, 4); U(16, 4); U(20, (uint)(bytes.Length - header)); U(28, 1);
        U(76, 32); U(80, 4); U(84, format == 0 ? 0x35545844u : 0x30315844u); U(108, 0x1000);
        if (format != 0) { U(128, (uint)format); U(132, 3); U(140, 1); }
        else { bytes[header] = bytes[header + 1] = 255; header += 8; }
        bytes[header] = bytes[header + 2] = 0x10; bytes[header + 1] = bytes[header + 3] = 0x84;
        return bytes;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void AuthoredMipChain_UsesTheOriginalReducedLevelsAndBlendsBetweenThem(bool compressed, bool fractional)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("modifier").Single().Add(new XElement("property", new XAttribute("name", "pre_multiplied_alpha"), new XAttribute("value", "true")));
        var source = document.Write(); var texture = MipDds(compressed); var original = texture.ToArray();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(source, "vfx/authored-mips.xml", _ => texture));
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            preview.Seek(.2f, Matrix.Identity);
            var span = fractional ? 8 * MathF.Sqrt(2) : 8;
            var position = new Vector3(0, 0, 10);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(position, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(span, span, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); preview.Draw(camera); device.SetRenderTarget(null);
            var pixels = new Vector4[4096]; target.GetData(pixels);
            var grey = new Vector3(compressed ? 132f / 255 : 128f / 255, compressed ? 130f / 255 : 128f / 255, compressed ? 132f / 255 : 128f / 255);
            grey = new(MathF.Pow((grey.X + .055f) / 1.055f, 2.4f), MathF.Pow((grey.Y + .055f) / 1.055f, 2.4f), MathF.Pow((grey.Z + .055f) / 1.055f, 2.4f));
            var expected = fractional ? Vector3.Lerp(Vector3.UnitZ, grey, .5f) : Vector3.UnitZ;
            Assert.That(Vector3.Distance(new(pixels[32 * 64 + 32].X, pixels[32 * 64 + 32].Y, pixels[32 * 64 + 32].Z), expected),
                Is.LessThan(.006f), $"Expected authored level {(fractional ? "2.5" : "2")}, observed {pixels[32 * 64 + 32]}");
            Assert.That(texture, Is.EqualTo(original));
            Assert.That(document.Write(), Is.EqualTo(source));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void AuthoredMipUpload_RejectsTruncatedAndImpossibleChainsWithoutConsumingASlot()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var direct = (SharpDX.Direct3D11.Device)graphics.GraphicsDevice.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            Assert.That(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, new byte[64], 64, 4, 4, 0, 2), Is.Zero);
            Assert.That(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, new byte[8], 8, 1, 1, 0, 2), Is.Zero);
            Assert.That(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, new byte[4], 4, 1, 1, 0, 0), Is.Zero);
            Assert.That(EffekseerNative.AecnVfxTextureAlias(handle, 0), Is.Zero);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, new byte[12], 12, 2, 1, 0, 2));
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureAlias(handle, 0));
        }
        finally { graphics.Release(); }
    }

    private static byte[] MipDds(bool compressed)
    {
        var dimensions = new[] { 32, 16, 8, 4, 2, 1 };
        var header = compressed ? 148 : 128;
        var bytes = new byte[header + dimensions.Sum(size => compressed ? Math.Max(1, size / 4) * Math.Max(1, size / 4) * 8 : size * size * 4)];
        void U(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        U(0, 0x20534444); U(4, 124); U(8, compressed ? 0xA1007u : 0x2100Fu); U(12, 32); U(16, 32);
        U(20, compressed ? 512u : 128u); U(28, 6); U(76, 32); U(108, 0x401008);
        if (compressed) { U(80, 4); U(84, 0x30315844); U(128, 72); U(132, 3); U(140, 1); }
        else { U(80, 0x41); U(88, 32); U(92, 255); U(96, 65280); U(100, 16711680); U(104, 4278190080); }
        var colours = new[] { Color.Red, Color.Lime, Color.Blue, new Color(128, 128, 128), Color.White, Color.Black };
        var endpoints = new ushort[] { 0xF800, 0x07E0, 0x001F, 0x8410, 0xFFFF, 0 };
        var offset = header;
        for (var mip = 0; mip < dimensions.Length; ++mip)
        {
            var size = dimensions[mip];
            var count = compressed ? Math.Max(1, size / 4) * Math.Max(1, size / 4) : size * size;
            for (var i = 0; i < count; ++i)
            {
                if (compressed)
                {
                    BitConverter.GetBytes(endpoints[mip]).CopyTo(bytes, offset);
                    BitConverter.GetBytes(endpoints[mip]).CopyTo(bytes, offset + 2); offset += 8;
                }
                else { var c = colours[mip]; bytes[offset++] = c.R; bytes[offset++] = c.G; bytes[offset++] = c.B; bytes[offset++] = 255; }
            }
        }
        return bytes;
    }

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
