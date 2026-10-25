#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[Category("Diagnostic")]
public class VfxModelDynamicLocalSpinTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly Vector3 BirthAngles = new(-35, 50, 17);
    private static readonly Vector3 Spin = new(150, 220, 300);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase("linear")]
    [TestCase("exp1")]
    [TestCase("exp3")]
    public void DynamicModelDamping_UsesEachTickEndpointAndUniformlyDecaysAllThreeAxes(string mode)
    {
        var bytes = Source(damping: Damping(mode)); var conversion = Convert(bytes);
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(UsesNativeModelSpin(emitter), Is.True);
            Assert.That(emitter.SpinMax, Is.EqualTo(Spin));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.875f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.875f, 4, 1, Spin, Birth(), phase => DampingValue(mode, phase)));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void StepDamping_RemainsDiagnosedUntilTheNativeBakedImportContractIsVerified()
    {
        var conversion = Convert(Source(damping: Damping("step")));
        Assert.Multiple(() =>
        {
            Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.False);
            Assert.That(conversion.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
        });
    }

    [Test]
    public void FallingDamping_DoesNotUseItsBirthValueForTheWholeParticleLife()
    {
        var simulation = Simulation(Source(damping: ScalarCurve("x", 3.5, .1)));
        simulation.Seek(.9f, Matrix.Identity);
        var expected = Oracle(.9f, 4, 1, Spin, Birth(), phase => 3.5 + (.1 - 3.5) * phase);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        Assert.That(Vector3.Distance(expected.Up, Oracle(.9f, 4, 1, Spin, Birth(), _ => 3.5).Up), Is.GreaterThan(.01f));
    }

    [TestCase("WORLD_SPACE", null)]
    [TestCase("WORLD_SPACE", "DISABLED")]
    [TestCase("WORLD_SPACE", "RELATIVE_TO_VFX_UP_AXIS")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", null)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", "DISABLED")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", "RELATIVE_TO_VFX_UP_AXIS")]
    public void DynamicSpin_KeepsTheAuthoredBirthBasisAndOnlyTheRequestedPositionFollow(string space, string? relative)
    {
        var source = VfxDocument.Read(Source(space: space)); var emitter = Emitter(source);
        if (relative != null) emitter.Add(Property("ParticleRelativeSpawnOrientation", relative));
        emitter.Add(new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(new(0, 33, 0))));
        var firstRoot = Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.4f);
        var laterRoot = Matrix.CreateRotationY(.8f) * Matrix.CreateTranslation(3, 2, 1);
        var birth = relative == "RELATIVE_TO_VFX_UP_AXIS"
            ? Birth() * Matrix.CreateRotationY(MathHelper.ToRadians(33)) * firstRoot : Birth();
        var simulation = Simulation(source.Write()); simulation.Seek(0, firstRoot); simulation.Seek(.4f, laterRoot);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(.4f, 4, 1, Spin, birth, phase => DampingValue("linear", phase)));
        Assert.That(actual.Translation, Is.EqualTo(space == "LOCAL_SPACE_POSITION_ONLY" ? laterRoot.Translation : Vector3.Zero));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void ReferenceSpeed_UsesSourceSecondsAndSourceLifetimeForDynamicPhase(float speed)
    {
        var child = VfxDocument.Read(Source()); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("model-dynamic-child", child); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/model-dynamic-parent.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.625f, 4, speed, Spin, Birth(), phase => DampingValue("linear", phase)));
    }

    [TestCase(0f)]
    [TestCase(.375f)]
    [TestCase(1f)]
    public void MinMaxDamping_KeepsOneBirthRandomWeightForEveryTick(float randomWeight)
    {
        const string damping = "x_min,0,0;0,.1,linear,linear,0,0,0,0,1;1,.8,linear,linear,0,0,0,0,1|"
            + "x_max,0,0;0,2,linear,linear,0,0,0,0,1;1,4.5,linear,linear,0,0,0,0,1:x,1;0,1";
        var simulation = Simulation(Source(damping: damping)); simulation.Seek(0, Matrix.Identity);
        // Set only the random input. The expected curve and angular integration are independent.
        SetParticleRandomWeight(simulation, randomWeight);
        foreach (var time in new[] { .17f, .51f, .91f })
        {
            simulation.Seek(time, Matrix.Identity);
            Near(simulation.Frame(Camera).Single().Transform, Oracle(time, 4, 1, Spin, Birth(), phase =>
                (.1 + .7 * phase) * (1 - randomWeight) + (2 + 2.5 * phase) * randomWeight));
        }
    }

    [TestCase("X")]
    [TestCase("Y")]
    [TestCase("Z")]
    public void SingleAxisDynamicModels_UseRigidBirthBasisRatherThanTheSpriteWorldZShortcut(string axis)
    {
        var spin = axis switch { "X" => new Vector3(450, 0, 0), "Y" => new Vector3(0, 450, 0), _ => new Vector3(0, 0, 450) };
        var conversion = Convert(Source(spin: spin));
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.625f, 4, 1, spin, Birth(), phase => DampingValue("linear", phase)));
    }

    [Test]
    public void FirstTick_DecaysAllAxisVelocityBeforeItsNormalizedQuaternionTurn()
    {
        var time = (float)Tick;
        var simulation = Simulation(Source(damping: ScalarCurve("x", 4, 5)));
        simulation.Seek(time, Matrix.Identity);
        var expected = Oracle(time, 4, 1, Spin, Birth(), phase => 4 + phase);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        var undamped = Oracle(time, 4, 1, Spin, Birth(), _ => 0);
        Assert.That(Vector3.Distance(expected.Up, undamped.Up), Is.GreaterThan(.0005f));
    }

    [Test]
    public void PartialTickFrames_DoNotCommitDynamicAngularState()
    {
        var simulation = Simulation(Source()); simulation.Seek(.004f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        Near(first.Transform, Oracle(.004f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase)));
        for (var index = 0; index < 8; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.007f, Matrix.Identity); simulation.Frame(Camera); simulation.Seek(.012f, Matrix.Identity);
        var direct = Simulation(Source()); direct.Seek(.012f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.012f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase)));
    }

    [Test]
    public void SeekingBackAndPartitioningAdvance_RebuildTheSameDynamicModelState()
    {
        var simulation = Simulation(Source()); simulation.Seek(.637f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        simulation.Seek(.11f, Matrix.Identity);
        foreach (var dt in new[] { .127f, .2f, .2f }) simulation.Advance(dt, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, first.Transform);
        Near(first.Transform, Oracle(.637f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase)));
    }

    [Test]
    public void DynamicModelState_DoesNotShiftNonAngularBirthRandomness()
    {
        var source = VfxDocument.Read(Source()); var emitter = Emitter(source);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = ScalarCurve("x", 8);
        Set(emitter, "MaximumParticles", "32");
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaBox"), ScalarCurve("xyz", 3)));
        var current = Convert(source.Write()); var legacy = Convert(source.Write());
        typeof(VfxPreviewEmitter).GetProperty("ModelQuaternionSpin")!.SetValue(legacy.Emitters.Single(), false);
        var simulation = new TotalWarVfxSimulation(current); var baseline = new TotalWarVfxSimulation(legacy);
        simulation.Seek(1.1f, Matrix.Identity); baseline.Seek(1.1f, Matrix.Identity);
        var actual = simulation.Frame(Camera); var expected = baseline.Frame(Camera);
        Assert.That(UsesNativeModelSpin(current.Emitters.Single()), Is.True);
        Assert.That(actual, Has.Length.EqualTo(expected.Length)); Assert.That(actual.Length, Is.GreaterThan(4));
        for (var index = 0; index < actual.Length; ++index)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[index].Transform.Translation, Is.EqualTo(expected[index].Transform.Translation));
                Assert.That(actual[index].Colour, Is.EqualTo(expected[index].Colour));
                Assert.That(actual[index].UV, Is.EqualTo(expected[index].UV));
                Assert.That(actual[index].Time, Is.EqualTo(expected[index].Time));
                Assert.That(actual[index].AppearancePhase, Is.EqualTo(expected[index].AppearancePhase));
            });
        }
    }

    [TestCase(null)]
    [TestCase("DISABLED")]
    [TestCase("RELATIVE_TO_VFX_UP_AXIS")]
    public void FullLocal_TransportsTheBirthPoseWithoutAppendingTheAbsoluteRootAgain(string? relative)
    {
        var source = LocalSource(relative: relative, damping: ScalarCurve("x", .15));
        var conversion = Convert(source);
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.625f, RootAt(.625f), RootAt);
        var birth = relative == "RELATIVE_TO_VFX_UP_AXIS" ? Birth() * EmitterRotation * RootAt(0) : Birth();
        var expected = Oracle(.625f, 4, 1, Spin, birth, _ => .15, RootAt);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        Assert.That(Vector3.Distance(expected.Up, (expected * RootAt(.625f)).Up), Is.GreaterThan(.1f),
            "The control must detect appending the absolute root to an already transported world pose.");
    }

    [TestCase("constant-xyz")]
    [TestCase("dynamic-xyz")]
    [TestCase("constant-z")]
    [TestCase("dynamic-z")]
    [TestCase("zero")]
    public void FullLocal_DecaysAndTurnsBeforeTransportingBothPoseAndAngularVelocity(string combination)
    {
        var spin = combination == "zero" ? Vector3.Zero : combination.EndsWith("-z", StringComparison.Ordinal) ? new Vector3(0, 0, 300) : Spin;
        var isDynamic = combination.StartsWith("dynamic", StringComparison.Ordinal);
        var damping = isDynamic ? Damping("linear") : ScalarCurve("x", .15);
        var conversion = Convert(LocalSource(damping: damping, spin: spin));
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.875f, RootAt(.875f), RootAt);
        double SampleDamping(double phase) => isDynamic ? DampingValue("linear", phase) : .15;
        var expected = Oracle(.875f, 4, 1, spin, Birth(), SampleDamping, RootAt);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        var untransported = Oracle(.875f, 4, 1, spin, Birth(), SampleDamping);
        Assert.That(Vector3.Distance(expected.Up, untransported.Up), Is.GreaterThan(.05f));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void FullLocalReferenceSpeed_ScalesSelfSpinButRootMotionUsesThePreviewClock(float speed)
    {
        var child = VfxDocument.Read(LocalSource()); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("model-full-local-child", child); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/model-full-local-speed.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, RootAt(.625f), RootAt);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.625f, 4, speed, Spin, Birth(), phase => DampingValue("linear", phase), RootAt));
    }

    [Test]
    public void FullLocalPartialTicks_DoNotCommitThePreviewRootOrItsTransportedOmega()
    {
        var source = LocalSource(); var simulation = Simulation(source);
        simulation.Seek(.004f, RootAt(.004f), RootAt);
        var first = simulation.Frame(Camera).Single();
        Near(first.Transform, Oracle(.004f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase), RootAt));
        for (var index = 0; index < 8; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.007f, RootAt(.007f), RootAt); simulation.Frame(Camera);
        simulation.Seek(.012f, RootAt(.012f), RootAt);
        var direct = Simulation(source); direct.Seek(.012f, RootAt(.012f), RootAt);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.012f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase), RootAt));
    }

    [Test]
    public void FullLocalSameTimeLiveRootChange_UpdatesOnlyTheFrameCopy()
    {
        const float time = .4f;
        var simulation = Simulation(LocalSource()); simulation.Seek(time, RootAt(time), RootAt);
        var committed = Oracle(time, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase), RootAt);
        Near(simulation.Frame(Camera).Single().Transform, committed);
        var liveRoot = Matrix.CreateRotationZ(-.7f) * Matrix.CreateRotationX(.9f);
        simulation.SeekWithCurrentAttachment(time, liveRoot, RootAt);
        var expected = FromBasis(Multiply(Basis(committed), Multiply(Transpose(RigidBasis(RootAt(time))), RigidBasis(liveRoot))));
        var first = simulation.Frame(Camera).Single(); Near(first.Transform, expected);
        for (var index = 0; index < 4; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.45f, RootAt(.45f), RootAt);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.45f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase), RootAt));
    }

    [Test]
    public void FullLocalSeekingBackAndReplaying_RebuildsTheSameRootAndAngularHistory()
    {
        var simulation = Simulation(LocalSource()); simulation.Seek(.637f, RootAt(.637f), RootAt);
        var first = simulation.Frame(Camera).Single();
        simulation.Seek(.11f, RootAt(.11f), RootAt);
        foreach (var target in new[] { .237f, .437f, .637f }) simulation.Seek(target, RootAt(target), RootAt);
        Near(simulation.Frame(Camera).Single().Transform, first.Transform);
        Near(first.Transform, Oracle(.637f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase), RootAt));
    }

    [Test]
    public void FullLocalDynamicEmitterOrientation_UsesItsDedicatedNativeQuaternionCurve()
    {
        var source = VfxDocument.Read(LocalSource());
        Emitter(source).Elements().Single(e => (string?)e.Attribute("name") == "EmitterOrientation").Value = ScalarCurve("xyz", 0, 33);
        var bytes = source.Write(); var conversion = Convert(bytes);
        Assert.That(UsesNativeModelSpin(conversion.Emitters.Single()), Is.True);
        Assert.That(conversion.Emitters.Single().ModelEmitterOrientation, Is.Not.Null);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void FullLocalPositiveUniformRootScale_KeepsTheVerifiedAngularPose(float scale)
    {
        Matrix ScaledRoot(float time) => Matrix.CreateScale(scale) * RootAt(time);
        var simulation = Simulation(LocalSource()); simulation.Seek(.625f, ScaledRoot(.625f), ScaledRoot);
        Near(simulation.Frame(Camera).Single().Transform,
            Matrix.CreateScale(scale) * Oracle(.625f, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase), RootAt));
        Assert.That(typeof(TotalWarVfxSimulation).GetProperty("ModelSpinBasisUnsupported")?.GetValue(simulation), Is.False);
    }

    [Test]
    public void FullLocalNonuniformRootScale_KeepsTheLayerVisibleAndReportsTheUnverifiedBasis()
    {
        Matrix NonuniformRoot(float time) => Matrix.CreateScale(2, 1, 1) * RootAt(time);
        var simulation = Simulation(LocalSource()); simulation.Seek(.625f, NonuniformRoot(.625f), NonuniformRoot);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        Assert.That(typeof(TotalWarVfxSimulation).GetProperty("ModelSpinBasisUnsupported")?.GetValue(simulation), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void DynamicModelRotation_ReachesTheGpuAsItsIndependentStaticPose(bool reflected)
    {
        const float time = .625f;
        var expected = Oracle(time, 4, 1, Spin, Birth(), phase => DampingValue("linear", phase));
        AssertGpuPose(Source(), time, expected, reflected);
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void FullLocalDynamicRotation_ReachesTheGpuAsItsIndependentStaticWorldPose(bool reflected)
    {
        const float time = .625f;
        var source = LocalSource(relative: "RELATIVE_TO_VFX_UP_AXIS");
        var birth = Birth() * EmitterRotation * RootAt(0);
        var expected = Oracle(time, 4, 1, Spin, birth, phase => DampingValue("linear", phase), RootAt);
        AssertGpuPose(source, time, expected, reflected, RootAt);
    }

    private static void AssertGpuPose(byte[] source, float time, Matrix expected, bool reflected,
        Func<float, Matrix>? attachmentAtTime = null)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        var radians = new Vector3(MathF.Atan2(expected.M23, expected.M33), MathF.Asin(-expected.M13),
            MathF.Atan2(expected.M12, expected.M11));
        var angles = radians * (180 / MathHelper.Pi);
        var reconstructed = Matrix.CreateRotationX(MathHelper.ToRadians(angles.X))
            * Matrix.CreateRotationY(MathHelper.ToRadians(angles.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(angles.Z));
        Near(reconstructed, expected);
        Assert.That(expected.Translation, Is.EqualTo(Vector3.Zero), "This controlled GPU fixture isolates angular transmission.");
        var reference = VfxDocument.Read(source); var emitter = Emitter(reference);
        Set(emitter, "ParticleSimulationSpace", "WORLD_SPACE");
        emitter.Elements().Where(e => (string?)e.Attribute("name") is "ParticleRelativeSpawnOrientation"
            or "ParticleRelativeOrientation" or "RelativeParticleOrientation" or "EmitterOrientation").Remove();
        foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
            emitter.Elements().Single(e => (string?)e.Attribute("name") == name).ReplaceWith(VectorProperty(name, angles));
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
            emitter.Elements().Single(e => (string?)e.Attribute("name") == name).ReplaceWith(VectorProperty(name, Vector3.Zero));
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = ScalarCurve("x", 0);
        // A constant coloured texture removes texture-filter quantization from the pose comparison.
        var texture = VfxModelVariantPixelTests.Dds(new Color(70, 180, 230, 155));
        byte[]? Resolve(string path) => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            ? texture : VfxModelVariantTests.Resolve(path);
        VfxPreviewSnapshot Snapshot(byte[] bytes) => new(bytes, "diagnostic/model-dynamic-local-pixels.xml", Resolve);
        var actualSimulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(Snapshot(source)));
        actualSimulation.Seek(time, attachmentAtTime?.Invoke(time) ?? Matrix.Identity, attachmentAtTime);
        Near(actualSimulation.Frame(Camera).Single().Transform, expected);
        var targetPoint = Vector3.Transform(new(.35f, .35f, 0), expected);
        var eye = targetPoint + expected.Backward * 6 + expected.Right * .4f + expected.Up * .2f;
        var camera = Camera with { View = Matrix.CreateLookAt(eye, targetPoint, expected.Up),
            Projection = Matrix.CreateOrthographic(3, 3, .1f, 100) * Matrix.CreateScale(reflected ? -1 : 1, 1, 1), CameraPosition = eye };
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            Vector4[] Render(byte[] bytes, Func<float, Matrix>? sampleAttachment)
            {
                using var preview = new EffekseerVfxPreview(device); preview.Reload(Snapshot(bytes));
                if (sampleAttachment == null) preview.Seek(time, Matrix.Identity);
                else preview.Seek(time, sampleAttachment(time), sampleAttachment);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
            }
            var actual = Render(source, attachmentAtTime); var control = Render(reference.Write(), null);
            Assert.That(control.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
            Assert.That(actual.Zip(control).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThanOrEqualTo(Tolerance),
                "Full float RGBA must match the independently integrated static model pose.");
        }
        finally { graphics.Release(); }
    }

    // Independent double Rodrigues integration: decay XYZ at the current phase, then turn
    // by 2*atan(|omegaAfter|*sourceDt/2). No product SpinAt or curve sampler is used.
    private static Matrix Oracle(float previewTime, float sourceLife, float sourceSpeed, Vector3 localDegrees,
        Matrix birth, Func<double, double> damping, Func<float, Matrix>? rootAtTime = null)
    {
        var rotation = Basis(birth);
        var omega = Transform(new[] { (double)localDegrees.X, localDegrees.Y, localDegrees.Z }, rotation)
            .Select(value => value * Math.PI / 180).ToArray();
        var committed = 0.0;
        var previousRoot = RigidBasis(rootAtTime?.Invoke(0) ?? Matrix.Identity);
        void Integrate(double target, double delta)
        {
            var sourceDelta = delta * sourceSpeed;
            var sourceAge = target * sourceSpeed;
            var phase = Math.Clamp(sourceAge / (sourceLife + .000001), 0, 1);
            var decay = Math.Exp(-damping(phase) * sourceDelta);
            for (var axis = 0; axis < 3; ++axis) omega[axis] *= decay;
            var speed = Math.Sqrt(omega.Sum(value => value * value));
            if (speed != 0)
            {
                var x = omega[0] / speed; var y = omega[1] / speed; var z = omega[2] / speed;
                var angle = 2 * Math.Atan(speed * sourceDelta / 2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1 - c;
                var turn = new[,] { { c + x*x*t, x*y*t + z*s, x*z*t - y*s },
                    { x*y*t - z*s, c + y*y*t, y*z*t + x*s }, { x*z*t + y*s, y*z*t - x*s, c + z*z*t } };
                rotation = Multiply(rotation, turn);
            }
            if (rootAtTime != null)
            {
                var currentRoot = RigidBasis(rootAtTime((float)target));
                var deltaRoot = Multiply(Transpose(previousRoot), currentRoot);
                rotation = Multiply(rotation, deltaRoot); omega = Transform(omega, deltaRoot);
                previousRoot = currentRoot;
            }
        }
        while (committed + Tick <= previewTime + .000001) { committed += Tick; Integrate(committed, Tick); }
        if (previewTime > committed) Integrate(previewTime, previewTime - committed);
        return FromBasis(rotation);
    }

    private static double DampingValue(string mode, double phase) => mode switch
    {
        "step" => phase <= .12 ? .1 : phase <= .19 ? 4.5 : .6,
        "exp1" => .1 + 4.4 * (phase * phase + phase) / 2,
        "exp3" => .1 + 4.4 * phase * phase * phase,
        _ => .1 + 4.4 * phase,
    };
    private static string Damping(string mode) => mode == "step"
        ? "x_min,0,0;0,.1,linear,step,0,0,0,0,1;.12,4.5,linear,step,0,0,0,0,1;.19,.6,linear,step,0,0,0,0,1;1,.6,linear,linear,0,0,0,0,1:x,0;0,0"
        : ScalarCurve("x", .1, 4.5, mode);
    private static double[,] Basis(Matrix matrix) => new[,] { { (double)matrix.M11, matrix.M12, matrix.M13 },
        { (double)matrix.M21, matrix.M22, matrix.M23 }, { (double)matrix.M31, matrix.M32, matrix.M33 } };
    private static double[,] RigidBasis(Matrix matrix)
    {
        // A float callback matrix is only approximately orthogonal. Native unit quaternions
        // carry a rigid basis; remove input rounding with independent double Gram-Schmidt.
        var x = new[] { (double)matrix.M11, matrix.M12, matrix.M13 };
        var xLength = Math.Sqrt(x.Sum(value => value * value));
        for (var axis = 0; axis < 3; ++axis) x[axis] /= xLength;
        var y = new[] { (double)matrix.M21, matrix.M22, matrix.M23 };
        var projection = x.Zip(y).Sum(pair => pair.First * pair.Second);
        for (var axis = 0; axis < 3; ++axis) y[axis] -= projection * x[axis];
        var yLength = Math.Sqrt(y.Sum(value => value * value));
        for (var axis = 0; axis < 3; ++axis) y[axis] /= yLength;
        return new[,] { { x[0], x[1], x[2] }, { y[0], y[1], y[2] },
            { x[1]*y[2] - x[2]*y[1], x[2]*y[0] - x[0]*y[2], x[0]*y[1] - x[1]*y[0] } };
    }
    private static double[] Transform(double[] vector, double[,] matrix) => Enumerable.Range(0, 3)
        .Select(column => Enumerable.Range(0, 3).Sum(row => vector[row] * matrix[row, column])).ToArray();
    private static double[,] Transpose(double[,] matrix) => new[,]
    {
        { matrix[0,0], matrix[1,0], matrix[2,0] }, { matrix[0,1], matrix[1,1], matrix[2,1] }, { matrix[0,2], matrix[1,2], matrix[2,2] },
    };
    private static double[,] Multiply(double[,] first, double[,] second)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; ++row) for (var column = 0; column < 3; ++column)
            for (var k = 0; k < 3; ++k) result[row, column] += first[row, k] * second[k, column];
        return result;
    }
    private static Matrix FromBasis(double[,] rotation) => new((float)rotation[0,0], (float)rotation[0,1], (float)rotation[0,2], 0,
        (float)rotation[1,0], (float)rotation[1,1], (float)rotation[1,2], 0,
        (float)rotation[2,0], (float)rotation[2,1], (float)rotation[2,2], 0, 0, 0, 0, 1);
    private static Matrix Birth() => Matrix.CreateRotationX(MathHelper.ToRadians(BirthAngles.X))
        * Matrix.CreateRotationY(MathHelper.ToRadians(BirthAngles.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(BirthAngles.Z));
    private static Matrix EmitterRotation => Matrix.CreateRotationY(MathHelper.ToRadians(33));
    private static Matrix RootAt(float time) => Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.4f)
        * Matrix.CreateRotationX(.35f * time) * Matrix.CreateRotationY(-.6f * time) * Matrix.CreateRotationZ(.15f * time);
    private static void Near(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(Tolerance));
        });
    }
    private static void SetParticleRandomWeight(TotalWarVfxSimulation simulation, float value)
    {
        var particles = (IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!;
        var particle = particles.Cast<object>().Single();
        particle.GetType().GetField("RandomAlpha")!.SetValue(particle, value);
    }
    private static bool UsesNativeModelSpin(VfxPreviewEmitter emitter) =>
        typeof(VfxPreviewEmitter).GetProperty("ModelQuaternionSpin")?.GetValue(emitter) is true;
    private static TotalWarVfxSimulation Simulation(byte[] bytes) => new(Convert(bytes));
    private static VfxConversion Convert(byte[] bytes)
    {
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/model-dynamic-local-spin.xml", VfxModelVariantTests.Resolve));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters.Single().Kind, Is.EqualTo(1));
        return conversion;
    }
    private static XElement Emitter(VfxDocument source) => source.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name),
        new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static void Set(XElement emitter, string name, string value) => emitter.Elements()
        .Single(e => (string?)e.Attribute("name") == name).SetAttributeValue("value", value);
    private static string ScalarCurve(string axes, double start, double? end = null, string mode = "linear") => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{start.ToString(CultureInfo.InvariantCulture)},linear,{mode},0,0,0,0,1" +
        (end.HasValue && end.Value != start ? $";1,{end.Value.ToString(CultureInfo.InvariantCulture)},{mode},linear,0,0,0,0,1" : ""))) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
    private static string VectorCurve(Vector3 value) => string.Join('|', new[] { ("x", value.X), ("y", value.Y), ("z", value.Z) }
        .Select(pair => $"{pair.Item1}_min,0,0;0,{pair.Item2.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] LocalSource(string? relative = null, string? damping = null, Vector3? spin = null)
    {
        var source = VfxDocument.Read(Source("LOCAL_SPACE_POSITION_ROTATION", damping, spin));
        var emitter = Emitter(source);
        emitter.Add(new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(new(0, 33, 0))));
        if (relative != null) emitter.Add(Property("ParticleRelativeSpawnOrientation", relative));
        return source.Write();
    }
    private static byte[] Source(string space = "WORLD_SPACE", string? damping = null, Vector3? spin = null)
    {
        var rigid = VfxDocument.Read(VfxModelVariantTests.Source(1)).Root.Descendants("property_rigid_data").Single();
        var emitter = new XElement("emitter", new XAttribute("id", "diagnostic-model-dynamic"), new XAttribute("type", "continuous_rigid"),
            Property("InfiniteLife", "true"), Property("MaximumParticles", 1), Property("ParticleSimulationSpace", space),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), ScalarCurve("x", .1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), ScalarCurve("x", 4)),
            new XElement("property", new XAttribute("name", "ParticleScale"), ScalarCurve("xyz", 1)),
            VectorProperty("ParticleOrientationMin", BirthAngles), VectorProperty("ParticleOrientationMax", BirthAngles),
            VectorProperty("ParticleSpin_Min", spin ?? Spin), VectorProperty("ParticleSpin_Max", spin ?? Spin),
            Property("ParticleSpinDirectionX", "CCW"), Property("ParticleSpinDirectionY", "CCW"), Property("ParticleSpinDirectionZ", "CCW"),
            new XElement("property", new XAttribute("name", "ParticleSpinDeceleration"), damping ?? Damping("linear")), new XElement(rigid));
        var quality = new XElement("quality", new XAttribute("value", "0"),
            new XElement("modifier", new XAttribute("fragment", "vs_translation"), Property("restriction", 0)));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "diagnostic-model-dynamic"), quality))).ToString(SaveOptions.DisableFormatting));
    }
}
