using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeVelocityNearParallelTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 1, 12000), new(0, 0, 10), Vector3.Zero,
        0, 0, 0, 1, Vector3.One, []);

    [TestCase(.00000001f)]
    [TestCase(-.00000001f)]
    [TestCase(.000001f)]
    [TestCase(-.000001f)]
    [TestCase(.0001f)]
    public void OrdinaryModeTwo_NormalizesTheActualNonzeroCrossWithoutScreenRightFallback(float sideVelocity)
    {
        // Actual selected official VS0f1c7230... instructions181..189 normalize this cross directly.
        // These are bounded arithmetic controls, not a claim of native visual acceptance at every camera angle.
        var velocity = new Vector3(sideVelocity, 0, 1);
        var reference = Vector3.Normalize(Camera.CameraPosition);
        var look = Vector3.Normalize(velocity);
        var cross = Vector3.Cross(reference, look);
        Assert.That(cross.LengthSquared(), Is.GreaterThan(0));
        var expected = Vector3.Normalize(cross);
        var packet = Frame(velocity).Single();
        var actual = Vector3.Normalize(packet.Transform.Right);
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.000001f),
                "Native mode2 has no mode5 screen-right fallback or threshold transition.");
            Assert.That(Vector3.Distance(Vector3.Normalize(packet.Transform.Up), look), Is.LessThan(.000001f));
            Assert.That(Vector3.Cross(packet.Transform.Right, packet.Transform.Up).Length(), Is.GreaterThan(.9f));
        });
    }

    [TestCase(0f)]
    [TestCase(1e-24f)]
    public void OrdinaryModeTwo_ZeroOrFloatUnderflowCrossDoesNotInventAVisibleQuad(float sideVelocity)
    {
        // dp3=0 -> rsq(0) -> nonfinite native basis. Even without GPU denormal flushing,
        // (1e-24)^2 is below the smallest representable float; no screen-right replacement exists.
        var velocity = new Vector3(sideVelocity, 0, 1);
        var cross = Vector3.Cross(Vector3.Normalize(Camera.CameraPosition), Vector3.Normalize(velocity));
        Assert.That(cross.LengthSquared(), Is.Zero);
        Assert.That(Frame(velocity), Is.Empty);
    }

    private static EffekseerParticle[] Frame(Vector3 velocity)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
            new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-facing velocity-aligned")),
            new XElement("property", new XAttribute("name", "velocity_stretch_mul"), new XAttribute("value", 0))));
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/native-velocity-mode-two.xml", _ => [1]));
        Assert.That(conversion.Emitters.Single().Alignment, Is.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        var particle = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>().Single();
        particle.GetType().GetField("Velocity")!.SetValue(particle, velocity);
        return simulation.Frame(Camera);
    }
}
