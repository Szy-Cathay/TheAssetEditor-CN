#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelReferenceCacheTests
{
    [Test]
    public void EqualAnimatedModelsAcrossReferences_ShareGeometryButRetainTheirParticlesAndAttachments()
    {
        var child = VfxDocument.Read(VfxModelAnimationTests.Source());
        var parent = Composition(("first", child), ("second", child));
        parent.Layers.Last(layer => layer.Kind == "Reference").Element.SetAttributeValue("pos_x", 5);
        var conversion = Convert(parent, new() { ["first"] = child, ["second"] = child });
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Models[1], Is.SameAs(conversion.Models[0]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.Identity);
        var frame = simulation.Frame(new CommonShaderParameters(Matrix.Identity, Matrix.Identity,
            new(0, 0, 12), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
        Assert.That(frame, Has.Length.EqualTo(2));
        Assert.That(frame[1].Transform.Translation.X - frame[0].Transform.Translation.X, Is.EqualTo(5).Within(.00001f));
    }

    [Test]
    public void RepeatedLargeAnimationAcrossReferences_IsChargedOnlyOnce()
    {
        var child = VfxDocument.Read(VfxModelAnimationTests.Source());
        var parent = Composition(("first", child), ("second", child));
        var conversion = Convert(parent, new() { ["first"] = child, ["second"] = child }, LargeModel());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Models[1], Is.SameAs(conversion.Models[0]));
        Assert.That(conversion.Models[0].AnimationVertices, Has.Length.EqualTo(1032000));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/Animation" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.False);
    }

    [TestCase("life")]
    [TestCase("clip")]
    [TestCase("speed")]
    [TestCase("path")]
    public void DifferentAnimationKeys_AreNotMerged(string difference)
    {
        var first = VfxDocument.Read(VfxModelAnimationTests.Source());
        var second = VfxDocument.Read(VfxModelAnimationTests.Source());
        if (difference == "life") second.Root.Descendants().Single(x => (string?)x.Attribute("name") == "ParticleLifeTime").Value = VfxModelAnimationTests.Scalar(2);
        if (difference == "clip") second.Root.Descendants("AnimFile").Single().SetAttributeValue("value", "animations/other.anim");
        if (difference == "speed") second.Root.Descendants("AnimSpeed").Single().SetAttributeValue("value", 2);
        if (difference == "path") second.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/other.rigid_model_v2");
        var clip = AnimationFile.Create(new Shared.Core.PackFiles.Models.PackFile("other", new Shared.Core.PackFiles.Models.MemorySource(VfxModelAnimationTests.Animation(false))));
        clip.AnimationParts[0].DynamicFrames[1].Transforms[0] = new RmvVector3(6, 0, 0);
        var otherClip = AnimationFile.ConvertToBytes(clip);
        var conversion = Convert(Composition(("first", first), ("second", second)), new() { ["first"] = first, ["second"] = second },
            extra: path => path.Replace('\\', '/') switch
            {
                "animations/other.anim" => otherClip,
                "vfx/other.rigid_model_v2" => VfxModelAnimationTests.Model(),
                _ => null,
            });
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Models[1], Is.Not.SameAs(conversion.Models[0]));
        if (difference is "life" or "speed") Assert.That(conversion.Models[1].AnimationFrameCount, Is.EqualTo(120));
        if (difference == "speed") Assert.That(conversion.Models[1].AnimationCycles, Is.EqualTo(2));
        if (difference == "clip") Assert.That(conversion.Models[1].AnimationVertices![^1].Position.X,
            Is.Not.EqualTo(conversion.Models[0].AnimationVertices![^1].Position.X));
    }

    [Test]
    public void StaticWrappersSharingAnRmv_RetainTheirOwnMaterialAndTexture()
    {
        var first = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var second = VfxDocument.Read(VfxModelVariantTests.Source(1));
        first.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/first.wsmodel");
        second.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/second.wsmodel");
        var conversion = Convert(Composition(("first", first), ("second", second)), new() { ["first"] = first, ["second"] = second },
            extra: path => path.Replace('\\', '/') switch
            {
                "vfx/first.wsmodel" => Wrapper("first"), "vfx/second.wsmodel" => Wrapper("second"),
                "vfx/first.material" => Material("fixture_alpha_off.xml", "vfx/red.dds"),
                "vfx/second.material" => Material("fixture_alpha_on.xml", "vfx/blue.dds"),
                "vfx/shared.rigid_model_v2" => VfxModelVariantTests.Model(0),
                _ => null,
            });
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Models[0].OpaqueMaterial, Is.True);
        Assert.That(conversion.Models[1].OpaqueMaterial, Is.False);
        Assert.That(conversion.Models[1], Is.Not.SameAs(conversion.Models[0]));
        Assert.That(conversion.TexturePaths.Select(p => p.Replace('\\', '/')), Is.EqualTo(new[] { "vfx/red.dds", "vfx/blue.dds" }));
    }

    [Test]
    public void FailedNestedReference_RemovesItsNewCacheEntriesAndRestoresTheirBudget()
    {
        var first = VfxDocument.Read(VfxModelAnimationTests.Source());
        var other = VfxDocument.Read(VfxModelAnimationTests.Source());
        other.Root.Descendants("AnimFile").Single().SetAttributeValue("value", "animations/other.anim");
        var branch = Composition(("first", first));
        var parent = Composition(("branch", branch), ("first", first), ("other", other));
        parent.Layers.First(layer => layer.Kind == "Reference").Element.SetAttributeValue("simulation_speed_multiplier", 0);
        var conversion = Convert(parent, new() { ["branch"] = branch, ["first"] = first, ["other"] = other }, LargeModel(),
            path => path.Replace('\\', '/') == "animations/other.anim" ? VfxModelAnimationTests.Animation(false) : null);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), "A failed branch must not leave reusable geometry with no charged budget.");
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "simulation_speed_multiplier"), Is.True);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/Animation" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
    }

    [Test]
    public void FailedReference_DoesNotDiscardGeometryCachedByAnEarlierSuccessfulReference()
    {
        var child = VfxDocument.Read(VfxModelAnimationTests.Source());
        var parent = Composition(("first", child), ("bad", child), ("last", child));
        parent.Layers.Where(layer => layer.Kind == "Reference").ElementAt(1).Element.SetAttributeValue("simulation_speed_multiplier", 0);
        var conversion = Convert(parent, new() { ["first"] = child, ["bad"] = child, ["last"] = child }, LargeModel());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Models[1], Is.SameAs(conversion.Models[0]));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "simulation_speed_multiplier"), Is.True);
    }

    [Test]
    public void FailedLayer_RemovesTheNewlyCachedPartBeforeItsNextSuccessfulUse()
    {
        var document = VfxDocument.Read(VfxModelAnimationTests.Source());
        var first = document.Layers.Single(layer => layer.Kind == "Emitter");
        document.DuplicateLayer(first).Apply();
        document.DuplicateLayer(first).Apply();
        var layers = document.Layers.Where(layer => layer.Kind == "Emitter").ToArray();
        var missing = new XElement(layers[0].Element.Descendants("rigid_entry").Single());
        missing.Element("ModelFile")!.SetAttributeValue("value", "vfx/missing.rigid_model_v2");
        layers[0].Element.Descendants("rigid_entry").Single().Parent!.Add(missing);
        layers[2].Element.Descendants("AnimFile").Single().SetAttributeValue("value", "animations/other.anim");
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/layers.xml", path => path.Replace('\\', '/') switch
        {
            "vfx/model-0.rigid_model_v2" => LargeModel(), "vfx/missing.rigid_model_v2" => null,
            "animations/other.anim" => VfxModelAnimationTests.Animation(false), _ => VfxModelAnimationTests.Resolve(path),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/Animation" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
    }

    private static VfxDocument Composition(params (string Name, VfxDocument Source)[] references)
    {
        var parent = VfxDocument.CreateComposition();
        foreach (var reference in references) parent.AddReference(reference.Name, reference.Source).Apply();
        return parent;
    }

    private static VfxConversion Convert(VfxDocument parent, Dictionary<string, VfxDocument> documents, byte[]? model = null, Func<string, byte[]?>? extra = null)
        => TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", path =>
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("vfx/") && normalized.EndsWith(".xml")
                && documents.TryGetValue(normalized[4..^4], out var document)) return document.Write();
            return extra?.Invoke(path) ?? (normalized == "vfx/model-0.rigid_model_v2" && model != null ? model : VfxModelAnimationTests.Resolve(path));
        }));

    private static byte[] LargeModel()
    {
        var file = ModelFactory.Create().Load(VfxModelAnimationTests.Model());
        var part = file.ModelList[0][0]; var original = part.Mesh.VertexList;
        part.Mesh.VertexList = Enumerable.Range(0, 4300).Select(index => original[index % original.Length]).ToArray();
        file.RecalculateOffsets();
        return ModelFactory.Create().Save(file);
    }

    private static byte[] Wrapper(string name) => Encoding.UTF8.GetBytes($"<model version=\"1\"><geometry>vfx/shared.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/{name}.material</material></materials></model>");
    private static byte[] Material(string name, string texture) => Encoding.UTF8.GetBytes(new XElement("material", new XElement("name", name),
        new XElement("shader", "shaders/rigid_character.xml.shader"), new XElement("textures", new XElement("texture",
            new XElement("slot", "t_xml_base_colour"), new XElement("source", texture)))).ToString());
}
