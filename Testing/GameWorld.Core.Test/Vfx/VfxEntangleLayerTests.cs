#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxEntangleLayerTests
{
    [Test]
    public void FixedAnimationSpeed_RetainsMeasuredNativeColoursAndRecordsItsRemainingLimitations()
    {
        var assembly = typeof(VfxEntangleLayerTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("NativeEntangleTiming.json")))!;
        using var fixture = JsonDocument.Parse(stream);
        Assert.That(fixture.RootElement.GetProperty("Limitation").GetString(), Does.Contain("remain outside"));
        foreach (var item in fixture.RootElement.GetProperty("Cases").EnumerateArray())
        {
            var phase = item.GetProperty("PhaseInterval").EnumerateArray().Average(p => p.GetDouble());
            var atlas = new VfxAtlas(2, 2, Animated: true,
                Loops: new(item.GetProperty("FrequencyStart").GetSingle(), item.GetProperty("FrequencyEnd").GetSingle()), FixedSpeed: true);
            var seconds = phase * (item.GetProperty("Mode").GetString() == "fixed_animation_speed" ? item.GetProperty("LifeSeconds").GetDouble() : 1);
            Assert.That((int)MathF.Floor(atlas.FrameAtSeconds(phase, seconds, 0)), Is.EqualTo(item.GetProperty("ObservedCell").GetInt32()),
                item.GetProperty("Image").GetString());
        }
    }

    [Test]
    public void FixedAnimationSpeed_ChangesOnlyUvWithoutChangingTheSimulationTimeline()
    {
        var bytes = Source(null, 0, 4);
        var lifetime = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("fixed_animation_speed", "using_particle_lifetime", StringComparison.Ordinal));
        var fixedSimulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(bytes, "test.xml", _ => [1])));
        var lifeSimulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(lifetime, "test.xml", _ => [1])));
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        foreach (var time in new[] { .1f, .75f, 1.5f, 2.5f })
        {
            fixedSimulation.Seek(time, Matrix.Identity); lifeSimulation.Seek(time, Matrix.Identity);
            var a = fixedSimulation.Frame(camera).Single(); var b = lifeSimulation.Frame(camera).Single();
            Assert.Multiple(() =>
            {
                Assert.That(a.Time, Is.EqualTo(b.Time));
                Assert.That(a.Transform, Is.EqualTo(b.Transform));
                Assert.That(a.Colour, Is.EqualTo(b.Colour));
            });
        }
    }

    [Test]
    public void UnsupportedAnimationTimer_IsStillRejectedInsteadOfBeingChangedToFixedSpeed()
    {
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Source(null, 0)).Replace("fixed_animation_speed", "trail_distribution", StringComparison.Ordinal));
        var converted = TotalWarVfxConverter.Convert(new(bytes, "test.xml", _ => [1]));
        Assert.That(converted.Emitters, Is.Empty);
        Assert.That(converted.Diagnostics.Any(d => d.Field == "time_mode" && d.ReasonKey == "Vfx.Preview.UvUnsupported"), Is.True);
    }

    [Test]
    public void ZeroDepthDecal_DoesNotInvertASingularVolumeOrProduceArbitraryPixels()
    {
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Source("DEFERRED_DECAL_EMISSIVE", 0))
            .Replace("z_min,0,0;0,2,", "z_min,0,0;0,0,", StringComparison.Ordinal));
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(bytes, "test.xml", _ => [1])));
        simulation.Seek(.1f, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 5, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        Assert.That(simulation.Frame(camera), Is.Empty);
    }

    [TestCase("DEFERRED_DECAL", 0)]
    [TestCase("DEFERRED_DECAL", 2)]
    [TestCase("DEFERRED_DECAL_EMISSIVE", 0)]
    [TestCase("DEFERRED_DECAL_EMISSIVE", 2)]
    public void DecalPass_PreservesProjectionVolumeInsteadOfUsingABillboard(string pass, int restriction)
    {
        var bytes = Source(pass, restriction);
        var converted = TotalWarVfxConverter.Convert(new(bytes, "vfx/test.xml", _ => [1]));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var emitter = converted.Emitters.Single();
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.1f, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 5, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var particle = simulation.Frame(camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Kind, Is.EqualTo(2));
            Assert.That(particle.Transform.Backward.Length(), Is.EqualTo(2).Within(.00001), "The third scale controls the receiver volume depth.");
            Assert.That(MathF.Abs(particle.Transform.Backward.Y), Is.EqualTo(2).Within(.00001));
            Assert.That(converted.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.ProjectedDecalCandidate"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase(2)]
    [TestCase(4)]
    public void FixedAnimationSpeed_UsesSecondsAndRetainsLifetimeForFrequencyInterpolation(float lifetime)
    {
        var bytes = Source(null, 0, lifetime);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/test.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.75f, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var particle = simulation.Frame(camera).Single();
        Assert.That(particle.Flipbook, Is.EqualTo(1.5f).Within(.00001), "A half cycle per second on four cells is independent of lifetime.");
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    internal static byte[] Source(string? pass, int restriction, float lifetime = 3)
    {
        var animation = pass == null ? """
            <modifier fragment="vs_animated_texture"><property name="no_cells_xy" value="float2(2,2)"/>
            <property name="no_loops" value="float2(0.5,0.5)"/><property name="time_mode" value="fixed_animation_speed"/>
            <property name="loop" value="true"/><property name="cell_blend" value="true"/></modifier>
            """ : "";
        var stack = pass == null ? "" : $"<effect_render_stacks><effect_render_stack id=\"test\"><property name=\"scene_view_type\" value=\"{pass}\"/></effect_render_stack></effect_render_stacks>";
        return Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="test" type="continuous_mesh">
            <property name="EmissionRateCurve">{Constant("x", .1)}</property><property name="ParticleLifeTime">{Constant("x", lifetime)}</property>
            <property name="ParticleScale">{Constant("xyz", 2)}</property><property name="InfiniteLife" value="true"/>
            <property name="ParticleOrientationMin" x="90" y="0" z="0"/><property name="ParticleOrientationMax" x="90" y="0" z="0"/>
            </emitter></emitters><behaviour_chains><behaviour_chain id="test"><quality value="0">
            <modifier fragment="vs_translation"><property name="restriction" value="{restriction}"/></modifier>
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/test.dds"/></modifier>{animation}
            </quality></behaviour_chain></behaviour_chains>{stack}</vfx>
            """);
    }

    private static string Constant(string axes, double value) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
}
