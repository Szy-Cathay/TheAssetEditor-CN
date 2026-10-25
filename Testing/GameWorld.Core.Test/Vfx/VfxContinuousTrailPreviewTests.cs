#nullable enable
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

// CPU diagnostics only: these fixtures do not establish official-effect visual fidelity.
public class VfxContinuousTrailPreviewTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void ShortFiniteDistanceTrail_ContinuesBirthsBeyondItsAuthoredEndWithoutExtendingParticleLife()
    {
        var bytes = Source(.125f, .5f, .1f);
        var conversion = Convert(bytes);
        var simulation = new TotalWarVfxSimulation(conversion);
        Continuous(simulation, 2, Moving);
        var particles = simulation.Frame(Camera);
        Assert.Multiple(() =>
        {
            Assert.That(particles, Has.Length.InRange(9, 11), "Only the last half-second of the moving trail should remain alive.");
            Assert.That(particles.All(p => p.Transform.Translation.X >= 2.99f && p.Transform.Translation.X <= 4.001f), Is.True);
            Assert.That(particles.All(p => p.Time >= 0 && p.Time < .5001f), Is.True, "The original particle lifetime must still expire.");
            Assert.That(conversion.Emitters[0].Infinite, Is.False);
            Assert.That(conversion.Emitters[0].Duration, Is.EqualTo(new VfxRange(.125f, .125f)));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void StoppingTheEmitter_StopsDistanceBirthsAndLetsTheExistingTailExpire()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, .1f)));
        static Matrix Motion(float time) => Matrix.CreateTranslation(Math.Min(time, 1) * 2, 0, 0);
        Continuous(simulation, 1, Motion);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
        Continuous(simulation, 1.5f, Motion);
        Assert.That(simulation.ParticleCount, Is.Zero, "A stationary root must not produce time-rate catch-up or immortal particles.");
    }

    [Test]
    public void ExplicitTrailMode_ContinuesTimeRateEmissionWithoutMakingParticlesImmortal()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, 0, true)));
        Continuous(simulation, 2, Moving);
        var particles = simulation.Frame(Camera);
        Assert.That(particles, Has.Length.InRange(9, 11));
        Assert.That(particles.All(p => p.Time < .2501f), Is.True);
    }

    [Test]
    public void OrdinaryOneShot_RemainsFiniteEvenWhenTheContinuousEntryIsCalled()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, 0)));
        Continuous(simulation, 2, Moving);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void MixedRoots_OnlyTrailBirthsContinueAndTheOneShotIsNotRepeated()
    {
        var trail = VfxDocument.Read(Source(.125f, .25f, .1f));
        var ordinary = VfxDocument.Read(Source(.125f, .25f, 0));
        var root = ordinary.Root.Descendants("emitter").Single();
        root.SetAttributeValue("id", "ordinary");
        trail.Root.Descendants("emitters").Single().Add(new XElement(root));
        var chain = ordinary.Root.Descendants("behaviour_chain").Single();
        chain.SetAttributeValue("id", "ordinary");
        trail.Root.Descendants("behaviour_chains").Single().Add(new XElement(chain));
        var conversion = Convert(trail.Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        var simulation = new TotalWarVfxSimulation(conversion);
        Continuous(simulation, 2, Moving);
        var particles = simulation.Frame(Camera);
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.All(p => p.Layer == 0), Is.True, "The second root is an authored finite one-shot.");
        Assert.That(conversion.Emitters.All(e => !e.Infinite), Is.True);
    }

    [Test]
    public void FiniteTrailEmissionCurve_RepeatsItsAuthoredPhaseInsteadOfClampingToZeroAtTheEnd()
    {
        var source = VfxDocument.Read(Source(.125f, .25f, 0, true));
        source.Root.Descendants("emitter").Single().Elements().Single(p => (string?)p.Attribute("name") == "EmissionRateCurve").Value
            = "x_min,0,1;0,80,linear,linear,0,0,0,0,0;1,0,linear,linear,0,0,0,0,0:x,0;0,0";
        var conversion = Convert(source.Write());
        var simulation = new TotalWarVfxSimulation(conversion);
        Continuous(simulation, 2, Moving);
        Assert.That(simulation.ParticleCount, Is.InRange(9, 11), "Two authored periods fit in the original quarter-second particle life.");
        Continuous(simulation, 2.0625f, Moving);
        Assert.That(simulation.ParticleCount, Is.InRange(9, 11));
        Assert.That(conversion.Emitters[0].Infinite, Is.False);
    }

    [Test]
    public void EventChildTrail_KeepsItsFiniteWindowWhileAnIndependentMotionRootContinues()
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var rootTrail = VfxDocument.Read(Source(.125f, .25f, .1f));
        var emitter = rootTrail.Root.Descendants("emitter").Single(); emitter.SetAttributeValue("id", "motion_root");
        parent.Root.Descendants("emitters").Single().Add(new XElement(emitter));
        var chain = rootTrail.Root.Descendants("behaviour_chain").Single(); chain.SetAttributeValue("id", "motion_root");
        parent.Root.Elements("vfx").Single().Add(new XElement("behaviour_chains", new XElement(chain)));
        var child = Source(.125f, .25f, .1f);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/cpu-event-parent.xml", _ => child));
        Assert.That(conversion.Emitters.Any(e => e.IsEventTemplate && e.SpawnSpacing > 0), Is.True);
        var rootLayer = conversion.Emitters.IndexOf(conversion.Emitters.Single(e => e.Name == "motion_root"));
        Assert.That(rootLayer, Is.GreaterThanOrEqualTo(0));
        var simulation = new TotalWarVfxSimulation(conversion);
        Continuous(simulation, .1f, Moving);
        Assert.That(simulation.Frame(Camera).Any(p => p.Layer != rootLayer), Is.True, "The finite event child must actually have emitted before the expiry assertion.");
        Continuous(simulation, 2, Moving);
        var particles = simulation.Frame(Camera);
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.All(p => p.Layer == rootLayer), Is.True, "Continuing the independent root cannot extend an event child's emitter or particle lifetime.");
        Assert.That(conversion.Emitters.Where(e => e.IsEventTemplate).All(e => !e.Infinite), Is.True);
    }

    [Test]
    public void ExistingSeek_DefaultStillStopsTheSameFiniteTrailAndExplicitRestartRestoresIt()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, .1f)));
        simulation.Seek(2, Moving(2), Moving);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Continuous(simulation, 0, Moving);
        Continuous(simulation, 2, Moving);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
        simulation.Seek(0, Moving(0), Moving);
        simulation.Seek(2, Moving(2), Moving);
        Assert.That(simulation.ParticleCount, Is.Zero, "The opt-in cannot stick to ordinary Seek calls.");
    }

    [Test]
    public void PreviewRuntime_ForwardsTheExplicitMotionEntryAndRetainsTheAuthoredCycleWithoutAGpu()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, .1f)));
        using var runtime = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, simulation);
        IVfxPreview preview = runtime;
        Assert.That(preview.HasMotionTrailEmitters, Is.True);
        var authoredDuration = preview.CycleDuration;
        preview.SeekContinuousTrail(2, Moving(2), Moving);
        Assert.That(preview.ParticleCount, Is.GreaterThan(0));
        Assert.That(preview.CycleDuration, Is.EqualTo(authoredDuration));
        preview.Seek(0, Moving(0), Moving);
        preview.Seek(2, Moving(2), Moving);
        Assert.That(preview.ParticleCount, Is.Zero);
    }

    [Test]
    public void ContinuousTrail_CrossesTheOldSixHundredSecondSeekLimitWithoutRestarting()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, .1f)));
        Continuous(simulation, 600.25f, Moving);
        var particles = simulation.Frame(Camera);
        Assert.That(simulation.Time, Is.EqualTo(600.25f));
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.All(p => p.Transform.Translation.X > 1199.9f), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Seek(600.25f, Matrix.Identity));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void ContinuousTrail_RejectsInvalidTimes(float seconds)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.125f, .25f, .1f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Continuous(simulation, seconds, Moving));
    }

    private static Matrix Moving(float seconds) => Matrix.CreateTranslation(seconds * 2, 0, 0);

    private static void Continuous(TotalWarVfxSimulation simulation, float seconds, Func<float, Matrix> history)
    {
        var method = typeof(TotalWarVfxSimulation).GetMethod("SeekContinuousTrail", [typeof(float), typeof(Matrix), typeof(Func<float, Matrix>)]);
        if (method == null)
        {
            // Before the fix, exercise the same finite source through the existing production entry.
            simulation.Seek(seconds, history(seconds), history);
            return;
        }
        try { method.Invoke(simulation, [seconds, history(seconds), history]); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static VfxConversion Convert(byte[] bytes)
    {
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/cpu-continuous-trail.xml", _ => [1]));
        Assert.That(conversion.Emitters, Is.Not.Empty, string.Join(";", conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return conversion;
    }

    private static byte[] Source(float duration, float life, float spacing, bool trailMode = false)
    {
        var modifiers = trailMode ? new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
            new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-aligned with stretching factor")),
            new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", "true"))).ToString(SaveOptions.DisableFormatting) : "";
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(modifiers));
        var emitter = document.Root.Descendants("emitter").Single();
        if (trailMode) emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", 1)));
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", life);
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 40);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", duration), new XAttribute("max", duration)),
            new XElement("property", new XAttribute("name", "SpawnSpacing"), new XAttribute("value", spacing)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 256)),
            new XElement("property", new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", "WORLD_SPACE")));
        return document.Write();
    }
}
