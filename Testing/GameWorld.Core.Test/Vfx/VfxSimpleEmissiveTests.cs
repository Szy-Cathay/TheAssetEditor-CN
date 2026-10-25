#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
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
public class VfxSimpleEmissiveTests
{
    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
    }

    public static IEnumerable<TestCaseData> NativeGainCases()
    {
        foreach (var item in Fixture().GetProperty("DefaultPassCases").EnumerateArray())
            yield return new TestCaseData(item.GetProperty("Group").GetString(), item.GetProperty("Emissive").GetSingle(),
                item.GetProperty("Brightness").GetSingle(), item.GetProperty("Texel").GetByte(),
                item.GetProperty("TextureAlpha").GetByte(), item.GetProperty("Premultiplied").GetBoolean());
    }

    [TestCaseSource(nameof(NativeGainCases))]
    public void NativeDefaultPassGain_MatchesTheSameTextureAndOpacityReference(string group, float emissive,
        float brightness, byte gray, byte alpha, bool premultiplied)
    {
        var native = Fixture().GetProperty("DefaultPassCases").EnumerateArray().Single(item => item.GetProperty("Group").GetString() == group);
        Assert.That(native.GetProperty("MaximumChannelError").GetSingle(), Is.LessThanOrEqualTo(1));
        var source = Source(emissive, true, new(brightness), premultiplied: premultiplied);
        var reference = Source(1, true, new(brightness * emissive), premultiplied: premultiplied);
        var texture = VfxMaterialCompositingTests.Texture(gray, alpha);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var actual = Render(graphics.GraphicsDevice, source, texture, Vector3.One);
            var expected = Render(graphics.GraphicsDevice, reference, texture, Vector3.One);
            Assert.Multiple(() =>
            {
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(.00001), "Compare equal covered colour while retaining identical texture alpha and compositor inputs.");
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.00001));
                Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(.00001));
                Assert.That(actual.X, Is.GreaterThan(.005), "The comparison must contain measurable colour.");
                Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void FullyEmissiveCoveredColour_DoesNotAcquireTheViewportLightColour()
    {
        var source = Source(2.2f, true, new(.1f), premultiplied: true);
        var texture = VfxMaterialCompositingTests.Texture(128, 128);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var neutral = Render(graphics.GraphicsDevice, source, texture, Vector3.One);
            var coloured = Render(graphics.GraphicsDevice, source, texture, new(1, .1f, .01f));
            Assert.That(coloured, Is.EqualTo(neutral));
        }
        finally { graphics.Release(); }
    }

    [TestCase(10f, 4f, true)]
    [TestCase(2.2f, 4f, true)]
    [TestCase(10f, 0f, false)]
    public void FullyEmissiveGlow_PreservesIndependentCoverageAndTextureEmission(float emissive, float additive, bool premultiplied)
    {
        var colour = new Vector3(.02f, .015f, .01f);
        var source = Source(emissive, true, colour, additive, premultiplied: premultiplied);
        var emissionWeight = additive * Math.Clamp(additive, 0, 1) / emissive;
        var referenceAdditive = emissionWeight < 1 ? MathF.Sqrt(emissionWeight) : emissionWeight;
        var reference = Source(1, true, colour * emissive, referenceAdditive, premultiplied: premultiplied);
        var texture = VfxMaterialCompositingTests.Texture(255, 128);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var actual = Render(graphics.GraphicsDevice, source, texture, Vector3.One);
            var expected = Render(graphics.GraphicsDevice, reference, texture, Vector3.One);
            Assert.That(Vector4.Distance(actual, expected), Is.LessThan(.00001),
                "Covered gain and texture emission are independent; the reference retains additive times its saturated shape weight.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        }
        finally { graphics.Release(); }
    }

    private static Vector4 Render(GraphicsDevice device, byte[] source, byte[] texture, Vector3 lightColour)
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(source, "vfx/simple-emissive.xml", _ => texture));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics));
        preview.Seek(3, Matrix.Identity);
        Assert.That(preview.ParticleCount, Is.EqualTo(1));
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(20, 20, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, lightColour, []);
        preview.Draw(camera); device.SetRenderTarget(null);
        var pixels = new Vector4[64 * 64]; target.GetData(pixels);
        return pixels[32 * 64 + 32];
    }

    private static JsonElement Fixture()
    {
        var assembly = typeof(VfxSimpleEmissiveTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeSimpleEmissive.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    internal static byte[] Source(float emissive, bool simple, Vector3 colour, float additive = 0,
        string? pass = null, bool premultiplied = false)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(VfxMaterialCompositingTests.Source(colour, premultiplied, additive)));
        root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "ps_lighting").Add(
            new XElement("property", new XAttribute("name", "emissive"), new XAttribute("type", "float"),
                new XAttribute("value", emissive.ToString(CultureInfo.InvariantCulture))));
        var stack = new XElement("effect_render_stack", new XAttribute("id", "material0"),
            new XElement("property", new XAttribute("name", "enable_simple_lighting"), new XAttribute("quality", 0), new XAttribute("value", simple ? "true" : "false")));
        if (pass != null) stack.Add(new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("quality", 0), new XAttribute("value", pass)));
        root.Add(new XElement("effect_render_stacks", stack));
        return Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
    }
}

public class VfxSimpleEmissiveCandidateTests
{
    [TestCase(true, 1f, 0f, null, true)]
    [TestCase(true, 2.2f, 0f, null, true)]
    [TestCase(true, 8f, 0f, null, true)]
    [TestCase(true, .5f, 0f, null, false)]
    [TestCase(true, 9f, 0f, null, true)]
    [TestCase(true, 10f, 0f, null, true)]
    [TestCase(true, 10.1f, 0f, null, false)]
    [TestCase(false, 2.2f, 0f, null, false)]
    [TestCase(true, 2.2f, .5f, null, false)]
    [TestCase(true, 2.2f, 0f, "FULLRES_DEFERRED_ALPHA", true)]
    [TestCase(true, 2.2f, 0f, "HALFRES_DEFERRED_ALPHA", true)]
    [TestCase(true, 2.2f, 0f, "HIRES_VFXUI3D", false)]
    public void OnlyConfirmedAlphaPassLighting_ChangesItsDiagnostic(bool simple, float emissive, float additive, string? pass, bool calibrated)
    {
        var source = VfxSimpleEmissiveTests.Source(emissive, simple, new(.1f), additive, pass);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/simple-emissive.xml", _ => VfxMaterialCompositingTests.Texture(128, 128)));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(conversion.Diagnostics.Any(note => note.ReasonKey == "Vfx.Preview.SimpleEmissiveCalibrated"), Is.EqualTo(calibrated));
            Assert.That(conversion.Diagnostics.Any(note => note.ReasonKey == "Vfx.Preview.LightingCandidate"), Is.EqualTo(!calibrated));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase("0", false)]
    [TestCase("1", true)]
    public void SimpleLighting_UsesTheSelectedQualityOverride(string quality, bool calibrated)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(VfxSimpleEmissiveTests.Source(2.2f, false, new(.1f))));
        root.Descendants("effect_render_stack").Single().Add(new XElement("property", new XAttribute("name", "enable_simple_lighting"), new XAttribute("quality", 1), new XAttribute("value", "true")));
        var source = Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/simple-emissive.xml", _ => VfxMaterialCompositingTests.Texture(128, 128), quality));
        Assert.That(conversion.Diagnostics.Any(note => note.ReasonKey == "Vfx.Preview.SimpleEmissiveCalibrated"), Is.EqualTo(calibrated));
    }

    [TestCase(true, 2.2f, true)]
    [TestCase(true, 10f, true)]
    [TestCase(true, 10.1f, false)]
    [TestCase(false, 10f, false)]
    public void TextureEmission_RequiresTheConfirmedPremultipliedCoveragePath(bool premultiplied, float emissive, bool calibrated)
    {
        var source = VfxSimpleEmissiveTests.Source(emissive, true, new(.1f), 4, premultiplied: premultiplied);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/simple-emissive.xml", _ => VfxMaterialCompositingTests.Texture(255, 128)));
        Assert.That(conversion.Diagnostics.Any(note => note.ReasonKey == "Vfx.Preview.SimpleEmissiveCalibrated"), Is.EqualTo(calibrated));
    }
}
