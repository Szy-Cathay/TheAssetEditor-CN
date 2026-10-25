#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class GameEnvironmentLightingTests
{
    [Test]
    public void NativeWeather_DecodesLinearFacesAndCorrectedSunDirectionWithoutChangingBytes()
    {
        var fixture = Fixture(); var bytes = Encoding.UTF8.GetBytes(fixture.GetProperty("EnvironmentXml").GetString()!);
        var weather = GameEnvironmentLighting.Read(bytes, "weather/control.environment");
        Assert.Multiple(() =>
        {
            Assert.That(weather.SourceSha256, Is.EqualTo(fixture.GetProperty("EnvironmentSha256").GetString()));
            Assert.That(weather.SourceSha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.That(weather.PositiveX.X, Is.EqualTo(.3185468f).Within(1e-6));
            Assert.That(weather.NegativeX.Y, Is.EqualTo(.21404114f).Within(1e-6));
            Assert.That(weather.SunDirection.X, Is.EqualTo(MathF.Sqrt(.75f)).Within(1e-6));
            Assert.That(weather.SunDirection.Y, Is.EqualTo(.5f).Within(1e-6));
            Assert.That(weather.SunDirection.Z, Is.Zero.Within(1e-6));
            Assert.That(weather.SunScale, Is.EqualTo(20000));
        });
    }

    [TestCase("sun_colour_scale", "NaN")]
    [TestCase("ambient_scale", "-1")]
    [TestCase("sun_direction_azimuth", "Infinity")]
    public void InvalidWeather_DoesNotReplaceTheSharedSelection(string field, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(Fixture().GetProperty("EnvironmentXml").GetString()!);
        var store = new GameWeatherStore(); var previous = GameEnvironmentLighting.Read(bytes, "original"); store.Set(previous);
        var root = XElement.Parse(Encoding.UTF8.GetString(bytes)); root.Element("lighting")!.SetAttributeValue(field, value);
        Assert.Throws<InvalidDataException>(() => store.Set(GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(root.ToString()), "invalid")));
        Assert.That(store.Current, Is.SameAs(previous));
    }

    [Test]
    public void SharedWeather_TravelsThroughTheSceneBuilderAndClearsForAllConsumers()
    {
        var shared = new GameWeatherStore(); var first = new SceneRenderParametersStore(shared); var second = new SceneRenderParametersStore(shared);
        var camera = new ArcBallCamera(Mock.Of<IDeviceResolver>(), Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        camera.ProjectionMatrixOverride = Matrix.Identity;
        var weather = GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(Fixture().GetProperty("EnvironmentXml").GetString()!), "control");
        shared.Set(weather);
        Assert.That(CommonShaderParameterBuilder.Build(camera, first).GameWeather, Is.SameAs(weather));
        Assert.That(CommonShaderParameterBuilder.Build(camera, second).GameWeather, Is.SameAs(weather));
        Assert.That(new SceneRenderParametersStore().GameWeather, Is.Null);
        shared.Set(null);
        Assert.That(CommonShaderParameterBuilder.Build(camera, first).GameWeather, Is.Null);
        Assert.That(CommonShaderParameterBuilder.Build(camera, second).GameWeather, Is.Null);
    }

    [Test]
    public void GeneratedAmbientOrRemappedIrradiance_IsRejected()
    {
        var root = XElement.Parse(Fixture().GetProperty("EnvironmentXml").GetString()!);
        root.Element("lighting")!.SetAttributeValue("use_custom_ambient_cube_colours", "false");
        Assert.Throws<NotSupportedException>(() => GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(root.ToString()), "unsupported"));
    }

    internal static JsonElement Fixture()
    {
        var assembly = typeof(GameEnvironmentLightingTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeCombinedLighting.json", StringComparison.Ordinal)))!;
        using var doc = JsonDocument.Parse(stream); return doc.RootElement.Clone();
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxWeatherLightingTests
{
    [SetUp]
    public void RequireExplicitOffscreenRendering()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Set ASSETEDITOR_VFX_HEADLESS_TEST=1 to opt into native offscreen rendering.");
    }

    public static IEnumerable<TestCaseData> NativeCases() => GameEnvironmentLightingTests.Fixture().GetProperty("Cases").EnumerateArray()
        .Select(item => new TestCaseData(item.Clone()).SetName("WeatherKernel_" + item.GetProperty("Profile").GetString() + "_" + item.GetProperty("Index").GetInt32()));

    [TestCaseSource(nameof(NativeCases))]
    public void CandidateInputs_MatchTheCapturedLightingKernelAtBothCameraRotations(JsonElement item)
    {
        var source = Source(item); var originalHash = SHA256.HashData(source);
        // The older 8-bit equivalent-input fit remains historical evidence, not exact native radiance.
        var expected = CapturedKernelGain(item) * .04f;
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var parameters = Parameters(item.GetProperty("Yaw").GetSingle(), item.GetProperty("Pitch").GetSingle());
            var pixels = Render(graphics.GraphicsDevice, source, NormalTexture(item.GetProperty("Normal").GetString()!), parameters);
            var actual = pixels[32 * 64 + 32];
            Assert.Multiple(() =>
            {
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-5));
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-5));
                Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(1e-5));
                Assert.That(SHA256.HashData(source), Is.EqualTo(originalHash));
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NoWeather_PreservesTheExistingRawPreviewAndClearingRestoresIt()
    {
        var item = GameEnvironmentLightingTests.Fixture().GetProperty("Cases")[0]; var source = Source(item); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var p = Parameters(0, 0); var device = graphics.GraphicsDevice;
            var without = Render(device, source, NormalTexture("unset"), p with { GameWeather = null })[32 * 64 + 32];
            var lit = Render(device, source, NormalTexture("unset"), p)[32 * 64 + 32];
            var cleared = Render(device, source, NormalTexture("unset"), p with { GameWeather = null })[32 * 64 + 32];
            Assert.That(without.X, Is.EqualTo(.04f).Within(1e-6)); Assert.That(cleared, Is.EqualTo(without));
            Assert.That(lit.X, Is.GreaterThan(without.X + .01f));
        }
        finally { graphics.Release(); }
    }

    [TestCase("FULLRES_DEFERRED_ALPHA", false, 0f)]
    [TestCase("FULLRES_DEFERRED_ALPHA", false, 1f)]
    [TestCase("FULLRES_DEFERRED_ALPHA", true, .5f)]
    [TestCase("FULLRES_DEFERRED_ALPHA", true, 2.2f)]
    [TestCase("HALFRES_DEFERRED_ALPHA", false, 0f)]
    [TestCase("HALFRES_DEFERRED_ALPHA", false, 1f)]
    [TestCase("HALFRES_DEFERRED_ALPHA", true, .5f)]
    [TestCase("HALFRES_DEFERRED_ALPHA", true, 2.2f)]
    public void ExplicitAlphaPass_PreservesTheDefaultPassLighting(string pass, bool simple, float emissive)
    {
        var source = VfxSimpleEmissiveTests.Source(emissive, simple, new(.04f), pass: pass);
        var reference = VfxSimpleEmissiveTests.Source(emissive, simple, new(.04f));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var parameters = Parameters(0, 0); var normal = NormalTexture("unset");
            var expected = Render(graphics.GraphicsDevice, reference, normal, parameters);
            var actual = Render(graphics.GraphicsDevice, source, normal, parameters);
            Assert.That(actual.Select((pixel, index) => Vector4.Distance(pixel, expected[index])).Max(), Is.LessThan(1e-6),
                "Native full and half alpha passes share the lighting shader; their resolve and sampling remain separate concerns.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void SpatialNormalTexture_IsSampledPerPixelInLinearSpace()
    {
        var item = GameEnvironmentLightingTests.Fixture().GetProperty("Cases")[2]; var source = Source(item); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var left = NormalTexture("x-positive"); var right = NormalTexture("x-negative"); var split = left.ToArray();
            for (var y = 0; y < 64; y++) for (var x = 32; x < 64; x++) Array.Copy(right, 128 + (y * 64 + x) * 4, split, 128 + (y * 64 + x) * 4, 4);
            var parameters = Parameters(0, 0); var device = graphics.GraphicsDevice;
            var a = Render(device, source, left, parameters); var b = Render(device, source, right, parameters); var mixed = Render(device, source, split, parameters);
            Assert.That(Vector4.Distance(a[32 * 64 + 27], b[32 * 64 + 27]), Is.GreaterThan(.005f));
            foreach (var index in new[] { 32 * 64 + 27, 32 * 64 + 37 })
                Assert.That(Math.Min(Vector4.Distance(mixed[index], a[index]), Vector4.Distance(mixed[index], b[index])), Is.LessThan(1e-5));
            Assert.That(Vector4.Distance(mixed[32 * 64 + 27], mixed[32 * 64 + 37]), Is.GreaterThan(.005f));
        }
        finally { graphics.Release(); }
    }

    [TestCase("x-positive", "FULLRES_DEFERRED_ALPHA")]
    [TestCase("y-positive", "HALFRES_DEFERRED_ALPHA")]
    public void AuthoredTextureNormalSlot_ReachesTheSameLightingAsTheNormalModifier(string normalName, string pass)
    {
        var item = GameEnvironmentLightingTests.Fixture().GetProperty("Cases").EnumerateArray()
            .First(entry => entry.GetProperty("Normal").GetString() == normalName);
        var root = XElement.Parse(Encoding.UTF8.GetString(Source(item)));
        root.Descendants("effect_render_stack").Single().Add(new XElement("property", new XAttribute("name", "scene_view_type"),
            new XAttribute("quality", "0"), new XAttribute("value", pass)));
        var normalModifier = root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "ps_normal");
        var normal = normalModifier.Element("texture_property")!;
        var legacy = Encoding.UTF8.GetBytes(root.ToString());
        normal.Remove();
        root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "ps_texture").Add(normal);
        var source = Encoding.UTF8.GetBytes(root.ToString());
        var originalHash = SHA256.HashData(source);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var parameters = Parameters(0, 0);
            var expected = Render(graphics.GraphicsDevice, legacy, NormalTexture(normalName), parameters);
            var flat = Render(graphics.GraphicsDevice, legacy, NormalTexture("unset"), parameters);
            var actual = Render(graphics.GraphicsDevice, source, NormalTexture(normalName), parameters);
            Assert.Multiple(() =>
            {
                Assert.That(Vector4.Distance(expected[32 * 64 + 32], flat[32 * 64 + 32]), Is.GreaterThan(.005f));
                Assert.That(actual.Select((pixel, index) => Vector4.Distance(pixel, expected[index])).Max(), Is.LessThan(1e-6),
                    "Shipped ps_texture/normal must not silently fall back to a flat billboard normal.");
                Assert.That(SHA256.HashData(source), Is.EqualTo(originalHash));
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void ConflictingNormalSlots_AreDiagnosedInsteadOfChoosingOne()
    {
        var item = GameEnvironmentLightingTests.Fixture().GetProperty("Cases").EnumerateArray()
            .First(entry => entry.GetProperty("Normal").GetString() == "x-positive");
        var root = XElement.Parse(Encoding.UTF8.GetString(Source(item)));
        root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "ps_texture")
            .Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", "vfx/conflicting.dds")));
        var converted = TotalWarVfxConverter.Convert(new(Encoding.UTF8.GetBytes(root.ToString()), "vfx/conflict.xml", _ => NormalTexture("unset")));
        Assert.That(converted.Diagnostics.Any(item => item.Field == "normal" && item.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
    }

    [Test]
    public void Weather_ChangesOnlyPremultipliedCoverageAndPreservesTextureEmission()
    {
        var item = GameEnvironmentLightingTests.Fixture().GetProperty("Cases")[0];
        var root = XElement.Parse(Encoding.UTF8.GetString(Source(item)));
        root.Descendants("property").Single(x => (string?)x.Attribute("name") == "pre_multiplied_alpha").SetAttributeValue("value", "true");
        root.Descendants("property").Single(x => (string?)x.Attribute("name") == "additive_blend_mul").SetAttributeValue("value", "1");
        var source = Encoding.UTF8.GetBytes(root.ToString()); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(source, "vfx/emission.xml", _ => VfxMaterialCompositingTests.Texture(255, 128))); preview.Seek(3, Matrix.Identity);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
            preview.Draw(Parameters(0, 0)); device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels);
            var alpha = MathF.Pow((128 / 255f + .055f) / 1.055f, 2.4f);
            var gain = CapturedKernelGain(item);
            var actual = pixels[32 * 64 + 32];
            var emission = (1 - alpha) * (1 - alpha);
            Assert.That(actual.X, Is.EqualTo(.04f * (alpha * gain.X + emission)).Within(1e-5));
            Assert.That(actual.Y, Is.EqualTo(.04f * (alpha * gain.Y + emission)).Within(1e-5));
            Assert.That(actual.Z, Is.EqualTo(.04f * (alpha * gain.Z + emission)).Within(1e-5));
        }
        finally { graphics.Release(); }
    }

    private static Vector3 CapturedKernelGain(JsonElement item)
    {
        var parameters = Parameters(item.GetProperty("Yaw").GetSingle(), item.GetProperty("Pitch").GetSingle());
        var weather = parameters.GameWeather!;
        var emissive = item.GetProperty("Emissive").GetSingle();
        var weight = Math.Clamp(emissive, 0, 1);
        if (item.GetProperty("SimpleLighting").GetBoolean()) return weather.PositiveY * (1 - weight) + new Vector3(emissive * weight);
        var name = item.GetProperty("Normal").GetString()!;
        var normal = Vector3.UnitZ;
        if (name != "unset")
        {
            var bytes = NormalTexture(name);
            var x = (bytes[131] + .5f) / 255 * 2 - 1;
            var y = (bytes[129] + .5f) / 255 * 2 - 1;
            normal = Vector3.Normalize(new(-x, -y, MathF.Sqrt(Math.Max(0, 1 - x*x - y*y))));
        }
        var inverse = Matrix.Invert(parameters.View);
        normal = Vector3.TransformNormal(normal, inverse);
        var ambient = normal.X*normal.X * (normal.X >= 0 ? weather.PositiveX : weather.NegativeX)
            + normal.Y*normal.Y * (normal.Y >= 0 ? weather.PositiveY : weather.NegativeY)
            + normal.Z*normal.Z * (normal.Z >= 0 ? weather.PositiveZ : weather.NegativeZ);
        var small = item.GetProperty("Small").GetSingle(); var large = item.GetProperty("Large").GetSingle();
        var directionality = item.GetProperty("Directionality").GetSingle();
        var solar = 0f;
        if (small + large > 0)
        {
            var cosine = Vector3.Dot(weather.SunDirection, inverse.Backward);
            var transmission = MathF.Exp(-(small + large) * .55f * item.GetProperty("AverageSize").GetSingle()
                * (1 - Vector3.Dot(normal, weather.SunDirection)) / 2);
            var phase = (small * 3 / (16 * MathF.PI) * (1 + cosine*cosine)
                + large * (1-directionality)*(1-directionality) / (4 * MathF.PI * MathF.Pow(1 + directionality*directionality - 2*directionality*cosine, 1.5f))) / (small + large);
            solar = 7.615242066094652e-5f * weather.SunScale * Math.Clamp(transmission + phase*(1-transmission), 0, 1);
        }
        return ambient + weather.SunColour * solar * (1 - weight) + new Vector3(emissive * weight);
    }

    private static CommonShaderParameters Parameters(float yawDegrees, float pitchDegrees)
    {
        var yaw = MathHelper.ToRadians(yawDegrees); var pitch = MathHelper.ToRadians(pitchDegrees);
        var up = new Vector3(MathF.Sin(yaw) * MathF.Sin(pitch), MathF.Cos(pitch), MathF.Cos(yaw) * MathF.Sin(pitch));
        var forward = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        return new(Matrix.CreateLookAt(-10 * forward, Vector3.Zero, up), Matrix.CreateOrthographic(2, 2, .1f, 50), -10 * forward, Vector3.Zero,
            0, 0, 0, 1, Vector3.One, [], GameWeather: GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(GameEnvironmentLightingTests.Fixture().GetProperty("EnvironmentXml").GetString()!), "weather/control.environment"));
    }

    private static byte[] Source(JsonElement item)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(VfxSimpleEmissiveTests.Source(item.GetProperty("Emissive").GetSingle(), item.GetProperty("SimpleLighting").GetBoolean(), new(.04f))));
        var quality = root.Descendants("quality").Single();
        var light = quality.Elements("modifier").Single(x => (string?)x.Attribute("fragment") == "ps_lighting");
        foreach (var (field, key) in new[] { ("small_particles", "Small"), ("large_particles", "Large"), ("directionality", "Directionality"), ("average_size", "AverageSize") })
            light.Add(new XElement("property", new XAttribute("name", field), new XAttribute("type", "float"), new XAttribute("value", item.GetProperty(key).GetSingle())));
        var normal = item.GetProperty("Normal").GetString()!;
        if (normal != "unset") quality.Add(new XElement("modifier", new XAttribute("fragment", "ps_normal"), new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", "vfx/normal.dds"))));
        root.Descendants("emitter").Single().Elements("property").Single(x => (string?)x.Attribute("name") == "ParticleScale").Value =
            "x,0,0;0,0.55,linear,linear,0,0,0,0,1|y,0,0;0,0.55,linear,linear,0,0,0,0,1|z,0,0;0,0.55,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
        return Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
    }

    private static byte[] NormalTexture(string name)
    {
        var a = name == "x-positive" ? (byte)230 : name == "x-negative" ? (byte)25 : (byte)128;
        var g = name == "y-positive" ? (byte)25 : name == "y-negative" ? (byte)230 : (byte)128;
        var bytes = VfxMaterialCompositingTests.Texture(0, a);
        for (var offset = 128; offset < bytes.Length; offset += 4) bytes[offset + 1] = g;
        return bytes;
    }

    private static Vector4[] Render(GraphicsDevice device, byte[] source, byte[] normal, CommonShaderParameters parameters)
    {
        using var preview = new EffekseerVfxPreview(device);
        preview.Reload(new(source, "vfx/weather.xml", path => path.EndsWith("normal.dds", StringComparison.Ordinal) ? normal : VfxMaterialCompositingTests.Texture(255, 255)));
        Assert.That(preview.EmitterCount, Is.EqualTo(1), JsonSerializer.Serialize(preview.Diagnostics)); preview.Seek(3, Matrix.Identity);
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
        preview.Draw(parameters); device.SetRenderTarget(null); var pixels = new Vector4[4096]; target.GetData(pixels); return pixels;
    }
}
