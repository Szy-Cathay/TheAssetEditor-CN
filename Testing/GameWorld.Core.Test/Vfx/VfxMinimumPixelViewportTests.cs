#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxMinimumPixelViewportTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float ReferenceScale = .8478705883026123f;
    private static readonly Vector3 Eye = new(520.0093994140625f, 8.134733200073242f, 528.4217529296875f);
    private static readonly Matrix NativeView = Matrix.Transpose(new Matrix(
        -.8988044261932373f, -.000010341406778024975f, .4383501410484314f, 235.7530975341797f,
        -.1783006340265274f, .9135465025901794f, -.36557066440582275f, 278.4620361328125f,
        -.40044936537742615f, -.40673452615737915f, -.8211012482643127f, 645.433837890625f,
        0, 0, 0, 1));
    private static readonly Matrix NativeProjection = Matrix.Transpose(new Matrix(
        3.1513705253601074f, 0, 0, 0, 0, 1.732050895690918f, 0, 0,
        0, 0, 1.000083327293396f, -1.000083327293396f, 0, 0, 1, 0));

    // Captured official blowpipe q1, frame55179 SHA0409dd2e9eaab0654d00de16fa4a3bcbcbebc53901b1c4f72d56ac34972626b4.
    // Actual VS0f1c7230... mode2/legacy, full viewport360x655, particle viewport180x327.
    // These freeze VS geometry inputs, not random birth or curve-baking parity. Rifle sourceX is recovered
    // from post-limit X using independently captured Y/(half(.2)*referenceScale); its projection factor is checked below.
    private sealed record NativeCase(Vector3 Position, Vector3 Velocity, Vector2 RawScale, float PivotY,
        float Stretch, float Limit, double Birth, float Life, Vector3[] Corners, Vector4[] Clips);

    private static readonly NativeCase Rifle = new(new(511.9713439941406f, 0, 512), new(-.16957411170005798f, 0, 0),
        new(.06132364645600319f, .16953271627426147f), 1, 10, 2, 704.6912841796875, .1940482258796692f,
        [new(511.9712219238281f, -.06330299377441406f, 512.03125f), new(512.75146484375f, -.06328964233398438f, 512.03125f),
         new(511.9712219238281f, .0631570816040039f, 511.9686584472656f), new(512.75146484375f, .06316757202148438f, 511.96868896484375f)],
        [new(.1259765625f, -.111114501953125f, 19.012996673583984f, 20.01141357421875f),
         new(-2.083984375f, -.352081298828125f, 18.700532913208008f, 19.698974609375f),
         new(.0394287109375f, .12860107421875f, 19.012996673583984f, 20.01141357421875f),
         new(-2.1705322265625f, -.11236572265625f, 18.700471878051758f, 19.69891357421875f)]);

    private static readonly NativeCase Arrow = new(new(511.973876953125f, 0, 512), new(-.16957412660121918f, 0, 0),
        new(.23039062321186066f, .16953271627426147f), .5f, 15, 0, 704.7063598632812, .3601205348968506f,
        [new(511.719482421875f, -.10326004028320312f, 512.0511474609375f), new(512.22802734375f, -.10329627990722656f, 512.0510864257812f),
         new(511.7194519042969f, .10316085815429688f, 511.9488220214844f), new(512.22802734375f, .1031503677368164f, 511.9488525390625f)],
        [new(.86651611328125f, -.109222412109375f, 19.1137752532959f, 20.1121826171875f),
         new(-.57403564453125f, -.26629638671875f, 18.910144805908203f, 19.9085693359375f),
         new(.72515869140625f, .2822265625f, 19.11383628845215f, 20.11224365234375f),
         new(-.71539306640625f, .125152587890625f, 18.910144805908203f, 19.9085693359375f)]);

    [TestCase(true)]
    [TestCase(false)]
    public void HalfResolution_UsesCapturedNativeViewportWithoutChangingTheUnrestrictedLayer(bool limited)
    {
        var native = limited ? Rifle : Arrow;
        var packet = Frame(native, "HALFRES_DEFERRED_ALPHA", 360);
        var units = new[] { new Vector2(1, 1), new(1, 0), new(0, 1), new(0, 0) };
        for (var i = 0; i < units.Length; ++i)
        {
            var point = units[i] - new Vector2(.5f, native.PivotY);
            var actual = Vector3.Transform(new Vector3(point, 0), packet.Transform);
            var clip = Vector4.Transform(Vector4.Transform(new Vector4(actual, 1), Camera(360).View), Camera(360).Projection);
            var expectedPixel = Pixel(native.Clips[i]);
            var actualPixel = Pixel(clip);
            Assert.Multiple(() =>
            {
                // Four world float ULPs at512 bound repeated native view/inverse-view arithmetic.
                Assert.That(Vector3.Distance(actual, native.Corners[i]), Is.LessThan(4f / 16384), $"native corner{i}");
                Assert.That(Vector2.Distance(actualPixel, expectedPixel), Is.LessThan(.004f), $"native clip{i}");
                var uv = new Vector2(packet.UV.X + units[i].X * packet.UV.Z, packet.UV.Y + (1 - units[i].Y) * packet.UV.W);
                Assert.That(uv.X, Is.EqualTo(units[i].X > 0 ? .125f : .09375f).Within(.0000001f));
                Assert.That(uv.Y, Is.EqualTo(units[i].Y > 0 ? .1666666716337204f : .2083333432674408f).Within(.0000001f));
            });
        }
    }

    [TestCase("FULLRES_DEFERRED_ALPHA")]
    [TestCase("HIRES_VFXUI3D")]
    public void OtherPasses_KeepTheFullViewportPixelFloor(string pass)
    {
        var packet = Frame(Rifle, pass, 360);
        var factor = NativeFactor(Rifle, 360);
        Assert.Multiple(() =>
        {
            Assert.That(packet.Transform.Right.Length(), Is.EqualTo(Rifle.RawScale.X * factor).Within(.000001f));
            Assert.That(packet.Transform.Up.Length(), Is.EqualTo(Rifle.RawScale.Y * factor * 2).Within(.000001f));
            Assert.That(NativeFactor(Rifle, 180), Is.EqualTo(2.301116704940796f).Within(.000002f));
        });
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void MissingViewport_RetainsTheUnscaledCompatibilityPath(float width)
    {
        var half = Frame(Rifle, "HALFRES_DEFERRED_ALPHA", width);
        var full = Frame(Rifle, "FULLRES_DEFERRED_ALPHA", width);
        Assert.Multiple(() =>
        {
            Assert.That(half.Transform, Is.EqualTo(full.Transform));
            Assert.That(half.Transform.Right.Length(), Is.EqualTo(Rifle.RawScale.X).Within(.000001f));
        });
    }

    private static Vector2 Pixel(Vector4 clip) => new((clip.X / clip.W + 1) * 180, (1 - clip.Y / clip.W) * 327.5f);

    private static float NativeFactor(NativeCase native, float width)
    {
        var distance = Vector3.Distance(native.Position, Eye);
        var edge = Vector4.Transform(new Vector4(native.RawScale, distance, 1), NativeProjection);
        var origin = Vector4.Transform(new Vector4(0, 0, distance, 1), NativeProjection);
        var pixels = (edge.X / edge.W - origin.X / origin.W) * width * .5f;
        return Math.Max(1, native.Limit / pixels);
    }

    private static CommonShaderParameters Camera(float width)
    {
        var h = Matrix.CreateScale(-1, 1, -1);
        return new(NativeView * h, h * NativeProjection, Eye, new(512, 0, 512), 0, 0, 0, 1, Vector3.One, [],
            ViewportWidth: width, ViewportHeight: 655);
    }

    private static EffekseerParticle Frame(NativeCase native, string pass, float viewportWidth)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var effect = document.Root.Element("vfx")!;
        var emitter = effect.Descendants("emitter").Single();
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "ParticleScale").Value = Scale(native.RawScale / ReferenceScale);
        var behaviour = effect.Descendants("quality").Single();
        behaviour.Add(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
            new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-facing velocity-aligned")),
            new XElement("property", new XAttribute("name", "pivot_x"), new XAttribute("value", .5f)),
            new XElement("property", new XAttribute("name", "pivot_y"), new XAttribute("value", native.PivotY)),
            new XElement("property", new XAttribute("name", "velocity_stretch_mul"), new XAttribute("value", native.Stretch))));
        behaviour.Add(new XElement("modifier", new XAttribute("fragment", "vs_cell_texture"),
            P("atlas_number_of_cells_x", 8), P("atlas_number_of_cells_y", 8), P("atlas_cell", 8),
            P("number_of_cells_x", 4), P("number_of_cells_y", 3), P("cell", 7)));
        if (native.Limit > 0) behaviour.Add(new XElement("modifier", new XAttribute("fragment", "vs_rendersize_limiter"), P("pixelsize_limit", native.Limit)));
        effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "scene_view_type"), new XAttribute("value", pass)),
            new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", "true")))));
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/native-minimum-viewport.xml", _ => [1]));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Matrix.Identity);
        var particle = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", Private)!.GetValue(simulation)!).Cast<object>().Single();
        void Set(string name, object value) => particle.GetType().GetField(name)!.SetValue(particle, value);
        Set("Position", Vector3.Zero); Set("Velocity", native.Velocity / ReferenceScale);
        Set("Root", Matrix.CreateScale(ReferenceScale) * Matrix.CreateTranslation(native.Position));
        Set("Birth", native.Birth); Set("Life", native.Life);
        typeof(TotalWarVfxSimulation).GetField("_requestedTime", Private)!.SetValue(simulation, 704.8602905273438);
        return simulation.Frame(Camera(viewportWidth)).Single();
    }

    private static XElement P(string name, float value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static string Scale(Vector2 scale) => string.Join('|', new[] { VfxLibraryVisibilityTests.Constant("x", scale.X).Split(':')[0],
        VfxLibraryVisibilityTests.Constant("y", scale.Y).Split(':')[0], VfxLibraryVisibilityTests.Constant("z", 1).Split(':')[0] })
        + ":x,0;0,0|y,0;1,1|z,0;2,2";
}
