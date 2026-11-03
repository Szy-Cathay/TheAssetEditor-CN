#nullable enable
using System.Globalization;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxModelConstantForceTests
{
    public static IEnumerable<TestCaseData> CombinedForceCases()
    {
        foreach(var space in new[]{"WORLD_SPACE","LOCAL_SPACE_POSITION_ONLY"})foreach(var speed in new[]{.5f,1f,2f})
            foreach(var onlyB in new[]{false,true})yield return new(space,speed,onlyB);
    }

    [TestCaseSource(nameof(CombinedForceCases))]
    public void RigidForces_AddNativeAbcTablesBeforeScalingAndUseOneFixedUnary(string space,float speed,bool onlyB)
    {
        var source=CombinedForceSource(space,onlyB);var conversion=ForceEventConversion(source,speed,visibleChild:true);
        var definition=conversion.Emitters.Single(e=>!e.IsEventTemplate);
        Assert.That(definition.ModelForceCurve,Is.Not.Null);Assert.That(definition.ModelConstantForce,Is.False);
        Assert.That(conversion.Diagnostics.Any(d=>d.Layer==definition.Name && (d.Field is "ForceA" or "ForceB" or "ForceC")
            && d.ReasonKey=="Vfx.Preview.ForceUnsupported"),Is.False);
        var root=Root(.8f);var simulation=new TotalWarVfxSimulation(conversion);simulation.Seek(0,root);
        var particle=Particles(simulation).Single(p=>!Definition(p).IsEventTemplate);var birth=Snapshot(particle);
        var life=(float)Field(particle,"Life");var childLayer=conversion.Emitters.IndexOf(conversion.Emitters.Single(e=>e.IsEventTemplate));
        foreach(var time in new[]{.2f,.4f})
        {
            simulation.Seek(time,root);var ticks=(int)Math.Round(((double)Field(simulation,"_simulatedTime")-birth.Birth)/Step);
            var expected=CombinedForceOracle(birth,life,ticks,onlyB);var actual=Snapshot(particle);
            Near(actual.WorldPosition,expected.Position);Near(actual.WorldVelocity,expected.Velocity);
            Near(simulation.Frame(Camera()).Single(p=>p.Layer==childLayer).Transform.Translation,expected.Position);
            Assert.That(simulation.ForceBasisUnsupported,Is.False);
        }
        var final=Packet(simulation);simulation.Seek(.1f,root);simulation.Seek(.4f,root);Assert.That(Packet(simulation),Is.EqualTo(final));
        Assert.That(VfxDocument.Read(source).Write(),Is.EqualTo(source));
    }

    [Test]
    public void ThreeForceTables_PreserveFloatAdditionOrderBeforeTheUnitConversion()
    {
        var a=VfxParticleEventMotionTests.VectorCurve(new(167772.16f));
        var b=VfxParticleEventMotionTests.VectorCurve(new(-167772.15f));
        var c=VfxParticleEventMotionTests.VectorCurve(new(.0001f));
        var actual=VfxModelNativeForce.Create(a,b,c).Sample(.371,.63f);
        var sum=167772.16f;sum+=-167772.15f;sum+=.0001f;
        Assert.That(actual,Is.EqualTo(new Vector3(sum*.01f)));
    }

    [TestCase("WORLD_SPACE",false)]
    [TestCase("WORLD_SPACE",true)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY",false)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY",true)]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void CombinedRigidForceGpu_MatchesTheIndependentNativeBodyPlacement(string space,bool onlyB)
    {
        if(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST")!="1")Assert.Ignore("Native offscreen rendering is opt-in.");
        const float speed=1.4f,time=.4f;
        var source=CombinedForceSource(space,onlyB);var conversion=ForceEventConversion(source,speed);var root=Root(.8f);
        Assert.That(conversion.Emitters.Single().ModelForceCurve,Is.Not.Null);
        var simulation=new TotalWarVfxSimulation(conversion);simulation.Seek(0,root);var particle=Particles(simulation).Single();var birth=Snapshot(particle);
        var expected=CombinedForceOracle(birth,(float)Field(particle,"Life"),48,onlyB).Position;
        var control=VfxDocument.Read(source);var emitter=control.Root.Descendants("emitter").Single();
        foreach(var name in new[]{"ForceA","ForceB","ForceC"})VfxParticleEventMotionTests.Set(emitter,name,VfxParticleEventMotionTests.VectorCurve(Vector3.Zero));
        foreach(var name in new[]{"ParticleInitialSpeed","ParticleDeceleration"})VfxParticleEventMotionTests.Set(emitter,name,VfxLibraryVisibilityTests.Constant("x",0));
        var staticRoot=root;staticRoot.Translation+=expected-birth.WorldPosition;
        var targetPoint=expected+Vector3.TransformNormal(new(.3f,.3f,0),root);var eye=targetPoint+new Vector3(0,0,8);
        var camera=Camera() with {View=Matrix.CreateLookAt(eye,targetPoint,Vector3.Up),Projection=Matrix.CreateOrthographic(3,3,.1f,100),CameraPosition=eye};
        var texture=VfxModelVariantPixelTests.Dds(new Color(70,180,230,155));var empty=VfxDocument.CreateComposition().Write();
        var graphics=new GraphicsDeviceServiceMock();
        try
        {
            var device=graphics.GraphicsDevice;
            Vector4[] Render(byte[] bytes,Matrix attachment)
            {
                using var preview=new EffekseerVfxPreview(device);
                preview.Reload(new(Wrap(bytes,speed,"leaf"),"DIAG/rigid-combined-force-pixels.xml",path=>path.Replace('/','\\') switch
                {
                    @"vfx\leaf.xml"=>bytes,@"vfx\model-force-event-child.xml"=>empty,
                    _=>path.EndsWith(".dds",StringComparison.OrdinalIgnoreCase)?texture:VfxModelVariantTests.Resolve(path),
                }));
                Assert.That(preview.EmitterCount,Is.EqualTo(1));preview.Seek(time,attachment);
                using var target=new RenderTarget2D(device,128,128,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
                device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);preview.Draw(camera);device.SetRenderTarget(null);
                var pixels=new Vector4[128*128];target.GetData(pixels);return pixels;
            }
            var actual=Render(source,root);var reference=Render(control.Write(),staticRoot);
            Assert.That(reference.Count(p=>p.X+p.Y+p.Z>.05f),Is.GreaterThan(100));
            Assert.That(actual.Zip(reference).Max(p=>Vector4.Distance(p.First,p.Second)),Is.LessThanOrEqualTo(.00001f));
        }
        finally{graphics.Release();}
    }

    private static byte[] CombinedForceSource(string space,bool onlyB)
    {
        var document=VfxDocument.Read(RigidCurveSource(space,true,true));var emitter=document.Root.Descendants("emitter").Single();
        for(var force=0;force<3;force++)
        {
            var channels=new List<string>();
            foreach(var axis in new[]{0,1,2})foreach(var upper in new[]{false,true})
            {
                var begin=CombinedForceEndpoint(force,axis,upper,false,onlyB);var end=CombinedForceEndpoint(force,axis,upper,true,onlyB);
                channels.Add($"{"xyz"[axis]}_{(upper?"max":"min")},0,0;0,{begin.ToString("R",CultureInfo.InvariantCulture)},linear,linear,-.05,0,.05,0,1;1,{end.ToString("R",CultureInfo.InvariantCulture)},linear,linear,-.05,0,.05,0,1");
            }
            VfxParticleEventMotionTests.Set(emitter,"Force"+(char)('A'+force),string.Join('|',channels)+":x,1;0,1|y,1;2,3|z,1;4,5");
        }
        return document.Write();
    }
    private static float CombinedForceEndpoint(int force,int axis,bool upper,bool end,bool onlyB)
    {
        if(onlyB && force!=1)return 0;
        if(force==0)return RigidCurveEndpoint(axis,upper,end);
        var sign=axis==1?-1:1;
        return sign*((force==1?120:35)+(axis+1)*(upper?42:17)+(end?force*137:0));
    }
    private static (Vector3 Position,Vector3 Velocity) CombinedForceOracle(Sample birth,float life,int ticks,bool onlyB)
    {
        var position=birth.WorldPosition;var velocity=birth.WorldVelocity/birth.Speed;var dt=(float)Step*birth.Speed;
        for(var tick=1;tick<=ticks;tick++)
        {
            var phase=Math.Clamp((float)(tick*Step)*birth.Speed/(life*birth.Speed+.000001),0,1);var coordinate=(float)phase*127;
            var lower=(int)coordinate;var upperIndex=Math.Min(lower+1,127);var fraction=coordinate-lower;
            float Axis(int axis)
            {
                float Bound(bool upper,int index)
                {
                    float Source(int force)=>CombinedForceEndpoint(force,axis,upper,false,onlyB)
                        +(CombinedForceEndpoint(force,axis,upper,true,onlyB)-CombinedForceEndpoint(force,axis,upper,false,onlyB))*(index*(1f/127));
                    var sum=Source(0);sum+=Source(1);sum+=Source(2);return sum*.01f;
                }
                var minimum=Bound(false,lower)+(Bound(false,upperIndex)-Bound(false,lower))*fraction;
                var maximum=Bound(true,lower)+(Bound(true,upperIndex)-Bound(true,lower))*fraction;
                return minimum+(maximum-minimum)*birth.RandomAlpha;
            }
            var acceleration=new Vector3(Axis(0),Axis(1),Axis(2))*birth.Scale;
            velocity*=MathF.Exp(-2*dt);position+=velocity*dt+acceleration*((dt*.5f)*dt);velocity+=acceleration*dt;
        }
        return(position,velocity*birth.Speed);
    }
}
