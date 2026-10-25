#nullable enable
using System.Globalization;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSpawnRadiusTests
{
    private const float RadiusTolerance = .00001f;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(-.04f)]
    [TestCase(.04f)]
    public void FixedEqualRadius_SpawnsAnEmitterPlaneRing(float authoredRadius)
    {
        // R70 native frame4940 places equal -.04 radii on a .04 ring, including newborns.
        // Unequal or mixed negative radii are outside that captured contract.
        var positions = Positions(Source(authoredRadius, authoredRadius));
        var radii = positions.Select(position => new Vector2(position.X, position.Z).Length()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Length.GreaterThan(200));
            Assert.That(positions.All(position => Math.Abs(position.Y) < RadiusTolerance), Is.True);
            Assert.That(radii, Is.All.InRange(.04f - RadiusTolerance, .04f + RadiusTolerance));
            Assert.That(positions.Any(position => position.X < -.02f) && positions.Any(position => position.X > .02f), Is.True);
            Assert.That(positions.Any(position => position.Z < -.02f) && positions.Any(position => position.Z > .02f), Is.True);
        });
    }

    [Test]
    public void PositiveAnnulus_PreservesItsHoleAndOuterBoundary()
    {
        var positions = Positions(Source(.02f, .06f));
        var radii = positions.Select(position => new Vector2(position.X, position.Z).Length()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Length.GreaterThan(200));
            Assert.That(positions.All(position => Math.Abs(position.Y) < RadiusTolerance), Is.True);
            Assert.That(radii, Is.All.InRange(.02f - RadiusTolerance, .06f + RadiusTolerance));
            Assert.That(radii.Any(radius => radius < .03f) && radii.Any(radius => radius > .055f), Is.True);
            Assert.That(positions.Any(position => position.X < -.03f) && positions.Any(position => position.X > .03f), Is.True);
            Assert.That(positions.Any(position => position.Z < -.03f) && positions.Any(position => position.Z > .03f), Is.True);
        });
    }

    [Test]
    public void ZeroRadius_KeepsEveryParticleAtTheEmitterOrigin()
    {
        var positions = Positions(Source(0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(positions, Has.Length.GreaterThan(200));
            Assert.That(positions, Is.All.EqualTo(Vector3.Zero));
        });
    }

    [Test]
    public void FixedNegativeRadius_SeekingBackAndForwardRepeatsTheSameParticles()
    {
        var conversion = Convert(Source(-.04f, -.04f));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var expected = simulation.Frame(Camera);
        Assert.That(expected, Has.Length.GreaterThan(200));
        simulation.Seek(.2f, Matrix.Identity);
        simulation.Seek(1, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.EqualTo(expected));
        var replay = new TotalWarVfxSimulation(conversion);
        replay.Seek(1, Matrix.Identity);
        Assert.That(replay.Frame(Camera), Is.EqualTo(expected));
    }

    [Test]
    public void FixedNegativeRadius_ConversionAndSimulationPreserveTheAuthoredBytes()
    {
        var source = Source(-.04f, -.04f);
        var original = source.ToArray();
        var conversion = Convert(source);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().SpawnRadius.Sample("inner_radius", .5, .5f), Is.EqualTo(-.04f));
            Assert.That(conversion.Emitters.Single().SpawnRadius.Sample("outer_radius", .5, .5f), Is.EqualTo(-.04f));
            Assert.That(source, Is.EqualTo(original));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(original));
        });
    }

    private static Vector3[] Positions(byte[] source)
    {
        var simulation = new TotalWarVfxSimulation(Convert(source));
        simulation.Seek(1, Matrix.Identity);
        return simulation.Frame(Camera).Select(particle => particle.Transform.Translation).ToArray();
    }

    private static VfxConversion Convert(byte[] source) =>
        TotalWarVfxConverter.Convert(new(source, "vfx/cpu-fixed-spawn-radius-diagnostic.xml", _ => [1], "0"));

    private static byte[] Source(float inner, float outer)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0, rate: 256));
        document.Root.Descendants("emitter").Single().Add(
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), new XAttribute("value", 0)),
            new XElement("property", new XAttribute("name", "SpawnAreaCurve"),
                $"inner_radius,0,0;0,{inner.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1|" +
                $"outer_radius,0,0;0,{outer.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:" +
                "inner_radius,0;0,0|outer_radius,0;1,1"));
        return document.Write();
    }
}
