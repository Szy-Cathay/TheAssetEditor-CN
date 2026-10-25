using System.Text;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxParticleForceCurveTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 20), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(40, 40, .1f, 100), new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void ParticleForceAndDrag_FollowNativeLifeCurves(int index)
    {
        var assembly = typeof(VfxParticleForceCurveTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeParticleForceCurves.json")))!;
        using var data = JsonDocument.Parse(stream);
        var trace = data.RootElement.GetProperty("Traces")[index];
        var parameters = trace.GetProperty("Case");
        var snapshot = Snapshot(parameters.GetProperty("speed").GetDouble(), parameters.GetProperty("force0").GetDouble(),
            parameters.GetProperty("force1").GetDouble(), parameters.GetProperty("drag0").GetDouble(), parameters.GetProperty("drag1").GetDouble());
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
        var uncertainty = data.RootElement.GetProperty("AgeUncertaintySeconds").GetSingle();
        var tolerance = data.RootElement.GetProperty("PixelTolerance").GetDouble();
        foreach (var point in trace.GetProperty("Points").EnumerateArray())
        {
            var age = point.GetProperty("Age").GetSingle();
            var projected = new List<double>();
            // Image-derived age has a recorded interval, separate from the recording clock.
            foreach (var sample in new[] { age - uncertainty, age, age + uncertainty })
            {
                simulation.Seek(sample, Matrix.Identity);
                var position = simulation.Frame(Camera).Single().Transform.Translation.Y;
                projected.Add(trace.GetProperty("OriginY").GetDouble() - position * trace.GetProperty("PixelsPerUnit").GetDouble());
            }
            Assert.That(point.GetProperty("ObservedY").GetDouble(), Is.InRange(projected.Min() - tolerance, projected.Max() + tolerance),
                $"{parameters.GetProperty("name")}, source frame {point.GetProperty("Frame")}");
        }
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    [Test]
    public void CurvedForce_SeekAndIncrementalPlaybackAgreeWithoutChangingTheSharedStep()
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot(8, -981, 0, .3, 2));
        Assert.That(conversion.Emitters.Single().ParticleForceCurve, Is.True);
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(2, Matrix.Identity);
        var incremental = new TotalWarVfxSimulation(conversion);
        for (var i = 0; i < 16; i++) incremental.Advance(.125f, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(incremental.Frame(Camera)));
        direct.Seek(.4f, Matrix.Identity); direct.Seek(2, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(incremental.Frame(Camera)));
        direct.Seek(.01f, Matrix.Identity);
        var first = direct.Frame(Camera);
        direct.Seek(.016f, Matrix.Identity);
        Assert.That(direct.Frame(Camera).Single().Transform.Translation, Is.EqualTo(first.Single().Transform.Translation));
    }

    [TestCase(.8f)]
    [TestCase(2f)]
    public void CurvedForce_UniformScalePreservesAuthoredLocalMotion(float scale)
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot(8, -981, 0, .3, 2));
        var baseline = new TotalWarVfxSimulation(conversion); baseline.Seek(2, Matrix.Identity);
        var actual = new TotalWarVfxSimulation(conversion); actual.Seek(2, Matrix.CreateScale(scale));
        Assert.That(actual.Frame(Camera).Single().Transform.Translation,
            Is.EqualTo(baseline.Frame(Camera).Single().Transform.Translation * scale));
        Assert.That(actual.ForceBasisUnsupported, Is.False);
        actual.Seek(0, Matrix.CreateScale(scale, 1, 1)); actual.Seek(2, Matrix.CreateScale(scale, 1, 1));
        Assert.That(actual.ForceBasisUnsupported, Is.True);
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void CurvedForce_ReachesTheRendererAtTheCpuPosition()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit offscreen rendering opt-in is required.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var snapshot = Snapshot(8, -981, 0, .3, 2);
            var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice); preview.Reload(snapshot);
            foreach (var age in new[] { .5f, 1.5f, 3f })
            {
                simulation.Seek(age, Matrix.Identity); preview.Seek(age, Matrix.Identity);
                using var target = new RenderTarget2D(graphics.GraphicsDevice, 400, 400, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                graphics.GraphicsDevice.SetRenderTarget(target); graphics.GraphicsDevice.Clear(Color.Black);
                preview.Draw(Camera); graphics.GraphicsDevice.SetRenderTarget(null);
                var pixels = new Vector4[400 * 400]; target.GetData(pixels);
                var visible = Enumerable.Range(0, pixels.Length).Where(i => pixels[i].X > .8f).ToArray();
                Assert.That(visible, Has.Length.GreaterThan(10));
                var expected = 199.5 - simulation.Frame(Camera).Single().Transform.Translation.Y * 10;
                Assert.That(visible.Average(i => i / 400), Is.EqualTo(expected).Within(1));
            }
        }
        finally { graphics.Release(); }
    }

    private static VfxPreviewSnapshot Snapshot(double speed, double force0, double force1, double drag0, double drag1)
    {
        string Channel(string name, double a, double b) => FormattableString.Invariant($"{name},0,1;0,{a},linear,linear,0,0,0,0,0;1,{b},linear,linear,0,0,0,0,0");
        string Scalar(double a, double b) => Channel("x_min", a, b) + "|" + Channel("x_max", a, b) + ":x,0;0,1";
        var force = string.Join("|", Channel("x_min", 0, 0), Channel("y_min", force0, force1), Channel("z_min", 0, 0)) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var source = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="control" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{Scalar(.2,.2)}</property>
            <property name="ParticleLifeTime">{Scalar(4,4)}</property>
            <property name="SpawnAngle" x="0" y="0"/><ranged_property name="SpawnSpread" min="90" max="90"/>
            <property name="ParticleRelativeSpawnOrientation" value="RELATIVE_TO_VFX_UP_AXIS"/>
            <property name="ParticleInitialSpeed">{Scalar(speed,speed)}</property>
            <property name="ForceA">{force}</property><property name="ParticleDeceleration">{Scalar(drag0,drag1)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="control"><quality value="0"><modifier fragment="ps_texture">
            <texture_property name="diffuse" path="vfx/control.tga"/></modifier></quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(source, "vfx/control.xml", _ => texture);
    }
}
