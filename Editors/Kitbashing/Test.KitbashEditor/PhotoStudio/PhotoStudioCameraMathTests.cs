using System.Reflection;
using Editors.KitbasherEditor;
using Microsoft.Xna.Framework;

namespace Test.KitbashEditor.PhotoStudio;

[TestFixture]
public class PhotoStudioCameraMathTests
{
    private const string CameraMathTypeName =
        "Editors.KitbasherEditor.ChildEditors.PhotoStudio.PhotoStudioCameraMath";

    [TestCase(0.0f, 0.0f, 0.01f)]
    [TestCase(0.8f, 0.32f, 10.0f)]
    [TestCase(-2.7f, -0.5f, 1000.0f)]
    [TestCase(1.5707963f, 0.75f, 25.0f)]
    public void TryCalculateOrbit_RoundTripsArcBallPosition(
        float yaw,
        float pitch,
        float zoom)
    {
        var lookAt = new Vector3(4, -3, 2);
        var position =
            Vector3.Transform(
                Vector3.Backward,
                Matrix.CreateFromYawPitchRoll(yaw, pitch, 0)) *
            zoom +
            lookAt;

        var result = TryCalculateOrbit(
            position,
            lookAt,
            fallbackYaw: 1.25f);

        var reconstructed =
            Vector3.Transform(
                Vector3.Backward,
                Matrix.CreateFromYawPitchRoll(
                    result.Yaw,
                    result.Pitch,
                    0)) *
            result.Zoom +
            lookAt;

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(
                Vector3.Distance(reconstructed, position),
                Is.LessThan(0.001f));
            Assert.That(result.Zoom, Is.EqualTo(zoom).Within(0.001f));
        });
    }

    [Test]
    public void TryCalculateOrbit_VerticalDirectionKeepsFallbackYawAndClampsPitch()
    {
        var result = TryCalculateOrbit(
            new Vector3(0, 10, 0),
            Vector3.Zero,
            fallbackYaw: 0.73f);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Yaw, Is.EqualTo(0.73f));
            Assert.That(
                result.Pitch,
                Is.EqualTo(-MathHelper.PiOver2 + 0.3f)
                    .Within(0.0001f));
            Assert.That(result.Zoom, Is.EqualTo(10).Within(0.0001f));
        });
    }

    [TestCase(float.NaN, 0.0f, 0.0f)]
    [TestCase(float.PositiveInfinity, 0.0f, 0.0f)]
    [TestCase(0.0f, float.NegativeInfinity, 0.0f)]
    public void TryCalculateOrbit_NonFinitePositionIsRejected(
        float x,
        float y,
        float z)
    {
        var result = TryCalculateOrbit(
            new Vector3(x, y, z),
            Vector3.Zero,
            fallbackYaw: 0.5f);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void TryCalculateOrbit_DistanceBelowCameraMinimumIsRejected()
    {
        var result = TryCalculateOrbit(
            new Vector3(0.001f, 0, 0),
            Vector3.Zero,
            fallbackYaw: 0.5f);

        Assert.That(result.Success, Is.False);
    }

    private static OrbitResult TryCalculateOrbit(
        Vector3 position,
        Vector3 lookAt,
        float fallbackYaw)
    {
        var assembly = typeof(DependencyInjectionContainer).Assembly;
        var type = assembly.GetType(CameraMathTypeName);
        Assert.That(
            type,
            Is.Not.Null,
            $"Missing production type {CameraMathTypeName}");

        var method = type!.GetMethod(
            "TryCalculateOrbit",
            BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        object?[] arguments =
        [
            position,
            lookAt,
            fallbackYaw,
            -MathHelper.PiOver2 + 0.3f,
            MathHelper.PiOver2 - 0.3f,
            0.0f,
            0.0f,
            0.0f
        ];
        var success = (bool)method!.Invoke(null, arguments)!;

        return new OrbitResult(
            success,
            (float)arguments[5]!,
            (float)arguments[6]!,
            (float)arguments[7]!);
    }

    private readonly record struct OrbitResult(
        bool Success,
        float Yaw,
        float Pitch,
        float Zoom);
}
