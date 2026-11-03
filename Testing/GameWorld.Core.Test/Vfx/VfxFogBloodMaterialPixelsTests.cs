#nullable enable
using System.Security.Cryptography;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public partial class VfxFogBloodMaterialPixelsTests
{
    private const int Width=96,Height=80;
    private static readonly Matrix View=Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up);
    private static readonly Vector3 Ambient=new(.37f,.43f,.51f);
    private static readonly Vector4 Colour=new(.67f,.43f,.81f,.63f);
    private readonly VfxOfficialDistortionPixelTests _official=new();
    private readonly Dictionary<bool,(VfxScrollingMeshMaterial Material,Image[] Images)> _fixtures=[];
    private Image[] _ordinaryImages=[];
    private Vector2 _ordinaryUv;
    private sealed record Image(byte[] Pixels,int Width,int Height,bool Srgb,int Mips);

    [OneTimeSetUp]
    public void RequireCurrentOfficialOriginalsAndRealNativeOffscreenRendering()
    {
        _official.LoadCurrentOfficialResourcesOnly();
        var read=typeof(VfxOfficialDistortionPixelTests).GetMethod("Read",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        byte[] Read(string path)=>(byte[]?)read.Invoke(_official,[path])??throw new FileNotFoundException(path);
        foreach(var blood in new[]{false,true})
        {
            var fixture=VfxFogBloodMaterialTests.Original(blood);var original=Read(fixture.Path);
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)),Is.EqualTo(fixture.Sha256));
            var material=VfxCustomSpriteMaterial.Read(original).Material;
            var images=material.TexturePaths.Select(path=>
            {
                var bytes=Read(path);var image=VfxPreparedResources.DecodeTexture(bytes,VfxTextureUsage.MaterialValue);
                TestContext.Out.WriteLine($"Original {path} SHA256={Convert.ToHexStringLower(SHA256.HashData(bytes))} sRGB={image.Srgb}");
                return new Image(image.Pixels,image.Width,image.Height,image.Srgb,image.MipLevels);
            }).ToArray();
            _fixtures.Add(blood,(material,images));
        }
        var ordinary=VfxNativeMeshMaterialTests.Fixtures.First(f=>f.Mode==0);
        _ordinaryImages=ordinary.Textures.Select(path=>
        {
            var bytes=Read(path);var image=VfxPreparedResources.DecodeTexture(bytes,VfxTextureUsage.MaterialValue);
            TestContext.Out.WriteLine($"Original ordinary-model slot {path} SHA256={Convert.ToHexStringLower(SHA256.HashData(bytes))} sRGB={image.Srgb}");
            return new Image(image.Pixels,image.Width,image.Height,image.Srgb,image.MipLevels);
        }).ToArray();
        _ordinaryUv=VisibleBubbleUv(_ordinaryImages,ordinary.Parameters,.137f);
        TestContext.Out.WriteLine("DIAG constant-UV quads isolate native Fog/Blood material equations over complete original DDS/mips. Original full effects are a separate gate.");
    }

    public static IEnumerable<TestCaseData> Contexts()
    {
        foreach(var blood in new[]{false,true})foreach(var orthographic in new[]{false,true})foreach(var samples in new[]{0,4})
            yield return new(blood,orthographic,samples);
    }

    [TestCaseSource(nameof(Contexts))]
    public void OriginalTextureAndParameterGraph_MatchesTheIndependentSurfaceAndMboitOracle(bool blood,bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];var time=orthographic ? .731f : .137f;var unary=samples==4 ? .375123f : .61725f;
            var uv=VisibleUv(blood,fixture.Material.Parameters,fixture.Images,time,unary);
            TestContext.Out.WriteLine($"Material inputs: blood={blood}, uv={uv}, time={time:R}, unary={unary:R}; texture0={Sample(fixture.Images[0],uv)}, texture1={Sample(fixture.Images[1],uv)}, texture2={Sample(fixture.Images[2],uv)}");
            using var handle=Preview(device,fixture.Material,fixture.Images);
            var particle=Particle(uv,Colour,time,unary);var actual=Render(device,handle,handle,[particle],orthographic,samples);
            using var hardware=new TextureSampler(device,fixture.Images);
            var expected=Surface(blood,fixture.Material.Parameters,fixture.Images,uv,Colour,time,unary,hardware.Sample);
            Assert.That(expected.X+expected.Y+expected.Z,Is.GreaterThan(.002f),"The diagnostic must contain coloured material radiance.");
            AssertCentre(actual,expected);
            Assert.That(actual.Count(p=>p.W>.01f && p.X+p.Y+p.Z>.002f),Is.GreaterThan(100));
        });
    }

    [TestCaseSource(nameof(Contexts))]
    public void SameDepthLayers_AreResolvedTogetherAcrossHandlesAndOrder(bool blood,bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];const float time=.3721f,unary=.474981f;
            var uv=VisibleUv(blood,fixture.Material.Parameters,fixture.Images,time,unary);
            using var first=Preview(device,fixture.Material,fixture.Images);using var second=Preview(device,fixture.Material,fixture.Images);
            var a=Particle(uv,Colour,time,unary);var b=Particle(uv,new(.23f,.79f,.31f,.37f),time,unary);
            var one=Surface(blood,fixture.Material.Parameters,fixture.Images,uv,a.Colour,time,unary);
            var two=Surface(blood,fixture.Material.Parameters,fixture.Images,uv,b.Colour,time,unary);
            var coverage=1-(1-one.W)*(1-two.W);
            var expected=new Vector4((new Vector3(one.X,one.Y,one.Z)+new Vector3(two.X,two.Y,two.Z))*coverage/(one.W+two.W),coverage);
            var together=Render(device,first,second,[a,b],orthographic,samples);
            var split=Render(device,first,second,[a,b],orthographic,samples,true);
            var reversed=Render(device,second,first,[b,a],orthographic,samples,true);
            AssertCentre(together,expected);Assert.That(split,Is.EqualTo(together));Assert.That(reversed,Is.EqualTo(together));
        });
    }

    [TestCase(false,0)]
    [TestCase(false,4)]
    [TestCase(true,0)]
    [TestCase(true,4)]
    public void FogNoise_UsesTheNativeRedChannelWhenGreenIsDifferent(bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[false];const float time=.731f,unary=.61725f;
            var uv=VisibleUv(false,fixture.Material.Parameters,fixture.Images,time,unary);
            var images=(Image[])fixture.Images.Clone();
            // DIAG changes only the noise input; original source/material/DDS fixtures remain untouched.
            images[1]=new([64,230,140,255],1,1,false,1);
            using var handle=Preview(device,fixture.Material,images);
            var actual=Render(device,handle,handle,[Particle(uv,Colour,time,unary)],orthographic,samples);
            using var hardware=new TextureSampler(device,images);
            var expected=Surface(false,fixture.Material.Parameters,images,uv,Colour,time,unary,hardware.Sample);
            AssertCentre(actual,expected);
            Assert.That(actual.Count(pixel=>pixel.W>.01f && pixel.X+pixel.Y+pixel.Z>.002f),Is.GreaterThan(100));
            var wrongGreen=new Vector4(new Vector3(expected.X,expected.Y,expected.Z)*(230f/64),
                Math.Clamp((expected.W-.0015f)*(230f/64)+.0015f,.001f,.999f));
            Assert.That(Vector4.Distance(expected,wrongGreen),Is.GreaterThan(.05f),
                "Distinct R/G diagnostic values must expose the wrong scalar source channel.");
        });
    }

    [TestCaseSource(nameof(Contexts))]
    public void MixedScene_ResolvesMboitBeforeTheOrdinaryNativeColourModel(bool blood,bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];var uv=VisibleUv(blood,fixture.Material.Parameters,fixture.Images,.137f,.61725f);
            using var handle=Preview(device,fixture.Material,fixture.Images,ordinary:true);
            var fog=Particle(uv,Colour,.137f,.61725f);var model=Particle(uv,new(.37f,.61f,.83f,.31f),.137f,.61725f);model.Layer=1;
            model.UV=new(_ordinaryUv,0,0);
            // The original sphere-bubble material fades a normal-facing plane to zero; tilt this diagnostic receiver without changing its parameters.
            model.Transform*=Matrix.CreateRotationY(MathHelper.Pi/6);
            var isolatedFog=Render(device,handle,handle,[fog],orthographic,samples);
            var isolatedModel=Render(device,handle,handle,[model],orthographic,samples);
            var mixed=Render(device,handle,handle,[model,fog],orthographic,samples);
            var modelAlpha=isolatedModel[Height/2*Width+Width/2].W;
            Assert.That(modelAlpha,Is.InRange(.00001f,.9f),"The original model material must have usable transparency.");
            for(var y=Height/2-3;y<=Height/2+3;y++)for(var x=Width/2-3;x<=Width/2+3;x++)
            {
                var at=y*Width+x;var modelOnly=isolatedModel[at];var sourceAlpha=MathF.Sqrt(modelOnly.W);
                // NativeMeshBlendScope uses ONE/INV_SRC_ALPHA and SRC_ALPHA/INV_SRC_ALPHA for alpha.
                var expected=modelOnly+isolatedFog[at]*(1-sourceAlpha);
                Assert.That(Vector4.Distance(mixed[at],expected),Is.LessThan(.002f),$"Mixed native pass order at ({x},{y}): actual={mixed[at]}, expected={expected}.");
            }
        });
    }

    [TestCaseSource(nameof(Contexts))]
    public void OpaqueForeground_IsWrittenBeforeTheCustomMboitDepthTest(bool blood,bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];var uv=VisibleUv(blood,fixture.Material.Parameters,fixture.Images,.137f,.61725f);
            using var handle=Preview(device,fixture.Material,fixture.Images,ordinary:true,depthTest:true);
            var fog=Particle(uv,Colour,.137f,.61725f);var opaque=fog;opaque.Layer=2;opaque.Colour=new(.11f,.17f,.23f,1);opaque.Transform.Translation=new(0,0,4);
            var foreground=Render(device,handle,handle,[opaque],orthographic,samples);
            var combined=Render(device,handle,handle,[fog,opaque],orthographic,samples);
            for(var y=Height/2-3;y<=Height/2+3;y++)for(var x=Width/2-3;x<=Width/2+3;x++)
            {
                var at=y*Width+x;Assert.That(foreground[at].W,Is.EqualTo(1));
                Assert.That(combined[at],Is.EqualTo(foreground[at]),"A later accumulation cannot show through already opaque foreground.");
            }
        });
    }

    [TestCaseSource(nameof(Contexts))]
    public void NativeGlowDistortion_SamplesTheResolvedCustomAndOrdinaryColourScene(bool blood,bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];var uv=VisibleUv(blood,fixture.Material.Parameters,fixture.Images,.137f,.61725f);
            using var handle=Preview(device,fixture.Material,fixture.Images,ordinary:true,glowDistortion:true);
            var fog=Particle(uv,Colour,.137f,.61725f);var model=Particle(uv,new(.37f,.61f,.83f,.31f),.137f,.61725f);model.Layer=1;
            model.UV=new(_ordinaryUv,0,0);model.Transform*=Matrix.CreateRotationY(MathHelper.Pi/6);
            var distortion=fog;distortion.Layer=3;distortion.Colour=Vector4.One;distortion.Transform=Matrix.CreateScale(12,10,1);
            var baseline=Render(device,handle,handle,[model,fog],orthographic,samples);
            var actual=Render(device,handle,handle,[model,distortion,fog],orthographic,samples);
            // The shipped Glow PS pans detail first, then offsets noise UV by detail.rb*.5 and emits (noise.r,noise.r,0,1).
            var detail=Sample(fixture.Images[2],uv*.35f+new Vector2(MathF.Sin(.137f*.03f),MathF.Sin((.137f+1)*.03f)));
            var displaced=uv+new Vector2(detail.X,detail.Z)*.5f;
            var offset=(float)(Half)Sample(fixture.Images[1],displaced).X*.02f;var changed=0;
            Vector4 Background(float x,float y)
            {
                var left=(int)Math.Floor(x);var top=(int)Math.Floor(y);
                var fx=(float)Math.Round((x-left)*256)/256;var fy=(float)Math.Round((y-top)*256)/256;
                Vector4 Pixel(int a,int b)=>baseline[Math.Clamp(b,0,Height-1)*Width+Math.Clamp(a,0,Width-1)];
                return Vector4.Lerp(Vector4.Lerp(Pixel(left,top),Pixel(left+1,top),fx),Vector4.Lerp(Pixel(left,top+1),Pixel(left+1,top+1),fx),fy);
            }
            for(var y=Height/4;y<Height*3/4;y++)for(var x=Width/4;x<Width*3/4;x++)
            {
                var at=y*Width+x;var sample=Background(x+offset*Width,y+offset*Height);
                var expected=new Vector4(sample.X,sample.Y,sample.Z,baseline[at].W);
                Assert.That(Vector4.Distance(actual[at],expected),Is.LessThan(.002f),$"After-colour native distortion at ({x},{y}): actual={actual[at]}, expected={expected}.");
                if(Vector3.Distance(new(expected.X,expected.Y,expected.Z),new(baseline[at].X,baseline[at].Y,baseline[at].Z))>.003f)changed++;
            }
            Assert.That(changed,Is.GreaterThan(16),"The after-pass control must refract a real completed colour edge.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingLighting_DoesNotSubstituteAnUnlitMaterial(bool blood)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];var uv=VisibleUv(blood,fixture.Material.Parameters,fixture.Images,.137f,.61725f);
            using var handle=Preview(device,fixture.Material,fixture.Images);
            var pixels=Render(device,handle,handle,[Particle(uv,Colour,.137f,.61725f)],false,0,lighting:false);
            var center=pixels[Height/2*Width+Width/2];
            Assert.That(center.W,Is.GreaterThan(.01f));Assert.That(new Vector3(center.X,center.Y,center.Z),Is.EqualTo(Vector3.Zero));
        });
    }

    [TestCase(false,0)]
    [TestCase(false,4)]
    [TestCase(true,0)]
    [TestCase(true,4)]
    public void BloodSunlight_UsesTheNativeFiniteSolarDiscAndMaterialResponse(bool orthographic,int samples)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[true];var uv=VisibleUv(true,fixture.Material.Parameters,fixture.Images,.137f,.61725f);
            TestContext.Out.WriteLine($"BloodSun inputs: uv={uv}, diffuse={Sample(fixture.Images[0],uv)}, material={Sample(fixture.Images[1],uv)}, normal={Sample(fixture.Images[2],uv)}");
            using var handle=Preview(device,fixture.Material,fixture.Images);
            var particle=Particle(uv,Colour,.137f,.61725f);
            const float sunScale=1000;
            var actual=Render(device,handle,handle,[particle],orthographic,samples,sunScale:sunScale);
            var baseline=Surface(true,fixture.Material.Parameters,fixture.Images,uv,Colour,.137f,.61725f);
            var solarContributions=0;
            for(var y=Height/2-3;y<=Height/2+3;y++)for(var x=Width/2-3;x<=Width/2+3;x++)
            {
                var expected=BloodSunReference(fixture.Images,uv,Colour,x,y,orthographic,sunScale);
                Assert.That(Vector4.Distance(actual[y*Width+x],expected),Is.LessThan(.002f),$"Independent finite-sun integral at ({x},{y}): actual={actual[y*Width+x]}, expected={expected}.");
                if(Vector3.Distance(new(expected.X,expected.Y,expected.Z),new(baseline.X,baseline.Y,baseline.Z))>.001f)solarContributions++;
            }
            Assert.That(solarContributions,Is.GreaterThan(24),"A zero-sun control cannot verify the native direct-light arithmetic.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RawNativeParameters_RejectTheSingularSourceDomain(bool blood)
    {
        WithDevice(device=>
        {
            var fixture=_fixtures[blood];using var handle=Preview(device,fixture.Material,fixture.Images);
            var invalid=(float[])fixture.Material.Parameters.Clone();
            if(blood)invalid[0]=0;else invalid[18]=1;
            Assert.That(EffekseerNative.AecnVfxMeshMaterial(handle,[0,1,2],3,invalid,invalid.Length),Is.Zero);
        });
    }

    private static EffekseerParticle Particle(Vector2 uv,Vector4 colour,float time,float unary)=>new()
    {
        Transform=Matrix.CreateScale(4,3,1),Colour=colour,UV=new(uv,0,0),NormalRotation=new(unary,0,0,1),
        MaterialTime=time,ParticleSize=4,SoftParticleScale=3,
    };
    private EffekseerHandle Preview(GraphicsDevice device,VfxScrollingMeshMaterial material,Image[] images,bool ordinary=false,bool depthTest=false,bool glowDistortion=false)
    {
        var direct=(SharpDX.Direct3D11.Device)device.Handle;
        var handle=EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
        try
        {
            foreach(var image in images)EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle,image.Pixels,image.Pixels.Length,image.Width,image.Height,image.Srgb?1:0,image.Mips));
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle,[0,1,2],3,material.Parameters,material.Parameters.Length));
            var layer=EffekseerLayer.Create();layer.MeshMaterialIndex=0;layer.TextureIndex=0;layer.Kind=0;layer.ZTest=depthTest?1:0;
            var layers=new List<EffekseerLayer>{layer};
            if(ordinary)
            {
                var nativeModel=VfxNativeMeshMaterialTests.Fixtures.First(f=>f.Mode==0);
                foreach(var image in _ordinaryImages)
                    EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle,image.Pixels,image.Pixels.Length,image.Width,image.Height,image.Srgb?1:0,image.Mips));
                EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle,Enumerable.Range(images.Length,_ordinaryImages.Length).ToArray(),_ordinaryImages.Length,nativeModel.Parameters,nativeModel.Parameters.Length));
                EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle,[],0,new float[4],4));
                var vertices=new[]{new Vector3(-.5f,-.5f,0),new(.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,0)}.Select(p=>new EffekseerVertex
                {Position=p,Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(.375f,.625f),Colour=Vector4.One}).ToArray();
                EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle,vertices,4,[0,1,2,2,1,3],6));
                var model=EffekseerLayer.Create();model.Kind=1;model.MeshMaterialIndex=1;model.ModelIndex=0;model.TextureIndex=0;model.ZTest=0;layers.Add(model);
                var opaque=model;opaque.MeshMaterialIndex=2;opaque.TextureIndex=-1;opaque.OpaqueModel=1;opaque.ZTest=1;layers.Add(opaque);
                if(glowDistortion)
                {
                    EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle,[0,1,2],3,new float[16],16));
                    var distortion=model;distortion.MeshMaterialIndex=3;distortion.Distortion=1;layers.Add(distortion);
                }
            }
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle,layers.ToArray(),layers.Count));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigureFull(handle,[new(){LayerIndex=0,LegacyCoverageIndex=-1,LegacyEmissionIndex=-1,
                Premultiplied=0,CoveredColourMultiplier=1,EmissionMultiplier=0}],1,EffekseerHalfLayer.Bytes));
            return handle;
        }
        catch{handle.Dispose();throw;}
    }
    private static Vector4[] Render(GraphicsDevice device,EffekseerHandle first,EffekseerHandle second,EffekseerParticle[] particles,
        bool orthographic,int samples,bool split=false,bool lighting=true,float sunScale=0)
    {
        using var target=new RenderTarget2D(device,Width,Height,false,SurfaceFormat.HalfVector4,DepthFormat.Depth24,samples,RenderTargetUsage.PreserveContents);
        Assert.That(target.MultiSampleCount,Is.EqualTo(samples));device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Transparent,1,0);
        var ambient=new Vector4(Ambient,0);
        var light=lighting?new EffekseerLighting{PositiveX=ambient,NegativeX=ambient,PositiveY=ambient,NegativeY=ambient,PositiveZ=ambient,NegativeZ=ambient,
            SunDirection=new(Vector3.Normalize(sunScale>0?new(-.2f,-.1f,-1):new(.31f,-.74f,.59f)),sunScale/20000),
            SunColour=new(1,.9f,.7f,0),Parameters=new(1,1,1,0)}:default;
        var projection=orthographic?Matrix.CreateOrthographic(8,8f*Height/Width,.1f,50)
            :Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60),Width/(float)Height,.1f,50);
        EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(first,in light));
        try
        {
            if(split)
            {
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(),first,Values(View),Values(projection),[particles[0]],1,in light));
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(),second,Values(View),Values(projection),[particles[1]],1,in light));
            }
            else EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(),first,Values(View),Values(projection),particles,particles.Length,in light));
        }
        finally{EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(first));}
        TestContext.Out.WriteLine($"Native MBOIT draw completed: projection={(orthographic?"orthographic":"perspective")}, samples={samples}, split={split}, drawCalls={EffekseerNative.AecnVfxDrawCalls(first)}");
        device.SetRenderTarget(null);var pixels=new HalfVector4[Width*Height];target.GetData(pixels);return pixels.Select(p=>p.ToVector4()).ToArray();
    }

    private static Vector2 VisibleUv(bool blood,float[] p,Image[] images,float time,float unary)
    {
        var best=Vector2.Zero;var maximum=0f;
        for(var y=0;y<48;y++)for(var x=0;x<48;x++)
        {
            var uv=new Vector2((x+.5f)/48,(y+.5f)/48);var sample=Surface(blood,p,images,uv,Colour,time,unary);
            if(sample.X+sample.Y+sample.Z<=maximum)continue;
            maximum=sample.X+sample.Y+sample.Z;best=uv;
        }
        Assert.That(maximum,Is.GreaterThan(.002f));return best;
    }
    private static Vector2 VisibleBubbleUv(Image[] images,float[] p,float time)
    {
        var best=Vector2.Zero;var maximum=0f;
        float Dot(Vector4 value,int offset)=>value.X*p[offset]+value.Y*p[offset+1]+value.Z*p[offset+2]+value.W*p[offset+3];
        for(var y=0;y<48;y++)for(var x=0;x<48;x++)
        {
            var uv=new Vector2((x+.5f)/48,(y+.5f)/48);
            var a=Sample(images[0],(uv+time*new Vector2(p[27],p[28]))*new Vector2(p[31],p[32]));
            var b=Sample(images[1],(uv+time*new Vector2(p[29],p[30]))*new Vector2(p[33],p[34]));
            var global=Sample(images[3],(uv+time*new Vector2(p[14],p[15]))*new Vector2(p[16],p[17]));
            // Only locate a nonempty authored mask. Geometry, original material parameters and particle alpha stay fixed.
            var mask=Dot(a,0)*p[25]*Dot(b,4)*p[26]*global.X*p[35];
            if(mask<=maximum)continue;
            maximum=mask;best=uv;
        }
        Assert.That(maximum,Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Ordinary original bubble mask control UV={best}, intrinsic mask={maximum}");
        return best;
    }
    private static Vector4 Surface(bool blood,float[] p,Image[] images,Vector2 uv,Vector4 vertex,float time,float unary,Func<Image,Vector2,Vector4>? fetch=null)
    {
        fetch??=Sample;
        if(blood)
        {
            var diffuse=fetch(images[0],uv);var material=fetch(images[1],uv);var baseColour=new Vector3(diffuse.X*vertex.X,diffuse.Y*vertex.Y,diffuse.Z*vertex.Z);
            var f0=Vector3.Lerp(new(.04f),baseColour,material.X);
            var coverage=Math.Clamp(diffuse.W*vertex.W,0,1);
            return new(Ambient*baseColour*(1-material.X)*(Vector3.One-f0)*coverage,Math.Min(coverage+.001f,.999f));
        }
        var shifted=uv+new Vector2(unary);
        var noiseUV=new Vector2(MathF.FusedMultiplyAdd(time,p[9],shifted.X*p[7]),MathF.FusedMultiplyAdd(time,p[10],shifted.Y*p[8]));
        var distortion=fetch(images[0],(shifted+new Vector2(.5f))*new Vector2(p[3],p[4])+time*new Vector2(p[11],p[12]));
        var particle=fetch(images[2],new(MathF.FusedMultiplyAdd(distortion.X,p[1],uv.X),MathF.FusedMultiplyAdd(distortion.X,p[1],uv.Y)));
        var noiseOffset=distortion.Y*p[2];
        var noise=fetch(images[1],new(MathF.FusedMultiplyAdd(noiseUV.X,p[13],noiseOffset),MathF.FusedMultiplyAdd(noiseUV.Y,p[14],noiseOffset))).X;
        noise=MathF.FusedMultiplyAdd((float)Math.Ceiling(p[5]),MathF.FusedMultiplyAdd(-noise,2,1),noise);
        var opacity=particle.W*MathF.FusedMultiplyAdd(p[6],noise-1,1)*vertex.W;
        return new(new Vector3(particle.X*vertex.X,particle.Y*vertex.Y,particle.Z*vertex.Z)*Ambient*opacity,Math.Clamp(opacity+.0015f,.001f,.999f));
    }
    private static Vector4 Sample(Image image,Vector2 uv)
    {
        var x=uv.X*image.Width-.5f;var y=uv.Y*image.Height-.5f;var ix=(int)Math.Floor(x);var iy=(int)Math.Floor(y);
        var fx=(float)Math.Round((x-(float)Math.Floor(x))*256)/256;var fy=(float)Math.Round((y-(float)Math.Floor(y))*256)/256;
        Vector4 Pixel(int a,int b)
        {
            a=((a%image.Width)+image.Width)%image.Width;b=((b%image.Height)+image.Height)%image.Height;var at=(b*image.Width+a)*4;
            float Channel(int c)
            {
                var value=image.Pixels[at+c]/255f;
                return image.Srgb && c<3?value<=.04045f?value/12.92f:MathF.Pow((value+.055f)/1.055f,2.4f):value;
            }
            return new(Channel(0),Channel(1),Channel(2),Channel(3));
        }
        return Vector4.Lerp(Vector4.Lerp(Pixel(ix,iy),Pixel(ix+1,iy),fx),Vector4.Lerp(Pixel(ix,iy+1),Pixel(ix+1,iy+1),fx),fy);
    }
    private static Vector4 BloodSunReference(Image[] images,Vector2 uv,Vector4 vertex,int x,int y,bool orthographic,double sunScale)
    {
        var diffuse=Sample(images[0],uv);var material=Sample(images[1],uv);var normal=Sample(images[2],uv);
        var nx=(normal.W+1.0/510)*2-1;var ny=(normal.Y+1.0/510)*2-1;
        var nz=Math.Sqrt(Math.Max(0,1-nx*nx-ny*ny));
        var worldN=new Vector3(-1f/255,-1f/255,1);var worldT=new Vector3(1,-1f/255,-1f/255);
        // This quad is counterclockwise in render-target coordinates; the native clockwise-front rasterizer reports back-facing.
        var n=Vector3.Normalize(-(float)nx*Vector3.Normalize(worldT)-(float)ny*Vector3.Normalize(Vector3.Cross(worldN,worldT))+(float)nz*Vector3.Normalize(worldN));
        var ndcX=(x+.5)/Width*2-1;var ndcY=1-(y+.5)/Height*2;
        var worldX=orthographic?ndcX*4:ndcX*10*Math.Tan(Math.PI/6)*Width/Height;
        var worldY=orthographic?ndcY*4*Height/Width:ndcY*10*Math.Tan(Math.PI/6);
        var v=Vector3.Normalize(new(-(float)worldX,-(float)worldY,10));
        var sun=Vector3.Normalize(new(-.2f,-.1f,-1));var light=-sun;
        var nl=Math.Clamp(Vector3.Dot(n,light),0,1);var nv=Math.Clamp(Vector3.Dot(-v,n),0,1);
        var rough=Math.Clamp(1-material.Y,0,1);var smooth=1-rough;
        var sigma=1.1284000873565674-1.125248908996582*rough;
        var reflected=Vector3.Reflect(-v,n);var theta=Math.Acos(Math.Clamp(Vector3.Dot(light,reflected),-1,1));
        const double radius=Math.PI/360;
        var integral=(GaussianCdf((theta+radius)/sigma)-GaussianCdf((theta-radius)/sigma))
            /(GaussianCdf(Math.PI/2/sigma)-GaussianCdf(-Math.PI/2/sigma));
        var solar=Math.Pow(Math.Min(Math.Abs(integral),1),2)*Math.PI/4;
        var slope=1-smooth*smooth/2;var intercept=smooth*smooth/2;
        var visibility=Math.Min(1/((nv*slope+intercept)*(nl*slope+intercept))/4,1)
            *(1+Math.Sin(smooth*Math.PI/2)*(nl-1));
        var half=Vector3.Normalize(v-sun);var vh=Math.Clamp(Vector3.Dot(v,half),0,1);
        double Fresnel(double cosine)=>Math.Pow(2,cosine*(-5.55472993850708*cosine-6.983160018920898));
        var thetaL=Math.Acos(Math.Clamp(Vector3.Dot(n,light),-1,1));var thetaV=Math.Acos(Math.Clamp(Vector3.Dot(n,v),-1,1));
        var variance=sigma*sigma;
        var oren=nl*(1-variance/(2*(variance+.33))+.45*variance/(variance+.09)
            *Math.Max(0,Math.Cos(thetaL-thetaV))*Math.Sin(Math.Max(thetaL,thetaV))*Math.Tan(Math.Min(thetaL,thetaV)));
        var baseColour=new Vector3(diffuse.X*vertex.X,diffuse.Y*vertex.Y,diffuse.Z*vertex.Z);
        var f0=Vector3.Lerp(new(.04f),baseColour,material.X);var nonMetal=baseColour*(1-material.X);
        var specular=(f0+(Vector3.One-f0)*(float)Fresnel(vh))*(float)(visibility*solar);
        var diffuseSolar=nonMetal*(Vector3.One-(f0+(Vector3.One-f0)*(float)Fresnel(nl)))*(float)(oren*7.615242066094652e-5);
        var rgb=Ambient*nonMetal*(Vector3.One-f0)+(specular+diffuseSolar)*new Vector3(1,.9f,.7f)*(float)sunScale;
        var coverage=Math.Clamp(diffuse.W*vertex.W,0,1);
        return new(rgb*coverage,Math.Min(coverage+.001f,.999f));
    }
    private static double GaussianCdf(double x)
    {
        // Independent normal CDF approximation; error below 1.5e-7, not the native shader's polynomial.
        var z=Math.Abs(x)/Math.Sqrt(2);var t=1/(1+.3275911*z);
        var erf=1-(((((1.061405429*t-1.453152027)*t)+1.421413741)*t-.284496736)*t+.254829592)*t*Math.Exp(-z*z);
        return .5*(1+(x<0?-erf:erf));
    }
    private static void AssertCentre(Vector4[] pixels,Vector4 expected)
    {
        for(var y=Height/2-4;y<=Height/2+4;y++)for(var x=Width/2-4;x<=Width/2+4;x++)
            Assert.That(Vector4.Distance(pixels[y*Width+x],expected),Is.LessThan(.002f),
                $"Native two-pass MBOIT material oracle at ({x},{y}): actual={pixels[y*Width+x]}, expected={expected}; half-float accumulation and hardware bilinear weights bound the comparison.");
    }
    private static void WithDevice(Action<GraphicsDevice> action)
    {
        var graphics=new GraphicsDeviceServiceMock();try{action(graphics.GraphicsDevice);}finally{graphics.Release();}
    }
    private static float[] Values(Matrix value)=>[value.M11,value.M12,value.M13,value.M14,value.M21,value.M22,value.M23,value.M24,
        value.M31,value.M32,value.M33,value.M34,value.M41,value.M42,value.M43,value.M44];
}
