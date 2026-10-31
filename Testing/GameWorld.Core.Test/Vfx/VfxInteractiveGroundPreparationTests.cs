#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxInteractiveGroundPreparationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 10, 30), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 2);

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void LegacyFirstGroundProjectionReallyPerformsAHiddenFullReplay(bool model, bool durationFirst)
    {
        var conversion = Conversion(model, trail: false);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.EnableSeekCheckpoints();
        var attachment = History(false)(1.117f);
        if (durationFirst) _ = simulation.CycleDuration;
        Seek(simulation, 1.117f, attachment, History(false), durationFirst, false);
        var emitters = Objects(simulation, "_emitters");
        var randoms = Randoms(simulation);
        var particles = Objects(simulation, "_particles");
        Assert.That(particles, Is.Not.Empty);
        var frame = simulation.ProjectedFrame(Camera, false);
        Assert.That(frame, Is.Not.Empty, "A terrain event must produce actual visible child/model particles.");
        Assert.Multiple(() =>
        {
            Assert.That(Objects(simulation, "_emitters")[0], Is.Not.SameAs(emitters[0]), "The old Draw path replaces complete emitter state.");
            Assert.That(Randoms(simulation)[0], Is.Not.SameAs(randoms[0]), "The old Draw path starts a fresh seeded random stream.");
            Assert.That(Objects(simulation, "_particles").Any(particles.Contains), Is.False, "The old Draw path replaces all births, not just a projection cache.");
            Assert.That((float?)Field(simulation, "_groundHeight"), Is.EqualTo(2));
        });
    }

    [TestCase(false, false, false, false)]
    [TestCase(false, false, true, false)]
    [TestCase(false, true, false, false)]
    [TestCase(false, true, true, false)]
    [TestCase(true, false, false, false)]
    [TestCase(true, false, true, false)]
    [TestCase(true, true, false, false)]
    [TestCase(true, true, true, false)]
    [TestCase(false, false, false, true)]
    [TestCase(false, false, true, true)]
    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, true)]
    [TestCase(true, false, false, true)]
    [TestCase(true, false, true, true)]
    [TestCase(true, true, false, true)]
    [TestCase(true, true, true, true)]
    public void PreparedGroundKeepsCanonicalPacketsAndFirstDrawDoesNotReplay(bool model, bool moving, bool durationFirst, bool continuous)
    {
        var conversion = Conversion(model, continuous);
        var canonical = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 1.8f };
        var prepared = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 1.8f };
        if (!moving && !continuous) { canonical.EnableSeekCheckpoints(); prepared.EnableSeekCheckpoints(); }
        var history = History(moving); var attachment = history(1.117f);
        if (durationFirst) _ = canonical.CycleDuration;
        Seek(canonical, 1.117f, attachment, history, durationFirst, continuous);
        var expected = canonical.ProjectedFrame(Camera, false);
        Assert.That(expected, Is.Not.Empty);
        Assert.That(NeedsReplay(prepared, 2), Is.True);
        var current = durationFirst || continuous;
        PrepareInitial(prepared, 2, attachment, history, current, continuous);
        Assert.That(NeedsReplay(prepared, 2), Is.False);
        var slices = 0;
        while (!prepared.PrepareInteractiveSeek(1.117f, attachment, history, current, continuous, continuous ? Next : null, TimeSpan.Zero))
            if (++slices > 10000) Assert.Fail("Bounded ground preparation failed to complete its original ticks.");
        Assert.That(slices, Is.GreaterThan(100));
        if (!moving && !continuous) Assert.That(prepared.SeekCheckpointRandomBytes, Is.GreaterThan(0), "The draw check must cover a real recorded random ledger.");
        var emitters = Objects(prepared, "_emitters"); var particles = Objects(prepared, "_particles");
        var randoms = Randoms(prepared); var simulated = Field(prepared, "_simulatedTime");
        var budget = Field(prepared, "_seekRandomBudget"); var randomBytes = prepared.SeekCheckpointRandomBytes;
        var cursors = Cursors(prepared);
        var actual = prepared.ProjectedFrame(Camera, false);
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected), "Compare every submitted transform, colour, material, UV and source layer.");
            Assert.That(Values(prepared), Is.EqualTo(Values(canonical)), "Include invisible parent births, variants and velocity.");
            Assert.That(prepared.ParticleCount, Is.GreaterThan(0));
            SameInstances(Objects(prepared, "_emitters"), emitters, "emitters");
            SameInstances(Objects(prepared, "_particles"), particles, "particles");
            SameInstances(Randoms(prepared), randoms, "random generators");
            Assert.That(Field(prepared, "_simulatedTime"), Is.EqualTo(simulated), "First projection must not integrate any source tick.");
            Assert.That(Field(prepared, "_seekRandomBudget"), Is.SameAs(budget), "First projection must not replace the random ledger generation.");
            Assert.That(prepared.SeekCheckpointRandomBytes, Is.EqualTo(randomBytes));
            Assert.That(Cursors(prepared), Is.EqualTo(cursors));
        });
        foreach (var time in new[] { 1.617f, .337f, 2.117f })
        {
            Seek(canonical, time, history(time), history, durationFirst, continuous);
            Seek(prepared, time, history(time), history, durationFirst, continuous);
            Assert.That(prepared.ProjectedFrame(Camera, false), Is.EqualTo(canonical.ProjectedFrame(Camera, false)), "Preserve future and reverse random/terrain state at " + time);
            Assert.That(Values(prepared), Is.EqualTo(Values(canonical)));
        }
    }

    private static void SameInstances(object[] actual, object[] expected, string name)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length), "First projection changed the number of " + name);
        Assert.That(actual.Zip(expected, ReferenceEquals).All(same => same), Is.True,
            "First projection replaced or reordered existing " + name);
    }

    [Test]
    public void OrdinaryEffectWithoutTerrainEventsDoesNotNeedGroundReplay()
    {
        var conversion = TotalWarVfxConverter.Convert(new(VfxLibraryVisibilityTests.Source(""), "diagnostic/no-terrain.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        Assert.That(NeedsReplay(simulation, null), Is.False);
        Assert.That(NeedsReplay(simulation, 2), Is.False);
        PrepareInitial(simulation, 2, Matrix.Identity, _ => Matrix.Identity, true, false);
        Assert.That(simulation.IsInitialized, Is.False, "Ground preparation must not alter ordinary initialization order.");
    }

    private static bool NeedsReplay(TotalWarVfxSimulation simulation, float? height)
    {
        var method = typeof(TotalWarVfxSimulation).GetMethod("NeedsGroundReplay", Private);
        Assert.That(method, Is.Not.Null, "Interactive preparation must expose the actual first-draw replay condition.");
        return (bool)method!.Invoke(simulation, [height])!;
    }
    private static void PrepareInitial(TotalWarVfxSimulation simulation, float? height, Matrix attachment,
        Func<float, Matrix> history, bool current, bool continuous)
    {
        var method = typeof(TotalWarVfxSimulation).GetMethod("PrepareInitialGround", Private);
        Assert.That(method, Is.Not.Null, "Prepare the terrain receiver before publishing the simulation.");
        method!.Invoke(simulation, [height, attachment, history, current, continuous, continuous ? (Func<float, float?>)Next : null]);
    }
    private static void Seek(TotalWarVfxSimulation simulation, float time, Matrix attachment, Func<float, Matrix> history, bool current, bool continuous)
    {
        if (continuous) simulation.SeekContinuousTrail(time, attachment, history, Next);
        else if (current) simulation.SeekWithCurrentAttachment(time, attachment, history);
        else simulation.Seek(time, attachment, history);
    }
    // Terrain event anchors permit positive uniform scale and translation; rotated decal support is separate.
    private static Func<float, Matrix> History(bool moving) => time => Matrix.CreateScale(1.25f)
        * Matrix.CreateTranslation(2 + (moving ? time * 2 : 0), 4, -1 + (moving ? time * .3f : 0));
    private static float? Next(float time)
    {
        foreach (var sample in new[] { .137f, .291f, .493f, .817f, 1.317f, 1.817f }) if (sample > time) return sample;
        return null;
    }
    private static object? Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
    private static object[] Objects(TotalWarVfxSimulation simulation, string name) => ((IEnumerable)Field(simulation, name)!).Cast<object>().ToArray();
    private static object[] Randoms(TotalWarVfxSimulation simulation) => Objects(simulation, "_emitters").Select(emitter => emitter.GetType().GetField("Random")!.GetValue(emitter)!).ToArray();
    private static int[] Cursors(TotalWarVfxSimulation simulation) => Randoms(simulation).Select(random => random is VfxSeekRandom ? (int)Field(random, "_cursor")! : -1).ToArray();
    private static object[] Values(TotalWarVfxSimulation simulation) => Objects(simulation, "_particles").Select(particle => (object)particle.GetType().GetFields().Where(field => field.FieldType.IsValueType).Select(field => field.GetValue(particle)).ToArray()).ToArray();

    // DIAG: structured controls for ownership, exact history and terrain events, not visual acceptance.
    private static VfxConversion Conversion(bool model, bool trail)
    {
        var document = VfxDocument.Read(model ? VfxModelVariantTests.Source(2) : VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        if (!model) emitter.SetAttributeValue("type", "continuous_rigid");
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 12));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .6f));
        Set(emitter, "MaximumParticles", "64", true);
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION", true);
        Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL", true);
        Set(emitter, "ParticleRelativeSpawnDirection", "RELATIVE_TO_TERRAIN_NORMAL", true);
        Set(emitter, "ParticlesSpawnOnTerrain", "true", true);
        Set(emitter, "EventEnable", "true", true);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -2), new XAttribute("max", -2)));
        foreach (var name in new[] { "OnParticleSpawn", "OnParticleDeath" }) emitter.Add(new XElement("property_event", new XAttribute("name", name), new XAttribute("vfx_name", "child"),
            new XAttribute("track_position", "true"), new XAttribute("track_scale", "true"), new XAttribute("track_colour", "true"), new XAttribute("track_alpha", "true"),
            new XAttribute("track_lifetime", "false"), new XAttribute("spawn_event_time_offset", ".1"), new XAttribute("track_orientation_mode", "orientation")));
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        Set(child.Root.Descendants("emitter").Single(), "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .4f));
        if (trail)
        {
            var extra = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
            var root = extra.Root.Descendants("emitter").Single(); root.SetAttributeValue("id", "trail-diagnostic");
            Set(root, "SpawnSpacing", ".1", true);
            document.Root.Element("vfx")!.Element("emitters")!.Add(new XElement(root));
            document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "trail-diagnostic"),
                new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", "true")))));
        }
        var result = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/interactive-ground.xml", path => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(result.Emitters.Any(item => item.RequiresFlatTerrain && item.Events.Count > 0), Is.True);
        Assert.That(result.Emitters.Any(item => item.IsMotionTrail), Is.EqualTo(trail));
        return result;
    }
    private static void Set(XElement emitter, string name, string value, bool attribute = false)
    {
        emitter.Elements().Where(element => (string?)element.Attribute("name") == name).Remove();
        var element = new XElement("property", new XAttribute("name", name));
        if (attribute) element.SetAttributeValue("value", value); else element.Value = value;
        emitter.Add(element);
    }
}
