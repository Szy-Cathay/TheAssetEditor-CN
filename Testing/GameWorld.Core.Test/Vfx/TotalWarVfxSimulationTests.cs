using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Vfx;

public class TotalWarVfxSimulationTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void RandomEnvelope_KeepsItsWeightAsTheAuthoredRangeChanges()
    {
        var curve = new VfxPreviewCurve("x_min,0,1;0,2,linear,linear,0,0,0,0,0;1,4,linear,linear,0,0,0,0,0|x_max,0,1;0,6,linear,linear,0,0,0,0,0;1,12,linear,linear,0,0,0,0,0:x,0;0,1", "scale", 1);
        Assert.Multiple(() =>
        {
            Assert.That(curve.Sample("x", 0, 0.25f), Is.EqualTo(3));
            Assert.That(curve.Sample("x", 0.5, 0.25f), Is.EqualTo(4.5));
            Assert.That(curve.Sample("x", 1, 0.25f), Is.EqualTo(6));
        });
    }

    [Test]
    public void Bezier_UsesStoredTangentHandlesAndSolvesTheirTimeCoordinate()
    {
        var curve = new VfxPreviewCurve("x_min,0,1;0,0,bezier,bezier,-0.333333333,0,0.333333333,1,0;1,1,bezier,bezier,-0.333333333,0,0.333333333,0,0:x,0;0,0", "scale", 1);
        Assert.That(curve.Sample("x", 0.5, 0), Is.EqualTo(0.875).Within(0.00001));
    }

    [Test]
    public void NegativeStart_PrewarmCreatesAgedParticlesBeforeTimeZero()
    {
        var conversion = Plan("<ranged_property name=\"StartTime\" min=\"-0.5\" max=\"-0.5\"/>", Constant("x", 10));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ParticleCount, Is.GreaterThan(2));
            Assert.That(simulation.Frame(Camera).Max(x => x.Time), Is.GreaterThanOrEqualTo(0.49));
        });
    }

    [Test]
    public void EmissionCurve_AndMaximumParticlesAreAppliedWithoutAccumulatingABacklog()
    {
        var conversion = Plan("<property name=\"MaximumParticles\" value=\"3\"/>",
            "x_min,0,1;0,0,linear,linear,0,0,0,0,0;1,100,linear,linear,0,0,0,0,0:x,0;0,0");
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0.05f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.LessThan(3));
        simulation.Seek(0.5f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(3));
        simulation.Seek(2, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void Seek_ReloadAndDifferentUiFramePartitionsProduceTheSameParticles()
    {
        var conversion = Plan("", Constant("x", 20));
        var incremental = new TotalWarVfxSimulation(conversion);
        incremental.Advance(0.1f, Matrix.Identity); incremental.Advance(0.15f, Matrix.Identity);
        var direct = new TotalWarVfxSimulation(conversion);
        direct.Seek(0.25f, Matrix.Identity);
        Assert.That(incremental.Frame(Camera), Is.EqualTo(direct.Frame(Camera)));
        direct.Seek(0, Matrix.Identity); direct.Seek(0.25f, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(incremental.Frame(Camera)));
    }

    [Test]
    public void PausedLocalParticlesFollowTheNewAttachment_WhileWorldParticlesStayAtTheirSpawn()
    {
        var local = new TotalWarVfxSimulation(Plan("<property name=\"ParticleSimulationSpace\" value=\"LOCAL_SPACE_POSITION_ONLY\"/>", Constant("x", 1)));
        local.Seek(0, Matrix.Identity);
        local.Advance(0, Matrix.CreateTranslation(5, 0, 0));
        Assert.That(local.Frame(Camera).Single().Transform.Translation.X, Is.EqualTo(5));
        var world = new TotalWarVfxSimulation(Plan("<property name=\"ParticleSimulationSpace\" value=\"WORLD_SPACE\"/>", Constant("x", 1)));
        world.Seek(0, Matrix.Identity);
        world.Advance(0, Matrix.CreateTranslation(5, 0, 0));
        Assert.That(world.Frame(Camera).Single().Transform.Translation.X, Is.EqualTo(0));
    }

    [Test]
    public void NestedAtlas_StaticCellsAndAnimatedFramesStayInsideTheAuthoredOuterCell()
    {
        var atlas = new VfxAtlas(Columns: 2, Rows: 2, OuterColumns: 2, OuterRows: 2, OuterCell: 3);
        Assert.That(TotalWarVfxSimulation.AtlasRectangle(atlas, 3, 0, 1, 0), Is.EqualTo(new Vector4(0.75f, 0.75f, 0.25f, 0.25f)));
        var animated = atlas with { Animated = true, Loop = false };
        Assert.That(TotalWarVfxSimulation.AtlasRectangle(animated, 0, 0, 1, 0.5), Is.EqualTo(new Vector4(0.5f, 0.75f, 0.25f, 0.25f)));
        Assert.That(TotalWarVfxSimulation.AtlasRectangle(animated, 0, 0, 1, 1), Is.EqualTo(new Vector4(0.75f, 0.75f, 0.25f, 0.25f)));
        Assert.That(TotalWarVfxSimulation.AtlasFrame(animated, 0, 1, 0.125), Is.EqualTo(0.5f));
        var restricted = animated with { RowRestricted = true, Loop = true };
        Assert.That(TotalWarVfxSimulation.AtlasFrame(restricted, 1, 1, 0.75), Is.EqualTo(0.5f));
        Assert.That(TotalWarVfxSimulation.AtlasRectangle(restricted, 1, 1, 1, 0.75), Is.EqualTo(new Vector4(0.5f, 0.75f, 0.25f, 0.25f)));
    }

    [Test]
    public void WorldSpaceSeek_SamplesTheAttachmentAtEachBirthInsteadOfTheFinalBonePose()
    {
        var simulation = new TotalWarVfxSimulation(Plan("<property name=\"ParticleSimulationSpace\" value=\"WORLD_SPACE\"/>", Constant("x", 10)));
        simulation.Seek(0.5f, Matrix.CreateTranslation(5, 0, 0), seconds => Matrix.CreateTranslation(seconds * 10, 0, 0));
        var positions = simulation.Frame(Camera).Select(x => x.Transform.Translation.X).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(positions.Min(), Is.EqualTo(0));
            Assert.That(positions.Max(), Is.GreaterThan(3));
            Assert.That(positions.Distinct().Count(), Is.GreaterThan(3));
        });
    }

    [Test]
    public void UnconfirmedVelocityStretch_IsDiagnosedAndKeepsTheAuthoredBaseSize()
    {
        var conversion = Plan($"<property name=\"ParticleInitialSpeed\">{Constant("x", 2)}</property>", Constant("x", 1), """
            <modifier fragment="vs_translation_velocity">
            <property name="orientation_mode" value="Camera-aligned with stretching factor"/>
            <property name="velocity_stretch_mul" value="100"/>
            <property name="velocity_stretch_cap" value="1000"/>
            </modifier>
            """);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Diagnostics.Any(x => x.Field == "velocity_stretch_mul" && x.ReasonKey == "Vfx.Preview.StretchUnsupported"), Is.True);
            Assert.That(simulation.Frame(Camera).Single().Transform.Up.Length(), Is.EqualTo(1).Within(0.00001));
        });
    }

    [Test]
    public void VelocityV2_StretchesMultiplicativelyAndCorrectsTheViewDirectionAndAttachmentScale()
    {
        var conversion = Plan($"<property name=\"ParticleInitialSpeed\">{Constant("x", 2)}</property><property name=\"ParticleSimulationSpace\" value=\"LOCAL_SPACE_POSITION_ROTATION\"/>", Constant("x", 1), """
            <modifier fragment="vs_translation_velocity_v2">
            <property name="orientation_mode" value="Camera-aligned with stretching factor"/>
            <property name="velocity_stretch_mul" value="0.5"/>
            <property name="velocity_stretch_cap" value="1000"/>
            </modifier>
            """);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().Transform.Up.Length(), Is.EqualTo(2).Within(0.00001));
        Assert.That(simulation.Frame(Camera with { CameraPosition = new(0, 10, 0) }).Single().Transform.Up.Length(),
            Is.EqualTo(1).Within(0.00001), "Looking along velocity must not stretch the quad.");
        simulation.Seek(0, Matrix.CreateScale(2));
        Assert.That(simulation.Frame(Camera).Single().Transform.Up.Length(), Is.EqualTo(4).Within(0.00001),
            "Attachment scale must not be counted again in the velocity multiplier.");
        simulation.Seek(0, Matrix.CreateScale(2, 3, 2));
        simulation.Frame(Camera);
        Assert.That(simulation.VelocityScaleUnsupported, Is.True);
        Assert.That(conversion.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.StretchUnsupported"), Is.False);
    }

    [TestCase(false, 2f)]
    [TestCase(true, 1f)]
    public void VelocityV2_RespectsTheAuthoredTrailModeAndStretchCap(bool trailMode, float expected)
    {
        var conversion = Plan($"<property name=\"ParticleInitialSpeed\">{Constant("x", 2)}</property>", Constant("x", 1), $$"""
            <modifier fragment="vs_translation_velocity_v2">
            <property name="orientation_mode" value="Camera-facing velocity-aligned"/>
            <property name="velocity_stretch_mul" value="0.5"/>
            <property name="velocity_stretch_cap" value="2"/>
            <property name="enable_trail_mode" value="{{(trailMode ? "true" : "false")}}"/>
            </modifier>
            """);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().Transform.Up.Length(), Is.EqualTo(expected).Within(0.00001));
    }

    [Test]
    public void VerticalForce_MatchesHeldOutNativeCohortsWithinBirthAndPixelUncertainty()
    {
        var assembly = typeof(TotalWarVfxSimulationTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeVerticalForce.json", StringComparison.Ordinal)))!;
        using var json = System.Text.Json.JsonDocument.Parse(stream);
        var data = json.RootElement;
        var rawForce = data.GetProperty("RawForceY").GetInt32();
        var scale = data.GetProperty("PixelsPerWorldUnit").GetDouble();
        var tolerance = data.GetProperty("PixelTolerance").GetDouble();
        var force = string.Join('|', new[] { "x", "y", "z" }.Select(axis => Constant(axis, axis == "y" ? rawForce : 0).Split(':')[0]))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var simulation = new TotalWarVfxSimulation(Plan($"<property name=\"ForceA\">{force}</property>", Constant("x", .2), life: 4));
        foreach (var point in data.GetProperty("Points").EnumerateArray())
        {
            var time = point.GetProperty("SourceSeconds").GetDouble();
            var lowAge = (float)(time - point.GetProperty("BirthUpperSeconds").GetDouble());
            var highAge = (float)(time - point.GetProperty("BirthLowerSeconds").GetDouble());
            simulation.Seek(lowAge, Matrix.Identity); var low = simulation.Frame(Camera).Single().Transform.Translation.Y * scale;
            simulation.Seek(highAge, Matrix.Identity); var high = simulation.Frame(Camera).Single().Transform.Translation.Y * scale;
            Assert.That(point.GetProperty("DisplacementPixels").GetDouble(), Is.InRange(low - tolerance, high + tolerance),
                $"Native force-only cohort {point.GetProperty("Cohort")}, source t={time:F4}s. Original WGC recording hash: {data.GetProperty("RawRecordingSha256").GetString()}");
        }
    }

    private static VfxConversion Plan(string extra, string emission, string modifiers = "", int life = 1)
    {
        var scale = string.Join('|', new[] { "x", "y", "z" }.Select(axis => Constant(axis, 1).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="test" type="continuous_mesh">
            <property name="EmissionRateCurve">{emission}</property><property name="ParticleLifeTime">{Constant("x", life)}</property>
            <property name="ParticleScale">{scale}</property><property name="InfiniteLife" value="true"/>{extra}
            </emitter></emitters><behaviour_chains><behaviour_chain id="test"><quality value="0"><modifier fragment="ps_texture">
            <texture_property name="diffuse" path="vfx/real.dds"/></modifier>{modifiers}</quality></behaviour_chain></behaviour_chains></vfx>
            """);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "sample.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        return conversion;
    }
    private static string Constant(string axis, double value) => $"{axis}_min,0,0;0,{value.ToString(System.Globalization.CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:{axis},0;0,0";
}
