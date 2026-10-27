#nullable enable
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxInfiniteEmitterLifetimeTests
{
    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void RuntimeLifetime_UsesActualRootEmittersInsteadOfTheFiniteDisplayCycle(bool firstInfinite, bool secondInfinite, bool expected)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Single(node => (string?)node.Attribute("name") == "InfiniteLife").SetAttributeValue("value", firstInfinite);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", ".25"), new XAttribute("max", ".25")));
        var second = new XElement(emitter);
        second.SetAttributeValue("id", "second");
        second.Elements().Single(node => (string?)node.Attribute("name") == "InfiniteLife").SetAttributeValue("value", secondInfinite);
        emitter.Parent!.Add(second);
        var behaviour = document.Root.Descendants("behaviour_chain").Single();
        var secondBehaviour = new XElement(behaviour);
        secondBehaviour.SetAttributeValue("id", "second");
        behaviour.Parent!.Add(secondBehaviour);
        var bytes = document.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/infinite-lifetime-diagnostic.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        using var runtime = new EffekseerVfxPreview(null!);
        IVfxPreview preview = runtime;
        Assert.That(preview.HasInfiniteEmitters, Is.False, "An unloaded runtime has no known live emitters.");
        typeof(EffekseerVfxPreview).GetField("_conversion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, conversion);
        Assert.That(preview.HasInfiniteEmitters, Is.EqualTo(expected));
        Assert.That(preview.CycleDuration, Is.EqualTo(8), "The display cycle must not become an infinite timeline.");
        foreach (var definition in conversion.Emitters.Where(definition => definition.Infinite)) definition.IsEventTemplate = true;
        Assert.That(preview.HasInfiniteEmitters, Is.False, "Untriggered event templates are not independent persistent emitters.");
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        runtime.Dispose();
        Assert.That(preview.HasInfiniteEmitters, Is.False);
    }
}
