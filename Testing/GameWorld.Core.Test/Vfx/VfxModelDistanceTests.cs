using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelDistanceTests
{
    [Test]
    public void CameraDistance_HidesTheNearModelAndRestoresTheSameParticleWithoutRestarting()
    {
        var source = Source(30, 100, 150, 300);
        var simulation = new TotalWarVfxSimulation(VfxModelVariantTests.Convert(source));
        simulation.Seek(.2f, Matrix.Identity);
        var visible = simulation.Frame(Camera(125)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(simulation.Frame(Camera(18)), Is.Empty,
                "The original nagash_circle_top range must hide this model at the close comparison camera.");
            Assert.That(simulation.Frame(Camera(310)), Is.Empty);
            Assert.That(simulation.Frame(Camera(125)).Single(), Is.EqualTo(visible));
            Assert.That(simulation.ParticleCount, Is.EqualTo(1), "Visibility must not destroy or respawn the particle.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(30, 0)]
    [TestCase(65, .5f)]
    [TestCase(100, 1)]
    [TestCase(150, 1)]
    [TestCase(225, .5f)]
    [TestCase(300, 0)]
    public void ModelDistanceFade_PreservesTheColourAndUsesTheAuthoredNearAndFarIntervals(float distance, float alpha)
    {
        var simulation = new TotalWarVfxSimulation(VfxModelVariantTests.Convert(Source(30, 100, 150, 300)));
        simulation.Seek(.2f, Matrix.Identity);
        var frame = simulation.Frame(Camera(distance));
        if (alpha == 0) Assert.That(frame, Is.Empty);
        else
        {
            var full = simulation.Frame(Camera(125)).Single();
            Assert.That(frame.Single().Colour, Is.EqualTo(new Vector4(full.Colour.X, full.Colour.Y, full.Colour.Z, full.Colour.W * alpha)));
        }
    }

    [Test]
    public void Distance_IsMeasuredFromTheTransformedParticleAndDoesNotScaleWithTheModel()
    {
        var simulation = new TotalWarVfxSimulation(VfxModelVariantTests.Convert(Source(30, 100, 150, 300)));
        simulation.Seek(.2f, Matrix.CreateScale(8) * Matrix.CreateTranslation(0, 0, 100));
        Assert.That(simulation.Frame(Camera(118)), Is.Empty);
        Assert.That(simulation.Frame(Camera(225)), Has.Length.EqualTo(1));
    }

    [Test]
    public void EqualFadeEndpoints_AllowTheAuthoredVisibleRangeWithoutDivisionByZero()
    {
        var simulation = new TotalWarVfxSimulation(VfxModelVariantTests.Convert(Source(0, 0, 1000, 1000)));
        simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera(18)).Single().Colour.W, Is.EqualTo(1));
        Assert.That(simulation.Frame(Camera(1001)), Is.Empty);
    }

    private static byte[] Source(float near, float nearEnd, float farStart, float far)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("emitter").Single().Add(new XElement("property",
            new XAttribute("name", "ParticleCullDistances"), new XAttribute("quality", 0),
            new XAttribute("x", near), new XAttribute("y", nearEnd),
            new XAttribute("z", farStart), new XAttribute("w", far)));
        return document.Write();
    }

    private static CommonShaderParameters Camera(float distance) =>
        new(Matrix.Identity, Matrix.Identity, new(0, 0, distance), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
}
