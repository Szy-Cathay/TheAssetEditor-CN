#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxInheritedMotionTests
{
    [TestCase(0f)]
    [TestCase(3f)]
    public void WorldMotion_AddsScaledInheritedVelocityAndIntegratesItAfterBirth(float authoredSpeed)
    {
        var simulation = Simulate(Source(authoredSpeed: authoredSpeed));
        simulation.Seek(.5f, Moving(.5f), Moving);
        var particle = Particles(simulation).Single(p => Math.Abs(p.Birth - .1) < .00001);
        var expected = new Vector3(4, authoredSpeed, 0);
        AssertVector(particle.WorldVelocity, expected);
        AssertVector(particle.WorldPosition, particle.Root.Translation + expected * (float)(.5 - particle.Birth));
    }

    [Test]
    public void StationaryNodeAndItsFirstLargeWorldOffset_DoNotProduceInheritedSpeed()
    {
        var simulation = Simulate(Source());
        var root = Matrix.CreateTranslation(1000, 50, -200);
        simulation.Seek(0, root);
        Assert.That(Particles(simulation), Has.Length.EqualTo(1));
        AssertVector(Particles(simulation).Single().WorldVelocity, Vector3.Zero);
        simulation.Seek(.5f, root);
        Assert.That(Particles(simulation), Has.Length.GreaterThan(1));
        foreach (var particle in Particles(simulation)) AssertVector(particle.WorldVelocity, Vector3.Zero);
    }

    [Test]
    public void RootRotationAndScale_DoNotRotateOrMultiplyTheWorldInheritedVelocityAgain()
    {
        static Matrix Root(float time) => Matrix.CreateScale(3) * Matrix.CreateRotationZ(MathHelper.PiOver2)
            * Matrix.CreateTranslation(20 * time, 0, 0);
        var simulation = Simulate(Source());
        simulation.Seek(.5f, Root(.5f), Root);
        var particle = Particles(simulation).Single(p => Math.Abs(p.Birth - .1) < .00001);
        AssertVector(particle.WorldVelocity, new(4, 0, 0));
        AssertVector(particle.WorldPosition, particle.Root.Translation + new Vector3(4, 0, 0) * (float)(.5 - particle.Birth));
        Assert.That(simulation.VelocityScaleUnsupported, Is.False);
    }

    [Test]
    public void SeekingBackwardAndReplaying_RebuildsTheSameInheritedMotion()
    {
        var bytes = Source(); var sourceHash = System.Security.Cryptography.SHA256.HashData(bytes);
        var simulation = Simulate(bytes);
        simulation.Seek(1.05f, Moving(1.05f), Moving);
        var expected = Particles(simulation);
        simulation.Seek(.2f, Moving(.2f), Moving);
        simulation.Seek(.7f, Moving(.7f), Moving);
        simulation.Seek(1.05f, Moving(1.05f), Moving);
        Assert.That(Particles(simulation), Is.EqualTo(expected));
        Assert.That(System.Security.Cryptography.SHA256.HashData(bytes), Is.EqualTo(sourceHash));
    }

    [Test]
    public void NodeMotionIsUpdatedBetweenBirths_NewParticlesStopInheritingAfterTheNodeStops()
    {
        static Matrix Root(float time) => Matrix.CreateTranslation(20 * Math.Min(time, .6f), 0, 0);
        var simulation = Simulate(Source(rate: 2));
        simulation.Seek(1.05f, Root(1.05f), Root);
        var particles = Particles(simulation);
        AssertVector(particles.Single(p => Math.Abs(p.Birth - .5) < .00001).WorldVelocity, new(4, 0, 0));
        AssertVector(particles.Single(p => Math.Abs(p.Birth - 1) < .00001).WorldVelocity, Vector3.Zero);
    }

    [Test]
    public void PausedAttachmentTeleport_UpdatesThePositionBaselineWithoutCreatingASpeedPulse()
    {
        var simulation = Simulate(Source());
        const float pauseTime = .503f;
        simulation.SeekContinuousTrail(pauseTime, Moving(pauseTime), Moving);
        var before = MotionVelocity(simulation);
        static Matrix Teleported(float time) => Matrix.CreateTranslation(1000 + 20 * (time - pauseTime), 0, 0);
        simulation.SeekContinuousTrail(pauseTime, Teleported(pauseTime), Teleported);
        AssertVector(MotionVelocity(simulation), before);
        simulation.SeekContinuousTrail(.65f, Teleported(.65f), Teleported);
        foreach (var particle in Particles(simulation).Where(p => p.Birth > pauseTime))
        {
            // Native position inputs are floats; at x=1000 their finite difference is not exactly 20.
            var current = Teleported((float)particle.Birth).Translation;
            var previous = Teleported((float)(particle.Birth - 1.0 / 120)).Translation;
            var expected = (current - previous) / (float)(1.0 / 120) * .2f;
            AssertVector(particle.WorldVelocity, expected);
            Assert.That(particle.WorldVelocity.Length(), Is.LessThan(4.01f), "A paused teleport must not become a speed pulse.");
        }
        Assert.That(Particles(simulation).Count(p => p.Birth > pauseTime), Is.GreaterThan(0));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(1f)]
    public void DirectionBlend_InterpolatesUnitDirectionsWithoutNormalizingTheResult(float blend)
    {
        var simulation = Simulate(Source(blend: blend));
        simulation.Seek(.5f, Moving(.5f), Moving);
        var particle = Particles(simulation).Single(p => Math.Abs(p.Birth - .1) < .00001);
        var expected = new Vector3(4 * (1 - blend), 4 * blend, 0);
        AssertVector(particle.WorldVelocity, expected);
        Assert.That(particle.WorldVelocity.Length(), Is.EqualTo(expected.Length()).Within(.0001f),
            "The half blend has length sqrt(8), rather than being normalized back to inherited speed 4.");
    }

    [Test]
    public void ReferenceSpeed_ConvertsSourceVelocityToTheManagedClockExactlyOnce()
    {
        var child = Source(authoredSpeed: 3);
        var composition = VfxDocument.CreateComposition();
        var reference = composition.AddReference("vfx/inherited-child.xml", VfxDocument.Read(child)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", 2);
        var conversion = TotalWarVfxConverter.Convert(new(composition.Write(), "vfx/inherited-root.xml",
            path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child : [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.5f, Moving(.5f), Moving);
        var particle = Particles(simulation).Single(p => Math.Abs(p.Birth - .1) < .00001);
        AssertVector(particle.WorldVelocity, new(8, 6, 0));
        AssertVector(particle.WorldPosition, particle.Root.Translation + new Vector3(8, 6, 0) * (float)(.5 - particle.Birth));
    }

    [Test]
    public void MalformedAndNonFiniteInheritedFields_RejectTheLayerInsteadOfReplacingThemWithZero()
    {
        foreach (var (field, value) in new[] { ("SpawnInheritedSpeed", "invalid"), ("SpawnInheritedSpeed", "NaN"), ("SpawnInheritedDirectionBlend", "Infinity") })
        {
            var document = VfxDocument.Read(Source());
            document.Root.Descendants("property").Single(p => (string?)p.Attribute("name") == field).SetAttributeValue("value", value);
            var bytes = document.Write();
            var conversion = Convert(bytes);
            Assert.That(conversion.Emitters, Is.Empty, field + "=" + value);
            Assert.That(conversion.Diagnostics.Any(d => d.Field.Contains(field, StringComparison.Ordinal)
                && d.ReasonKey is "Vfx.Preview.InvalidData" or "Vfx.Preview.RangeUnsupported"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        }
    }

    [Test]
    public void NegativeInheritedMultiplier_PreservesItsSignAndTheSourceBytes()
    {
        var bytes = Source(factor: -.2f);
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters.Single().SpawnInheritedSpeed, Is.EqualTo(-.2f));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.5f, Moving(.5f), Moving);
        AssertVector(Particles(simulation).Single(p => Math.Abs(p.Birth - .1) < .00001).WorldVelocity, new(-4, 0, 0));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void AnimatedEmitterPosition_DoesNotBecomeCommonNodeInheritedMotion()
    {
        var document = VfxDocument.Read(Source());
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "EmitterPosition"),
            "x,0,1;0,0,linear,linear,0,0,0,0,0;1,100,linear,linear,0,0,0,0,0|y,0,0;0,0,linear,linear,0,0,0,0,1|z,0,0;0,0,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2"));
        var simulation = Simulate(document.Write());
        simulation.Seek(.5f, Matrix.Identity);
        var particles = Particles(simulation);
        Assert.That(particles.Max(p => p.WorldPosition.X) - particles.Min(p => p.WorldPosition.X), Is.GreaterThan(5));
        foreach (var particle in particles) AssertVector(particle.WorldVelocity, Vector3.Zero);
    }

    private static byte[] Source(float factor = .2f, float blend = 0, float authoredSpeed = 0, float rate = 20)
    {
        var document = VfxDocument.Read(VfxParticleEventTests.Source(false));
        var emitter = document.Root.Descendants("emitter").Single();
        var duration = emitter.Elements().Single(p => (string?)p.Attribute("name") == "LifeTime");
        duration.SetAttributeValue("min", 4); duration.SetAttributeValue("max", 4);
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "MaximumParticles").SetAttributeValue("value", 256);
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "ParticleLifeTime").Value = VfxParticleEventTests.Curve(["x"], 5);
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "EmissionRateCurve").Value = VfxParticleEventTests.Curve(["x"], rate);
        emitter.Add(Property("ParticleSimulationSpace", "WORLD_SPACE"), Property("SpawnInheritedSpeed", factor),
            Property("SpawnInheritedDirectionBlend", blend),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxParticleEventTests.Curve(["x"], authoredSpeed)));
        return document.Write();
    }

    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/inherited.xml", _ => [1]));
    private static TotalWarVfxSimulation Simulate(byte[] bytes)
    {
        var conversion = Convert(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        return new(conversion);
    }

    private static Matrix Moving(float time) => Matrix.CreateTranslation(20 * time, 0, 0);
    private static void AssertVector(Vector3 actual, Vector3 expected, float tolerance = .0001f)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance), $"actual={actual}; expected={expected}");
    private static object Field(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
    private static Vector3 MotionVelocity(TotalWarVfxSimulation simulation)
        => (Vector3)Field(((IEnumerable)Field(simulation, "_emitters")).Cast<object>().Single(), "MotionVelocity");
    private static ParticleSnapshot[] Particles(TotalWarVfxSimulation simulation)
        => ((IEnumerable)Field(simulation, "_particles")).Cast<object>().Select(p => new ParticleSnapshot(
            (double)Field(p, "Birth"), (Vector3)Field(p, "Position"), (Vector3)Field(p, "Velocity"), (Matrix)Field(p, "Root"))).ToArray();

    private readonly record struct ParticleSnapshot(double Birth, Vector3 Position, Vector3 Velocity, Matrix Root)
    {
        public Vector3 WorldVelocity => Vector3.TransformNormal(Velocity, Root);
        public Vector3 WorldPosition => Vector3.Transform(Position, Root);
    }
}
