#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxDefaultVerticalMotionTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 6, -10), new(0, 6, 0), Vector3.Up),
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 540f / 980, .1f, 100), new(0, 6, -10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [Test]
    public void OriginalConstantCombination_MatchesNativeColourAgeIntervals()
    {
        var assembly = typeof(VfxDefaultVerticalMotionTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("NativeDefaultVerticalMotion.json", StringComparison.Ordinal)))!;
        using var fixture = JsonDocument.Parse(stream);
        var data = fixture.RootElement;
        var snapshot = Snapshot();
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters.Single().ConstantForceDamping, Is.EqualTo(.1f));
        Assert.That(conversion.Emitters.Single().CalibratedVerticalAcceleration, Is.EqualTo(-9.81f).Within(.00001));
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        var simulation = new TotalWarVfxSimulation(conversion);
        foreach (var point in data.GetProperty("Points").EnumerateArray())
        {
            var projection = new List<double>();
            foreach (var age in point.GetProperty("AgeInterval").EnumerateArray())
            {
                simulation.Seek(age.GetSingle(), Matrix.Identity);
                var position = simulation.Frame(Camera).Single().Transform.Translation;
                projection.Add(point.GetProperty("OriginY").GetDouble() - position.Y * data.GetProperty("PixelsPerUnit").GetDouble());
                Assert.That(position.X, Is.Zero); Assert.That(position.Z, Is.Zero);
            }
            var observed = point.GetProperty("ObservedY").GetDouble();
            var tolerance = data.GetProperty("PixelTolerance").GetDouble();
            Assert.That(observed, Is.InRange(projection.Min() - tolerance, projection.Max() + tolerance), point.GetProperty("SourceImage").GetString());
        }
        Assert.That(VfxDocument.Read(snapshot.GameBytes).Write(), Is.EqualTo(snapshot.GameBytes));
    }

    [TestCase("speed")]
    [TestCase("damping")]
    [TestCase("force")]
    [TestCase("lifetime")]
    [TestCase("spawn-angle")]
    [TestCase("spawn-box")]
    [TestCase("rotation")]
    [TestCase("wind")]
    [TestCase("surface-normal")]
    public void OutsideTheOriginalControls_UsesParticleCurvesWhenTheBasisIsSupported(string control)
    {
        var original = Snapshot(); var document = VfxDocument.Read(original.GameBytes);
        var emitter = document.Root.Descendants("emitter").Single();
        void Set(string name, string value) => emitter.Elements().Single(node => (string?)node.Attribute("name") == name).Value = value;
        switch (control)
        {
            case "speed": Set("ParticleInitialSpeed", Scalar(-9)); break;
            case "damping": Set("ParticleDeceleration", Scalar(.2f)); break;
            case "force": Set("ForceA", Vector(new(0, -980, 0))); break;
            case "lifetime": Set("ParticleLifeTime", Scalar(.5f)); break;
            case "spawn-angle": emitter.Add(new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0))); break;
            case "spawn-box": emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaBox"), Vector(Vector3.One))); break;
            case "rotation": emitter.Add(new XElement("property", new XAttribute("name", "EmitterOrientation"), Vector(new(0, 0, 1)))); break;
            case "wind": emitter.Add(new XElement("property", new XAttribute("name", "WindVarianceFactor"), new XAttribute("value", 1))); break;
            case "surface-normal": emitter.Add(new XElement("property", new XAttribute("name", "UseSurfaceNormal"), new XAttribute("value", "true"))); break;
        }
        var bytes = document.Write(); var actualConversion = TotalWarVfxConverter.Convert(original with { GameBytes = bytes });
        var unsupported = control is "rotation" or "surface-normal";
        Assert.That(actualConversion.Emitters.Single().ParticleForceCurve, Is.EqualTo(!unsupported));
        Assert.That(actualConversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.EqualTo(unsupported));
        if (control == "wind")
        {
            Assert.That(actualConversion.Emitters.Single().ConstantForceDamping, Is.Null);
            Assert.That(actualConversion.Diagnostics.Any(item => item.Field == "WindVarianceFactor" && item.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
        }
        Set("ForceA", Vector(Vector3.Zero));
        var expected = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(original with { GameBytes = document.Write() })); expected.Seek(.25f, Matrix.Identity);
        var actual = new TotalWarVfxSimulation(actualConversion); actual.Seek(.25f, Matrix.Identity);
        if (unsupported) Assert.That(actual.Frame(Camera), Is.EqualTo(expected.Frame(Camera)));
        else Assert.That(actual.Frame(Camera).Single().Transform.Translation.Y,
            Is.LessThan(expected.Frame(Camera).Single().Transform.Translation.Y));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("scale")]
    [TestCase("rotation")]
    [TestCase("time")]
    public void OriginalCombination_RespectsTheExistingAttachmentAndTimeBounds(string control)
    {
        var snapshot = Snapshot(); var actualConversion = TotalWarVfxConverter.Convert(snapshot);
        var document = VfxDocument.Read(snapshot.GameBytes);
        document.Root.Descendants("emitter").Single().Elements().Single(node => (string?)node.Attribute("name") == "ForceA").Value = Vector(Vector3.Zero);
        var expectedConversion = TotalWarVfxConverter.Convert(snapshot with { GameBytes = document.Write() });
        if (control == "time")
        {
            VfxPreviewReference[] references = [new(new(1, 1), Vector3.Zero, Vector3.Zero, new(0, 0), 2,
                new(1, 1), new(0, 0), new(0, 0), Vector4.One, Vector4.One)];
            actualConversion.Emitters[0].References = references; expectedConversion.Emitters[0].References = references;
        }
        var root = control == "scale" ? Matrix.CreateScale(2) : control == "rotation" ? Matrix.CreateRotationZ(.3f) : Matrix.Identity;
        var actual = new TotalWarVfxSimulation(actualConversion); actual.Seek(.12f, root);
        var expected = new TotalWarVfxSimulation(expectedConversion); expected.Seek(.12f, root);
        Assert.That(actual.Frame(Camera), Is.EqualTo(expected.Frame(Camera)));
        Assert.That(actual.ForceBasisUnsupported, Is.True);
    }

    private static VfxPreviewSnapshot Snapshot()
    {
        var bytes = Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="original" type="continuous_mesh"><property name="InfiniteLife" value="true"/>
            <property name="EmissionRateCurve">{Scalar(1)}</property><property name="ParticleLifeTime">{Scalar(.4f)}</property>
            <property name="ParticleScale">{Vector(Vector3.One)}</property>
            <property name="ParticleInitialSpeed">{Scalar(-10)}</property><property name="ParticleDeceleration">{Scalar(.1f)}</property>
            <property name="ForceA">{Vector(new(0, -981.000061f, 0))}</property></emitter></emitters>
            <behaviour_chains><behaviour_chain id="original"><quality value="0"><modifier fragment="vs_translation"/>
            <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/marker.tga"/></modifier>
            </quality></behaviour_chain></behaviour_chains></vfx>
            """);
        byte[] texture = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 40, 255, 255, 255, 255];
        return new(bytes, "vfx/original-motion.xml", _ => texture);
    }
    private static string Scalar(float value) => Axis("x", value) + ":x,0;0,0";
    private static string Vector(Vector3 value) => Axis("x", value.X) + "|" + Axis("y", value.Y) + "|" + Axis("z", value.Z) + ":x,0;0,0|y,0;1,1|z,0;2,2";
    private static string Axis(string name, float value) => $"{name}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1";
}
