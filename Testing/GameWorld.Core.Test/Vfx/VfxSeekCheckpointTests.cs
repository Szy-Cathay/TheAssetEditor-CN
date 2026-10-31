#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSeekCheckpointTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ReverseAndForward_KeepCompletePacketsIncludingNegativePrewarmAndEvents(bool model, bool events)
    {
        var conversion = Conversion(model, events);
        var cached = new TotalWarVfxSimulation(conversion);
        Assert.That(cached.EnableSeekCheckpoints(), Is.True);
        foreach (var time in new[] { 0f, .137f, .717f, 1.37f, 2.13f, 1.837f, 1.637f, .937f, .337f, 0f, 2.437f, .817f, 3.137f })
        {
            cached.Seek(time, Matrix.Identity, _ => Matrix.Identity);
            var fresh = new TotalWarVfxSimulation(conversion);
            fresh.Seek(time, Matrix.Identity, _ => Matrix.Identity);
            Assert.That(cached.ProjectedFrame(Camera, false), Is.EqualTo(fresh.ProjectedFrame(Camera, false)), $"At {time}s");
            Assert.That(cached.ParticleCount, Is.EqualTo(fresh.ParticleCount));
            Assert.That(cached.SeekCheckpointCount, Is.LessThanOrEqualTo(8));
            Assert.That(cached.SeekCheckpointStateBytes, Is.LessThanOrEqualTo(16 * 1024 * 1024));
            Assert.That(cached.SeekCheckpointRandomBytes, Is.LessThanOrEqualTo(16 * 1024 * 1024));
        }
        Assert.That(cached.SeekCheckpointRestoreCount, Is.GreaterThan(3));
    }

    [TestCase(1, 16777216)]
    [TestCase(16777216, 1)]
    public void ExceededBudget_DisablesCachingWithoutChangingSimulation(long stateBytes, long randomBytes)
    {
        var conversion = Conversion(model: false, events: true);
        var cached = new TotalWarVfxSimulation(conversion);
        cached.EnableSeekCheckpoints(stateBudgetBytes: stateBytes, randomBudgetBytes: randomBytes);
        foreach (var time in new[] { 1.37f, .317f, 1.937f, 0f, 2.817f })
        {
            cached.Seek(time, Matrix.Identity);
            var original = new TotalWarVfxSimulation(conversion); original.Seek(time, Matrix.Identity);
            Assert.That(cached.ProjectedFrame(Camera, false), Is.EqualTo(original.ProjectedFrame(Camera, false)));
        }
        Assert.That(cached.SeekCheckpointingEnabled, Is.False);
        Assert.That(cached.SeekCheckpointCount, Is.Zero);
        Assert.That(cached.SeekCheckpointRandomBytes, Is.Zero);
    }

    [Test]
    public void DisablingAfterRestore_PreservesTheOriginalFutureRandomStream()
    {
        var conversion = Conversion(model: true, events: true);
        var cached = new TotalWarVfxSimulation(conversion); cached.EnableSeekCheckpoints();
        cached.Seek(2.13f, Matrix.Identity); cached.Seek(.937f, Matrix.Identity);
        Assert.That(cached.SeekCheckpointRestoreCount, Is.EqualTo(1));
        cached.DisableSeekCheckpoints();
        foreach (var time in new[] { 1.317f, 2.937f, 3.317f })
        {
            cached.Seek(time, Matrix.Identity);
            var original = new TotalWarVfxSimulation(conversion); original.Seek(time, Matrix.Identity);
            Assert.That(cached.ProjectedFrame(Camera, false), Is.EqualTo(original.ProjectedFrame(Camera, false)));
        }
        Assert.That(cached.SeekCheckpointRandomBytes, Is.Zero);
    }

    [Test]
    public void ChangedAttachmentOrEmissionEnd_InvalidatesExistingCheckpoints()
    {
        var conversion = Conversion(model: false, events: false);
        var cached = new TotalWarVfxSimulation(conversion); cached.EnableSeekCheckpoints();
        cached.Seek(1.37f, Matrix.Identity);
        Assert.That(cached.HasSeekCheckpoint(.7f), Is.True);
        cached.Seek(1.37f, Matrix.CreateTranslation(2, 0, 0));
        Assert.That(cached.HasSeekCheckpoint(.7f), Is.False);
        cached.Seek(.617f, Matrix.CreateTranslation(2, 0, 0));
        var fresh = new TotalWarVfxSimulation(conversion); fresh.Seek(.617f, Matrix.CreateTranslation(2, 0, 0));
        Assert.That(cached.ProjectedFrame(Camera, false), Is.EqualTo(fresh.ProjectedFrame(Camera, false)));
        cached.EmissionEndTimeSeconds = .4f;
        Assert.That(cached.HasSeekCheckpoint(.3f), Is.False);
        cached.Seek(.317f, Matrix.Identity);
        fresh.EmissionEndTimeSeconds = .4f; fresh.Seek(.317f, Matrix.Identity);
        Assert.That(cached.ProjectedFrame(Camera, false), Is.EqualTo(fresh.ProjectedFrame(Camera, false)));
    }

    [Test]
    public void DefaultAndLateEnable_KeepTheUnrecordedSynchronousRoute()
    {
        var simulation = new TotalWarVfxSimulation(Conversion(model: false, events: false));
        Assert.That(simulation.SeekCheckpointingEnabled, Is.False);
        simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.EnableSeekCheckpoints(), Is.False);
        Assert.That(simulation.SeekCheckpointCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConstantScaledWorldForceFallback_KeepsEveryPacketAndItsDiagnosticAfterRestore(bool nonuniform)
    {
        // DIAG: calibrated constant force with an intentionally unsupported but constant scaled attachment.
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 12));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .6f));
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 1));
        Set(emitter, "ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", 5));
        Set(emitter, "ForceA", Curve(new(0, -200, 0), new(0, -200, 0)));
        Set(emitter, "ParticleSimulationSpace", "WORLD_SPACE", attribute: true);
        Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", attribute: true);
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)),
            new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -2), new XAttribute("max", -2)));
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/seek-force-fallback.xml", _ => [1]));
        Assert.That(conversion.Emitters.Single().ConstantForceDamping, Is.EqualTo(5));
        var attachment = nonuniform ? Matrix.CreateScale(2, 3, 2) : Matrix.CreateScale(2);
        var cached = new TotalWarVfxSimulation(conversion);
        Assert.That(cached.EnableSeekCheckpoints(), Is.True);
        foreach (var time in new[] { 0f, .517f, 1.317f, 2.137f, 1.837f, 1.637f, .937f, .337f, 0f, 2.437f, .817f })
        {
            cached.Seek(time, attachment, _ => attachment);
            var original = new TotalWarVfxSimulation(conversion); original.Seek(time, attachment, _ => attachment);
            Assert.That(cached.ProjectedFrame(Camera, false), Is.EqualTo(original.ProjectedFrame(Camera, false)), $"At {time}s");
            Assert.That(cached.ForceBasisUnsupported, Is.True);
            Assert.That(original.ForceBasisUnsupported, Is.True);
            Assert.That(cached.SeekCheckpointingEnabled, Is.True);
        }
        Assert.That(cached.SeekCheckpointRestoreCount, Is.GreaterThan(3));
    }

    [Test]
    public void MotionTrails_RejectCheckpointingAndKeepTheirAttachmentHistoryRoute()
    {
        var source = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        Set(source.Root.Descendants("emitter").Single(), "SpawnSpacing", ".1", attribute: true);
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "diagnostic/seek-motion-trail.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        Assert.That(simulation.HasMotionTrailEmitters, Is.True);
        Assert.That(simulation.EnableSeekCheckpoints(), Is.False);
        Matrix At(float time) => Matrix.CreateTranslation(time, 0, 0);
        simulation.SeekContinuousTrail(.317f, At(.317f), At);
        Assert.That(simulation.SeekCheckpointCount, Is.Zero);
    }

    [Test]
    public void TerrainEvents_ChangedGroundRetainsTheOriginalResetAndCompletePackets()
    {
        var conversion = Conversion(model: true, events: true, terrain: true);
        Assert.That(conversion.Emitters.Any(emitter => emitter.RequiresFlatTerrain && emitter.Events.Count > 0), Is.True);
        var cached = new TotalWarVfxSimulation(conversion); cached.EnableSeekCheckpoints();
        var original = new TotalWarVfxSimulation(conversion);
        foreach (var (time, height) in new[] { (1.37f, 0f), (1.817f, 0f), (.717f, 0f), (.937f, 2f), (.317f, 2f), (1.637f, -1f) })
        {
            cached.Seek(time, Matrix.Identity); original.Seek(time, Matrix.Identity);
            var camera = Camera with { VfxGroundHeight = height };
            Assert.That(cached.ProjectedFrame(camera, false), Is.EqualTo(original.ProjectedFrame(camera, false)), $"At {time}s, ground={height}");
        }
        Assert.That(cached.SeekCheckpointRestoreCount, Is.GreaterThan(0));
    }

    // Synthetic diagnostic controls exercise state ownership; they are not official visual acceptance samples.
    private static VfxConversion Conversion(bool model, bool events, bool terrain = false)
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var childEmitter = child.Root.Descendants("emitter").Single();
        Set(childEmitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .4f));
        Set(childEmitter, "InfiniteLife", "false", attribute: true);
        var document = VfxDocument.Read(model ? VfxModelVariantTests.Source(2) : VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        if (events && !model) emitter.SetAttributeValue("type", "continuous_rigid");
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 12));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .6f));
        Set(emitter, "MaximumParticles", "64", attribute: true);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -2), new XAttribute("max", -2)));
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", terrain ? 0 : 2));
        Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION", attribute: true);
        if (model && !terrain)
        {
            Set(emitter, "EmitterOrientation", Curve(new(0, 0, 0), new(70, -35, 110)));
            Set(emitter, "EmitterPosition", Curve(new(0, 0, 0), new(2, 1, -1)));
        }
        if (terrain)
        {
            Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL", attribute: true);
            Set(emitter, "ParticleRelativeSpawnDirection", "RELATIVE_TO_TERRAIN_NORMAL", attribute: true);
            Set(emitter, "ParticlesSpawnOnTerrain", "true", attribute: true);
        }
        if (events)
        {
            Set(emitter, "EventEnable", "true", attribute: true);
            foreach (var name in new[] { "OnParticleSpawn", "OnParticleDeath" })
                emitter.Add(new XElement("property_event", new XAttribute("name", name),
                    new XAttribute("vfx_name", "child"), new XAttribute("track_position", "true"),
                    new XAttribute("track_scale", "true"), new XAttribute("track_colour", "true"),
                    new XAttribute("track_alpha", "true"), new XAttribute("track_lifetime", "false"),
                    new XAttribute("spawn_event_time_offset", ".1"), new XAttribute("track_orientation_mode", "orientation")));
        }
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/seek-checkpoints.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters.Count(emitter => !emitter.IsEventTemplate), Is.EqualTo(1));
        Assert.That(conversion.Emitters.Any(emitter => emitter.IsMotionTrail), Is.False);
        Assert.That(conversion.Emitters.Any(emitter => emitter.Events.Count > 0), Is.EqualTo(events));
        return conversion;
    }

    private static void Set(XElement emitter, string name, string value, bool attribute = false)
    {
        emitter.Elements().Where(element => (string?)element.Attribute("name") == name).Remove();
        var element = new XElement("property", new XAttribute("name", name));
        if (attribute) element.SetAttributeValue("value", value); else element.Value = value;
        emitter.Add(element);
    }

    private static string Curve(Vector3 first, Vector3 last)
    {
        var starts = new[] { first.X, first.Y, first.Z }; var ends = new[] { last.X, last.Y, last.Z };
        var names = new[] { "x", "y", "z" };
        return string.Join('|', names.Select((name, index) => FormattableString.Invariant(
            $"{name}_min,0,1;0,{starts[index]},linear,linear,0,0,0,0,0;1,{ends[index]},linear,linear,0,0,0,0,0")))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
    }
}
