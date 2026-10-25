#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeContrastMetadataTests
{
    [Test]
    public void UniformContrast_RequiresNeutralOtherGradingAndPreservesSourceBytes()
    {
        var property = typeof(GameEnvironmentLighting).GetProperty("UniformContrast");
        Assert.That(property, Is.Not.Null, "The captured uniform post-processing contrast must be retained separately from material gain.");
        var original = GameEnvironmentExposureMetadataTests.WeatherRoot();
        foreach (var channel in new[] { "r", "g", "b" }) original.Element("post_processing")!.SetAttributeValue("contrast_" + channel, "1.1");
        var bytes = Encoding.UTF8.GetBytes(original.ToString()); var hash = SHA256.HashData(bytes);
        var weather = GameEnvironmentLighting.Read(bytes, "official-derived/contrast-metadata-DIAG.environment");
        Assert.Multiple(() =>
        {
            Assert.That(property!.GetValue(weather), Is.EqualTo(1.1f).Within(1e-6));
            Assert.That(weather.SourceSha256, Is.EqualTo(Convert.ToHexStringLower(hash)));
            Assert.That(SHA256.HashData(bytes), Is.EqualTo(hash));
        });
        foreach (var (name, value) in new[] { ("contrast_g", "1.2"), ("brightness_r", "1.1"), ("saturation_b", ".9"), ("hue", "1"), ("lut_texture_file", "custom") })
        {
            var changed = new XElement(original); changed.Element("post_processing")!.SetAttributeValue(name, value);
            Assert.That(property!.GetValue(GameEnvironmentExposureMetadataTests.Read(changed)), Is.Null, name + " must retain the unsupported-grading boundary.");
        }
        var malformed = new XElement(original); malformed.Element("post_processing")!.SetAttributeValue("contrast_r", "NaN");
        Assert.Throws<InvalidDataException>(() => GameEnvironmentExposureMetadataTests.Read(malformed));
    }

    [TestCase(".8")]
    [TestCase("1e-8")]
    public void UniformContrast_BelowIdentityRemainsUnsupportedAndPreservesSource(string contrast)
    {
        var original = GameEnvironmentExposureMetadataTests.WeatherRoot();
        foreach (var channel in new[] { "r", "g", "b" })
            original.Element("post_processing")!.SetAttributeValue("contrast_" + channel, contrast);
        var bytes = Encoding.UTF8.GetBytes(original.ToString());
        var before = SHA256.HashData(bytes);
        var weather = GameEnvironmentLighting.Read(bytes, "official-derived/contrast-domain-DIAG.environment");
        Assert.Multiple(() =>
        {
            Assert.That(weather.UniformContrast, Is.Null);
            Assert.That(weather.UnsupportedPostProcessing, Is.True);
            Assert.That(weather.SourceSha256, Is.EqualTo(Convert.ToHexStringLower(before)));
            Assert.That(SHA256.HashData(bytes), Is.EqualTo(before));
        });
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxNativeContrastTests
{
    private const string TexturePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private static readonly float[] Lut = [0,8,16,24,33,41,49,57,65,73,82,91,99,107,115,123,132,140,148,156,164,173,182,190,198,206,214,222,231,239,247,255];
    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) _image;
    private string _sourceHash = "";
    private Vector4 _uv;

    [OneTimeSetUp]
    public void LoadManifestOfficialTextureOnly()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen opt-in is required.");
        var official = new VfxOfficialDistortionPixelTests(); official.LoadCurrentOfficialResourcesOnly();
        var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bytes = (byte[]?)read.Invoke(official, [TexturePath]) ?? throw new FileNotFoundException(TexturePath);
        _sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))decode.Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
        var sample = Enumerable.Range(0, _image.Width * _image.Height).Where(index => _image.Pixels[index * 4 + 3] >= 192)
            .OrderByDescending(index => _image.Pixels[index * 4] + _image.Pixels[index * 4 + 1] + _image.Pixels[index * 4 + 2]).First();
        _uv = new((sample % _image.Width + .5f) / _image.Width, (sample / _image.Width + .5f) / _image.Height, 0, 0);
    }

    [Test]
    public void UniformContrast_IsAppliedAfterGammaAndBeforeTheDefaultLut()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var rows = new List<object>();
            foreach (var gain in new[] { .03f, 3f })
            {
                var linear = LinearPixel(graphics.GraphicsDevice, gain, 1, blend: 1);
                var identity = DisplayPixels(graphics.GraphicsDevice, gain, 1, 0, Color.Black, blend: 1)[2080];
                var contrast = DisplayPixels(graphics.GraphicsDevice, gain, 1, .1f, Color.Black, blend: 1)[2080];
                var expectedIdentity = Encode(new(linear.X, linear.Y, linear.Z), 1);
                var expectedContrast = Encode(new(linear.X, linear.Y, linear.Z), 1.1f);
                rows.Add(new { Gain = gain, Linear = new[] { linear.X, linear.Y, linear.Z }, Identity = identity.R, Contrast = contrast.R, ExpectedIdentity = expectedIdentity.X * 255, ExpectedContrast = expectedContrast.X * 255 });
                Evidence("contrast-final-transfer-DIAG.json", rows);
                Assert.Multiple(() =>
                {
                    Assert.That((int)identity.R, Is.EqualTo(expectedIdentity.X * 255).Within(1.5), "The independent native transfer control must match before testing contrast.");
                    Assert.That((int)contrast.R, Is.EqualTo(expectedContrast.X * 255).Within(1.5), "Display.w carries C-1; it must not be ignored or applied as a material gain.");
                    Assert.That(Math.Abs((int)contrast.R - identity.R), Is.GreaterThanOrEqualTo(3));
                });
            }
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void ContrastDecode_CompensatesRepeatedTransparentDrawsAndPreservesUncoveredRgba()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var linear = LinearPixel(graphics.GraphicsDevice, .65f, .35f, blend: 1);
            var opacity = linear.W;
            Assert.That(opacity, Is.InRange(.05f, .95f));
            var background = new Color(113, 82, 140, 191);
            var expected = background.ToVector3();
            for (var copy = 0; copy < 2; copy++)
            {
                expected = Encode(Decode(expected, 1.1f) * (1 - opacity) + new Vector3(linear.X, linear.Y, linear.Z), 1.1f);
                expected = new Color(expected).ToVector3();
            }
            var actual = DisplayPixels(graphics.GraphicsDevice, .65f, .35f, .1f, background, blend: 1, copies: 2);
            var centre = actual[2080];
            Evidence("contrast-transparent-roundtrip-DIAG.json", new { Opacity = opacity, Actual = new[] { (int)centre.R, centre.G, centre.B, centre.A }, Expected = new[] { expected.X * 255, expected.Y * 255, expected.Z * 255 }, Background = new[] { (int)background.R, background.G, background.B, background.A } });
            Assert.Multiple(() =>
            {
                Assert.That((int)centre.R, Is.EqualTo(expected.X * 255).Within(2));
                Assert.That((int)centre.G, Is.EqualTo(expected.Y * 255).Within(2));
                Assert.That((int)centre.B, Is.EqualTo(expected.Z * 255).Within(2));
                Assert.That(actual[130], Is.EqualTo(background), "Untouched displayed pixels must retain their exact RGBA.");
                Assert.That(centre.A, Is.EqualTo(background.A), "Contrast must not change MAX-composed scene alpha.");
            });
        }
        finally { graphics.Release(); }
    }

    private Vector4 LinearPixel(GraphicsDevice device, float gain, float alpha, int blend)
    {
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        Draw(device, target, gain, alpha, 0, new Color(0, 0, 0, 0), blend, 1, display: false);
        var values = new Vector4[4096]; target.GetData(values); return values[2080];
    }

    private Color[] DisplayPixels(GraphicsDevice device, float gain, float alpha, float contrastDelta, Color background, int blend, int copies = 1)
    {
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
        Draw(device, target, gain, alpha, contrastDelta, background, blend, copies, display: true);
        var values = new Color[4096]; target.GetData(values); return values;
    }

    private void Draw(GraphicsDevice device, RenderTarget2D target, float gain, float alpha, float contrastDelta, Color background, int blend, int copies, bool display)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, _image.Pixels, _image.Pixels.Length, _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.MipLevels));
        var layer = EffekseerLayer.Create(); layer.MaterialMode = EffekseerLayer.PreciseCoverageMode; layer.Blend = blend;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
        var particle = new EffekseerParticle { Transform = Matrix.CreateScale(2), Colour = new(gain, gain, gain, alpha), UV = _uv };
        var lighting = new EffekseerLighting { Display = new(display ? 1 : 0, 1, .05f, contrastDelta), Bloom = new(4, 0, 0, 0) };
        var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up); var projection = Matrix.CreateOrthographic(4, 4, .1f, 50);
        device.SetRenderTarget(target); device.Clear(background); device.DepthStencilState = DepthStencilState.Default;
        for (var copy = 0; copy < copies; copy++) EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1, in lighting));
        device.SetRenderTarget(null);
    }

    private static Vector3 Encode(Vector3 rgb, float contrast)
    {
        rgb = Vector3.Max(rgb, Vector3.Zero); var y = Vector3.Dot(rgb, new(.2126f, .7152f, .0722f));
        var mapped = rgb * ((1 + y / (1 / .05f - .999f)) / (1 + y));
        float Channel(float value) => Lookup(contrast * (MathF.Pow(Math.Clamp(value, 0, 1), 1 / 2.2f) - .5f) + .5f);
        return new(Channel(mapped.X), Channel(mapped.Y), Channel(mapped.Z));
    }

    private static Vector3 Decode(Vector3 rgb, float contrast)
    {
        float Channel(float value) => MathF.Pow(Math.Max(0, (InverseLookup(value) - .5f) / contrast + .5f), 2.2f);
        var c = new Vector3(Channel(rgb.X), Channel(rgb.Y), Channel(rgb.Z)); var y = Vector3.Dot(c, new(.2126f, .7152f, .0722f));
        var white = 1 / (1 / .05f - .999f); var x = 2 * y / Math.Max(MathF.Sqrt((1 - y) * (1 - y) + 4 * white * y) + 1 - y, 1e-8f);
        return c * (x / Math.Max(y, 1e-8f));
    }

    private static float Lookup(float value)
    {
        var p = Math.Clamp(value * 32 - .5f, 0, 31); var index = (int)p;
        return MathHelper.Lerp(Lut[index], Lut[Math.Min(index + 1, 31)], p - index) / 255;
    }

    private static float InverseLookup(float value)
    {
        if (value <= 0 || value >= 1) return Math.Clamp(value, 0, 1);
        for (var index = 0; index < 31; index++) if (value * 255 <= Lut[index + 1]) return (index + .5f + (value * 255 - Lut[index]) / (Lut[index + 1] - Lut[index])) / 32;
        return 1;
    }

    private void Evidence(string name, object metrics)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT"); if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(new { Texture = TexturePath, OriginalDdsSha256 = _sourceHash, Metrics = metrics,
            Diagnostic = "Official manifest original DDS and fixed original texel; direct Display.w contrast only. Not a complete official-effect visual acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
