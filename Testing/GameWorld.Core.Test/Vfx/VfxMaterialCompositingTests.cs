using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxMaterialCompositingTests
{
    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
    }

    public static IEnumerable<TestCaseData> EncodingCases()
    {
        foreach (var item in Fixture().GetProperty("Encoding").EnumerateArray())
            yield return new TestCaseData(item.GetProperty("Gray").GetByte(), item.GetProperty("Brightness").GetSingle());
    }

    public static IEnumerable<TestCaseData> OverlapCases()
    {
        foreach (var item in Fixture().GetProperty("Overlap").EnumerateArray())
            yield return new TestCaseData(item.GetProperty("Texel").GetByte(), item.GetProperty("Count").GetInt32());
    }

    [TestCaseSource(nameof(EncodingCases))]
    public void NativeGrayTexture_MatchesItsLinearWhiteReferenceWithoutLosingDarkColour(byte gray, float brightness)
    {
        var native = Fixture().GetProperty("Encoding").EnumerateArray().Single(item =>
            item.GetProperty("Gray").GetByte() == gray && item.GetProperty("Brightness").GetSingle() == brightness);
        Assert.That(native.GetProperty("TextureRgb").EnumerateArray().Select(value => value.GetSingle()),
            Is.EqualTo(native.GetProperty("LinearReferenceRgb").EnumerateArray().Select(value => value.GetSingle())));
        var value = gray / 255f;
        var linear = value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
        var expected = linear * brightness;
        var source = Source(new(brightness), false, 0);
        var reference = Source(new(expected), false, 0);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var texturePixel = Render(graphics.GraphicsDevice, source, Texture(gray, 255), 1);
            var referencePixel = Render(graphics.GraphicsDevice, reference, Texture(255, 255), 1);
            var tolerance = Math.Max(.000002f, expected * .01f);
            Assert.Multiple(() =>
            {
                Assert.That(texturePixel.X, Is.EqualTo(expected).Within(tolerance), "The diffuse role uses sRGB sampling even for a legacy DDS without a DX10 colour-space tag.");
                Assert.That(referencePixel.X, Is.EqualTo(expected).Within(tolerance), "A dark float colour must survive the renderer's 8-bit vertex conversion.");
                Assert.That(texturePixel.X, Is.EqualTo(referencePixel.X).Within(tolerance), "The native equal-input pair must remain equal without fitting scene exposure.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCaseSource(nameof(OverlapCases))]
    public void PremultipliedOverlap_MatchesEquivalentIndependentEmitters(byte gray, int count)
    {
        var native = Fixture().GetProperty("Overlap").EnumerateArray().Single(item =>
            item.GetProperty("Texel").GetByte() == gray && item.GetProperty("Count").GetInt32() == count);
        Assert.That(native.GetProperty("MaximumChannelError").GetSingle(), Is.Zero);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var colour = new Vector3(.35f, .2f, .1f);
            var sameEmitter = Render(graphics.GraphicsDevice, Source(colour, true, 1, 1, count / 2f), Texture(gray, 128), count);
            var independent = Render(graphics.GraphicsDevice, Source(colour, true, 1, count, .5f), Texture(gray, 128), count);
            Assert.Multiple(() =>
            {
                Assert.That(sameEmitter.X, Is.EqualTo(independent.X).Within(.00001));
                Assert.That(sameEmitter.Y, Is.EqualTo(independent.Y).Within(.00001));
                Assert.That(sameEmitter.Z, Is.EqualTo(independent.Z).Within(.00001), "Emission from an earlier particle must remain behind later particle coverage.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(128, 0, .5f, 2f)]
    [TestCase(128, 0, .5f, .25f)]
    [TestCase(128, 0, 2f, 2f)]
    [TestCase(128, 0, 8f, 2f)]
    [TestCase(255, 128, 128 / 255f, 2f)]
    [TestCase(255, 128, 2f, .25f)]
    public void PremultipliedEmission_UsesTextureLuminanceBeforeParticleTint(int gray, int alpha, float vertexAlpha, float additive)
    {
        static float Linear(int value)
        {
            var c = value / 255f;
            return c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
        }
        var colour = new Vector3(.2f, .6f, 1);
        var diffuse = Linear(gray); var textureAlpha = Linear(alpha);
        var emission = Math.Max(diffuse - textureAlpha, 0);
        var opacity = Math.Clamp(textureAlpha * vertexAlpha, 0, 1);
        var covered = Math.Min(diffuse, textureAlpha) * Math.Clamp(vertexAlpha, 0, 1);
        var emitted = emission * additive * Math.Clamp(emission * vertexAlpha, 0, 1) * Math.Clamp(additive, 0, 1);
        var background = new Color(new Vector4(.1f, .2f, .4f, 1)).ToVector4();
        var expected = new Vector3(background.X, background.Y, background.Z) * (1 - opacity) + colour * (covered + emitted);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var actual = Render(graphics.GraphicsDevice, Source(colour, true, additive, alpha: vertexAlpha), Texture((byte)gray, (byte)alpha), 1);
            Assert.That(Vector3.Distance(new(actual.X, actual.Y, actual.Z), expected), Is.LessThan(.0015),
                "Default-pass emission keeps its own luminance shape and multiplier saturation, separate from covered opacity and particle tint.");
        }
        finally { graphics.Release(); }
    }

    private static Vector4 Render(GraphicsDevice device, byte[] source, byte[] texture, int particles)
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(source, "vfx/material.xml", _ => texture));
        Assert.That(preview.EmitterCount, Is.EqualTo(preview.TotalEmitterCount), JsonSerializer.Serialize(preview.Diagnostics));
        preview.Seek(3, Matrix.Identity);
        Assert.That(preview.ParticleCount, Is.EqualTo(particles));
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(new Color(new Vector4(.1f, .2f, .4f, 1))); device.DepthStencilState = DepthStencilState.Default;
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(20, 20, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        preview.Draw(camera); device.SetRenderTarget(null);
        var pixels = new Vector4[64 * 64]; target.GetData(pixels);
        return pixels[32 * 64 + 32];
    }

    private static JsonElement Fixture()
    {
        var assembly = typeof(VfxMaterialCompositingTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeMaterialCompositing.json", StringComparison.Ordinal)))!;
        using var fixture = JsonDocument.Parse(stream);
        return fixture.RootElement.Clone();
    }

    internal static byte[] Source(Vector3 colour, bool premultiplied, float additive, int emitters = 1, float rate = .5f, float alpha = 1)
    {
        var definitions = new XElement("emitters"); var behaviours = new XElement("behaviour_chains");
        for (var index = 0; index < emitters; index++)
        {
            var id = "material" + index;
            definitions.Add(new XElement("emitter", new XAttribute("id", id), new XAttribute("type", "continuous_mesh"),
                new XElement("property", new XAttribute("name", "InfiniteLife"), new XAttribute("value", "true")),
                Property("EmissionRateCurve", Curve("x", rate)), Property("ParticleLifeTime", Curve("x", 2)),
                Property("ParticleScale", Channels(("x", 5), ("y", 5), ("z", 1)))));
            behaviours.Add(new XElement("behaviour_chain", new XAttribute("id", id), new XElement("quality", new XAttribute("value", 0),
                new XElement("modifier", new XAttribute("fragment", "vs_translation")),
                new XElement("modifier", new XAttribute("fragment", "vs_colour"), Property("curve_colour", Channels(("r", colour.X), ("g", colour.Y), ("b", colour.Z), ("brightness", 1)))),
                new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"), Property("curve_alpha", Curve("alpha", alpha))),
                new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                    new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("type", "file"), new XAttribute("path", "vfx/material.dds")),
                    new XElement("property", new XAttribute("name", "pre_multiplied_alpha"), new XAttribute("type", "bool"), new XAttribute("value", premultiplied ? "true" : "false"))),
                new XElement("modifier", new XAttribute("fragment", "ps_lighting"), new XElement("property", new XAttribute("name", "additive_blend_mul"), new XAttribute("type", "float"), new XAttribute("value", additive.ToString(CultureInfo.InvariantCulture)))))));
        }
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), definitions, behaviours).ToString(SaveOptions.DisableFormatting));
    }

    private static XElement Property(string name, string curve) => new("property", new XAttribute("name", name), curve);
    private static string Curve(string axis, float value) => Channels((axis, value));
    private static string Channels(params (string Name, float Value)[] channels) =>
        string.Join("|", channels.Select(channel => $"{channel.Name},0,0;0,{channel.Value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":" +
        string.Join("|", channels.Select((channel, index) => $"{channel.Name},0;{index},{index}"));

    internal static byte[] Texture(byte gray, byte alpha)
    {
        var bytes = new byte[128 + 64 * 64 * 4];
        void UInt(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        UInt(0, 0x20534444); UInt(4, 124); UInt(8, 0x100F); UInt(12, 64); UInt(16, 64); UInt(20, 256); UInt(28, 1);
        UInt(76, 32); UInt(80, 0x41); UInt(88, 32); UInt(92, 255); UInt(96, 65280); UInt(100, 16711680); UInt(104, 4278190080); UInt(108, 4096);
        for (var offset = 128; offset < bytes.Length; offset += 4)
        {
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = gray;
            bytes[offset + 3] = alpha;
        }
        return bytes;
    }
}

public class VfxMaterialCandidateTests
{
    [TestCase(true, 0, 1)]
    [TestCase(true, 1, 1)]
    [TestCase(false, 0, 1)]
    [TestCase(false, 1, 2)]
    public void Distortion_DoesNotUseTheColourPassAdditiveBlend(bool distortion, int additive, int expectedBlend)
    {
        var source = Shared.GameFormats.Vfx.VfxDocument.Read(VfxTranslationOrientationTests.Source(0, Vector3.Zero, distortion));
        source.Root.Descendants("property").Single(x => (string)x.Attribute("name") == "additive_blend_mul")
            .SetAttributeValue("value", additive);
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "vfx/distortion.xml", _ => [1]));
        Assert.That(conversion.Emitters.Single().Render.Blend, Is.EqualTo(expectedBlend));
    }

    [Test]
    public void AlphaAboveOne_ReportsTheUncalibratedSharpeningBoundary()
    {
        var source = VfxMaterialCompositingTests.Source(Vector3.One, true, 1, alpha: 1.645606f);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/material.xml", _ => VfxMaterialCompositingTests.Texture(128, 128)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Diagnostics.Any(diagnostic => diagnostic.Field == "curve_alpha" && diagnostic.ReasonKey == "Vfx.Preview.AlphaSharpeningCandidate"), Is.True);
    }
}
