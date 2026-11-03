#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxModelQuaternionSpinTests
{
    private const string AngularChild = "angular-event-child";
    private static readonly byte[] EmptyAngularChild = VfxDocument.CreateComposition().Write();
    private static readonly string[] EventSpaces = ["WORLD_SPACE", "LOCAL_SPACE_POSITION_ONLY", "LOCAL_SPACE_POSITION_ROTATION"];

    public static IEnumerable<TestCaseData> EventNodeCases()
    {
        foreach (var space in EventSpaces)
            foreach (var kind in new[] { "OnParticleSpawn", "OnParticleDeath" })
                foreach (var mode in new[] { "enabled", "disabled", "empty-path", "disabled-empty-path", "both-events" })
                {
                    if (mode == "both-events" && kind == "OnParticleDeath") continue;
                    yield return new TestCaseData(space, kind, mode);
                }
    }

    [TestCaseSource(nameof(EventNodeCases))]
    public void RawEventNodes_DoNotSelectADifferentOwnerAngularIntegrator(string space, string kind, string mode)
    {
        var document = VfxDocument.Read(Source(space: space, damping: 1.7f));
        AddAngularEvent(Emitter(document), kind, mode);
        var bytes = document.Write(); var conversion = ConvertAngularEvent(bytes);
        var definition = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(definition.ModelQuaternionSpin, Is.True);
            Assert.That(definition.ModelEmitterMotion, Is.Null, "Constant emitter transforms must not be needed to admit owner rotation.");
            Assert.That(definition.Events, Has.Count.EqualTo(mode == "both-events" ? 2 : mode == "enabled" ? 1 : 0));
            Assert.That(conversion.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
        });
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.637f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        Near(first.Transform, Oracle(.637f, 1, Spin, Birth(), 1.7f));
        for (var frame = 0; frame < 3; ++frame) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.11f, Matrix.Identity);
        foreach (var target in new[] { .237f, .437f, .637f }) simulation.Seek(target, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, first.Transform);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    public static IEnumerable<TestCaseData> EventAngularParameterCases()
    {
        foreach (var space in EventSpaces)
            foreach (var axis in new[] { "X", "Y", "Z", "XYZ", "zero" })
                foreach (var damping in new[] { "zero", "constant", "five", "negative", "high", "dynamic", "bezier" })
                    yield return new TestCaseData(space, axis, damping);
    }

    [TestCaseSource(nameof(EventAngularParameterCases))]
    public void EventOwners_KeepTheCalibratedSpaceAxisAndDampingRules(string space, string axis, string damping)
    {
        var spin = axis switch { "X" => new Vector3(180, 0, 0), "Y" => new Vector3(0, 180, 0),
            "Z" => new Vector3(0, 0, 180), "zero" => Vector3.Zero, _ => Spin };
        var control = VfxDocument.Read(Source(space: space, spin: spin));
        Emitter(control).Elements().Single(item => (string?)item.Attribute("name") == "ParticleSpinDeceleration").Value = damping switch
        {
            "zero" => Curve("x", 0), "constant" => Curve("x", .15), "five" => Curve("x", 5),
            "negative" => Curve("x", -.4), "high" => Curve("x", 8), "dynamic" => Curve("x", .1, 1.4),
            _ => "x_min,0,0;0,.1,linear,bezier_c,0,0,.11,1.3,1;1,2,bezier_c,linear,-.31,-1.1,0,0,1:x,0;0,0",
        };
        var actual = VfxDocument.Read(control.Write());
        AddAngularEvent(Emitter(actual), axis is "Y" or "XYZ" ? "OnParticleDeath" : "OnParticleSpawn", "enabled");
        var expected = ConvertAngularEvent(control.Write()); var conversion = ConvertAngularEvent(actual.Write());
        var definition = conversion.Emitters.Single(); var baseline = expected.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(definition.ModelQuaternionSpin, Is.EqualTo(baseline.ModelQuaternionSpin));
            Assert.That(definition.SpinMin, Is.EqualTo(spin));
            Assert.That(definition.SpinMax, Is.EqualTo(spin));
            Assert.That(definition.ModelSpinDeceleration != null, Is.EqualTo(baseline.ModelSpinDeceleration != null));
            Assert.That(definition.Events, Has.Count.EqualTo(1));
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "ParticleSpinDeceleration"
                && item.ReasonKey == "Vfx.Preview.ForceUnsupported"),
                Is.EqualTo(expected.Diagnostics.Any(item => item.Field == "ParticleSpinDeceleration"
                    && item.ReasonKey == "Vfx.Preview.ForceUnsupported")));
        });
        var quaternionExpected = space == "LOCAL_SPACE_POSITION_ROTATION" || axis is "X" or "Y" or "XYZ"
            || axis == "Z" && damping is "negative" or "high" or "dynamic" or "bezier";
        Assert.That(definition.ModelQuaternionSpin, Is.EqualTo(quaternionExpected));
        var simulation = new TotalWarVfxSimulation(conversion); var reference = new TotalWarVfxSimulation(expected);
        foreach (var time in new[] { .004f, .2f, .637f, .11f, .637f })
        {
            simulation.Seek(time, Matrix.Identity); reference.Seek(time, Matrix.Identity);
            Near(simulation.Frame(Camera).Single().Transform, reference.Frame(Camera).Single().Transform);
        }
    }

    [TestCase(false, false, "OnParticleSpawn")]
    [TestCase(false, true, "OnParticleSpawn")]
    [TestCase(true, false, "OnParticleSpawn")]
    [TestCase(true, true, "OnParticleSpawn")]
    [TestCase(false, false, "OnParticleDeath")]
    [TestCase(false, true, "OnParticleDeath")]
    [TestCase(true, false, "OnParticleDeath")]
    [TestCase(true, true, "OnParticleDeath")]
    public void FullLocalEvents_DoNotChangeConstantOrDynamicEmitterTransport(bool position, bool orientation, string kind)
    {
        var control = VfxDocument.Read(Source(space: "LOCAL_SPACE_POSITION_ROTATION"));
        Emitter(control).Add(new XElement("property", new XAttribute("name", "EmitterPosition"),
            position ? Curve("xyz", 0, 2) : VectorCurve(new(1, 2, 3))));
        Emitter(control).Add(new XElement("property", new XAttribute("name", "EmitterOrientation"),
            orientation ? Curve("xyz", 0, 33) : VectorCurve(new(0, 33, 0))));
        Emitter(control).Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        var actual = VfxDocument.Read(control.Write()); AddAngularEvent(Emitter(actual), kind, "enabled");
        var expected = ConvertAngularEvent(control.Write()); var conversion = ConvertAngularEvent(actual.Write());
        var definition = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(definition.ModelQuaternionSpin, Is.True);
            Assert.That(definition.ModelEmitterOrientation != null, Is.EqualTo(orientation));
            Assert.That(definition.ModelEmitterMotion != null, Is.EqualTo(position || orientation));
        });
        var simulation = new TotalWarVfxSimulation(conversion); var reference = new TotalWarVfxSimulation(expected);
        Matrix Root(float time) => Matrix.CreateRotationX(.3f * time) * Matrix.CreateRotationY(-.4f * time)
            * Matrix.CreateTranslation(time, time * 2, time * 3);
        foreach (var time in new[] { .004f, .2f, .637f, .11f, .637f })
        {
            simulation.Seek(time, Root(time), Root); reference.Seek(time, Root(time), Root);
            var packet = simulation.Frame(Camera).Single(); var baseline = reference.Frame(Camera).Single();
            Near(packet.Transform, baseline.Transform);
            Assert.That(Vector3.Distance(packet.Transform.Translation, baseline.Transform.Translation), Is.LessThan(Tolerance));
            Assert.That(simulation.ModelSpinBasisUnsupported, Is.False);
        }
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY")]
    [TestCase("LOCAL_SPACE_POSITION_ROTATION")]
    public void EventNodes_DoNotAdmitAnInvalidDampingTableLink(string space)
    {
        var document = VfxDocument.Read(Source(space: space));
        Emitter(document).Elements().Single(item => (string?)item.Attribute("name") == "ParticleSpinDeceleration").Value =
            "x_min,0,0;0,.1,linear,step,0,0,0,0,1;1,1.4,step,linear,0,0,0,0,1:x,0;0,9";
        AddAngularEvent(Emitter(document), "OnParticleSpawn", "enabled");
        var conversion = ConvertAngularEvent(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().ModelQuaternionSpin, Is.False);
            Assert.That(conversion.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "ParticleSpinDeceleration"
                && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "ParticleSpinDeceleration"
                && item.ReasonKey == "Vfx.Preview.InvalidData"), Is.True);
        });
    }

    [TestCase("OnParticleSpawn")]
    [TestCase("OnParticleDeath")]
    public void MappedStaticMeshLoop_RetainsItsSeparateAngularPath(string kind)
    {
        var document = VfxDocument.Read(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1)); var emitter = Emitter(document);
        emitter.Add(Property("MaximumParticles", 1), Property("ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION"),
            new XElement("property", new XAttribute("name", "ParticleLoopParams"), new XAttribute("x", .2f), new XAttribute("y", .4f)),
            VectorProperty("ParticleSpin_Min", Spin), VectorProperty("ParticleSpin_Max", Spin));
        AddAngularEvent(emitter, kind, "enabled");
        var material = Encoding.UTF8.GetBytes(VfxLegacyRigidDissolveMaterialTests.Material().ToString());
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/model-loop-events.xml", path =>
            IsAngularChild(path) ? EmptyAngularChild : path.Replace('\\', '/') == "vfx/character.xml.material" ? material
                : VfxOpaqueModelMaterialTests.Resolve(path, [255])));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(item => item.Field + ":" + item.ReasonKey)));
        Assert.That(conversion.Emitters.Single().MeshLoop, Is.EqualTo(new Vector2(.2f, .4f)));
        Assert.That(conversion.Emitters.Single().ModelQuaternionSpin, Is.False);
        Assert.That(conversion.Emitters.Single().ModelSpinDeceleration, Is.Null);
    }

    private static VfxConversion ConvertAngularEvent(byte[] source)
    {
        var result = TotalWarVfxConverter.Convert(new(source, "diagnostic/model-owner-event-angular.xml",
            path => IsAngularChild(path) ? EmptyAngularChild : VfxModelVariantTests.Resolve(path)));
        Assert.That(result.Emitters, Has.Count.EqualTo(1), string.Join(';', result.Diagnostics.Select(item => item.Field + ":" + item.ReasonKey)));
        return result;
    }

    private static bool IsAngularChild(string path) => path.Replace('\\', '/').EndsWith("/" + AngularChild + ".xml", StringComparison.Ordinal);

    private static void AddAngularEvent(XElement emitter, string kind, string mode)
    {
        emitter.Add(Property("EventEnable", mode is "disabled" or "disabled-empty-path" ? "false" : "true"));
        XElement Event(string name) => new("property_event", new XAttribute("name", name),
            new XAttribute("vfx_name", mode is "empty-path" or "disabled-empty-path" ? "" : AngularChild),
            new XAttribute("track_position", "true"), new XAttribute("track_scale", "false"),
            new XAttribute("track_colour", "false"), new XAttribute("track_alpha", "false"),
            new XAttribute("track_lifetime", "false"), new XAttribute("spawn_event_time_offset", 0),
            new XAttribute("track_orientation_mode", "orientation"));
        emitter.Add(Event(kind));
        if (mode == "both-events") emitter.Add(Event(kind == "OnParticleSpawn" ? "OnParticleDeath" : "OnParticleSpawn"));
    }
}
