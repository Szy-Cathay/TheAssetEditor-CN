using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Components.Selection;

public class EditOverlayVisibilityTests
{
    private static readonly BoundingBox UnitBounds = new(
        new Vector3(-1.0f, -1.0f, -0.1f),
        new Vector3(1.0f, 1.0f, 0.1f));

    [Test]
    public void CalculateDetailOpacity_FadesMonotonicallyWithProjectedDensity()
    {
        var near = EditOverlayVisibility.CalculateDetailOpacity(
            UnitBounds,
            Matrix.Identity,
            Matrix.Identity,
            Matrix.Identity,
            1000,
            1000,
            10_000);
        var transition = EditOverlayVisibility.CalculateDetailOpacity(
            UnitBounds,
            Matrix.CreateScale(0.2f),
            Matrix.Identity,
            Matrix.Identity,
            1000,
            1000,
            10_000);
        var far = EditOverlayVisibility.CalculateDetailOpacity(
            UnitBounds,
            Matrix.CreateScale(0.02f),
            Matrix.Identity,
            Matrix.Identity,
            1000,
            1000,
            10_000);

        Assert.Multiple(() =>
        {
            Assert.That(near, Is.EqualTo(1.0f));
            Assert.That(transition, Is.InRange(0.05f, 0.95f));
            Assert.That(far, Is.EqualTo(0.0f));
            Assert.That(near, Is.GreaterThan(transition));
            Assert.That(transition, Is.GreaterThan(far));
        });
    }

    [TestCase(0, 1000, 10)]
    [TestCase(1000, 0, 10)]
    [TestCase(1000, 1000, 0)]
    public void CalculateDetailOpacity_InvalidViewportOrEmptyOverlayIsHidden(
        int width,
        int height,
        int primitiveCount)
    {
        var opacity = EditOverlayVisibility.CalculateDetailOpacity(
            UnitBounds,
            Matrix.Identity,
            Matrix.Identity,
            Matrix.Identity,
            width,
            height,
            primitiveCount);

        Assert.That(opacity, Is.Zero);
    }

    [Test]
    public void CalculateDetailOpacity_NearPlaneIntersectionStaysVisible()
    {
        var crossingBounds = new BoundingBox(
            new Vector3(-1, -1, -1),
            new Vector3(1, 1, 1));

        var opacity = EditOverlayVisibility.CalculateDetailOpacity(
            crossingBounds,
            Matrix.Identity,
            Matrix.Identity,
            Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                1,
                0.1f,
                100),
            1000,
            1000,
            100_000);

        Assert.That(opacity, Is.EqualTo(1.0f));
    }
}
