#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

// CPU diagnostics only; these fixtures do not establish official-effect visual fidelity.
public class VfxTrailRenderStackTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase("0", false)]
    [TestCase("0", true)]
    [TestCase("1", false)]
    [TestCase("1", true)]
    [TestCase("2", false)]
    [TestCase("2", true)]
    public void RenderStackTrailMode_UsesTheSelectedQualityAndChangesActualParticleLength(string quality, bool enabled)
    {
        var document = Sprite();
        var stack = RenderStack(document);
        if (quality != "0") stack.Add(TrailProperty(!enabled, "0"));
        stack.Add(TrailProperty(enabled, quality));
        var bytes = document.Write();
        var conversion = Convert(bytes, quality);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters[0].TrailMode, Is.EqualTo(enabled));
            Assert.That(simulation.Frame(Camera).Single().Transform.Up.Length(), Is.EqualTo(enabled ? 1 : 2).Within(.00001f));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "enable_trail_mode" && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase(true, .75f)]
    [TestCase(false, 1.5f)]
    public void RenderStackTrailMode_AppliesTheStretchCapAfterTheAuthoredBaseLength(bool enabled, float cap)
    {
        var document = Sprite(cap);
        RenderStack(document).Add(TrailProperty(enabled, "0"));
        var conversion = Convert(document.Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().Transform.Up.Length(), Is.EqualTo(cap).Within(.00001f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BehaviourTrailMode_RemainsCompatibleWhenTheRenderStackHasNoValue(bool enabled)
    {
        var document = Sprite();
        document.Root.Descendants("modifier").Single(m => (string?)m.Attribute("fragment") == "vs_translation_velocity_v2")
            .Add(TrailProperty(enabled));
        var conversion = Convert(document.Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].TrailMode, Is.EqualTo(enabled));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConflictingBehaviourAndRenderStackTrailModes_RejectTheLayerExplicitly(bool renderValue)
    {
        var document = Sprite();
        RenderStack(document).Add(TrailProperty(renderValue, "0"));
        document.Root.Descendants("modifier").Single(m => (string?)m.Attribute("fragment") == "vs_translation_velocity_v2")
            .Add(TrailProperty(!renderValue));
        var conversion = Convert(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "enable_trail_mode" && d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
        });
    }

    [Test]
    public void DuplicateRenderStackTrailModesAtTheSameQuality_RejectTheLayerExplicitly()
    {
        var document = Sprite();
        RenderStack(document).Add(TrailProperty(true, "1"), TrailProperty(false, "1"));
        var conversion = Convert(document.Write(), "1");
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "enable_trail_mode" && d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
        });
    }

    [Test]
    public void RigidDistanceEmission_KeepsSpacingAndWorldBirthPositionsWithoutTimeRateCatchUp()
    {
        var conversion = ConvertRigid(Rigid(.5f, 4, 1).Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters[0].SpawnSpacing, Is.EqualTo(.5f));
            Assert.That(conversion.Emitters[0].IsMotionTrail, Is.True);
        });
        var simulation = new TotalWarVfxSimulation(conversion);
        static Matrix Motion(float time) => Matrix.CreateTranslation(Math.Min(time, 1) * 2, 0, 0);
        simulation.Seek(1, Motion(1), Motion);
        Assert.That(simulation.Frame(Camera).Select(p => p.Transform.Translation.X).Order(),
            Is.EqualTo(new[] { .5f, 1, 1.5f, 2 }).Within(.001f));
        simulation.Seek(1.1f, Motion(1.1f), Motion);
        Assert.That(simulation.ParticleCount, Is.EqualTo(4), "Stopping must not fall back to the authored time rate of 100.");
        simulation.Seek(2.1f, Motion(2.1f), Motion);
        Assert.That(simulation.ParticleCount, Is.Zero, "Stopped distance particles retain their original finite lifetime.");
    }

    [Test]
    public void RigidDistanceEmission_DefaultSeekPreservesItsFiniteEmissionWindowAndParticleLife()
    {
        var conversion = ConvertRigid(Rigid(.5f, .5f, 1).Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var emitter = conversion.Emitters[0];
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(1, Moving(1), Moving);
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Infinite, Is.False);
            Assert.That(emitter.Duration, Is.EqualTo(new VfxRange(.5f, .5f)));
            Assert.That(emitter.Life.Maximum, Is.EqualTo(1));
            Assert.That(simulation.Frame(Camera).Select(p => p.Transform.Translation.X).Order(),
                Is.EqualTo(new[] { .5f, 1 }).Within(.001f));
        });
        simulation.Seek(1.6f, Moving(1.6f), Moving);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void RigidDistanceTrail_ContinuousSeekCrossesItsShortWindowWithoutExtendingParticleLife()
    {
        var bytes = Rigid(.1f, .125f, .5f).Write();
        var conversion = ConvertRigid(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var ordinary = new TotalWarVfxSimulation(conversion);
        ordinary.Seek(2, Moving(2), Moving);
        Assert.That(ordinary.ParticleCount, Is.Zero);
        var continuous = new TotalWarVfxSimulation(conversion);
        continuous.SeekContinuousTrail(2, Moving(2), Moving);
        var particles = continuous.Frame(Camera);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters[0].IsMotionTrail, Is.True);
            Assert.That(particles, Has.Length.InRange(9, 11));
            Assert.That(particles.All(p => p.Transform.Translation.X >= 2.99f && p.Transform.Translation.X <= 4.001f), Is.True);
            Assert.That(particles.All(p => p.Time < .5001f), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void RigidEventTemplate_KeepsItsFiniteChildWindowWhileTheIndependentRigidTrailContinues()
    {
        var document = Rigid(.1f, .125f, .5f);
        var helper = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var parent = helper.Root.Descendants("emitter").Single();
        parent.Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), VfxLibraryVisibilityTests.Constant("x", 2)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)));
        document.Root.Descendants("emitters").Single().Add(new XElement(parent));
        var child = Rigid(.1f, .125f, .25f).Write();
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/rigid-trail.xml",
            path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child : VfxModelVariantTests.Resolve(path)));
        var template = conversion.Emitters.Single(e => e.IsEventTemplate);
        Assert.That(template.Kind, Is.EqualTo(1));
        Assert.That(template.IsMotionTrail, Is.False);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.SeekContinuousTrail(.2f, Moving(.2f), Moving);
        Assert.That(simulation.Frame(Camera).Any(p => conversion.Emitters[p.Layer].IsEventTemplate), Is.True);
        simulation.SeekContinuousTrail(2, Moving(2), Moving);
        var particles = simulation.Frame(Camera);
        Assert.That(particles, Is.Not.Empty);
        Assert.That(particles.All(p => !conversion.Emitters[p.Layer].IsEventTemplate), Is.True,
            "A finite event child must not inherit the independent root's continuous-preview extension.");
    }

    private static Matrix Moving(float time) => Matrix.CreateTranslation(time * 2, 0, 0);

    private static VfxDocument Sprite(float cap = 1000)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"),
            VfxLibraryVisibilityTests.Constant("x", 2)));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
            new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-facing velocity-aligned")),
            new XElement("property", new XAttribute("name", "velocity_stretch_mul"), new XAttribute("value", .5f)),
            new XElement("property", new XAttribute("name", "velocity_stretch_cap"), new XAttribute("value", cap))));
        document.Root.Element("vfx")!.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"))));
        return document;
    }

    private static XElement RenderStack(VfxDocument document) => document.Root.Descendants("effect_render_stack").Single();

    private static XElement TrailProperty(bool value, string? quality = null) => new("property",
        new XAttribute("name", "enable_trail_mode"), new XAttribute("value", value), quality == null ? null : new XAttribute("quality", quality));

    private static VfxDocument Rigid(float spacing, float duration, float life)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "InfiniteLife").SetAttributeValue("value", false);
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", life);
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 100);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", duration), new XAttribute("max", duration)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 32)),
            new XElement("property", new XAttribute("name", "SpawnSpacing"), new XAttribute("value", spacing)),
            new XElement("property", new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", "WORLD_SPACE")));
        return document;
    }

    private static VfxConversion Convert(byte[] bytes, string quality = "0")
        => TotalWarVfxConverter.Convert(new(bytes, "vfx/render-stack-trail.xml", _ => [1], quality));

    private static VfxConversion ConvertRigid(byte[] bytes)
        => TotalWarVfxConverter.Convert(new(bytes, "vfx/rigid-trail.xml", VfxModelVariantTests.Resolve));
}
