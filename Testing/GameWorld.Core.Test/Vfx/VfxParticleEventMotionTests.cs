#nullable enable
using System.Globalization;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxParticleEventMotionTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 100),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    private static IEnumerable<TestCaseData> MovingParents()
    {
        foreach (var visible in new[] { false, true })
            foreach (var track in new[] { false, true })
            {
                foreach (var space in new[] { "WORLD_SPACE", "LOCAL_SPACE_POSITION_ONLY", "LOCAL_SPACE_POSITION_ROTATION" })
                    yield return new TestCaseData(visible, space, track, false);
                yield return new TestCaseData(visible, "LOCAL_SPACE_POSITION_ROTATION", track, true);
            }
    }

    [TestCaseSource(nameof(MovingParents))]
    public void ChildPosition_UsesTheActualParentAcrossSpacesAndCurrentHistoricalAttachments(bool visible, string space,
        bool track, bool dynamicEmitter)
    {
        var bytes = Parent(visible, space, track, dynamicEmitter);
        var (simulation, conversion) = Create(bytes, Child());
        var (control, controlConversion) = visible ? (simulation, conversion)
            : Create(Parent(true, space, track, dynamicEmitter), Child());
        Assert.That(conversion.Emitters.Single(e => e.Name == "parent").ModelEmitterMotion != null, Is.EqualTo(dynamicEmitter));
        Vector3? birthChild = null, initialParent = null;
        foreach (var time in new[] { 0f, .2f, .4f, .6f })
        {
            simulation.SeekWithCurrentAttachment(time, Current(time), History);
            if (!ReferenceEquals(control, simulation)) control.SeekWithCurrentAttachment(time, Current(time), History);
            var parent = Frame(control, controlConversion, "parent").Transform.Translation;
            var child = Frame(simulation, conversion, "child").Transform.Translation;
            birthChild ??= child;
            initialParent ??= parent;
            Near(child, track ? parent : birthChild.Value, $"{space}, visible={visible}, dynamic={dynamicEmitter}, t={time}");
            if (time == .6f)
                Assert.That(Vector3.Distance(parent, initialParent.Value), Is.GreaterThan(.1f), "A stationary control cannot detect a frozen birth anchor.");
        }
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void OrientationAndScaleTracking_DoNotEnablePositionTracking(bool orientation, bool scale)
    {
        var parent = VfxDocument.Read(Parent(true, "LOCAL_SPACE_POSITION_ROTATION", false,
            trackOrientation: orientation, trackScale: scale));
        var emitter = parent.Root.Descendants("emitter").Single();
        foreach (var name in new[] { "ParticleSpin_Min", "ParticleSpin_Max" })
            emitter.Add(new XElement("property", new XAttribute("name", name),
                new XAttribute("x", 0), new XAttribute("y", 90), new XAttribute("z", 0)));
        Set(emitter, "ParticleSpinDirectionY", "CCW", true);
        Set(emitter, "ParticleScale", VectorCurve(Vector3.One, new(2)));
        var (simulation, conversion) = Create(parent.Write(), Child(model: true));
        simulation.Seek(0, Matrix.Identity);
        var birth = Frame(simulation, conversion, "child").Transform.Translation;
        simulation.Seek(.5f, Matrix.Identity);
        var actualParent = Frame(simulation, conversion, "parent").Transform;
        var child = Frame(simulation, conversion, "child").Transform;
        Near(child.Translation, birth, "Orientation/scale follow must not move a position-frozen child.");
        Near(Vector3.Normalize(child.Right), orientation ? Vector3.Normalize(actualParent.Right) : Vector3.Right);
        Near(Vector3.Normalize(child.Up), orientation ? Vector3.Normalize(actualParent.Up) : Vector3.Up);
        Near(Vector3.Normalize(child.Backward), orientation ? Vector3.Normalize(actualParent.Backward) : Vector3.Backward);
        Assert.That(child.Right.Length(), Is.EqualTo(scale ? actualParent.Right.Length() : 1).Within(.00002));
        Assert.That(actualParent.Right.Length(), Is.GreaterThan(1.1f));
        Assert.That(Vector3.Distance(Vector3.Normalize(actualParent.Right), Vector3.Right), Is.GreaterThan(.1f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedEvent_TracksItsImmediateMovingParentRatherThanTheRootEmitter(bool track)
    {
        var middle = Parent(true, "LOCAL_SPACE_POSITION_ROTATION", track, name: "middle", eventPath: "leaf");
        var (simulation, conversion) = Create(Parent(true, "LOCAL_SPACE_POSITION_ROTATION", true, true), middle,
            Child(name: "leaf"));
        simulation.SeekWithCurrentAttachment(0, Current(0), History);
        var birth = Frame(simulation, conversion, "leaf").Transform.Translation;
        simulation.SeekWithCurrentAttachment(.5f, Current(.5f), History);
        var root = Frame(simulation, conversion, "parent").Transform.Translation;
        var parent = Frame(simulation, conversion, "middle").Transform.Translation;
        var child = Frame(simulation, conversion, "leaf").Transform.Translation;
        Near(child, track ? parent : birth);
        Assert.That(Vector3.Distance(root, parent), Is.GreaterThan(.1f), "The immediate parent must be distinguishable from the root emitter.");
    }

    [TestCase("OnParticleSpawn")]
    [TestCase("OnParticleDeath")]
    public void ParentDeath_FreezesTheActualWorldAnchorAndReplayPreservesIt(string eventName)
    {
        var parent = VfxDocument.Read(Parent(true, "LOCAL_SPACE_POSITION_ONLY", true));
        var emitter = parent.Root.Descendants("emitter").Single();
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .4f));
        emitter.Elements("ranged_property").Single(e => (string?)e.Attribute("name") == "LifeTime").SetAttributeValue("max", .01);
        emitter.Elements("ranged_property").Single(e => (string?)e.Attribute("name") == "LifeTime").SetAttributeValue("min", .01);
        emitter.Elements("property_event").Single().SetAttributeValue("name", eventName);
        var (simulation, conversion) = Create(parent.Write(), Child());
        simulation.Seek(.2f, History(.2f), History);
        var alive = Frame(simulation, conversion, "parent").Transform.Translation;
        simulation.Seek(.5f, History(.5f), History);
        var frozen = Frame(simulation, conversion, "child").Transform.Translation;
        Assert.That(simulation.Frame(Camera).Length, Is.EqualTo(1));
        Assert.That(Vector3.Distance(alive, frozen), Is.GreaterThan(.01f));
        simulation.SeekWithCurrentAttachment(.8f, Matrix.CreateTranslation(50, 30, -20), History);
        Near(Frame(simulation, conversion, "child").Transform.Translation, frozen);
        simulation.Seek(.1f, History(.1f), History);
        simulation.Seek(.5f, History(.5f), History);
        Near(Frame(simulation, conversion, "child").Transform.Translation, frozen, "Rewind must reproduce the same actual death anchor.");
    }

    internal static byte[] Parent(bool visible, string space, bool track, bool dynamicEmitter = false,
        string name = "parent", string eventPath = "child", bool trackOrientation = false, bool trackScale = false)
    {
        var document = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.SetAttributeValue("id", name);
        Set(emitter, "ParticleSimulationSpace", space, true);
        Set(emitter, "EmitterPosition", VectorCurve(new(1, 2, 3), dynamicEmitter ? new(3, -1, 4) : new(1, 2, 3)));
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 3));
        Set(emitter, "ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", 2));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 2));
        Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", true);
        emitter.Elements("ranged_property").Single(e => (string?)e.Attribute("name") == "LifeTime").SetAttributeValue("min", 2);
        emitter.Elements("ranged_property").Single(e => (string?)e.Attribute("name") == "LifeTime").SetAttributeValue("max", 2);
        var particleEvent = emitter.Elements("property_event").Single();
        particleEvent.SetAttributeValue("vfx_name", eventPath);
        particleEvent.SetAttributeValue("track_position", track ? "true" : "false");
        particleEvent.SetAttributeValue("track_orientation_mode", trackOrientation ? "orientation" : "none");
        particleEvent.SetAttributeValue("track_scale", trackScale ? "true" : "false");
        if (visible)
            emitter.Elements("property_rigid_data").Single().Add(new XElement("rigid_entry",
                new XElement("ModelFile", new XAttribute("value", "vfx/model-0.rigid_model_v2")),
                new XElement("PivotPoint", new XAttribute("x", .5), new XAttribute("y", .5), new XAttribute("z", .5))));
        return document.Write();
    }

    internal static byte[] Child(bool model = false, string name = "child")
    {
        var document = VfxDocument.Read(model ? VfxModelVariantTests.Source(1) : VfxParticleEventTests.Source(false));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.SetAttributeValue("id", name);
        Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION", true);
        Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", true);
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 2));
        Set(emitter, "MaximumParticles", "1", true);
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 1));
        Set(emitter, "ParticleScale", VectorCurve(Vector3.One));
        Set(emitter, "InfiniteLife", "false", true);
        emitter.Elements().Where(e => (string?)e.Attribute("name") == "LifeTime").Remove();
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .01), new XAttribute("max", .01)));
        return document.Write();
    }

    private static (TotalWarVfxSimulation Simulation, VfxConversion Conversion) Create(byte[] parent, byte[] child, byte[]? leaf = null)
    {
        var conversion = TotalWarVfxConverter.Convert(new(parent, "DIAG/event-motion-parent.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child
            : path.EndsWith("leaf.xml", StringComparison.OrdinalIgnoreCase) ? leaf : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters.Any(e => e.Name == "parent" && !e.IsEventTemplate), Is.True,
            string.Join(';', conversion.Diagnostics.Select(d => $"{d.Layer}/{d.Field}/{d.ReasonKey}")));
        return (new(conversion), conversion);
    }

    private static EffekseerParticle Frame(TotalWarVfxSimulation simulation, VfxConversion conversion, string name)
        => simulation.ProjectedFrame(Camera, applyCameraCulling: false).Single(p => conversion.Emitters[p.Render.Layer].Name == name).Render;
    private static Matrix History(float time) => Matrix.CreateRotationY(time * .3f) * Matrix.CreateTranslation(time * 2, time, -time);
    private static Matrix Current(float time) => Matrix.CreateRotationY(.4f + time) * Matrix.CreateTranslation(10 + time, 4 - time, time * 2);
    internal static void Set(XElement emitter, string name, string value, bool attribute = false)
    {
        emitter.Elements().Where(e => (string?)e.Attribute("name") == name).Remove();
        var node = new XElement("property", new XAttribute("name", name));
        if (attribute) node.SetAttributeValue("value", value); else node.Value = value;
        emitter.Add(node);
    }
    internal static string VectorCurve(Vector3 first, Vector3? end = null)
    {
        var last = end ?? first;
        return string.Join('|', new[] { ("x", first.X, last.X), ("y", first.Y, last.Y), ("z", first.Z, last.Z) }
            .Select(v => $"{v.Item1}_min,0,0;0,{v.Item2.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1;1,{v.Item3.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1"))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
    }
    private static void Near(Vector3 actual, Vector3 expected, string? message = null)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00002f), message ?? $"{actual} != {expected}");
}
