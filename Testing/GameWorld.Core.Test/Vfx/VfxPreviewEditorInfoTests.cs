#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxPreviewEditorInfoTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    [Test]
    public void ExactFieldIdentity_SeparatesQualityContextDuplicatesAndAttributesWithoutDependingOnValues()
    {
        var document = QualitySource();
        var layer = document.Layers.First();
        var keys = layer.Fields.Select(VfxPreviewFieldIdentity.GetKey).ToArray();
        Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Length));
        foreach (var field in layer.Fields)
        {
            if (field.IsText) field.Element.Value = "different";
            else foreach (var attribute in field.Attributes) attribute.Value = "different";
        }
        Assert.That(document.Layers.First().Fields.Select(VfxPreviewFieldIdentity.GetKey), Is.EqualTo(keys));
    }

    [TestCase("0")]
    [TestCase("1")]
    public void CurrentQualityAndConsumedScopes_AreReportedFromTheActualConversion(string quality)
    {
        var document = QualitySource();
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/editor-info-quality.xml", _ => [1], quality));
        var fields = conversion.EditorInfo.Layers.Single(layer => layer.Kind == "Emitter").Fields;
        var sourceFields = document.Layers.First().Fields;
        foreach (var field in sourceFields)
        {
            var info = fields.Single(info => info.Key == VfxPreviewFieldIdentity.GetKey(field));
            Assert.That(info.Name, Is.EqualTo(field.Name));
            Assert.That(info.Context, Is.EqualTo(field.Context));
            Assert.That(info.Quality, Is.EqualTo(field.Quality));
        }
        Assert.That(fields.Single(field => field.Name == "EmissionRateCurve" && field.Quality == quality).State,
            Is.EqualTo(VfxPreviewFieldState.Connected));
        Assert.That(fields.Single(field => field.Name == "EmissionRateCurve" && field.Quality != quality).State,
            Is.EqualTo(VfxPreviewFieldState.InactiveQuality));
        if (quality == "1")
        {
            Assert.That(fields.Single(field => field.Name == "diffuse" && field.Context == "ps_future").State,
                Is.EqualTo(VfxPreviewFieldState.NotConnected));
            Assert.That(fields.Single(field => field.Name == "diffuse" && field.Context == "ps_texture" && field.Quality == "0").State,
                Is.EqualTo(VfxPreviewFieldState.InactiveQuality));
        }
    }

    [TestCase("disabled", VfxPreviewFieldState.Disabled)]
    [TestCase("unselected", VfxPreviewFieldState.Unselected)]
    [TestCase("failed", VfxPreviewFieldState.Failed)]
    public void SourceDisabledRangeExcludedAndFailedLayers_AreNeverReportedAsConnected(string kind, VfxPreviewFieldState expected)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        if (kind == "disabled") emitter.Add(new XElement("property", new XAttribute("name", "Enable"), new XAttribute("value", "false")));
        if (kind == "failed") emitter.SetAttributeValue("type", "unmapped_geometry");
        var selection = kind == "unselected" ? new VfxPreviewRootSelection([], []) : null;
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/editor-info-state.xml", _ => [1], RootSelection: selection));
        var layer = conversion.EditorInfo.Layers.Single(layer => layer.Kind == "Emitter");
        Assert.That(layer.State, Is.EqualTo(expected));
        Assert.That(layer.Fields.Select(field => field.State), Has.All.EqualTo(expected));
    }

    [Test]
    public void RootIdentity_DistinguishesDuplicateInstancesAndPropagatesThroughEvents()
    {
        var (snapshot, _) = Composition();
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters.Where(emitter => !emitter.IsEventTemplate).Select(emitter =>
            new VfxPreviewLayerIdentity(emitter.PreviewRootKind, emitter.PreviewRootName, emitter.PreviewRootOccurrence)),
            Is.EquivalentTo(new[] { new VfxPreviewLayerIdentity("Emitter", "visible", 0), new("Emitter", "visible", 1),
                new("Reference", "visible", 0), new("Reference", "visible", 1) }));
        var templates = conversion.Emitters.Where(emitter => emitter.IsEventTemplate).ToArray();
        Assert.That(templates, Has.Length.EqualTo(2));
        Assert.That(templates.Select(emitter => new VfxPreviewLayerIdentity(emitter.PreviewRootKind, emitter.PreviewRootName,
            emitter.PreviewRootOccurrence)), Is.EquivalentTo(new[] { new VfxPreviewLayerIdentity("Reference", "visible", 0), new("Reference", "visible", 1) }));
        var layers = conversion.EditorInfo.Layers.Where(layer => layer.Kind is "Emitter" or "Reference").ToArray();
        Assert.That(layers.Select(layer => layer.Key).Distinct().Count(), Is.EqualTo(4));
        Assert.That(layers.Select(layer => (layer.Kind, layer.Name, layer.Occurrence)).Distinct().Count(), Is.EqualTo(4));
    }

    [Test]
    public void MetadataCollection_DoesNotChangeResourcesDiagnosticsCountsPacketsOrSource()
    {
        var (snapshot, document) = Composition();
        var originalBytes = document.Write();
        var annotated = TotalWarVfxConverter.Convert(snapshot);
        var control = TotalWarVfxConverter.Convert(snapshot, collectEditorInfo: false);
        Assert.That(control.EditorInfo, Is.SameAs(VfxPreviewEditorInfo.Empty));
        Assert.That(annotated.Diagnostics, Is.EqualTo(control.Diagnostics));
        Assert.That(annotated.TotalEmitterCount, Is.EqualTo(control.TotalEmitterCount));
        Assert.That(annotated.DisabledEmitterCount, Is.EqualTo(control.DisabledEmitterCount));
        Assert.That(annotated.Emitters.Count, Is.EqualTo(control.Emitters.Count));
        Assert.That(annotated.TexturePaths, Is.EqualTo(control.TexturePaths));
        Assert.That(annotated.TextureUsages, Is.EqualTo(control.TextureUsages));
        Assert.That(annotated.Textures, Is.EqualTo(control.Textures));
        Assert.That(annotated.Models, Is.EqualTo(control.Models));
        Assert.That(annotated.MeshMaterials, Is.EqualTo(control.MeshMaterials));
        var actual = new TotalWarVfxSimulation(annotated); var expected = new TotalWarVfxSimulation(control);
        foreach (var time in new[] { 0f, .137f, .717f, 1.337f, 2.737f, .317f, 3.117f })
        {
            actual.Seek(time, Matrix.Identity); expected.Seek(time, Matrix.Identity);
            Assert.That(actual.ProjectedFrame(Camera, false), Is.EqualTo(expected.ProjectedFrame(Camera, false)), $"At {time}s");
        }
        Assert.That(document.Write(), Is.EqualTo(originalBytes));
        Assert.That(snapshot.GameBytes, Is.EqualTo(originalBytes));
    }

    [Test]
    public void ReferencesOverridesAndSettings_DistinguishParsedTransformsIgnoredNotesAndUnknownData()
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var document = VfxDocument.CreateComposition(); var edit = document.AddReference("child", child); edit.Apply();
        edit.Selection.SetAttributeValue("future_attribute", "123");
        document.Root.Element("vfx")!.SetAttributeValue("future_setting", "1");
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/editor-info-reference.xml", _ => child.Write()));
        var reference = conversion.EditorInfo.Layers.Single(layer => layer.Kind == "Reference");
        Assert.That(reference.Fields.Single(field => field.Name == "reference_position").State, Is.EqualTo(VfxPreviewFieldState.Connected));
        Assert.That(reference.Fields.Single(field => field.Name == "notes").State, Is.EqualTo(VfxPreviewFieldState.Ignored));
        Assert.That(reference.Fields.Single(field => field.Name == "future_attribute").State, Is.EqualTo(VfxPreviewFieldState.Unknown));
        var settings = conversion.EditorInfo.Layers.Single(layer => layer.Kind == "Settings");
        Assert.That(settings.Fields.Single(field => field.Name == "revision").State, Is.EqualTo(VfxPreviewFieldState.Connected));
        Assert.That(settings.Fields.Single(field => field.Name == "future_setting").State, Is.EqualTo(VfxPreviewFieldState.NotConnected));
        var overrides = conversion.EditorInfo.Layers.Single(layer => layer.Kind == "Override");
        Assert.That(overrides.Fields.Single(field => field.Name == "sound_record").State, Is.EqualTo(VfxPreviewFieldState.Ignored));
    }

    [Test]
    public void MissingEventDependency_KeepsTheParentButMarksThatExactEventFieldFailed()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single(); emitter.SetAttributeValue("type", "continuous_rigid");
        emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")),
            new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "missing")));
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/editor-info-missing-event.xml", _ => null));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var info = conversion.EditorInfo.Layers.Single(layer => layer.Kind == "Emitter");
        Assert.That(info.State, Is.EqualTo(VfxPreviewFieldState.Connected));
        Assert.That(info.Fields.Single(field => field.Name == "OnParticleSpawn").State, Is.EqualTo(VfxPreviewFieldState.Failed));
        Assert.That(info.Fields.Single(field => field.Name == "OnParticleSpawn").ReasonKeys,
            Does.Contain("Vfx.Preview.MissingResource"));
    }

    private static VfxDocument QualitySource()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emission = document.Root.Descendants("property").Single(element => (string?)element.Attribute("name") == "EmissionRateCurve");
        emission.SetAttributeValue("quality", "0");
        var alternate = new XElement(emission); alternate.SetAttributeValue("quality", "1"); alternate.Value = VfxLibraryVisibilityTests.Constant("x", 2);
        emission.AddAfterSelf(alternate);
        document.Root.Descendants("behaviour_chain").Single().Add(new XElement("quality", new XAttribute("value", "1"),
            new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "vfx/alternate.dds"))),
            new XElement("modifier", new XAttribute("fragment", "ps_future"),
                new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "vfx/unknown.dds")))));
        return document;
    }

    // DIAG composition: duplicate root names and an event helper exercise ownership, not official visual acceptance.
    private static (VfxPreviewSnapshot Snapshot, VfxDocument Document) Composition()
    {
        var helper = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = helper.Root.Descendants("emitter").Single(); emitter.SetAttributeValue("type", "continuous_rigid");
        emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")),
            new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "grand"),
                new XAttribute("track_position", "true"), new XAttribute("track_orientation_mode", "orientation")));
        var child = VfxLibraryVisibilityTests.Source("");
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var rootEmitter = document.Root.Descendants("emitter").Single(); rootEmitter.AddAfterSelf(new XElement(rootEmitter));
        var first = document.AddReference("helper", helper); first.Apply();
        var second = document.AddReference("helper", helper); second.Apply();
        first.Selection.SetAttributeValue("inst_name", "visible"); second.Selection.SetAttributeValue("inst_name", "visible");
        var bytes = document.Write();
        return (new(bytes, "diagnostic/editor-info-composition.xml", path => path.EndsWith("helper.xml", StringComparison.OrdinalIgnoreCase)
            ? helper.Write() : path.EndsWith("grand.xml", StringComparison.OrdinalIgnoreCase) ? child : [1]), document);
    }
}
