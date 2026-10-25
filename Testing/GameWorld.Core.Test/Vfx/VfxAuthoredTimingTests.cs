#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxAuthoredTimingTests
{
    [TestCase(600)]
    [TestCase(3600)]
    public void LongAuthoredLifetime_DoesNotPreventEarlyPreviewOrExtendItsSeekBudget(float lifetime)
    {
        var source = Source(lifetime);
        var conversion = Convert(source); Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(3, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ParticleCount, Is.EqualTo(1));
            Assert.That(conversion.Emitters[0].Life.Maximum, Is.EqualTo(lifetime));
            Assert.That(simulation.CycleDuration, Is.EqualTo(600));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Seek(601, Matrix.Identity));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void LongReferenceLifetimeBias_IsPreservedWithoutSimulatingAnHour()
    {
        var source = Source(3600); var doc = VfxDocument.CreateComposition();
        var edit = doc.AddReference("vfx/life-child.xml", VfxDocument.Read(source)); edit.Apply();
        foreach (var emitter in edit.Selection.Elements("emitter"))
        {
            emitter.SetAttributeValue("particle_lifetime_bias_min", .25f);
            emitter.SetAttributeValue("particle_lifetime_bias_max", .25f);
        }
        var conversion = TotalWarVfxConverter.Convert(new(doc.Write(), "vfx/life-root.xml", _ => source));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(3, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
    }

    [Test]
    public void NegativeEmissionTail_MatchesTheExplicitlyZeroClampedNativeControl()
    {
        var negative = Convert(Source(.5f, "0,20;1,-20")); var clamped = Convert(Source(.5f, "0,20;.5,0;1,0"));
        Assert.That(negative.Emitters, Has.Count.EqualTo(1));
        var a = new TotalWarVfxSimulation(negative); var b = new TotalWarVfxSimulation(clamped);
        foreach (var seconds in new[] { .1f, 1f, 2.5f, 3.6f, 4f, 6.2f, 7f })
        {
            a.Seek(seconds, Matrix.Identity); b.Seek(seconds, Matrix.Identity);
            Assert.That(a.ParticleCount, Is.EqualTo(b.ParticleCount), "Native negative tail and zero clamp share emission timing.");
        }
        Assert.That(a.RateLimited, Is.False, "An authored stop tail is not a capacity overflow.");
    }

    private static byte[] Source(float lifetime, string? emission = null)
    {
        var doc = VfxDocument.Read(VfxMaterialCompositingTests.Source(Vector3.One, false, 0));
        var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Elements().Single(x => (string?)x.Attribute("name") == "ParticleLifeTime").ReplaceWith(new XElement("property", new XAttribute("name", "ParticleLifeTime"), new XAttribute("value", lifetime)));
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", emission == null ? 1 : 100)),
            new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", 6), new XAttribute("max", 6)),
            new XElement("property", new XAttribute("name", "EmissionLoopAnim"), new XAttribute("value", emission == null ? "false" : "true")));
        if (emission != null)
        {
            var points = emission.Split(';').Select(p => p + ",linear,linear,0,0,0,0,1");
            emitter.Elements().Single(x => (string?)x.Attribute("name") == "EmissionRateCurve").Value = "x,0,0;" + string.Join(';', points) + ":x,0;0,0";
        }
        return doc.Write();
    }
    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "vfx/timing.xml", _ => [1]));
}
