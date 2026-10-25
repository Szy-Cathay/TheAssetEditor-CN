using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxLegacySpawnArcCombinationTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void ZeroArcWithExplicitSpread_ReproducesTheCapturedHorizontalPlaneAtAuthoredSpeed()
    {
        // Official arc360 blood in R69 frame11616 has zero Y velocity and radial XZ speed 10..20.
        // This CPU control isolates that spawn contract from its force, texture and lifetime curves.
        var source = Source();
        var conversion = Convert(source);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var particles = simulation.Frame(Camera).Where(particle => particle.Time > .1f).ToArray();
        Assert.That(particles, Has.Length.GreaterThan(100));
        var positions = particles.Select(particle => particle.Transform.Translation).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(positions.All(position => Math.Abs(position.Y) < .00001f), Is.True);
            Assert.That(particles.All(particle => Math.Abs(particle.Transform.Translation.Length() / particle.Time - 20) < .0001f), Is.True);
            Assert.That(positions.Any(position => position.X < -1) && positions.Any(position => position.X > 1), Is.True);
            Assert.That(positions.Any(position => position.Z < -1) && positions.Any(position => position.Z > 1), Is.True);
            Assert.That(conversion.Diagnostics.Any(item => item.Field is "SpawnArc" or "SpreadArc"
                && item.ReasonKey == "Vfx.Preview.Unmapped"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void ZeroArcWithExplicitSector_RestrictsDirectionAndPreservesSeeking()
    {
        var source = Source(45, 135);
        var conversion = Convert(source);
        var direct = new TotalWarVfxSimulation(conversion);
        direct.Seek(1, Matrix.Identity);
        var incremental = new TotalWarVfxSimulation(conversion);
        incremental.Advance(.3f, Matrix.Identity);
        incremental.Advance(.7f, Matrix.Identity);
        var positions = direct.Frame(Camera).Select(particle => particle.Transform.Translation)
            .Where(position => position.Length() > .1f).ToArray();
        Assert.That(positions, Has.Length.GreaterThan(100));
        Assert.Multiple(() =>
        {
            Assert.That(positions.All(position => Math.Abs(position.Y) < .00001f && position.Z < 0
                && Math.Abs(position.X) <= -position.Z + .00001f), Is.True);
            Assert.That(positions.Any(position => position.X < -1) && positions.Any(position => position.X > 1), Is.True);
            Assert.That(incremental.Frame(Camera), Is.EqualTo(direct.Frame(Camera)));
        });
        direct.Seek(.2f, Matrix.Identity);
        direct.Seek(1, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(incremental.Frame(Camera)));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void ZeroArcWithoutExplicitSpread_KeepsTheExistingPoleMotion()
    {
        var document = VfxDocument.Read(Source());
        document.Root.Descendants("emitter").Single().Elements()
            .Single(node => (string)node.Attribute("name") == "SpawnSpread").Remove();
        var conversion = Convert(document.Write());
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var particles = simulation.Frame(Camera).Where(particle => particle.Time > .1f).ToArray();
        Assert.That(particles, Has.Length.GreaterThan(100));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().LegacySpreadSpawn, Is.False);
            Assert.That(particles.All(particle => particle.Transform.Translation.Y > 0
                && Math.Abs(particle.Transform.Translation.X) < .00001f
                && Math.Abs(particle.Transform.Translation.Z) < .00001f), Is.True);
        });
    }

    [Test]
    public void NonzeroArcWithExplicitSpread_DoesNotClaimTheLegacyCombination()
    {
        var document = VfxDocument.Read(Source());
        document.Root.Descendants("emitter").Single().Elements()
            .Single(node => (string)node.Attribute("name") == "SpawnArc").SetAttributeValue("y", 1);
        var conversion = Convert(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().LegacySpreadSpawn, Is.False);
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "SpawnArc"
                && item.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "SpreadArc"
                && item.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
        });
    }

    [Test]
    public void ExplicitSpawnAngleWithZeroArc_IsNotOverriddenByTheNewCombination()
    {
        var document = VfxDocument.Read(Source());
        document.Root.Descendants("emitter").Single().Add(new XElement("property",
            new XAttribute("name", "SpawnAngle"), new XAttribute("x", 90), new XAttribute("y", 90)));
        var conversion = Convert(document.Write());
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var positions = simulation.Frame(Camera).Select(particle => particle.Transform.Translation)
            .Where(position => position.Length() > .1f).ToArray();
        Assert.That(positions, Has.Length.GreaterThan(100));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().LegacySpreadSpawn, Is.False);
            Assert.That(positions.All(position => position.X > 0 && Math.Abs(position.Y) < .00001f
                && Math.Abs(position.Z) < .00001f), Is.True);
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "SpawnArc"
                && item.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
        });
    }

    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(
        new(source, "vfx/native-zero-arc-combination.xml", _ => [1], "1"));

    private static byte[] Source(float sectorMinimum = 0, float sectorMaximum = 360)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0, rate: 256));
        document.Root.Descendants("emitter").Single().Add(
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), new XAttribute("value", 20)),
            new XElement("property", new XAttribute("name", "SpawnArc"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 0), new XAttribute("max", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpreadArc"), new XAttribute("min", sectorMinimum), new XAttribute("max", sectorMaximum)));
        return document.Write();
    }
}
