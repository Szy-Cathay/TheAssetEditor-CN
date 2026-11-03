#nullable enable
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxFungiMeshMaterialPixelTests
{
    private const int Size = 64, Centre = 32 * Size + 32;
    private static readonly Vector4 Background = new Color(3,5,9,59).ToVector4();
    private static readonly Vector3[] Cube = [new(.4f,.1f,.2f),new(.1f,.5f,.2f),new(.2f,.3f,.7f),new(.6f,.2f,.1f),new(.3f,.6f,.4f),new(.7f,.1f,.5f)];
    private static readonly byte[][] Samples =
    [
        [103,157,61,255], [0,0,0,255], [255,255,255,255], [71,112,23,255], [150,0,0,255],
        [153,77,0,191], [173,119,0,255], [0,127,0,127], [0,0,197,255],
    ];

    [SetUp]
    public void RequireExplicitOffscreenRun()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
    }

    [TestCase(.13f,false,false)]
    [TestCase(.81f,true,false)]
    [TestCase(.37f,true,true)]
    public void NativeMaterialCoreAndPbr_MatchIndependentAmbientSunAndDxt5nEquations(float unary, bool tiltedNormal, bool sun)
    {
        WithDevice(device =>
        {
            var samples = Samples.Select(x => x.ToArray()).ToArray();
            if (tiltedNormal) samples[7] = [0,90,0,207];
            var p = Parameters();
            var expected = Reference(p,samples,unary,.73,sun);
            var actual = Render(device,p,samples,unary:unary,sun:sun)[Centre];
            Near(actual,expected);
            var changed = Render(device,p,samples,unary:unary,sun:sun,tint:new(.9f,.01f,.8f))[Centre];
            Near(changed,expected);
            Assert.That(Vector3.Distance(new(actual.X,actual.Y,actual.Z),new(Background.X,Background.Y,Background.Z)), Is.GreaterThan(.01f));
            TestContext.Progress.WriteLine($"DIAG native fungi core + bounded UI sun/ambient: unary={unary}, normal={tiltedNormal}, sun={sun}, actual={actual}, expected={expected}; original faction provider absent: explicit white controls.");
        });
    }

    [Test]
    public void MissingHostLighting_StaysFiniteAndDarkWithoutInventingAnUnlitFallback()
    {
        WithDevice(device=>Near(Render(device,Parameters(),Samples,missingLighting:true)[Centre],new Vector4(0,0,0,1)));
    }

    [TestCase(127,255,false)]
    [TestCase(128,255,true)]
    [TestCase(255,128,false)]
    [TestCase(255,129,true)]
    public void NativeOpaqueClip_UsesBaseHalfAndPackedByteAlphaDitherBoundaries(int baseAlpha, int alphaByte, bool visible)
    {
        WithDevice(device =>
        {
            var samples=Samples.Select(x=>x.ToArray()).ToArray(); samples[0][3]=(byte)baseAlpha;
            samples[1][0]=128;
            var pixels=Render(device,Parameters(),samples,alpha:alphaByte/255f);
            if (visible) Near(pixels[Centre],Reference(Parameters(),samples,.31f,.73,false));
            else Near(pixels[Centre],Background);
        });
    }

    [TestCase(0f)]
    [TestCase(.35f)]
    [TestCase(1f)]
    public void NativeVertexClockAndRedMask_MatchIndependentlyBakedWorldGeometry(float timeSource)
    {
        WithDevice(device =>
        {
            var p=Parameters(); p[7]=p[8]=p[9]=0; p[13]=timeSource;
            p[14]=1; p[20]=0; p[21]=.37f; p[22]=0; p[23]=1.2f; p[24]=1;
            const float age=.17f,global=1.1f,red=166f/255;
            var time=age+(global-age)*timeSource;
            var offset=.37f*MathF.Sin(time)*1.2f*red;
            var referenceParameters=p.ToArray(); referenceParameters[21]=0;
            var actual=Render(device,p,Samples,age:age,global:global,vertexRed:red,smallGeometry:true);
            var baked=Render(device,referenceParameters,Samples,age:age,global:global,vertexRed:red,smallGeometry:true,bakedOffset:offset);
            var still=Render(device,referenceParameters,Samples,age:age,global:global,vertexRed:red,smallGeometry:true);
            Assert.That(actual.Zip(baked).Max(pair=>Vector4.Distance(pair.First,pair.Second)), Is.LessThanOrEqualTo(.00075f),
                "The reference moves original input vertices by native XYZ broadcast; it does not call the production displacement helper.");
            Assert.That(actual.Zip(still).Count(pair=>Vector4.Distance(pair.First,pair.Second)>.001f), Is.GreaterThan(20),
                "Clock/red controls must move coverage, not merely change a pixel colour.");
        });
    }

    [Test]
    public void NativeDisplacementBlueAndNormalX_MatchIndependentlyBakedWorldGeometry()
    {
        WithDevice(device =>
        {
            var p=Parameters();p[7]=p[8]=p[9]=0;p[20]=.8f;p[21]=0;
            var normal=new Vector3(.6f,0,.8f);
            var offset=(197f/255)*normal.X*p[15]*(1-.625f)*p[20];
            var reference=p.ToArray();reference[20]=0;
            var actual=Render(device,p,Samples,smallGeometry:true,modelNormal:normal);
            var baked=Render(device,reference,Samples,smallGeometry:true,bakedOffset:offset,modelNormal:normal);
            var still=Render(device,reference,Samples,smallGeometry:true,modelNormal:normal);
            Assert.That(actual.Zip(baked).Max(pair=>Vector4.Distance(pair.First,pair.Second)),Is.LessThanOrEqualTo(.00075f));
            Assert.That(actual.Zip(still).Count(pair=>Vector4.Distance(pair.First,pair.Second)>.001f),Is.GreaterThan(20),
                "VS slot 0 must sample logical texture 8 blue and original normal X, not a PS base-colour alias.");
        });
    }

    [TestCase(true,0)]
    [TestCase(true,2)]
    [TestCase(false,0)]
    [TestCase(false,2)]
    public void NativeEmissionDirectionAndDistortion_UseRedBlueWithIndependentUvGeometry(bool direction, int channel)
    {
        WithDevice(device =>
        {
            // DIAG: retain scalar inputs; a patterned emission texture makes both UV axes observable.
            const int emissionSize=64;
            var p=Parameters();
            var samples=Samples.Select(sample=>sample.ToArray()).ToArray();
            samples[2]=direction ? [31,223,79,255] : [255,255,255,255];
            samples[3]=direction ? [0,0,0,255] : [51,217,83,255];
            samples[4]=Enumerable.Range(0,emissionSize*emissionSize).SelectMany(index=>new byte[]
                {(byte)(((index%emissionSize/4+index/emissionSize/7)%2==0)?35:215),19,149,255}).ToArray();
            var global=direction ? 4.25f : 0;
            var reference=p.ToArray();reference[0]=reference[3]=0;
            Vector4[] Draw(byte[][] input, float[] parameters, Vector2 offset=default) =>
                Render(device,parameters,input,global:global,emissionSize:emissionSize,varyingUV:true,bakedUVOffset:offset);
            var pixels=Draw(samples,p);
            var offset=NativeEmissionUvOffset(p,samples,global);
            var baked=Draw(samples,reference,offset);
            Assert.That(pixels.Zip(baked).Max(pair=>Vector4.Distance(pair.First,pair.Second)),Is.LessThanOrEqualTo(.00075f),
                "The oracle bakes native RB offsets into input UVs with shader offsets disabled; it does not reuse the production sampler.");
            var wrongGreen=Draw(samples,reference,NativeEmissionUvOffset(p,samples,global,true));
            Assert.That(baked.Zip(wrongGreen).Count(pair=>Vector4.Distance(pair.First,pair.Second)>.001f),Is.GreaterThan(20),
                "An RG interpretation must visibly sample a different part of the emission pattern.");
            var changed=samples.Select(sample=>sample.ToArray()).ToArray();
            changed[direction ? 2 : 3][channel]=channel==0 ? (byte)163 : (byte)151;
            var changedOffset=NativeEmissionUvOffset(p,changed,global);
            Assert.That(channel==0 ? changedOffset.Y : changedOffset.X,Is.EqualTo(channel==0 ? offset.Y : offset.X));
            Assert.That(Math.Abs(channel==0 ? changedOffset.X-offset.X : changedOffset.Y-offset.Y),Is.GreaterThan(.01f));
            var changedPixels=Draw(changed,p);
            var changedBaked=Draw(changed,reference,changedOffset);
            Assert.That(changedPixels.Zip(changedBaked).Max(pair=>Vector4.Distance(pair.First,pair.Second)),Is.LessThanOrEqualTo(.00075f));
            Assert.That(pixels.Zip(changedPixels).Count(pair=>Vector4.Distance(pair.First,pair.Second)>.001f),Is.GreaterThan(20),
                "Changing only R must move horizontal sampling; changing only B must move vertical sampling.");
        });
    }

    private static Vector2 NativeEmissionUvOffset(float[] p,byte[][] samples,double time,bool wrongGreen=false)
    {
        // Official GBUFFER: direction r1.xz/t2.xyzw; distortion r2.xy/t3.xzyw. Both consume RB.
        var vertical=wrongGreen ? 1 : 2;
        return new((float)(samples[3][0]/255d*p[0]+(Math.Pow(samples[2][0]/255d,.45)*2-1)*time*p[3]*.05),
            (float)(samples[3][vertical]/255d*p[0]+(Math.Pow(samples[2][vertical]/255d,.45)*2-1)*time*p[3]*.05));
    }

    // Independent double equations transcribed from native PS reflection/instructions, not product helpers.
    private static Vector4 Reference(float[] p, byte[][] samples, double unary, double global, bool sun)
    {
        double[] S(int i)=>samples[i].Select(b=>b/255d).ToArray();
        var b=S(0);var mask=S(5);var material=S(6);var ns=S(7);
        var position=new Vector3(1f/Size,-1f/Size,0);
        var v=Vector3.Normalize(new Vector3(0,0,10)-position);
        var facing=Math.Pow(1-Math.Abs(v.Z),p[11]);
        var pulse=(Math.Sin((unary+global+.5)*p[1])+.99)*p[2]*.1;
        var glow=Enumerable.Range(0,3).Select(i=>(facing+S(4)[0]*p[4]+pulse)*p[7+i]*mask[3]).ToArray();
        var scale=Math.Max(glow.Max(),1);
        var baseColour=Enumerable.Range(0,3).Select(i=>Math.Clamp(1-(1-b[i])*(1-glow[i]/scale),0,1)).ToArray();
        var luminance=Math.Clamp(glow[0]*.2126+glow[1]*.7152+glow[2]*.0722,0,1);
        var metal=Math.Clamp(material[0]*(1-mask[0]*p[12]*(unary-.5))*(1-luminance),0,1);
        var rough=Math.Clamp(material[1]+(1-material[1])*luminance,0,1);
        var nx=(ns[3]+1d/510)*2-1;var ny=(ns[1]+1d/510)*2-1;
        var n=Vector3.Normalize(new((float)nx,(float)ny,(float)Math.Sqrt(Math.Max(0,1-nx*nx-ny*ny))));
        var ambient=new double[3];var weights=new[]{n.X*n.X,n.Y*n.Y,n.Z*n.Z};
        var faces=new[]{n.X<0?1:0,n.Y<0?3:2,n.Z<0?5:4};
        for(var c=0;c<3;++c)for(var axis=0;axis<3;++axis)
        {
            var cube=Cube[faces[axis]]; ambient[c]+=weights[axis]*new[]{cube.X,cube.Y,cube.Z}[c];
        }
        var f0=baseColour.Select(c=>.04+(c-.04)*metal).ToArray();
        var rgb=Enumerable.Range(0,3).Select(i=>ambient[i]*baseColour[i]*(1-metal)*(1-f0[i])).ToArray();
        if(sun)
        {
            var l=Vector3.Backward; var nl=Vector3.Dot(n,l);var nv=Vector3.Dot(n,v);
            var sigma=1.1284-1.125249*(1-rough);var radius=Math.PI/360;
            var angle=Acos(Vector3.Dot(l,Vector3.Reflect(-v,n)));
            var pop=Math.Abs(Cdf((angle+radius)/sigma/Math.Sqrt(2))-Cdf((angle-radius)/sigma/Math.Sqrt(2)));
            var all=Cdf(Math.PI/2/sigma/Math.Sqrt(2))-Cdf(-Math.PI/2/sigma/Math.Sqrt(2));
            var facets=Math.Pow(Math.Clamp(pop/all,0,1),2)*Math.PI/4;
            var k=rough*rough*.5;var nls=Math.Clamp(nl,0,1);var nvs=Math.Clamp(nv,0,1);
            var visibility=Math.Clamp(.25/((nls*(1-k)+k)*(nvs*(1-k)+k)),0,1)*(1+(nls-1)*Math.Sin(rough*Math.PI/2));
            var half=Vector3.Normalize(v+l);var vh=Vector3.Dot(v,half);
            var a=Acos(nl);var z=Acos(nv);var square=sigma*sigma;
            var oren=1-square/(2*(square+.33))+.45*square/(square+.09)*Math.Max(Math.Cos(a-z),0)*Math.Sin(Math.Max(a,z))*Math.Tan(Math.Min(a,z));
            var sunColour=new[]{2d,1.5,1d};
            for(var i=0;i<3;++i)
                rgb[i]+=sunColour[i]*(Fresnel(f0[i],vh)*facets*visibility+baseColour[i]*(1-metal)*(1-Fresnel(f0[i],nls))*oren*Math.Max(nl,0)*.00007615242180438042);
        }
        return new((float)rgb[0],(float)rgb[1],(float)rgb[2],1);
    }

    private static double Acos(double x)
    {
        x=Math.Clamp(x,-1,1);var a=Math.Abs(x);var value=Math.Sqrt(1-a)*(((-.018729*a+.074261)*a-.212114)*a+1.570729);
        return x<0?Math.PI-value:value;
    }
    private static double Cdf(double x)
    {
        var a=Math.Abs(x);var polynomial=1+.278393*a+.230389*a*a+.000972*a*a*a+.078108*a*a*a*a;
        return .5*(1+Math.Sign(x)*(1-1/Math.Pow(polynomial,4)));
    }
    private static double Fresnel(double f0,double cosine)
    {
        var c=Math.Clamp(cosine,0,1);return f0+(1-f0)*Math.Pow(2,(-5.55473*c-6.98316)*c);
    }

    private static float[] Parameters()
    {
        var p=new float[80];VfxFungiMeshMaterialTests.Expected.CopyTo(p,0);
        p[2]=.7f;p[4]=.2f;p[12]=.8f;p[20]=p[21]=0;return p;
    }

    private static Vector4[] Render(GraphicsDevice device,float[] p,byte[][] samples,float unary=.31f,float alpha=1,
        bool sun=false,Vector3? tint=null,float age=.17f,float global=.73f,float vertexRed=.65f,bool smallGeometry=false,float bakedOffset=0,Vector3? modelNormal=null,bool missingLighting=false,
        int emissionSize=1,bool varyingUV=false,Vector2 bakedUVOffset=default)
    {
        var d3d=(SharpDX.Direct3D11.Device)device.Handle;
        using var preview=EffekseerNative.AecnVfxCreate(d3d.NativePointer,d3d.ImmediateContext.NativePointer);
        for(var index=0;index<samples.Length;++index)
        {
            var textureSize=index==4 ? emissionSize : 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,samples[index],samples[index].Length,textureSize,textureSize,0));
        }
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,Enumerable.Range(0,9).ToArray(),9,p,80));
        var size=smallGeometry ? .5f : 1f;
        var vertices=new[]{new Vector3(-size,-size,0),new(size,-size,0),new(-size,size,0),new(size,size,0)}
            .Select(position=>new EffekseerVertex{Position=position+new Vector3(bakedOffset),Normal=modelNormal??Vector3.Backward,
                Tangent=Vector3.Right,Binormal=Vector3.Up,UV=(varyingUV ? new Vector2(position.X*.5f+.5f,position.Y*.5f+.5f) : new(.375f,.625f))+bakedUVOffset,
                Colour=new(vertexRed,.2f,.7f,.43f)}).ToArray();
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview,vertices,4,[0,1,2,2,1,3],6));
        var layer=EffekseerLayer.Create();layer.Kind=1;layer.ModelIndex=0;layer.MeshMaterialIndex=0;layer.TextureIndex=0;layer.OpaqueModel=1;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
        var lighting=new EffekseerLighting{PositiveX=new(Cube[0],0),NegativeX=new(Cube[1],0),PositiveY=new(Cube[2],0),NegativeY=new(Cube[3],0),
            PositiveZ=new(Cube[4],0),NegativeZ=new(Cube[5],0),SunDirection=new(0,0,-1,1f/20000),SunColour=sun?new(2,1.5f,1,0):Vector4.Zero};
        if(missingLighting)lighting=default;
        var particle=new EffekseerParticle{Transform=Matrix.Identity,Colour=new(tint??new Vector3(.01f,.9f,.1f),alpha),UV=new(0,0,1,1),
            ParticleSize=1,Time=age,MaterialTime=global,NormalRotation=new(unary,0,0,0)};
        using var target=new RenderTarget2D(device,Size,Size,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
        device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,new Color(Background),1,0);
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,Values(Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up)),
            Values(Matrix.CreateOrthographic(2,2,.1f,100)),[particle],1,in lighting));
        device.SetRenderTarget(null);var result=new Vector4[Size*Size];target.GetData(result);
        Assert.That(result.All(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&float.IsFinite(v.W)),Is.True);
        return result;
    }
    private static float[] Values(Matrix m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static void Near(Vector4 actual,Vector4 expected)=>Assert.That(Vector4.Distance(actual,expected),Is.LessThanOrEqualTo(.00075f),$"actual={actual}, independent={expected}");
    private static void WithDevice(Action<GraphicsDevice> action){var graphics=new GraphicsDeviceServiceMock();try{action(graphics.GraphicsDevice);}finally{graphics.Release();}}
}
