#nullable enable
using System.Globalization;
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
public class VfxConstantForceDampingTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 540f / 980, .1f, 50), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    public static IEnumerable<TestCaseData> NativeTraces()
    {
        var traces = ReadFixture().GetProperty("Traces").EnumerateArray().ToArray();
        for (var index = 0; index < traces.Length; index++)
            yield return new TestCaseData(index).SetName($"NativeConstantMotion_{traces[index].GetProperty("Phase").GetString()}_cohort{traces[index].GetProperty("Cohort")}_speed{traces[index].GetProperty("InitialSpeed")}_drag{traces[index].GetProperty("Damping")}");
    }

    [TestCaseSource(nameof(NativeTraces))]
    public void ConstantForceDamping_MatchesFrozenNativeTrajectory(int index)
    {
        var fixture = ReadFixture();
        var trace = fixture.GetProperty("Traces")[index];
        var snapshot = Snapshot(trace.GetProperty("ForceY").GetDouble(), trace.GetProperty("Damping").GetDouble(), trace.GetProperty("InitialSpeed").GetDouble());
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
        var maximum = 0d;
        foreach (var point in trace.GetProperty("Points").EnumerateArray())
        {
            simulation.Seek(point.GetProperty("Age").GetSingle(), Matrix.Identity);
            var position = simulation.Frame(Camera).Single().Transform.Translation;
            var projected = trace.GetProperty("OriginY").GetDouble() - position.Y * trace.GetProperty("PixelsPerUnit").GetDouble();
            var error = Math.Abs(projected - point.GetProperty("ObservedY").GetDouble());
            maximum = Math.Max(maximum, error);
            Assert.That(error, Is.LessThanOrEqualTo(fixture.GetProperty("PixelTolerance").GetDouble()),
                $"{trace.GetProperty("Phase")}, cohort {trace.GetProperty("Cohort")}, drag {trace.GetProperty("Damping")}, frame {point.GetProperty("SourceFrame")}, age {point.GetProperty("Age")}; raw {trace.GetProperty("RawSha256")}");
            Assert.That(position.X, Is.Zero.Within(.000001));
            Assert.That(position.Z, Is.Zero.Within(.000001));
        }
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
        WriteEvidence($"cpu-trace-{index}.json", new { Trace = index, Points = trace.GetProperty("Points").GetArrayLength(), MaximumNativeErrorPixels = maximum, ActualProductSimulation = true });
    }

    [Test]
    public void BoundedMotion_SeekAdvanceAndReloadRemainDeterministic()
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot(-200, 5, 1));
        var incremental = new TotalWarVfxSimulation(conversion);
        incremental.Advance(.125f, Matrix.Identity); incremental.Advance(.25f, Matrix.Identity); incremental.Advance(.5f, Matrix.Identity);
        var direct = new TotalWarVfxSimulation(conversion); direct.Seek(.875f, Matrix.Identity);
        Assert.That(incremental.Frame(Camera), Is.EqualTo(direct.Frame(Camera)));
        direct.Seek(.713f, Matrix.Identity); direct.Seek(.875f, Matrix.Identity);
        Assert.That(direct.Frame(Camera), Is.EqualTo(incremental.Frame(Camera)));
        var reloaded = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(Snapshot(-200, 5, 1))); reloaded.Seek(.875f, Matrix.Identity);
        Assert.That(reloaded.Frame(Camera), Is.EqualTo(direct.Frame(Camera)));
        Assert.That(conversion.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.ConstantForceDampingCandidate"), Is.True);
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
    }

    [Test]
    public void OrdinaryMotion_KeepsTheExistingOneOver120TickWithoutPresentationInterpolation()
    {
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(Snapshot(0, 1, 1)));
        simulation.Seek(.01f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().Transform.Translation.Y, Is.EqualTo(MathF.Exp(-1f / 120) / 120).Within(.0000001));
    }

    [Test]
    public void CalibratedVerticalMotion_KeepsTheLegacyAzimuthAndAuthoredBytes()
    {
        var snapshot = Snapshot(-200, 5, 1);
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.ConstantForceDamping, Is.EqualTo(5));
            Assert.That(emitter.SpawnAngle, Is.EqualTo(new VfxRange(0, 360)), "Restoring the motion candidate must not overwrite legacy spawn direction.");
            Assert.That(emitter.SpawnSpread, Is.EqualTo(new VfxRange(90, 90)));
            Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
        });
    }

    [TestCase(0f, true)]
    [TestCase(90f, false)]
    [TestCase(180f, false)]
    public void ExplicitSpreadArc_DoesNotExpandTheCalibratedMotionScope(float angle, bool calibrated)
    {
        var snapshot = Snapshot(-200, 5, 1);
        var document = VfxDocument.Read(snapshot.GameBytes);
        document.Layers.Single(layer => layer.Kind == "Emitter").Element.Add(new System.Xml.Linq.XElement("ranged_property",
            new System.Xml.Linq.XAttribute("name", "SpreadArc"), new System.Xml.Linq.XAttribute("min", angle), new System.Xml.Linq.XAttribute("max", angle)));
        var bytes = document.Write();
        var emitter = TotalWarVfxConverter.Convert(snapshot with { GameBytes = bytes }).Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.ConstantForceDamping.HasValue, Is.EqualTo(calibrated));
            Assert.That(emitter.SpawnAngle, Is.EqualTo(new VfxRange(-angle, -angle)));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase("force-range")]
    [TestCase("speed-range")]
    [TestCase("drag-range")]
    [TestCase("lifetime")]
    [TestCase("direction")]
    [TestCase("spread")]
    [TestCase("missing-direction")]
    [TestCase("emitter-rotation")]
    [TestCase("spawn-area")]
    [TestCase("random-speed")]
    [TestCase("dynamic-drag")]
    [TestCase("additional-force")]
    [TestCase("wind")]
    [TestCase("expanded-force-range")]
    [TestCase("expanded-speed-range")]
    [TestCase("expanded-drag-range")]
    [TestCase("expanded-mixed-profiles")]
    [TestCase("intermediate-force-range")]
    [TestCase("intermediate-drag-range")]
    [TestCase("intermediate-mixed-profiles")]
    public void OutsideTheAnalyticControls_UsesParticleCurvesExceptForUnsupportedForces(string control)
    {
        var original = control.StartsWith("intermediate-", StringComparison.Ordinal) ? Snapshot(-50, 1.5, .75)
            : control.StartsWith("expanded-", StringComparison.Ordinal) ? Snapshot(-50, 2, .5) : Snapshot(-200, 5, 1);
        var document = VfxDocument.Read(original.GameBytes);
        var emitter = document.Layers.Single(layer => layer.Kind == "Emitter").Element;
        void Set(string name, string value) => emitter.Elements().Single(element => (string?)element.Attribute("name") == name).Value = value;
        switch (control)
        {
            case "force-range": Set("ForceA", VectorCurve(-201)); break;
            case "speed-range": Set("ParticleInitialSpeed", ScalarCurve(2)); break;
            case "drag-range": Set("ParticleDeceleration", ScalarCurve(2)); break;
            case "lifetime": Set("ParticleLifeTime", ScalarCurve(5)); break;
            case "direction": emitter.Elements().Single(e => (string?)e.Attribute("name") == "SpawnAngle").SetAttributeValue("x", "30"); break;
            case "spread": emitter.Elements().Single(e => (string?)e.Attribute("name") == "SpawnSpread").SetAttributeValue("max", "120"); break;
            case "missing-direction": emitter.Elements().Single(e => (string?)e.Attribute("name") == "SpawnAngle").Remove(); break;
            case "emitter-rotation": emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "EmitterOrientation"), VectorCurve(20))); break;
            case "spawn-area": emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "SpawnAreaBox"), VectorCurve(1))); break;
            case "random-speed": Set("ParticleInitialSpeed", "x_min,0,0;0,0,linear,linear,0,0,0,0,1|x_max,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,1"); break;
            case "dynamic-drag": Set("ParticleDeceleration", "x_min,0,0;0,1,linear,linear,0,0,0,0,1;1,5,linear,linear,0,0,0,0,1:x,0;0,0"); break;
            case "additional-force": emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "ForceB"), VectorCurve(1))); break;
            case "wind": emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "WindVarianceFactor"), new System.Xml.Linq.XAttribute("value", "1"))); break;
            case "expanded-force-range": Set("ForceA", VectorCurve(-51)); break;
            case "expanded-speed-range": Set("ParticleInitialSpeed", ScalarCurve(1.25)); break;
            case "expanded-drag-range": Set("ParticleDeceleration", ScalarCurve(3)); break;
            case "expanded-mixed-profiles": Set("ParticleInitialSpeed", ScalarCurve(.75)); break;
            case "intermediate-force-range": Set("ForceA", VectorCurve(-51)); break;
            case "intermediate-drag-range": Set("ParticleDeceleration", ScalarCurve(4)); break;
            case "intermediate-mixed-profiles": Set("ParticleInitialSpeed", ScalarCurve(.5)); break;
        }
        var bytes = document.Write();
        var conversion = TotalWarVfxConverter.Convert(original with { GameBytes = bytes });
        var unsupported = control is "emitter-rotation" or "additional-force" or "wind";
        Assert.That(conversion.Emitters.Single().ConstantForceDamping, Is.Null);
        Assert.That(conversion.Emitters.Single().ParticleForceCurve, Is.EqualTo(!unsupported));
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.EqualTo(unsupported));
        Set("ForceA", VectorCurve(0));
        var expected = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(original with { GameBytes = document.Write() }));
        var actual = new TotalWarVfxSimulation(conversion);
        expected.Seek(.713f, Matrix.Identity); actual.Seek(.713f, Matrix.Identity);
        if (unsupported) Assert.That(actual.Frame(Camera), Is.EqualTo(expected.Frame(Camera)));
        else Assert.That(actual.Frame(Camera).Single().Transform.Translation.Y,
            Is.LessThan(expected.Frame(Camera).Single().Transform.Translation.Y));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("scale")]
    [TestCase("rotation")]
    [TestCase("time")]
    public void UnsupportedAttachmentOrReferenceTime_OmitsTheCombinedForce(string control)
    {
        var conversion = TotalWarVfxConverter.Convert(Snapshot(-200, 5, 1));
        var baseline = TotalWarVfxConverter.Convert(Snapshot(0, 5, 1));
        if (control == "time")
        {
            VfxPreviewReference[] references = [new(new(1, 1), Vector3.Zero, Vector3.Zero, new(0, 0), 2,
                new(1, 1), new(0, 0), new(0, 0), Vector4.One, Vector4.One)];
            conversion.Emitters[0].References = references; baseline.Emitters[0].References = references;
        }
        var attachment = control == "scale" ? Matrix.CreateScale(2) : control == "rotation" ? Matrix.CreateRotationZ(.3f) : Matrix.Identity;
        var actual = new TotalWarVfxSimulation(conversion); actual.Seek(.713f, attachment);
        var expected = new TotalWarVfxSimulation(baseline); expected.Seek(.713f, attachment);
        Assert.That(actual.Frame(Camera), Is.EqualTo(expected.Frame(Camera)));
        Assert.That(actual.ForceBasisUnsupported, Is.True);
    }

    [TestCase(-200, 0)]
    [TestCase(-200, 1)]
    [TestCase(200, 0)]
    [TestCase(200, 1)]
    [TestCase(-50, .5)]
    [TestCase(-50, 2)]
    [TestCase(50, .5)]
    [TestCase(50, 2)]
    [TestCase(-50, 0)]
    [TestCase(-50, 1)]
    [TestCase(-50, .75)]
    [TestCase(-50, 1.5)]
    [TestCase(50, 0)]
    [TestCase(50, 1)]
    [TestCase(50, .75)]
    [TestCase(50, 1.5)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void BoundedMotion_ActualPreviewPixelsFollowTheFrozenNativeTracks(int force, double speed)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit offscreen rendering opt-in is required.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            // Measure geometry before display exposure and bloom change the white marker's intensity.
            using var target = new RenderTarget2D(device, 540, 980, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            using var ruler = new EffekseerVfxPreview(device); ruler.Reload(Snapshot(0, 0, 0));
            var lower = Centre(ruler, 0, Matrix.CreateTranslation(0, -3, 0), null);
            // World-space particles retain their birth anchor; the second ruler needs a new birth.
            ruler.Reload(Snapshot(0, 0, 0));
            var upper = Centre(ruler, 0, Matrix.CreateTranslation(0, 3, 0), null);
            var gpuScale = (lower - upper) / 6;
            var gpuOrigin = (lower + upper) / 2;
            Assert.That(gpuScale, Is.InRange(84, 86));
            using var preview = new EffekseerVfxPreview(device);
            var fixture = ReadFixture();
            var rows = new List<object>();
            var maximumNative = 0d; var maximumCpu = 0d;
            var traces = fixture.GetProperty("Traces").EnumerateArray().Where(trace => trace.GetProperty("ForceY").GetInt32() == force && trace.GetProperty("InitialSpeed").GetDouble() == speed);
            foreach (var trace in traces)
            {
                var snapshot = Snapshot(force, trace.GetProperty("Damping").GetDouble(), speed);
                preview.Reload(snapshot);
                var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(snapshot));
                foreach (var index in trace.GetProperty("GpuPointIndices").EnumerateArray().Select(value => value.GetInt32()))
                {
                    var point = trace.GetProperty("Points")[index];
                    var age = point.GetProperty("Age").GetSingle();
                    var imageName = index == 0 || index == trace.GetProperty("Points").GetArrayLength() - 1
                        ? $"gpu-{trace.GetProperty("Phase")}-{force}-{speed}-{trace.GetProperty("Damping")}-{trace.GetProperty("Cohort")}-{index}.png" : null;
                    var centre = Centre(preview, age, Matrix.Identity, imageName);
                    simulation.Seek(age, Matrix.Identity);
                    var position = simulation.Frame(Camera).Single().Transform.Translation.Y;
                    var cpuProjected = gpuOrigin - gpuScale * position;
                    var nativeProjected = trace.GetProperty("OriginY").GetDouble() + (centre - gpuOrigin) * trace.GetProperty("PixelsPerUnit").GetDouble() / gpuScale;
                    var nativeError = Math.Abs(nativeProjected - point.GetProperty("ObservedY").GetDouble());
                    var cpuError = Math.Abs(centre - cpuProjected);
                    maximumNative = Math.Max(maximumNative, nativeError); maximumCpu = Math.Max(maximumCpu, cpuError);
                    rows.Add(new { Phase = trace.GetProperty("Phase").GetString(), Age = age, Cohort = trace.GetProperty("Cohort").GetInt32(), Damping = trace.GetProperty("Damping").GetDouble(), GpuY = centre, CpuProjectedY = cpuProjected, NativeProjectedY = nativeProjected, NativeY = point.GetProperty("ObservedY").GetDouble(), NativeError = nativeError, CpuError = cpuError });
                    if (nativeError > 4 || cpuError > 1) Centre(preview, age, Matrix.Identity, $"gpu-failure-{trace.GetProperty("Phase")}-{force}-{speed}-{trace.GetProperty("Damping")}-{trace.GetProperty("Cohort")}-{index}.png");
                    Assert.That(cpuError, Is.LessThanOrEqualTo(1), "Actual pixels must follow the production simulation within rasterization uncertainty.");
                    Assert.That(nativeError, Is.LessThanOrEqualTo(4), "Native 3px tolerance plus independent GPU centroid measurement uncertainty of 1px.");
                }
            }
            Assert.That(preview.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.ConstantForceDampingCandidate"), Is.True);
            WriteEvidence($"gpu-trajectory-{force}-{speed}.json", new { ActualProductPreviewAndGpu = true, NativeEvidenceReused = "Frozen R7, R9 and R10 captures", GpuScale = gpuScale, GpuOrigin = gpuOrigin, MaximumNativeErrorPixels = maximumNative, MaximumCpuErrorPixels = maximumCpu, Points = rows });
            preview.Reload(Snapshot(force, speed is .75 or 1.5 ? .75 : 1, speed));
            Centre(preview, 0, Matrix.CreateScale(2), null);
            Assert.That(preview.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.ForceBasisUnsupported"), Is.True,
                "An unsupported paused attachment must be diagnosed on the first actual draw.");

            double Centre(EffekseerVfxPreview effect, float seconds, Matrix attachment, string? imageName)
            {
                effect.Seek(seconds, attachment);
                device.SetRenderTarget(target); device.Clear(Color.Black); device.DepthStencilState = DepthStencilState.Default;
                effect.Draw(Camera); device.SetRenderTarget(null);
                var pixels = new Vector4[540 * 980]; target.GetData(pixels);
                var selected = Enumerable.Range(0, pixels.Length).Where(index => pixels[index].X > 200f / 255).ToArray();
                Assert.That(selected.Length, Is.GreaterThan(30), $"The actual renderer must display the controlled particle. Maximum linear red={pixels.Max(pixel => pixel.X)}, time={seconds}, attachment={attachment.Translation}.");
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                if (imageName != null && !string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    using var image = new Texture2D(device, 540, 980);
                    image.SetData(pixels.Select(pixel => new Color(pixel)).ToArray());
                    using var stream = File.Create(Path.Combine(output, imageName)); image.SaveAsPng(stream, 540, 980);
                }
                return selected.Average(index => index / 540);
            }
        }
        finally { graphics.Release(); }
    }

    private static JsonElement ReadFixture()
    {
        var assembly = typeof(VfxConstantForceDampingTests).Assembly;
        var traces = new List<JsonElement>();
        foreach (var fixture in new[] { "NativeConstantForceDamping.json", "NativeExpandedConstantForceDamping.json", "NativeIntermediateCrossConstantForceDamping.json" })
        {
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(fixture, StringComparison.Ordinal)))!;
            using var document = JsonDocument.Parse(stream);
            traces.AddRange(document.RootElement.GetProperty("Traces").EnumerateArray().Select(trace => trace.Clone()));
        }
        return JsonSerializer.SerializeToElement(new { PixelTolerance = 3, Traces = traces });
    }

    private static void WriteEvidence(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static VfxPreviewSnapshot Snapshot(double force, double damping, double speed)
    {
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2" future="keep"><emitters><emitter id="control" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="EmissionRateCurve">{ScalarCurve(.2)}</property>
            <property name="ParticleLifeTime">{ScalarCurve(4)}</property><property name="ParticleScale">{VectorCurve(.15, .15, .15)}</property>
            <property name="ParticleInitialSpeed">{ScalarCurve(speed)}</property><property name="ParticleDeceleration">{ScalarCurve(damping)}</property>
            <property name="ForceA">{VectorCurve(force)}</property><property name="SpawnAngle" x="0" y="0"/>
            <ranged_property name="SpawnSpread" min="90" max="90"/><property name="SpreadMethod" value="0"/>
            <property name="ParticleRelativeSpawnOrientation" value="RELATIVE_TO_VFX_UP_AXIS"/></emitter></emitters>
            <behaviour_chains><behaviour_chain id="control"><quality value="0"><modifier fragment="ps_texture">
            <texture_property name="diffuse" path="vfx/control.tga"/></modifier></quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(bytes, "vfx/control.xml", _ => texture);
    }
    private static string ScalarCurve(double value) => $"x_min,0,0;0,{Number(value)},linear,linear,0,0,0,0,1:x,0;0,0";
    private static string VectorCurve(double y, double x = 0, double z = 0) => $"x_min,0,0;0,{Number(x)},linear,linear,0,0,0,0,1|y_min,0,0;0,{Number(y)},linear,linear,0,0,0,0,1|z_min,0,0;0,{Number(z)},linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
