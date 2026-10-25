#nullable enable
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;
using Color = Microsoft.Xna.Framework.Color;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxNormalUvFlipPixelTests
{
    private const int Width = 128, Height = 96;
    private const byte NormalA = 218, NormalG = 203;
    private const float Tolerance = .000005f;

    [SetUp]
    public void RequireOffscreenOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [TestCase(false, false, true)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void LitNormalUvMirrors_MatchIndependentlyMirroredDxt5nChannels(bool flipU, bool flipV, bool reflected)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var camera = Camera(reflected, true);
            var actualSource = Source(flipU ? 1 : 0, flipV ? 1 : 0);
            var referenceSource = Source(0, 0);
            // Native decode is (byte + 0.5) * 2 / 255 - 1, so its exact sign mirror is 254-byte.
            var referenceA = flipU ? (byte)(254 - NormalA) : NormalA;
            var referenceG = flipV ? (byte)(254 - NormalG) : NormalG;
            var actual = Render(graphics.GraphicsDevice, actualSource, NormalA, NormalG, camera);
            var reference = Render(graphics.GraphicsDevice, referenceSource, referenceA, referenceG, camera);
            Assert.That(reference[48 * Width + 64].X, Is.GreaterThan(.0001f), "The directional-lighting control must be visible.");
            Assert.That(MaxDifference(actual, reference), Is.LessThanOrEqualTo(Tolerance),
                "Mirroring a sampled normal must change its lighting direction, not only its UV coordinates.");
            Assert.That(VfxDocument.Read(actualSource).Write(), Is.EqualTo(actualSource));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OneLayerWithMixedParticleMirrors_KeepsEachParticlesNormalSigns()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var camera = Camera(true, true);
            var source = Source(.5f, .5f, rate: 8, size: 1);
            using var preview = Preview(device, source, NormalA, NormalG);
            preview.Seek(.4f, Matrix.Identity);
            var simulation = (TotalWarVfxSimulation)typeof(EffekseerVfxPreview)
                .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            var raw = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(simulation)!).Cast<object>().ToArray();
            Assert.That(raw, Has.Length.EqualTo(4), "Exactly one native layer must contain all four sign combinations.");
            for (var index = 0; index < raw.Length; index++)
            {
                var position = new Vector3(-3 + index * 2, 0, 0);
                raw[index].GetType().GetField("Position")!.SetValue(raw[index], position);
                raw[index].GetType().GetField("MotionOrigin")!.SetValue(raw[index], position);
                raw[index].GetType().GetField("RandomAlpha")!.SetValue(raw[index], RandomFor((index & 1) != 0, (index & 2) != 0));
            }
            var frame = simulation.Frame(camera);
            Assert.That(frame.Select(p => (p.UV.Z < 0, p.UV.W < 0)).Distinct().Count(), Is.EqualTo(4));
            var actual = Draw(device, preview, camera);
            for (var index = 0; index < raw.Length; index++)
            {
                var flipU = (index & 1) != 0; var flipV = (index & 2) != 0;
                var reference = Render(device, Source(0, 0, size: 1),
                    flipU ? (byte)(254 - NormalA) : NormalA, flipV ? (byte)(254 - NormalG) : NormalG, camera);
                var projected = new Viewport(0, 0, Width, Height).Project(new(-3 + index * 2, 0, 0), camera.Projection, camera.View, Matrix.Identity);
                Assert.That(Vector4.Distance(actual[(int)projected.Y * Width + (int)projected.X], reference[48 * Width + 64]),
                    Is.LessThanOrEqualTo(Tolerance), $"Particle {index}: U={flipU}, V={flipV}; one batch must not reuse another particle's signs.");
            }
        }
        finally { graphics.Release(); }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void MissingNormalOrWeather_PreservesItsExistingColourOutput(bool normal, bool weather)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var camera = Camera(true, weather);
            var reference = Render(graphics.GraphicsDevice, Source(0, 0, normal), NormalA, NormalG, camera);
            Assert.That(reference[48 * Width + 64].X, Is.GreaterThan(.0001f));
            foreach (var (u, v) in new[] { (0, 1), (1, 0), (1, 1) })
            {
                var actual = Render(graphics.GraphicsDevice, Source(u, v, normal), NormalA, NormalG, camera);
                Assert.That(MaxDifference(actual, reference), Is.LessThanOrEqualTo(Tolerance));
            }
        }
        finally { graphics.Release(); }
    }

    private static float RandomFor(bool u, bool v)
    {
        for (var index = 1; index < 1024; index++)
        {
            var random = index / 1024f;
            var a = random * 251; var b = random * 149;
            if ((a - MathF.Floor(a) > .5f) == u && (b - MathF.Floor(b) > .5f) == v) return random;
        }
        throw new InvalidOperationException("No deterministic random value for the native flip control.");
    }

    private static float MaxDifference(Vector4[] a, Vector4[] b) => a.Zip(b).Max(p => Vector4.Distance(p.First, p.Second));

    private static Vector4[] Render(GraphicsDevice device, byte[] source, byte a, byte g, CommonShaderParameters camera)
    {
        using var preview = Preview(device, source, a, g); preview.Seek(.25f, Matrix.Identity);
        return Draw(device, preview, camera);
    }

    private static EffekseerVfxPreview Preview(GraphicsDevice device, byte[] source, byte a, byte g)
    {
        var normal = Texture(0, a);
        for (var offset = 128; offset < normal.Length; offset += 4) normal[offset + 1] = g;
        var snapshot = new VfxPreviewSnapshot(source, "diagnostic/normal-uv-flip.xml",
            path => path.EndsWith("normal.dds", StringComparison.Ordinal) ? normal : Texture(255, 255));
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Render.LightingMode, Is.EqualTo(2));
        var normalRequested = XElement.Parse(Encoding.UTF8.GetString(source)).Descendants("texture_property")
            .Any(t => (string?)t.Attribute("name") == "normal");
        Assert.That(conversion.Emitters[0].Render.NormalIndex >= 0, Is.EqualTo(normalRequested),
            "The sign control must exercise an actually uploaded normal texture.");
        var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot);
        return preview;
    }

    private static Vector4[] Draw(GraphicsDevice device, EffekseerVfxPreview preview, CommonShaderParameters camera)
    {
        using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
        preview.Draw(camera); device.SetRenderTarget(null);
        var pixels = new Vector4[Width * Height]; target.GetData(pixels); return pixels;
    }

    private static CommonShaderParameters Camera(bool reflected, bool weather)
    {
        var eye = new Vector3(0, 0, 10);
        GameEnvironmentLighting? lighting = null;
        if (weather)
        {
            var assembly = typeof(VfxNormalUvFlipPixelTests).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("NativeSmokeLighting.json", StringComparison.Ordinal)))!;
            using var document = JsonDocument.Parse(stream);
            lighting = GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes(document.RootElement.GetProperty("EnvironmentXml").GetString()!), "diagnostic/native-smoke.environment");
        }
        return new(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(10, 7.5f, .1f, 100) * Matrix.CreateScale(reflected ? -1 : 1, 1, 1),
            eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], GameWeather: lighting);
    }

    private static byte[] Source(float flipU, float flipV, bool normal = true, float rate = .1f, float size = 4)
    {
        XElement Property(string name, object value, string? type = null) => new("property", new XAttribute("name", name),
            new XAttribute("type", type ?? "float"), new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
        string Curve(string axes, float value) => string.Join('|', axes.Select(a => $"{a}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1"))
            + ":" + string.Join('|', axes.Select((a, i) => $"{a},0;{i},{i}"));
        var quality = new XElement("quality", new XAttribute("value", 0),
            new XElement("modifier", new XAttribute("fragment", "vs_translation")),
            new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "diagnostic/white.dds")),
                Property("pre_multiplied_alpha", "true", "bool")),
            new XElement("modifier", new XAttribute("fragment", "vs_flip_uv"), Property("flip_u", flipU), Property("flip_v", flipV)),
            new XElement("modifier", new XAttribute("fragment", "ps_lighting"), Property("additive_blend_mul", 0), Property("emissive", 0),
                Property("small_particles", .5f), Property("large_particles", 0), Property("directionality", .4f), Property("average_size", 1)));
        if (normal)
        {
            quality.Elements("modifier").Single(m => (string?)m.Attribute("fragment") == "ps_texture")
                .Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", "diagnostic/normal.dds")));
            quality.Add(new XElement("modifier", new XAttribute("fragment", "ps_normal")));
        }
        var root = new XElement("vfx", new XAttribute("revision", 2),
            new XElement("emitters", new XElement("emitter", new XAttribute("id", "flip"), new XAttribute("type", "continuous_mesh"),
                Property("InfiniteLife", "true", "bool"), new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve("x", rate)),
                new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve("x", 4)),
                new XElement("property", new XAttribute("name", "ParticleScale"), Curve("xyz", size)))),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "flip"), quality)));
        return Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
    }

    private static byte[] Texture(byte gray, byte alpha)
    {
        var bytes = new byte[128 + 16 * 16 * 4];
        void UInt(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        UInt(0, 0x20534444); UInt(4, 124); UInt(8, 0x100F); UInt(12, 16); UInt(16, 16); UInt(20, 64); UInt(28, 1);
        UInt(76, 32); UInt(80, 0x41); UInt(88, 32); UInt(92, 255); UInt(96, 65280); UInt(100, 16711680); UInt(104, 4278190080); UInt(108, 4096);
        for (var offset = 128; offset < bytes.Length; offset += 4)
        {
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = gray;
            bytes[offset + 3] = alpha;
        }
        return bytes;
    }
}
