using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxLegacySpawnDirectionTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0f, 0f)]
    [TestCase(0f, 3f)]
    public void LegacySpread_EmitsAcrossTheHorizontalRing(float minimum, float maximum)
    {
        var source = Source(minimum, maximum);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/legacy-spawn.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var positions = simulation.Frame(Camera).Select(p => p.Transform.Translation).Where(p => p.Length() > .1f).ToArray();
        Assert.That(positions, Has.Length.GreaterThan(100));
        Assert.That(positions.All(p => p.Y >= -.000001f && p.Y / p.Length() <= MathF.Sin(MathHelper.ToRadians(maximum)) + .000001f), Is.True);
        Assert.That(positions.Any(p => p.X < -.2f) && positions.Any(p => p.X > .2f), Is.True);
        Assert.That(positions.Any(p => p.Z < -.2f) && positions.Any(p => p.Z > .2f), Is.True);
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void LegacyNinetyDegreeSpread_EmitsUpward()
    {
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(Source(90, 90), "vfx/vertical-spawn.xml", _ => [1])));
        simulation.Seek(1, Matrix.Identity);
        var positions = simulation.Frame(Camera).Select(p => p.Transform.Translation).Where(p => p.Length() > .1f).ToArray();
        Assert.That(positions.All(p => MathF.Abs(p.X) < .000001f && MathF.Abs(p.Z) < .000001f && p.Y > 0), Is.True);
    }

    [Test]
    public void LegacySpawn_SeekAdvanceAndReloadStayDeterministic()
    {
        var source = Source(0, 3);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/legacy-spawn.xml", _ => [1]));
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(1, Matrix.Identity);
        var incremental = new TotalWarVfxSimulation(conversion); incremental.Advance(.3f, Matrix.Identity); incremental.Advance(.7f, Matrix.Identity);
        Assert.That(incremental.Frame(Camera), Is.EqualTo(direct.Frame(Camera)));
        direct.Seek(.2f, Matrix.Identity); direct.Seek(1, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(incremental.Frame(Camera)));
    }

    [TestCase(0f, 1f, 0f)]
    [TestCase(90f, 0f, -1f)]
    [TestCase(180f, -1f, 0f)]
    public void FixedSpreadArc_UsesNativeClockwiseAzimuth(float angle, float x, float z)
    {
        var source = WithArc(angle, angle);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/directional-slash.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        var positions = simulation.Frame(Camera).Select(p => p.Transform.Translation).Where(p => p.Length() > .1f).ToArray();
        Assert.That(positions, Has.Length.GreaterThan(100));
        Assert.That(positions.All(p => Vector3.Distance(Vector3.Normalize(p), new(x, 0, z)) < .00001f), Is.True);
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void RangedSpreadArc_StaysInsideTheNativeSectorAndPreservesSeeking()
    {
        var conversion = TotalWarVfxConverter.Convert(new(WithArc(45, 135), "vfx/sector.xml", _ => [1]));
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(1, Matrix.Identity);
        var incremental = new TotalWarVfxSimulation(conversion); incremental.Advance(.3f, Matrix.Identity); incremental.Advance(.7f, Matrix.Identity);
        var positions = direct.Frame(Camera).Select(p => p.Transform.Translation).Where(p => p.Length() > .1f).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(positions.All(p => p.Z < 0 && MathF.Abs(p.X) <= -p.Z + .00001f), Is.True);
            Assert.That(positions.Any(p => p.X > .2f) && positions.Any(p => p.X < -.2f), Is.True);
            Assert.That(incremental.Frame(Camera), Is.EqualTo(direct.Frame(Camera)));
        });
    }

    private static byte[] WithArc(float minimum, float maximum)
    {
        var document = VfxDocument.Read(Source(0, 0));
        document.Root.Descendants("emitter").Single().Add(new XElement("ranged_property",
            new XAttribute("name", "SpreadArc"), new XAttribute("min", minimum), new XAttribute("max", maximum)));
        return document.Write();
    }

    private static byte[] Source(float minimum, float maximum)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0, rate: 256));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 8)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", minimum), new XAttribute("max", maximum)));
        return document.Write();
    }
}
