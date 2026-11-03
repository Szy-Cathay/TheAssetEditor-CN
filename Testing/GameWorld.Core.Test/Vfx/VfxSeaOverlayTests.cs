#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSeaOverlayTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void SourcePass_ReachesOneRawSeaPacketAndTheIndependent24ByteConfiguration(bool premultiplied)
    {
        var source = Source(premultiplied);
        var conversion = Convert(source);
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.SeaOverlay && emitter.RequiresFlatTerrain && emitter.SpawnOnTerrain, Is.True);
            Assert.That(emitter.PremultipliedAlpha, Is.EqualTo(premultiplied));
            Assert.That(emitter.HalfResolution || emitter.FullResolution, Is.False);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.PassUnsupported"), Is.False);
            Assert.That(Marshal.SizeOf<EffekseerHalfLayer>(), Is.EqualTo(24));
            Assert.That(Marshal.OffsetOf<EffekseerHalfLayer>(nameof(EffekseerHalfLayer.CoveredColourMultiplier)).ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<EffekseerHalfLayer>(nameof(EffekseerHalfLayer.EmissionMultiplier)).ToInt32(), Is.EqualTo(20));
            Assert.That(Marshal.SizeOf<EffekseerLayer>(), Is.EqualTo(132));
            Assert.That(Marshal.SizeOf<EffekseerParticle>(), Is.EqualTo(148));
        });
        using var preview = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_conversion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(preview, conversion);
        var built = preview.BuildRenderLayers([null]);
        Assert.That(built.Layers, Has.Length.EqualTo(1), "SEA has no legacy coverage/emission fallback layers.");
        Assert.That(built.HalfLayers, Is.Empty);
        Assert.That(built.FullLayers, Is.Empty);
        var config = built.SeaLayers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(config.LayerIndex, Is.Zero);
            Assert.That(config.LegacyCoverageIndex, Is.EqualTo(-1));
            Assert.That(config.LegacyEmissionIndex, Is.EqualTo(-1));
            Assert.That(config.Premultiplied, Is.EqualTo(premultiplied ? 1 : 0));
            Assert.That(config.CoveredColourMultiplier, Is.EqualTo(emitter.CoveredColourMultiplier));
            Assert.That(config.EmissionMultiplier, Is.EqualTo(emitter.AdditiveMultiplier));
            Assert.That(built.Layers[0].Kind, Is.Zero);
            Assert.That(built.Layers[0].Blend, Is.EqualTo(1));
            Assert.That(built.Layers[0].MaterialMode, Is.EqualTo(config.Premultiplied));
            Assert.That(built.Layers[0].LightingMode, Is.EqualTo(2));
            Assert.That(built.Layers[0].SmallParticles, Is.EqualTo(.1f));
            Assert.That(built.Layers[0].LargeParticles, Is.Zero);
            Assert.That(built.Layers[0].Directionality, Is.EqualTo(.5f));
            Assert.That(built.Layers[0].AverageSize, Is.EqualTo(.1f));
            Assert.That(built.Layers[0].Emissive, Is.EqualTo(.7f));
            Assert.That(built.Layers[0].EmissionShapeWeight, Is.EqualTo(premultiplied ? 1 : 0));
        });
        var original = new EffekseerParticle { Transform = Matrix.Identity, Colour = new(2, 3, 4, .375f), UV = new(.1f, .2f, .3f, .4f) };
        var count = (int)typeof(EffekseerVfxPreview).GetMethod("PrepareSubmission", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(preview, [new[] { new VfxPreviewParticle(original, 0) }])!;
        var packet = ((EffekseerParticle[])typeof(EffekseerVfxPreview).GetField("_submissionParticles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(preview)!)[0];
        Assert.That(count, Is.EqualTo(1));
        Assert.That(packet.Colour, Is.EqualTo(original.Colour), "Native SEA owns its multipliers; CPU must retain the original RGBA once.");
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [TestCase("restriction")]
    [TestCase("ParticlesSpawnOnTerrain")]
    [TestCase("ParticleInitialSpeed")]
    [TestCase("EmitterPosition")]
    public void UnsupportedWaterReceiverCombinations_StayDiagnosedInsteadOfBecomingOrdinarySprites(string control)
    {
        var document = VfxDocument.Read(Source(true));
        if (control == "restriction")
            document.Root.Descendants("property").Single(p => (string?)p.Attribute("name") == control).SetAttributeValue("value", 0);
        else if (control == "ParticlesSpawnOnTerrain")
            document.Root.Descendants("property").Single(p => (string?)p.Attribute("name") == control).SetAttributeValue("value", "false");
        else
            document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", control),
                control == "EmitterPosition" ? VectorCurve(1) : VfxLibraryVisibilityTests.Constant("x", 1)));
        var source = document.Write();
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "scene_view_type" && d.ReasonKey == "Vfx.Preview.PassUnsupported"), Is.True);
        Assert.That(document.Write(), Is.EqualTo(source));
    }

    [Test]
    public void ExplicitFlatReceiver_IsRequiredAndYawUniformScaleDoNotInventTiltedWater()
    {
        var conversion = Convert(Source(true));
        var simulation = new TotalWarVfxSimulation(conversion);
        var root = Matrix.CreateScale(1.7f) * Matrix.CreateRotationY(.9f) * Matrix.CreateTranslation(2, 5, 3);
        simulation.Seek(.5f, root);
        Assert.That(simulation.Frame(Camera(null)), Is.Empty);
        Assert.That(simulation.TerrainBasisUnsupported, Is.True);
        var visible = simulation.Frame(Camera(0));
        Assert.That(visible, Is.Not.Empty);
        Assert.That(visible.All(p => Math.Abs(p.Transform.Translation.Y) < .00001f), Is.True);
        Assert.That(simulation.TerrainBasisUnsupported, Is.False);
        var shear = Matrix.Identity;
        shear.M12 = .1f;
        foreach (var (label, unsupported) in new[]
        {
            ("tilted", Matrix.CreateRotationX(.1f)), ("nonuniform", Matrix.CreateScale(1, 2, 1)),
            ("mirrored", Matrix.CreateScale(-1, 1, 1)), ("sheared", shear),
        })
        {
            Assert.That(VfxSeaOverlay.IsFlatReceiverBasis(unsupported, Vector3.One), Is.False, label);
            var unsupportedSimulation = new TotalWarVfxSimulation(conversion);
            unsupportedSimulation.Seek(.5f, unsupported);
            Assert.That(unsupportedSimulation.Frame(Camera(0)), Is.Empty, label);
            Assert.That(unsupportedSimulation.TerrainBasisUnsupported, Is.True, label);
        }
        var ordinary = VfxDocument.Read(Source(true));
        ordinary.Root.Descendants("effect_render_stacks").Remove();
        ordinary.Root.Descendants("property").Where(p => (string?)p.Attribute("name") is "ParticlesSpawnOnTerrain" or "ParticlesFollowTerrain").Remove();
        var ordinarySimulation = new TotalWarVfxSimulation(Convert(ordinary.Write()));
        ordinarySimulation.Seek(.5f, Matrix.Identity);
        Assert.That(ordinarySimulation.Frame(Camera(null)), Is.Not.Empty, "The SEA receiver requirement must not suppress ordinary sprites.");
    }

    internal static byte[] Source(bool premultiplied)
    {
        var document = VfxDocument.Read(VfxSimpleEmissiveTests.Source(.7f, false, new(.3f, .6f, .9f), 2, "SEA_OVERLAY", premultiplied));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements("property").Single(p => (string?)p.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 4);
        emitter.Add(new XElement("property", new XAttribute("name", "ParticlesSpawnOnTerrain"), new XAttribute("value", "true")),
            new XElement("property", new XAttribute("name", "ParticlesFollowTerrain"), new XAttribute("value", "true")));
        document.Root.Descendants("modifier").Single(m => (string?)m.Attribute("fragment") == "vs_translation")
            .Add(new XElement("property", new XAttribute("name", "restriction"), new XAttribute("value", 2)));
        document.Root.Descendants("modifier").Single(m => (string?)m.Attribute("fragment") == "ps_lighting").Add(
            new XElement("property", new XAttribute("name", "small_particles"), new XAttribute("value", ".1")),
            new XElement("property", new XAttribute("name", "large_particles"), new XAttribute("value", "0")),
            new XElement("property", new XAttribute("name", "directionality"), new XAttribute("value", ".5")),
            new XElement("property", new XAttribute("name", "average_size"), new XAttribute("value", ".1")));
        return document.Write();
    }

    private static string VectorCurve(float value) => string.Join('|', new[] { "x", "y", "z" }
        .Select(axis => VfxLibraryVisibilityTests.Constant(axis, value).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "DIAG/sea-overlay.xml", _ => [1]));
    private static CommonShaderParameters Camera(float? ground) => new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: ground);
}
