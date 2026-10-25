#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxMinimumPixelSizeTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, .1f, 1000) * Matrix.CreateScale(-1, 1, 1),
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], ViewportHeight: 1000, ViewportWidth: 1000);

    [TestCase(false)]
    [TestCase(true)]
    public void SmallSprite_UsesTheAuthoredPixelFloorAndPreservesAspectRatio(bool orthographic)
    {
        var source = Source(new(.01f, .02f, 1), 2);
        var bytes = source.Write();
        var conversion = Convert(bytes);
        var camera = orthographic ? Camera with { Projection = Matrix.CreateOrthographic(20, 20, .1f, 1000) } : Camera;
        var particle = Frame(conversion, camera);
        Assert.Multiple(() =>
        {
            Assert.That(particle.Transform.Right.Length(), Is.EqualTo(.04f).Within(.000001f));
            Assert.That(particle.Transform.Up.Length(), Is.EqualTo(.08f).Within(.000001f));
            Assert.That(particle.SoftParticleScale, Is.EqualTo(.04f).Within(.000001f));
            Assert.That(conversion.Diagnostics.Any(d => d.Field is "pixelsize_limit" or "modifier/vs_rendersize_limiter"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void ProjectionOffsetsAndReflection_DoNotChangeTheProjectedParticleWidth()
    {
        var conversion = Convert(Source(new(.01f, .02f, 1), 2).Write());
        var projection = Camera.Projection;
        projection.M31 = .25f; projection.M41 = .5f;
        var expected = Frame(conversion, Camera);
        var offset = Frame(conversion, Camera with { Projection = projection });
        var reflected = Frame(conversion, Camera with { Projection = projection * Matrix.CreateScale(-1, 1, 1) });
        Assert.Multiple(() =>
        {
            Assert.That(offset.Transform.Right.Length(), Is.EqualTo(expected.Transform.Right.Length()).Within(.000001f));
            Assert.That(reflected.Transform.Right.Length(), Is.EqualTo(expected.Transform.Right.Length()).Within(.000001f));
        });
    }

    [Test]
    public void DistanceAndViewportWidth_ProduceTheCorrespondingWorldSize()
    {
        var conversion = Convert(Source(new(.001f, .002f, 1), 2).Write());
        var baseline = Frame(conversion, Camera);
        var farther = Frame(conversion, Camera with { CameraPosition = new(0, 0, 20) });
        var wider = Frame(conversion, Camera with { ViewportWidth = 2000 });
        Assert.Multiple(() =>
        {
            Assert.That(farther.Transform.Right.Length(), Is.EqualTo(baseline.Transform.Right.Length() * 2).Within(.000001f));
            Assert.That(wider.Transform.Right.Length(), Is.EqualTo(baseline.Transform.Right.Length() * .5f).Within(.000001f));
        });
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void MissingOrInvalidViewport_DoesNotCreateAnUnboundedParticle(float width)
    {
        var conversion = Convert(Source(new(.01f, .02f, 1), 2).Write());
        Assert.That(Frame(conversion, Camera with { ViewportWidth = width }).Transform.Right.Length(), Is.EqualTo(.01f).Within(.000001f));
    }

    [TestCase(1f, 2f)]
    [TestCase(-.01f, 2f)]
    [TestCase(.01f, 0f)]
    public void LargerNegativeOrDisabledSize_PreservesTheAuthoredGeometry(float width, float limit)
    {
        var conversion = Convert(Source(new(width, .02f, 1), limit).Write());
        Assert.That(Frame(conversion, Camera).Transform.Right.Length(), Is.EqualTo(Math.Abs(width)).Within(.000001f));
    }

    [Test]
    public void Limiter_ChangesGeometryWithoutChangingColourUvOrAppearanceAge()
    {
        var source = Source(new(.01f, .02f, 1), 2);
        var limited = Frame(Convert(source.Write()), Camera);
        source.Root.Descendants("modifier").Single(m => (string?)m.Attribute("fragment") == "vs_rendersize_limiter").Remove();
        var original = Frame(Convert(source.Write()), Camera);
        Assert.Multiple(() =>
        {
            Assert.That(limited.Colour, Is.EqualTo(original.Colour));
            Assert.That(limited.UV, Is.EqualTo(original.UV));
            Assert.That(limited.Time, Is.EqualTo(original.Time));
            Assert.That(limited.AppearancePhase, Is.EqualTo(original.AppearancePhase));
            Assert.That(limited.Transform.Translation, Is.EqualTo(original.Transform.Translation));
            Assert.That(original.Transform.Right.Length(), Is.EqualTo(.01f).Within(.000001f));
        });
    }

    [Test]
    public void VelocityStretchAndAttachmentScale_AreRetainedWhenBothAxesAreEnlarged()
    {
        var source = Source(new(.01f, .02f, 1), 2);
        source.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"),
            VfxLibraryVisibilityTests.Constant("x", 2)));
        source.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
            new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-facing velocity-aligned")),
            new XElement("property", new XAttribute("name", "velocity_stretch_mul"), new XAttribute("value", .5f))));
        var conversion = Convert(source.Write());
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.CreateScale(2));
        var particle = simulation.Frame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.Transform.Right.Length(), Is.EqualTo(.04f).Within(.000001f));
            Assert.That(particle.Transform.Up.Length(), Is.EqualTo(.16f).Within(.000001f));
        });
    }

    [TestCase(-1f)]
    [TestCase(65505f)]
    public void UnrepresentablePixelLimit_RejectsTheLayer(float limit)
    {
        var conversion = Convert(Source(Vector3.One, limit).Write());
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "pixelsize_limit" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
        });
    }

    [Test]
    public void PixelLimit_UsesTheNativeHalfPrecisionAndDefault()
    {
        var source = Source(Vector3.One, 1.0001f);
        Assert.That(Convert(source.Write()).Emitters.Single().MinimumPixelSize, Is.EqualTo((float)(Half)1.0001f));
        source.Root.Descendants("property").Single(p => (string?)p.Attribute("name") == "pixelsize_limit").Remove();
        Assert.That(Convert(source.Write()).Emitters.Single().MinimumPixelSize, Is.EqualTo(1));
    }

    private static VfxDocument Source(Vector3 scale, float limit)
    {
        var source = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        source.Root.Descendants("emitter").Single().Elements().Single(p => (string?)p.Attribute("name") == "ParticleScale").Value
            = string.Join('|', new[] { VfxLibraryVisibilityTests.Constant("x", scale.X).Split(':')[0],
                VfxLibraryVisibilityTests.Constant("y", scale.Y).Split(':')[0], VfxLibraryVisibilityTests.Constant("z", scale.Z).Split(':')[0] })
                + ":x,0;0,0|y,0;1,1|z,0;2,2";
        source.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_rendersize_limiter"),
            new XElement("property", new XAttribute("name", "pixelsize_limit"), new XAttribute("value", limit))));
        return source;
    }

    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/minimum-pixel.xml", _ => [1]));

    private static EffekseerParticle Frame(VfxConversion conversion, CommonShaderParameters camera)
    {
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, Matrix.Identity);
        return simulation.Frame(camera).Single();
    }
}
