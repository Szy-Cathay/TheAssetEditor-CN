#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

// CPU-only single-variable controls; complete official META acceptance is separate.
public class VfxEmissionTailTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(false)]
    [TestCase(true)]
    public void OwnerEnd_StopsOnlyEmissionAndPreservesOriginalParticleAppearance(bool infinite)
    {
        var source = Source(infinite: infinite);
        var conversion = Convert(source);
        var control = new TotalWarVfxSimulation(conversion);
        var stopped = new TotalWarVfxSimulation(conversion); SetEnd(stopped, .25f);
        control.Seek(.25f, Matrix.Identity); stopped.Seek(.25f, Matrix.Identity);
        Assert.That(stopped.Frame(Camera), Is.EqualTo(control.Frame(Camera)), "Before End, birth randomness and appearance must be unchanged.");
        var born = Raw(stopped);
        stopped.Seek(.6f, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(Raw(stopped), Is.EquivalentTo(born), "End must not clear surviving particles or keep emitting.");
            Assert.That(Raw(stopped).All(p => Value<double>(p, "Birth") <= .25), Is.True);
            Assert.That(Raw(stopped).All(p => Value<float>(p, "Life") == 1), Is.True);
            Assert.That(stopped.Frame(Camera).All(p => p.Colour.W > 0 && p.AppearancePhase > .3f), Is.True);
            Assert.That(Drained(stopped), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
        stopped.Seek(1.3f, Matrix.Identity);
        Assert.That(stopped.ParticleCount, Is.Zero);
        Assert.That(Drained(stopped), Is.True, "Natural particle death, not CycleDuration, completes this owner.");
    }

    [TestCase(0f)]
    [TestCase(.253f)]
    [TestCase(.25f)]
    public void CutoffTick_NoRootBirthIsLaterThanEnd(float end)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(rate: 240))); SetEnd(simulation, end);
        simulation.Seek(.4f, Matrix.Identity);
        Assert.That(Raw(simulation).All(p => Value<double>(p, "Birth") <= end), Is.True);
        if (end == 0) Assert.That(simulation.ParticleCount, Is.Zero, "An empty interval must not spawn the initial root particle.");
        else Assert.That(simulation.ParticleCount, Is.GreaterThan(1));
    }

    [Test]
    public void PartialTick_IntegratesOnlyTheActiveFractionBeforeEnd()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(rate: 240))); SetEnd(simulation, .253f);
        simulation.Seek(.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(61), "Initial particle + floor(240 * .253), without adding the rest of the crossing tick.");
    }

    [Test]
    public void PartialTick_RootBirthTransformAndSpawnEventClockStayAtOrBeforeEnd()
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        SetProperty(parent, "InfiniteLife", true); SetProperty(parent, "MaximumParticles", 1024);
        SetProperty(parent, "EmissionRateCurve", 480f);
        var child = VfxParticleEventTests.Source(false);
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/cpu-owner-partial-event.xml", _ => child)));
        SetEnd(simulation, .253f);
        Matrix History(float t) => Matrix.CreateTranslation(t * 4, 0, 0);
        simulation.Seek(.3f, History(.3f), History);
        var roots = Raw(simulation).Where(p => Value<object>(Value<object>(p, "Emitter"), "Parent") == null).ToArray();
        Assert.That(roots, Has.Length.EqualTo(122));
        foreach (var root in roots)
        {
            var birth = Value<double>(root, "Birth");
            Assert.That(birth, Is.LessThanOrEqualTo(.253f));
            Assert.That(Value<Matrix>(root, "Root").Translation, Is.EqualTo(History((float)birth).Translation));
            var children = ((IEnumerable)root.GetType().GetField("EventInstances")!.GetValue(root)!).Cast<object>().ToArray();
            Assert.That(children, Has.Length.EqualTo(1));
            Assert.That(Value<float>(children[0], "Start"), Is.EqualTo((float)birth), "OnSpawn delay starts at the real clipped birth, not the following fixed tick.");
        }
        Assert.That(Raw(simulation).Any(p => Value<double>(p, "Birth") > .253f), Is.True, "Event children may continue after the root cutoff.");
        simulation.Seek(3.1f, History(3.1f), History);
        Assert.That(Drained(simulation), Is.True);
    }

    [Test]
    public void NegativePrewarm_EndAtZeroKeepsAlreadyBornTails()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(start: -.5f))); SetEnd(simulation, 0);
        simulation.Seek(.2f, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ParticleCount, Is.GreaterThan(1));
            Assert.That(Raw(simulation).All(p => Value<double>(p, "Birth") <= 0), Is.True);
            Assert.That(Raw(simulation).Any(p => Value<double>(p, "Birth") < -.2), Is.True);
            Assert.That(Drained(simulation), Is.False);
        });
        simulation.Seek(1.1f, Matrix.Identity);
        Assert.That(Drained(simulation), Is.True);
    }

    [Test]
    public void FutureRootAfterEnd_NeverSpawnsAndDoesNotDelayDrain()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(start: 2))); SetEnd(simulation, .25f);
        simulation.Seek(.3f, Matrix.Identity);
        Assert.That(Drained(simulation), Is.True);
        simulation.Seek(3, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void FutureUnstoppedRoot_IsNotDrainedJustBecauseItIsCurrentlyEmpty()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(start: 2, infinite: false)));
        simulation.Seek(.3f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Assert.That(Drained(simulation), Is.False);
        simulation.Seek(4.1f, Matrix.Identity);
        Assert.That(Drained(simulation), Is.True);
    }

    [Test]
    public void BurstWindow_EndInsideQuietGapDoesNotRestartTheNextBurst()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(burst: true))); SetEnd(simulation, .3f);
        simulation.Seek(.4f, Matrix.Identity); var count = simulation.ParticleCount;
        Assert.That(count, Is.GreaterThan(1));
        simulation.Seek(.8f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(count));
        Assert.That(Raw(simulation).All(p => Value<double>(p, "Birth") <= .3), Is.True);
    }

    [Test]
    public void DistanceEmission_StopsMovingBirthsButKeepsOriginalWorldTrail()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(spacing: .1f))); SetEnd(simulation, .253f);
        Matrix History(float t) => Matrix.CreateTranslation(t * 4, 0, 0);
        simulation.Seek(.4f, History(.4f), History); var count = simulation.ParticleCount;
        Assert.That(count, Is.EqualTo(10));
        simulation.Seek(.7f, History(.7f), History);
        Assert.That(simulation.ParticleCount, Is.EqualTo(count));
        Assert.That(Raw(simulation).All(p => Value<double>(p, "Birth") <= .253f), Is.True);
        Assert.That(simulation.Frame(Camera).All(p => p.Transform.Translation.X <= 1.01201f), Is.True);
    }

    [TestCase("OnParticleSpawn", .5f)]
    [TestCase("OnParticleDeath", 0f)]
    public void DelayedEventsAfterEnd_CompleteTheirAuthoredChildTails(string eventName, float delay)
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true", eventName, delay));
        SetProperty(parent, "ParticleLifeTime", .2f);
        var child = VfxParticleEventTests.Source(false);
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/cpu-owner-event.xml", _ => child)));
        SetEnd(simulation, .05f);
        simulation.Seek(eventName == "OnParticleDeath" ? .1f : .3f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
        Assert.That(Drained(simulation), Is.False, "Pending spawn/death children are future work even when no visible particles remain.");
        simulation.Seek(.7f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1), "The root cutoff must not discard pending spawn/death chains.");
        simulation.Seek(3, Matrix.Identity);
        Assert.That(Drained(simulation), Is.True);
    }

    [Test]
    public void InfiniteTrackedChild_StopsAtParentDeathAndDrainsItsOwnTail()
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        parent.Root.Descendants("property_event").Single().SetAttributeValue("track_lifetime", "true");
        SetProperty(parent, "ParticleLifeTime", .2f);
        var child = VfxDocument.Read(VfxParticleEventTests.Source(false));
        SetProperty(child, "InfiniteLife", true); SetProperty(child, "ParticleLifeTime", .3f);
        SetProperty(child, "MaximumParticles", 128); SetProperty(child, "EmissionRateCurve", 40f);
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/cpu-owner-tracked.xml", _ => child.Write())));
        SetEnd(simulation, .05f);
        simulation.Seek(.3f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Not.Empty);
        Assert.That(Drained(simulation), Is.False);
        simulation.Seek(.7f, Matrix.Identity);
        Assert.That(Drained(simulation), Is.True);
    }

    [Test]
    public void RewindAndChangingEnd_ReplayTheSameAuthoredState()
    {
        var conversion = Convert(Source()); var simulation = new TotalWarVfxSimulation(conversion); SetEnd(simulation, .25f);
        simulation.Seek(.7f, Matrix.Identity); var expected = simulation.Frame(Camera);
        simulation.Seek(.1f, Matrix.Identity); simulation.Seek(.7f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.EqualTo(expected));
        SetEnd(simulation, .5f); simulation.Seek(.7f, Matrix.Identity);
        var fresh = new TotalWarVfxSimulation(conversion); SetEnd(fresh, .5f); fresh.Seek(.7f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.EqualTo(fresh.Frame(Camera)), "Changing an already simulated cutoff must replay rather than retain stale births.");
        SetEnd(simulation, null); simulation.Seek(.7f, Matrix.Identity);
        var unstopped = new TotalWarVfxSimulation(conversion); unstopped.Seek(.7f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.EqualTo(unstopped.Frame(Camera)));
    }

    [Test]
    public void NullEnd_PreservesIncrementalAuthoredPlaybackExactly()
    {
        var conversion = Convert(Source(infinite: false, burst: true));
        var a = new TotalWarVfxSimulation(conversion); var b = new TotalWarVfxSimulation(conversion); SetEnd(b, null);
        foreach (var t in new[] { 0, .113f, .59f, 1.2f, 2.1f, .1f, .8f })
        {
            a.Seek(t, Matrix.Identity); b.Seek(t, Matrix.Identity);
            Assert.That(b.Frame(Camera), Is.EqualTo(a.Frame(Camera)));
            Assert.That(b.CycleDuration, Is.EqualTo(a.CycleDuration));
        }
    }

    [Test]
    public void FutureStop_DoesNotRecreateAlreadyBornParticles()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source()));
        simulation.Seek(.25f, Matrix.Identity); var born = Raw(simulation);
        SetEnd(simulation, .3f); simulation.Seek(.25f, Matrix.Identity);
        Assert.That(Raw(simulation), Is.EquivalentTo(born));
        simulation.Seek(.6f, Matrix.Identity);
        Assert.That(born.All(p => Raw(simulation).Contains(p)), Is.True);
        Assert.That(Raw(simulation).All(p => Value<double>(p, "Birth") <= .3f), Is.True);
    }

    [Test]
    public void MeshAppearanceLoop_StopsLoopingAtOwnerEndThenFinishesTheOriginalLife()
    {
        var conversion = Convert(Source());
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.MeshLoop))!.SetValue(conversion.Emitters[0], new Vector2(.3f, .7f));
        var simulation = new TotalWarVfxSimulation(conversion); SetEnd(simulation, 2f);
        simulation.Seek(2.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).First().AppearancePhase, Is.GreaterThan(0));
        Assert.That(Drained(simulation), Is.False);
        simulation.Seek(3.1f, Matrix.Identity);
        Assert.That(Drained(simulation), Is.True, "Stopping the appearance loop must release a looping model without changing Life.");
    }

    [Test]
    public void ContinuousEditorTrail_RespectsExplicitOwnerEndAndKeepsTheTail()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(infinite: false, spacing: .1f))); SetEnd(simulation, 1.25f);
        Matrix History(float t) => Matrix.CreateTranslation(t * 4, 0, 0);
        simulation.SeekContinuousTrail(1.4f, History(1.4f), History);
        Assert.That(Raw(simulation).Any(p => Value<double>(p, "Birth") > 1), Is.True, "The explicit continuous strategy remains active past authored emitter duration.");
        Assert.That(Raw(simulation).All(p => Value<double>(p, "Birth") <= 1.25), Is.True);
        simulation.SeekContinuousTrail(2.4f, History(2.4f), History);
        Assert.That(Drained(simulation), Is.True);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void NonFiniteEnd_IsRejectedWithoutChangingAuthoredState(float end)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source()));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.EmissionEndTimeSeconds = end);
        Assert.That(simulation.EmissionEndTimeSeconds, Is.Null);
    }

    [Test]
    public void PreviewReload_PreservesTheInstanceBoundaryWithoutWritingItIntoTheSnapshot()
    {
        using var preview = new EffekseerVfxPreview(null!);
        var document = VfxDocument.CreateComposition(); var bytes = document.Write();
        preview.EmissionEndTimeSeconds = .25f;
        preview.Reload(new(bytes, "vfx/empty-owner.xml", _ => null));
        preview.Seek(1, Matrix.Identity);
        Assert.That(preview.EmissionEndTimeSeconds, Is.EqualTo(.25f));
        Assert.That(preview.IsDrained, Is.True);
        Assert.That(document.Write(), Is.EqualTo(bytes));
        preview.EmissionEndTimeSeconds = null;
        preview.Reload(new(bytes, "vfx/empty-owner.xml", _ => null));
        Assert.That(preview.EmissionEndTimeSeconds, Is.Null);
    }

    private static void SetEnd(TotalWarVfxSimulation simulation, float? end)
        => simulation.EmissionEndTimeSeconds = end;
    private static bool Drained(TotalWarVfxSimulation simulation)
        => simulation.IsDrained;
    private static object[] Raw(TotalWarVfxSimulation simulation)
        => ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!).Cast<object>().ToArray();
    private static T Value<T>(object particle, string name) => (T)particle.GetType().GetField(name)!.GetValue(particle)!;

    private static VfxConversion Convert(byte[] bytes)
    {
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/cpu-owner-tail.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics));
        return conversion;
    }

    private static byte[] Source(bool infinite = true, float start = 0, float rate = 40, bool burst = false, float spacing = 0)
    {
        var doc = VfxDocument.Read(VfxParticleEventTests.Source(false));
        SetProperty(doc, "InfiniteLife", infinite); SetProperty(doc, "ParticleLifeTime", 1f);
        SetProperty(doc, "MaximumParticles", 1024); SetProperty(doc, "EmissionRateCurve", rate);
        var emitter = doc.Root.Descendants("emitter").Single();
        var duration = emitter.Elements().Single(x => (string?)x.Attribute("name") == "LifeTime");
        duration.SetAttributeValue("min", 1); duration.SetAttributeValue("max", 1);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", start), new XAttribute("max", start)),
            new XElement("property", new XAttribute("name", "SpawnSpacing"), new XAttribute("value", spacing)),
            new XElement("property", new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", "WORLD_SPACE")));
        if (burst) emitter.Add(new XElement("ranged_property", new XAttribute("name", "BurstInterval"), new XAttribute("min", .4f), new XAttribute("max", .4f)),
            new XElement("ranged_property", new XAttribute("name", "BurstIntervalLength"), new XAttribute("min", .1f), new XAttribute("max", .1f)));
        return doc.Write();
    }

    private static void SetProperty(VfxDocument doc, string name, object value)
    {
        var property = doc.Root.Descendants("emitter").Single().Elements().Single(x => (string?)x.Attribute("name") == name);
        property.RemoveNodes(); property.SetAttributeValue("value", value);
    }
}
