using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxTerrainDecalOrientationTests
{
    public static IEnumerable<TestCaseData> NativeCases()
    {
        var assembly = typeof(VfxTerrainDecalOrientationTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("NativeTerrainOrientation.json", StringComparison.Ordinal)))!;
        using var fixture = JsonDocument.Parse(input);
        foreach (var item in fixture.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("TerrainOrientationNative_" + item.GetProperty("Name").GetString());
    }

    public static IEnumerable<TestCaseData> NativeSimulationCases() => NativeCases().Select(data =>
        data.SetName("TerrainSimulationNative_" + ((JsonElement)data.Arguments[0]).GetProperty("Name").GetString()));

    [TestCaseSource(nameof(NativeCases))]
    public void BirthBasis_MatchesNativeQuaternionInstructionsForTheExplicitReceiverNormal(JsonElement item)
    {
        // Retain the original R102 function executions as explicit-normal mathematical controls only.
        var actual = VfxTerrainDecalOrientation.BirthBasis(Vector(item, "Angles"), Vector(item, "OrientationInputNormal"));
        var q = item.GetProperty("NativeQuaternionFloatBits").EnumerateArray()
            .Select(value => BitConverter.UInt32BitsToSingle(value.GetUInt32())).ToArray();
        var expected = Matrix.CreateFromQuaternion(new(q[0], q[1], q[2], q[3]));
        Near(actual.Right, expected.Right);
        Near(actual.Up, expected.Up);
        Near(actual.Backward, expected.Backward);
        Assert.That(actual.Translation, Is.EqualTo(Vector3.Zero));
    }

    [TestCaseSource(nameof(NativeSimulationCases))]
    public void SimulatedDecal_UsesTheActualIndependentReceiverNormalFromTheNativeCaller(JsonElement item)
    {
        var bytes = Source(Vector(item, "Angles"), item.GetProperty("Direction").GetInt32());
        var conversion = Convert(bytes);
        var simulation = new TotalWarVfxSimulation(conversion);
        var root = item.GetProperty("RootQuaternion").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var transform = Matrix.CreateScale(2.5f) * Matrix.CreateFromQuaternion(new(root[0], root[1], root[2], root[3]))
            * Matrix.CreateTranslation(3, 8, 5);
        simulation.Seek(.2f, transform);
        // Diagnostic submission of an exact native fixture birth point; this does not edit an official source.
        var particles = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>().ToArray();
        Assert.That(particles, Has.Length.EqualTo(1));
        particles[0].GetType().GetField("Position")!.SetValue(particles[0], Vector(item, "LocalBirthPosition"));
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity,
            new(0, 10, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []) { VfxGroundHeight = 4 };
        var frame = simulation.Frame(camera);
        Assert.That(frame, Has.Length.EqualTo(1));
        // The actual writer receives a separate Up normal on flat terrain, not the spawn direction.
        // The R102 manual direction-function composition remains only a low-level math control.
        var q = ProductionOracle(Vector(item, "Angles")).GetProperty("NativeQuaternionFloatBits").EnumerateArray()
            .Select(value => BitConverter.UInt32BitsToSingle(value.GetUInt32())).ToArray();
        var expected = Matrix.CreateScale(2.5f) * Matrix.CreateFromQuaternion(new(q[0], q[1], q[2], q[3]));
        Near(frame[0].Transform.Right, expected.Right);
        Near(frame[0].Transform.Up, expected.Up);
        Near(frame[0].Transform.Backward, expected.Backward);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void MissingDirection_UsesTheNativeDefaultOneInsteadOfTheCacheConstructorZero()
    {
        var bytes = Source(new(90, 37, 0), null);
        var emitter = Convert(bytes).Emitters.Single();
        var property = emitter.GetType().GetProperty("TerrainSpawnDirection");
        Assert.That(property, Is.Not.Null);
        Assert.That(property!.GetValue(emitter), Is.EqualTo(1));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    private static VfxConversion Convert(byte[] bytes)
    {
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/terrain-orientation-control.xml", _ => [1]));
        Assert.That(result.Emitters, Has.Count.EqualTo(1), string.Join(';', result.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return result;
    }

    private static byte[] Source(Vector3 angles, int? direction)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source("<modifier fragment=\"vs_translation\"/>"));
        var effect = document.Root.Element("vfx")!;
        var emitter = effect.Element("emitters")!.Element("emitter")!;
        emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL"),
            Property("ParticlesSpawnOnTerrain", "true"), Property("SpreadMethod", "false"));
        if (direction != null)
            emitter.Add(Property("ParticleRelativeSpawnDirection", direction switch
            {
                0 => "DISABLED",
                1 => "RELATIVE_TO_VFX_UP_AXIS",
                _ => "RELATIVE_TO_TERRAIN_NORMAL",
            }));
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Add(new XElement("property", new XAttribute("name", name),
                new XAttribute("x", angles.X), new XAttribute("y", angles.Y), new XAttribute("z", angles.Z)));
        effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            Property("scene_view_type", "DEFERRED_DECAL"))));
        return document.Write();
    }

    private static XElement Property(string name, string value) => new("property", new XAttribute("name", name), new XAttribute("value", value));

    private static Vector3 Vector(JsonElement item, string field)
    {
        var values = item.GetProperty(field).EnumerateArray().Select(value => value.GetSingle()).ToArray();
        return new(values[0], values[1], values[2]);
    }

    private static JsonElement ProductionOracle(Vector3 angles)
    {
        var assembly = typeof(VfxTerrainDecalOrientationTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("NativeTerrainOrientationProduction.json", StringComparison.Ordinal)))!;
        using var fixture = JsonDocument.Parse(input);
        return fixture.RootElement.GetProperty("Oracles").EnumerateArray()
            .Single(item => Vector(item, "Angles") == angles).Clone();
    }

    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00001f));
}
