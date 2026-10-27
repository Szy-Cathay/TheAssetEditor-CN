#nullable enable
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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

[Category("Diagnostic")]
public class VfxQuadraticScaleTests
{
    private const float Tolerance = .00001f;
    private const string OfficialHash = "B5CCB9B9CFA431C3E62FABA28EA0D396EB044BF4C6294B93E30DF5A4A453AE0A";
    private const string TexturePath = @"vfx\textures\smoke_billow2_diffuse.dds";
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    public static IEnumerable<TestCaseData> NativeCases()
    {
        using var fixture = Fixture();
        foreach (var item in fixture.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("NativeQuadraticScale_" + item.GetProperty("Name").GetString());
    }

    [TestCaseSource(nameof(NativeCases))]
    public void AllSixBakedScaleRows_MatchIndependentOfficialInstructionOutputs(JsonElement item)
    {
        var curve = VfxSpriteNativeScaleCurve.Create(item.GetProperty("Source").GetString()!);
        foreach (var axis in new[] { "x", "y", "z" })
        {
            var component = axis == "x" ? 0 : axis == "y" ? 1 : 2;
            foreach (var upper in new[] { false, true })
                for (var index = 0; index < 128; index++)
                    Assert.That(curve.SampleBoundAt(axis, index, upper),
                        Is.EqualTo(Texel(item, upper ? 1 : 0, index, component)),
                        $"{axis} row {(upper ? "max" : "min")} texel {index}");
        }
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void CompleteOfficialSiegeSmoke_KeepsItsCentralLayerWithoutChangingOriginalXml(string quality)
    {
        var bytes = Resource("OfficialSiegeSmokeLarge.xml");
        Assert.That(System.Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(OfficialHash));
        var conversion = TotalWarVfxConverter.Convert(new(bytes, @"vfx\wh3_main_seige_smoke_large.xml", _ => [1], quality));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Select(emitter => emitter.Name),
                Does.Contain("smoke_central_pillar"), "The complete original central emitter must survive scale conversion.");
            Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Does.Contain("smoke_base"));
            Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Does.Contain("smoke_end_fade"));
            Assert.That(conversion.Diagnostics.Any(d => d.Layer == "smoke_central_pillar"
                && d.Field == "ParticleScale" && d.ReasonKey == "Vfx.Preview.InterpolationUnsupported"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void OriginalUnorderedKeysAndQuadraticTokens_KeepTheirSerializedOrder()
    {
        using var fixture = Fixture(); var item = Central(fixture);
        var text = item.GetProperty("Source").GetString()!;
        Assert.That(VfxCurve.TryParse(text, out var curve), Is.True);
        Assert.That(curve!.ToString(), Is.EqualTo(text));
        Assert.That(curve.Channels[0].Points.Select(point => point.Time), Is.EqualTo(new[] { 0d, .568501d, 1d }));
        Assert.That(text.IndexOf(";1.0,", StringComparison.Ordinal), Is.LessThan(text.IndexOf(";0.568501,", StringComparison.Ordinal)));
    }

    [Test]
    public void UnauthoredColourCurve_KeepsItsExistingFallbackOutsideNativeScale()
    {
        var curve = new VfxPreviewCurve(null, "curve_colour", 1);
        Assert.That(curve.Sample("r", .5, .5f), Is.EqualTo(1));
    }

    [TestCase(.1f, 0f)]
    [TestCase(.35f, .375f)]
    [TestCase(.565f, .375f)]
    [TestCase(.9f, 1f)]
    public void CentralScale_PassesNativeDimensionsToTheSpriteTransform(float phase, float unary)
    {
        using var fixture = Fixture(); var item = Central(fixture);
        var bytes = Source(item.GetProperty("Source").GetString()!);
        var conversion = Convert(bytes);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(phase * 2, Matrix.Identity);
        SetUnary(simulation, unary);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Matrix.CreateScale(Sample(item, 0, phase, unary), Sample(item, 1, phase, unary), 1));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("ParticleScale", false)]
    [TestCase("ParticleInitialScale", true)]
    [TestCase("curve_alpha", false)]
    public void QuadraticScaleSupport_DoesNotOpenModelsInitialScaleOrOtherFields(string field, bool planar)
    {
        using var fixture = Fixture();
        var text = Central(fixture).GetProperty("Source").GetString();
        var error = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(text, field, 1, planar));
        Assert.That(error!.Reason, Is.EqualTo("Vfx.Preview.InterpolationUnsupported"));
    }

    [TestCase("exp1", "bezier_q")]
    [TestCase("bezier_q", "exp1")]
    [TestCase("future_mode", "linear")]
    public void UnprovedEndpointPairs_KeepTheirExplicitDiagnostic(string outgoing, string incoming)
    {
        var row = $"0,.1,linear,{outgoing},-.25,-.2,.25,1,1;1,2,{incoming},linear,-.25,-.2,.25,1,1";
        var channels = new[] { "x", "y", "z" }.SelectMany(axis => new[] { axis + "_min", axis + "_max" })
            .Select(name => name + ",0,0;" + row);
        var text = string.Join('|', channels) + ":x,1;0,1|y,1;2,3|z,1;4,5";
        var error = Assert.Throws<VfxMappingException>(() => VfxSpriteNativeScaleCurve.Create(text));
        Assert.That(error!.Reason, Is.EqualTo("Vfx.Preview.InterpolationUnsupported"));
    }

    [TestCase("wrong-binding")]
    [TestCase("split-zero-different-half")]
    public void UnprovedRawScaleBindings_KeepTheirExplicitDiagnostic(string layout)
    {
        using var fixture = Fixture();
        var text = Central(fixture).GetProperty("Source").GetString()!;
        text = layout == "wrong-binding" ? text.Replace("x,1;0,1", "x,1;1,0", StringComparison.Ordinal)
            : text.Replace("x,1;0,1", "x,0;0,1", StringComparison.Ordinal);
        var error = Assert.Throws<VfxMappingException>(() => VfxSpriteNativeScaleCurve.Create(text));
        Assert.That(error!.Reason, Is.EqualTo("Vfx.Preview.AmbiguousField"));
    }

    public static IEnumerable<TestCaseData> NativeHandleCases()
    {
        using var fixture = Fixture();
        foreach (var item in fixture.RootElement.GetProperty("QuadraticControls").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("NativeQuadraticHandles_"
                + item.GetProperty("OutgoingMode").GetInt32() + "_" + item.GetProperty("IncomingMode").GetInt32());
    }

    [TestCaseSource(nameof(NativeHandleCases))]
    public void QuadraticHandles_MatchTheNativeOriginalAndIndependentPerturbations(JsonElement item)
    {
        var original = VfxSpriteNativeScaleCurve.Create(item.GetProperty("OriginalSource").GetString()!);
        var incoming = VfxSpriteNativeScaleCurve.Create(item.GetProperty("ChangedRightIncomingSource").GetString()!);
        var outgoing = VfxSpriteNativeScaleCurve.Create(item.GetProperty("ChangedLeftOutgoingSource").GetString()!);
        var changed = false;
        foreach (var sample in new[] { 2, 3, 4 })
        {
            var index = (int)(item.GetProperty("SampleTimes")[sample].GetDouble() * 128);
            float Value(string name) => (float)BitConverter.UInt16BitsToHalf(item.GetProperty(name)[sample].GetUInt16());
            Assert.Multiple(() =>
            {
                Assert.That(original.SampleBoundAt("x", index, false), Is.EqualTo(Value("NativeHalfBits")));
                Assert.That(incoming.SampleBoundAt("x", index, false), Is.EqualTo(Value("ChangedRightIncomingHalfBits")));
                Assert.That(outgoing.SampleBoundAt("x", index, false), Is.EqualTo(Value("ChangedLeftOutgoingHalfBits")));
                Assert.That(Value("ChangedRightIncomingHalfBits"), Is.EqualTo(Value("NativeHalfBits")));
            });
            changed |= Value("ChangedLeftOutgoingHalfBits") != Value("NativeHalfBits");
        }
        Assert.That(changed, Is.True, "The independent native perturbation must distinguish the left outgoing control point.");
    }

    [TestCase(.35f, .375f)]
    [TestCase(.565f, .375f)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void CentralScale_ReachesTheGpuAsItsIndependentStaticSizeWithTheSameOfficialTexture(float phase, float unary)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        using var fixture = Fixture(); var item = Central(fixture);
        var actual = Source(item.GetProperty("Source").GetString()!);
        var expectedX = Sample(item, 0, phase, unary); var expectedY = Sample(item, 1, phase, unary);
        var reference = Source(VectorCurve(expectedX, expectedY));
        var texture = Resource("OfficialSiegeSmokeDiffuse.dds");
        Assert.That(System.Convert.ToHexString(SHA256.HashData(texture)),
            Is.EqualTo("18346CC4E31F527C4ABE1D92C287CD50591AA3E154E67B5815B44497E9AA4A70"));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var eye = new Vector3(0, 0, 60);
            var camera = Camera with
            {
                View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Projection = Matrix.CreateOrthographic(40, 40, .1f, 100),
                CameraPosition = eye
            };
            Vector4[] Render(byte[] source)
            {
                using var preview = new EffekseerVfxPreview(device);
                preview.Reload(new(source, "diagnostic/central-quadratic-scale.xml",
                    path => path.Replace('/', '\\').Equals(TexturePath, StringComparison.OrdinalIgnoreCase) ? texture : null));
                Assert.That(preview.EmitterCount, Is.EqualTo(1), "The scale diagnostic must reach the actual preview runtime.");
                preview.Seek(phase * 2, Matrix.Identity);
                var simulation = (TotalWarVfxSimulation)typeof(EffekseerVfxPreview)
                    .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
                SetUnary(simulation, unary);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
            }
            var observed = Render(actual); var expected = Render(reference);
            Assert.That(expected.Count(pixel => pixel.X + pixel.Y + pixel.Z > .001f), Is.GreaterThan(100));
            Assert.That(observed.Zip(expected).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThanOrEqualTo(Tolerance),
                "Only ParticleScale differs; the original diffuse bytes, camera and all other diagnostic fields are identical.");
        }
        finally { graphics.Release(); }
    }

    private static float Texel(JsonElement item, int row, int index, int component) =>
        (float)BitConverter.UInt16BitsToHalf(item.GetProperty("MinMaxRgbaRows")[row][index]
            .GetProperty("HalfBits")[component].GetUInt16());
    private static float Sample(JsonElement item, int component, double phase, float unary)
    {
        // Independent normalized linear-clamp geometry over native half texels.
        var coordinate = Math.Clamp((float)phase, 0, 1) * 128d - .5;
        var lower = (int)Math.Floor(coordinate); var x = coordinate - lower; var y = Math.Clamp(unary, 0, 1);
        var a = Texel(item, 0, Math.Clamp(lower, 0, 127), component);
        var b = Texel(item, 0, Math.Clamp(lower + 1, 0, 127), component);
        var c = Texel(item, 1, Math.Clamp(lower, 0, 127), component);
        var d = Texel(item, 1, Math.Clamp(lower + 1, 0, 127), component);
        return (float)((1 - y) * ((1 - x) * a + x * b) + y * ((1 - x) * c + x * d));
    }
    private static void SetUnary(TotalWarVfxSimulation simulation, float unary)
    {
        // Set only an authored random input; expected dimensions never read product sampling state.
        var particles = (IList)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!;
        particles[0]!.GetType().GetField("RandomScale")!.SetValue(particles[0], new Vector3(unary, .75f, .125f));
    }
    private static void Near(Matrix a, Matrix b) => Assert.Multiple(() =>
    {
        Assert.That(Vector3.Distance(a.Right, b.Right), Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(a.Up, b.Up), Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(a.Backward, b.Backward), Is.LessThan(Tolerance));
    });
    private static byte[] Resource(string suffix)
    {
        var assembly = typeof(VfxQuadraticScaleTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("." + suffix, StringComparison.Ordinal)))!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    private static JsonDocument Fixture() => JsonDocument.Parse(Resource("NativeSpriteQuadraticScale.json"));
    private static JsonElement Central(JsonDocument document) => document.RootElement.GetProperty("Cases")
        .EnumerateArray().Single(item => item.GetProperty("Name").GetString() == "official-central-pillar");
    private static VfxConversion Convert(byte[] bytes)
    {
        var converted = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/central-quadratic-scale.xml", _ => [1]));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1), string.Join(';', converted.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return converted;
    }
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
    private static string Curve(string axes, double value) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":"
        + string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
    private static string VectorCurve(float x, float y) => $"x,0,0;0,{x.ToString("R", CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1"
        + $"|y,0,0;0,{y.ToString("R", CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1|z,0,0;0,0,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] Source(string scale)
    {
        var emitter = new XElement("emitter", new XAttribute("id", "diagnostic-central-scale"), new XAttribute("type", "continuous_mesh"),
            Property("InfiniteLife", "true"), Property("MaximumParticles", 1), Property("ParticleSimulationSpace", "WORLD_SPACE"),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve("x", .1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve("x", 2)),
            new XElement("property", new XAttribute("name", "ParticleScale"), scale),
            new XElement("property", new XAttribute("name", "ParticleInitialScale"), Curve("xyz", 1)));
        var quality = new XElement("quality", new XAttribute("value", "0"),
            new XElement("modifier", new XAttribute("fragment", "vs_translation"), Property("restriction", 3)),
            new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", TexturePath))));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "diagnostic-central-scale"), quality))).ToString(SaveOptions.DisableFormatting));
    }
}
