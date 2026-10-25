using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxVisibleFramingTests
{
    [Test]
    public void UnreachedAnimationPose_DoesNotShrinkTheInitialVisibleGeometry()
    {
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        source.Root.Descendants("emitter").Single().Elements().Single(e => e.Attribute("name")?.Value == "ParticleLifeTime").Value
            = VfxLibraryVisibilityTests.Constant("x", 20);
        source.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        var conversion = VfxModelVariantTests.Convert(source.Write()); var model = conversion.Models[0];
        var future = model.Vertices.Select(v => { v.Position.Y += 300; return v; }).ToArray();
        conversion.Models[0] = model with { AnimationVertices = [.. model.Vertices, .. future], AnimationFrameCount = 2, AnimationCycles = 1 };
        var original = conversion.Models[0].AnimationVertices!.ToArray();
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.That(bounds!.Value.Max.Y, Is.LessThan(2));
        Assert.That(conversion.Models[0].AnimationVertices, Is.EqualTo(original));
    }

    [Test]
    public void GeometryBelowTheFlatGround_DoesNotPullTheCameraUnderground()
    {
        var conversion = VfxModelVariantTests.Convert(VfxModelVariantTests.Source(2)); var model = conversion.Models[1];
        conversion.Models[1] = model with { Vertices = model.Vertices.Select(v => { v.Position.Y -= 300; return v; }).ToArray() };
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.That(bounds!.Value.Min.Y, Is.GreaterThanOrEqualTo(0));
        Assert.That(bounds.Value.Max.Y, Is.GreaterThan(.9f));
    }

    [Test]
    public void DistortionVolume_DoesNotReplaceTheVisibleColourGeometryAsTheFramingTarget()
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source("")); var parent = VfxDocument.CreateComposition();
        parent.AddReference("colour", child).Apply(); var second = parent.AddReference("distortion", child); second.Apply();
        second.Selection.SetAttributeValue("pos_x", 5000);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/framing.xml", path => path.EndsWith(".xml") ? child.Write() : [1]));
        var layer = conversion.Emitters[1].Render; layer.Distortion = 1; conversion.Emitters[1].Render = layer;
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.That(bounds!.Value.Max.X, Is.LessThan(2));
    }

    [Test]
    public void DistortionOnlyEffect_RetainsAUsableFramingFallback()
    {
        var conversion = TotalWarVfxConverter.Convert(new(VfxLibraryVisibilityTests.Source(""), "vfx/framing.xml", _ => [1]));
        var layer = conversion.Emitters[0].Render; layer.Distortion = 1; conversion.Emitters[0].Render = layer;
        Assert.That(VfxPreviewBounds.Estimate(conversion), Is.Not.Null);
    }

    [Test]
    public void EntirelyUndergroundEffect_RetainsItsGeometryForManualInspection()
    {
        var conversion = VfxModelVariantTests.Convert(VfxModelVariantTests.Source(1)); var model = conversion.Models[0];
        conversion.Models[0] = model with { Vertices = model.Vertices.Select(v => { v.Position.Y -= 300; return v; }).ToArray() };
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.That(bounds, Is.Not.Null);
        Assert.That(bounds!.Value.Min.Y, Is.LessThan(-299));
    }

    [Test]
    public void OpaqueCharacter_IgnoresParticleAlphaForFramingAsForDrawing()
    {
        var source = VfxOpaqueModelMaterialTests.Source(Vector3.One, 0);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/framing.xml", path => VfxOpaqueModelMaterialTests.Resolve(path, [1])));
        Assert.That(VfxPreviewBounds.Estimate(conversion), Is.Not.Null);
    }
}
