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
public class VfxSpriteFullLocalSpinTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly Vector3 Spin = new(0, 0, 200);
    private static readonly Vector3 BirthAngles = new(0, 0, 17);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
    private const string NativeSourceHash = "DCF3BEB9E262AC5565DAF6456FC17CD1FACDB3F511DAA0981B3011442E9ABB45";

    public static IEnumerable<TestCaseData> NativeTransportCases()
    {
        var assembly = typeof(VfxSpriteFullLocalSpinTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".NativeSpriteFullLocalTransport.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        Assert.That(document.RootElement.GetProperty("SourceSha256").GetString(), Is.EqualTo(NativeSourceHash));
        foreach (var item in document.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("NativeSpriteFullLocalTransport_" + item.GetProperty("index").GetInt32());
    }

    [TestCaseSource(nameof(NativeTransportCases))]
    public void IndependentTransportMatrix_MatchesActualOrdinaryCsOutputs(JsonElement item)
    {
        var previous = QMatrix(Read(item, "previous"));
        var current = QMatrix(Read(item, "A"));
        var delta = Multiply(Transpose(previous), current);
        var expectedRotation = Multiply(QMatrix(Read(item, "qAfterOwnSpin")), delta);
        var actualRotation = QMatrix(Read(item, "actualCsQuaternion"));
        Near(AsMatrix(actualRotation), AsMatrix(expectedRotation));
        var expectedOmega = Transform(Read(item, "omegaAfterZDamping"), delta);
        var actualOmega = Read(item, "actualCsOmega");
        Assert.That(Distance(expectedOmega, actualOmega), Is.LessThan(Tolerance),
            "The captured ordinary CS transports both angular state fields by the same node delta.");
        var reversed = Multiply(Transpose(current), previous);
        Assert.That(Distance(Transform(Read(item, "omegaAfterZDamping"), reversed), actualOmega), Is.GreaterThan(.05),
            "These noncommuting native controls must distinguish the reverse transport order.");
    }

    [TestCase(.05f)]
    [TestCase(.125f)]
    [TestCase(.3f)]
    public void PoxParameters_KeepTheAuthoredSpinAndTransportThroughTwoNoncommutingNodeTurns(float time)
    {
        var bytes = Source(); var converted = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().Kind, Is.EqualTo(0));
            Assert.That(converted.Emitters.Single().SpinMin, Is.EqualTo(Spin));
            Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Spin));
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.False);
        });
        var simulation = new TotalWarVfxSimulation(converted);
        simulation.Seek(time, TwoTurns(time), TwoTurns);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(time, 1, TwoTurns));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void SecondTick_DampsWorldZAfterTheFirstTurnInsteadOfDampingTheTransportedLocalAxis()
    {
        var time = (float)(2 * Tick);
        var simulation = Simulation(Source()); simulation.Seek(time, OneTurn(time), OneTurn);
        var expected = Oracle(time, 1, OneTurn);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        var uniform = Oracle(time, 1, OneTurn, uniformDamping: true);
        Assert.That(Vector3.Distance(expected.Up, uniform.Up), Is.GreaterThan(.004f),
            "After the first Y90 turn, world-X angular velocity must survive the next world-Z damping step.");
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void ReferenceSpeed_ChangesTheSpinClockWhileNodeTransportKeepsThePreviewClock(float speed)
    {
        const float time = .25f;
        var child = Source(); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("diagnostic-full-local-child", VfxDocument.Read(child)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var converted = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/full-local-reference.xml", _ => child));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(converted);
        simulation.Seek(time, TwoTurns(time), TwoTurns);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(time, speed, TwoTurns));
    }

    [Test]
    public void PartialFrames_OnlyProjectTheCurrentNodeAndDoNotCommitAngularOrRootState()
    {
        var bytes = Source(); var simulation = Simulation(bytes);
        simulation.Seek(.004f, SmoothRoot(.004f), SmoothRoot);
        var first = simulation.Frame(Camera).Single();
        Near(first.Transform, Oracle(.004f, 1, SmoothRoot));
        for (var index = 0; index < 8; index++) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first));
        simulation.Seek(.007f, SmoothRoot(.007f), SmoothRoot); simulation.Frame(Camera);
        simulation.Seek(.012f, SmoothRoot(.012f), SmoothRoot);
        var direct = Simulation(bytes); direct.Seek(.012f, SmoothRoot(.012f), SmoothRoot);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(direct.Frame(Camera).Single().Transform, Oracle(.012f, 1, SmoothRoot));
    }

    [Test]
    public void RewindAndPartitionedAdvance_RestoreTheSameTransportedAngularState()
    {
        var bytes = Source(); var simulation = Simulation(bytes);
        simulation.Seek(.375f, TwoTurns(.375f), TwoTurns);
        var expected = simulation.Frame(Camera).Single().Transform;
        Near(expected, Oracle(.375f, 1, TwoTurns));
        simulation.Seek(.11f, TwoTurns(.11f), TwoTurns);
        simulation.Seek(.237f, TwoTurns(.237f), TwoTurns);
        simulation.Seek(.375f, TwoTurns(.375f), TwoTurns);
        Near(simulation.Frame(Camera).Single().Transform, expected);
        var partitioned = Simulation(bytes); partitioned.Seek(0, TwoTurns(0), TwoTurns);
        foreach (var elapsed in new[] { .11f, .127f, .138f })
        {
            var next = partitioned.Time + elapsed;
            partitioned.Seek(next, TwoTurns(next), TwoTurns);
        }
        Near(partitioned.Frame(Camera).Single().Transform, expected);
    }

    [Test]
    public void PausedSameTimeRootChange_ProjectsWithoutCommittingPoseOrTransportedOmega()
    {
        const float pausedAt = .375f;
        var changedRoot = Matrix.CreateScale(1.5f) * Matrix.CreateRotationY(-.9f)
            * Matrix.CreateRotationX(.4f) * Matrix.CreateRotationZ(-.2f);
        var simulation = Simulation(Source()); simulation.Seek(pausedAt, TwoTurns(pausedAt), TwoTurns);
        var delta = Multiply(Transpose(UnitBasis(TwoTurns(pausedAt))), UnitBasis(changedRoot));
        var expected = Oracle(pausedAt, 1, TwoTurns) * AsMatrix(delta);
        simulation.SeekWithCurrentAttachment(pausedAt, changedRoot, TwoTurns);
        var projected = simulation.Frame(Camera).Single(); Near(projected.Transform, expected);
        for (var index = 0; index < 8; index++) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(projected));
        Matrix EditedHistory(float time) => time > pausedAt ? changedRoot : TwoTurns(time);
        var next = pausedAt + (float)Tick;
        simulation.Seek(next, changedRoot, EditedHistory);
        var direct = Simulation(Source()); direct.Seek(next, changedRoot, EditedHistory);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(next, 1, EditedHistory));
    }

    [TestCase("birth-x")]
    [TestCase("relative")]
    public void UnprovedFullLocalBirthBases_KeepTheirExplicitDiagnostic(string combination)
    {
        var document = VfxDocument.Read(Source()); var emitter = Emitter(document);
        if (combination == "birth-x")
            foreach (var name in new[] { "ParticleOrientationMin", "ParticleOrientationMax" }) SetVector(emitter, name, new(15, 0, 17));
        else emitter.Add(Property("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS"));
        var converted = Convert(document.Write());
        Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
        Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
            && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
    }

    [Test]
    public void IndependentPureZFullLocal_DoesNotShiftNonAngularBirthRandomnessFromTheDynamicSpritePath()
    {
        var local = VfxDocument.Read(Source());
        var emitter = Emitter(local);
        Set(emitter, "MaximumParticles", "32");
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = Curve("x", 8);
        emitter.Add(new XElement("property", new XAttribute("name", "SpawnAreaBox"), Curve("xyz", 3)));
        SetVector(emitter, "ParticleOrientationMax", new(0, 0, 360));
        local.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_cell_texture"),
            Property("number_of_cells_x", 2), Property("number_of_cells_y", 2), Property("randomize_cell", "true")));
        var world = VfxDocument.Read(local.Write()); Set(Emitter(world), "ParticleSimulationSpace", "WORLD_SPACE");
        var actual = Simulation(local.Write()); var baseline = Simulation(world.Write());
        actual.Seek(1.1f, Matrix.Identity); baseline.Seek(1.1f, Matrix.Identity);
        var frame = actual.Frame(Camera); var expected = baseline.Frame(Camera);
        Assert.That(frame, Has.Length.EqualTo(expected.Length)); Assert.That(frame.Length, Is.GreaterThan(4));
        for (var index = 0; index < frame.Length; index++)
            Assert.Multiple(() =>
            {
                Assert.That(frame[index].Transform.Translation, Is.EqualTo(expected[index].Transform.Translation));
                Assert.That(frame[index].Colour, Is.EqualTo(expected[index].Colour));
                Assert.That(frame[index].UV, Is.EqualTo(expected[index].UV));
                Assert.That(frame[index].Time, Is.EqualTo(expected[index].Time));
                Assert.That(frame[index].AppearancePhase, Is.EqualTo(expected[index].AppearancePhase));
            });
    }

    [Test]
    public void FiniteEmitter_KeepsTheOriginalParticleLifetime()
    {
        var document = VfxDocument.Read(Source()); Set(Emitter(document), "InfiniteLife", "false");
        Emitter(document).Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .1), new XAttribute("max", .1)));
        var simulation = Simulation(document.Write()); simulation.Seek(.4f, TwoTurns(.4f), TwoTurns);
        Near(simulation.Frame(Camera).Single().Transform, Oracle(.4f, 1, TwoTurns));
        simulation.Seek(.81f, TwoTurns(.81f), TwoTurns);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void DynamicEmitterOrientation_RemainsExplicitlyUnsupportedForThisFullLocalBranch()
    {
        var document = VfxDocument.Read(Source());
        var orientation = Emitter(document).Elements().Single(e => (string?)e.Attribute("name") == "EmitterOrientation");
        orientation.Value = orientation.Value.Replace("0,90,linear,linear,0,0,0,0,1", "0,90,linear,linear,0,0,0,0,1;1,180,linear,linear,0,0,0,0,1", StringComparison.Ordinal);
        var converted = Convert(document.Write());
        Assert.That(converted.Emitters.Single().SpinMin, Is.EqualTo(Vector3.Zero));
        Assert.That(converted.Emitters.Single().SpinMax, Is.EqualTo(Vector3.Zero));
        Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
            && d.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
    }

    private static Matrix Oracle(float time, float sourceSpeed, Func<float, Matrix> roots, bool uniformDamping = false)
    {
        var birth = Matrix.CreateRotationZ(MathHelper.ToRadians(BirthAngles.Z));
        var rotation = Basis(birth);
        var radians = new[] { Spin.X * (MathHelper.Pi / 180), Spin.Y * (MathHelper.Pi / 180), Spin.Z * (MathHelper.Pi / 180) };
        var omega = FloatTransform(radians.Select(value => (double)value).ToArray(), rotation);
        var previous = roots(0); var previousRotation = UnitBasis(previous);
        var committed = 0d;
        void Step(double target, double delta)
        {
            var sourceDelta = (float)(delta * sourceSpeed); var decay = MathF.Exp(-50 * sourceDelta);
            if (uniformDamping) { omega[0] = (float)((float)omega[0] * decay); omega[1] = (float)((float)omega[1] * decay); }
            omega[2] = (float)((float)omega[2] * decay);
            var speed = Math.Sqrt(omega.Sum(value => value * value));
            if (speed > 0)
            {
                var x = omega[0]/speed; var y = omega[1]/speed; var z = omega[2]/speed;
                var angle = 2*Math.Atan(speed*sourceDelta/2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1-c;
                rotation = Multiply(rotation, new[,] { { c+x*x*t, x*y*t+z*s, x*z*t-y*s },
                    { x*y*t-z*s, c+y*y*t, y*z*t+x*s }, { x*z*t+y*s, y*z*t-x*s, c+z*z*t } });
            }
            var current = roots((float)target);
            if (current == previous) return;
            var currentRotation = UnitBasis(current);
            var transport = Multiply(Transpose(previousRotation), currentRotation);
            rotation = Multiply(rotation, transport);
            omega = FloatTransform(omega, transport);
            previous = current; previousRotation = currentRotation;
        }
        while (committed + Tick <= time + .000001) { committed += Tick; Step(committed, Tick); }
        if (time > committed) Step(time, time - committed);
        var root = roots(time);
        // Ordinary sprite geometry keeps its local Z scale at one.
        return Matrix.CreateScale(root.Right.Length(), root.Up.Length(), 1) * AsMatrix(rotation);
    }

    private static Matrix TwoTurns(float time)
    {
        var rotation = time + .0000001f < Tick ? Matrix.CreateRotationZ(.2f) * Matrix.CreateRotationX(-.3f)
            : time + .0000001f < 2*Tick ? Matrix.CreateRotationY(.8f) * Matrix.CreateRotationZ(-.4f)
            : Matrix.CreateRotationX(.6f) * Matrix.CreateRotationY(-.5f) * Matrix.CreateRotationZ(.7f);
        return Matrix.CreateScale(1.5f) * rotation;
    }
    private static Matrix OneTurn(float time) => Matrix.CreateScale(1.5f)
        * (time + .0000001f < Tick ? Matrix.Identity : Matrix.CreateRotationY(MathHelper.PiOver2));
    private static Matrix SmoothRoot(float time) => Matrix.CreateScale(1.5f)
        * Matrix.CreateRotationZ(.2f + time*.7f) * Matrix.CreateRotationY(-.3f + time*.9f);
    private static double[,] Basis(Matrix value) => new[,] { { (double)value.M11,value.M12,value.M13 },
        { (double)value.M21,value.M22,value.M23 }, { (double)value.M31,value.M32,value.M33 } };
    private static double[,] UnitBasis(Matrix value)
    {
        var result = Basis(value);
        for (var row = 0; row < 3; row++)
        {
            var length = Math.Sqrt(Enumerable.Range(0, 3).Sum(column => result[row,column]*result[row,column]));
            for (var column = 0; column < 3; column++) result[row,column] /= length;
        }
        return result;
    }
    private static double[] Read(JsonElement item, string name) => item.GetProperty(name).EnumerateArray().Select(value => value.GetDouble()).ToArray();
    private static double[,] QMatrix(double[] q)
    {
        var x=q[0]; var y=q[1]; var z=q[2]; var w=q[3];
        return new[,] { { 1-2*y*y-2*z*z, 2*x*y+2*z*w, 2*x*z-2*y*w },
            { 2*x*y-2*z*w, 1-2*x*x-2*z*z, 2*y*z+2*x*w },
            { 2*x*z+2*y*w, 2*y*z-2*x*w, 1-2*x*x-2*y*y } };
    }
    private static double[,] Transpose(double[,] value)
    {
        var result = new double[3,3];
        for (var row=0;row<3;row++) for (var column=0;column<3;column++) result[row,column]=value[column,row];
        return result;
    }
    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var result = new double[3,3];
        for (var row=0;row<3;row++) for (var column=0;column<3;column++) for (var index=0;index<3;index++)
            result[row,column] += a[row,index]*b[index,column];
        return result;
    }
    private static double[] Transform(double[] value, double[,] matrix) => Enumerable.Range(0,3)
        .Select(column => Enumerable.Range(0,3).Sum(row => value[row]*matrix[row,column])).ToArray();
    private static double[] FloatTransform(double[] value, double[,] matrix) => Enumerable.Range(0,3)
        .Select(column => (double)(float)(((float)value[0]*(float)matrix[0,column]
            + (float)value[1]*(float)matrix[1,column]) + (float)value[2]*(float)matrix[2,column])).ToArray();
    private static double Distance(double[] a, double[] b) => Math.Sqrt(a.Zip(b).Sum(pair => (pair.First-pair.Second)*(pair.First-pair.Second)));
    private static Matrix AsMatrix(double[,] value) => new((float)value[0,0],(float)value[0,1],(float)value[0,2],0,
        (float)value[1,0],(float)value[1,1],(float)value[1,2],0,(float)value[2,0],(float)value[2,1],(float)value[2,2],0,0,0,0,1);
    private static void Near(Matrix actual, Matrix expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(actual.Right, expected.Right), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Up, expected.Up), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Backward, expected.Backward), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Translation, expected.Translation), Is.LessThan(Tolerance));
        });
    }
    private static TotalWarVfxSimulation Simulation(byte[] bytes) => new(Convert(bytes));
    private static VfxConversion Convert(byte[] bytes)
    {
        var converted = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/pox-full-local-fixed-birth.xml", _ => [1]));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1), string.Join(';', converted.Diagnostics.Select(d => d.Field+":"+d.ReasonKey)));
        return converted;
    }
    private static XElement Emitter(VfxDocument document) => document.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value) => new("property", new XAttribute("name",name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null,CultureInfo.InvariantCulture) : value));
    private static void Set(XElement emitter, string name, string value) => emitter.Elements()
        .Single(element => (string?)element.Attribute("name") == name).SetAttributeValue("value",value);
    private static XElement VectorProperty(string name, Vector3 value) => new("property",new XAttribute("name",name),
        new XAttribute("x",value.X),new XAttribute("y",value.Y),new XAttribute("z",value.Z));
    private static void SetVector(XElement emitter,string name,Vector3 value) => emitter.Elements()
        .Single(element => (string?)element.Attribute("name") == name).ReplaceWith(VectorProperty(name,value));
    private static string Curve(string axes, double value) => string.Join('|',axes.Select(axis =>
        $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":"
        + string.Join('|',axes.Select((axis,index) => $"{axis},0;{index},{index}"));
    private static string OrientationCurve() => string.Join('|',new[] { ("x",90),("y",0),("z",0) }.SelectMany(pair =>
        new[] { $"{pair.Item1}_min,0,0;0,{pair.Item2},linear,linear,0,0,0,0,1", $"{pair.Item1}_max,0,0;0,{pair.Item2},linear,linear,0,0,0,0,1" }))
        + ":x,0;0,1|y,0;2,3|z,0;4,5";
    private static byte[] Source()
    {
        // Pox spin/damping/emitter parameters; fixed birth angle and lifespan isolate the transport diagnostic.
        var emitter = new XElement("emitter",new XAttribute("id","diagnostic-full-local"),new XAttribute("type","continuous_mesh"),
            Property("InfiniteLife","true"),Property("MaximumParticles",1),Property("ParticleSimulationSpace","LOCAL_SPACE_POSITION_ROTATION"),
            new XElement("property",new XAttribute("name","EmissionRateCurve"),Curve("x",.1)),
            new XElement("property",new XAttribute("name","ParticleLifeTime"),Curve("x",.8)),
            new XElement("property",new XAttribute("name","ParticleScale"),Curve("xyz",1)),
            new XElement("property",new XAttribute("name","EmitterOrientation"),OrientationCurve()),
            VectorProperty("ParticleOrientationMin",BirthAngles),VectorProperty("ParticleOrientationMax",BirthAngles),
            VectorProperty("ParticleSpin_Min",Spin),VectorProperty("ParticleSpin_Max",Spin),Property("ParticleSpinDirectionZ","CCW"),
            new XElement("property",new XAttribute("name","ParticleSpinDeceleration"),
                "x_min,8405056,0;0.0,50.0,bezier_c,bezier_c,-0.05,0.0,0.05,0.0,1|x_max,16728128,0;0.0,50.0,bezier_c,bezier_c,-0.05,0.0,0.05,0.0,1:x,0;0,1"));
        var quality = new XElement("quality",new XAttribute("value","0"),
            new XElement("modifier",new XAttribute("fragment","vs_translation"),Property("restriction",3)),
            new XElement("modifier",new XAttribute("fragment","ps_texture"),
                new XElement("texture_property",new XAttribute("name","diffuse"),new XAttribute("path","vfx/test.dds"))));
        return Encoding.UTF8.GetBytes(new XElement("vfx",new XAttribute("revision",2),new XElement("emitters",emitter),
            new XElement("behaviour_chains",new XElement("behaviour_chain",new XAttribute("id","diagnostic-full-local"),quality))).ToString(SaveOptions.DisableFormatting));
    }
}
