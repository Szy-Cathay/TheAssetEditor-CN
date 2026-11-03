#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxParticleEventPrewarmTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 20),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    public static IEnumerable<TestCaseData> NegativeEventCases()
    {
        foreach (var visible in new[] { false, true })
            foreach (var space in new[] { "WORLD_SPACE", "LOCAL_SPACE_POSITION_ONLY", "LOCAL_SPACE_POSITION_ROTATION" })
                foreach (var reference in new[] { false, true }) yield return new(visible, space, reference);
    }

    [TestCaseSource(nameof(NegativeEventCases))]
    public void NegativeChildStart_PreparesExistingTailWithoutRewindingTheParent(bool visible, string space, bool reference)
    {
        var parent = Parent(visible, space); var child = Child(reference ? .07f : -.18f);
        var eventSource = reference ? Reference(child, -.25f) : child;
        var (simulation, conversion) = Create(parent, eventSource, reference ? child : null);
        simulation.Seek(0, Matrix.Identity);
        var parents = Particles(simulation).Where(p => !Definition(p).IsEventTemplate).ToArray();
        var tails = Particles(simulation).Where(p => Definition(p).Name == "tail").ToArray();
        Assert.That(parents, Has.Length.EqualTo(1)); Assert.That(tails, Has.Length.EqualTo(1));
        Assert.That((double)Get(tails[0], "Birth"), Is.LessThan(-.16));
        Assert.That((double)Get(parents[0], "Birth"), Is.EqualTo(0));
        Assert.That(simulation.Time, Is.EqualTo(0));
        var disabled = VfxDocument.Read(parent);
        VfxParticleEventMotionTests.Set(disabled.Root.Descendants("emitter").Single(), "EventEnable", "false", true);
        var (control, _) = Create(disabled.Write(), eventSource, reference ? child : null);
        foreach (var time in new[] { 0f, .04f })
        {
            simulation.Seek(time, Matrix.Identity); control.Seek(time, Matrix.Identity);
            var actual = Particles(simulation).Single(p => !Definition(p).IsEventTemplate);
            var expected = Particles(control).Single();
            Assert.That(Get(actual, "Position"), Is.EqualTo(Get(expected, "Position")), "Child preparation cannot move the existing parent.");
            Assert.That(Get(actual, "Velocity"), Is.EqualTo(Get(expected, "Velocity")));
            Assert.That(simulation.Frame(Camera).Count(p => conversion.Emitters[p.Layer].Name == "tail"), Is.EqualTo(1));
        }
        var first = simulation.Frame(Camera).Where(p => conversion.Emitters[p.Layer].Name == "tail").Select(p => p.Transform).ToArray();
        simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Count(p => conversion.Emitters[p.Layer].Name == "tail"), Is.EqualTo(0), "Authored tail life is unchanged.");
        simulation.Seek(0, Matrix.Identity); simulation.Seek(.04f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Where(p => conversion.Emitters[p.Layer].Name == "tail").Select(p => p.Transform), Is.EqualTo(first));
        Assert.That(VfxDocument.Read(parent).Write(), Is.EqualTo(parent));
        Assert.That(VfxDocument.Read(eventSource).Write(), Is.EqualTo(eventSource));
    }

    [TestCase(false, .5f)]
    [TestCase(true, .5f)]
    [TestCase(false, 2f)]
    [TestCase(true, 2f)]
    public void PositiveEventDelay_UsesSourceSecondsBeforePreparingItsNegativeChildren(bool tracking, float speed)
    {
        var parent = VfxDocument.Read(Parent(true, "WORLD_SPACE"));
        var particleEvent = parent.Root.Descendants("property_event").Single();
        particleEvent.SetAttributeValue("spawn_event_time_offset", .5);
        particleEvent.SetAttributeValue("track_position", tracking ? "true" : "false");
        var source = parent.Write(); var wrapper = VfxDocument.CreateComposition();
        var owner = wrapper.AddReference("owner", parent); owner.Apply();
        owner.Selection.SetAttributeValue("simulation_speed_multiplier", speed.ToString("R", CultureInfo.InvariantCulture));
        var (simulation, conversion) = Create(wrapper.Write(), Reference(Child(.07f), -.25f), Child(.07f), owner: source);
        var activation = .5f / speed;
        simulation.Seek(activation - .1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Any(p => conversion.Emitters[p.Layer].Name == "tail"), Is.False,
            "A negative child/reference start cannot activate a parent event before its positive delay.");
        simulation.Seek(activation, Matrix.Identity);
        var packets = simulation.Frame(Camera);
        var child = packets.Single(p => conversion.Emitters[p.Layer].Name == "tail");
        var parentPacket = packets.Single(p => conversion.Emitters[p.Layer].Name == "parent");
        Assert.That(Vector3.Distance(child.Transform.Translation, parentPacket.Transform.Translation), Is.LessThan(.00002f));
        Assert.That(Particles(simulation).Where(p => Definition(p).Name == "tail").All(p => (double)Get(p, "Birth") < activation), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeathEvent_NegativeChildTailStartsAtTheFrozenDeathPose(bool visible)
    {
        var parent = VfxDocument.Read(Parent(visible, "WORLD_SPACE")); var emitter = parent.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .2f));
        emitter.Elements("property_event").Single().SetAttributeValue("name", "OnParticleDeath");
        var (simulation, conversion) = Create(parent.Write(), Child(-.18f));
        simulation.Seek(.21f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single(p => conversion.Emitters[p.Layer].Name == "tail").Transform.Translation;
        Assert.That(Particles(simulation).Where(p => Definition(p).Name == "tail").All(p => (double)Get(p, "Birth") < .21), Is.True);
        simulation.Seek(.23f, Matrix.CreateTranslation(30, 0, 0));
        var second = simulation.Frame(Camera).Single(p => conversion.Emitters[p.Layer].Name == "tail").Transform.Translation;
        Assert.That(Vector3.Distance(first, second), Is.LessThan(.00002f));
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY")]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION")]
    public void NegativeStartAtDelayedActivation_UsesTheCreationPoseWithMovingAttachmentHistory(string space)
    {
        var parent = VfxDocument.Read(Parent(true, space));
        parent.Root.Descendants("property_event").Single().SetAttributeValue("spawn_event_time_offset", .5);
        var (simulation, conversion) = Create(parent.Write(), Child(-.18f));
        static Matrix History(float time) => Matrix.CreateRotationY(time)
            * Matrix.CreateTranslation(time * 12, time * 2, time * -3);
        simulation.Seek(.5f, History(.5f), History);
        var packets = simulation.Frame(Camera);
        var child = packets.Single(p => conversion.Emitters[p.Layer].Name == "tail");
        var owner = packets.Single(p => conversion.Emitters[p.Layer].Name == "parent");
        Assert.That(Vector3.Distance(child.Transform.Translation, owner.Transform.Translation), Is.LessThan(.00002f),
            "Negative-time preparation uses the newly created event pose, not an earlier attachment pose.");
        var expected = child.Transform;
        simulation.Seek(.7f, History(.7f), History);
        simulation.Seek(0, History(0), History);
        simulation.Seek(.5f, History(.5f), History);
        Assert.That(simulation.Frame(Camera).Single(p => conversion.Emitters[p.Layer].Name == "tail").Transform,
            Is.EqualTo(expected), "Rewind must rebuild the same creation pose and surviving negative-time tail.");
    }

    [Test]
    public void NestedNegativeEventPreparation_PreservesTheDescendantTail()
    {
        var middle = VfxDocument.Read(Parent(false, "LOCAL_SPACE_POSITION_ROTATION")); var emitter = middle.Root.Descendants("emitter").Single();
        emitter.SetAttributeValue("id", "middle"); emitter.Elements("property_event").Single().SetAttributeValue("vfx_name", "leaf");
        VfxParticleEventMotionTests.Set(emitter, "EmitterPosition", VfxParticleEventMotionTests.VectorCurve(Vector3.Zero));
        VfxParticleEventMotionTests.Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        Range(emitter, "StartTime", -.18f); Range(emitter, "LifeTime", .1f);
        VfxParticleEventMotionTests.Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .25f));
        var leaf = VfxDocument.Read(Child(-.1f));
        VfxParticleEventMotionTests.Set(leaf.Root.Descendants("emitter").Single(), "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 1));
        var (simulation, conversion) = Create(Parent(true, "WORLD_SPACE"), middle.Write(), leaf: leaf.Write());
        simulation.Seek(0, Matrix.Identity);
        var tail = simulation.Frame(Camera).Single(p => conversion.Emitters[p.Layer].Name == "tail");
        var parent = simulation.Frame(Camera).Single(p => conversion.Emitters[p.Layer].Name == "parent");
        Assert.That(Vector3.Distance(tail.Transform.Translation, parent.Transform.Translation), Is.LessThan(.00002f));
        Assert.That((double)Get(Particles(simulation).Single(p => Definition(p).Name == "tail"), "Birth"), Is.LessThan(-.25));
    }

    private static byte[] Parent(bool visible, string space) => VfxParticleEventMotionTests.Parent(visible, space, true);
    private static byte[] Child(float start)
    {
        var document = VfxDocument.Read(VfxParticleEventMotionTests.Child(name: "tail")); var emitter = document.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(emitter, "EmitterPosition", VfxParticleEventMotionTests.VectorCurve(Vector3.Zero));
        VfxParticleEventMotionTests.Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .25f));
        Range(emitter, "StartTime", start); Range(emitter, "LifeTime", .1f); return document.Write();
    }
    private static byte[] Reference(byte[] child, float start)
    {
        var document = VfxDocument.CreateComposition(); var reference = document.AddReference("inner", VfxDocument.Read(child)); reference.Apply();
        foreach (var bound in new[] { "start_time_min", "start_time_max" }) reference.Selection.SetAttributeValue(bound, start.ToString("R", CultureInfo.InvariantCulture));
        return document.Write();
    }
    private static void Range(XElement emitter, string name, float value)
    {
        emitter.Elements().Where(p => (string?)p.Attribute("name") == name).Remove();
        emitter.Add(new XElement("ranged_property", new XAttribute("name", name), new XAttribute("min", value), new XAttribute("max", value)));
    }
    private static (TotalWarVfxSimulation, VfxConversion) Create(byte[] parent, byte[] child, byte[]? inner = null, byte[]? leaf = null, byte[]? owner = null)
    {
        var conversion = TotalWarVfxConverter.Convert(new(parent, "DIAG/event-negative-start.xml", path => path.Replace('/', '\\') switch
        {
            @"vfx\child.xml" => child, @"vfx\inner.xml" => inner, @"vfx\leaf.xml" => leaf, @"vfx\owner.xml" => owner,
            _ => VfxModelVariantTests.Resolve(path),
        }));
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
        return (new(conversion), conversion);
    }
    private static object Get(object target, string name) => target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static IEnumerable<object> Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Get(simulation, "_particles")).Cast<object>();
    private static VfxPreviewEmitter Definition(object particle) => (VfxPreviewEmitter)Get(Get(particle, "Emitter"), "Definition");
}
