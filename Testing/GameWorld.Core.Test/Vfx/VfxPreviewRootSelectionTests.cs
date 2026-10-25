#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxPreviewRootSelectionTests
{
    [Test]
    public void NullSelection_PreservesAllOriginalEnabledRoots()
    {
        var document = Library(); var bytes = document.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/library.xml", _ => [1]));
        Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "visible", "distant" }));
        Assert.That(document.Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void ExplicitEmptySelection_LoadsNoRootDependencies()
    {
        var document = Library(); var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.AddReference("child", child).Apply(); var bytes = document.Write();
        var resolved = new List<string>();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/library.xml", path => { resolved.Add(path); return [1]; },
            RootSelection: new([], [])));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Textures, Is.Empty);
            Assert.That(conversion.TotalEmitterCount, Is.Zero);
            Assert.That(conversion.DisabledEmitterCount, Is.Zero);
            Assert.That(resolved, Is.Empty);
            Assert.That(document.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void EmitterSelection_SkipsUnselectedResourcesAndFraming()
    {
        var document = Library();
        document.Root.Descendants("behaviour_chain").Single(chain => (string?)chain.Attribute("id") == "distant")
            .Descendants("texture_property").Single().SetAttributeValue("path", "vfx/unselected-missing.dds");
        var bytes = document.Write(); var resolved = new List<string>();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/library.xml", path => { resolved.Add(path); return [1]; },
            RootSelection: new(["visible"], [])));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "visible" }));
            Assert.That(conversion.TotalEmitterCount, Is.EqualTo(1));
            Assert.That(conversion.DisabledEmitterCount, Is.Zero, "Preview scope is not a source Enable flag.");
            Assert.That(resolved, Does.Not.Contain("vfx\\unselected-missing.dds"));
            Assert.That(VfxPreviewBounds.Estimate(conversion)!.Value.Max.X, Is.LessThan(5));
            Assert.That(document.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void MultipleEmitterSelection_CombinesOnlyItsExplicitNames()
    {
        var document = Library(); var bytes = document.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/library.xml", _ => [1],
            RootSelection: new(["visible", "distant"], [])));
        Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "visible", "distant" }));
    }

    [Test]
    public void ReferenceInstanceSelection_PreservesItsNativeWhitelistAndPlacement()
    {
        var source = Library(); var sourceBytes = source.Write(); var document = VfxDocument.CreateComposition();
        var first = document.AddReference("source", source); first.Apply(); first.Selection.SetAttributeValue("pos_x", 10);
        var second = document.AddReference("source", source); second.Apply(); second.Selection.SetAttributeValue("pos_x", 20);
        second.Selection.Elements("emitter").Single(emitter => (string?)emitter.Attribute("name") == "distant").Remove();
        var selected = (string)second.Selection.Attribute("inst_name")!; var bytes = document.Write(); var resolves = 0;
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/composition.xml", path =>
        {
            if (path == "vfx\\source.xml") { resolves++; return sourceBytes; }
            return [1];
        }, RootSelection: new([], [selected])));
        Assert.Multiple(() =>
        {
            Assert.That(resolves, Is.EqualTo(1));
            Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "visible" }));
            Assert.That(conversion.Emitters.Single().References.Single().Position.X, Is.EqualTo(20));
            Assert.That(document.Write(), Is.EqualTo(bytes));
            Assert.That(source.Write(), Is.EqualTo(sourceBytes));
        });
    }

    [Test]
    public void EmitterAndReferenceNames_AreSeparateRootNamespaces()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); var child = Library();
        var reference = document.AddReference("child", child); reference.Apply(); reference.Selection.SetAttributeValue("inst_name", "visible");
        var bytes = document.Write();
        byte[]? Resolve(string path) => path == "vfx\\child.xml" ? child.Write() : [1];
        var own = TotalWarVfxConverter.Convert(new(bytes, "vfx/mixed.xml", Resolve, RootSelection: new(["visible"], [])));
        var referenced = TotalWarVfxConverter.Convert(new(bytes, "vfx/mixed.xml", Resolve, RootSelection: new([], ["visible"])));
        Assert.Multiple(() =>
        {
            Assert.That(own.Emitters, Has.Count.EqualTo(1));
            Assert.That(own.Emitters.Single().References, Is.Empty);
            Assert.That(referenced.Emitters, Has.Count.EqualTo(2));
            Assert.That(referenced.Emitters.All(emitter => emitter.References.Count == 1), Is.True);
            Assert.That(document.Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void RootSelection_DoesNotFilterNestedReferenceRoots()
    {
        var source = Library(); var sourceBytes = source.Write(); var nested = VfxDocument.CreateComposition();
        nested.AddReference("source", source).Apply(); var nestedBytes = nested.Write();
        var document = VfxDocument.CreateComposition(); var root = document.AddReference("nested", nested); root.Apply();
        var selected = (string)root.Selection.Attribute("inst_name")!;
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/root.xml", path => path switch
        {
            "vfx\\nested.xml" => nestedBytes,
            "vfx\\source.xml" => sourceBytes,
            _ => [1],
        }, RootSelection: new([], [selected])));
        Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "visible", "distant" }));
        Assert.That(conversion.Emitters.All(emitter => emitter.References.Count == 2), Is.True);
    }

    [Test]
    public void SelectedRoot_KeepsItsDifferentlyNamedEventDependency()
    {
        var parent = VfxParticleEventTests.Source(true, "true");
        var child = VfxDocument.Read(VfxParticleEventTests.Source(false));
        child.Root.Descendants("emitter").Single().SetAttributeValue("id", "event_child"); var childBytes = child.Write();
        var conversion = TotalWarVfxConverter.Convert(new(parent, "vfx/parent.xml", _ => childBytes,
            RootSelection: new(["event_test"], [])));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single(emitter => emitter.Name == "event_child").IsEventTemplate, Is.True);
            Assert.That(simulation.Frame(camera), Has.Length.EqualTo(1));
            Assert.That(child.Write(), Is.EqualTo(childBytes));
        });
    }

    private static VfxDocument Library()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); var effect = document.Root.Element("vfx")!;
        var second = new XElement(effect.Element("emitters")!.Element("emitter")!); second.SetAttributeValue("id", "distant");
        second.Add(new XElement("property", new XAttribute("name", "EmitterPosition"), VfxParticleEventTests.Curve(["x", "y", "z"], 1000, 0, 0)));
        effect.Element("emitters")!.Add(second);
        var chain = new XElement(effect.Element("behaviour_chains")!.Element("behaviour_chain")!); chain.SetAttributeValue("id", "distant");
        effect.Element("behaviour_chains")!.Add(chain);
        return document;
    }
}
