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
public class VfxExtendedDynamicSpinIntegrationTests
{
    private const double Tick = 1d/120;
    private const float Tolerance = .00001f;
    private const float Life = .8f;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity,Matrix.Identity,
        new(0,0,10),Vector3.Zero,0,0,0,1,Vector3.One,[]);

    public static IEnumerable<TestCaseData> NativeMotionCases()
    {
        using var document = Fixture();
        foreach (var item in document.RootElement.GetProperty("Cases").EnumerateArray())
        {
            var name = item.GetProperty("Name").GetString();
            if (name is not ("official-bear-exp3" or "diagnostic-exp3" or "diagnostic-linear-negative-positive")) continue;
            foreach (var local in new[] { false,true })
                yield return new TestCaseData(item.Clone(),local).SetName("NativeSpriteMotion_"+name+(local ? "_FullLocal" : "_World"));
        }
    }

    [TestCaseSource(nameof(NativeMotionCases))]
    public void ExtendedDynamicRows_DriveSpinFromIndependentNativeHalfTexels(JsonElement item,bool local)
    {
        var curve = item.GetProperty("Source").GetString()!;
        var bytes = Source(curve,local);
        var converted = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMax.Z,Is.EqualTo(200));
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"),Is.False);
        });
        var values = Values(item);
        float Sample(double phase) => NativeTextureValue(values,phase);
        Matrix Root(float time) => local ? RootAt(time) : Matrix.Identity;
        var simulation = new TotalWarVfxSimulation(converted);
        simulation.Seek(.004f,Root(.004f),Root);
        var partial = simulation.Frame(Camera).Single();
        Near(partial.Transform,Oracle(.004f,1,Root,Sample));
        for (var index=0;index<8;index++) Assert.That(simulation.Frame(Camera).Single(),Is.EqualTo(partial));
        simulation.Seek(.625f,Root(.625f),Root);
        var expected = simulation.Frame(Camera).Single().Transform;
        Near(expected,Oracle(.625f,1,Root,Sample));
        simulation.Seek(.1f,Root(.1f),Root);
        simulation.Seek(.625f,Root(.625f),Root);
        Near(simulation.Frame(Camera).Single().Transform,expected);
        Assert.That(VfxDocument.Read(bytes).Write(),Is.EqualTo(bytes));
    }

    [TestCase(.5f,false)]
    [TestCase(2f,false)]
    [TestCase(.5f,true)]
    [TestCase(2f,true)]
    public void BearCurveReferenceSpeed_UsesSourceAgeWhileRootTransportUsesPreviewTime(float speed,bool local)
    {
        using var fixture = Fixture();
        var item = fixture.RootElement.GetProperty("Cases").EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == "official-bear-exp3");
        var child = Source(item.GetProperty("Source").GetString()!,local);
        var parent = VfxDocument.CreateComposition();
        var reference = parent.AddReference("diagnostic-native-dynamic",VfxDocument.Read(child)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier",speed);
        var converted = TotalWarVfxConverter.Convert(new(parent.Write(),"diagnostic/dynamic-reference.xml",_ => child));
        Assert.That(converted.Emitters,Has.Count.EqualTo(1));
        var values = Values(item);
        Matrix Root(float time) => local ? RootAt(time) : Matrix.Identity;
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.25f,Root(.25f),Root);
        Near(simulation.Frame(Camera).Single().Transform,Oracle(.25f,speed,Root,phase => NativeTextureValue(values,phase)));
    }

    [Test]
    public void ExistingZeroToFiveDynamicRows_KeepTheirEstablishedDirectCurvePath()
    {
        const string curve = "x_min,0,0;0,0,linear,linear,0,0,0,0,1;1,.8,linear,linear,0,0,0,0,1|x_max,0,0;0,0,linear,linear,0,0,0,0,1;1,.8,linear,linear,0,0,0,0,1:x,0;0,1";
        var converted = Convert(Source(curve,false));
        var definition = converted.Emitters.Single();
        Assert.That(definition.DynamicSpinDamping,Is.True);
        var nativeProperty = definition.GetType().GetProperty("SpriteNativeSpinDamping");
        if (nativeProperty != null) Assert.That(nativeProperty.GetValue(definition),Is.Null);
        var simulation = new TotalWarVfxSimulation(converted); simulation.Seek(.625f,Matrix.Identity);
        Near(simulation.Frame(Camera).Single().Transform,
            Oracle(.625f,1,_ => Matrix.Identity,phase => (float)(.8*phase)));
    }

    [TestCase("single-channel",false)]
    [TestCase("single-channel",true)]
    [TestCase("bezier",false)]
    [TestCase("bezier",true)]
    public void UnprovedDynamicLayouts_KeepTheirDiagnosticAndDoNotEraseTheirSource(string layout,bool local)
    {
        const string row = "0,-1,linear,bezier_c,-.05,0,.05,0,1;1,.5,bezier_c,linear,-.05,0,.05,0,1";
        var curve = layout == "single-channel"
            ? "x_min,0,0;0,-1,linear,linear,0,0,0,0,1;1,.5,linear,linear,0,0,0,0,1:x,0;0,0"
            : $"x_min,0,0;{row}|x_max,0,0;{row}:x,0;0,1";
        var bytes = Source(curve,local); var converted = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters.Single().SpinMin,Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Emitters.Single().SpinMax,Is.EqualTo(Vector3.Zero));
            Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
                && d.ReasonKey == "Vfx.Preview.ForceUnsupported"),Is.True);
        });
        Assert.That(VfxDocument.Read(bytes).Write(),Is.EqualTo(bytes));
    }

    [Test]
    public void ProjectedDynamicOverflow_DoesNotDisableTheNextRepresentableCommittedTick()
    {
        const string row = "0,-32768,linear,linear,0,0,0,0,1;.05,-32768,linear,linear,0,0,0,0,1;.075,32768,linear,linear,0,0,0,0,1;1,32768,linear,linear,0,0,0,0,1";
        var bytes = Source($"x_min,0,0;{row}|x_max,0,0;{row}:x,0;0,1",false,.1f);
        var simulation = new TotalWarVfxSimulation(Convert(bytes)); simulation.Seek(.004f,Matrix.Identity);
        Assert.That(simulation.Frame(Camera),Is.Empty);
        Assert.That(simulation.SpriteSpinRangeUnsupported,Is.True);
        Assert.That(simulation.Frame(Camera),Is.Empty);
        simulation.Seek((float)Tick,Matrix.Identity);
        var actual = simulation.Frame(Camera);
        var direct = new TotalWarVfxSimulation(Convert(bytes)); direct.Seek((float)Tick,Matrix.Identity);
        Assert.That(actual,Has.Length.EqualTo(1));
        Near(actual.Single().Transform,direct.Frame(Camera).Single().Transform);
        Near(actual.Single().Transform,Matrix.CreateRotationZ(MathHelper.ToRadians(17)));
        Assert.That(simulation.SpriteSpinRangeUnsupported,Is.False);
        Assert.That(simulation.ModelSpinRangeUnsupported,Is.False);
    }

    [Test]
    public void ExtendedWorldDynamicWithBirthXY_RemainsExplicitlyUnsupported()
    {
        using var fixture = Fixture();
        var item = fixture.RootElement.GetProperty("Cases").EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == "official-bear-exp3");
        var document = VfxDocument.Read(Source(item.GetProperty("Source").GetString()!,false));
        foreach (var property in document.Root.Descendants("emitter").Single().Elements()
            .Where(element => (string?)element.Attribute("name") is "ParticleOrientationMin" or "ParticleOrientationMax"))
            property.SetAttributeValue("x",15);
        var converted = Convert(document.Write());
        Assert.That(converted.Emitters.Single().SpinMax,Is.EqualTo(Vector3.Zero));
        Assert.That(converted.Diagnostics.Any(d => d.Field == "ParticleSpinDeceleration"
            && d.ReasonKey == "Vfx.Preview.ForceUnsupported"),Is.True);
    }

    private static Matrix Oracle(float time,float sourceSpeed,Func<float,Matrix> roots,Func<double,float> sample)
    {
        var rotation = Basis(Matrix.CreateRotationZ(MathHelper.ToRadians(17)));
        var omega = new[] { 0d,0d,(double)MathHelper.ToRadians(200) };
        var previous = roots(0); var previousRotation = UnitBasis(previous); var committed=0d;
        void Step(double target,double dt)
        {
            var delta = (float)(dt*sourceSpeed);
            var phase = Math.Clamp(target*sourceSpeed/(Life+.000001),0,1);
            omega[2] = (float)((float)omega[2]*MathF.Exp(-sample(phase)*delta));
            var speed = Math.Sqrt(omega.Sum(value => value*value));
            if (speed > 0)
            {
                var x=omega[0]/speed; var y=omega[1]/speed; var z=omega[2]/speed;
                var angle=2*Math.Atan(speed*delta/2); var c=Math.Cos(angle); var s=Math.Sin(angle); var t=1-c;
                rotation=Multiply(rotation,new[,] { { c+x*x*t,x*y*t+z*s,x*z*t-y*s },
                    { x*y*t-z*s,c+y*y*t,y*z*t+x*s },{ x*z*t+y*s,y*z*t-x*s,c+z*z*t } });
            }
            var current=roots((float)target);
            if (current == previous) return;
            var currentRotation=UnitBasis(current);
            var transport=Multiply(Transpose(previousRotation),currentRotation);
            rotation=Multiply(rotation,transport);
            omega=Enumerable.Range(0,3).Select(column => (double)(float)(
                ((float)omega[0]*(float)transport[0,column]+(float)omega[1]*(float)transport[1,column])
                +(float)omega[2]*(float)transport[2,column])).ToArray();
            previous=current; previousRotation=currentRotation;
        }
        while (committed+Tick <= time+.000001) { committed+=Tick; Step(committed,Tick); }
        if (time>committed) Step(time,time-committed);
        var root=roots(time);
        // Ordinary sprite geometry keeps its local Z scale at one.
        return Matrix.CreateScale(root.Right.Length(),root.Up.Length(),1)*AsMatrix(rotation);
    }

    private static float NativeTextureValue(float[] values,double phase)
    {
        var texel=Math.Clamp((float)phase,0,1)*128d-.5;
        if (texel <= 0) return values[0];
        if (texel >= 127) return values[127];
        var index=(int)texel; var weight=texel-index;
        return (float)(values[index]*(1-weight)+values[index+1]*weight);
    }
    private static float[] Values(JsonElement item) => item.GetProperty("HalfBits").EnumerateArray()
        .Select(value => (float)BitConverter.UInt16BitsToHalf(value.GetUInt16())).ToArray();
    private static JsonDocument Fixture()
    {
        var assembly=typeof(VfxExtendedDynamicSpinIntegrationTests).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".NativeSpriteDynamicScalar.json",StringComparison.Ordinal)))!;
        return JsonDocument.Parse(stream);
    }
    private static Matrix RootAt(float time) => Matrix.CreateScale(1.5f)*(time+.0000001f<Tick
        ? Matrix.CreateRotationZ(.2f)*Matrix.CreateRotationX(-.3f)
        : time+.0000001f<2*Tick ? Matrix.CreateRotationY(.8f)*Matrix.CreateRotationZ(-.4f)
        : Matrix.CreateRotationX(.6f)*Matrix.CreateRotationY(-.5f)*Matrix.CreateRotationZ(.7f));
    private static double[,] Basis(Matrix m) => new[,] { { (double)m.M11,m.M12,m.M13 },{ (double)m.M21,m.M22,m.M23 },{ (double)m.M31,m.M32,m.M33 } };
    private static double[,] UnitBasis(Matrix m)
    {
        var result=Basis(m);
        for (var row=0;row<3;row++)
        {
            var length=Math.Sqrt(Enumerable.Range(0,3).Sum(column => result[row,column]*result[row,column]));
            for (var column=0;column<3;column++) result[row,column]/=length;
        }
        return result;
    }
    private static double[,] Transpose(double[,] a)
    {
        var result=new double[3,3];
        for (var r=0;r<3;r++) for (var c=0;c<3;c++) result[r,c]=a[c,r];
        return result;
    }
    private static double[,] Multiply(double[,] a,double[,] b)
    {
        var result=new double[3,3];
        for (var r=0;r<3;r++) for (var c=0;c<3;c++) for (var k=0;k<3;k++) result[r,c]+=a[r,k]*b[k,c];
        return result;
    }
    private static Matrix AsMatrix(double[,] m) => new((float)m[0,0],(float)m[0,1],(float)m[0,2],0,
        (float)m[1,0],(float)m[1,1],(float)m[1,2],0,(float)m[2,0],(float)m[2,1],(float)m[2,2],0,0,0,0,1);
    private static void Near(Matrix a,Matrix b) => Assert.Multiple(() =>
    {
        Assert.That(Vector3.Distance(a.Right,b.Right),Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(a.Up,b.Up),Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(a.Backward,b.Backward),Is.LessThan(Tolerance));
        Assert.That(Vector3.Distance(a.Translation,b.Translation),Is.LessThan(Tolerance));
    });
    private static VfxConversion Convert(byte[] bytes)
    {
        var converted=TotalWarVfxConverter.Convert(new(bytes,"diagnostic/extended-dynamic-spin.xml",_ => [1]));
        Assert.That(converted.Emitters,Has.Count.EqualTo(1),string.Join(';',converted.Diagnostics.Select(d => d.Field+":"+d.ReasonKey)));
        return converted;
    }
    private static XElement Property(string name,object value) => new("property",new XAttribute("name",name),
        new XAttribute("value",value is IFormattable number ? number.ToString(null,CultureInfo.InvariantCulture) : value));
    private static string Curve(string axes,double value) => string.Join('|',axes.Select(axis =>
        $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1"))+":"
        +string.Join('|',axes.Select((axis,index) => $"{axis},0;{index},{index}"));
    private static byte[] Source(string damping,bool local,float life=Life)
    {
        var emitter=new XElement("emitter",new XAttribute("id","diagnostic-native-dynamic"),new XAttribute("type","continuous_mesh"),
            Property("InfiniteLife","true"),Property("MaximumParticles",1),
            Property("ParticleSimulationSpace",local ? "LOCAL_SPACE_POSITION_ROTATION" : "WORLD_SPACE"),
            new XElement("property",new XAttribute("name","EmissionRateCurve"),Curve("x",.1)),
            new XElement("property",new XAttribute("name","ParticleLifeTime"),Curve("x",life)),
            new XElement("property",new XAttribute("name","ParticleScale"),Curve("xyz",1)),
            new XElement("property",new XAttribute("name","EmitterOrientation"),Curve("xyz",0)),
            new XElement("property",new XAttribute("name","ParticleSpinDeceleration"),damping),Property("ParticleSpinDirectionZ","CCW"));
        foreach (var name in new[] { "ParticleOrientationMin","ParticleOrientationMax" })
            emitter.Add(new XElement("property",new XAttribute("name",name),new XAttribute("x",0),new XAttribute("y",0),new XAttribute("z",17)));
        foreach (var name in new[] { "ParticleSpin_Min","ParticleSpin_Max" })
            emitter.Add(new XElement("property",new XAttribute("name",name),new XAttribute("x",0),new XAttribute("y",0),new XAttribute("z",200)));
        var quality=new XElement("quality",new XAttribute("value","0"),
            new XElement("modifier",new XAttribute("fragment","vs_translation"),Property("restriction",3)),
            new XElement("modifier",new XAttribute("fragment","ps_texture"),
                new XElement("texture_property",new XAttribute("name","diffuse"),new XAttribute("path","vfx/test.dds"))));
        return Encoding.UTF8.GetBytes(new XElement("vfx",new XAttribute("revision",2),new XElement("emitters",emitter),
            new XElement("behaviour_chains",new XElement("behaviour_chain",new XAttribute("id","diagnostic-native-dynamic"),quality))).ToString(SaveOptions.DisableFormatting));
    }
}
