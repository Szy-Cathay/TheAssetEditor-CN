#nullable enable

using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxDistanceTrailBirthAppearanceTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const float Duration = .1f;
    private const float End = 17f / 120;

    [OneTimeSetUp]
    public void RecordLoadedCore()
    {
        var path = typeof(TotalWarVfxSimulation).Assembly.Location;
        var hash = System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { CoreModule = path, CoreSha256 = hash }));
        var expected = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_CORE_SHA256");
        if (!string.IsNullOrWhiteSpace(expected)) Assert.That(hash, Is.EqualTo(expected).IgnoreCase);
    }

    [TestCase(56f, 10f, false, false)]
    [TestCase(56f, 10f, false, true)]
    [TestCase(56f, 10f, true, false)]
    [TestCase(56f, 10f, true, true)]
    [TestCase(56f, 15f, false, false)]
    [TestCase(56f, 15f, false, true)]
    [TestCase(56f, 15f, true, false)]
    [TestCase(56f, 15f, true, true)]
    [TestCase(120f, 10f, false, false)]
    [TestCase(120f, 10f, false, true)]
    [TestCase(120f, 10f, true, false)]
    [TestCase(120f, 10f, true, true)]
    [TestCase(120f, 15f, false, false)]
    [TestCase(120f, 15f, false, true)]
    [TestCase(120f, 15f, true, false)]
    [TestCase(120f, 15f, true, true)]
    public void DIAG_OfficialTrailParameters_SampleInitialAppearanceAtEachInterpolatedBirth(
        float movementSpeed, float stretch, bool initialLoop, bool emissionLoop)
    {
        var source = Source(stretch, initialLoop, emissionLoop);
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        Matrix At(float time) => Matrix.CreateTranslation(movementSpeed * time, 0, 0);
        simulation.Seek(End, At(End), At);
        var particles = Particles(simulation);
        var segmentLength = .2f * .2f * stretch;
        var count = (int)Math.Floor(movementSpeed * (17d / 120) / segmentLength);
        Assert.That(particles, Has.Length.EqualTo(count));
        Assert.That(particles.Any(particle => Birth(particle) > Duration), Is.True);
        Assert.That(particles.Any(particle => Math.Abs(Birth(particle) * 120 - Math.Round(Birth(particle) * 120)) > .01), Is.True,
            "The control must contain births strictly inside a tick, not only at its end.");

        var constant = new TotalWarVfxSimulation(Convert(Source(stretch, initialLoop, emissionLoop, dynamic: false)));
        constant.Seek(End, At(End), At);
        Assert.That(UnrelatedRecords(simulation), Is.EqualTo(UnrelatedRecords(constant)),
            "Changing the two initial appearance curves must preserve birth, lifetime, position, velocity and random draws.");
        Assert.Multiple(() =>
        {
            for (var index = 0; index < particles.Length; index++)
            {
                var particle = particles[index];
                var expectedBirth = (index + 1d) * segmentLength / movementSpeed;
                Assert.That(Birth(particle), Is.EqualTo(expectedBirth).Within(.000001), $"Birth {index}");
                var position = ((Matrix)Read(particle, "Root")).Translation;
                Assert.That(position.X, Is.EqualTo((index + 1) * segmentLength).Within(.00001), $"Position {index}");
                Assert.That(Vector3.Distance((Vector3)Read(particle, "MotionInitialVelocity"), new Vector3(.2f, 0, 0)), Is.LessThan(.000001));
                Assert.That((float)Read(particle, "Life"), Is.EqualTo(2));
                AssertInitialAppearance(particle, Birth(particle), initialLoop);
            }
        });
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DIAG_ContinuousPreviewDoesNotReplaceTheIndependentInitialParameterLoop(bool initialLoop, bool emissionLoop)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(10, initialLoop, emissionLoop, infinite: false)));
        static Matrix At(float time) => Matrix.CreateTranslation(120 * time, 0, 0);
        simulation.SeekContinuousTrail(End, At(End), At);
        var particles = Particles(simulation);
        Assert.That(particles, Has.Length.EqualTo(42));
        Assert.That(particles.Any(particle => Birth(particle) > Duration), Is.True);
        Assert.Multiple(() =>
        {
            foreach (var particle in particles) AssertInitialAppearance(particle, Birth(particle), initialLoop);
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DIAG_OrdinaryTimeEmissionUsesItsActualBirthAndTheIndependentInitialParameterLoop(bool initialLoop, bool emissionLoop)
    {
        var source = Source(10, initialLoop, emissionLoop, distance: false);
        var simulation = new TotalWarVfxSimulation(Convert(source));
        static Matrix At(float time) => Matrix.CreateTranslation(56 * time, 0, 0);
        simulation.Seek(End, At(End), At);
        var constant = new TotalWarVfxSimulation(Convert(Source(10, initialLoop, emissionLoop, dynamic: false, distance: false)));
        constant.Seek(End, At(End), At);
        Assert.That(UnrelatedRecords(simulation), Is.EqualTo(UnrelatedRecords(constant)));
        var particles = Particles(simulation);
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.Any(particle => Birth(particle) > Duration), Is.True);
        Assert.Multiple(() =>
        {
            foreach (var particle in particles) AssertInitialAppearance(particle, Birth(particle), initialLoop);
        });
    }

    [Test]
    public void DIAG_DistanceBirthPhaseIsRelativeToTheEmitterStart()
    {
        const float start = .025f;
        var source = VfxDocument.Read(Source(10, false, false));
        Set(source, "StartTime", new XElement("ranged_property", new XAttribute("name", "StartTime"),
            new XAttribute("min", start), new XAttribute("max", start)));
        var simulation = new TotalWarVfxSimulation(Convert(source.Write()));
        static Matrix At(float time) => Matrix.CreateTranslation(120 * time, 0, 0);
        simulation.Seek(.075f, At(.075f), At);
        var particles = Particles(simulation);
        Assert.That(particles, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var particle in particles) AssertInitialAppearance(particle, Birth(particle), false, start);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void DIAG_GpuBirthInputsUseNativeFloatLoopBoundariesWithoutALifetimeEpsilon(int boundary)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(10, true, false)));
        simulation.Seek(0, Matrix.Identity, _ => Matrix.Identity);
        var below = MathF.BitDecrement(Duration);
        var above = MathF.BitIncrement(Duration);
        var birth = boundary switch
        {
            0 => (double)below,
            1 => Duration,
            2 => above,
            _ => Duration - ((double)above - Duration) * .25,
        };
        // Managed interpolation has double storage, but the native producer's writer receives float32.
        var expectedPhase = boundary switch
        {
            0 => below / Duration,
            2 => (above - Duration) / Duration,
            _ => 0,
        };
        typeof(TotalWarVfxSimulation).GetMethod("Spawn", Flags)!.Invoke(simulation,
            [0, .83d, (Matrix?)Matrix.Identity, (Vector3?)Vector3.UnitX, (double?)birth]);
        var particle = Particles(simulation).Single();
        Assert.That(Birth(particle), Is.EqualTo(birth));
        Assert.That((float)Read(particle, "InitialAlpha"), Is.EqualTo(expectedPhase).Within(.000001));
        Assert.That(((Vector3)Read(particle, "InitialScale")).X, Is.EqualTo(1 + expectedPhase).Within(.000001));
        Assert.That((float)Read(particle, "Life"), Is.EqualTo(2));
    }

    [TestCase("ParticleLifeTimeLoopAnim")]
    [TestCase("ParticleInitialSpeedLoopAnim")]
    [TestCase("SpawnAreaLoopAnim")]
    public void DIAG_InitialParameterLoopDoesNotConsumeTheOtherUncalibratedLoopFlags(string field)
    {
        var source = VfxDocument.Read(Source(10, true, false));
        Set(source, field, new XElement("property", new XAttribute("name", field), new XAttribute("value", "true")));
        var conversion = Convert(source.Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Kind, Is.Zero);
        Assert.That(conversion.Emitters[0].InitialParameterLoop, Is.True);
        Assert.That(conversion.Diagnostics.Any(diagnostic => diagnostic.Field == "ParticleInitialParamLoopAnim"
            && diagnostic.ReasonKey == "Vfx.Preview.EmitterTimelineCandidate"), Is.False);
        Assert.That(conversion.Diagnostics.Any(diagnostic => diagnostic.Field == field
            && diagnostic.ReasonKey == "Vfx.Preview.EmitterTimelineCandidate"), Is.True);
    }

    [Test]
    public void DIAG_DecalRetainsItsInitialLoopCandidateAndExistingCpuInitialSampling()
    {
        static VfxConversion Decal(bool initialLoop)
        {
            var document = VfxDocument.Read(Source(10, initialLoop, false, distance: false));
            document.Root.Descendants("modifier").Single(node => (string?)node.Attribute("fragment") == "vs_translation_velocity_v2").Remove();
            document.Root.Descendants("effect_render_stack").Single().Add(new XElement("property", new XAttribute("name", "scene_view_type"),
                new XAttribute("value", "DEFERRED_DECAL")));
            return Convert(document.Write());
        }
        var conversion = Decal(true);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Kind, Is.EqualTo(2));
        Assert.That(conversion.Emitters[0].InitialParameterLoop, Is.True);
        Assert.That(conversion.Diagnostics.Any(diagnostic => diagnostic.Field == "ParticleInitialParamLoopAnim"
            && diagnostic.ReasonKey == "Vfx.Preview.EmitterTimelineCandidate"), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion);
        var control = new TotalWarVfxSimulation(Decal(false));
        simulation.Seek(End, Matrix.Identity); control.Seek(End, Matrix.Identity);
        Assert.That(UnrelatedRecords(simulation), Is.EqualTo(UnrelatedRecords(control)));
        var particles = Particles(simulation); var unchanged = Particles(control);
        Assert.That(particles.Any(particle => Birth(particle) > Duration), Is.True);
        for (var index = 0; index < particles.Length; index++)
        {
            Assert.That(Read(particles[index], "InitialAlpha"), Is.EqualTo(Read(unchanged[index], "InitialAlpha")));
            Assert.That(Read(particles[index], "InitialScale"), Is.EqualTo(Read(unchanged[index], "InitialScale")));
        }
    }

    private static void AssertInitialAppearance(object particle, double birthTime, bool loop, float start = 0)
    {
        // Native mulss/addss birth input, subss, fmodf and divss are all float32; no life-unary epsilon applies.
        var emitterAgeAtBirth = (float)birthTime - start;
        var phase = loop ? emitterAgeAtBirth % Duration / Duration : Math.Clamp(emitterAgeAtBirth / Duration, 0, 1);
        Assert.That((float)Read(particle, "InitialAlpha"), Is.EqualTo(phase).Within(.000001), $"InitialAlpha birth={Birth(particle):R}");
        var scale = (Vector3)Read(particle, "InitialScale");
        Assert.That(scale.X, Is.EqualTo(1 + phase).Within(.000001), $"InitialScale.X birth={Birth(particle):R}");
        Assert.That(scale.Y, Is.EqualTo(1));
        Assert.That(scale.Z, Is.EqualTo(1));
    }

    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "DIAG/blowpipe-initial-appearance.xml", _ => [1]));
    private static object Read(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target)
        ?? target.GetType().GetProperty(name, Flags)?.GetValue(target) ?? throw new InvalidOperationException(name);
    private static double Birth(object particle) => (double)Read(particle, "Birth");
    private static object[] Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Read(simulation, "_particles")).Cast<object>().ToArray();
    private static object[] UnrelatedRecords(TotalWarVfxSimulation simulation) => Particles(simulation).Select(particle => (object)new[]
    {
        Read(particle, "Birth"), Read(particle, "Life"), Read(particle, "Root"), Read(particle, "Position"),
        Read(particle, "Velocity"), Read(particle, "MotionInitialVelocity"), Read(particle, "RandomScale"),
        Read(particle, "RandomColour"), Read(particle, "RandomAlpha"), Read(particle, "Orientation"), Read(particle, "Cell")
    }).ToArray();

    private static byte[] Source(float stretch, bool initialLoop, bool emissionLoop, bool dynamic = true, bool infinite = true, bool distance = true)
    {
        // DIAG preserves the official q1 rifle/glow speed, Y size and stretch; only initial X/alpha and timing isolate the producer contract.
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source($"""
            <modifier fragment="vs_translation_velocity_v2"><property name="pivot_x" value=".5"/><property name="pivot_y" value="1"/>
            <property name="orientation_mode" value="Camera-facing velocity-aligned"/>
            <property name="velocity_stretch_mul">{VfxLibraryVisibilityTests.Constant("x", stretch)}</property>
            <property name="velocity_stretch_cap" value="1000"/></modifier>
            """));
        Set(document, "EmissionRateCurve", Property("EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 120)));
        Set(document, "LifeTime", new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", Duration), new XAttribute("max", Duration)));
        Set(document, "InfiniteLife", new XElement("property", new XAttribute("name", "InfiniteLife"), new XAttribute("value", infinite ? "true" : "false")));
        Set(document, "ParticleLifeTime", Property("ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 2)));
        Set(document, "ParticleScale", Property("ParticleScale", VectorCurve(VfxLibraryVisibilityTests.Constant("x", .1f), .2f, 1)));
        Set(document, "ParticleInitialScale", Property("ParticleInitialScale", VectorCurve(dynamic ? Linear("x", 1, 2) : VfxLibraryVisibilityTests.Constant("x", 1), 1, 1)));
        Set(document, "ParticleInitialAlpha", Property("ParticleInitialAlpha", dynamic ? Linear("x", 0, 1) : VfxLibraryVisibilityTests.Constant("x", 1)));
        Set(document, "ParticleInitialSpeed", Property("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", .2f)));
        Set(document, "EmitterOrientation", Property("EmitterOrientation", VectorCurve(VfxLibraryVisibilityTests.Constant("x", 90), 0, 0)));
        Set(document, "SpawnSpacing", new XElement("property", new XAttribute("name", "SpawnSpacing"), new XAttribute("value", distance ? 1 : 0)));
        foreach (var (name, value) in new[] { ("ParticleInitialParamLoopAnim", initialLoop), ("EmissionLoopAnim", emissionLoop) })
            Set(document, name, new XElement("property", new XAttribute("name", name), new XAttribute("value", value ? "true" : "false")));
        document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", distance ? "true" : "false")))));
        return document.Write();
    }

    private static XElement Property(string name, string curve) => new("property", new XAttribute("name", name), curve);
    private static void Set(VfxDocument document, string name, XElement replacement)
    {
        var emitter = document.Root.Descendants("emitter").Single();
        var previous = emitter.Elements().SingleOrDefault(node => (string?)node.Attribute("name") == name);
        if (previous == null) emitter.Add(replacement); else previous.ReplaceWith(replacement);
    }

    private static string Linear(string channel, float start, float end) => FormattableString.Invariant(
        $"{channel}_min,0,1;0,{start},linear,linear,0,0,0,0,0;1,{end},linear,linear,0,0,0,0,0|{channel}_max,0,1;0,{start},linear,linear,0,0,0,0,0;1,{end},linear,linear,0,0,0,0,0:{channel},1;0,1");
    private static string VectorCurve(string x, float y, float z)
    {
        var channels = x.Split(':')[0];
        if (!channels.Contains('|')) channels += "|" + channels.Replace("x_min,", "x_max,", StringComparison.Ordinal);
        return channels + "|" + VfxLibraryVisibilityTests.Constant("y", y).Split(':')[0]
            + "|" + VfxLibraryVisibilityTests.Constant("z", z).Split(':')[0] + ":x,1;0,1|y,0;2,2|z,0;3,3";
    }
}
