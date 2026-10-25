#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxStaticModelPivotTests
{
    private static readonly Vector3 HeaderPivot = new(.75f, 1.5f, -.25f);

    [TestCase(.5f, .5f, .5f)]
    [TestCase(1.5f, .5f, .5f)]
    [TestCase(.5f, 1.5f, .5f)]
    [TestCase(.5f, .5f, 1.5f)]
    [TestCase(.5f, 2f, .5f)]
    public void StaticGeometry_AppliesHeaderAndEntryPivotsInLocalUnits(float x, float y, float z)
    {
        var source = Source(new(x, y, z));
        var model = Model(HeaderPivot);
        var sourceNormal = ModelFactory.Create().Load(model).ModelList[0][0].Mesh.VertexList[0].Normal;
        var originalSource = source.ToArray(); var originalModel = model.ToArray();
        var conversion = Convert(source, model);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), Diagnostics(conversion));
        var offset = HeaderPivot + new Vector3(.5f) - new Vector3(x, y, z);
        Assert.Multiple(() =>
        {
            Near(conversion.Models.Single().Vertices[0].Position, offset);
            Near(conversion.Models.Single().Vertices[1].Position, Vector3.Right + offset);
            Assert.That(conversion.Models.Single().Vertices[0].Normal, Is.EqualTo(sourceNormal));
            Assert.That(source, Is.EqualTo(originalSource));
            Assert.That(model, Is.EqualTo(originalModel));
        });
    }

    [Test]
    public void MissingEntryPivot_UsesTheNeutralHalfRatherThanTheModelOrigin()
    {
        var document = VfxDocument.Read(Source(new(.5f)));
        document.Root.Descendants("PivotPoint").Single().Remove();
        var conversion = Convert(document.Write(), Model(HeaderPivot));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), Diagnostics(conversion));
        Near(conversion.Models.Single().Vertices[0].Position, HeaderPivot);
    }

    [Test]
    public void SameModelWithDifferentEntryPivots_DoesNotReuseOrMutateAnotherChoicesGeometry()
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(3));
        var entries = document.Root.Descendants("rigid_entry").ToArray();
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index].Element("ModelFile")!.SetAttributeValue("value", "vfx/model-0.rigid_model_v2");
            entries[index].Element("PivotPoint")!.SetAttributeValue("x", index == 1 ? 1.5f : .5f);
        }
        var conversion = Convert(document.Write(), Model(HeaderPivot));
        Assert.That(conversion.Models, Has.Count.EqualTo(3));
        Near(conversion.Models[0].Vertices[0].Position, HeaderPivot);
        Near(conversion.Models[1].Vertices[0].Position, HeaderPivot - Vector3.Right);
        Near(conversion.Models[2].Vertices[0].Position, HeaderPivot);
    }

    [Test]
    public void MultipartModel_KeepsEachHeadersOriginAndTheSharedEntryPivot()
    {
        var model = Model(HeaderPivot, new(-2, .25f, .5f));
        var conversion = Convert(Source(new(1.5f, .5f, .5f)), model);
        Assert.That(conversion.Emitters.Single().RenderLayerCount, Is.EqualTo(2));
        Near(conversion.Models[0].Vertices[0].Position, HeaderPivot - Vector3.Right);
        Near(conversion.Models[1].Vertices[0].Position, new(-3, .25f, .5f));
    }

    [Test]
    public void PivotOffset_IsScaledAndRotatedWithGeometryWithoutMovingTheEmitter()
    {
        var document = VfxDocument.Read(Source(new(.5f, 2, .5f)));
        var emitter = document.Root.Descendants("emitter").Single();
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Add(new XElement("property", new XAttribute("name", name), new XAttribute("x", 90), new XAttribute("y", 0), new XAttribute("z", 0)));
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "ParticleScale").Value =
            string.Join('|', new[] { ("x", 2f), ("y", 3f), ("z", 4f) }.Select(axis => VfxLibraryVisibilityTests.Constant(axis.Item1, axis.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var conversion = Convert(document.Write(), Model(HeaderPivot));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.CreateTranslation(1, 4, 2));
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 10, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var particle = simulation.ProjectedFrame(camera).Single();
        Near(particle.Render.Transform.Translation, new(1, 4, 2));
        var vertex = Vector3.Transform(conversion.Models.Single().Vertices[0].Position, particle.Render.Transform);
        Near(vertex, new(2.5f, 5, 2));
    }

    [Test]
    public void AnimatedNonDefaultEntryPivot_RemainsDiagnosedUntilNativeAnimationIsCalibrated()
    {
        var document = VfxDocument.Read(VfxModelAnimationTests.Source());
        document.Root.Descendants("PivotPoint").Single().SetAttributeValue("x", 1.5);
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/pivot.xml", VfxModelAnimationTests.Resolve));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/PivotPoint" && d.ReasonKey == "Vfx.Preview.GeometryUnsupported"), Is.True);
    }

    private static byte[] Source(Vector3 pivot)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var point = document.Root.Descendants("PivotPoint").Single();
        point.SetAttributeValue("x", pivot.X); point.SetAttributeValue("y", pivot.Y); point.SetAttributeValue("z", pivot.Z);
        return document.Write();
    }

    private static byte[] Model(params Vector3[] pivots)
    {
        var file = ModelFactory.Create().Load(VfxModelVariantTests.Model(0));
        file.ModelList[0] = pivots.Select(pivot =>
        {
            var part = ModelFactory.Create().Load(VfxModelVariantTests.Model(0)).ModelList[0][0];
            part.Material.PivotPoint = pivot; return part;
        }).ToArray();
        file.RecalculateOffsets(); return ModelFactory.Create().Save(file);
    }

    private static VfxConversion Convert(byte[] source, byte[] model) => TotalWarVfxConverter.Convert(new(source, "vfx/pivot.xml",
        path => path.EndsWith(".rigid_model_v2", StringComparison.OrdinalIgnoreCase) ? model : VfxModelVariantPixelTests.Dds(Microsoft.Xna.Framework.Color.White)));

    private static string Diagnostics(VfxConversion conversion) => string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey));

    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00001f), $"{actual} != {expected}");
}
