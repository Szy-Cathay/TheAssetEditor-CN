#nullable enable
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxWorldForceCacheTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 100), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase("single", true)]
    [TestCase("linear", false)]
    [TestCase("bezier", false)]
    [TestCase("dynamic", false)]
    [TestCase("missing", false)]
    [TestCase("duplicate-time", false)]
    public void VectorCacheAdmitsOnlyCompleteFiniteOriginalSingleKeys(string kind, bool expected)
    {
        var curve = new VfxPreviewCurve(VectorCurve(kind), "ForceA", 0);
        var gate = typeof(VfxPreviewCurve).GetProperty("HasSingleKeyVector");
        Assert.That(gate, Is.Not.Null, "A strict gate is required before moving an authored curve sample to birth.");
        Assert.That(gate!.GetValue(curve), Is.EqualTo(expected));
        if (kind == "bezier")
        {
            Assert.That(curve.IsConstant, Is.True, "Endpoint equality alone does not make a Bezier constant.");
            Assert.That(curve.Vector(.625, new(.5f)), Is.Not.EqualTo(curve.Vector(0, new(.5f))));
        }
    }

    [Test]
    public void NonfiniteAuthoredValuesRemainRejectedByTheExistingParser()
    {
        var exception = Assert.Throws<VfxMappingException>(() => new VfxPreviewCurve(VectorCurve("nonfinite"), "ForceA", 0));
        Assert.That(exception!.Reason, Is.EqualTo("Vfx.Preview.InvalidData"));
    }

    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    public void OnlyWorldSpaceProjectionAvoidsUnusedCurrentHistorySampling(int space, bool readsHistory)
    {
        var plan = Convert(Source(force: "zero", prewarm: false));
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(plan.Emitters.Single(), space);
        var simulation = new TotalWarVfxSimulation(plan);
        var born = Matrix.CreateTranslation(10, 0, 0);
        simulation.Seek(0, born);
        var before = simulation.Frame(Camera);
        var reads = 0;
        Matrix History(float _) { reads++; return Matrix.CreateTranslation(20, 0, 0); }
        simulation.Seek(0, Matrix.CreateTranslation(20, 0, 0), History);
        reads = 0;
        var after = simulation.Frame(Camera);
        Assert.That(reads > 0, Is.EqualTo(readsHistory));
        if (space == 2 || space == 3) Assert.That(Bytes(after), Is.EqualTo(Bytes(before)), "World/legacy invalid-space particles retain their actual birth transform.");
        else Assert.That(after.Single().Transform.Translation.X, Is.EqualTo(20));
    }

    [Test]
    public void CachedRandomForcePreservesDynamicDampingEveryTickAndTheFutureBirthRandomSequence()
    {
        var cachedPlan = Convert(Source());
        var controlPlan = Convert(Source(force: "linear"));
        Assert.That(cachedPlan.Emitters.Single().ParticleForceCurve, Is.True);
        Assert.That(controlPlan.Emitters.Single().ParticleForceCurve, Is.True);
        Assert.That(cachedPlan.Emitters.Single().Deceleration.IsConstant, Is.False);
        var cached = new TotalWarVfxSimulation(cachedPlan);
        var control = new TotalWarVfxSimulation(controlPlan);
        _ = cached.CycleDuration; _ = control.CycleDuration;
        foreach (var time in new[] { 0f, 1f / 120, .1f, .5f, 1f, 2f, 3f, 5f })
        {
            cached.Seek(time, Matrix.Identity); control.Seek(time, Matrix.Identity);
            Assert.That(Bytes(cached.Frame(Camera)), Is.EqualTo(Bytes(control.Frame(Camera))), $"Full packet bytes at {time}s.");
            Assert.That(ParticleState(cached), Is.EqualTo(ParticleState(control)), "Original lifetime, motion and all existing random channels.");
            foreach (var particle in Particles(cached))
            {
                var stored = Field(particle, "FixedForce");
                Assert.That(stored, Is.TypeOf<Vector3>());
                var random = (float)Field(particle, "RandomAlpha")!;
                var expected = cachedPlan.Emitters.Single().Force.Vector(0, new(random)) * .01f;
                var actual = (Vector3)stored!;
                Assert.That(new[] { Bits(actual.X), Bits(actual.Y), Bits(actual.Z) },
                    Is.EqualTo(new[] { Bits(expected.X), Bits(expected.Y), Bits(expected.Z) }));
            }
            Assert.That(Particles(control).All(particle => Field(particle, "FixedForce") == null), Is.True,
                "The equivalent multi-key control must keep sampling, not use IsConstant as a cache gate.");
        }
    }

    [TestCase("bezier")]
    [TestCase("dynamic")]
    [TestCase("overflow")]
    public void DynamicBezierAndNonfiniteForceResultsKeepTheOriginalSamplingRoute(string kind)
    {
        var plan = Convert(Source(force: kind, prewarm: false));
        Assert.That(plan.Emitters.Single().ParticleForceCurve, Is.True);
        var simulation = new TotalWarVfxSimulation(plan);
        Assert.DoesNotThrow(() => simulation.Seek(0, Matrix.Identity));
        Assert.That(Particles(simulation), Is.Not.Empty);
        Assert.That(Particles(simulation).All(particle => Field(particle, "FixedForce") == null), Is.True);
        if (kind != "overflow")
        {
            simulation.Seek(.3f, Matrix.Identity);
            Assert.That(Particles(simulation).All(particle => Field(particle, "FixedForce") == null), Is.True);
        }
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    public void OnlyImmutableWorldBasisIsCachedAndUnsupportedResultsStillReportTheOriginalDiagnostic(int space, bool rotate)
    {
        var plan = Convert(Source(prewarm: false));
        var emitter = plan.Emitters.Single();
        typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.SimulationSpace))!.SetValue(emitter, space);
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(.1f, rotate ? Matrix.CreateRotationY(MathHelper.PiOver4) : Matrix.Identity);
        var particle = Particles(simulation).First();
        var basis = Field(particle, "WorldForceBasisSupported");
        if (space == 2) Assert.That(basis, Is.EqualTo(!rotate));
        else Assert.That(basis, Is.Null);
        if (rotate)
        {
            typeof(TotalWarVfxSimulation).GetProperty(nameof(TotalWarVfxSimulation.ForceBasisUnsupported))!.SetValue(simulation, false);
            var supported = typeof(TotalWarVfxSimulation).GetMethod("ForceBasisSupported", Private)!.Invoke(simulation, [particle, emitter]);
            Assert.That(supported, Is.False);
            Assert.That(simulation.ForceBasisUnsupported, Is.True, "A cached false must retain the diagnostic on every use.");
        }
    }

    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "diagnostic-world-force.xml", _ => [1]));
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static byte[] Bytes(EffekseerParticle[] particles) => MemoryMarshal.AsBytes(particles.AsSpan()).ToArray();
    private static object? Field(object particle, string name) => particle.GetType().GetField(name)?.GetValue(particle);
    private static object[] Particles(TotalWarVfxSimulation simulation)
        => ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", Private)!.GetValue(simulation)!).Cast<object>().ToArray();
    private static object[] ParticleState(TotalWarVfxSimulation simulation)
        => Particles(simulation).Select(particle => (object)new object?[] { Field(particle, "Birth"), Field(particle, "Life"),
            Field(particle, "Root"), Field(particle, "Position"), Field(particle, "Velocity"), Field(particle, "RandomAlpha"),
            Field(particle, "RandomScale"), Field(particle, "RandomColour"), Field(particle, "InitialAlpha"), Field(particle, "InitialScale") }).ToArray();
    private static byte[] Source(string force = "single", bool prewarm = true)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Element("vfx")!.Element("emitters")!.Element("emitter")!;
        emitter.Elements("property").Single(x => (string?)x.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 3);
        emitter.Elements("property").Single(x => (string?)x.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 4);
        emitter.Add(new XElement("property", new XAttribute("name", "ForceA"), VectorCurve(force)));
        if (force != "zero") emitter.Add(new XElement("property", new XAttribute("name", "ParticleDeceleration"),
            "x_min,0,1;0,4.459414,linear,linear,0,0,0,0,1;1,9.634362,linear,linear,0,0,0,0,1|x_max,0,1;0,4.459414,linear,linear,0,0,0,0,1;1,9.634362,linear,linear,0,0,0,0,1:x,0;0,1"));
        if (prewarm) emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -.5), new XAttribute("max", -.5)));
        return document.Write();
    }
    private static string VectorCurve(string kind)
    {
        string Channel(string axis, string value)
        {
            var next = kind == "dynamic" && axis.StartsWith("y") ? "8000" : value;
            if (kind == "bezier") return $"{axis},0,1;.25,{value},bezier,bezier,0,0,.25,1000,1;1,{value},bezier,bezier,-.25,1000,0,0,1";
            if (kind is "linear" or "dynamic") return $"{axis},0,1;.25,{value},linear,linear,0,0,0,0,1;1,{next},linear,linear,0,0,0,0,1";
            if (kind == "duplicate-time") return $"{axis},0,1;.25,{value},linear,linear,0,0,0,0,1;.25,{value},linear,linear,0,0,0,0,1";
            return $"{axis},0,0;.25,{value},linear,linear,0,0,0,0,1";
        }
        var low = kind switch { "zero" => "0", "nonfinite" => "NaN", "overflow" => "-3.4028234e38", _ => "2500" };
        var high = kind switch { "zero" => "0", "overflow" => "3.4028234e38", _ => "4000" };
        var channels = new[] { Channel("x_min", "0"), Channel("x_max", "0"), Channel("y_min", low), Channel("y_max", high), Channel("z_min", "0"), Channel("z_max", "0") };
        return string.Join('|', kind == "missing" ? channels.Take(4) : channels) + ":x,0;0,1|y,1;2,3|z,0;4,5";
    }
}
