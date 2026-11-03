using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxSpriteWorldForceBasisTests
{
    [TestCase(0, .5f)]
    [TestCase(0, 1.4f)]
    [TestCase(0, 2f)]
    [TestCase(0, 5f)]
    [TestCase(2, .5f)]
    [TestCase(2, 1.4f)]
    [TestCase(2, 2f)]
    [TestCase(2, 5f)]
    public void PositiveReferenceSpeedUsesNativeSourceTimeForForceDampingAndPosition(int space, float speed)
    {
        var leaf = Snapshot();
        var snapshot = ReferenceSnapshot(leaf, speed, "leaf");
        var original = snapshot.GameBytes.ToArray();
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        var emitter = conversion.Emitters.Single();
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(emitter, space);
        Assert.That(emitter.ParticleForceCurve, Is.True);
        foreach (var rotation in new[] { Matrix.Identity, Matrix.CreateFromYawPitchRoll(.7f, -.4f, .5f) })
        {
            var root = Matrix.CreateScale(.8f) * rotation * Matrix.CreateTranslation(2, 4, -1);
            var simulation = new TotalWarVfxSimulation(conversion);
            foreach (var ticks in new[] { 15, 30, 60 })
            {
                simulation.Seek(ticks / 120f, root);
                AssertPosition(simulation.Frame(Camera).Single().Transform.Translation,
                    root.Translation + SourceDisplacement(ticks, speed) * .8f);
                Assert.That(simulation.ForceBasisUnsupported, Is.False);
            }
        }
        Assert.That(snapshot.GameBytes, Is.EqualTo(original));
        Assert.That(VfxDocument.Read(leaf.GameBytes).Write(), Is.EqualTo(leaf.GameBytes));
    }

    [TestCase(0, .5f)]
    [TestCase(0, 2f)]
    [TestCase(2, .5f)]
    [TestCase(2, 2f)]
    public void PositiveReferenceSpeedAlsoScalesCalibratedVerticalForceBySourceSeconds(int space, float speed)
    {
        var force = new Vector3(0, -981, 0);
        var conversion = TotalWarVfxConverter.Convert(ReferenceSnapshot(Snapshot(force, 0), speed, "leaf"));
        var emitter = conversion.Emitters.Single();
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(emitter, space);
        Assert.That(emitter.ParticleForceCurve, Is.False);
        Assert.That(emitter.ConstantForceDamping, Is.Null);
        var root = Matrix.CreateScale(1.2f) * Matrix.CreateRotationX(MathHelper.PiOver2);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.5f, root);
        AssertPosition(simulation.Frame(Camera).Single().Transform.Translation, SourceDisplacement(60, speed, force, 0) * 1.2f);
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
    }

    [TestCase(.5f, 2f)]
    [TestCase(2f, .7f)]
    [TestCase(2f, 2.5f)]
    public void NestedReferenceSpeedsReachTheOrdinaryForceClockExactlyOnce(float inner, float outer)
    {
        var snapshot = ReferenceSnapshot(ReferenceSnapshot(Snapshot(), inner, "leaf"), outer, "inner");
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
        var root = Matrix.CreateScale(.8f) * Matrix.CreateFromYawPitchRoll(.2f, -.8f, .3f);
        simulation.Seek(.5f, root);
        AssertPosition(simulation.Frame(Camera).Single().Transform.Translation, SourceDisplacement(60, inner * outer) * .8f);
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void SourceTimeForceKeepsDirectIncrementalAndCheckpointReplayEquivalent(float speed)
    {
        var conversion = TotalWarVfxConverter.Convert(ReferenceSnapshot(Snapshot(), speed, "leaf"));
        var root = Matrix.CreateRotationZ(.7f);
        var direct = new TotalWarVfxSimulation(conversion);
        Assert.That(direct.EnableSeekCheckpoints(), Is.True);
        direct.Seek(.5f, root);
        var incremental = new TotalWarVfxSimulation(conversion);
        for (var tick = 1; tick <= 60; tick++) incremental.Seek(tick / 120f, root);
        Assert.That(ForcePacket(direct), Is.EqualTo(ForcePacket(incremental)));
        foreach (var time in new[] { .25f, .4f, .1f, .375f })
        {
            direct.Seek(time, root);
            var fresh = new TotalWarVfxSimulation(conversion);
            fresh.Seek(time, root);
            Assert.That(ForcePacket(direct), Is.EqualTo(ForcePacket(fresh)));
            Assert.That(direct.ForceBasisUnsupported, Is.False);
        }
    }

    [Test]
    public void UnitReferenceSpeedPreservesTheOriginalCompleteOrdinaryForcePackets()
    {
        var leaf = Snapshot();
        var direct = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(leaf));
        var referenced = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(ReferenceSnapshot(leaf, 1, "leaf")));
        foreach (var time in new[] { 0, .1f, .5f, .837f, .2f })
        {
            direct.Seek(time, Matrix.Identity);
            referenced.Seek(time, Matrix.Identity);
            Assert.That(ForcePacket(referenced), Is.EqualTo(ForcePacket(direct)));
        }
    }

    [Test]
    public void SourceTimeCorrectionDoesNotAdmitTheSeparateOneSixtiethConstantForceCandidate()
    {
        var snapshot = Snapshot(new(0, -50, 0), 1);
        var source = VfxDocument.Read(snapshot.GameBytes);
        var emitter = source.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)),
            new XElement("property", new XAttribute("name", "ParticleRelativeSpawnOrientation"), new XAttribute("value", "RELATIVE_TO_VFX_UP_AXIS")));
        var conversion = TotalWarVfxConverter.Convert(ReferenceSnapshot(snapshot with { GameBytes = source.Write() }, 2, "leaf"));
        Assert.That(conversion.Emitters.Single().ConstantForceDamping, Is.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.5f, Matrix.Identity);
        Assert.That(simulation.ForceBasisUnsupported, Is.True);
    }

    private static byte[] ForcePacket(TotalWarVfxSimulation simulation)
        => MemoryMarshal.AsBytes(simulation.Frame(Camera).AsSpan()).ToArray();

    private static Vector3 SourceDisplacement(int ticks, float speed, Vector3? force = null, float drag = Drag)
    {
        var position = Vector3.Zero;
        var sourceVelocity = Vector3.Zero;
        var sourceDelta = (1f / 120) * speed;
        // Independent native units: velocity and both integrals use source seconds, with no managed TimeScale squared.
        for (var tick = 0; tick < ticks; tick++)
        {
            sourceVelocity += (force ?? AuthoredForce) * .01f * sourceDelta;
            sourceVelocity *= MathF.Exp(-drag * sourceDelta);
            position += sourceVelocity * sourceDelta;
        }
        return position;
    }

    private static VfxPreviewSnapshot ReferenceSnapshot(VfxPreviewSnapshot child, float speed, string name)
    {
        var composition = VfxDocument.CreateComposition();
        var reference = composition.AddReference(name, VfxDocument.Read(child.GameBytes));
        reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed.ToString("R", CultureInfo.InvariantCulture));
        return new(composition.Write(), "diagnostic/source-clock.xml", path =>
            string.Equals(path.Replace('/', '\\'), $@"vfx\{name}.xml", StringComparison.OrdinalIgnoreCase)
                ? child.GameBytes : child.Resolve(path));
    }
}
