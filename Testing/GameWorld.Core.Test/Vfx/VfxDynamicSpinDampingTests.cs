#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxDynamicSpinDampingTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    // Independent native GPU fixture: ordinary-gpu-scalar-execution-v2.json, authored constant -0.4.
    private const double NativeMinusPointFour = -.39990234375;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase("CW", -1)]
    [TestCase("CCW", 1)]
    public void DynamicDamping_KeepsTheAuthoredSpinAndUsesCurrentLifetimePhase(string direction, int sign)
    {
        var bytes = Source(direction: direction);
        var converted = Convert(bytes);
        Assert.That(converted.Emitters.Single().SpinMax.Z, Is.EqualTo(180), "A valid dynamic curve must not erase the authored spin.");
        Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.75f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.75f, 4, 1, sign * 180, 17, 0, .8));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(.1f, 1.4f)]
    [TestCase(1.4f, .1f)]
    public void RisingAndFallingDamping_MatchIndependentDecayBeforeTurnIntegration(float start, float end)
    {
        var simulation = Simulation(Source(dampingStart: start, dampingEnd: end)); simulation.Seek(.875f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.875f, 4, 1, 180, 17, start, end));
    }

    [TestCase(-.4f, -.4f, "CW", -1)]
    [TestCase(-.4f, -.4f, "CCW", 1)]
    [TestCase(6f, 6f, "CW", -1)]
    [TestCase(6f, 6f, "CCW", 1)]
    public void SignedOrHighConstantDamping_PreservesAuthoredSpinAndMatchesTheNativeTick(float start, float end, string direction, int sign)
    {
        var bytes = Source(dampingStart: start, dampingEnd: end, direction: direction);
        var converted = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMax.Z, Is.EqualTo(180));
            Assert.That(converted.Emitters.Single().DynamicSpinDamping, Is.True);
            Assert.That(converted.Emitters.Single().ComplexSpinDamping, Is.False);
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.875f, Matrix.Identity);
        var damping = start == -.4f ? NativeMinusPointFour : start;
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.875f, 4, 1, sign * 180, 17, damping, damping));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(-.4f, -.4f)]
    [TestCase(6f, 6f)]
    public void SignedOrHighConstantDamping_PartialFramesReplayAndPartitionsUseTheSameTicks(float start, float end)
    {
        var bytes = Source(dampingStart: start, dampingEnd: end);
        var damping = start == -.4f ? NativeMinusPointFour : start;
        var simulation = Simulation(bytes); simulation.Seek(.004f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.004f, 4, 1, 180, 17, damping, damping));
        var partial = simulation.Frame(Camera).Single();
        for (var index = 0; index < 8; index++) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(partial));
        simulation.Seek(.007f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.012f, Matrix.Identity);
        var direct = Simulation(bytes); direct.Seek(.012f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(direct.Frame(Camera).Single().Transform, Oracle(.012f, 4, 1, 180, 17, damping, damping));
        simulation.Seek(.637f, Matrix.Identity); var sought = simulation.Frame(Camera).Single().Transform;
        Near(sought, Oracle(.637f, 4, 1, 180, 17, damping, damping));
        simulation.Seek(0, Matrix.Identity);
        foreach (var elapsed in new[] { .11f, .127f, .2f, .2f }) simulation.Advance(elapsed, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, sought);
        simulation.Seek(.1f, Matrix.Identity); simulation.Advance(.537f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, sought);
    }

    [Test]
    public void FirstTick_DecaysVelocityBeforeAdvancingTheQuaternion()
    {
        var time = (float)Tick;
        var simulation = Simulation(Source(dampingStart: 4, dampingEnd: 5)); simulation.Seek(time, Matrix.Identity);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(time, 4, 1, 180, 17, 4, 5));
        var noDecayAngle = 17 + 2 * Math.Atan(Math.PI * Tick / 2) * 180 / Math.PI;
        Assert.That(Vector3.Distance(actual.Right, Matrix.CreateRotationZ(MathHelper.ToRadians((float)noDecayAngle)).Right),
            Is.GreaterThan(.0005f), "This control must distinguish decay-before-turn from turn-before-decay.");
    }

    [Test]
    public void SeekingBackAndReplaying_RestoresDynamicAngularState()
    {
        var simulation = Simulation(Source()); simulation.Seek(.825f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        simulation.Seek(.175f, Matrix.Identity); simulation.Advance(.65f, Matrix.Identity);
        var replay = simulation.Frame(Camera).Single();
        Near(replay.Transform, first.Transform);
        Assert.That(replay.Time, Is.EqualTo(first.Time).Within(.000001f));
        Near(replay.Transform, Oracle(.825f, 4, 1, 180, 17, 0, .8));
    }

    [Test]
    public void PartitionedAdvanceAndOneSeek_UseTheSameFixedTicks()
    {
        var sought = Simulation(Source()); sought.Seek(.637f, Matrix.Identity);
        var advanced = Simulation(Source()); advanced.Seek(0, Matrix.Identity);
        foreach (var elapsed in new[] { .11f, .127f, .2f, .2f }) advanced.Advance(elapsed, Matrix.Identity);
        Near(advanced.Frame(Camera).Single().Transform, sought.Frame(Camera).Single().Transform);
        Near(sought.Frame(Camera).Single().Transform, Oracle(.637f, 4, 1, 180, 17, 0, .8));
    }

    [Test]
    public void PartialTickFrames_DoNotMutateTheNextCommittedTick()
    {
        var simulation = Simulation(Source()); simulation.Seek(.004f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.004f, 4, 1, 180, 17, 0, .8));
        var first = simulation.Frame(Camera).Single();
        for (var index = 0; index < 8; index++) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.007f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.012f, Matrix.Identity);
        var reference = Simulation(Source()); reference.Seek(.012f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, reference.Frame(Camera).Single().Transform);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.012f, 4, 1, 180, 17, 0, .8));
    }

    [TestCase(.5f, 0f, .8f)]
    [TestCase(2f, 0f, .8f)]
    [TestCase(.5f, -.4f, -.4f)]
    [TestCase(2f, -.4f, -.4f)]
    [TestCase(.5f, 6f, 6f)]
    [TestCase(2f, 6f, 6f)]
    public void ReferenceSpeed_UsesSourceSecondsForDecayAndSourceLifetimeForPhase(float speed, float start, float end)
    {
        var child = VfxDocument.Read(Source(dampingStart: start, dampingEnd: end)); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("dynamic-spin-child", child); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var converted = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/root.xml", _ => child.Write()));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.625f, Matrix.Identity);
        var first = start == end && start == -.4f ? NativeMinusPointFour : start;
        var last = start == end && start == -.4f ? NativeMinusPointFour : end;
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.625f, 4, speed, 180, 17, first, last));
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", true)]
    public void IndependentSpin_KeepsWorldOrientationWhilePositionFollowsItsOwnSpace(string space, bool followsPosition)
    {
        var simulation = Simulation(Source(space: space)); simulation.Seek(0, Matrix.Identity);
        var root = Matrix.CreateRotationX(.7f) * Matrix.CreateRotationY(-.4f) * Matrix.CreateTranslation(3, 2, 1);
        simulation.Seek(.4f, root);
        var actual = simulation.Frame(Camera).Single().Transform;
        Near(actual, Oracle(.4f, 4, 1, 180, 17, 0, .8));
        Assert.That(actual.Translation, Is.EqualTo(followsPosition ? root.Translation : Vector3.Zero));
    }

    [TestCase(0f, .8f)]
    [TestCase(-.4f, -.4f)]
    [TestCase(6f, 6f)]
    public void FiniteEmission_LeavesTheOriginalParticleSpinningUntilItsOwnDeath(float start, float end)
    {
        var bytes = Source(life: .5f, dampingStart: start, dampingEnd: end);
        var doc = VfxDocument.Read(bytes); var emitter = Emitter(doc);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .1f), new XAttribute("max", .1f)));
        var simulation = Simulation(doc.Write()); simulation.Seek(.4f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        var first = start == end && start == -.4f ? NativeMinusPointFour : start;
        var last = start == end && start == -.4f ? NativeMinusPointFour : end;
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.4f, .5f, 1, 180, 17, first, last));
        simulation.Seek(.51f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [TestCase(0f, .8f)]
    [TestCase(-.4f, -.4f)]
    [TestCase(6f, 6f)]
    public void EnablingDynamicSpin_DoesNotConsumeAnotherBirthRandomSample(float start, float end)
    {
        var spinning = VfxDocument.Read(Source(dampingStart: start, dampingEnd: end)); var emitter = Emitter(spinning);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = Curve("x", 8);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "MaximumParticles").SetAttributeValue("value", 32);
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaBox"), Curve("xyz", 3)));
        spinning.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_cell_texture"),
            Property("number_of_cells_x", 2), Property("number_of_cells_y", 2), Property("randomize_cell", "true")));
        var stationary = VfxDocument.Read(spinning.Write());
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" }) SetVector(Emitter(stationary), name, Vector3.Zero);
        Emitter(stationary).Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", 0);
        var actual = Simulation(spinning.Write()); actual.Seek(1.1f, Matrix.Identity);
        var baseline = Simulation(stationary.Write()); baseline.Seek(1.1f, Matrix.Identity);
        var expectedFrame = baseline.Frame(Camera); var actualFrame = actual.Frame(Camera);
        Assert.That(actualFrame, Has.Length.EqualTo(expectedFrame.Length));
        Assert.That(actualFrame.Length, Is.GreaterThan(4));
        for (var index = 0; index < actualFrame.Length; index++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actualFrame[index].Transform.Translation, Is.EqualTo(expectedFrame[index].Transform.Translation));
                Assert.That(actualFrame[index].Colour, Is.EqualTo(expectedFrame[index].Colour));
                Assert.That(actualFrame[index].UV, Is.EqualTo(expectedFrame[index].UV));
                Assert.That(actualFrame[index].Time, Is.EqualTo(expectedFrame[index].Time));
                Assert.That(actualFrame[index].AppearancePhase, Is.EqualTo(expectedFrame[index].AppearancePhase));
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnrepresentableSignedGrowth_DisablesOnlyItsOwnSpriteLayer(bool complex)
    {
        var bytes = SignedGrowthSource(complex);
        var converted = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/signed-growth-range.xml", _ => [1]));
        Assert.That(converted.Emitters, Has.Count.EqualTo(2));
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.1f, Matrix.Identity);
        var frame = simulation.Frame(Camera);
        Assert.That(frame, Has.Length.EqualTo(1), "An unrepresentable angular state must not poison or silently freeze the other sprite layer.");
        Assert.That(frame.Single().Layer, Is.EqualTo(1));
        Assert.That(simulation.SpriteSpinRangeUnsupported, Is.True);
        Assert.That(simulation.ModelSpinRangeUnsupported, Is.False);
        Near(frame.Single().Transform, Oracle(.1f, 4, 1, 180, 17, 0, .8));
        simulation.Seek(0, Matrix.Identity);
        var restarted = simulation.Frame(Camera);
        Assert.That(restarted, Has.Length.EqualTo(2), "Rewinding before the overflow must restore the authored layers.");
        Assert.That(simulation.SpriteSpinRangeUnsupported, Is.False);
        foreach (var particle in restarted)
            Assert.That(particle.Transform.Right.LengthSquared(), Is.EqualTo(1).Within(Tolerance));
        simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Select(particle => particle.Layer), Is.EqualTo(new[] { 1 }));
        Assert.That(simulation.SpriteSpinRangeUnsupported, Is.True);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void SignedGrowthRangeDiagnostic_ReachesTheGpuAndClearsAfterRewind(bool complex)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            var texture = VfxModelVariantPixelTests.Dds(Color.White);
            preview.Reload(new(SignedGrowthSource(complex), "diagnostic/signed-growth-range.xml", _ => texture));
            var eye = new Vector3(0, 0, 6);
            var camera = Camera with { View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Projection = Matrix.CreateOrthographic(3, 3, .1f, 100), CameraPosition = eye };
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            bool HasRangeDiagnostic() => preview.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.RangeUnsupported");
            preview.Seek(.1f, Matrix.Identity);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(camera); device.SetRenderTarget(null);
            var pixels = new Vector4[128 * 128]; target.GetData(pixels);
            Assert.That(HasRangeDiagnostic(), Is.True);
            Assert.That(pixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100),
                "The finite sibling must reach native drawing while the unrepresentable layer is isolated.");
            preview.Seek(0, Matrix.Identity);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(camera); device.SetRenderTarget(null);
            Assert.That(HasRangeDiagnostic(), Is.False, "Runtime sprite range warnings must not outlive a rewind that clears the angular failure.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(-2f, -.1f)]
    [TestCase(-.01f, .8f)]
    [TestCase(-.25f, 5f)]
    [TestCase(.25f, -5f)]
    [TestCase(0f, 5.01f)]
    public void SignedOrHighDynamicDamping_RemainsExplicitlyUnsupported(float start, float end)
    {
        var converted = Convert(Source(dampingStart: start, dampingEnd: end));
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMin, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().DynamicSpinDamping, Is.False);
            Assert.That(converted.Emitters.Single().ComplexSpinDamping, Is.False);
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
        });
    }

    [TestCase("single-channel", false, -1f)]
    [TestCase("single-channel", true, -1f)]
    [TestCase("unequal-rows", false, -1f)]
    [TestCase("unequal-rows", true, -1f)]
    [TestCase("wrong-binding", false, -1f)]
    [TestCase("wrong-binding", true, -1f)]
    [TestCase("multiple-keys", false, -1f)]
    [TestCase("multiple-keys", true, -1f)]
    [TestCase("single-channel", false, 12f)]
    [TestCase("single-channel", true, 12f)]
    [TestCase("unequal-rows", false, 12f)]
    [TestCase("unequal-rows", true, 12f)]
    [TestCase("wrong-binding", false, 12f)]
    [TestCase("wrong-binding", true, 12f)]
    [TestCase("multiple-keys", false, 12f)]
    [TestCase("multiple-keys", true, 12f)]
    public void SignedOrHighConstantDamping_UnverifiedRawLayoutsRemainExplicitlyUnsupported(string layout, bool complex, float damping)
    {
        var document = VfxDocument.Read(Source(dampingStart: damping, dampingEnd: damping));
        if (complex)
        {
            foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
                SetVector(Emitter(document), name, new(80, 120, 180));
            foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
                SetVector(Emitter(document), name, new(15, -30, 17));
            Emitter(document).Add(Property("ParticleSpinDirectionX", "CCW"), Property("ParticleSpinDirectionY", "CCW"));
        }
        var value = damping.ToString(CultureInfo.InvariantCulture);
        var other = (damping < 0 ? damping - 1 : damping + 1).ToString(CultureInfo.InvariantCulture);
        var point = $"0,{value},linear,linear,0,0,0,0,1";
        var curve = layout switch
        {
            "single-channel" => Curve("x", damping),
            "unequal-rows" => $"x_min,0,0;{point}|x_max,0,0;0,{other},linear,linear,0,0,0,0,1:x,0;0,1",
            "wrong-binding" => $"x_min,0,0;{point}|x_max,0,0;{point}:x,0;0,0",
            "multiple-keys" => $"x_min,0,0;{point};1,{value},linear,linear,0,0,0,0,1|x_max,0,0;{point};1,{value},linear,linear,0,0,0,0,1:x,0;0,1",
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
        Emitter(document).Elements().Single(element => (string?)element.Attribute("name") == "ParticleSpinDeceleration").Value = curve;
        var bytes = document.Write(); var converted = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMin, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().DynamicSpinDamping, Is.False);
            Assert.That(converted.Emitters.Single().ComplexSpinDamping, Is.False);
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
        });
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("full-local")]
    [TestCase("relative")]
    [TestCase("birth-x")]
    [TestCase("spin-x")]
    [TestCase("events")]
    [TestCase("bezier")]
    public void UnsupportedDynamicCombinations_KeepTheirExplicitDiagnosticAndZeroSpin(string combination)
    {
        var document = VfxDocument.Read(Source()); var emitter = Emitter(document);
        if (combination == "full-local") Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION");
        if (combination == "relative") emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        if (combination == "birth-x")
            foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" }) SetVector(emitter, name, new(15, 0, 17));
        if (combination == "spin-x")
            foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" }) SetVector(emitter, name, new(20, 0, 180));
        if (combination == "events") emitter.Add(new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "child")));
        if (combination == "bezier") emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value =
            "x_min,8405056,0;0,0.2,linear,bezier_c,-0.2,0,0.2,0,1;1,1,sine,linear,-0.2,0,0.2,0,1:x,0;0,0";
        var converted = Convert(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMin, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InactiveOrUnknownSpin_PreservesTheExactExistingBirthMatrix(bool unknownDirection)
    {
        var source = VfxDocument.Read(Source(direction: unknownDirection ? "UNKNOWN" : "CCW"));
        if (!unknownDirection)
            foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" }) SetVector(Emitter(source), name, Vector3.Zero);
        var reference = VfxDocument.Read(source.Write());
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" }) SetVector(Emitter(reference), name, Vector3.Zero);
        Emitter(reference).Elements().Single(e => (string?)e.Attribute("name") == "ParticleSpinDeceleration").Value = Curve("x", 0);
        var actual = Simulation(source.Write()); var unchanged = Simulation(reference.Write());
        actual.Seek(.732f, Matrix.Identity); unchanged.Seek(.732f, Matrix.Identity);
        Assert.That(actual.Frame(Camera), Is.EqualTo(unchanged.Frame(Camera)),
            "A layer without valid spin must not enter quaternion stepping or change its birth geometry.");
    }

    [TestCase(0f)]
    [TestCase(.4f)]
    public void ExistingConstantSpinPath_RetainsItsExactContinuousConvention(float damping)
    {
        var simulation = Simulation(Source(dampingStart: damping, dampingEnd: damping)); simulation.Seek(.625f, Matrix.Identity);
        var spinTime = damping == 0 ? .625 : (1 - Math.Exp(-damping * .625)) / damping;
        var expected = Matrix.CreateRotationZ(MathHelper.ToRadians(17 + (float)(180 * spinTime)));
        Near(simulation.Frame(Camera).Single().Transform, expected);
    }

    // Native pure-Z q normalization advances angle by 2*atan(omegaAfter*sourceDt/2).
    private static Matrix Oracle(float previewTime, float sourceLife, float speed, double degreesPerSecond,
        double birthDegrees, double dampingStart, double dampingEnd)
    {
        var omega = degreesPerSecond * Math.PI / 180; var angle = birthDegrees * Math.PI / 180;
        var committed = 0.0;
        void Integrate(double target, double previewDelta)
        {
            var sourceDt = previewDelta * speed;
            var phase = Math.Clamp(target * speed / (sourceLife + .000001), 0, 1);
            var damping = dampingStart + (dampingEnd - dampingStart) * phase;
            omega *= Math.Exp(-damping * sourceDt);
            angle += 2 * Math.Atan(omega * sourceDt / 2);
        }
        while (committed + Tick <= previewTime + .000001)
        {
            committed += Tick;
            Integrate(committed, Tick);
        }
        if (previewTime > committed) Integrate(previewTime, previewTime - committed);
        return Matrix.CreateRotationZ((float)angle);
    }

    private static void Near(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(Tolerance));
        });
    }

    private static TotalWarVfxSimulation Simulation(byte[] bytes) => new(Convert(bytes));
    private static VfxConversion Convert(byte[] bytes)
    {
        var converted = TotalWarVfxConverter.Convert(new(bytes, "vfx/dynamic-spin.xml", _ => [1]));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1), string.Join(';', converted.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return converted;
    }
    private static XElement Emitter(VfxDocument doc) => doc.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
    private static void Set(XElement emitter, string name, string value) => emitter.Elements().Single(e => (string?)e.Attribute("name") == name).SetAttributeValue("value", value);
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name),
        new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static void SetVector(XElement emitter, string name, Vector3 value) => emitter.Elements().Single(e => (string?)e.Attribute("name") == name).ReplaceWith(VectorProperty(name, value));
    private static string Curve(string axes, double start, double? end = null) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{start.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" +
        (end.HasValue && end.Value != start ? $";1,{end.Value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" : ""))) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));

    private static string SpinDampingCurve(float start, float end)
    {
        if (start != end || start is >= 0 and <= 5) return Curve("x", start, end);
        var value = start.ToString(CultureInfo.InvariantCulture);
        return $"x_min,0,0;0,{value},linear,linear,0,0,0,0,1|x_max,0,0;0,{value},linear,linear,0,0,0,0,1:x,0;0,1";
    }

    private static byte[] SignedGrowthSource(bool complex)
    {
        var document = VfxDocument.Read(Source(dampingStart: -1000, dampingEnd: -1000));
        if (complex)
        {
            foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
                SetVector(Emitter(document), name, new(80, 120, 180));
            foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" })
                SetVector(Emitter(document), name, new(15, -30, 17));
            Emitter(document).Add(Property("ParticleSpinDirectionX", "CCW"), Property("ParticleSpinDirectionY", "CCW"));
        }
        var siblingSource = VfxDocument.Read(Source());
        var sibling = new XElement(Emitter(siblingSource)); sibling.SetAttributeValue("id", "finite-sibling");
        document.Root.Descendants("emitters").Single().Add(sibling);
        var behaviour = new XElement(siblingSource.Root.Descendants("behaviour_chain").Single());
        behaviour.SetAttributeValue("id", "finite-sibling");
        document.Root.Descendants("behaviour_chains").Single().Add(behaviour);
        return document.Write();
    }

    private static byte[] Source(string direction = "CCW", string space = "WORLD_SPACE", float life = 4,
        float dampingStart = 0, float dampingEnd = .8f)
    {
        var emitter = new XElement("emitter", new XAttribute("id", "dynamic-spin"), new XAttribute("type", "continuous_mesh"),
            Property("InfiniteLife", "true"), Property("MaximumParticles", 1), Property("ParticleSimulationSpace", space),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve("x", .1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve("x", life)),
            new XElement("property", new XAttribute("name", "ParticleScale"), Curve("xyz", 1)),
            VectorProperty("ParticleOrientationMin", new(0, 0, 17)), VectorProperty("ParticleOrientationMax", new(0, 0, 17)),
            VectorProperty("ParticleSpin_Min", new(0, 0, 180)), VectorProperty("ParticleSpin_Max", new(0, 0, 180)),
            Property("ParticleSpinDirectionZ", direction),
            new XElement("property", new XAttribute("name", "ParticleSpinDeceleration"), SpinDampingCurve(dampingStart, dampingEnd)));
        var quality = new XElement("quality", new XAttribute("value", "0"),
            new XElement("modifier", new XAttribute("fragment", "vs_translation"), Property("restriction", 3)),
            new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "vfx/test.dds"))));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "dynamic-spin"), quality))).ToString(SaveOptions.DisableFormatting));
    }
}
