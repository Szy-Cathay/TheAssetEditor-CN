#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxModelLinearDampingTimeScaleTests
{
    private const double Step = 1.0 / 120;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.5f)]
    [TestCase(1f)]
    [TestCase(1.4f)]
    [TestCase(2f)]
    public void ModelDecayUsesSourceSecondsWhilePositionUsesPreviewSeconds(float speed)
    {
        var simulation = Simulation(Source(), speed);
        simulation.Seek(0, Matrix.Identity);
        var birth = Snapshot(simulation);
        Assert.That(birth.Velocity.Y, Is.EqualTo(15 * speed));
        simulation.Seek(.4f, Matrix.Identity);
        AssertConstantDecay(simulation, birth, speed, 2);
    }

    [TestCase(.5f, 2f)]
    [TestCase(2f, .7f)]
    public void NestedReferencesMultiplyTheSourceClockExactlyOnce(float innerSpeed, float outerSpeed)
    {
        var leaf = Source(); var inner = Wrap(leaf, innerSpeed, "leaf"); var parent = Wrap(inner, outerSpeed, "inner");
        var conversion = TotalWarVfxConverter.Convert(new(parent, "diagnostic/nested-linear-damping.xml", path => path.Replace('/', '\\') switch
        {
            @"vfx\leaf.xml" => leaf, @"vfx\inner.xml" => inner, _ => VfxModelVariantTests.Resolve(path),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity); var birth = Snapshot(simulation);
        var speed = innerSpeed * outerSpeed;
        Assert.That(birth.SourceSpeed, Is.EqualTo(speed));
        simulation.Seek(.4f, Matrix.Identity);
        AssertConstantDecay(simulation, birth, speed, 2);
    }

    [TestCase(.5f)]
    [TestCase(1f)]
    [TestCase(1.4f)]
    [TestCase(2f)]
    public void DelayedBirthAndDynamicDampingUseParticleAgeAndTheUnchangedTickOrder(float speed)
    {
        var simulation = Simulation(Source(delay: .2f, dynamic: true), speed);
        var birthTime = Math.Ceiling((.2f / speed - .000001) / Step) * Step;
        simulation.Seek((float)(birthTime - Step), Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek((float)birthTime, Matrix.Identity);
        var birth = Snapshot(simulation);
        Assert.That(birth.Birth, Is.EqualTo(birthTime).Within(.000001));
        simulation.Seek(.7f, Matrix.Identity);
        var actual = Snapshot(simulation); var position = birth.Position; var velocity = birth.Velocity;
        var ticks = Ticks(simulation, birth.Birth);
        for (var index = 1; index <= ticks; index++)
        {
            // Native selector0 samples the age controller, applies expf, then integrates the scaled velocity.
            var phase = (index * Step) / birth.Life;
            var damping = (float)(2 + 3 * phase);
            velocity *= MathF.Exp(-damping * ((float)Step * speed));
            position += velocity * (float)Step;
        }
        Near(actual.Velocity, velocity); Near(actual.Position, position);
        Assert.That(actual.Birth, Is.EqualTo(birth.Birth));
        Assert.That(actual.Life, Is.EqualTo(4 / speed));
    }

    [Test]
    public void RewindForwardAndCheckpointsPreserveTheSameDampedModelPackets()
    {
        var conversion = Conversion(Source(dynamic: true), 2);
        var checkpointed = new TotalWarVfxSimulation(conversion);
        Assert.That(checkpointed.EnableSeekCheckpoints(), Is.True);
        checkpointed.Seek(1.2f, Matrix.Identity); checkpointed.Seek(.7f, Matrix.Identity);
        Assert.That(checkpointed.SeekCheckpointRestoreCount, Is.GreaterThan(0));
        foreach (var time in new[] { .7f, .85f, .2f, .637f })
        {
            checkpointed.Seek(time, Matrix.Identity);
            var fresh = new TotalWarVfxSimulation(conversion); fresh.Seek(time, Matrix.Identity);
            Assert.That(Packet(checkpointed), Is.EqualTo(Packet(fresh)));
            var actual = Snapshot(checkpointed); var expected = Snapshot(fresh);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }

    [Test]
    public void FullLocalEmitterMotionRemainsSeparateFromLinearDecay()
    {
        var conversion = Conversion(Source(motion: true), 2);
        Assert.That(conversion.Emitters.Single().ModelEmitterMotion, Is.Not.Null);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity); var birth = Snapshot(simulation);
        simulation.Seek(.4f, Matrix.Identity);
        AssertConstantDecay(simulation, birth, 2, 2, comparePosition: false);
        var projected = simulation.Frame(Camera).Single().Transform.Translation;
        var expectedMotion = 2 * .4f * 2 / 2;
        Assert.That(projected.Z, Is.EqualTo(expectedMotion).Within(.00001));
        Assert.That(simulation.ModelMotionScaleUnsupported, Is.False);
        Assert.That(simulation.ModelSpinBasisUnsupported, Is.False);
    }

    [TestCase(0)]
    [TestCase(2)]
    public void SpriteAndDecalKeepTheirExistingPreviewDeltaDamping(int kind)
    {
        var simulation = Simulation(Source(kind), 2);
        simulation.Seek(0, Matrix.Identity); var birth = Snapshot(simulation);
        Assert.That(birth.Kind, Is.EqualTo(kind));
        simulation.Seek(.4f, Matrix.Identity);
        AssertConstantDecay(simulation, birth, 1, 2);
    }

    [Test]
    public void IdentityReferenceKeepsEveryUnitSpeedPacketBit()
    {
        var bytes = Source(dynamic: true);
        var direct = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(bytes, "diagnostic/direct.xml", VfxModelVariantTests.Resolve)));
        var referenced = Simulation(bytes, 1);
        foreach (var time in new[] { 0, .1f, .4f, .837f, .2f })
        {
            direct.Seek(time, Matrix.Identity); referenced.Seek(time, Matrix.Identity);
            Assert.That(Packet(referenced), Is.EqualTo(Packet(direct)));
        }
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("circle", 1.4f, "211c6b5d32c1b6fd7523a6e222b4ad32c72c95642caf3a2239abfe92ff5d7f0b")]
    [TestCase("wall", 2f, "970af2ae8f39213bcd3ff9621d8c91ea8f19fe1f2220fe2792628fde80879af4")]
    public void CompleteOfficialCrystalRootsDecayFromTheirOwnObservedBirthState(string attack, float speed, string rootSha)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original official game data is required for this CPU control.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" })
        {
            var path = Path.Combine(data!, name);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var entry in new PackFileContainerLoader(null!).Load(path)!.FileList) entries[entry.Key.Replace('/', '\\')] = entry.Value;
        }
        const string library = @"vfx\wh3_dlc24_lib_entity_green_dragon.xml";
        var root = $@"vfx\wh3_dlc24_cth_jade_dragon_human_{attack}_attk.xml";
        var source = entries[root].DataSource.ReadData(); var libraryBytes = entries[library].DataSource.ReadData();
        Assert.That(Hash(source), Is.EqualTo(rootSha));
        Assert.That(Hash(libraryBytes), Is.EqualTo("6e992c150f080bbaf5c5ffcd3c870fdca7d4b20d5c0726b9b7a8ef50ef85acba"));
        var cached = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? Resolve(string path)
        {
            path = path.Replace('/', '\\');
            if (cached.TryGetValue(path, out var bytes)) return bytes;
            if (!entries.TryGetValue(path, out var entry)) return null;
            return cached[path] = entry.DataSource.ReadData();
        }
        var conversion = TotalWarVfxConverter.Convert(new(source, root, Resolve, "0"));
        var crystals = conversion.Emitters.Where(emitter => emitter.Name.StartsWith("gem spike", StringComparison.Ordinal)).ToArray();
        Assert.That(crystals, Has.Length.EqualTo(attack == "circle" ? 1 : 3));
        Assert.That(crystals.All(emitter => emitter.Deceleration.TryConstantRange("x", out var range) && range.Min == 10 && range.Max == 10), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion);
        var births = new Dictionary<object, Sample>(ReferenceEqualityComparer.Instance);
        for (var tick = 0; tick <= 48; tick++)
        {
            simulation.Seek((float)(tick * Step), Matrix.Identity);
            foreach (var particle in Particles(simulation).Where(particle => Definition(particle).Name.StartsWith("gem spike", StringComparison.Ordinal)))
                births.TryAdd(particle, SampleOf(particle));
        }
        var checkedParticles = 0;
        foreach (var particle in Particles(simulation).Where(particle => births.ContainsKey(particle)))
        {
            var birth = births[particle]; var actual = SampleOf(particle);
            Assert.That(birth.SourceSpeed, Is.EqualTo(speed));
            var ticks = Ticks(simulation, birth.Birth);
            var (velocity, position) = ConstantOracle(birth, ticks, speed, 10);
            Near(actual.Velocity, velocity); Near(actual.Position, position);
            Assert.That(actual.InitialScale, Is.EqualTo(birth.InitialScale));
            checkedParticles++;
        }
        Assert.That(checkedParticles, Is.GreaterThan(10));
        Assert.That(entries[root].DataSource.ReadData(), Is.EqualTo(source));
        Assert.That(entries[library].DataSource.ReadData(), Is.EqualTo(libraryBytes));
    }

    private static (Vector3 Velocity, Vector3 Position) ConstantOracle(Sample birth, int ticks, float sourceSpeed, float damping)
    {
        var decay = MathF.Exp(-damping * ((float)Step * sourceSpeed));
        var power = Math.Pow(decay, ticks);
        var velocity = birth.Velocity * (float)power;
        var distance = decay == 1 ? ticks : decay * (1 - power) / (1 - decay);
        return (velocity, birth.Position + birth.Velocity * (float)(distance * (float)Step));
    }

    private static void AssertConstantDecay(TotalWarVfxSimulation simulation, Sample birth, float sourceSpeed, float damping, bool comparePosition = true)
    {
        var expected = ConstantOracle(birth, Ticks(simulation, birth.Birth), sourceSpeed, damping);
        var actual = Snapshot(simulation);
        Near(actual.Velocity, expected.Velocity);
        if (comparePosition) Near(actual.Position, expected.Position);
    }

    private static int Ticks(TotalWarVfxSimulation simulation, double birth) => (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth) / Step);
    private static IEnumerable<object> Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Field(simulation, "_particles")).Cast<object>();
    private static VfxPreviewEmitter Definition(object particle) => (VfxPreviewEmitter)Field(Field(particle, "Emitter"), "Definition");
    private static object Field(object value, string name) => value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static Sample Snapshot(TotalWarVfxSimulation simulation) => SampleOf(Particles(simulation).Single());
    private static Sample SampleOf(object particle) => new((Vector3)Field(particle, "Position"), (Vector3)Field(particle, "Velocity"),
        (double)Field(particle, "Birth"), (float)Field(particle, "Life"), (Vector3)Field(particle, "InitialScale"),
        (float)Field(Field(particle, "Emitter"), "TimeScale"), Definition(particle).Kind);
    private readonly record struct Sample(Vector3 Position, Vector3 Velocity, double Birth, float Life, Vector3 InitialScale, float SourceSpeed, int Kind);
    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00002f));
    private static byte[] Packet(TotalWarVfxSimulation simulation) => MemoryMarshal.AsBytes(simulation.Frame(Camera).AsSpan()).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static TotalWarVfxSimulation Simulation(byte[] bytes, float speed) => new(Conversion(bytes, speed));
    private static VfxConversion Conversion(byte[] bytes, float speed)
    {
        var parent = Wrap(bytes, speed, "leaf");
        var converted = TotalWarVfxConverter.Convert(new(parent, "diagnostic/linear-damping.xml", path =>
            path.Replace('/', '\\') == @"vfx\leaf.xml" ? bytes : VfxModelVariantTests.Resolve(path)));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1), string.Join(';', converted.Diagnostics));
        return converted;
    }
    private static byte[] Wrap(byte[] child, float speed, string name)
    {
        var parent = VfxDocument.CreateComposition(); var reference = parent.AddReference(name, VfxDocument.Read(child)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed.ToString("R", CultureInfo.InvariantCulture));
        return parent.Write();
    }
    private static byte[] Source(int kind = 1, float delay = 0, bool dynamic = false, bool motion = false)
    {
        var document = VfxDocument.Read(kind == 1 ? VfxModelVariantTests.Source(1) : VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        void Set(string name, string text, bool attribute = false)
        {
            emitter.Elements().Where(node => (string?)node.Attribute("name") == name).Remove();
            emitter.Add(attribute ? new XElement("property", new XAttribute("name", name), new XAttribute("value", text))
                : new XElement("property", new XAttribute("name", name), text));
        }
        Set("MaximumParticles", "1", true); Set("InfiniteLife", "false", true);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", 2), new XAttribute("max", 2)),
            new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", delay), new XAttribute("max", delay)));
        Set("EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", .1f));
        Set("ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 4));
        Set("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 15));
        Set("ParticleDeceleration", dynamic ? "x_min,0,0;0,2,linear,linear,0,0,0,0,1;1,5,linear,linear,0,0,0,0,1:x,0;0,0"
            : VfxLibraryVisibilityTests.Constant("x", 2));
        Set("ParticleSimulationSpace", motion ? "LOCAL_SPACE_POSITION_ROTATION" : "WORLD_SPACE", true);
        if (motion) Set("EmitterPosition", "x_min,0,0;0,0,linear,linear,0,0,0,0,1|y_min,0,0;0,0,linear,linear,0,0,0,0,1|z_min,0,0;0,0,linear,linear,0,0,0,0,1;1,2,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2");
        if (kind == 2) document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", "DEFERRED_DECAL")))));
        return document.Write();
    }
}
