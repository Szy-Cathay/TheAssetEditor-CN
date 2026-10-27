using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxTrailCameraBasisTests
{
    [TestCase(true, .005f)]
    [TestCase(true, .002f)]
    [TestCase(true, .008f)]
    [TestCase(true, .02f)]
    [TestCase(false, .005f)]
    [TestCase(false, .02f)]
    public void OrdinaryVelocityModeFive_UsesTheNativeScreenRightFallback(bool capturedReflectedCamera, float crossLength)
    {
        // Actual R100 VS cb_vfx_camera element0 right=(1,0,0), matched RH view right=(-1,0,0).
        // This is an ordinary velocity-v2 particle diagnostic, not a ribbon/model classification.
        var eye = capturedReflectedCamera ? new Vector3(0, 65.85088348388672f, -68.94400024414062f) : new(0, 0, 12);
        var reference = capturedReflectedCamera ? Vector3.Normalize(new(0, .6427817344665527f, -.7660494446754456f)) : Vector3.Backward;
        var nativeRight = Vector3.Right;
        var position = eye - reference * 12;
        reference = Vector3.Normalize(eye - position);
        var tangent = Vector3.Normalize(Vector3.Cross(nativeRight, reference));
        var velocity = Vector3.Normalize(reference * MathF.Sqrt(1 - crossLength * crossLength) + tangent * crossLength);
        var view = capturedReflectedCamera ? new Matrix(-1, 0, 0, 0,
            0, .7660494446754456f, .6427817344665527f, 0,
            0, .6427817344665527f, -.7660494446754456f, 0,
            0, -6.12908935546875f, -95.14224243164062f, 1)
            : Matrix.CreateLookAt(eye, position, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 200)
            * Matrix.CreateScale(capturedReflectedCamera ? -1 : 1, 1, 1);
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
            new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-aligned with stretching factor")),
            new XElement("property", new XAttribute("name", "velocity_stretch_mul"), VfxLibraryVisibilityTests.Constant("x", 0)),
            new XElement("property", new XAttribute("name", "velocity_stretch_cap"), new XAttribute("value", 1000))));
        var source = document.Write();
        var conversion = TotalWarVfxConverter.Convert(new(source, "diagnostic/native-camera-fallback.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Alignment, Is.EqualTo(3));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.Identity);
        var particles = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>().ToArray();
        Assert.That(particles, Has.Length.EqualTo(1));
        // Explicit controlled VS inputs; the original resource or a live process is never modified.
        particles[0].GetType().GetField("Position")!.SetValue(particles[0], position);
        particles[0].GetType().GetField("Velocity")!.SetValue(particles[0], velocity);
        var camera = new CommonShaderParameters(view, projection, eye, position, 0, 0, 0, 1, Vector3.One, []);
        var actual = simulation.Frame(camera).Single().Transform;

        // Shipped main VS mode5 instructions718..739 use this actual native camera right before the blend.
        var cross = Vector3.Cross(reference, velocity);
        var weight = Math.Clamp(cross.Length() * 100, 0, 1);
        var expectedRight = Vector3.Lerp(nativeRight, Vector3.Normalize(cross), weight);
        var expectedUp = Vector3.Normalize(Vector3.Cross(expectedRight, reference));
        var expectedNormal = Vector3.Cross(expectedRight, expectedUp);
        Assert.Multiple(() =>
        {
            Near(actual.Right, expectedRight);
            Near(actual.Up, expectedUp);
            Near(actual.Backward, expectedNormal);
            Assert.That(Vector3.Cross(actual.Right, actual.Up).Length(), Is.EqualTo(1).Within(.00001f),
                "A real native right-axis control must retain its full area at the half-weight camera transition.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00001f));
}
