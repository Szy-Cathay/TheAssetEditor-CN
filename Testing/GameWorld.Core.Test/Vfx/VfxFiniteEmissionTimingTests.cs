using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxFiniteEmissionTimingTests
{
    // R45 Terry: both fixed-direction/depth cohorts produced 1/1/2/6 particles; 245 recorded frames.
    // Movie pack SHA256: 778ff761fedae1e8021a8de90b91777f6cbc6b9d11f7b8763f7edfa599a19b2f.
    [TestCase(.01f, 1)]
    [TestCase(.1f, 1)]
    [TestCase(.3f, 2)]
    [TestCase(1f, 6)]
    public void FiniteEmitter_MatchesTheRepeatedNativeCountsAtBothReferenceStarts(float duration, int expected)
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = child.Root.Descendants("emitter").Single();
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 5);
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", duration), new XAttribute("max", duration)));
        var source = child.Write(); var parent = VfxDocument.CreateComposition();
        foreach (var start in new[] { 2, 8 })
        {
            var edit = parent.AddReference("child", child); edit.Apply();
            edit.Selection.SetAttributeValue("start_time_min", start); edit.Selection.SetAttributeValue("start_time_max", start);
        }
        var bytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/finite.xml", path => path.EndsWith(".xml") ? source : [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(3.2f, Matrix.Identity); var first = simulation.ParticleCount;
        simulation.Seek(9.2f, Matrix.Identity); var second = simulation.ParticleCount;
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(expected), "First native cohort, authored reference start 2s.");
            Assert.That(second, Is.EqualTo(expected), "Repeated native cohort, authored reference start 8s.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase(0f)]
    [TestCase(2f)]
    [TestCase(8f)]
    public void FiniteEmitter_BirthCountDoesNotDependOnTheAbsoluteStart(float start)
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); var emitter = child.Root.Descendants("emitter").Single();
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 5);
        emitter.Elements().Single(e => e.Attribute("name")?.Value == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", 1), new XAttribute("max", 1)),
            new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", start), new XAttribute("max", start)));
        var conversion = TotalWarVfxConverter.Convert(new(child.Write(), "vfx/finite.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(start + 1.2f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(6));
    }
}
