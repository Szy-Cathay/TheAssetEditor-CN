#nullable enable
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class GameEnvironmentExposureMetadataTests
{
    [TestCase("false")]
    [TestCase("true")]
    public void ExposureMetadata_IsReadWithoutTreatingTheLegacyEnabledFlagAsABypass(string legacyEnabled)
    {
        var root = WeatherRoot(); var tone = root.Element("tone_mapping")!;
        tone.SetAttributeValue("enabled", legacyEnabled); tone.SetAttributeValue("brightness", "2");
        var bytes = Encoding.UTF8.GetBytes(root.ToString()); var hash = SHA256.HashData(bytes);
        var weather = GameEnvironmentLighting.Read(bytes, "weather/exposure.environment");
        Assert.Multiple(() =>
        {
            Assert.That(weather.ToneMappingBrightness, Is.EqualTo(2));
            Assert.That(weather.ToneMappingBurn, Is.EqualTo(.05f));
            Assert.That(weather.AutomaticExposure, Is.False);
            Assert.That(weather.AutomaticExposureTarget, Is.EqualTo(-1));
            Assert.That(weather.SourceSha256, Is.EqualTo(Convert.ToHexStringLower(hash)));
            Assert.That(SHA256.HashData(bytes), Is.EqualTo(hash));
        });
    }

    [TestCase("0", false)]
    [TestCase("1", true)]
    public void AutomaticExposure_AcceptsXmlBooleanValues(string text, bool expected)
    {
        var root = WeatherRoot(); root.Element("tone_mapping")!.SetAttributeValue("auto_exposure", text);
        Assert.That(Read(root).AutomaticExposure, Is.EqualTo(expected));
    }

    [Test]
    public void MissingExposureSettings_RemainUnknownInsteadOfInventingFixedExposure()
    {
        var root = WeatherRoot(); root.Element("tone_mapping")!.Remove(); var weather = Read(root);
        Assert.Multiple(() =>
        {
            Assert.That(weather.ToneMappingBrightness, Is.Null); Assert.That(weather.ToneMappingBurn, Is.Null);
            Assert.That(weather.AutomaticExposure, Is.Null); Assert.That(weather.AutomaticExposureTarget, Is.Null);
            Assert.That(weather.SunScale, Is.EqualTo(20000));
        });
    }

    [TestCase("brightness", "NaN")]
    [TestCase("burn", "Infinity")]
    [TestCase("target_exposure", "-Infinity")]
    [TestCase("auto_exposure", "unknown")]
    public void MalformedExposure_DoesNotReplaceThePreviousReadOnlyWeather(string field, string text)
    {
        var root = WeatherRoot(); var store = new GameWeatherStore(); var previous = Read(root); store.Set(previous);
        root.Element("tone_mapping")!.SetAttributeValue(field, text);
        Assert.Throws<InvalidDataException>(() => store.Set(Read(root)));
        Assert.That(store.Current, Is.SameAs(previous));
    }

    internal static XElement WeatherRoot() => XElement.Parse(GameEnvironmentLightingTests.Fixture().GetProperty("EnvironmentXml").GetString()!);
    internal static GameEnvironmentLighting Read(XElement root) => GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(root.ToString()), "weather/exposure.environment");

    [Test]
    public void BloomMetadata_PreservesExplicitZeroAndMissingValues()
    {
        var root = WeatherRoot(); root.Element("bloom")?.Remove();
        Assert.That(Read(root).BloomStrength, Is.Null);
        root.Add(new XElement("bloom", new XAttribute("bloom_threshold", "-1"), new XAttribute("bloom_strength", "0")));
        var weather = Read(root);
        Assert.Multiple(() =>
        {
            Assert.That(weather.BloomThreshold, Is.EqualTo(-1));
            Assert.That(weather.BloomStrength, Is.Zero);
            Assert.That(weather.BloomParticleStrength, Is.Null);
        });
    }

    [TestCase("bloom_threshold", "NaN")]
    [TestCase("bloom_strength", "Infinity")]
    [TestCase("bloom_particle_strength", "-1")]
    [TestCase("bloom_emissive_strength", "bad")]
    [TestCase("bloom_water_strength", "1000001")]
    public void InvalidBloom_DoesNotReplaceThePreviousWeather(string field, string value)
    {
        var root = WeatherRoot(); var store = new GameWeatherStore(); var previous = Read(root); store.Set(previous);
        root.Element("bloom")?.Remove(); root.Add(new XElement("bloom", new XAttribute(field, value)));
        Assert.Throws<InvalidDataException>(() => store.Set(Read(root)));
        Assert.That(store.Current, Is.SameAs(previous));
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxExposureBoundaryTests
{
    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
    }

    [TestCase(.5f)]
    [TestCase(1f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void UncalibratedFinalExposure_DoesNotBecomeAPerParticleGainOrChangeOpacity(float brightness)
    {
        var source = VfxSimpleEmissiveTests.Source(5, false, new(.04f), additive: .5f, premultiplied: true);
        var hash = SHA256.HashData(source); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(source, "vfx/exposure-boundary.xml", _ => VfxMaterialCompositingTests.Texture(255, 128)));
            preview.Seek(3, Matrix.Identity); Assert.That(preview.ParticleCount, Is.EqualTo(1));
            var root = GameEnvironmentExposureMetadataTests.WeatherRoot();
            var baselineWeather = GameEnvironmentExposureMetadataTests.Read(root);
            var baseline = Render(preview, graphics.GraphicsDevice, baselineWeather);
            root.Element("tone_mapping")!.SetAttributeValue("brightness", brightness.ToString(CultureInfo.InvariantCulture));
            root.Element("tone_mapping")!.SetAttributeValue("target_exposure", "0");
            var changedWeather = GameEnvironmentExposureMetadataTests.Read(root);
            var changed = Render(preview, graphics.GraphicsDevice, changedWeather);
            Render(preview, graphics.GraphicsDevice, changedWeather);
            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.EqualTo(baseline), "Opaque native equivalence does not justify a material gain for transparent game samples.");
                Assert.That(changedWeather.ToneMappingBrightness, Is.EqualTo(brightness));
                Assert.That(preview.Diagnostics.Count(x => x.ReasonKey == "Vfx.Preview.WeatherExposureUnsupported"), Is.EqualTo(1));
            });
            var cleared = Render(preview, graphics.GraphicsDevice, null);
            Assert.That(preview.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.WeatherExposureUnsupported"), Is.False);
            Assert.That(Vector4.Distance(cleared, baseline), Is.GreaterThan(.01f), "Existing weather lighting must still be active.");
            var restored = Render(preview, graphics.GraphicsDevice, baselineWeather);
            Assert.That(restored, Is.EqualTo(baseline));
            Assert.That(SHA256.HashData(source), Is.EqualTo(hash));
        }
        finally { graphics.Release(); }
    }

    private static Vector4 Render(EffekseerVfxPreview preview, GraphicsDevice device, GameEnvironmentLighting? weather)
    {
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
        preview.Draw(new(Matrix.CreateLookAt(new(0, 0, -10), Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(2, 2, .1f, 50),
            new(0, 0, -10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], GameWeather: weather));
        device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels); return pixels[32 * 64 + 32];
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxDisplayTonePixelTests
{
    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
    }

    // Shipped hdr_to_screen and default LUT; these neutral values were also observed in Terry controls.
    [TestCase(.01f, 28)]
    [TestCase(.1f, 85)]
    [TestCase(.5f, 157)]
    [TestCase(1f, 193)]
    [TestCase(2f, 225)]
    public void DisplayTarget_UsesNativeFinalTransferAfterComposition(float value, int expected)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixels = Render(graphics.GraphicsDevice, new(value), Color.Black);
            Assert.That((int)pixels[32 * 64 + 32].R, Is.EqualTo(expected).Within(1));
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void DisplayTransfer_PreservesAlreadyDisplayedBackgroundAndTargetAlpha(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var background = new Color(51, 82, 140, 191);
            var pixels = Render(graphics.GraphicsDevice, new(4, 1, .1f), background, samples: samples);
            Assert.That(pixels[2 * 64 + 2], Is.EqualTo(background), "An unaffected scene pixel must not be tone mapped twice.");
            Assert.That(pixels[32 * 64 + 32].A, Is.EqualTo(255));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void FixedWeatherExposure_IsAppliedToTheComposedDisplayButNotTheLinearMaterial()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var root = GameEnvironmentExposureMetadataTests.WeatherRoot();
            root.Element("tone_mapping")!.SetAttributeValue("brightness", ".5");
            var dim = Render(graphics.GraphicsDevice, new(.5f), Color.Black, weather: GameEnvironmentExposureMetadataTests.Read(root));
            root.Element("tone_mapping")!.SetAttributeValue("brightness", "2");
            var bright = Render(graphics.GraphicsDevice, new(.5f), Color.Black, weather: GameEnvironmentExposureMetadataTests.Read(root));
            Assert.That((int)dim[2080].R, Is.EqualTo(94).Within(1));
            Assert.That((int)bright[2080].R, Is.EqualTo(225).Within(1));
        }
        finally { graphics.Release(); }
    }

    [TestCase(1, 140)]
    [TestCase(2, 153)]
    public void TransparentParticles_ComposeOverDisplayedBackgroundBeforeToneMapping(int copies, int expected)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            // Terry: linear background .2 -> 113; one/two .8 alpha-.25 surfaces -> 140/153.
            var pixels = Render(graphics.GraphicsDevice, new(.8f), new Color(113, 113, 113), alpha: .25f, copies: copies);
            Assert.That((int)pixels[2080].R, Is.EqualTo(expected).Within(1),
                "Separate effect draws must reconstruct the displayed destination before composing the next particle.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeWarmColour_PreservesHueThroughFinalTransfer()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var pixel = Render(graphics.GraphicsDevice, new(1, .2f, .05f), Color.Black)[2080];
            Assert.Multiple(() =>
            {
                Assert.That((int)pixel.R, Is.EqualTo(227).Within(1));
                Assert.That((int)pixel.G, Is.EqualTo(107).Within(1));
                Assert.That((int)pixel.B, Is.EqualTo(54).Within(1));
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void UniformBloom_IsAddedBeforeExposureWithNativeFiveLevelEnergy(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            // Terry uniform emission: .25 HDR -> 123; T=0, common gain=.1 -> .28125 HDR -> 129.
            var off = Render(graphics.GraphicsDevice, new(.25f), Color.Black, samples, BloomWeather(0, 0), span: 2);
            var on = Render(graphics.GraphicsDevice, new(.25f), Color.Black, samples, BloomWeather(0, .1f), span: 2);
            Assert.Multiple(() =>
            {
                Assert.That((int)off[2080].R, Is.EqualTo(123).Within(1));
                Assert.That((int)on[2080].R, Is.EqualTo(129).Within(1));
                Assert.That(on[2080].A, Is.EqualTo(off[2080].A));
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void Bloom_SpreadsBeyondTheBrightParticleButDoesNotBloomTheExistingBackground()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var below = Render(graphics.GraphicsDevice, new(2), Color.Black, weather: BloomWeather(4, .1f));
            var bright = Render(graphics.GraphicsDevice, new(8), Color.Black, weather: BloomWeather(4, .1f));
            var background = new Color(240, 240, 240, 191);
            var occluder = Render(graphics.GraphicsDevice, Vector3.Zero, background, weather: BloomWeather(0, .1f));
            Assert.Multiple(() =>
            {
                Assert.That(below[32 * 64 + 16].R, Is.Zero);
                Assert.That(bright[32 * 64 + 16].R, Is.GreaterThan(10), "The glow must extend beyond the original geometry.");
                Assert.That(occluder[2 * 64 + 2], Is.EqualTo(background), "The existing display is not a new bloom source.");
                Assert.That(occluder[2080].R, Is.Zero, "A dark occluder must not inherit a halo from the decoded background.");
            });
        }
        finally { graphics.Release(); }
    }

    internal static GameEnvironmentLighting BloomWeather(float threshold, float strength)
    {
        var root = GameEnvironmentExposureMetadataTests.WeatherRoot(); root.Element("bloom")?.Remove();
        var bloom = new XElement("bloom", new XAttribute("bloom_threshold", threshold.ToString(CultureInfo.InvariantCulture)));
        foreach (var field in new[] { "bloom_strength", "bloom_particle_strength", "bloom_emissive_strength", "bloom_water_strength" })
            bloom.SetAttributeValue(field, strength.ToString(CultureInfo.InvariantCulture));
        root.Add(bloom); return GameEnvironmentExposureMetadataTests.Read(root);
    }

    internal static Color[] Render(GraphicsDevice device, Vector3 colour, Color background, int samples = 0,
        GameEnvironmentLighting? weather = null, float alpha = 1, int copies = 1, float span = 20)
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(VfxMaterialCompositingTests.Source(colour, false, 0, alpha: alpha),
            "vfx/display-tone.xml", _ => VfxMaterialCompositingTests.Texture(255, 255)));
        preview.Seek(3, Matrix.Identity);
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target); device.Clear(background); device.DepthStencilState = DepthStencilState.Default;
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(span, span, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], GameWeather: weather);
        for (var index = 0; index < copies; index++) preview.Draw(camera);
        device.SetRenderTarget(null); var pixels = new Color[4096]; target.GetData(pixels); return pixels;
    }
}
