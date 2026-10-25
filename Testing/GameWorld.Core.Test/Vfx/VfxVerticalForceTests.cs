using System.Text;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxVerticalForceTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(2, 2, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase("speed")]
    [TestCase("drag")]
    [TestCase("rotation")]
    [TestCase("horizontal")]
    [TestCase("varying")]
    [TestCase("random")]
    public void ParticleForceCurves_ApplyBeyondTheOldConstantOnlyControls(string control)
    {
        var snapshot = Snapshot(control);
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var unsupported = control == "rotation";
        Assert.That(conversion.Emitters.Single().ParticleForceCurve, Is.EqualTo(!unsupported));
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.EqualTo(unsupported));
        var baseline = VfxDocument.Read(snapshot.GameBytes);
        baseline.Layers.Single(layer => layer.Kind == "Emitter").Fields.Single(field => field.Name == "ForceA").Element.Value = VectorCurve(0);
        var expected = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot with { GameBytes = baseline.Write() }));
        var actual = new TotalWarVfxSimulation(conversion);
        expected.Seek(.5f, Matrix.Identity); actual.Seek(.5f, Matrix.Identity);
        if (unsupported) Assert.That(actual.Frame(Camera), Is.EqualTo(expected.Frame(Camera)));
        else Assert.That(actual.Frame(Camera).Single().Transform.Translation.Y,
            Is.GreaterThan(expected.Frame(Camera).Single().Transform.Translation.Y));
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    [TestCase("scale")]
    [TestCase("rotation")]
    [TestCase("time")]
    public void UncalibratedAttachmentOrReferenceSpeed_ReportsAndOmitsTheForce(string control)
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot());
        if (control == "time")
            conversion.Emitters[0].References = [new(new(1, 1), Vector3.Zero, Vector3.Zero, new(0, 0), 2,
                new(1, 1), new(0, 0), new(0, 0), Vector4.One, Vector4.One)];
        var attachment = control == "scale" ? Matrix.CreateScale(2, 1, 1) : control == "rotation" ? Matrix.CreateRotationZ(.3f) : Matrix.Identity;
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(1, attachment);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.ForceBasisUnsupported, Is.True);
            Assert.That(simulation.Frame(Camera).Single().Transform.Translation, Is.EqualTo(Vector3.Zero));
        });
        if (control != "time")
        {
            simulation.Seek(0, Matrix.Identity); simulation.Seek(1, Matrix.Identity);
            Assert.That(simulation.ForceBasisUnsupported, Is.False);
            Assert.That(simulation.Frame(Camera).Single().Transform.Translation.Y, Is.EqualTo(.5f).Within(.005f));
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void CalibratedVerticalForce_ReachesTheSharedRendererAndMovesActualPixels()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit offscreen rendering opt-in is required.");
        var snapshot = Snapshot(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device); preview.Reload(snapshot);
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            var first = Centre(0, Matrix.Identity); var moved = Centre(1, Matrix.Identity);
            Assert.That(first - moved, Is.EqualTo(16).Within(1), "100 authored force units should produce about 0.5 scene units displacement after 1s, or 16px in this viewport.");
            preview.Reload(snapshot);
            var scaledStart = Centre(0, Matrix.CreateScale(.8f)); var scaledEnd = Centre(1, Matrix.CreateScale(.8f));
            Assert.That(scaledStart - scaledEnd, Is.EqualTo((first - moved) * .8).Within(1), "Native scale controls retain force-only motion under a positive uniform attachment scale.");
            preview.Reload(snapshot);
            var unsupportedStart = Centre(0, Matrix.CreateScale(2, 1, 1)); var unsupportedEnd = Centre(1, Matrix.CreateScale(2, 1, 1));
            Assert.That(unsupportedEnd, Is.EqualTo(unsupportedStart).Within(.1));
            Assert.That(preview.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.ForceBasisUnsupported"), Is.True);
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));

            double Centre(float seconds, Matrix attachment)
            {
                preview.Seek(seconds, attachment);
                using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
                preview.Draw(Camera); device.SetRenderTarget(null);
                var pixels = new Vector4[4096]; target.GetData(pixels);
                var indices = Enumerable.Range(0, pixels.Length).Where(index => pixels[index].X > .8f).ToArray();
                Assert.That(indices.Length, Is.GreaterThan(10), "The force control must visibly render.");
                return indices.Average(index => index / 64);
            }
        }
        finally { graphics.Release(); }
    }

    private static VfxPreviewSnapshot Snapshot(string control = "")
    {
        var force = VectorCurve(100, control == "horizontal" ? 20 : 0, control == "varying", control == "random");
        var extra = control switch
        {
            "speed" => $"<property name=\"ParticleInitialSpeed\">{ScalarCurve(1)}</property>",
            "drag" => $"<property name=\"ParticleDeceleration\">{ScalarCurve(1)}</property>",
            "rotation" => $"<property name=\"EmitterOrientation\">{VectorCurve(30)}</property>",
            _ => "",
        };
        var source = Encoding.UTF8.GetBytes($"""
            <vfx revision="2" future="keep"><emitters><emitter id="control" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{ScalarCurve(.2)}</property>
            <property name="ParticleLifeTime">{ScalarCurve(4)}</property><property name="ParticleScale">{VectorCurve(.15, .15, z: .15)}</property>
            <property name="ForceA">{force}</property>{extra}</emitter></emitters>
            <behaviour_chains><behaviour_chain id="control"><quality value="0"><modifier fragment="ps_texture">
            <texture_property name="diffuse" path="vfx/control.tga"/></modifier></quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(source, "vfx/control.xml", _ => texture);
    }

    private static string ScalarCurve(double value) => $"x_min,0,0;0,{Number(value)},linear,linear,0,0,0,0,1:x,0;0,0";
    private static string VectorCurve(double y, double x = 0, bool varying = false, bool random = false, double z = 0)
    {
        var vertical = $"y_min,0,0;0,{Number(y)},linear,linear,0,0,0,0,1";
        if (varying) vertical += ";1,200,linear,linear,0,0,0,0,1";
        var result = $"x_min,0,0;0,{Number(x)},linear,linear,0,0,0,0,1|{vertical}|z_min,0,0;0,{Number(z)},linear,linear,0,0,0,0,1";
        if (random) return result + $"|y_max,0,0;0,200,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,3|z,0;2,2";
        return result + ":x,0;0,0|y,0;1,1|z,0;2,2";
    }
    private static string Number(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
