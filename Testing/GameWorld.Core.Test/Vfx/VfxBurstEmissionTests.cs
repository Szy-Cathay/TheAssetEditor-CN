using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxBurstEmissionTests
{
    // R61 Terry short-life controls: emit for Length, then wait Interval; zero Interval is continuous.
    [TestCase(1f, .25f, .7f, false)]
    [TestCase(1f, .25f, 1.4f, true)]
    [TestCase(1f, .25f, 1.8f, false)]
    [TestCase(1f, .5f, 1.2f, false)]
    [TestCase(1f, .5f, 1.7f, true)]
    [TestCase(2f, .25f, 2.1f, false)]
    [TestCase(2f, .25f, 2.4f, true)]
    [TestCase(0f, .25f, .7f, true)]
    [TestCase(0f, .25f, 1.4f, true)]
    public void ShortLifeParticles_FollowNativeActiveAndQuietWindows(float interval, float length, float time, bool active)
    {
        var (conversion, _) = Convert(interval, length);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(time, Matrix.Identity);
        Assert.That(simulation.ParticleCount > 0, Is.EqualTo(active));
    }

    [Test]
    public void ShortFiniteEmitter_DoesNotEmitDuringItsQuietTail()
    {
        var (conversion, _) = Convert(2, .1f, duration: 2);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.05f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
        simulation.Seek(.5f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(2.2f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void ReferenceSpeedAndDelayedStart_TransformTheEntireBurstSchedule()
    {
        var (_, bytes) = Convert(1, .25f);
        var child = VfxDocument.Read(bytes);
        var parent = VfxDocument.CreateComposition();
        var edit = parent.AddReference("burst", child); edit.Apply();
        edit.Selection.SetAttributeValue("start_time_min", 2);
        edit.Selection.SetAttributeValue("start_time_max", 2);
        edit.Selection.SetAttributeValue("simulation_speed_multiplier", 2);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", path => path.EndsWith(".xml") ? bytes : [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(2.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(2.7f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
    }

    [Test]
    public void SeekAndIncrementalPlayback_PreserveBurstScheduleAndSource()
    {
        var (conversion, bytes) = Convert(1, .25f);
        var direct = new TotalWarVfxSimulation(conversion);
        var incremental = new TotalWarVfxSimulation(conversion);
        direct.Seek(1.4f, Matrix.Identity);
        foreach (var time in new[] { .2f, .9f, 1.1f, 1.4f }) incremental.Seek(time, Matrix.Identity);
        Assert.That(incremental.ParticleCount, Is.EqualTo(direct.ParticleCount));
        direct.Seek(.7f, Matrix.Identity);
        Assert.That(direct.ParticleCount, Is.Zero);
        direct.Seek(1.4f, Matrix.Identity);
        Assert.That(direct.ParticleCount, Is.EqualTo(incremental.ParticleCount));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    private static (VfxConversion Conversion, byte[] Bytes) Convert(float interval, float length, float duration = 6)
    {
        var doc = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 120);
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", .1f);
        foreach (var (name, value) in new[] { ("LifeTime", duration), ("BurstInterval", interval), ("BurstIntervalLength", length) })
            emitter.Add(new XElement("ranged_property", new XAttribute("name", name), new XAttribute("min", value), new XAttribute("max", value)));
        var bytes = doc.Write();
        return (TotalWarVfxConverter.Convert(new(bytes, "vfx/burst.xml", _ => [1])), bytes);
    }
}
