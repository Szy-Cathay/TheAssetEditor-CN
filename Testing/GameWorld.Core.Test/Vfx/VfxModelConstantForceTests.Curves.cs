#nullable enable
using System.Globalization;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxModelConstantForceTests
{
    public static IEnumerable<TestCaseData> RigidForceCurveCases()
    {
        foreach (var space in new[] { "WORLD_SPACE", "LOCAL_SPACE_POSITION_ONLY" })
            foreach (var speed in new[] { .5f, 1f, 2f })
                foreach (var dynamic in new[] { false, true }) yield return new(space, speed, dynamic);
    }

    [TestCaseSource(nameof(RigidForceCurveCases))]
    public void RigidForceTables_PreserveRandomBoundsSourcePhaseAndTrackedChild(string space, float speed, bool dynamic)
    {
        var source = RigidCurveSource(space, dynamic, enabled: true);
        var conversion = ForceEventConversion(source, speed, visibleChild: true);
        var definition = conversion.Emitters.Single(item => !item.IsEventTemplate);
        Assert.That(definition.ModelConstantForce, Is.False);
        Assert.That(definition.ModelForceCurve, Is.Not.Null);
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        var root = Root(.8f); var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, root);
        var particle = Particles(simulation).Single(item => !Definition(item).IsEventTemplate);
        var birth = Snapshot(particle); var life = (float)Field(particle, "Life");
        var childLayer = conversion.Emitters.IndexOf(conversion.Emitters.Single(item => item.IsEventTemplate));
        foreach (var time in new[] { .2f, .4f })
        {
            simulation.Seek(time, root);
            var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
            var expected = RigidCurveOracle(birth, life, ticks, dynamic);
            var actual = Snapshot(particle);
            Near(actual.WorldPosition, expected.Position); Near(actual.WorldVelocity, expected.Velocity);
            Near(simulation.Frame(Camera()).Single(item => item.Layer == childLayer).Transform.Translation, expected.Position);
            Assert.That(simulation.ForceBasisUnsupported, Is.False);
        }
        var first = Packet(simulation); simulation.Seek(.1f, root); simulation.Seek(.4f, root);
        Assert.That(Packet(simulation), Is.EqualTo(first));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void RigidForceTableGpu_MatchesAnIndependentStaticPlacement(bool dynamic)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        const float time = .4f, speed = 1.4f;
        var source = RigidCurveSource("WORLD_SPACE", dynamic, enabled: true);
        var conversion = ForceEventConversion(source, speed); var root = Root(.8f);
        Assert.That(conversion.Emitters.Single().ModelForceCurve, Is.Not.Null);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, root);
        var particle = Particles(simulation).Single(); var birth = Snapshot(particle);
        var expected = RigidCurveOracle(birth, (float)Field(particle, "Life"), 48, dynamic).Position;
        var reference = VfxDocument.Read(source); var emitter = reference.Root.Descendants("emitter").Single();
        foreach (var field in new[] { "ParticleInitialSpeed", "ParticleDeceleration" })
            emitter.Elements().Single(item => (string?)item.Attribute("name") == field).Value = VfxLibraryVisibilityTests.Constant("x", 0);
        emitter.Elements().Single(item => (string?)item.Attribute("name") == "ForceA").Value = VfxParticleEventMotionTests.VectorCurve(Vector3.Zero);
        var staticRoot = root; staticRoot.Translation += expected - birth.WorldPosition;
        var texture = VfxModelVariantPixelTests.Dds(new Color(70, 180, 230, 155));
        var empty = VfxDocument.CreateComposition().Write();
        var targetPoint = expected + Vector3.TransformNormal(new(.3f, .3f, 0), root);
        var eye = targetPoint + new Vector3(0, 0, 8);
        var camera = Camera() with { View = Matrix.CreateLookAt(eye, targetPoint, Vector3.Up),
            Projection = Matrix.CreateOrthographic(3, 3, .1f, 100), CameraPosition = eye };
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            Vector4[] Render(byte[] bytes, Matrix attachment)
            {
                using var preview = new EffekseerVfxPreview(device);
                preview.Reload(new(Wrap(bytes, speed, "leaf"), "DIAG/rigid-force-table-pixels.xml", path => path.Replace('/', '\\') switch
                {
                    @"vfx\leaf.xml" => bytes, @"vfx\model-force-event-child.xml" => empty,
                    _ => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? texture : VfxModelVariantTests.Resolve(path),
                }));
                Assert.That(preview.EmitterCount, Is.EqualTo(1)); preview.Seek(time, attachment);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
            }
            var actual = Render(source, root); var control = Render(reference.Write(), staticRoot);
            Assert.That(control.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
            Assert.That(actual.Zip(control).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThanOrEqualTo(.00001f));
        }
        finally { graphics.Release(); }
    }

    private static byte[] RigidCurveSource(string space, bool dynamic, bool enabled)
    {
        var document = VfxDocument.Read(Source(space, 2)); AddForceEvent(document, "OnParticleSpawn", enabled ? "enabled" : "disabled");
        var emitter = document.Root.Descendants("emitter").Single();
        var axes = new[] { "x", "y", "z" }; var channels = new List<string>();
        for (var axis = 0; axis < 3; axis++)
            foreach (var upper in new[] { false, true })
            {
                var start = RigidCurveEndpoint(axis, upper, false);
                var end = RigidCurveEndpoint(axis, upper, dynamic);
                channels.Add($"{axes[axis]}_{(upper ? "max" : "min")},0,0;0,{start.ToString("R", CultureInfo.InvariantCulture)},linear,linear,-0.05,0,0.05,0,1"
                    + (dynamic ? $";1,{end.ToString("R", CultureInfo.InvariantCulture)},linear,linear,-0.05,0,0.05,0,1" : ""));
            }
        emitter.Elements().Single(item => (string?)item.Attribute("name") == "ForceA").Value = string.Join('|', channels) + ":x,1;0,1|y,1;2,3|z,1;4,5";
        return document.Write();
    }

    private static float RigidCurveEndpoint(int axis, bool upper, bool end) => (axis, upper, end) switch
    {
        (0, false, false) => -120, (0, true, false) => 80, (0, false, true) => 220, (0, true, true) => 430,
        (1, false, false) => -900, (1, true, false) => -500, (1, false, true) => -100, (1, true, true) => 100,
        (2, false, false) => 40, (2, true, false) => 90, (2, false, true) => -130, _ => 170,
    };

    private static (Vector3 Position, Vector3 Velocity) RigidCurveOracle(Sample birth, float life, int ticks, bool dynamic)
    {
        var position = birth.WorldPosition; var velocity = birth.WorldVelocity / birth.Speed;
        var dt = (float)Step * birth.Speed;
        for (var tick = 1; tick <= ticks; tick++)
        {
            // Independent reconstruction of the native table coordinates and float min/max filtering.
            var phase = Math.Clamp((float)(tick * Step) * birth.Speed / (life * birth.Speed + .000001), 0, 1);
            float Axis(int axis)
            {
                float Bound(bool upper, int index) => (RigidCurveEndpoint(axis, upper, false)
                    + (RigidCurveEndpoint(axis, upper, dynamic) - RigidCurveEndpoint(axis, upper, false)) * (index * (1f / 127))) * .01f;
                var coordinate = (float)phase * (dynamic ? 127 : 0);
                var lower = (int)coordinate; var upper = Math.Min(lower + 1, 127); var blend = coordinate - lower;
                var minimum = Bound(false, lower) + (Bound(false, upper) - Bound(false, lower)) * blend;
                var maximum = Bound(true, lower) + (Bound(true, upper) - Bound(true, lower)) * blend;
                return minimum + (maximum - minimum) * birth.RandomAlpha;
            }
            var acceleration = new Vector3(Axis(0), Axis(1), Axis(2)) * birth.Scale;
            velocity *= MathF.Exp(-2 * dt);
            position += velocity * dt + acceleration * ((dt * .5f) * dt);
            velocity += acceleration * dt;
        }
        return (position, velocity * birth.Speed);
    }
}
