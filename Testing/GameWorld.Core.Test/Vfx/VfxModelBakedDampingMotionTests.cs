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

[Category("Diagnostic")]
public class VfxModelBakedDampingMotionTests
{
    private static readonly Vector3 Spin = new(150, 220, 300);
    private static readonly Vector3 Angles = new(-35, 50, 17);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    public static IEnumerable<TestCaseData> NativeMotionCases()
    {
        foreach (var entry in VfxModelNativeCurveTests.NativeCases())
        {
            var item = (JsonElement)entry.Arguments[0]!;
            var source = item.GetProperty("Source").GetString()!;
            Assert.That(VfxCurve.TryParse(source, out var parsed), Is.True);
            if (!source.Contains(",bezier_c,", StringComparison.Ordinal)
                && !parsed!.Channels.SelectMany(c => c.Points).Any(p => p.Value < 0 || p.Value > 5)) continue;
            yield return new TestCaseData(item).SetName("NativeModelMotion_" + item.GetProperty("Name").GetString());
        }
    }

    [TestCaseSource(nameof(NativeMotionCases))]
    public void NativeBakedDamping_DrivesTheRigidQuaternionWithoutClearingAuthoredSpin(JsonElement item)
    {
        var bytes = Source(item.GetProperty("Source").GetString()!);
        var converted = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/native-model-baked-motion.xml", VfxModelVariantTests.Resolve));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        Assert.That(converted.Emitters.Single().ModelQuaternionSpin, Is.True);
        Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Spin));
        Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration" && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(0, Matrix.Identity);
        var list = (System.Collections.IList)typeof(TotalWarVfxSimulation)
            .GetField("_particles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(simulation)!;
        // Set only the sampled input unary; never read the product angular state to construct an expected result.
        list[0]!.GetType().GetField("RandomAlpha")!.SetValue(list[0], .375f);
        simulation.Seek(.637f, Matrix.Identity);
        var actual = simulation.Frame(Camera).Single().Transform;
        var expected = Oracle(item, .637f);
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(.00001f));
        });
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    private static Matrix Oracle(JsonElement item, float time)
    {
        var min = item.GetProperty("Minimum").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        var max = item.GetProperty("Maximum").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        var birth = Birth();
        var rotation = new[,] { { (double)birth.M11, birth.M12, birth.M13 },
            { (double)birth.M21, birth.M22, birth.M23 }, { (double)birth.M31, birth.M32, birth.M33 } };
        var radians = new[] { Spin.X * (MathHelper.Pi / 180), Spin.Y * (MathHelper.Pi / 180), Spin.Z * (MathHelper.Pi / 180) };
        var omega = Enumerable.Range(0, 3).Select(column => (double)(float)(
            (radians[0] * (float)rotation[0, column] + radians[1] * (float)rotation[1, column])
            + radians[2] * (float)rotation[2, column])).ToArray();
        var committed = 0.0;
        void Step(double target, double delta)
        {
            var sourceDelta = (float)delta;
            var position = Math.Clamp((float)(target / (4 + .000001)), 0, 1) * (min.Length - 1);
            var lower = (int)position; var upper = Math.Min(lower + 1, min.Length - 1); var fraction = position - lower;
            var first = min[lower] + (min[upper] - min[lower]) * fraction;
            var second = max[lower] + (max[upper] - max[lower]) * fraction;
            var lambda = first + (second - first) * .375f;
            var factor = (float)Math.Exp(-lambda * sourceDelta);
            for (var axis = 0; axis < 3; ++axis) omega[axis] = (float)((float)omega[axis] * factor);
            var speed = Math.Sqrt(omega.Sum(v => v*v));
            if (speed == 0) return;
            var x = omega[0]/speed; var y = omega[1]/speed; var z = omega[2]/speed;
            var angle = 2*Math.Atan(speed*sourceDelta/2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1-c;
            var turn = new[,] { { c+x*x*t, x*y*t+z*s, x*z*t-y*s },
                { x*y*t-z*s, c+y*y*t, y*z*t+x*s }, { x*z*t+y*s, y*z*t-x*s, c+z*z*t } };
            var result = new double[3,3];
            for (var row=0; row<3; ++row) for (var col=0; col<3; ++col)
                for (var k=0; k<3; ++k) result[row,col] += rotation[row,k]*turn[k,col];
            rotation = result;
        }
        const double tick = 1.0 / 120;
        while (committed + tick <= time + .000001) { committed += tick; Step(committed, tick); }
        if (time > committed) Step(time, time-committed);
        return new((float)rotation[0,0], (float)rotation[0,1], (float)rotation[0,2], 0,
            (float)rotation[1,0], (float)rotation[1,1], (float)rotation[1,2], 0,
            (float)rotation[2,0], (float)rotation[2,1], (float)rotation[2,2], 0, 0, 0, 0, 1);
    }

    private static Matrix Birth() => Matrix.CreateRotationX(MathHelper.ToRadians(Angles.X))
        * Matrix.CreateRotationY(MathHelper.ToRadians(Angles.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(Angles.Z));
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name),
        new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static string Curve(string axes, double value) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":" +
        string.Join('|', axes.Select((axis,index) => $"{axis},0;{index},{index}"));
    private static byte[] Source(string damping)
    {
        var rigid = VfxDocument.Read(VfxModelVariantTests.Source(1)).Root.Descendants("property_rigid_data").Single();
        var emitter = new XElement("emitter", new XAttribute("id","native-baked-model"), new XAttribute("type","continuous_rigid"),
            Property("InfiniteLife","true"), Property("MaximumParticles",1), Property("ParticleSimulationSpace","WORLD_SPACE"),
            new XElement("property",new XAttribute("name","EmissionRateCurve"),Curve("x",.1)),
            new XElement("property",new XAttribute("name","ParticleLifeTime"),Curve("x",4)),
            new XElement("property",new XAttribute("name","ParticleScale"),Curve("xyz",1)),
            VectorProperty("ParticleOrientationMin",Angles),VectorProperty("ParticleOrientationMax",Angles),
            VectorProperty("ParticleSpin_Min",Spin),VectorProperty("ParticleSpin_Max",Spin),
            Property("ParticleSpinDirectionX","CCW"),Property("ParticleSpinDirectionY","CCW"),Property("ParticleSpinDirectionZ","CCW"),
            new XElement("property",new XAttribute("name","ParticleSpinDeceleration"),damping),new XElement(rigid));
        var quality = new XElement("quality",new XAttribute("value","0"),new XElement("modifier",new XAttribute("fragment","vs_translation"),Property("restriction",0)));
        return Encoding.UTF8.GetBytes(new XElement("vfx",new XAttribute("revision",2),new XElement("emitters",emitter),
            new XElement("behaviour_chains",new XElement("behaviour_chain",new XAttribute("id","native-baked-model"),quality))).ToString(SaveOptions.DisableFormatting));
    }
}
