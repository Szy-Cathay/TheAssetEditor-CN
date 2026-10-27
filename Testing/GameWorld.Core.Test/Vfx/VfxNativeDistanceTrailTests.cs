#nullable enable

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeDistanceTrailTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.1f)]
    [TestCase(1f)]
    [TestCase(7f)]
    public void NativeTrail_UsesRenderedSegmentLengthAndMotionDirectionIndependentlyOfPositiveSpacingValue(float authoredSpacing)
    {
        var source = Source(authoredSpacing, true);
        var simulation = new TotalWarVfxSimulation(Convert(source));
        const float scale = .8478705883026123f;
        static Matrix At(float time) => Matrix.CreateScale(scale) * Matrix.CreateTranslation(8 * time, 0, 0);
        simulation.Seek(1, At(1), At);
        var particles = Particles(simulation);
        Assert.That(particles, Has.Length.EqualTo(23));
        const float nativeSpacing = .2f * .2f * 10 * scale;
        var births = particles.Select(particle => ((Matrix)Read(particle, "Root")!).Translation.X).Order().ToArray();
        Assert.That(births, Is.EqualTo(Enumerable.Range(1, 23).Select(index => index * nativeSpacing)).Within(.00001));
        foreach (var particle in particles)
        {
            var root = (Matrix)Read(particle, "Root")!;
            var velocity = Vector3.TransformNormal((Vector3)Read(particle, "MotionInitialVelocity")!, root);
            Assert.That(Vector3.Distance(velocity, new Vector3(.2f * scale, 0, 0)), Is.LessThan(.000001));
            Assert.That((float)Read(particle, "Life")!, Is.EqualTo(2));
        }
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void NativeTrail_TurnsUseTheUnconsumedChordAndInterpolateEachBirthTime()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(1, true)));
        static Matrix At(float time) => Matrix.CreateTranslation(time <= .5f ? new Vector3(time * 2, 0, 0) : new Vector3(1, 0, (time - .5f) * 2));
        simulation.Seek(1, At(1), At);
        var particles = Particles(simulation);
        Assert.That(particles, Has.Length.GreaterThan(2));
        var first = particles[0];
        Assert.That((double)Read(first, "Birth")!, Is.EqualTo(.2).Within(.000001));
        foreach (var particle in particles)
        {
            var root = (Matrix)Read(particle, "Root")!;
            var birth = (double)Read(particle, "Birth")!;
            var velocity = Vector3.TransformNormal((Vector3)Read(particle, "MotionInitialVelocity")!, root);
            Assert.That(velocity.Length(), Is.EqualTo(.2f).Within(.000001));
            Assert.That(velocity.Y, Is.EqualTo(0).Within(.000001));
            Assert.That(birth, Is.InRange(0, 1));
        }
        Assert.That(particles.Select(particle => (Vector3)Read(particle, "MotionInitialVelocity")!).Any(velocity => velocity.X > .01f && velocity.Z > .01f), Is.True,
            "A short turn retains the unconsumed segment anchor instead of emitting a new independent direction at every tick.");
    }

    [Test]
    public void NativeTrail_StationaryTimeDoesNotResetTheLastSegmentClockOrExtendParticleLife()
    {
        var document = VfxDocument.Read(Source(1, true));
        Field(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", .05f);
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        static Matrix At(float time) => Matrix.CreateTranslation(time < 4 ? Vector3.Zero : new Vector3(8.1f, 0, 0));
        simulation.Seek(3.9f, At(3.9f), At);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(4, At(4), At);
        var particles = Particles(simulation);
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.All(particle => (double)Read(particle, "Birth")! < 4), Is.True,
            "The original producer interpolates from its retained last-emission clock; it does not give a teleport a fresh full lifetime.");
        simulation.Seek(4.01f, At(4.01f), At);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void NativeTrail_SplitUpdatesAndSeekingBackReplayIdenticalBirthsAndUnrelatedRandomValues()
    {
        var conversion = Convert(Source(.1f, true));
        static Matrix At(float time) => Matrix.CreateTranslation(time * 8, 0, 0);
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(1, At(1), At);
        var split = new TotalWarVfxSimulation(conversion);
        foreach (var time in new[] { .17f, .39f, .61f, 1f }) split.Seek(time, At(time), At);
        Assert.That(Records(split), Is.EqualTo(Records(direct)));
        split.Seek(0, At(0), At); split.Seek(1, At(1), At);
        Assert.That(Records(split), Is.EqualTo(Records(direct)));
    }

    [Test]
    public void NativeTrail_ExpiredBirthsReleaseCapacityBeforeTheNextBatchAndDoNotBlockLaterMotion()
    {
        var document = VfxDocument.Read(Source(1, true));
        Field(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", .05f);
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        var simulation = new TotalWarVfxSimulation(Convert(document.Write()));
        static Matrix At(float time) => Matrix.CreateTranslation(time < 4 ? 0 : time < 5 ? 8.1f : time < 5.5f ? 16.2f : 16.2f + (time - 5.5f) * 8, 0, 0);
        simulation.Seek(4, At(4), At);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        simulation.Seek(4.5f, At(4.5f), At);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(5, At(5), At);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        Assert.That(((Matrix)Read(Particles(simulation).Single(), "Root")!).Translation.X, Is.GreaterThan(8.1f));
        simulation.Seek(5.5f, At(5.5f), At);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(6, At(6), At);
        var visible = simulation.Frame(Camera);
        Assert.That(visible, Is.Not.Empty, "Expired teleport births must not permanently prevent a later continuously moving segment from becoming visible.");
        Assert.That(visible.All(particle => particle.Time >= 0 && particle.Time < .05f && particle.Transform.Translation.X > 16.2f), Is.True);
        Assert.That(simulation.CapacityReached, Is.False);
    }

    [Test]
    public void NativeTrail_StopsBirthsAtAnEmissionEndBetweenSimulationTicksAndPreservesItsTail()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(1, true))) { EmissionEndTimeSeconds = 1.006f };
        static Matrix At(float time) => Matrix.CreateTranslation(time * 80, 0, 0);
        simulation.Seek(1.25f, At(1.25f), At);
        var particles = Particles(simulation);
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.Max(particle => (double)Read(particle, "Birth")!), Is.EqualTo(1.005f).Within(.000001));
        Assert.That(particles.All(particle => (double)Read(particle, "Birth")! <= 1.006f), Is.True);
        foreach (var particle in particles)
            Assert.That(((Matrix)Read(particle, "Root")!).Translation.X,
                Is.EqualTo(At((float)(double)Read(particle, "Birth")!).Translation.X).Within(.00001));
        Assert.That(particles.All(particle => ((Matrix)Read(particle, "Root")!).Translation.X <= At(1.006f).Translation.X), Is.True);
        Assert.That(simulation.Frame(Camera), Is.Not.Empty);
        simulation.Seek(3.1f, At(3.1f), At);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void OrdinaryDistanceEmission_KeepsAuthoredSpacingAndItsOriginalEmitterDirection()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(1, false)));
        static Matrix At(float time) => Matrix.CreateTranslation(8 * time, 0, 0);
        simulation.Seek(1, At(1), At);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(4));
        var particles = Particles(simulation);
        Assert.That(particles, Has.Length.EqualTo(8));
        Assert.That(particles.Select(particle => ((Matrix)Read(particle, "Root")!).Translation.X).Order(),
            Is.EqualTo(Enumerable.Range(1, 8).Select(index => (float)index)).Within(.00001));
        Assert.That(particles.All(particle => ((Vector3)Read(particle, "MotionInitialVelocity")!).Z > .19f), Is.True);
    }

    [TestCase(-1f, -1f, -1f)]
    [TestCase(1f, 2f, 1f)]
    public void UncalibratedRootScale_KeepsExistingDistanceEmissionAndDiagnosesTheTrail(float x, float y, float z)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(1, true)));
        Matrix At(float time) => Matrix.CreateScale(x, y, z) * Matrix.CreateTranslation(time * 8, 0, 0);
        simulation.Seek(1, At(1), At);
        Assert.That(simulation.ParticleCount, Is.EqualTo(8));
        Assert.That(simulation.VelocityScaleUnsupported, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DynamicYOrNativeSizeTables_KeepTheExistingDistanceRoute(bool nativeSize)
    {
        var document = VfxDocument.Read(Source(1, true));
        var constant = "0,.2,linear,linear,0,0,0,0,1";
        Field(document, "ParticleScale").Value = nativeSize
            ? "x_min,0,1;0,.1,linear,bezier_q,-.05,0,.333333,0,0;1,.2,linear,linear,-.05,0,.05,0,0|"
                + "x_max,0,1;0,.1,linear,bezier_q,-.05,0,.333333,0,0;1,.2,linear,linear,-.05,0,.05,0,0|"
                + $"y_min,0,0;{constant}|y_max,0,0;{constant}|z_min,0,0;0,1,linear,linear,0,0,0,0,1|z_max,0,0;0,1,linear,linear,0,0,0,0,1:x,1;0,1|y,0;2,3|z,0;4,5"
            : "x,0,0;0,.1,linear,linear,0,0,0,0,1|y,0,0;0,.2,linear,linear,0,0,0,0,0;1,.4,linear,linear,0,0,0,0,0|z,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
        var converted = Convert(document.Write());
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(converted);
        static Matrix At(float time) => Matrix.CreateTranslation(time * 8, 0, 0);
        simulation.Seek(1, At(1), At);
        var particles = Particles(simulation);
        Assert.That(particles, Has.Length.EqualTo(8));
        Assert.That(particles.All(particle => ((Vector3)Read(particle, "MotionInitialVelocity")!).Z > .19f), Is.True);
    }

    [TestCase("0", .1f)]
    [TestCase("1", .2f)]
    [TestCase("2", .4f)]
    public void CompleteOfficialBlowpipe_BothOriginalLayersFollowMotionWithNativeSegmentLengths(string quality, float sourceSpeed)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original game data is required for this unchanged complete official root.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(parts => parts.Length > 1)
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" })
        {
            var path = Path.Combine(data!, name); Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var pair in new PackFileContainerLoader(null!).Load(path)!.FileList) files[pair.Key.Replace('/', '\\')] = pair.Value;
        }
        const string rootPath = @"vfx\wh2_dlc17_weapon_trail_blowpipe_hero.xml";
        var source = files[rootPath].DataSource.ReadData();
        Assert.That(System.Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo("dc9b7b8d10bbd9b9f76a66ea5ff45bd0a6fbc6f2911caffef831fdef2323b5f3"));
        var conversion = TotalWarVfxConverter.Convert(new(source, rootPath, path => files.GetValueOrDefault(path.Replace('/', '\\'))?.DataSource.ReadData(), quality));
        Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "_rifle_100ms", "_arrow_glowing_large" }));
        var simulation = new TotalWarVfxSimulation(conversion);
        static Matrix At(float time) => Matrix.CreateTranslation(time * 8, 0, 0);
        simulation.Seek(1, At(1), At);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(4));
        foreach (var particle in Particles(simulation))
        {
            var root = (Matrix)Read(particle, "Root")!; var state = Read(particle, "Emitter")!; var definition = (VfxPreviewEmitter)Read(state, "Definition")!;
            var velocity = Vector3.TransformNormal((Vector3)Read(particle, "MotionInitialVelocity")!, root);
            Assert.That(Vector3.Distance(Vector3.Normalize(velocity), Vector3.UnitX), Is.LessThan(.000001));
            Assert.That(velocity.Length(), Is.EqualTo(sourceSpeed * root.Right.Length()).Within(.000001));
            var lifeBias = (float)Read(state, "LifeBias")!; var timeScale = (float)Read(state, "TimeScale")!;
            Assert.That((float)Read(particle, "Life")!, Is.InRange((definition.Life.Minimum + lifeBias) / timeScale, (definition.Life.Maximum + lifeBias) / timeScale));
        }
        Assert.That(files[rootPath].DataSource.ReadData(), Is.EqualTo(source));
    }

    private static object? Read(object value, string name) => value.GetType().GetFields(Flags).FirstOrDefault(field => field.Name == name)?.GetValue(value)
        ?? value.GetType().GetProperties(Flags).FirstOrDefault(property => property.Name == name)?.GetValue(value);
    private static object[] Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Read(simulation, "_particles")!).Cast<object>().ToArray();
    private static object[] Records(TotalWarVfxSimulation simulation) => Particles(simulation).Select(particle =>
        (object)new object?[] { Read(particle, "Birth"), Read(particle, "Life"), Read(particle, "Root"), Read(particle, "MotionInitialVelocity"),
            Read(particle, "RandomScale"), Read(particle, "RandomColour"), Read(particle, "RandomAlpha"), Read(particle, "InitialAlpha") }).ToArray();
    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "vfx/native-distance-trail.xml", _ => [1]));
    private static XElement Field(VfxDocument document, string name) => document.Root.Descendants("emitter").Single().Elements().Single(element => (string?)element.Attribute("name") == name);
    private static string VectorCurve(float x, float y, float z) => string.Join('|', new[] { ("x", x), ("y", y), ("z", z) }.Select(pair => VfxLibraryVisibilityTests.Constant(pair.Item1, pair.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static byte[] Source(float spacing, bool trail)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source($"""
            <modifier fragment="vs_translation_velocity_v2"><property name="pivot_x" value=".5"/><property name="pivot_y" value="1"/>
            <property name="orientation_mode" value="Camera-facing velocity-aligned"/><property name="velocity_stretch_mul">{VfxLibraryVisibilityTests.Constant("x", 10)}</property><property name="velocity_stretch_cap" value="1000"/></modifier>
            """));
        Field(document, "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        Field(document, "ParticleScale").Value = VectorCurve(.1f, .2f, 1);
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnSpacing"), new XAttribute("value", spacing)),
            new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", .2f)),
            new XElement("property", new XAttribute("name", "EmitterOrientation"), VectorCurve(90, 0, 0)));
        document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", trail ? "true" : "false")))));
        return document.Write();
    }
}
