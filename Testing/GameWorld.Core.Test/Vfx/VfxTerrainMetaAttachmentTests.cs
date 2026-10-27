#nullable enable
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxTerrainMetaAttachmentTests
{
    private const float Tolerance = .00001f;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 10, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []) { VfxGroundHeight = 4 };

    public static IEnumerable<TestCaseData> NativeCases()
    {
        using var fixture = JsonDocument.Parse(Resource());
        foreach (var item in fixture.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("TerrainMetaNative_" + item.GetProperty("Name").GetString());
    }

    [TestCaseSource(nameof(NativeCases))]
    public void TerrainNormalBirth_IgnoresBoneRotationButKeepsNativeUniformSize(JsonElement item)
    {
        var raw = item.GetProperty("RootQuaternion").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var root = Matrix.CreateScale(item.GetProperty("RootScale").GetSingle())
            * Matrix.CreateFromQuaternion(new(raw[0], raw[1], raw[2], raw[3])) * Matrix.CreateTranslation(3, 8, 5);
        var bytes = Source();
        var simulation = Simulation(bytes); simulation.Seek(.2f, root);
        var frame = simulation.Frame(Camera);
        Assert.That(frame, Has.Length.EqualTo(1), "An explicitly supplied flat terrain must receive the stationary decal from a rotated META bone.");
        var q = item.GetProperty("NativeQuaternionFloatBits").EnumerateArray()
            .Select(value => BitConverter.UInt32BitsToSingle(value.GetUInt32())).ToArray();
        var factor = BitConverter.UInt32BitsToSingle(item.GetProperty("NativeScaleFactorFloatBits").GetUInt32());
        var expected = Matrix.CreateScale(factor) * Matrix.CreateFromQuaternion(new(q[0], q[1], q[2], q[3]))
            * Matrix.CreateTranslation(3, 4, 5);
        Near(frame[0].Transform.Right, expected.Right);
        Near(frame[0].Transform.Up, expected.Up);
        Near(frame[0].Transform.Backward, expected.Backward);
        Near(frame[0].Transform.Translation, expected.Translation);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void FrostRadiusBirth_TransformsWorldXZThenProjectsOnlyYToTheExplicitTerrain()
    {
        var bytes = Source(radius: .2f, rate: 32);
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters.Single().Kind, Is.EqualTo(2));
        var simulation = new TotalWarVfxSimulation(conversion);
        var root = Matrix.CreateScale(2.5f) * Matrix.CreateFromYawPitchRoll(.73f, -.41f, .29f) * Matrix.CreateTranslation(3, 8, 5);
        const float time = .35f;
        simulation.Seek(time, root);
        var frame = simulation.Frame(Camera);
        var particles = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>().ToArray();
        Assert.That(particles.Length, Is.GreaterThan(5));
        Assert.That(frame, Has.Length.EqualTo(particles.Length), "A nonzero authored birth radius must not disable the complete decal layer.");
        var distinctPositions = new HashSet<Vector3>();
        foreach (var particle in particles)
        {
            var type = particle.GetType();
            var local = (Vector3)type.GetField("Position")!.GetValue(particle)!;
            var birth = (double)type.GetField("Birth")!.GetValue(particle)!;
            var actual = frame.Single(value => Math.Abs(value.Time - (time - birth)) < .000001).Transform.Translation;
            // The native terrain query runs after world birth placement and writes only world Y.
            var expected = Vector3.Transform(local, root); expected.Y = 4;
            Near(actual, expected);
            Assert.That(local.Length(), Is.LessThanOrEqualTo(.200001f));
            distinctPositions.Add(actual);
        }
        Assert.That(distinctPositions.Count, Is.GreaterThan(5), "Projecting onto terrain must preserve the authored footprint distribution.");
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("nonuniform")]
    [TestCase("mirror")]
    [TestCase("shear")]
    [TestCase("zero-scale")]
    public void TerrainMetaAttachment_DoesNotOpenUnprovedRootTransforms(string kind)
    {
        var root = kind switch
        {
            "nonuniform" => Matrix.CreateScale(1, 2, 1),
            "mirror" => Matrix.CreateScale(-1, 1, 1),
            "zero-scale" => Matrix.CreateScale(0),
            _ => Matrix.Identity,
        };
        if (kind == "shear") root.M12 = .25f;
        var simulation = Simulation(Source()); simulation.Seek(.2f, root);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void TerrainMetaAttachment_RequiresAnExplicitFiniteSceneReceiver()
    {
        foreach (var height in new float?[] { null, float.NaN, float.PositiveInfinity })
        {
            var simulation = Simulation(Source()); simulation.Seek(.2f, Matrix.Identity);
            Assert.That(simulation.Frame(Camera with { VfxGroundHeight = height }), Is.Empty);
        }
    }

    [Test]
    public void TerrainMetaAttachment_LeavesNonTerrainBirthOutsideTheNewBoneTransformScope()
    {
        var simulation = Simulation(Source(spawnOnTerrain: false));
        simulation.Seek(.2f, Matrix.CreateScale(2.5f) * Matrix.CreateRotationY(.5f) * Matrix.CreateTranslation(0, 8, 0));
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    private static TotalWarVfxSimulation Simulation(byte[] bytes) => new(Convert(bytes));

    private static VfxConversion Convert(byte[] bytes)
    {
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/terrain-meta-control.xml", _ => [1]));
        Assert.That(result.Emitters, Has.Count.EqualTo(1), string.Join(';', result.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return result;
    }

    private static byte[] Source(float radius = 0, float rate = 1, bool spawnOnTerrain = true)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source("<modifier fragment=\"vs_translation\"/>"));
        var effect = document.Root.Element("vfx")!;
        var emitter = effect.Element("emitters")!.Element("emitter")!;
        emitter.Elements("property").Single(node => (string?)node.Attribute("name") == "EmissionRateCurve").Value
            = VfxLibraryVisibilityTests.Constant("x", rate);
        emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL"),
            Property("ParticleRelativeSpawnDirection", "RELATIVE_TO_TERRAIN_NORMAL"),
            Property("ParticlesSpawnOnTerrain", spawnOnTerrain ? "true" : "false"));
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", 90), new XAttribute("y", 37), new XAttribute("z", 0)));
        if (radius != 0)
            emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaCurve"),
                VfxLibraryVisibilityTests.Constant("inner_radius", 0).Split(':')[0] + "|"
                + VfxLibraryVisibilityTests.Constant("outer_radius", radius).Split(':')[0] + ":inner_radius,0;0,0|outer_radius,0;1,1"));
        effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            Property("scene_view_type", "DEFERRED_DECAL"))));
        return document.Write();
    }

    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance));

    private static byte[] Resource()
    {
        var assembly = typeof(VfxTerrainMetaAttachmentTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeTerrainAttachment.json", StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
}
