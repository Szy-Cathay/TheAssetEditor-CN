#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxInteractiveSeekPreparationTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 30), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void SlicedNegativeStartKeepsInitializationOrderEventsAndFutureRandomStream(bool model, bool events, bool durationFirst)
    {
        var conversion = Conversion(model, events, trail: false, negative: true);
        var original = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .93f };
        var sliced = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .93f };
        if (durationFirst) Assert.That(sliced.CycleDuration, Is.EqualTo(original.CycleDuration));
        var attachment = Matrix.CreateRotationY(.3f) * Matrix.CreateTranslation(2, 1, -1);
        Func<float, Matrix> history = time => Matrix.CreateRotationY(time * .4f) * Matrix.CreateTranslation(time, time * .1f, -time * .3f);
        var slices = Finish(sliced, 1.317f, attachment, history, current: true);
        original.SeekWithCurrentAttachment(1.317f, attachment, history);
        Assert.That(slices, Is.GreaterThan(100), "Zero-budget preparation must yield between original fixed ticks.");
        Assert.That(sliced.ParticleCount, Is.GreaterThan(0));
        AssertSame(sliced, original);
        foreach (var time in new[] { 1.737f, 2.137f, .317f, 1.517f })
        {
            original.SeekWithCurrentAttachment(time, attachment, history);
            sliced.SeekWithCurrentAttachment(time, attachment, history);
            AssertSame(sliced, original);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SlicedContinuousTrailKeepsEveryRecordedChordAndTurn(bool model, bool negative)
    {
        var conversion = Conversion(model, events: false, trail: true, negative);
        Assert.That(conversion.Emitters.Single().IsMotionTrail, Is.True);
        var original = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .63f };
        var sliced = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .63f };
        var originalQueries = new List<float>(); var slicedQueries = new List<float>();
        var turns = new[] { .137f, .291f, .493f, .713f };
        float? Next(float time, List<float> queries)
        {
            queries.Add(time);
            foreach (var turn in turns) if (turn > time) return turn;
            return null;
        }
        Func<float, Matrix> history = time => time < .291f
            ? Matrix.CreateTranslation(time * 8, 0, 0)
            : Matrix.CreateTranslation(.291f * 8, 0, (time - .291f) * 11);
        Func<float, float?> originalNext = time => Next(time, originalQueries);
        Func<float, float?> slicedNext = time => Next(time, slicedQueries);
        foreach (var time in new[] { .317f, .817f, 1.137f })
        {
            var attachment = history(time);
            original.SeekContinuousTrail(time, attachment, history, originalNext);
            Finish(sliced, time, attachment, history, current: true, continuous: true, next: slicedNext);
            if (time == .317f) Assert.That(sliced.ParticleCount, Is.GreaterThan(0));
            AssertSame(sliced, original);
            Assert.That(slicedQueries, Is.EqualTo(originalQueries), "Yielding must not create extra trail chords or sample-boundary queries.");
        }
    }

    [Test]
    public void ChangedTargetFinishesTheOriginalOperationBeforeAcceptingTheLatestTarget()
    {
        var conversion = Conversion(model: false, events: true, trail: false, negative: true);
        var original = new TotalWarVfxSimulation(conversion);
        var sliced = new TotalWarVfxSimulation(conversion);
        Func<float, Matrix> first = time => Matrix.CreateTranslation(time, 0, 0);
        Func<float, Matrix> latest = time => Matrix.CreateTranslation(0, 0, time);
        Assert.That(sliced.PrepareInteractiveSeek(.817f, first(.817f), first, budget: TimeSpan.Zero), Is.False);
        original.Seek(.817f, first(.817f), first);
        Finish(sliced, .317f, latest(.317f), latest);
        original.Seek(.317f, latest(.317f), latest);
        AssertSame(sliced, original);
    }

    [Test]
    public void PendingPreparationRejectsSynchronousMutationWithoutDamagingItsFinalState()
    {
        var conversion = Conversion(model: false, events: false, trail: false, negative: true);
        var original = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .4f };
        var sliced = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .4f };
        Assert.That(sliced.PrepareInteractiveSeek(.817f, Matrix.Identity, budget: TimeSpan.Zero), Is.False);
        Assert.Throws<InvalidOperationException>(() => sliced.Seek(.1f, Matrix.Identity));
        Assert.Throws<InvalidOperationException>(() => sliced.EmissionEndTimeSeconds = .2f);
        sliced.EmissionEndTimeSeconds = .4f;
        Finish(sliced, .817f, Matrix.Identity);
        original.Seek(.817f, Matrix.Identity);
        AssertSame(sliced, original);
    }

    [Test]
    public void SameTimeCurrentAttachmentUpdateAndCheckpointReplayKeepCompletePackets()
    {
        var conversion = Conversion(model: false, events: true, trail: false, negative: true);
        var original = new TotalWarVfxSimulation(conversion); var sliced = new TotalWarVfxSimulation(conversion);
        original.EnableSeekCheckpoints(); sliced.EnableSeekCheckpoints();
        Func<float, Matrix> history = _ => Matrix.Identity;
        foreach (var time in new[] { 1.317f, .717f, .717f, 1.837f })
        {
            var attachment = Matrix.CreateTranslation(time, 0, time * .5f);
            original.SeekWithCurrentAttachment(time, attachment, history);
            Finish(sliced, time, attachment, history, current: true);
            AssertSame(sliced, original);
            Assert.That(sliced.SeekCheckpointCount, Is.EqualTo(original.SeekCheckpointCount));
            Assert.That(sliced.SeekCheckpointRestoreCount, Is.EqualTo(original.SeekCheckpointRestoreCount));
        }
    }

    [Test]
    public void InvalidArgumentsDoNotStartPreparation()
    {
        var simulation = new TotalWarVfxSimulation(Conversion(false, false, false, false));
        foreach (var seconds in new[] { float.NaN, float.PositiveInfinity, -1, 601 })
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.PrepareInteractiveSeek(seconds, Matrix.Identity));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.PrepareInteractiveSeek(0, Matrix.Identity, budget: TimeSpan.FromTicks(-1)));
        Assert.That(simulation.PrepareInteractiveSeek(0, Matrix.Identity), Is.True);
    }

    private static int Finish(TotalWarVfxSimulation simulation, float time, Matrix attachment,
        Func<float, Matrix>? history = null, bool current = false, bool continuous = false, Func<float, float?>? next = null)
    {
        var slices = 1;
        while (!simulation.PrepareInteractiveSeek(time, attachment, history, current, continuous, next, TimeSpan.Zero))
            if (++slices > 10000) Assert.Fail("Preparation did not finish its original fixed ticks.");
        return slices;
    }

    private static void AssertSame(TotalWarVfxSimulation actual, TotalWarVfxSimulation expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Time, Is.EqualTo(expected.Time));
            Assert.That(actual.ParticleCount, Is.EqualTo(expected.ParticleCount));
            Assert.That(actual.CycleDuration, Is.EqualTo(expected.CycleDuration));
            Assert.That(actual.ProjectedFrame(Camera, false), Is.EqualTo(expected.ProjectedFrame(Camera, false)));
            Assert.That(ParticleValues(actual), Is.EqualTo(ParticleValues(expected)), "Include invisible random channels, births and velocity in the state comparison.");
            Assert.That(actual.IsDrained, Is.EqualTo(expected.IsDrained));
        });
    }

    private static object[] ParticleValues(TotalWarVfxSimulation simulation)
        => ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!)
            .Cast<object>().Select(particle => (object)particle.GetType().GetFields()
                .Where(field => field.FieldType.IsValueType).Select(field => field.GetValue(particle)).ToArray()).ToArray();

    // Synthetic DIAG controls test scheduling equivalence, not official visual acceptance.
    private static VfxConversion Conversion(bool model, bool events, bool trail, bool negative)
    {
        var document = VfxDocument.Read(model ? VfxModelVariantTests.Source(2) : VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        if (events && !model) emitter.SetAttributeValue("type", "continuous_rigid");
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 12));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .6f));
        Set(emitter, "MaximumParticles", "64", attribute: true);
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 2));
        if (negative) emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -1.7), new XAttribute("max", -1.7)));
        if (trail)
        {
            Set(emitter, "SpawnSpacing", ".1", attribute: true);
            document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
                new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", "true")))));
        }
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        Set(child.Root.Descendants("emitter").Single(), "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .4f));
        if (events)
        {
            Set(emitter, "EventEnable", "true", attribute: true);
            foreach (var name in new[] { "OnParticleSpawn", "OnParticleDeath" })
                emitter.Add(new XElement("property_event", new XAttribute("name", name), new XAttribute("vfx_name", "child"),
                    new XAttribute("track_position", "true"), new XAttribute("track_orientation_mode", "orientation"),
                    new XAttribute("track_scale", "true"), new XAttribute("track_colour", "true"), new XAttribute("track_alpha", "true"),
                    new XAttribute("track_lifetime", "false"), new XAttribute("spawn_event_time_offset", ".1")));
        }
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/sliced-interactive-seek.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters.Any(emitter => !emitter.IsEventTemplate), Is.True);
        Assert.That(conversion.Emitters.Any(emitter => emitter.Events.Count > 0), Is.EqualTo(events));
        return conversion;
    }

    private static void Set(XElement emitter, string name, string value, bool attribute = false)
    {
        emitter.Elements().Where(element => (string?)element.Attribute("name") == name).Remove();
        var element = new XElement("property", new XAttribute("name", name));
        if (attribute) element.SetAttributeValue("value", value); else element.Value = value;
        emitter.Add(element);
    }
}
