#nullable enable
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Vfx;

public class VfxBoundsCameraCullingTests
{
    [TestCase(0, 1, 2, 4)]
    [TestCase(150, 200, 300, 400)]
    [TestCase(0, 10000, 20000, 30000)]
    public void AutoFraming_IgnoresTheDummyCamerasFarNearAndOpacityDistanceCulling(float x, float y, float z, float w)
    {
        var source = VfxSpriteLowOpacityCullingTests.Conversion(distances: new(x, y, z, w));
        var unfaded = VfxPreviewBounds.Estimate(VfxSpriteLowOpacityCullingTests.Conversion());
        var actual = VfxPreviewBounds.Estimate(source);
        Assert.That(unfaded, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(unfaded), "The synthetic bounds camera at distance100 must not hide the geometry it is meant to frame.");
    }

    [Test]
    public void AutoFraming_PreservesAuthoredWeakGeometry()
    {
        var actual = VfxPreviewBounds.Estimate(VfxSpriteLowOpacityCullingTests.Conversion(initialAlpha: .005f));
        Assert.That(actual, Is.EqualTo(VfxPreviewBounds.Estimate(VfxSpriteLowOpacityCullingTests.Conversion())));
    }

    [Test]
    public void ExplicitProjectionBypass_RetainsGeometryWhileNormalFramesStillCullIt()
    {
        var simulation = VfxSpriteLowOpacityCullingTests.Simulation(.005f, distances: new(2, 4, 8, 10));
        var camera = VfxSpriteLowOpacityCullingTests.Camera();
        Assert.That(simulation.ProjectedFrame(camera), Is.Empty);
        var unculled = simulation.ProjectedFrame(camera, applyCameraCulling: false);
        Assert.That(unculled, Has.Length.EqualTo(1));
        Assert.That(unculled[0].Render.Colour.W, Is.EqualTo(.005f), "Bypass omits camera-dependent distance fade as well as the opacity gate.");
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UncalibratedEmitterScale_RetainsLegacyFadeAndReportsTheUnsupportedBasis(bool reflected)
    {
        var simulation = VfxSpriteLowOpacityCullingTests.Simulation(distances: new(2, 4, 8, 10));
        var current = reflected ? Matrix.CreateScale(-1, 1, 1) : Matrix.CreateScale(2, 1, 1);
        simulation.SeekWithCurrentAttachment(.1f, current, _ => Matrix.Identity);
        var camera = VfxSpriteLowOpacityCullingTests.Camera() with { CameraPosition = new(0, 0, 3) };
        Assert.That(simulation.Frame(camera).Single().Colour.W, Is.EqualTo(1), "The uncalibrated transform keeps the earlier ordinary-sprite alpha, which had no distance fade.");
        Assert.That(simulation.SpriteCullScaleUnsupported, Is.True);
    }
}
