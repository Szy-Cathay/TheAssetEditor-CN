using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxReferenceSelectionTests
{
    [TestCase("true", "true", "0", 1)]
    [TestCase("false", "true", "0", 0)]
    [TestCase("true", "false", "0", 0)]
    [TestCase("false", "false", "0", 0)]
    [TestCase("true", "true", "1", 0)]
    public void ReferenceEnable_RequiresTheSourceLayerToBeEnabledAtTheSelectedQuality(
        string sourceEnable, string referenceEnable, string quality, int visible)
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = child.Root.Element("vfx")!.Element("emitters")!.Element("emitter")!;
        emitter.Add(new XElement("property", new XAttribute("name", "Enable"),
            new XAttribute("quality", "0"), new XAttribute("value", sourceEnable)));
        emitter.Add(new XElement("property", new XAttribute("name", "Enable"),
            new XAttribute("quality", "1"), new XAttribute("value", "false")));
        var childBytes = child.Write();
        var parent = VfxDocument.CreateComposition(); var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.Element("emitter")!.SetAttributeValue("enable", referenceEnable);
        var bytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/parent.xml",
            path => path == @"vfx\child.xml" ? childBytes : [1], quality));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(visible));
            Assert.That(conversion.DisabledEmitterCount, Is.EqualTo(1 - visible));
            Assert.That(conversion.Models, Is.Empty);
            Assert.That(child.Write(), Is.EqualTo(childBytes));
            Assert.That(parent.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void ReferenceSelection_MatchesNativeRedOnlyControlAndExcludesUnlistedGeometryFromFraming()
    {
        var child = Child(); var originalChild = child.Write();
        var parent = VfxDocument.CreateComposition(); var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.Elements("emitter").Single(e => (string)e.Attribute("name")! == "unused").Remove();
        var bytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/parent.xml", path => path == @"vfx\child.xml" ? originalChild : [1]));
        Assert.That(conversion.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "visible" }));
        Assert.That(conversion.TotalEmitterCount, Is.EqualTo(2));
        Assert.That(conversion.DisabledEmitterCount, Is.EqualTo(1));
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.That(bounds, Is.Not.Null);
        Assert.That(bounds!.Value.Max.X, Is.LessThan(5), "The unselected emitter at x=1000 must not shrink the selected effect to a dot.");
        Assert.That(parent.Write(), Is.EqualTo(bytes)); Assert.That(child.Write(), Is.EqualTo(originalChild));
    }

    [Test]
    public void EmptyReferenceSelection_MatchesNativeNoRowsControlWithoutChangingTheChild()
    {
        var child = Child(); var childBytes = child.Write();
        var parent = VfxDocument.CreateComposition(); var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.Elements("emitter").Remove(); var bytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/parent.xml", path => path == @"vfx\child.xml" ? childBytes : [1]));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.TotalEmitterCount, Is.EqualTo(2));
        Assert.That(conversion.DisabledEmitterCount, Is.EqualTo(2));
        Assert.That(child.Write(), Is.EqualTo(childBytes)); Assert.That(parent.Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void DirectDocument_StillUsesItsOwnEnabledLayersWithoutAReferenceSelection()
    {
        var child = Child(); var bytes = child.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/direct.xml", _ => [1]));
        Assert.That(conversion.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "visible", "unused" }));
        Assert.That(child.Write(), Is.EqualTo(bytes));
    }

    private static VfxDocument Child()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); var effect = document.Root.Element("vfx")!;
        var second = new XElement(effect.Element("emitters")!.Element("emitter")!); second.SetAttributeValue("id", "unused");
        var vector = string.Join('|', new[] { ("x", 1000f), ("y", 0f), ("z", 0f) }
            .Select(v => VfxLibraryVisibilityTests.Constant(v.Item1, v.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        second.Add(new XElement("property", new XAttribute("name", "EmitterPosition"), vector));
        effect.Element("emitters")!.Add(second);
        var chain = new XElement(effect.Element("behaviour_chains")!.Element("behaviour_chain")!); chain.SetAttributeValue("id", "unused");
        effect.Element("behaviour_chains")!.Add(chain); return document;
    }
}
