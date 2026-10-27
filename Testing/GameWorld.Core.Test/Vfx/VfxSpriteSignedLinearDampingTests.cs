#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxSpriteSignedLinearDampingTests
{
    private const double Step = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 20),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(-1f, -1f, 0)]
    [TestCase(-.5f, -.5f, 0)]
    [TestCase(-1f, .5f, 0)]
    [TestCase(-.5f, -.5f, 100)]
    [TestCase(1f, 1f, 0)]
    public void OrdinaryLinearDamping_UsesSignedExponentialAfterForce(float dragStart, float dragEnd, float forceY)
    {
        var plan = TotalWarVfxConverter.Convert(Diagnostic(dragStart, dragEnd, forceY));
        Assert.That(plan.Emitters, Has.Count.EqualTo(1));
        if (forceY != 0) Assert.That(plan.Emitters.Single().ParticleForceCurve, Is.True);
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(0, Matrix.Identity);
        var particle = Particle(simulation);
        var initialVelocity = Read<Vector3>(particle, "Velocity");
        var initialPosition = Read<Vector3>(particle, "Position");
        var life = Read<float>(particle, "Life");
        Assert.That(Read<double>(particle, "Birth"), Is.Zero);
        Assert.That(initialVelocity.Length(), Is.GreaterThan(1));
        foreach (var seconds in new[] { .5f, 1.5f, 3f })
        {
            var expectedPosition = initialPosition;
            var expectedVelocity = initialVelocity;
            var count = (int)Math.Round(seconds / Step);
            for (var i = 1; i <= count; i++)
            {
                var phase = i * Step / life;
                var lambda = (float)(dragStart + (dragEnd - dragStart) * phase);
                expectedVelocity += new Vector3(0, forceY * .01f, 0) * (float)Step;
                expectedVelocity *= MathF.Exp(-lambda * (float)Step);
                expectedPosition += expectedVelocity * (float)Step;
            }
            simulation.Seek(seconds, Matrix.Identity);
            Assert.Multiple(() =>
            {
                Assert.That(Vector3.Distance(Read<Vector3>(Particle(simulation), "Velocity"), expectedVelocity), Is.LessThan(Tolerance),
                    "Native ordinary CS applies exp(-lambda*sourceDt), including negative lambda, after independent forces.");
                Assert.That(Vector3.Distance(simulation.Frame(Camera).Single().Transform.Translation, expectedPosition), Is.LessThan(Tolerance),
                    "Position advances with the updated velocity; negative damping is not a pause or a reset.");
            });
        }
    }

    [Test]
    public void SpriteSignedDamping_DoesNotReplaceTheExistingModelLinearRule()
    {
        var plan = TotalWarVfxConverter.Convert(Diagnostic(-1, -1, 0));
        // DIAG: exercise the shared tick's existing model branch, without claiming native model parity.
        var render = plan.Emitters.Single().Render;
        render.Kind = 1; plan.Emitters.Single().Render = render;
        var simulation = new TotalWarVfxSimulation(plan);
        simulation.Seek(0, Matrix.Identity);
        var initial = Read<Vector3>(Particle(simulation), "Velocity");
        simulation.Seek(1, Matrix.Identity);
        Assert.That(Read<Vector3>(Particle(simulation), "Velocity"), Is.EqualTo(initial),
            "Ordinary GPU damping must not be copied into the distinct model CPU rule.");
    }

    [Test]
    public void CompleteOfficialTrail_PreservesItsNegativeLinearDampingEnvelope()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Official resources require explicit VFX opt-in.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !Directory.Exists(data)) Assert.Ignore("Original game data is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(l => l.Split('\t')).Where(p => p.Length > 1)
            .ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var names = new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" };
        var loader = new PackFileContainerLoader(null!);
        var packs = names.Select(name =>
        {
            var path = Path.Combine(data!, name);
            Assert.That(manifest.TryGetValue(name, out var expected), Is.True);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(expected));
            return loader.Load(path)!;
        }).OrderBy(p => p.Header.LoadOrder).ThenBy(p => p.Name).ToArray();
        var index = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        foreach (var file in pack.FileList) index[file.Key.Replace('/', '\\')] = file.Value;
        byte[]? ReadOfficial(string path) => index.TryGetValue(path.Replace('/', '\\'), out var file) ? file.DataSource.ReadData() : null;
        const string root = @"vfx\wh2_dlc12_machine_warp_lightning_rifle_trail_purple.xml";
        var source = ReadOfficial(root) ?? throw new System.IO.FileNotFoundException(root);
        var plan = TotalWarVfxConverter.Convert(new(source, root, ReadOfficial, "1"));
        var trail = plan.Emitters.Single(e => e.Name == "TRAIL_embers");
        Assert.Multiple(() =>
        {
            Assert.That(trail.Kind, Is.EqualTo(0));
            Assert.That(trail.Deceleration.Sample("x", 0, 0), Is.LessThan(0), "The original minimum envelope includes signed linear damping.");
            Assert.That(plan.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
        // The numeric tests above cover the negative-weight boundary; no fixed official random seed is assumed.
    }

    private static VfxPreviewSnapshot Diagnostic(float dragStart, float dragEnd, float forceY)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Element("vfx")!.Element("emitters")!.Element("emitter")!;
        void Set(string name, string value)
        {
            var node = emitter.Elements().FirstOrDefault(n => (string?)n.Attribute("name") == name);
            if (node == null) emitter.Add(new XElement("property", new XAttribute("name", name), value));
            else node.Value = value;
        }
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        Set("ParticleLifeTime", Scalar(4, 4));
        Set("ParticleInitialSpeed", Scalar(2, 2));
        Set("ParticleDeceleration", Scalar(dragStart, dragEnd));
        if (forceY != 0) Set("ForceA", string.Join("|", Channel("x_min", 0, 0), Channel("y_min", forceY, forceY), Channel("z_min", 0, 0)) + ":x,0;0,0|y,0;1,1|z,0;2,2");
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(document.Write(), "vfx/diagnostic-signed-linear-damping.xml", _ => texture);
    }

    private static object Particle(TotalWarVfxSimulation simulation) => ((IEnumerable)typeof(TotalWarVfxSimulation)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!).Cast<object>().Single();
    private static T Read<T>(object particle, string name) => (T)particle.GetType().GetField(name)!.GetValue(particle)!;
    private static string Channel(string name, double a, double b) => FormattableString.Invariant($"{name},0,1;0,{a},linear,linear,0,0,0,0,0;1,{b},linear,linear,0,0,0,0,0");
    private static string Scalar(double a, double b) => Channel("x_min", a, b) + "|" + Channel("x_max", a, b) + ":x,0;0,1";
}
