#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelEmitterMotionTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(false)]
    [TestCase(true)]
    public void FullLocal_TransportsAnExistingModelWithItsEmitterPosition(bool events)
    {
        var simulation = Simulation(Source(events));
        simulation.Seek(.7f, Matrix.Identity);
        var parent = simulation.Frame(Camera).Single(particle => particle.Layer == 0);
        Near(parent.Transform.Translation, new(3, -.7f, 2));
        Assert.That(simulation.ModelMotionScaleUnsupported, Is.False);
    }

    [Test]
    public void SpawnEvent_DoesNotDisableTheParentsNativeEmitterOrientation()
    {
        var independent = Simulation(Source(false, rotate: true));
        var withEvent = Simulation(Source(true, rotate: true));
        independent.Seek(.7f, Matrix.Identity); withEvent.Seek(.7f, Matrix.Identity);
        var expected = independent.Frame(Camera).Single().Transform;
        var actual = withEvent.Frame(Camera).Single(particle => particle.Layer == 0).Transform;
        Near(actual.Right, expected.Right); Near(actual.Up, expected.Up); Near(actual.Backward, expected.Backward);
    }

    [Test]
    public void TrackedChild_FollowsTheMovingModelInsteadOfItsBirthAnchor()
    {
        var simulation = Simulation(Source(true));
        simulation.Seek(.7f, Matrix.Identity);
        var particles = simulation.Frame(Camera);
        Near(particles.Single(particle => particle.Layer != 0).Transform.Translation, new(3, -.7f, 2));
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY")]
    public void OtherSpaces_RetainTheOriginalBirthPosition(string space)
    {
        var simulation = Simulation(Source(false, space: space));
        simulation.Seek(.7f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform.Translation, new(3, 7, 2));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void NonUnitRoot_DoesNotApplyUncalibratedEmitterTransport(float scale)
    {
        var simulation = Simulation(Source(false));
        simulation.Seek(.7f, Matrix.CreateScale(scale));
        Near(simulation.Frame(Camera).Single().Transform.Translation, new Vector3(3, 7, 2) * scale);
        AssertScaleDiagnostic(simulation, Matrix.CreateScale(scale));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void NonUnitInheritedEventScale_DoesNotApplyUncalibratedEmitterTransport(float scale)
    {
        var parent = VfxDocument.Read(Source(true));
        var emitter = parent.Root.Descendants("emitter").Single();
        Set(emitter, "EmitterPosition", Curve(Vector3.Zero, Vector3.Zero));
        Set(emitter, "ParticleScale", Curve(new(scale), new(scale)));
        emitter.Elements("property_event").Single().SetAttributeValue("track_scale", "true");
        var simulation = Simulation(parent.Write(), Source(false));
        simulation.Seek(.7f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single(particle => particle.Layer != 0).Transform.Translation,
            new Vector3(3, 7, 2) * scale);
        AssertScaleDiagnostic(simulation, Matrix.Identity);
    }

    [Test]
    public void PartialFramesAndRewind_DoNotCommitAdditionalEmitterTransport()
    {
        var simulation = Simulation(Source(true, rotate: true));
        simulation.Seek(.004f, Matrix.Identity);
        var first = simulation.Frame(Camera);
        for (var index = 0; index < 5; index++) Assert.That(simulation.Frame(Camera), Is.EqualTo(first));
        simulation.Seek(.637f, Matrix.Identity);
        var expected = simulation.Frame(Camera);
        simulation.Seek(.11f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.237f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.437f, Matrix.Identity); simulation.Frame(Camera);
        simulation.Seek(.637f, Matrix.Identity);
        var actual = simulation.Frame(Camera);
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        for (var index = 0; index < actual.Length; index++)
        {
            Near(actual[index].Transform.Translation, expected[index].Transform.Translation);
            Near(actual[index].Transform.Up, expected[index].Transform.Up);
        }
    }

    [TestCaseSource(nameof(NativePositionControls))]
    public void Transport_MatchesFreshNativeSsePositionAndVelocity(Vector3 position, Vector3 velocity,
        Quaternion previousNodeOrientation, Quaternion currentNodeOrientation, Vector3 previousNodePosition, Vector3 currentNodePosition,
        Vector3 previousEmitterPosition, Vector3 currentEmitterPosition, Quaternion previousEmitterOrientation, Quaternion currentEmitterOrientation,
        Vector3 expectedPosition, Vector3 expectedVelocity)
    {
        var previous = Matrix.CreateFromQuaternion(previousNodeOrientation) * Matrix.CreateTranslation(previousNodePosition);
        var current = Matrix.CreateFromQuaternion(currentNodeOrientation) * Matrix.CreateTranslation(currentNodePosition);
        var actual = VfxModelNativeMotion.Transport(position, velocity, previous, current,
            previousEmitterPosition, currentEmitterPosition, previousEmitterOrientation, currentEmitterOrientation);
        Near(actual.Position, expectedPosition); Near(actual.Velocity, expectedVelocity);
    }

    private static IEnumerable<object[]> NativePositionControls()
    {
        // Fresh official C75D40A3 DLL, 6CC965 matrix producer and 6CD870 SSE transport; fixed independent outputs.
        yield return
        [
            new Vector3(131f, 7f, 130f),
            new Vector3(0f, 0f, 0f),
            new Quaternion(0f, 0f, 0f, 1f),
            new Quaternion(0f, 0f, 0f, 1f),
            new Vector3(128f, 0f, 128f),
            new Vector3(128f, 0f, 128f),
            new Vector3(3f, 7f, 2f),
            new Vector3(3f, -4f, 2f),
            new Quaternion(0f, 0f, 0f, 1f),
            new Quaternion(0f, 0f, 0f, 1f),
            new Vector3(131f, -4f, 130f),
            new Vector3(0f, 0f, 0f)
        ];
        yield return
        [
            new Vector3(1.9808161092230119f, 1.4488614393278718f, 2.8759744381965424f),
            new Vector3(-2.2417131094145022f, -0.5310873891251946f, 1.8299024215875574f),
            new Quaternion(0.62803193710942384f, 0.32761248746431959f, 0.66992390800043189f, -0.22236884115688202f),
            new Quaternion(0.62803193710942384f, 0.32761248746431959f, 0.66992390800043189f, -0.22236884115688202f),
            new Vector3(-2.9748871130944154f, -1.3249240239200484f, 3.436758428482543f),
            new Vector3(0.57867208225145461f, -1.3960297786186437f, -1.5261264892731496f),
            new Vector3(0.70923948313050733f, 2.0190787905184262f, -0.17289431042233705f),
            new Vector3(0.70923948313050733f, 2.0190787905184262f, -0.17289431042233705f),
            new Quaternion(-0.68236542230393726f, -0.54671983247535572f, 0.026195996648847961f, -0.48454992000996333f),
            new Quaternion(0.31550411800839162f, 0.57926820827748238f, 0.74089514144164148f, -0.12641156508370702f),
            new Vector3(-2.6282138824462891f, -0.10131758451461792f, 2.1254804134368896f),
            new Vector3(2.7687711715698242f, -0.88101184368133545f, 0.46217045187950134f)
        ];
        yield return
        [
            new Vector3(-1.7023189883276411f, 3.0565858504065444f, 1.2494798015010549f),
            new Vector3(-3.3176295012079429f, 3.6031919701635831f, 1.9558066824908806f),
            new Quaternion(0.19684096224848935f, -0.49364782623182324f, 0.30455811530397392f, -0.79044912147473201f),
            new Quaternion(-0.35394384302140292f, 0.51514065040619961f, 0.51030679180269722f, 0.59071215031211f),
            new Vector3(-2.3344770720173136f, -1.9510057236908089f, 1.4991675256099137f),
            new Vector3(1.5418517671477181f, -2.2302316039748922f, 2.6835325675820974f),
            new Vector3(3.6427767386979317f, -3.3481064226488355f, -3.6169978166769425f),
            new Vector3(-0.38622200401967177f, 3.0781588685137011f, 3.950503051109032f),
            new Quaternion(0.9326112065891341f, 0.1296613234281607f, 0.10701607599167733f, -0.31933029613640029f),
            new Quaternion(0.9326112065891341f, 0.1296613234281607f, 0.10701607599167733f, -0.31933029613640029f),
            new Vector3(-2.3538882732391357f, -4.8112139701843262f, 16.350366592407227f),
            new Vector3(-0.7606205940246582f, -3.0110187530517578f, 4.2626361846923828f)
        ];
    }

    private static TotalWarVfxSimulation Simulation(byte[] bytes, byte[]? child = null)
    {
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/model-emitter-motion.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child ?? Child() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters.Count(emitter => !emitter.IsEventTemplate), Is.EqualTo(1),
            string.Join(';', conversion.Diagnostics.Select(item => item.ReasonKey)));
        return new(conversion);
    }

    private static void AssertScaleDiagnostic(TotalWarVfxSimulation simulation, Matrix root)
    {
        Assert.That(simulation.ModelMotionScaleUnsupported, Is.True);
        using var preview = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_simulation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(preview, simulation);
        preview.Seek(simulation.Time, root);
        preview.Seek(simulation.Time, root);
        Assert.That(preview.Diagnostics.Count(item => item.Field == "EmitterPosition/RootScale/InheritedScale"
            && item.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.EqualTo(1));
    }

    private static byte[] Source(bool events, bool rotate = false, string space = "LOCAL_SPACE_POSITION_ROTATION")
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "EmitterPosition", Curve(new(3, 7, 2), new(3, -4, 2)));
        Set(emitter, "EmitterOrientation", Curve(Vector3.Zero, rotate ? new(80, 35, -110) : Vector3.Zero));
        Set(emitter, "ParticleSimulationSpace", space, attribute: true);
        Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", attribute: true);
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 4));
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 1));
        Set(emitter, "MaximumParticles", "1", attribute: true);
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        emitter.Elements().Where(element => (string?)element.Attribute("name") == "LifeTime").Remove();
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"),
            new XAttribute("min", 1), new XAttribute("max", 1)));
        if (events)
        {
            Set(emitter, "EventEnable", "true", attribute: true);
            emitter.Add(new XElement("property_event", new XAttribute("name", "OnParticleSpawn"),
                new XAttribute("vfx_name", "child"), new XAttribute("track_position", "true"),
                new XAttribute("track_scale", "false"), new XAttribute("track_colour", "false"),
                new XAttribute("track_alpha", "false"), new XAttribute("track_lifetime", "false"),
                new XAttribute("spawn_event_time_offset", 0), new XAttribute("track_orientation_mode", "orientation")));
        }
        return document.Write();
    }

    private static byte[] Child()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 4));
        Set(emitter, "MaximumParticles", "1", attribute: true);
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ONLY", attribute: true);
        return document.Write();
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

    private static void Near(Vector3 actual, Vector3 expected)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f), $"{actual} != {expected}");
}
