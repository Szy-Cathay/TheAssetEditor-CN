#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeTrailFrameCadenceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float RootScale = .8478705883026123f;
    private sealed record Birth(double Time, Vector3 Position, Vector3 Velocity);
    private sealed record NativePath(Vector3[] Knots, Birth[][] Expected);

    // Actual C75 native6c1a60 arithmetic/control loop, SHA07e16897c0941bc9f89e38b3f25563dca0ff7994a59e054bcfc1efce556d513e.
    // Constant original blowpipe q1 speed.2/stretch10,15/Ysize.2/referenceS; zero-area/identity inputs.
    // External getter/allocator/writer hooks isolate the kernel; no full native RNG, curves, lifetime or visual claim.
    // The DIAG2s life below only retains every recorded birth for comparison; it is not an official asset modification.
    [TestCase("arc60", 0)]
    [TestCase("arc60", 1)]
    [TestCase("right-angle60-off-boundary", 0)]
    [TestCase("right-angle60-off-boundary", 1)]
    [TestCase("stop-restart60", 0)]
    [TestCase("stop-restart60", 1)]
    public void ContinuousTrail_PreservesNativeOuterFrameBirthCentresVelocityAndClock(string name, int layer)
    {
        var path = Paths[name];
        var simulation = new TotalWarVfxSimulation(Convert(Source(layer)));
        Matrix At(float time) => History(path, time);
        for (var frame = 0; frame < path.Knots.Length; frame++)
        {
            var time = frame / 60f;
            simulation.SeekContinuousTrail(time, At(time), At);
        }
        var actual = Births(simulation);
        var expected = path.Expected[layer];
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        Assert.Multiple(() =>
        {
            for (var index = 0; index < actual.Length; index++)
            {
                Assert.That(Vector3.Distance(actual[index].Position, expected[index].Position), Is.LessThan(.00001f), $"birth{index} centre");
                Assert.That(Vector3.Distance(actual[index].Velocity, expected[index].Velocity), Is.LessThan(.000001f), $"birth{index} world velocity");
                Assert.That(actual[index].Time, Is.EqualTo(expected[index].Time).Within(.000002), $"birth{index} source clock");
            }
            Assert.That(simulation.RateLimited, Is.False);
            Assert.That(simulation.CapacityReached, Is.False);
            Assert.That(Particles(simulation).All(particle => (float)Read(particle, "Life") == 2), Is.True);
        });
    }

    [Test]
    public void OrdinarySeek_KeepsFixedStepResultsIndependentOfOuterCallBoundaries()
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        var conversion = Convert(Source(0));
        var direct = new TotalWarVfxSimulation(conversion);
        var split = new TotalWarVfxSimulation(conversion);
        direct.Seek(1, At(1), At);
        for (var frame = 0; frame <= 60; frame++) split.Seek(frame / 60f, At(frame / 60f), At);
        Assert.That(Records(split), Is.EqualTo(Records(direct)));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void NegativeStartContinuousTrail_KeepsTheExistingFixedStepRoute(int layer)
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        var document = VfxDocument.Read(Source(layer));
        document.Root.Descendants("emitter").Single().Add(new XElement("ranged_property", new XAttribute("name", "StartTime"),
            new XAttribute("min", -.25f), new XAttribute("max", -.25f)));
        var conversion = Convert(document.Write());
        Assert.That(conversion.Emitters.Single().Start.Min, Is.EqualTo(-.25f));
        var ordinary = new TotalWarVfxSimulation(conversion);
        var continuous = new TotalWarVfxSimulation(conversion);
        for (var frame = 0; frame <= 42; frame++)
        {
            var time = frame / 60f;
            ordinary.Seek(time, At(time), At);
            continuous.SeekContinuousTrail(time, At(time), At);
        }
        Assert.That(Records(continuous), Is.EqualTo(Records(ordinary)));
    }

    [Test]
    public void NonTrailDistanceEmitter_KeepsItsExistingDirectionAndPlaybackRoute()
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        var conversion = Convert(Source(0, trail: false));
        var ordinary = new TotalWarVfxSimulation(conversion);
        var continuous = new TotalWarVfxSimulation(conversion);
        for (var frame = 0; frame <= 60; frame++)
        {
            var time = frame / 60f;
            ordinary.Seek(time, At(time), At);
            continuous.SeekContinuousTrail(time, At(time), At);
        }
        Assert.That(Records(continuous), Is.EqualTo(Records(ordinary)));
    }

    [Test]
    public void ContinuousFrame_AdvancesConstantMotionFromItsBirthClockExactlyOnceAtAPartialTick()
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        var simulation = new TotalWarVfxSimulation(Convert(Source(0)));
        for (var frame = 0; frame <= 10; frame++) simulation.SeekContinuousTrail(frame / 60f, At(frame / 60f), At);
        const float now = .173f;
        simulation.SeekContinuousTrail(now, At(now), At);
        var packets = simulation.Frame(Camera);
        var births = Births(simulation);
        Assert.That(packets, Has.Length.EqualTo(births.Length));
        foreach (var birth in births)
        {
            var expected = birth.Position + birth.Velocity * (float)(now - birth.Time);
            Assert.That(packets.Min(packet => Vector3.Distance(packet.Transform.Translation, expected)), Is.LessThan(.000002f));
        }
    }

    [Test]
    public void ContinuousFrame_DoesNotDisplayBackdatedBirthsPastTheirUnchangedLife()
    {
        var document = VfxDocument.Read(Source(0));
        Field(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", .05f);
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        static Matrix At(float time) => Matrix.CreateScale(RootScale) * Matrix.CreateTranslation(time < .5f ? Vector3.Zero : new Vector3(8.1f, 0, 0));
        simulation.SeekContinuousTrail(0, At(0), At);
        simulation.SeekContinuousTrail(.5f, At(.5f), At);
        var packets = simulation.Frame(Camera);
        Assert.That(packets, Is.Not.Empty);
        Assert.That(packets.All(packet => packet.Time >= 0 && packet.Time < .05f), Is.True,
            "A native backdated birth keeps its authored life rather than being clamped into a visible final frame.");
        Assert.That(Particles(simulation).All(particle => (float)Read(particle, "Life") == .05f), Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void RecordedInputKnots_SingleSeekReplaysTheSameBirthsAndRandomsAsIndividualFrames(int layer)
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        float? Next(float time) => NextSample(path, time);
        var conversion = Convert(Source(layer));
        var split = new TotalWarVfxSimulation(conversion);
        for (var frame = 0; frame < path.Knots.Length; frame++)
            split.SeekContinuousTrail(frame / 60f, At(frame / 60f), At, Next);
        var replay = new TotalWarVfxSimulation(conversion);
        replay.SeekContinuousTrail(1, At(1), At, Next);
        Assert.That(Records(replay), Is.EqualTo(Records(split)));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void RewindAndReplaceFutureKnots_ReplaysTheRetainedPrefixAndOnlyTheNewPath(int layer)
    {
        var original = Paths["arc60"];
        var changedKnots = original.Knots.ToArray();
        for (var frame = 31; frame < changedKnots.Length; frame++)
            changedKnots[frame] = changedKnots[30] + new Vector3(0, 0, (frame - 30) * 8 / 60f);
        var changed = new NativePath(changedKnots, []);
        Matrix Before(float time) => History(original, time);
        Matrix After(float time) => History(changed, time);
        float? NextBefore(float time) => NextSample(original, time);
        float? NextAfter(float time) => NextSample(changed, time);
        var conversion = Convert(Source(layer));
        var edited = new TotalWarVfxSimulation(conversion);
        edited.SeekContinuousTrail(1, Before(1), Before, NextBefore);
        var previous = Records(edited);
        edited.SeekContinuousTrail(.5f, Before(.5f), Before, NextBefore);
        edited.SeekContinuousTrail(1, Before(1), Before, NextBefore);
        Assert.That(Records(edited), Is.EqualTo(previous), "A plain rewind must replay the same source birth and random stream.");
        edited.SeekContinuousTrail(.5f, Before(.5f), Before, NextBefore);
        edited.SeekContinuousTrail(1, After(1), After, NextAfter);
        var fresh = new TotalWarVfxSimulation(conversion);
        fresh.SeekContinuousTrail(1, After(1), After, NextAfter);
        Assert.That(Records(edited), Is.EqualTo(Records(fresh)));
        Assert.That(Records(edited), Is.Not.EqualTo(previous), "The truncated old future must not remain in replay.");
    }

    [Test]
    public void PausedTransformAtTheSameClock_DoesNotEmitOrReplaceExistingWorldParticles()
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        float? Next(float time) => NextSample(path, time);
        var simulation = new TotalWarVfxSimulation(Convert(Source(0)));
        simulation.SeekContinuousTrail(.5f, At(.5f), At, Next);
        var before = Records(simulation);
        var moved = At(.5f) * Matrix.CreateTranslation(5, 0, 0);
        simulation.SeekContinuousTrail(.5f, moved, At, Next);
        simulation.SeekContinuousTrail(.5f, moved, At, Next);
        Assert.That(Records(simulation), Is.EqualTo(before));
    }

    [Test]
    public void EmissionEndInsideAFixedTick_UsesTheAuthoredStopPointAndRetainsItsLiveTail()
    {
        const float stop = .506f;
        const float now = .75f;
        static Matrix At(float time) => Matrix.CreateScale(RootScale) * Matrix.CreateTranslation(time * 80, 0, 0);
        static float? Next(float time)
        {
            for (var frame = 1; frame <= 45; frame++)
                if (frame / 60f > time) return frame / 60f;
            return null;
        }
        var simulation = new TotalWarVfxSimulation(Convert(Source(0))) { EmissionEndTimeSeconds = stop };
        simulation.SeekContinuousTrail(now, At(now), At, Next);
        var births = Births(simulation);
        Assert.That(births, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var birth in births)
            {
                Assert.That(birth.Time, Is.LessThanOrEqualTo(stop));
                Assert.That(birth.Position.X, Is.EqualTo(80 * birth.Time).Within(.00002));
                Assert.That(birth.Position.X, Is.LessThanOrEqualTo(80 * stop));
            }
            Assert.That(simulation.Frame(Camera), Is.Not.Empty);
            Assert.That(Particles(simulation).All(particle => (float)Read(particle, "Life") == 2), Is.True);
        });
        simulation.SeekContinuousTrail(2.6f, At(2.6f), At, Next);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void CapacityOne_BackdatedBirthKeepsItsLifeAndLaterContinuousMotionCanBeVisible()
    {
        var document = VfxDocument.Read(Source(0));
        Field(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", .05f);
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        static Matrix At(float time) => Matrix.CreateScale(RootScale) * Matrix.CreateTranslation(
            time < 4 ? 0 : time < 5 ? 8.1f : 8.1f + (time - 5) * 8, 0, 0);
        simulation.SeekContinuousTrail(0, At(0), At);
        simulation.SeekContinuousTrail(4, At(4), At);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1), "The original batch can insert an already expired birth before capacity stops it.");
        Assert.That(simulation.Frame(Camera), Is.Empty);
        var visibleAfterMotion = false;
        for (var frame = 1; frame <= 120; frame++)
        {
            var time = 4 + frame / 60f;
            simulation.SeekContinuousTrail(time, At(time), At);
            var packets = simulation.Frame(Camera);
            Assert.That(simulation.ParticleCount, Is.LessThanOrEqualTo(1));
            Assert.That(Particles(simulation).All(particle => (float)Read(particle, "Life") == .05f), Is.True);
            Assert.That(packets.All(packet => packet.Time >= 0 && packet.Time < .05f), Is.True);
            visibleAfterMotion |= time > 5 && packets.Length > 0;
        }
        Assert.That(visibleAfterMotion, Is.True);
        Assert.That(simulation.CapacityReached, Is.False);
    }

    [TestCase(4f)]
    [TestCase(-4f)]
    public void ZeroEndpointBezierDamping_KeepsTheExistingFixedStepMotion(float handle)
    {
        var path = Paths["arc60"];
        Matrix At(float time) => History(path, time);
        var document = VfxDocument.Read(Source(0));
        var tangent = handle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleDeceleration"),
            $"x_min,0,0;0,0,linear,bezier_c,0,0,.333,{tangent},1;1,0,bezier_c,linear,-.333,{tangent},0,0,1:x,0;0,0"));
        var conversion = Convert(document.Write());
        var definition = conversion.Emitters.Single();
        Assert.That(definition.Deceleration.HasBezierSegments, Is.True);
        Assert.That(definition.Deceleration.Sample("x", .5, .5f) * MathF.Sign(handle), Is.GreaterThan(1),
            "Zero key values do not make nonzero Bezier handles a zero damping curve.");
        var ordinary = new TotalWarVfxSimulation(conversion);
        var continuous = new TotalWarVfxSimulation(conversion);
        for (var frame = 0; frame <= 60; frame++)
        {
            var time = frame / 60f;
            ordinary.Seek(time, At(time), At);
            continuous.SeekContinuousTrail(time, At(time), At);
        }
        Assert.That(Particles(ordinary).Any(particle => Vector3.Distance((Vector3)Read(particle, "Velocity"),
            (Vector3)Read(particle, "MotionInitialVelocity")) > .001f), Is.True,
            "The ordinary route must actually integrate the nonzero damping before it serves as the fallback control.");
        Assert.Multiple(() =>
        {
            Assert.That(Particles(continuous).Select(particle => (Vector3)Read(particle, "Velocity")).ToArray(),
                Is.EqualTo(Particles(ordinary).Select(particle => (Vector3)Read(particle, "Velocity")).ToArray()),
                "The frame route must not discard either positive or negative Bezier damping.");
            Assert.That(Records(continuous), Is.EqualTo(Records(ordinary)),
                "Constant-motion frame cadence must not replace a nonzero Bezier damping trajectory.");
        });
    }

    private static float? NextSample(NativePath path, float time)
    {
        for (var frame = 1; frame < path.Knots.Length; frame++)
            if (frame / 60f > time) return frame / 60f;
        return null;
    }

    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
    private static object Read(object value, string name) => value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static object[] Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Read(simulation, "_particles")).Cast<object>().ToArray();
    private static Birth[] Births(TotalWarVfxSimulation simulation) => Particles(simulation).Select(particle =>
    {
        var root = (Matrix)Read(particle, "Root");
        return new Birth((double)Read(particle, "Birth"), root.Translation,
            Vector3.TransformNormal((Vector3)Read(particle, "MotionInitialVelocity"), root));
    }).OrderBy(particle => particle.Time).ToArray();
    private static object[] Records(TotalWarVfxSimulation simulation) => Particles(simulation).Select(particle =>
        (object)new object[] { Read(particle, "Birth"), Read(particle, "Life"), Read(particle, "Root"), Read(particle, "Position"),
            Read(particle, "Velocity"), Read(particle, "MotionInitialVelocity"), Read(particle, "RandomScale"),
            Read(particle, "RandomColour"), Read(particle, "RandomAlpha"), Read(particle, "InitialAlpha") }).ToArray();
    private static Matrix History(NativePath path, float time)
    {
        var position = path.Knots[0];
        if (time > 0)
        {
            position = path.Knots[^1];
            for (var frame = 1; frame < path.Knots.Length; frame++)
            {
                var end = frame / 60f;
                if (time > end) continue;
                var start = (frame - 1) / 60f;
                position = Vector3.Lerp(path.Knots[frame - 1], path.Knots[frame], (time - start) / (end - start));
                break;
            }
        }
        return Matrix.CreateScale(RootScale) * Matrix.CreateTranslation(position);
    }
    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "DIAG/native-frame-cadence.xml", _ => [1]));
    private static XElement Field(VfxDocument document, string name) => document.Root.Descendants("emitter").Single().Elements().Single(element => (string?)element.Attribute("name") == name);
    private static string VectorCurve(float x, float y, float z) => string.Join('|', new[] { ("x", x), ("y", y), ("z", z) }
        .Select(pair => VfxLibraryVisibilityTests.Constant(pair.Item1, pair.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] Source(int layer, bool trail = true)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source($"""
            <modifier fragment="vs_translation_velocity_v2"><property name="pivot_x" value=".5"/><property name="pivot_y" value="{(layer == 0 ? 1 : .5f)}"/>
            <property name="orientation_mode" value="Camera-facing velocity-aligned"/><property name="velocity_stretch_mul">{VfxLibraryVisibilityTests.Constant("x", layer == 0 ? 10 : 15)}</property><property name="velocity_stretch_cap" value="1000"/></modifier>
            """));
        Field(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        Field(document, "ParticleScale").Value = VectorCurve(.1f, .2f, 1);
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "SpawnSpacing"), new XAttribute("value", layer == 0 ? 1 : .1f)),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", .2f)),
            new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(90, 0, 0)));
        document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", trail ? "true" : "false")))));
        return document.Write();
    }

    private static readonly IReadOnlyDictionary<string, NativePath> Paths = new Dictionary<string, NativePath>
    {
        ["arc60"] = new(
            [
                new(0.0f, 0.0f, 0.0f),
                new(0.13323459029197693f, 0.0f, 0.004442798439413309f),
                new(0.26587724685668945f, 0.0f, 0.017751455307006836f),
                new(0.3973386585712433f, 0.0f, 0.039866846054792404f),
                new(0.5270347595214844f, 0.0f, 0.07069070637226105f),
                new(0.6543893814086914f, 0.0f, 0.1100861057639122f),
                new(0.7788366675376892f, 0.0f, 0.15787801146507263f),
                new(0.899823784828186f, 0.0f, 0.2138540893793106f),
                new(1.0168131589889526f, 0.0f, 0.2777656614780426f),
                new(1.1292849779129028f, 0.0f, 0.34932875633239746f),
                new(1.2367396354675293f, 0.0f, 0.42822548747062683f),
                new(1.3386996984481812f, 0.0f, 0.5141052603721619f),
                new(1.4347121715545654f, 0.0f, 0.6065865755081177f),
                new(1.5243505239486694f, 0.0f, 0.7052585482597351f),
                new(1.607216477394104f, 0.0f, 0.8096827864646912f),
                new(1.6829419136047363f, 0.0f, 0.919395387172699f),
                new(1.751190423965454f, 0.0f, 1.0339088439941406f),
                new(1.8116587400436401f, 0.0f, 1.152714490890503f),
                new(1.864078164100647f, 0.0f, 1.2752845287322998f),
                new(1.908215880393982f, 0.0f, 1.4010741710662842f),
                new(1.943875789642334f, 0.0f, 1.529524803161621f),
                new(1.9708994626998901f, 0.0f, 1.660065770149231f),
                new(1.9891668558120728f, 0.0f, 1.7921168804168701f),
                new(1.9985966682434082f, 0.0f, 1.9250915050506592f),
                new(1.9991471767425537f, 0.0f, 2.058398962020874f),
                new(1.9908158779144287f, 0.0f, 2.1914470195770264f),
                new(1.9736398458480835f, 0.0f, 2.3236446380615234f),
                new(1.947695255279541f, 0.0f, 2.45440411567688f),
                new(1.9130975008010864f, 0.0f, 2.5831449031829834f),
                new(1.8700001239776611f, 0.0f, 2.7092950344085693f),
                new(1.8185948133468628f, 0.0f, 2.832293748855591f),
                new(1.7591098546981812f, 0.0f, 2.951594591140747f),
                new(1.6918095350265503f, 0.0f, 3.0666680335998535f),
                new(1.6169928312301636f, 0.0f, 3.177002191543579f),
                new(1.5349920988082886f, 0.0f, 3.282107353210449f),
                new(1.446171760559082f, 0.0f, 3.381516218185425f),
                new(1.350926399230957f, 0.0f, 3.474787473678589f),
                new(1.2496790885925293f, 0.0f, 3.561506509780884f),
                new(1.1428797245025635f, 0.0f, 3.6412878036499023f),
                new(1.0310027599334717f, 0.0f, 3.713777542114258f),
                new(0.9145452380180359f, 0.0f, 3.778653144836426f),
                new(0.794024646282196f, 0.0f, 3.8356266021728516f),
                new(0.6699762940406799f, 0.0f, 3.8844447135925293f),
                new(0.5429514050483704f, 0.0f, 3.9248905181884766f),
                new(0.41351428627967834f, 0.0f, 3.95678448677063f),
                new(0.28224000334739685f, 0.0f, 3.979984998703003f),
                new(0.14971180260181427f, 0.0f, 3.9943888187408447f),
                new(0.016518453136086464f, 0.0f, 3.999931812286377f),
                new(-0.11674828827381134f, 0.0f, 3.9965896606445312f),
                new(-0.24949634075164795f, 0.0f, 3.984376907348633f),
                new(-0.3811359405517578f, 0.0f, 3.963347911834717f),
                new(-0.5110822319984436f, 0.0f, 3.93359637260437f),
                new(-0.6387578248977661f, 0.0f, 3.895254135131836f),
                new(-0.7635956406593323f, 0.0f, 3.848491668701172f),
                new(-0.8850408792495728f, 0.0f, 3.7935168743133545f),
                new(-1.0025540590286255f, 0.0f, 3.7305736541748047f),
                new(-1.1156131029129028f, 0.0f, 3.6599419116973877f),
                new(-1.2237157821655273f, 0.0f, 3.581935405731201f),
                new(-1.3263816833496094f, 0.0f, 3.496900796890259f),
                new(-1.4231547117233276f, 0.0f, 3.4052155017852783f),
                new(-1.513604998588562f, 0.0f, 3.3072872161865234f),
            ],
            [
                [
                    new(0.04246427118778229, new(0.3374539315700531f, 0.0f, 0.03385833278298378f), new(0.16872695088386536f, 0.0f, 0.01692916452884674f)),
                    new(0.08502521365880966, new(0.6639583110809326f, 0.0f, 0.12559948861598969f), new(0.16325217485427856f, 0.0f, 0.0458705797791481f)),
                    new(0.12766125798225403, new(0.9753828048706055f, 0.0f, 0.2598990797996521f), new(0.15571220219135284f, 0.0f, 0.06714979559183121f)),
                    new(0.17024201154708862, new(1.253265380859375f, 0.0f, 0.4543284773826599f), new(0.13894130289554596f, 0.0f, 0.09721468389034271f)),
                    new(0.21286526322364807, new(1.5021531581878662f, 0.0f, 0.6847115755081177f), new(0.12444392591714859f, 0.0f, 0.11519156396389008f)),
                    new(0.25540781021118164, new(1.6990751028060913f, 0.0f, 0.9608333706855774f), new(0.09846099466085434f, 0.0f, 0.13806091248989105f)),
                    new(0.2979952394962311, new(1.856660008430481f, 0.0f, 1.2611474990844727f), new(0.07879243046045303f, 0.0f, 0.15015704929828644f)),
                    new(0.3404995799064636, new(1.950029730796814f, 0.0f, 1.5871899127960205f), new(0.046684879809617996f, 0.0f, 0.16302117705345154f)),
                    new(0.38305404782295227, new(1.9982800483703613f, 0.0f, 1.9228882789611816f), new(0.02412513829767704f, 0.0f, 0.16784922778606415f)),
                    new(0.4255237281322479, new(1.9774670600891113f, 0.0f, 2.261397361755371f), new(-0.010406486690044403f, 0.0f, 0.16925452649593353f)),
                    new(0.46808910369873047, new(1.8983389139175415f, 0.0f, 2.5911855697631836f), new(-0.039564091712236404f, 0.0f, 0.16489410400390625f)),
                    new(0.5107299089431763, new(1.7761253118515015f, 0.0f, 2.907548189163208f), new(-0.06110680103302002f, 0.0f, 0.15818136930465698f)),
                    new(0.5533164739608765, new(1.5925424098968506f, 0.0f, 3.1927127838134766f), new(-0.09179145842790604f, 0.0f, 0.14258232712745667f)),
                    new(0.5959451198577881, new(1.3719130754470825f, 0.0f, 3.450286626815796f), new(-0.11031465977430344f, 0.0f, 0.1287868767976761f)),
                    new(0.6384931802749634, new(1.1035730838775635f, 0.0f, 3.6576876640319824f), new(-0.13416999578475952f, 0.0f, 0.10370051860809326f)),
                    new(0.6810855269432068, new(0.8095422387123108f, 0.0f, 3.826706647872925f), new(-0.14701542258262634f, 0.0f, 0.08450944721698761f)),
                    new(0.7235949039459229, new(0.4873296320438385f, 0.0f, 3.932539463043213f), new(-0.16110631823539734f, 0.0f, 0.05291636660695076f)),
                    new(0.7661536335945129, new(0.15373292565345764f, 0.0f, 3.993652105331421f), new(-0.16679835319519043f, 0.0f, 0.03055635653436184f)),
                    new(0.8086279034614563, new(-0.18532565236091614f, 0.0f, 3.9858529567718506f), new(-0.1695292741060257f, 0.0f, -0.00389956240542233f)),
                    new(0.8511970639228821, new(-0.5179002285003662f, 0.0f, 3.9194023609161377f), new(-0.16628728806972504f, 0.0f, -0.03322532773017883f)),
                    new(0.8938418626785278, new(-0.8387137651443481f, 0.0f, 3.809401512145996f), new(-0.16040678322315216f, 0.0f, -0.055000413209199905f)),
                    new(0.9364333152770996, new(-1.1307071447372437f, 0.0f, 3.6368865966796875f), new(-0.14599668979644775f, 0.0f, -0.0862574577331543f)),
                    new(0.9790664911270142, new(-1.3965485095977783f, 0.0f, 3.4262924194335938f), new(-0.13292068243026733f, 0.0f, -0.10529708862304688f)),
                ],
                [
                    new(0.06377910822629929, new(0.5042070746421814f, 0.0f, 0.06762884557247162f), new(0.1680690497159958f, 0.0f, 0.022542953491210938f)),
                    new(0.12764811515808105, new(0.9749137163162231f, 0.0f, 0.26058948040008545f), new(0.15690217912197113f, 0.0f, 0.06432019919157028f)),
                    new(0.1916116327047348, new(1.3814038038253784f, 0.0f, 0.5664721727371216f), new(0.13549672067165375f, 0.0f, 0.1019609123468399f)),
                    new(0.2556735873222351, new(1.6970288753509521f, 0.0f, 0.965444803237915f), new(0.1052083820104599f, 0.0f, 0.13299089670181274f)),
                    new(0.31983718276023865, new(1.900977373123169f, 0.0f, 1.4314956665039062f), new(0.06798281520605087f, 0.0f, 0.15535031259059906f)),
                    new(0.38410484790802, new(1.9796814918518066f, 0.0f, 1.9340929985046387f), new(0.026234714314341545f, 0.0f, 0.16753245890140533f)),
                    new(0.44841092824935913, new(1.9484666585922241f, 0.0f, 2.441856622695923f), new(-0.010404962114989758f, 0.0f, 0.1692545861005783f)),
                    new(0.5122670531272888, new(1.771315336227417f, 0.0f, 2.9187378883361816f), new(-0.05905042961239815f, 0.0f, 0.1589604616165161f)),
                    new(0.5761889219284058, new(1.4788590669631958f, 0.0f, 3.3349921703338623f), new(-0.09748545289039612f, 0.0f, 0.13875149190425873f)),
                    new(0.6402050256729126, new(1.0904347896575928f, 0.0f, 3.6635115146636963f), new(-0.12947478890419006f, 0.0f, 0.10950645059347153f)),
                    new(0.7043210864067078, new(0.6313486099243164f, 0.0f, 3.882688045501709f), new(-0.1530287265777588f, 0.0f, 0.0730588287115097f)),
                    new(0.7685401439666748, new(0.13163092732429504f, 0.0f, 3.977980613708496f), new(-0.16657257080078125f, 0.0f, 0.03176421299576759f)),
                    new(0.8327960968017578, new(-0.37688443064689636f, 0.0f, 3.9634692668914795f), new(-0.16950511932373047f, 0.0f, -0.004837126471102238f)),
                    new(0.8966009020805359, new(-0.8593391180038452f, 0.0f, 3.8021128177642822f), new(-0.16081824898719788f, 0.0f, -0.053785499185323715f)),
                    new(0.9604873657226562, new(-1.2851918935775757f, 0.0f, 3.523818254470825f), new(-0.14195093512535095f, 0.0f, -0.09276484698057175f)),
                ],
            ]),
        ["right-angle60-off-boundary"] = new(
            [
                new(0.0f, 0.0f, 0.0f),
                new(0.13333334028720856f, 0.0f, 0.0f),
                new(0.2666666805744171f, 0.0f, 0.0f),
                new(0.4000000059604645f, 0.0f, 0.0f),
                new(0.5333333611488342f, 0.0f, 0.0f),
                new(0.6666666865348816f, 0.0f, 0.0f),
                new(0.800000011920929f, 0.0f, 0.0f),
                new(0.9333333373069763f, 0.0f, 0.0f),
                new(1.0666667222976685f, 0.0f, 0.0f),
                new(1.2000000476837158f, 0.0f, 0.0f),
                new(1.3333333730697632f, 0.0f, 0.0f),
                new(1.4666666984558105f, 0.0f, 0.0f),
                new(1.600000023841858f, 0.0f, 0.0f),
                new(1.7333333492279053f, 0.0f, 0.0f),
                new(1.8666666746139526f, 0.0f, 0.0f),
                new(2.0f, 0.0f, 0.0f),
                new(2.133333444595337f, 0.0f, 0.0f),
                new(2.2666666507720947f, 0.0f, 0.0f),
                new(2.4000000953674316f, 0.0f, 0.0f),
                new(2.5333333015441895f, 0.0f, 0.0f),
                new(2.6666667461395264f, 0.0f, 0.0f),
                new(2.799999952316284f, 0.0f, 0.0f),
                new(2.933333396911621f, 0.0f, 0.0f),
                new(3.066666603088379f, 0.0f, 0.0f),
                new(3.200000047683716f, 0.0f, 0.0f),
                new(3.3333332538604736f, 0.0f, 0.0f),
                new(3.4666666984558105f, 0.0f, 0.0f),
                new(3.5999999046325684f, 0.0f, 0.0f),
                new(3.7333333492279053f, 0.0f, 0.0f),
                new(3.866666555404663f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.039999961853027f, 0.0f, 0.09333333373069763f),
                new(4.039999961853027f, 0.0f, 0.2266666740179062f),
                new(4.039999961853027f, 0.0f, 0.36000001430511475f),
                new(4.039999961853027f, 0.0f, 0.4933333396911621f),
                new(4.039999961853027f, 0.0f, 0.6266666650772095f),
                new(4.039999961853027f, 0.0f, 0.7599999904632568f),
                new(4.039999961853027f, 0.0f, 0.8933333158493042f),
                new(4.039999961853027f, 0.0f, 1.0266666412353516f),
                new(4.039999961853027f, 0.0f, 1.159999966621399f),
                new(4.039999961853027f, 0.0f, 1.2933332920074463f),
                new(4.039999961853027f, 0.0f, 1.4266666173934937f),
                new(4.039999961853027f, 0.0f, 1.559999942779541f),
                new(4.039999961853027f, 0.0f, 1.693333387374878f),
                new(4.039999961853027f, 0.0f, 1.8266667127609253f),
                new(4.039999961853027f, 0.0f, 1.9600000381469727f),
                new(4.039999961853027f, 0.0f, 2.0933332443237305f),
                new(4.039999961853027f, 0.0f, 2.2266666889190674f),
                new(4.039999961853027f, 0.0f, 2.359999895095825f),
                new(4.039999961853027f, 0.0f, 2.493333339691162f),
                new(4.039999961853027f, 0.0f, 2.626666784286499f),
                new(4.039999961853027f, 0.0f, 2.759999990463257f),
                new(4.039999961853027f, 0.0f, 2.8933334350585938f),
                new(4.039999961853027f, 0.0f, 3.0266666412353516f),
                new(4.039999961853027f, 0.0f, 3.1600000858306885f),
                new(4.039999961853027f, 0.0f, 3.2933332920074463f),
                new(4.039999961853027f, 0.0f, 3.426666736602783f),
                new(4.039999961853027f, 0.0f, 3.559999942779541f),
                new(4.039999961853027f, 0.0f, 3.693333387374878f),
                new(4.039999961853027f, 0.0f, 3.8266665935516357f),
                new(4.039999961853027f, 0.0f, 3.9600000381469727f),
            ],
            [
                [
                    new(0.042393531650304794, new(0.33914825320243835f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.08478706330060959, new(0.6782965064048767f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.12718060612678528, new(1.0174448490142822f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.16957414150238037, new(1.356593132019043f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.21196767687797546, new(1.6957414150238037f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.25436121225357056, new(2.0348896980285645f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.29675474762916565, new(2.374037981033325f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.33914828300476074, new(2.713186264038086f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.38154181838035583, new(3.0523345470428467f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.4239353537559509, new(3.3914828300476074f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.466328889131546, new(3.730631113052368f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.5255813598632812, new(4.004208087921143f, 0.0f, 0.20044271647930145f), new(0.13678838312625885f, 0.0f, 0.10022136569023132f)),
                    new(0.5713736414909363, new(4.032588005065918f, 0.0f, 0.5384014248847961f), new(0.014189931564033031f, 0.0f, 0.16897937655448914f)),
                    new(0.6146430969238281, new(4.039669036865234f, 0.0f, 0.8774757385253906f), new(0.0035404034424573183f, 0.0f, 0.16953715682029724f)),
                    new(0.6570703983306885, new(4.039938926696777f, 0.0f, 1.2166237831115723f), new(0.00013494116137735546f, 0.0f, 0.1695740818977356f)),
                    new(0.6994714140892029, new(4.039999008178711f, 0.0f, 1.5557719469070435f), new(3.0141824026941322e-05f, 0.0f, 0.16957412660121918f)),
                    new(0.7418650388717651, new(4.039999961853027f, 0.0f, 1.8949202299118042f), new(4.0006739254749846e-07f, 0.0f, 0.16957411170005798f)),
                    new(0.7842585444450378, new(4.039999961853027f, 0.0f, 2.2340683937072754f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(0.8266520500183105, new(4.039999961853027f, 0.0f, 2.573216676712036f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(0.869045615196228, new(4.039999961853027f, 0.0f, 2.912364959716797f), new(0.0f, 0.0f, 0.16957411170005798f)),
                    new(0.9114391803741455, new(4.039999961853027f, 0.0f, 3.2515132427215576f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(0.9538326859474182, new(4.039999961853027f, 0.0f, 3.5906615257263184f), new(0.0f, 0.0f, 0.16957411170005798f)),
                    new(0.9962261915206909, new(4.039999961853027f, 0.0f, 3.929809808731079f), new(0.0f, 0.0f, 0.16957412660121918f)),
                ],
                [
                    new(0.06359029561281204, new(0.5087223649024963f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.12718060612678528, new(1.0174448490142822f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.19077089428901672, new(1.5261671543121338f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.25436121225357056, new(2.0348896980285645f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.317951500415802, new(2.543612003326416f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.38154178857803345, new(3.0523343086242676f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.4451320767402649, new(3.561056613922119f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.5298128128051758, new(4.020883083343506f, 0.0f, 0.21761928498744965f), new(0.15327544510364532f, 0.0f, 0.07253976166248322f)),
                    new(0.5956035852432251, new(4.038802623748779f, 0.0f, 0.7260259389877319f), new(0.005973140709102154f, 0.0f, 0.16946889460086823f)),
                    new(0.6593279838562012, new(4.039876461029053f, 0.0f, 1.2347471714019775f), new(0.00035789611865766346f, 0.0f, 0.16957375407218933f)),
                    new(0.7229315638542175, new(4.039982795715332f, 0.0f, 1.7434695959091187f), new(3.538072996889241e-05f, 0.0f, 0.16957412660121918f)),
                    new(0.7865236401557922, new(4.039997100830078f, 0.0f, 2.2521917819976807f), new(4.721475761471083e-06f, 0.0f, 0.16957412660121918f)),
                    new(0.8501142263412476, new(4.039999485015869f, 0.0f, 2.7609143257141113f), new(7.567056172774755e-07f, 0.0f, 0.16957412660121918f)),
                    new(0.9137045741081238, new(4.039999961853027f, 0.0f, 3.269636631011963f), new(1.5187146118478267e-07f, 0.0f, 0.16957412660121918f)),
                    new(0.977294921875, new(4.039999961853027f, 0.0f, 3.7783589363098145f), new(0.0f, 0.0f, 0.16957412660121918f)),
                ],
            ]),
        ["stop-restart60"] = new(
            [
                new(0.0f, 0.0f, 0.0f),
                new(0.13333334028720856f, 0.0f, 0.0f),
                new(0.2666666805744171f, 0.0f, 0.0f),
                new(0.4000000059604645f, 0.0f, 0.0f),
                new(0.5333333611488342f, 0.0f, 0.0f),
                new(0.6666666865348816f, 0.0f, 0.0f),
                new(0.800000011920929f, 0.0f, 0.0f),
                new(0.9333333373069763f, 0.0f, 0.0f),
                new(1.0666667222976685f, 0.0f, 0.0f),
                new(1.2000000476837158f, 0.0f, 0.0f),
                new(1.3333333730697632f, 0.0f, 0.0f),
                new(1.4666666984558105f, 0.0f, 0.0f),
                new(1.600000023841858f, 0.0f, 0.0f),
                new(1.7333333492279053f, 0.0f, 0.0f),
                new(1.8666666746139526f, 0.0f, 0.0f),
                new(2.0f, 0.0f, 0.0f),
                new(2.133333444595337f, 0.0f, 0.0f),
                new(2.2666666507720947f, 0.0f, 0.0f),
                new(2.4000000953674316f, 0.0f, 0.0f),
                new(2.5333333015441895f, 0.0f, 0.0f),
                new(2.6666667461395264f, 0.0f, 0.0f),
                new(2.799999952316284f, 0.0f, 0.0f),
                new(2.933333396911621f, 0.0f, 0.0f),
                new(3.066666603088379f, 0.0f, 0.0f),
                new(3.200000047683716f, 0.0f, 0.0f),
                new(3.3333332538604736f, 0.0f, 0.0f),
                new(3.4666666984558105f, 0.0f, 0.0f),
                new(3.5999999046325684f, 0.0f, 0.0f),
                new(3.7333333492279053f, 0.0f, 0.0f),
                new(3.866666555404663f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.0f),
                new(4.0f, 0.0f, 0.13333334028720856f),
                new(4.0f, 0.0f, 0.2666666805744171f),
                new(4.0f, 0.0f, 0.4000000059604645f),
                new(4.0f, 0.0f, 0.5333333611488342f),
                new(4.0f, 0.0f, 0.6666666865348816f),
                new(4.0f, 0.0f, 0.800000011920929f),
                new(4.0f, 0.0f, 0.9333333373069763f),
                new(4.0f, 0.0f, 1.0666667222976685f),
                new(4.0f, 0.0f, 1.2000000476837158f),
                new(4.0f, 0.0f, 1.3333333730697632f),
                new(4.0f, 0.0f, 1.4666666984558105f),
                new(4.0f, 0.0f, 1.600000023841858f),
                new(4.0f, 0.0f, 1.7333333492279053f),
                new(4.0f, 0.0f, 1.8666666746139526f),
                new(4.0f, 0.0f, 2.0f),
                new(4.0f, 0.0f, 2.133333444595337f),
                new(4.0f, 0.0f, 2.2666666507720947f),
                new(4.0f, 0.0f, 2.4000000953674316f),
                new(4.0f, 0.0f, 2.5333333015441895f),
                new(4.0f, 0.0f, 2.6666667461395264f),
                new(4.0f, 0.0f, 2.799999952316284f),
                new(4.0f, 0.0f, 2.933333396911621f),
                new(4.0f, 0.0f, 3.066666603088379f),
                new(4.0f, 0.0f, 3.200000047683716f),
                new(4.0f, 0.0f, 3.3333332538604736f),
                new(4.0f, 0.0f, 3.4666666984558105f),
                new(4.0f, 0.0f, 3.5999999046325684f),
                new(4.0f, 0.0f, 3.7333333492279053f),
                new(4.0f, 0.0f, 3.866666555404663f),
                new(4.0f, 0.0f, 4.0f),
            ],
            [
                [
                    new(0.042393531650304794, new(0.33914825320243835f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.08478706330060959, new(0.6782965064048767f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.12718060612678528, new(1.0174448490142822f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.16957414150238037, new(1.356593132019043f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.21196767687797546, new(1.6957414150238037f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.25436121225357056, new(2.0348896980285645f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.29675474762916565, new(2.374037981033325f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.33914828300476074, new(2.713186264038086f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.38154181838035583, new(3.0523345470428467f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.4239353537559509, new(3.3914828300476074f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(0.466328889131546, new(3.730631113052368f, 0.0f, 0.0f), new(0.16957411170005798f, 0.0f, 0.0f)),
                    new(1.421039342880249, new(3.971651077270508f, 0.0f, 0.23860207200050354f), new(0.12050995230674744f, 0.0f, 0.11930104345083237f)),
                    new(1.549341082572937, new(3.9940624237060547f, 0.0f, 0.5770090818405151f), new(0.011205636896193027f, 0.0f, 0.1692034751176834f)),
                    new(1.6134124994277954, new(3.9997129440307617f, 0.0f, 0.9161102771759033f), new(0.0028252906631678343f, 0.0f, 0.1695505827665329f)),
                    new(1.6567012071609497, new(3.999946355819702f, 0.0f, 1.2552584409713745f), new(0.0001166696019936353f, 0.0f, 0.1695740818977356f)),
                    new(1.699297547340393, new(3.9999990463256836f, 0.0f, 1.5944067239761353f), new(2.638690966705326e-05f, 0.0f, 0.16957412660121918f)),
                    new(1.7416938543319702, new(3.999999761581421f, 0.0f, 1.933555006980896f), new(3.987208572198142e-07f, 0.0f, 0.16957412660121918f)),
                    new(1.7840877771377563, new(4.0f, 0.0f, 2.2727034091949463f), new(8.667606010703821e-08f, 0.0f, 0.16957412660121918f)),
                    new(1.8264814615249634, new(4.0f, 0.0f, 2.611851692199707f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(1.8688750267028809, new(4.0f, 0.0f, 2.9509999752044678f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(1.9112684726715088, new(4.0f, 0.0f, 3.2901482582092285f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(1.9536620378494263, new(4.0f, 0.0f, 3.6292965412139893f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(1.9960556030273438, new(4.0f, 0.0f, 3.96844482421875f), new(0.0f, 0.0f, 0.16957412660121918f)),
                ],
                [
                    new(0.06359029561281204, new(0.5087223649024963f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.12718060612678528, new(1.0174448490142822f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.19077089428901672, new(1.5261671543121338f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.25436121225357056, new(2.0348896980285645f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.317951500415802, new(2.543612003326416f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.38154178857803345, new(3.0523343086242676f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(0.4451320767402649, new(3.561056613922119f, 0.0f, 0.0f), new(0.16957412660121918f, 0.0f, 0.0f)),
                    new(1.5230040550231934, new(3.995833396911621f, 0.0f, 0.2641354501247406f), new(0.14492560923099518f, 0.0f, 0.0880451425909996f)),
                    new(1.5960978269577026, new(3.999788761138916f, 0.0f, 0.7728424072265625f), new(0.0013184798881411552f, 0.0f, 0.1695689857006073f)),
                    new(1.6601487398147583, new(3.9999804496765137f, 0.0f, 1.281564712524414f), new(6.390940689016134e-05f, 0.0f, 0.16957412660121918f)),
                    new(1.7237797975540161, new(3.99999737739563f, 0.0f, 1.7902870178222656f), new(5.666070592269534e-06f, 0.0f, 0.16957412660121918f)),
                    new(1.7873750925064087, new(3.999999523162842f, 0.0f, 2.2990095615386963f), new(7.29401847365807e-07f, 0.0f, 0.16957412660121918f)),
                    new(1.8509663343429565, new(4.0f, 0.0f, 2.807732105255127f), new(1.2747312894134666e-07f, 0.0f, 0.16957412660121918f)),
                    new(1.9145567417144775, new(4.0f, 0.0f, 3.3164544105529785f), new(0.0f, 0.0f, 0.16957412660121918f)),
                    new(1.9781471490859985, new(4.0f, 0.0f, 3.82517671585083f), new(0.0f, 0.0f, 0.16957412660121918f)),
                ],
            ]),
    };
}
