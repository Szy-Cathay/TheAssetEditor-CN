using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSpawnArcTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public void ZeroArc_EmitsUpwardForEveryAzimuth(float angle)
    {
        var source = Source(angle, 0);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/arc.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Matrix.Identity);
        Assert.That(Vector3.Distance(simulation.Frame(Camera).Single().Transform.Translation, Vector3.Up), Is.LessThan(.00001f));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    internal static byte[] Source(float angle, float arc)
    {
        var document = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", angle), new XAttribute("y", angle)),
            new XElement("property", new XAttribute("name", "SpawnArc"), new XAttribute("x", arc), new XAttribute("y", arc)));
        return document.Write();
    }
}
