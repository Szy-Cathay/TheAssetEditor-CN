#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[Category("Diagnostic")]
public class VfxModelSignedDampingTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly Vector3 Angles = new(-35, 50, 17);
    private static readonly Vector3 Spin = new(150, 220, 300);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(-.1f, -.1f)]
    [TestCase(-.4f, -.4f)]
    [TestCase(-2f, -.1f)]
    [TestCase(-.1f, -2f)]
    [TestCase(-.25f, .5f)]
    [TestCase(.5f, -.25f)]
    public void SignedModelDamping_PreservesAuthoredSpinAndUsesUnclampedExponentialGrowth(float start, float end)
    {
        var bytes = Source(start, end); var conversion = Convert(bytes);
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.ModelQuaternionSpin, Is.True);
            Assert.That(emitter.SpinMin, Is.EqualTo(Spin));
            Assert.That(emitter.SpinMax, Is.EqualTo(Spin));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.875f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.875f, 1, Spin, start, end));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("X")]
    [TestCase("Y")]
    [TestCase("Z")]
    public void NegativeDamping_AcceleratesEveryRigidAxisIncludingPureZ(string axis)
    {
        var spin = axis == "X" ? new Vector3(180, 0, 0) : axis == "Y" ? new Vector3(0, 180, 0) : new Vector3(0, 0, 180);
        var simulation = new TotalWarVfxSimulation(Convert(Source(-.8f, -.8f, spin)));
        simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 1, spin, -.8, -.8));
    }

    [TestCase("CW", -1)]
    [TestCase("CCW", 1)]
    public void AngularGrowth_KeepsTheAuthoredDirectionInsteadOfFlippingItsSign(string direction, int sign)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(-.4f, -.4f, direction: direction)));
        simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 1, Spin * sign, -.4, -.4));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void SignedDampingReference_UsesSourceSecondsAndOriginalSourceLifetime(float speed)
    {
        var child = Source(-2, -.1f); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("signed-model-child", VfxDocument.Read(child)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/signed-model-parent.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, speed, Spin, -2, -.1));
    }

    [Test]
    public void NegativeDamping_PartialFramesAndSeekDoNotCommitAnExtraAccelerationStep()
    {
        var conversion = Convert(Source(-2, -.1f));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.004f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.004f, 1, Spin, -2, -.1));
        var frame = simulation.Frame(Camera).Single();
        for (var index = 0; index < 8; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(frame));
        simulation.Seek(.012f, Matrix.Identity); simulation.Seek(.637f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.637f, 1, Spin, -2, -.1));
        simulation.Seek(.1f, Matrix.Identity); simulation.Advance(.537f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.637f, 1, Spin, -2, -.1));
    }

    [Test]
    public void NegativeDamping_DoesNotExtendTheOriginalParticleLifetime()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(-2, -.1f, life: .5f)));
        simulation.Seek(.49f, Matrix.Identity); Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        simulation.Seek(.51f, Matrix.Identity); Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [TestCase(-1000000f)]
    [TestCase(-1000f)]
    public void UnrepresentableNativeGrowth_DisablesOnlyItsOwnLayerInsteadOfCappingVelocity(float damping)
    {
        var source = VfxDocument.Read(Source(damping, damping));
        var sibling = new XElement(VfxDocument.Read(Source(.1f, .1f)).Root.Descendants("emitter").Single());
        sibling.SetAttributeValue("id", "finite-sibling");
        source.Root.Descendants("emitters").Single().Add(sibling);
        var behaviour = new XElement(source.Root.Descendants("behaviour_chain").Single());
        behaviour.SetAttributeValue("id", "finite-sibling");
        source.Root.Descendants("behaviour_chains").Single().Add(behaviour);
        var bytes = source.Write(); var converted = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/native-growth-range.xml", VfxModelVariantTests.Resolve));
        Assert.That(converted.Emitters, Has.Count.EqualTo(2));
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.1f, Matrix.Identity);
        var frame = simulation.Frame(Camera);
        Assert.That(simulation.ModelSpinRangeUnsupported, Is.True);
        Assert.That(frame, Has.Length.EqualTo(1));
        Assert.That(frame.Single().Layer, Is.EqualTo(1));
        Assert.That(frame.Single().Transform.Right.LengthSquared(), Is.EqualTo(1).Within(.00001f));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    // A normalized quaternion Euler step is an exact Rodrigues turn of 2*atan(|omega|*dt/2).
    // Rodrigues stays double; dt, the sampled scalar, expf result and omega follow native float storage.
    // Expected values use authored inputs, never the product curve sampler or angular state.
    private static Matrix Oracle(float time, float sourceSpeed, Vector3 localSpin, double start, double end)
    {
        var birth = Birth();
        var rotation = new[,] { { (double)birth.M11, birth.M12, birth.M13 },
            { (double)birth.M21, birth.M22, birth.M23 }, { (double)birth.M31, birth.M32, birth.M33 } };
        var authored = new[] { (double)localSpin.X, localSpin.Y, localSpin.Z };
        var omega = Enumerable.Range(0, 3).Select(column => Enumerable.Range(0, 3)
            .Sum(row => authored[row] * rotation[row, column]) * Math.PI / 180).ToArray();
        var committed = 0.0;
        void Step(double target, double delta)
        {
            var sourceDt = (float)(delta * sourceSpeed);
            var phase = Math.Clamp(target * sourceSpeed / (4 + .000001), 0, 1);
            var damping = (float)(start + (end - start) * phase);
            var decay = (float)Math.Exp(-damping * sourceDt);
            for (var axis = 0; axis < 3; ++axis) omega[axis] = (float)((float)omega[axis] * decay);
            var speed = Math.Sqrt(omega.Sum(value => value * value));
            if (speed == 0) return;
            var x = omega[0] / speed; var y = omega[1] / speed; var z = omega[2] / speed;
            var angle = 2 * Math.Atan(speed * sourceDt / 2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1 - c;
            var turn = new[,] { { c + x*x*t, x*y*t + z*s, x*z*t - y*s },
                { x*y*t - z*s, c + y*y*t, y*z*t + x*s }, { x*z*t + y*s, y*z*t - x*s, c + z*z*t } };
            var next = new double[3, 3];
            for (var row = 0; row < 3; ++row) for (var column = 0; column < 3; ++column)
                for (var k = 0; k < 3; ++k) next[row, column] += rotation[row, k] * turn[k, column];
            rotation = next;
        }
        while (committed + Tick <= time + .000001) { committed += Tick; Step(committed, Tick); }
        if (time > committed) Step(time, time - committed);
        return new((float)rotation[0,0], (float)rotation[0,1], (float)rotation[0,2], 0,
            (float)rotation[1,0], (float)rotation[1,1], (float)rotation[1,2], 0,
            (float)rotation[2,0], (float)rotation[2,1], (float)rotation[2,2], 0, 0, 0, 0, 1);
    }

    private static Matrix Birth() => Matrix.CreateRotationX(MathHelper.ToRadians(Angles.X))
        * Matrix.CreateRotationY(MathHelper.ToRadians(Angles.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(Angles.Z));
    private static void Near(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(Tolerance));
        });
    }
    private static VfxConversion Convert(byte[] bytes)
    {
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/signed-model-damping.xml", VfxModelVariantTests.Resolve));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics));
        return conversion;
    }
    private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name),
        new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static string Curve(string axes, double start, double? end = null) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{Number(start)},linear,linear,0,0,0,0,1" +
        (end.HasValue && end != start ? $";1,{Number(end.Value)},linear,linear,0,0,0,0,1" : ""))) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
    private static byte[] Source(float start, float end, Vector3? spin = null, string direction = "CCW", float life = 4)
    {
        var rigid = VfxDocument.Read(VfxModelVariantTests.Source(1)).Root.Descendants("property_rigid_data").Single();
        var emitter = new XElement("emitter", new XAttribute("id", "signed-rigid"), new XAttribute("type", "continuous_rigid"),
            Property("InfiniteLife", "true"), Property("MaximumParticles", 1), Property("ParticleSimulationSpace", "WORLD_SPACE"),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve("x", .1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve("x", life)),
            new XElement("property", new XAttribute("name", "ParticleScale"), Curve("xyz", 1)),
            VectorProperty("ParticleOrientationMin", Angles), VectorProperty("ParticleOrientationMax", Angles),
            VectorProperty("ParticleSpin_Min", spin ?? Spin), VectorProperty("ParticleSpin_Max", spin ?? Spin),
            Property("ParticleSpinDirectionX", direction), Property("ParticleSpinDirectionY", direction), Property("ParticleSpinDirectionZ", direction),
            new XElement("property", new XAttribute("name", "ParticleSpinDeceleration"), Curve("x", start, end)), new XElement(rigid));
        var quality = new XElement("quality", new XAttribute("value", "0"), new XElement("modifier", new XAttribute("fragment", "vs_translation"), Property("restriction", 0)));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "signed-rigid"), quality))).ToString(SaveOptions.DisableFormatting));
    }
}
