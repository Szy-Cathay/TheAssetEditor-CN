using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxDistanceEmissionTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(10f)]
    public void PositiveSpacing_EmitsByDistanceIndependentlyOfTimeRate(float rate)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.5f, rate)));
        simulation.Seek(1, Matrix.Identity, time => Matrix.CreateTranslation(0, time * 2, 0));
        var positions = simulation.Frame(Camera).Select(p => p.Transform.Translation.Y).Order().ToArray();
        Assert.That(positions, Is.EqualTo(new[] { .5f, 1, 1.5f, 2 }).Within(.001));
    }

    [TestCase(null, true)]
    [TestCase("WORLD_SPACE", true)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", false)]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION", false)]
    public void SpriteSimulationSpace_DefaultLeavesTheTrailAtItsBirthPositions(string space, bool leavesTrail)
    {
        var document = VfxDocument.Read(Source(.5f, 1));
        var field = document.Root.Descendants("emitter").Single().Elements()
            .Single(p => (string?)p.Attribute("name") == "ParticleSimulationSpace");
        if (space == null) field.Remove(); else field.SetAttributeValue("value", space);
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        simulation.Seek(1, Matrix.Identity, time => Matrix.CreateTranslation(0, time * 2, 0));
        Assert.That(simulation.Frame(Camera).Select(p => p.Transform.Translation.Y).Order(),
            Is.EqualTo(leavesTrail ? new[] { .5f, 1, 1.5f, 2 } : new[] { 2f, 2, 2, 2 }).Within(.001));
    }

    [Test]
    public void StationaryEmitter_DoesNotCreateTimeBasedParticlesOrCatchUpAfterStopping()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.5f, 100)));
        simulation.Seek(1, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(2, Matrix.Identity, time => Matrix.CreateTranslation(0, Math.Clamp(time - 1, 0, 1), 0));
        Assert.That(simulation.ParticleCount, Is.EqualTo(2));
        simulation.Seek(3, Matrix.CreateTranslation(0, 1, 0));
        Assert.That(simulation.ParticleCount, Is.EqualTo(2));
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION")]
    public void NoModelHelper_LeavesChildParticlesAlongItsMotionAndStopsAtDeath(string space)
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var emitter = parent.Root.Descendants("emitter").Single();
        emitter.Add(Property("ParticleSimulationSpace", space),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxParticleEventTests.Curve(["x"], 2)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)));
        var child = Source(.5f, 0);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/helper.xml", _ => child));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.8f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Select(p => p.Transform.Translation.Y).Order(),
            Is.EqualTo(new[] { .5f, 1, 1.5f }).Within(.001));
        simulation.Seek(2, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Length, Is.InRange(3, 4));
        var count = simulation.ParticleCount;
        simulation.Seek(3, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(count));
    }

    [Test]
    public void RotatingReferenceOffset_ProducesAnArcAndReplaysDeterministically()
    {
        var bytes = Source(.1f, 1);
        var document = VfxDocument.Read(bytes);
        var composition = VfxDocument.CreateComposition();
        var edit = composition.AddReference("trail", document); edit.Apply(); edit.Selection.SetAttributeValue("pos_x", 2);
        var conversion = TotalWarVfxConverter.Convert(new(composition.Write(), "vfx/root.xml", _ => bytes));
        var simulation = new TotalWarVfxSimulation(conversion);
        static Matrix Attachment(float time) => Matrix.CreateRotationY(time * MathHelper.PiOver2);
        simulation.Seek(1, Matrix.Identity, Attachment);
        var first = simulation.Frame(Camera).Select(p => p.Transform.Translation).ToArray();
        Assert.That(first, Has.Length.InRange(30, 32));
        Assert.That(first.All(p => Math.Abs(p.Length() - 2) < .001), Is.True);
        simulation.Seek(0, Matrix.Identity, Attachment); simulation.Seek(.4f, Matrix.Identity, Attachment); simulation.Seek(1, Matrix.Identity, Attachment);
        Assert.That(simulation.Frame(Camera).Select(p => p.Transform.Translation), Is.EqualTo(first));
        Assert.That(document.Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void FiniteEmission_ClipsTheLastTickAndDoesNotMeasureTheInitialWorldOffset()
    {
        var document = VfxDocument.Read(Source(.5f, 1));
        var duration = document.Root.Descendants("emitter").Single().Elements().Single(p => (string)p.Attribute("name") == "LifeTime");
        duration.SetAttributeValue("min", .503); duration.SetAttributeValue("max", .503);
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        simulation.Seek(2, Matrix.Identity, time => Matrix.CreateTranslation(100 + time * 2, 0, 0));
        Assert.That(simulation.Frame(Camera).Select(p => p.Transform.Translation.X).Order(),
            Is.EqualTo(new[] { 100.5f, 101 }).Within(.001));
    }

    [Test]
    public void VerySmallSpacing_RemainsBoundedAndReportsRateLimiting()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(.0000001f, 0)));
        simulation.Seek(.01f, Matrix.Identity, time => Matrix.CreateTranslation(time * 1000, 0, 0));
        Assert.That(simulation.RateLimited, Is.True);
        Assert.That(simulation.ParticleCount, Is.InRange(1, 84));
    }

    [Test]
    public void ZeroSpacing_PreservesTheExistingTimeEmission()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(0, 2)));
        simulation.Seek(1, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(3));
    }

    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/trail.xml", _ => [1]));

    private static byte[] Source(float spacing, float rate)
    {
        var document = VfxDocument.Read(VfxParticleEventTests.Source(false));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Single(p => (string)p.Attribute("name") == "LifeTime").SetAttributeValue("min", 4);
        emitter.Elements().Single(p => (string)p.Attribute("name") == "LifeTime").SetAttributeValue("max", 4);
        emitter.Elements().Single(p => (string)p.Attribute("name") == "MaximumParticles").SetAttributeValue("value", 256);
        emitter.Elements().Single(p => (string)p.Attribute("name") == "ParticleLifeTime").Value = VfxParticleEventTests.Curve(["x"], 5);
        emitter.Elements().Single(p => (string)p.Attribute("name") == "EmissionRateCurve").Value = VfxParticleEventTests.Curve(["x"], rate);
        emitter.Add(Property("SpawnSpacing", spacing), Property("ParticleSimulationSpace", "WORLD_SPACE"));
        return document.Write();
    }

    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
}
