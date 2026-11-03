using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public partial class VfxSpriteWorldForceBasisTests
{
    private static readonly Vector3 AuthoredForce = new(140, -981, 75);
    private const float Drag = .35f;
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 20), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(40, 40, .1f, 100), new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0, 1f, 0)]
    [TestCase(0, 2f, 0)]
    [TestCase(0, 1f, 1)]
    [TestCase(0, 2f, 1)]
    [TestCase(0, 1f, 2)]
    [TestCase(0, 2f, 2)]
    [TestCase(2, 1f, 0)]
    [TestCase(2, 2f, 0)]
    [TestCase(2, 1f, 1)]
    [TestCase(2, 2f, 1)]
    [TestCase(2, 1f, 2)]
    [TestCase(2, 2f, 2)]
    public void WorldAxisForceIsIndependentOfBirthRotationAndRetainsNativeScale(int space, float scale, int rotation)
    {
        var snapshot = Snapshot();
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        var emitter = conversion.Emitters.Single();
        Assert.That(emitter.ParticleForceCurve, Is.True);
        Assert.That(emitter.ConstantForceDamping, Is.Null);
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(emitter, space);
        var root = Matrix.CreateScale(scale) * Rotation(rotation) * Matrix.CreateTranslation(2, 4, -1);
        var simulation = new TotalWarVfxSimulation(conversion);
        foreach (var ticks in new[] { 30, 60, 120 })
        {
            simulation.Seek(ticks / 120f, root);
            var expected = root.Translation + WorldDisplacement(ticks) * scale;
            AssertPosition(simulation.Frame(Camera).Single().Transform.Translation, expected);
            Assert.That(simulation.ForceBasisUnsupported, Is.False);
        }
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void LaterEmitterRotationDoesNotRotateEarlierWorldAxisAcceleration(int space)
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot());
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(conversion.Emitters.Single(), space);
        Matrix At(float time) => Matrix.CreateScale(2) * Matrix.CreateFromYawPitchRoll(.4f + time, -.8f + .3f * time, .6f - .5f * time)
            * Matrix.CreateTranslation(2 + 3 * time, 4 - 2 * time, -1 + .5f * time);
        var direct = new TotalWarVfxSimulation(conversion);
        direct.Seek(1, At(1), At);
        var expected = (space == 0 ? At(1) : At(0)).Translation + WorldDisplacement(120) * 2;
        AssertPosition(direct.Frame(Camera).Single().Transform.Translation, expected);
        Assert.That(direct.ForceBasisUnsupported, Is.False);
        var incremental = new TotalWarVfxSimulation(conversion);
        for (var ticks = 1; ticks <= 120; ticks++) incremental.Seek(ticks / 120f, At(ticks / 120f), At);
        AssertPosition(incremental.Frame(Camera).Single().Transform.Translation, expected);
        direct.Seek(.25f, At(.25f), At);
        direct.Seek(1, At(1), At);
        AssertPosition(direct.Frame(Camera).Single().Transform.Translation, expected);
    }

    [TestCase(0)]
    [TestCase(2)]
    public void CalibratedVerticalForceAlsoRemainsWorldDownUnderAPitchedAttachment(int space)
    {
        var force = new Vector3(0, -981, 0);
        var conversion = TotalWarVfxConverter.Convert(Snapshot(force, 0));
        var emitter = conversion.Emitters.Single();
        Assert.That(emitter.ParticleForceCurve, Is.False);
        Assert.That(emitter.CalibratedVerticalAcceleration, Is.EqualTo(-981 * .01f));
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(emitter, space);
        var root = Matrix.CreateScale(.8f) * Matrix.CreateRotationX(MathHelper.PiOver2);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.5f, root);
        AssertPosition(simulation.Frame(Camera).Single().Transform.Translation, WorldDisplacement(60, force, 0) * .8f);
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
    }

    [TestCase("nonuniform")]
    [TestCase("mirror")]
    [TestCase("shear")]
    [TestCase("changing-scale")]
    public void UnsupportedAffineForceBasesKeepTheirDiagnostic(string kind)
    {
        var root = Matrix.CreateRotationX(MathHelper.PiOver2);
        if (kind == "nonuniform") root = Matrix.CreateScale(1, 2, 1) * root;
        if (kind == "mirror") root = Matrix.CreateScale(-1, 1, 1) * root;
        if (kind == "shear") root.M21 += .25f;
        Matrix At(float time) => kind == "changing-scale" ? Matrix.CreateScale(1 + time) * root : root;
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(Snapshot()));
        simulation.Seek(.5f, At(.5f), At);
        Assert.That(simulation.ForceBasisUnsupported, Is.True,
            "The world-axis correction does not silently admit an unverified affine or changing-scale path.");
    }

    [TestCase(1f)]
    [TestCase(2f)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void RotatedForcePositionReachesOffscreenPixelsInWorldAxes(float speed)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit offscreen rendering opt-in is required.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var snapshot = speed == 1 ? Snapshot() : ReferenceSnapshot(Snapshot(), speed, "leaf");
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(snapshot);
            var root = Matrix.CreateScale(2) * Matrix.CreateRotationX(MathHelper.PiOver2) * Matrix.CreateTranslation(2, 4, -1);
            foreach (var ticks in speed == 1 ? new[] { 30, 60, 120 } : new[] { 15, 30, 60 })
            {
                preview.Seek(ticks / 120f, root);
                using var target = new RenderTarget2D(graphics.GraphicsDevice, 400, 400, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                graphics.GraphicsDevice.SetRenderTarget(target);
                graphics.GraphicsDevice.Clear(Color.Black);
                preview.Draw(Camera);
                graphics.GraphicsDevice.SetRenderTarget(null);
                var pixels = new Vector4[400 * 400];
                target.GetData(pixels);
                var visible = Enumerable.Range(0, pixels.Length).Where(i => pixels[i].X > .8f).ToArray();
                Assert.That(visible, Has.Length.GreaterThan(10));
                var expected = root.Translation + SourceDisplacement(ticks, speed) * 2;
                Assert.That(visible.Average(i => i % 400), Is.EqualTo(199.5 + expected.X * 10).Within(1));
                Assert.That(visible.Average(i => i / 400), Is.EqualTo(199.5 - expected.Y * 10).Within(1));
            }
        }
        finally { graphics.Release(); }
    }

    private static Matrix Rotation(int index) => index switch
    {
        0 => Matrix.CreateRotationX(MathHelper.PiOver2),
        1 => Matrix.CreateRotationZ(-MathHelper.PiOver2),
        _ => Matrix.CreateFromYawPitchRoll(.7f, -1.1f, .5f),
    };

    private static Vector3 WorldDisplacement(int ticks, Vector3? force = null, float drag = Drag)
    {
        var position = Vector3.Zero;
        var velocity = Vector3.Zero;
        const float delta = 1f / 120;
        for (var tick = 0; tick < ticks; tick++)
        {
            velocity += (force ?? AuthoredForce) * .01f * delta;
            velocity *= MathF.Exp(-drag * delta);
            position += velocity * delta;
        }
        return position;
    }

    private static void AssertPosition(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(.0001f));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.0001f));
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(.0001f));
    }

    private static VfxPreviewSnapshot Snapshot(Vector3? force = null, float drag = Drag)
    {
        string Scalar(float value) => VfxLibraryVisibilityTests.Constant("x", value);
        var acceleration = force ?? AuthoredForce;
        var forceCurve = string.Join('|', new[] { ("x", acceleration.X), ("y", acceleration.Y), ("z", acceleration.Z) }
            .Select(axis => VfxLibraryVisibilityTests.Constant(axis.Item1, axis.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var source = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="diagnostic-world-force" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{Scalar(.2f)}</property>
            <property name="ParticleLifeTime">{Scalar(4)}</property><property name="ParticleInitialSpeed">{Scalar(0)}</property>
            <property name="ForceA">{forceCurve}</property><property name="ParticleDeceleration">{Scalar(drag)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="diagnostic-world-force"><quality value="0">
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/diagnostic-white.tga"/></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(source, "vfx/diagnostic-world-force.xml", _ => texture);
    }
}
