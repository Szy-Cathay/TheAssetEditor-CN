#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[Category("Diagnostic")]
public class VfxModelDynamicEmitterOrientationTests
{
    private const double Tick = 1.0 / 120;
    private const float Tolerance = .00001f;
    private static readonly Vector3 BirthAngles = new(-35, 50, 17);
    private static readonly Vector3 Spin = new(80, 120, 160);
    private static readonly Vector3 MinStart = new(-25, 5, 12), MinEnd = new(35, 120, -40);
    private static readonly Vector3 MaxStart = new(15, 28, 65), MaxEnd = new(100, 165, 80);
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(0f)]
    [TestCase(.375f)]
    [TestCase(1f)]
    public void Sampler_UsesNativeQuaternionBoundsRatherThanInterpolatingEulerAngles(float unary)
    {
        // Fresh official6d0fc0 fixture: inputs Xmin10,Xmax40,Ymin-15,Ymax80,Zmin25,Zmax-35.
        var minimum = new[] { .11250537633895874, -.10824471712112427, .22487781941890717, .9617980122566223 };
        var maximum = new[] { .43150949478149414, .497281014919281, -.42613309621810913, .6204205751419067 };
        var curve = VfxModelNativeOrientation.Create(OrientationText(new(10, -15, 25), new(10, -15, 25),
            new(40, 80, -35), new(40, 80, -35)), "EmitterOrientation");
        var actual = curve.Sample(.41, unary);
        Near(Matrix.CreateFromQuaternion(actual), FromBasis(QMatrix(Normalize(minimum.Zip(maximum)
            .Select(pair => pair.First + (pair.Second - pair.First) * unary).ToArray()))));
    }

    [TestCase(.17)]
    [TestCase(.51)]
    [TestCase(.89)]
    public void Sampler_BakesQuaternionKeysThenInterpolatesTimeAndOneSharedUnary(double phase)
    {
        var curve = VfxModelNativeOrientation.Create(OrientationText(), "EmitterOrientation");
        Near(Matrix.CreateFromQuaternion(curve.Sample(phase, .375f)), FromBasis(EmitterBasis(phase, .375f)));
        var euler = Vector3.Lerp(Vector3.Lerp(MinStart, MinEnd, (float)phase),
            Vector3.Lerp(MaxStart, MaxEnd, (float)phase), .375f);
        Assert.That(Vector3.Distance(FromBasis(EmitterBasis(phase, .375f)).Up, Birth(euler).Up), Is.GreaterThan(.01f));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DynamicEmitter_TransportsThePoseAndWorldOmegaInNativeNonCommutingOrder(bool movingRoot, bool relative)
    {
        var simulation = Simulation(Source(relative: relative));
        Func<float, Matrix> roots = movingRoot ? RootAt : _ => RootAt(0);
        simulation.Seek(.625f, roots(.625f), roots);
        Assert.That(EmitterDefinition(simulation).ModelQuaternionSpin, Is.True);
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, .625f, roots));
        Assert.That(simulation.ModelSpinBasisUnsupported, Is.False);
    }

    [Test]
    public void EmitterClock_UsesEmitterDurationRatherThanParticleLifetime()
    {
        var simulation = Simulation(Source(relative: true, spin: Vector3.Zero));
        simulation.Seek(.625f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, .625f, _ => Matrix.Identity));
        var wrongEmitter = EmitterBasis(.625 / 4, RandomRate(simulation));
        var wrong = FromBasis(Multiply(Basis(Birth()), wrongEmitter));
        Assert.That(Vector3.Distance(simulation.Frame(Camera).Single().Transform.Up, wrong.Up), Is.GreaterThan(.1f));
    }

    [TestCase(.5f)]
    [TestCase(2f)]
    public void ReferenceStartAndSpeed_ApplyToTheEmitterClockAndItsOwnSpin(float speed)
    {
        var child = VfxDocument.Read(Source(relative: true)); var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("orientation-child", child); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        reference.Selection.SetAttributeValue("start_time_min", .15f); reference.Selection.SetAttributeValue("start_time_max", .15f);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/dynamic-emitter-reference.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.625f, RootAt(.625f), RootAt);
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, .625f, RootAt));
    }

    [TestCase(0f)]
    [TestCase(.375f)]
    [TestCase(1f)]
    public void AllParticlesAndTicks_UseTheSameControllerOrientationUnary(float unary)
    {
        var simulation = Simulation(Source(relative: true, start: .05f)); simulation.Seek(0, RootAt(0), RootAt);
        SetRandomRate(simulation, unary);
        foreach (var time in new[] { .17f, .41f, .625f })
        {
            simulation.Seek(time, RootAt(time), RootAt);
            Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, time, RootAt));
            Assert.That(RandomRate(simulation), Is.EqualTo(unary));
        }
    }

    [Test]
    public void PartialFrames_UseTheDisplayEndpointWithoutCommittingEmitterOrAngularState()
    {
        var simulation = Simulation(Source(relative: true)); simulation.Seek(.004f, RootAt(.004f), RootAt);
        var frame = simulation.Frame(Camera).Single(); Near(frame.Transform, Expected(simulation, .004f, RootAt));
        for (var index = 0; index < 8; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(frame));
        simulation.Seek(.007f, RootAt(.007f), RootAt); simulation.Frame(Camera);
        simulation.Seek(.012f, RootAt(.012f), RootAt);
        var direct = Simulation(Source(relative: true)); direct.Seek(.012f, RootAt(.012f), RootAt);
        Near(simulation.Frame(Camera).Single().Transform, direct.Frame(Camera).Single().Transform);
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, .012f, RootAt));
    }

    [Test]
    public void SameTimeAttachmentChanges_OnlyTransportTheRootAndDoNotReplayEmitterDelta()
    {
        const float time = .25f;
        var simulation = Simulation(Source(relative: true)); simulation.Seek(time, RootAt(time), RootAt);
        var committed = simulation.Frame(Camera).Single().Transform;
        var changedRoot = Matrix.CreateRotationZ(-.7f) * Matrix.CreateRotationX(.9f);
        simulation.SeekWithCurrentAttachment(time, changedRoot, RootAt);
        var expected = FromBasis(Multiply(Basis(committed), Multiply(Transpose(RigidBasis(RootAt(time))), RigidBasis(changedRoot))));
        var frame = simulation.Frame(Camera).Single(); Near(frame.Transform, expected);
        for (var index = 0; index < 6; ++index) Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(frame));
        simulation.Seek(.45f, RootAt(.45f), RootAt);
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, .45f, RootAt));
    }

    [Test]
    public void SeekAndPartitionedSampling_RebuildTheSameDynamicEmitterHistory()
    {
        var simulation = Simulation(Source(relative: true)); simulation.Seek(.637f, RootAt(.637f), RootAt);
        var first = simulation.Frame(Camera).Single(); simulation.Seek(.11f, RootAt(.11f), RootAt);
        foreach (var delta in new[] { .127f, .2f, .2f })
        {
            var target = simulation.Time + delta;
            simulation.Seek(target, RootAt(target), RootAt);
        }
        Near(simulation.Frame(Camera).Single().Transform, first.Transform);
        Near(first.Transform, Expected(simulation, .637f, RootAt));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void BirthAndFollowLoops_UseIndependentFlagsInTheDeclaredPreviewParentMapping(bool infinite, bool birthLoop)
    {
        // Native birthloop is EmitterOrientationLoopAnim. Followloop additionally requires sourceInfiniteLife.
        // The preview root allows looping; this fixture does not infer arbitrary game parent scheduling.
        var simulation = Simulation(Source(relative: true, spin: Vector3.Zero, infinite: infinite, birthLoop: birthLoop, duration: .1f));
        simulation.Seek(.1875f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        Assert.That(typeof(VfxPreviewEmitter).GetProperty("ModelEmitterBirthLoop")?.GetValue(EmitterDefinition(simulation)) is true, Is.EqualTo(birthLoop));
        Assert.That(typeof(VfxPreviewEmitter).GetProperty("ModelEmitterFollowLoop")?.GetValue(EmitterDefinition(simulation)) is true, Is.EqualTo(infinite));
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, .1875f, _ => Matrix.Identity,
            birthLoop: birthLoop, followLoop: infinite));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LateBirth_UsesItsIndependentEmitterOrientationLoopFlag(bool birthLoop)
    {
        var source = VfxDocument.Read(Source(relative: true, spin: Vector3.Zero, infinite: true, birthLoop: birthLoop, duration: .1f));
        Emitter(source).Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value =
            "x_min,0,0;0,0,linear,linear,0,0,0,0,1;1,.1,linear,linear,0,0,0,0,1:x,0;0,0";
        var simulation = Simulation(source.Write()); simulation.Seek(10.125f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        var birth = (double)Particle(simulation).GetType().GetField("Birth")!.GetValue(Particle(simulation))!;
        Assert.That(birth, Is.GreaterThan(.1), "This control must actually be born after the emitter source duration.");
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, 10.125f, _ => Matrix.Identity,
            birthLoop: birthLoop, followLoop: true));
    }

    [Test]
    public void ExplicitParentStop_DisablesFollowWrappingWithoutChangingBirthFlag()
    {
        var child = VfxDocument.Read(Source(relative: true, spin: Vector3.Zero, infinite: true, birthLoop: true));
        var parent = VfxDocument.CreateComposition(); var reference = parent.AddReference("orientation-child", child); reference.Apply();
        reference.Selection.SetAttributeValue("infinite_life", "false");
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/orientation-loop-parent.xml", path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(1.25f, Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform, Expected(simulation, 1.25f, _ => Matrix.Identity,
            birthLoop: true, followLoop: false));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void DynamicEmitterPose_ReachesTheGpuAsItsIndependentStaticWorldPose(bool movingRoot, bool reflected)
    {
        const float time = .437f;
        var bytes = Source(relative: true); Func<float, Matrix> roots = movingRoot ? RootAt : _ => RootAt(0);
        var simulation = Simulation(bytes); simulation.Seek(time, roots(time), roots);
        AssertGpuPose(bytes, time, Expected(simulation, time, roots), reflected, roots);
    }

    private static Matrix Expected(TotalWarVfxSimulation simulation, float time, Func<float, Matrix> rootAt,
        bool birthLoop = false, bool followLoop = false)
    {
        var state = State(simulation); var particle = Particle(simulation);
        T Value<T>(object value, string name) => (T)value.GetType().GetField(name)!.GetValue(value)!;
        var start = Value<float>(state, "Start"); var duration = Value<float>(state, "Duration");
        var speed = Value<float>(state, "TimeScale"); var birthTime = Value<double>(particle, "Birth");
        var definition = EmitterDefinition(simulation); var unary = RandomRate(simulation);
        double Phase(double target, bool loop)
        {
            var phase = Math.Max(0, (target - start) * speed) / (duration * speed);
            return loop ? phase - Math.Floor(phase) : Math.Clamp(phase, 0, 1);
        }
        var previousRoot = RigidBasis(rootAt((float)birthTime));
        var previousEmitter = EmitterBasis(Phase(birthTime, followLoop), unary);
        var rotation = Basis(Birth());
        if (definition.RelativeOrientation)
            rotation = Multiply(Multiply(rotation, EmitterBasis(Phase(birthTime, birthLoop), unary)), previousRoot);
        var omega = Transform(new[] { (double)definition.SpinMin.X, definition.SpinMin.Y, definition.SpinMin.Z }, rotation)
            .Select(value => value * Math.PI / 180).ToArray();
        void Step(double target, double delta)
        {
            var sourceDt = delta * speed;
            for (var axis = 0; axis < 3; ++axis) omega[axis] *= Math.Exp(-.15 * sourceDt);
            var length = Math.Sqrt(omega.Sum(value => value * value));
            if (length > 0)
            {
                var x = omega[0] / length; var y = omega[1] / length; var z = omega[2] / length;
                var angle = 2 * Math.Atan(length * sourceDt / 2); var c = Math.Cos(angle); var s = Math.Sin(angle); var t = 1 - c;
                rotation = Multiply(rotation, new[,] { { c+x*x*t, x*y*t+z*s, x*z*t-y*s },
                    { x*y*t-z*s, c+y*y*t, y*z*t+x*s }, { x*z*t+y*s, y*z*t-x*s, c+z*z*t } });
            }
            var currentRoot = RigidBasis(rootAt((float)target)); var currentEmitter = EmitterBasis(Phase(target, followLoop), unary);
            var deltaRotation = Product(Transpose(currentRoot), Transpose(previousEmitter), currentEmitter,
                currentRoot, Transpose(previousRoot), currentRoot);
            rotation = Multiply(rotation, deltaRotation); omega = Transform(omega, deltaRotation);
            previousRoot = currentRoot; previousEmitter = currentEmitter;
        }
        var committed = birthTime;
        while (committed + Tick <= time + .000001) { committed += Tick; Step(committed, Tick); }
        if (time > committed) Step(time, time - committed);
        return FromBasis(rotation);
    }

    private static double[,] EmitterBasis(double phase, float unary)
    {
        phase = Math.Clamp(phase, 0, 1); var position = phase * 127; var lower = (int)Math.Floor(position);
        var upper = Math.Min(127, lower + 1); var fraction = position - lower;
        double[] Key(int index, bool high)
        {
            var start = high ? MaxStart : MinStart; var end = high ? MaxEnd : MinEnd; var t = index / 127.0;
            return EulerQuaternion(new[] { start.X + (end.X-start.X)*t, start.Y + (end.Y-start.Y)*t, start.Z + (end.Z-start.Z)*t });
        }
        var minA = Key(lower, false); var minB = Key(upper, false); var maxA = Key(lower, true); var maxB = Key(upper, true);
        var quaternion = new double[4];
        for (var axis = 0; axis < 4; ++axis)
        {
            var low = minA[axis] + (minB[axis]-minA[axis])*fraction;
            var high = maxA[axis] + (maxB[axis]-maxA[axis])*fraction;
            quaternion[axis] = low + (high-low)*unary;
        }
        return QMatrix(Normalize(quaternion));
    }

    private static double[] EulerQuaternion(double[] degrees)
    {
        var x = degrees[0]*Math.PI/360; var y = degrees[1]*Math.PI/360; var z = degrees[2]*Math.PI/360;
        var sx = Math.Sin(x); var cx = Math.Cos(x); var sy = Math.Sin(y); var cy = Math.Cos(y); var sz = Math.Sin(z); var cz = Math.Cos(z);
        return new[] { sx*cy*cz-cx*sy*sz, cx*sy*cz+sx*cy*sz, cx*cy*sz-sx*sy*cz, cx*cy*cz+sx*sy*sz };
    }
    private static double[] Normalize(double[] q) { var length = Math.Sqrt(q.Sum(value => value*value)); return q.Select(value => value/length).ToArray(); }
    private static double[,] QMatrix(double[] q)
    {
        var x=q[0]; var y=q[1]; var z=q[2]; var w=q[3];
        return new[,] { { 1-2*y*y-2*z*z, 2*x*y+2*z*w, 2*x*z-2*y*w },
            { 2*x*y-2*z*w, 1-2*x*x-2*z*z, 2*y*z+2*x*w }, { 2*x*z+2*y*w, 2*y*z-2*x*w, 1-2*x*x-2*y*y } };
    }
    private static object State(TotalWarVfxSimulation simulation) => ((IList)typeof(TotalWarVfxSimulation)
        .GetField("_emitters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!)[0]!;
    private static object Particle(TotalWarVfxSimulation simulation) => ((IList)typeof(TotalWarVfxSimulation)
        .GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(simulation)!)[0]!;
    private static float RandomRate(TotalWarVfxSimulation simulation) => (float)State(simulation).GetType().GetField("RandomRate")!.GetValue(State(simulation))!;
    private static void SetRandomRate(TotalWarVfxSimulation simulation, float value) => State(simulation).GetType().GetField("RandomRate")!.SetValue(State(simulation), value);
    private static VfxPreviewEmitter EmitterDefinition(TotalWarVfxSimulation simulation) => (VfxPreviewEmitter)State(simulation).GetType().GetField("Definition")!.GetValue(State(simulation))!;
    private static TotalWarVfxSimulation Simulation(byte[] bytes)
    {
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "diagnostic/model-emitter-orientation.xml", VfxModelVariantTests.Resolve));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1)); Assert.That(conversion.Models, Has.Count.EqualTo(1));
        return new(conversion);
    }
    private static XElement Emitter(VfxDocument document) => document.Root.Descendants("emitter").Single();
    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name), new XAttribute("value", value));
    private static XElement VectorProperty(string name, Vector3 value) => new("property", new XAttribute("name", name), new XAttribute("x", value.X), new XAttribute("y", value.Y), new XAttribute("z", value.Z));
    private static string ScalarCurve(string axes, double value) => string.Join('|', axes.Select(axis => $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":" + string.Join('|', axes.Select((axis,index) => $"{axis},0;{index},{index}"));
    private static string OrientationText() => OrientationText(MinStart, MinEnd, MaxStart, MaxEnd);
    private static string OrientationText(Vector3 minStart, Vector3 minEnd, Vector3 maxStart, Vector3 maxEnd)
    {
        var channels = new List<string>(); var connections = new List<string>();
        var values = new[] { (minStart.X,minEnd.X,maxStart.X,maxEnd.X), (minStart.Y,minEnd.Y,maxStart.Y,maxEnd.Y), (minStart.Z,minEnd.Z,maxStart.Z,maxEnd.Z) };
        for (var axis=0; axis<3; ++axis)
        {
            var name = "xyz"[axis]; var (a,b,c,d)=values[axis];
            string Channel(string suffix,float start,float end) => $"{name}_{suffix},0,0;0,{start.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" + (start!=end ? $";1,{end.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1" : "");
            channels.Add(Channel("min",a,b)); channels.Add(Channel("max",c,d)); connections.Add($"{name},1;{axis*2},{axis*2+1}");
        }
        return string.Join('|', channels)+":"+string.Join('|',connections);
    }
    private static byte[] Source(bool relative, Vector3? spin=null, float start=0, bool infinite=false, bool birthLoop=false, float duration=1)
    {
        var rigid = VfxDocument.Read(VfxModelVariantTests.Source(1)).Root.Descendants("property_rigid_data").Single();
        var emitter = new XElement("emitter", new XAttribute("id", "diagnostic-model-orientation"), new XAttribute("type", "continuous_rigid"),
            Property("InfiniteLife",infinite ? "true":"false"), Property("MaximumParticles",1), Property("ParticleSimulationSpace","LOCAL_SPACE_POSITION_ROTATION"),
            new XElement("ranged_property",new XAttribute("name","StartTime"),new XAttribute("min",start),new XAttribute("max",start)),
            new XElement("ranged_property",new XAttribute("name","LifeTime"),new XAttribute("min",duration),new XAttribute("max",duration)),
            new XElement("property",new XAttribute("name","EmissionRateCurve"),ScalarCurve("x",.1)),
            new XElement("property",new XAttribute("name","ParticleLifeTime"),ScalarCurve("x",4)),
            new XElement("property",new XAttribute("name","ParticleScale"),ScalarCurve("xyz",1)),
            new XElement("property",new XAttribute("name","EmitterOrientation"),OrientationText()),
            Property("EmitterOrientationLoopAnim",birthLoop ? "true":"false"),
            VectorProperty("ParticleOrientationMin",BirthAngles),VectorProperty("ParticleOrientationMax",BirthAngles),
            VectorProperty("ParticleSpin_Min",spin ?? Spin),VectorProperty("ParticleSpin_Max",spin ?? Spin),
            Property("ParticleSpinDirectionX","CCW"),Property("ParticleSpinDirectionY","CCW"),Property("ParticleSpinDirectionZ","CCW"),
            new XElement("property",new XAttribute("name","ParticleSpinDeceleration"),ScalarCurve("x",.15)),
            Property("ParticleRelativeSpawnOrientation",relative ? "RELATIVE_TO_VFX_UP_AXIS":"DISABLED"),new XElement(rigid));
        var quality = new XElement("quality",new XAttribute("value","0"),new XElement("modifier",new XAttribute("fragment","vs_translation"),Property("restriction",0)));
        return Encoding.UTF8.GetBytes(new XElement("vfx",new XAttribute("revision",2),new XElement("emitters",emitter),
            new XElement("behaviour_chains",new XElement("behaviour_chain",new XAttribute("id","diagnostic-model-orientation"),quality))).ToString(SaveOptions.DisableFormatting));
    }

    private static double[,] Basis(Matrix m) => new[,] { { (double)m.M11,m.M12,m.M13 }, { (double)m.M21,m.M22,m.M23 }, { (double)m.M31,m.M32,m.M33 } };
    private static double[,] RigidBasis(Matrix m)
    {
        var x=new[] { (double)m.M11,m.M12,m.M13 }; var length=Math.Sqrt(x.Sum(v=>v*v)); for(var i=0;i<3;++i)x[i]/=length;
        var y=new[] { (double)m.M21,m.M22,m.M23 }; var projection=x.Zip(y).Sum(pair=>pair.First*pair.Second);
        for(var i=0;i<3;++i)y[i]-=projection*x[i]; length=Math.Sqrt(y.Sum(v=>v*v)); for(var i=0;i<3;++i)y[i]/=length;
        return new[,] { { x[0],x[1],x[2] }, { y[0],y[1],y[2] }, { x[1]*y[2]-x[2]*y[1],x[2]*y[0]-x[0]*y[2],x[0]*y[1]-x[1]*y[0] } };
    }
    private static double[,] Transpose(double[,] m) => new[,] { { m[0,0],m[1,0],m[2,0] }, { m[0,1],m[1,1],m[2,1] }, { m[0,2],m[1,2],m[2,2] } };
    private static double[,] Multiply(double[,] a,double[,] b) { var c=new double[3,3]; for(var i=0;i<3;++i)for(var j=0;j<3;++j)for(var k=0;k<3;++k)c[i,j]+=a[i,k]*b[k,j]; return c; }
    private static double[,] Product(params double[][,] matrices) { var result=matrices[0]; for(var i=1;i<matrices.Length;++i)result=Multiply(result,matrices[i]); return result; }
    private static double[] Transform(double[] v,double[,] m) => Enumerable.Range(0,3).Select(j=>Enumerable.Range(0,3).Sum(i=>v[i]*m[i,j])).ToArray();
    private static Matrix FromBasis(double[,] r) => new((float)r[0,0],(float)r[0,1],(float)r[0,2],0,(float)r[1,0],(float)r[1,1],(float)r[1,2],0,(float)r[2,0],(float)r[2,1],(float)r[2,2],0,0,0,0,1);
    private static Matrix Birth(Vector3? angles=null) { var a=angles ?? BirthAngles; return Matrix.CreateRotationX(MathHelper.ToRadians(a.X))*Matrix.CreateRotationY(MathHelper.ToRadians(a.Y))*Matrix.CreateRotationZ(MathHelper.ToRadians(a.Z)); }
    private static Matrix RootAt(float t) => Matrix.CreateRotationX(.6f)*Matrix.CreateRotationY(-.4f)*Matrix.CreateRotationX(.35f*t)*Matrix.CreateRotationY(-.6f*t)*Matrix.CreateRotationZ(.15f*t);
    private static void Near(Matrix actual,Matrix expected)
    {
        Assert.Multiple(()=> { Assert.That(Vector3.Distance(actual.Right,expected.Right),Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(actual.Up,expected.Up),Is.LessThan(Tolerance)); Assert.That(Vector3.Distance(actual.Backward,expected.Backward),Is.LessThan(Tolerance)); });
    }

    private static void AssertGpuPose(byte[] bytes,float time,Matrix expected,bool reflected,Func<float,Matrix> roots)
    {
        if(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST")!="1")Assert.Ignore("Native offscreen rendering requires explicit opt-in.");
        var angles=new Vector3(MathF.Atan2(expected.M23,expected.M33),MathF.Asin(-expected.M13),MathF.Atan2(expected.M12,expected.M11))*(180/MathHelper.Pi);
        Near(Birth(angles),expected);
        var reference=VfxDocument.Read(bytes); var emitter=Emitter(reference);
        emitter.Elements().Single(e=>(string?)e.Attribute("name")=="ParticleSimulationSpace").SetAttributeValue("value","WORLD_SPACE");
        emitter.Elements().Where(e=>(string?)e.Attribute("name") is "ParticleRelativeSpawnOrientation" or "EmitterOrientation" or "EmitterOrientationLoopAnim").Remove();
        foreach(var name in new[] { "ParticleOrientationMin","ParticleOrientationMax" })emitter.Elements().Single(e=>(string?)e.Attribute("name")==name).ReplaceWith(VectorProperty(name,angles));
        foreach(var name in new[] { "ParticleSpin_Min","ParticleSpin_Max" })emitter.Elements().Single(e=>(string?)e.Attribute("name")==name).ReplaceWith(VectorProperty(name,Vector3.Zero));
        var texture=VfxModelVariantPixelTests.Dds(new Color(70,180,230,155));
        byte[]? Resolve(string path)=>path.EndsWith(".dds",StringComparison.OrdinalIgnoreCase)?texture:VfxModelVariantTests.Resolve(path);
        VfxPreviewSnapshot Snapshot(byte[] data)=>new(data,"diagnostic/model-emitter-orientation.xml",Resolve);
        var targetPoint=Vector3.Transform(new(.35f,.35f,0),expected); var eye=targetPoint+expected.Backward*6+expected.Right*.4f+expected.Up*.2f;
        var camera=Camera with { View=Matrix.CreateLookAt(eye,targetPoint,expected.Up),Projection=Matrix.CreateOrthographic(3,3,.1f,100)*Matrix.CreateScale(reflected?-1:1,1,1),CameraPosition=eye };
        var graphics=new GraphicsDeviceServiceMock();
        try
        {
            var device=graphics.GraphicsDevice;
            Vector4[] Render(byte[] source,Func<float,Matrix>? attachments)
            {
                using var preview=new EffekseerVfxPreview(device);preview.Reload(Snapshot(source));
                if(attachments==null)preview.Seek(time,Matrix.Identity);else preview.Seek(time,attachments(time),attachments);
                using var target=new RenderTarget2D(device,128,128,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
                device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);preview.Draw(camera);device.SetRenderTarget(null);
                var pixels=new Vector4[128*128];target.GetData(pixels);return pixels;
            }
            var actual=Render(bytes,roots);var control=Render(reference.Write(),null);
            Assert.That(control.Count(pixel=>pixel.X+pixel.Y+pixel.Z>.05f),Is.GreaterThan(100));
            Assert.That(actual.Zip(control).Max(pair=>Vector4.Distance(pair.First,pair.Second)),Is.LessThanOrEqualTo(Tolerance));
        }
        finally { graphics.Release(); }
    }
}
