#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxParticleCullScaleTests
{
    private static readonly Vector4 Distances = new(2, 4, 8, 10);

    [Test]
    public void OrdinarySprite_MapsItsExplicitParticleCullDistances()
    {
        var conversion = Convert(Source());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Kind, Is.Zero);
        Assert.That(conversion.Emitters[0].ParticleCullDistances, Is.EqualTo(Distances));
    }

    [TestCase(.5f, 1f, 0f)]
    [TestCase(.5f, 3f, .49999952f)]
    [TestCase(.5f, 6f, 1f)]
    [TestCase(.5f, 9f, .49999952f)]
    [TestCase(.5f, 11f, 0f)]
    [TestCase(1f, 1f, 0f)]
    [TestCase(1f, 3f, .49999976f)]
    [TestCase(1f, 6f, 1f)]
    [TestCase(1f, 9f, .49999976f)]
    [TestCase(1f, 11f, 0f)]
    [TestCase(2f, 1f, 0f)]
    [TestCase(2f, 3f, .49999988f)]
    [TestCase(2f, 6f, 1f)]
    [TestCase(2f, 9f, .49999988f)]
    [TestCase(2f, 11f, 0f)]
    public void OrdinarySprite_NativeNearMiddleFarIntervalsUseTheEmitterScale(float scale, float normalizedDistance, float expected)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source()));
        var attachment = Matrix.CreateScale(scale);
        simulation.SeekWithCurrentAttachment(.1f, attachment, _ => attachment);
        var particles = simulation.Frame(Camera(new(0, 0, normalizedDistance * scale)));
        if (expected == 0) Assert.That(particles, Is.Empty);
        else Assert.That(particles.Single().Colour.W, Is.EqualTo(expected).Within(.0000001), "Native VS scales all four distances before its interval masks and1e-6 denominator.");
    }

    [Test]
    public void DIAG_Native27352CameraPositionAndScaleReproduceItsIndependentDistanceFactor()
    {
        var distances = new Vector4(2, 25, 75, 600);
        var conversion = Convert(Source(distances));
        var attachment = Matrix.CreateScale(.8478705883026123f) * Matrix.CreateTranslation(511.97100830078125f, 0, 512);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.SeekWithCurrentAttachment(.1f, attachment, _ => attachment);
        var actual = simulation.Frame(Camera(new(514.0023803710938f, 2.0336833000183105f, 516.10546875f)));
        Assert.That(actual, Has.Length.EqualTo(1));
        // Actual27352 VS42..80 inputs. Constant opacity isolates distance fade; this is not a rewritten original effect.
        Assert.That(actual[0].Colour.W, Is.EqualTo(.17004064f).Within(.0000001));
    }

    [Test]
    public void WorldSprite_DistanceFadeUsesCurrentEmitterScaleRatherThanItsBirthRootScale()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source()));
        simulation.SeekWithCurrentAttachment(.1f, Matrix.CreateScale(2), _ => Matrix.Identity);
        var particle = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>().Single();
        var birthRoot = (Matrix)particle.GetType().GetField("Root")!.GetValue(particle)!;
        Assert.That(birthRoot.Right.Length(), Is.EqualTo(1), "The fixed history deliberately retains the old world-particle root.");
        var actual = simulation.Frame(Camera(new(0, 0, 6)));
        Assert.That(actual.Single().Colour.W, Is.EqualTo(.49999988f).Within(.0000001), "Native VS reads current emitterInstances+48.y, independently of birth root.");
    }

    [Test]
    public void ReferenceAndAttachmentScale_AreCombinedOnceForTheDistanceIntervals()
    {
        var child = VfxDocument.Read(Source()); var root = VfxDocument.CreateComposition();
        var edit = root.AddReference("child", child); edit.Apply();
        edit.Selection.SetAttributeValue("scale_min", .5f); edit.Selection.SetAttributeValue("scale_max", .5f);
        var conversion = TotalWarVfxConverter.Convert(new(root.Write(), @"vfx\cull-scope-diag.xml", path => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : [1]));
        var simulation = new TotalWarVfxSimulation(conversion); var attachment = Matrix.CreateScale(2);
        simulation.SeekWithCurrentAttachment(.1f, attachment, _ => attachment);
        Assert.That(simulation.Frame(Camera(new(0, 0, 3))).Single().Colour.W, Is.EqualTo(.49999976f).Within(.0000001));
    }

    [Test]
    public void MissingCullProperty_RetainsTheExistingUnfadedSprite()
    {
        var source = VfxLibraryVisibilityTests.Source(""); var conversion = Convert(source);
        Assert.That(conversion.Emitters.Single().ParticleCullDistances, Is.Null);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera(new(0, 0, .1f))).Single().Colour.W, Is.EqualTo(1));
    }

    [Test]
    public void ModelCullDistances_RetainTheirExistingSeparateMapping()
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("emitter").Single().Add(Cull(Distances));
        var converted = VfxModelVariantTests.Convert(document.Write());
        Assert.That(converted.Emitters.Single().Kind, Is.EqualTo(1));
        Assert.That(converted.Emitters.Single().ParticleCullDistances, Is.EqualTo(Distances));
    }

    private static CommonShaderParameters Camera(Vector3 eye) => new(Matrix.Identity, Matrix.Identity, eye,
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
    private static XElement Cull(Vector4 distances) => new("property", new XAttribute("name", "ParticleCullDistances"),
        new XAttribute("x", distances.X), new XAttribute("y", distances.Y), new XAttribute("z", distances.Z), new XAttribute("w", distances.W));
    private static byte[] Source(Vector4? distances = null)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("emitter").Single().Add(Cull(distances ?? Distances));
        return document.Write();
    }
    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, @"vfx\cull-scale-diag.xml", _ => [1]));
}
