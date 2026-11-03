#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxModelConstantForceTests
{
    private const string ForceEventChildName = "model-force-event-child";

    public static IEnumerable<TestCaseData> ForceEventCases()
    {
        foreach (var space in new[] { "WORLD_SPACE", "LOCAL_SPACE_POSITION_ONLY" })
            foreach (var motion in new[] { (.5f, 0f), (1.4f, 2f), (2f, 10f) })
                foreach (var kind in new[] { "OnParticleSpawn", "OnParticleDeath" })
                    foreach (var mode in new[] { "enabled", "disabled", "empty-path" })
                        yield return new TestCaseData(space, motion.Item1, motion.Item2, kind, mode);
    }

    [TestCaseSource(nameof(ForceEventCases))]
    public void ChildEventSettings_DoNotDiscardTheSupportedRigidForce(string space, float speed, float damping, string kind, string mode)
    {
        var document = VfxDocument.Read(Source(space, damping)); AddForceEvent(document, kind, mode);
        var bytes = document.Write(); var conversion = ForceEventConversion(bytes, speed);
        var definition = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(definition.ModelConstantForce, Is.True);
            Assert.That(definition.Events, Has.Count.EqualTo(mode == "enabled" ? 1 : 0));
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
        var root = Root(.8f); var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, root); var birth = Snapshot(Particles(simulation).Single());
        simulation.Seek(.4f, root); var actual = Snapshot(Particles(simulation).Single());
        var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
        var expected = NativeOracle(birth, ticks, damping);
        Near(actual.WorldPosition, expected.Position); Near(actual.WorldVelocity, expected.Velocity);
        var first = Packet(simulation);
        simulation.Seek(.1f, root); simulation.Seek(.4f, root);
        Assert.That(Packet(simulation), Is.EqualTo(first));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("WORLD_SPACE", .5f)]
    [TestCase("WORLD_SPACE", 1.4f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", .5f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", 1.4f)]
    public void PositionTrackingChild_FollowsTheIndependentlyIntegratedParentForce(string space, float speed)
    {
        var document = VfxDocument.Read(Source(space, 2)); AddForceEvent(document, "OnParticleSpawn", "enabled");
        var conversion = ForceEventConversion(document.Write(), speed, visibleChild: true);
        var definition = conversion.Emitters.Single(item => !item.IsEventTemplate);
        var child = conversion.Emitters.Single(item => item.IsEventTemplate);
        var childLayer = conversion.Emitters.IndexOf(child);
        Assert.That(definition.ModelConstantForce, Is.True);
        var root = Root(.8f); var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, root);
        var parentParticle = Particles(simulation).Single(item => !Definition(item).IsEventTemplate);
        var birth = Snapshot(parentParticle);
        foreach (var time in new[] { .2f, .4f })
        {
            simulation.Seek(time, root);
            var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
            var expected = NativeOracle(birth, ticks, 2);
            var actual = Snapshot(Particles(simulation).Single(item => !Definition(item).IsEventTemplate));
            Near(actual.WorldPosition, expected.Position); Near(actual.WorldVelocity, expected.Velocity);
            var packet = simulation.Frame(Camera()).Single(item => item.Layer == childLayer);
            Near(packet.Transform.Translation, expected.Position);
            Assert.That(simulation.ForceBasisUnsupported, Is.False);
        }
    }

    [TestCase("ParticlesFollowTerrain")]
    [TestCase("EnableCollisions")]
    [TestCase("EffectedByWindFactor")]
    [TestCase("WindVarianceFactor")]
    [TestCase("AllowEmitFromMesh")]
    [TestCase("negative-linear-damping")]
    public void RigidForceEvents_KeepTheOtherPhysicalCombinationGuards(string field)
    {
        var document = VfxDocument.Read(Source("WORLD_SPACE", 2)); AddForceEvent(document, "OnParticleSpawn", "enabled");
        var emitter = document.Root.Descendants("emitter").Single();
        if (field == "negative-linear-damping")
            emitter.Elements().Single(item => (string?)item.Attribute("name") == "ParticleDeceleration").Value = VfxLibraryVisibilityTests.Constant("x", -1);
        else emitter.Add(new XElement("property", new XAttribute("name", field), new XAttribute("value", 1)));
        var conversion = ForceEventConversion(document.Write(), 1);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.False);
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
    }

    [TestCase("WORLD_SPACE", false)]
    [TestCase("WORLD_SPACE", true)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", true)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void ForceEventOwner_ReachesTheGpuAtTheIndependentStaticPosition(string space, bool enabled)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        const float time = .4f, speed = 1.4f, damping = 2;
        var document = VfxDocument.Read(Source(space, damping));
        AddForceEvent(document, "OnParticleSpawn", enabled ? "enabled" : "disabled");
        var source = document.Write(); var conversion = ForceEventConversion(source, speed); var root = Root(.8f);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, root);
        var birth = Snapshot(Particles(simulation).Single()); simulation.Seek(time, root);
        var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
        var expected = NativeOracle(birth, ticks, damping).Position;
        Near(simulation.Frame(Camera()).Single().Transform.Translation, expected);
        var reference = VfxDocument.Read(source); var emitter = reference.Root.Descendants("emitter").Single();
        foreach (var field in new[] { "ParticleInitialSpeed", "ParticleDeceleration" })
            emitter.Elements().Single(item => (string?)item.Attribute("name") == field).Value = VfxLibraryVisibilityTests.Constant("x", 0);
        emitter.Elements().Single(item => (string?)item.Attribute("name") == "ForceA").Value =
            string.Join('|', new[] { "x", "y", "z" }.Select(axis => VfxLibraryVisibilityTests.Constant(axis, 0).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var staticRoot = root; staticRoot.Translation += expected - birth.WorldPosition;
        var texture = VfxModelVariantPixelTests.Dds(new Color(70, 180, 230, 155));
        var emptyChild = VfxDocument.CreateComposition().Write();
        byte[]? Resolve(string path) => path.Replace('/', '\\') == @"vfx\model-force-event-child.xml" ? emptyChild
            : path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? texture : VfxModelVariantTests.Resolve(path);
        var targetPoint = expected + Vector3.TransformNormal(new Vector3(.3f, .3f, 0), root);
        var eye = targetPoint + new Vector3(0, 0, 8);
        var camera = Camera() with { View = Matrix.CreateLookAt(eye, targetPoint, Vector3.Up),
            Projection = Matrix.CreateOrthographic(3, 3, .1f, 100), CameraPosition = eye };
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            Vector4[] Render(byte[] leaf, Matrix attachment)
            {
                var composition = Wrap(leaf, speed, "leaf");
                using var preview = new EffekseerVfxPreview(device);
                preview.Reload(new(composition, "diagnostic/model-force-event-pixels.xml", path => path.Replace('/', '\\') == @"vfx\leaf.xml" ? leaf : Resolve(path)));
                Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(item => item.Field + ":" + item.ReasonKey)));
                preview.Seek(time, attachment);
                using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
            }
            var actual = Render(source, root); var control = Render(reference.Write(), staticRoot);
            Assert.That(control.Count(pixel => pixel.X + pixel.Y + pixel.Z > .05f), Is.GreaterThan(100));
            Assert.That(actual.Zip(control).Max(pair => Vector4.Distance(pair.First, pair.Second)), Is.LessThanOrEqualTo(.00001f),
                "Native upload must match the static pose placed by the independent rigid ForceA equations, with event enable preserved.");
        }
        finally { graphics.Release(); }
    }

    private static void AddForceEvent(VfxDocument document, string kind, string mode)
    {
        document.Root.Descendants("emitter").Single().Add(
            new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", mode == "disabled" ? "false" : "true")),
            new XElement("property_event", new XAttribute("name", kind),
                new XAttribute("vfx_name", mode == "empty-path" ? "" : ForceEventChildName), new XAttribute("track_position", "true"),
                new XAttribute("track_scale", "false"), new XAttribute("track_colour", "false"), new XAttribute("track_alpha", "false"),
                new XAttribute("track_lifetime", "false"), new XAttribute("track_orientation_mode", "orientation"),
                new XAttribute("spawn_event_time_offset", 0)));
    }

    private static VfxConversion ForceEventConversion(byte[] source, float speed, bool visibleChild = false)
    {
        var child = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var emitter = child.Root.Descendants("emitter").Single();
        emitter.Elements().Single(item => (string?)item.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 5);
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", "LOCAL_SPACE_POSITION_ONLY")));
        var childBytes = visibleChild ? child.Write() : VfxDocument.CreateComposition().Write();
        var result = TotalWarVfxConverter.Convert(new(Wrap(source, speed, "leaf"), "diagnostic/model-force-events.xml", path => path.Replace('/', '\\') switch
        {
            @"vfx\leaf.xml" => source, @"vfx\model-force-event-child.xml" => childBytes,
            _ => VfxModelVariantTests.Resolve(path),
        }));
        Assert.That(result.Emitters.Count(item => !item.IsEventTemplate), Is.EqualTo(1), string.Join(';', result.Diagnostics.Select(item => item.Field + ":" + item.ReasonKey)));
        Assert.That(result.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
        return result;
    }
}
