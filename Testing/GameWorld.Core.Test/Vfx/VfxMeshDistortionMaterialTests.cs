#nullable enable
using System.Reflection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxMeshDistortionMaterialTests
{
    private const int Width = 64;
    private static readonly Vector2 Uv = new(.125f,.5f);

    [TestCase(false)]
    [TestCase(true)]
    public void RealPartRouting_UsesOnlyTheDistortionPassAndKeepsTheOrdinaryPart(bool first)
    {
        var conversion = TotalWarVfxConverter.Convert(new(VfxModelVariantTests.Source(1), "DIAG/mesh-distortion-parts.xml", VfxModelVariantTests.Resolve));
        conversion.MeshMaterials.Add(new([0,0], VfxNativeMeshMaterialTests.Fixtures.Single(f => f.Mode == 5).Parameters));
        var emitter = conversion.Emitters.Single(); var ordinary = emitter.Render; var distortion = ordinary; distortion.MeshMaterialIndex = 0;
        emitter.Render = first ? distortion : ordinary; emitter.AdditionalModelParts = [first ? ordinary : distortion];
        using var preview = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_conversion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(preview, conversion);
        var built = preview.BuildRenderLayers([null]);
        Assert.Multiple(() =>
        {
            Assert.That(built.Layers, Has.Length.EqualTo(2));
            Assert.That(built.Layers.Single(l => l.MeshMaterialIndex == 0).Distortion, Is.EqualTo(1));
            Assert.That(built.Layers.Single(l => l.MeshMaterialIndex < 0).Distortion, Is.Zero);
            Assert.That(built.HalfLayers, Is.Empty); Assert.That(built.FullLayers, Is.Empty);
        });
    }

    [TestCase(.137f, 1.8f, false)]
    [TestCase(.913f, -.6f, false)]
    [TestCase(.137f, 1.8f, true)]
    public void NativeVector_UsesOriginalChannelsTimeSignedAlphaFacingAndDepth(float time, float alpha, bool depth)
    {
        WithDevice(device =>
        {
            var actual = Render(device, time, alpha, .45f, false, depth);
            AssertCentre(actual, Expected(time, alpha, .45f, 1, depth));
        });
    }

    [Test]
    public void ZeroMaterialAlpha_StillContributesItsVectorWhenAnotherLayerProvidesRevealage()
    {
        WithDevice(device =>
        {
            var zero = Render(device,.137f,1.8f,0,false,false);
            var single = Render(device,.137f,1.8f,.45f,false,false);
            var mixed = Render(device,.137f,1.8f,.45f,true,false);
            AssertCentre(zero, Expected(.137f,1.8f,0,1,false));
            AssertCentre(mixed, Expected(.137f,1.8f,.45f,2,false));
            Assert.That(Vector3.Distance(Rgb(single[32*Width+32]),Rgb(mixed[32*Width+32])), Is.GreaterThan(.0001f));
        });
    }

    [Test]
    public void ProductionPreview_ReportsAndClearsTheActualSceneDepthAvailability()
    {
        WithDevice(device =>
        {
            var original=VfxNativeMeshMaterialTests.Fixtures.Single(f=>f.Mode==5);
            using var preview=new EffekseerVfxPreview(device);
            preview.Reload(new(VfxOpaqueModelMaterialTests.Source(Vector3.One,1),"DIAG/original-mesh-distortion-depth.xml",
                path=>path.Replace('\\','/')=="vfx/character.xml.material" ? original.Original
                    : VfxOpaqueModelMaterialTests.Resolve(path,VfxModelVariantPixelTests.Dds(Color.White))));
            Assert.That(preview.EmitterCount,Is.EqualTo(1));preview.Seek(.2f,Matrix.Identity);
            var camera=new Vector3(.5f,.5f,10);
            var parameters=new CommonShaderParameters(Matrix.CreateLookAt(camera,new(.5f,.5f,0),Vector3.Up),
                Matrix.CreateOrthographic(4,4,.1f,50),camera,Vector3.Zero,0,0,0,1,Vector3.One,[]);
            foreach(var depth in new[]{DepthFormat.None,DepthFormat.Depth24,DepthFormat.None})
            {
                using var target=new RenderTarget2D(device,Width,Width,false,SurfaceFormat.Vector4,depth);
                device.SetRenderTarget(target);
                device.Clear(depth==DepthFormat.None ? ClearOptions.Target : ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);
                try{preview.Draw(parameters);}
                finally{device.SetRenderTarget(null);}
                Assert.That(preview.Diagnostics.Any(d=>d.ReasonKey=="Vfx.Preview.SceneDepthUnavailable" && d.Field=="depth_fade"),
                    Is.EqualTo(depth==DepthFormat.None),$"Actual target depth={depth}");
            }
        });
    }

    private static Vector4 Expected(float time, float alpha, float materialAlpha, int vectorCount, bool depth)
    {
        // Independent interpretation of the original 54 instructions: detail.RG, then main.RB.
        var mainU = (Uv.X + (double)time*.07f)*1.3f + 51/255.0;
        var coordinate = (mainU-Math.Floor(mainU))*2-.5;
        var left = (int)Math.Floor(coordinate); var fraction = coordinate-left;
        var first = (left%2+2)%2 == 0 ? 64/255.0 : 192/255.0;
        var second = (left%2+2)%2 == 0 ? 192/255.0 : 64/255.0;
        var red = first+(second-first)*fraction; var blue = 1.003921568627451-red;
        var vector = 0.0;
        var projection = Projection;
        var clip = Vector4.Transform(new Vector4(0,0,-10.05f,1), projection);
        var storedDepth = clip.Z/clip.W;
        var receiverDistance = ((double)projection.M43-storedDepth)/projection.M33;
        // Resolve samples four neighbouring vector texels half a texel before the background.
        foreach (var y in new[] {31,32}) foreach (var x in new[] {31,32})
        {
            var worldX = ((x+.5)/Width*2-1)*2; var worldY = (1-(y+.5)/Width*2)*2;
            var facing = 10/Math.Sqrt(100+worldX*worldX+worldY*worldY);
            var fade = depth ? Math.Clamp(Math.Abs(receiverDistance-10)/.2,0,1) : 1;
            var raw = 2*(red-blue)*.7f*facing*(129/255.0)*(MathF.Floor(alpha*1000)*.001f)*fade*fade;
            // The actual native weighted target stores half floats after each draw.
            var accumulated = (float)(Half)(float)raw;
            if(vectorCount==2) accumulated = (float)(Half)(accumulated+(float)raw);
            vector += accumulated/4.0;
        }
        var transmittance = (float)(Half)(1-materialAlpha);
        var offset = .02*vector*(1-transmittance)*(1-transmittance);
        return new((float)(32/63.0+offset*Width/63.0), (float)(32/63.0+offset*Width/63.0), .2f, 0);
    }

    private static Vector4[] Render(GraphicsDevice device,float time,float alpha,float materialAlpha,bool zeroLayer,bool depth)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,[51,77,151,255],4,1,1,0));
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,[64,117,192,255,192,201,64,255],8,2,1,0));
        // Raw-ABI DIAG includes signed alpha; it does not claim the production source supports negative alpha.
        float[] parameters = [materialAlpha,.2f,.02f,-.03f,1.1f,.75f,.7f,.07f,-.04f,1.3f,.9f,0,0,0,0];
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,[0,1],2,parameters,15));
        if(zeroLayer)
        {
            var zero = (float[])parameters.Clone(); zero[0]=0;
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,[0,1],2,zero,15));
        }
        var vertices = new[] {new Vector3(-.5f,-.5f,0),new(.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,0)}
            .Select(position => new EffekseerVertex {Position=position,Normal=Vector3.Backward,Tangent=Vector3.Right,
                Binormal=Vector3.Up,UV=Uv,Colour=new(1,1,1,129/255f)}).ToArray();
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview,vertices,4,[0,1,2,2,1,3],6));
        var layer=EffekseerLayer.Create();layer.Kind=1;layer.ModelIndex=0;layer.MeshMaterialIndex=0;layer.Distortion=1;
        var second=layer;second.MeshMaterialIndex=1;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,zeroLayer ? [layer,second] : [layer],zeroLayer ? 2 : 1));
        using var target=new RenderTarget2D(device,Width,Width,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
        device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);
        using(var image=new Texture2D(device,Width,Width,false,SurfaceFormat.Vector4))
        using(var batch=new SpriteBatch(device))
        {
            image.SetData(Enumerable.Range(0,Width*Width).Select(i=>new Vector4(i%Width/63f,i/Width/63f,.2f,0)).ToArray());
            batch.Begin(SpriteSortMode.Immediate,BlendState.Opaque,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
            batch.Draw(image,Vector2.Zero,Color.White);batch.End();
        }
        var particle=new EffekseerParticle {Transform=Matrix.CreateScale(4),UV=new(0,0,1,1),Colour=new(1,1,1,alpha),MaterialTime=time};
        var other=particle;other.Layer=1;
        var view=Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up);var projection=Projection;
        using var stream=depth ? new SharpDX.DataStream(Width*Width*sizeof(float),true,true) : null;
        if(stream!=null)
        {
            var clip=Vector4.Transform(new Vector4(0,0,-10.05f,1),projection);
            stream.WriteRange(Enumerable.Repeat(clip.Z/clip.W,Width*Width).ToArray());stream.Position=0;
        }
        using var texture=stream==null ? null : new SharpDX.Direct3D11.Texture2D(direct,new SharpDX.Direct3D11.Texture2DDescription
        {Width=Width,Height=Width,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R32_Float,SampleDescription=new(1,0),
            Usage=SharpDX.Direct3D11.ResourceUsage.Immutable,BindFlags=SharpDX.Direct3D11.BindFlags.ShaderResource},
            new SharpDX.DataRectangle(stream.DataPointer,Width*sizeof(float)));
        using var srv=texture==null ? null : new SharpDX.Direct3D11.ShaderResourceView(direct,texture);
        var lighting=default(EffekseerLighting);
        try{EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,Values(view),Values(projection),zeroLayer ? [particle,other] : [particle],zeroLayer ? 2 : 1,in lighting,srv?.NativePointer??0));}
        finally{device.SetRenderTarget(null);}
        var result=new Vector4[Width*Width];target.GetData(result);return result;
    }

    private static Matrix Projection=>Matrix.CreateOrthographic(4,4,.1f,50);
    private static Vector3 Rgb(Vector4 value)=>new(value.X,value.Y,value.Z);
    private static void AssertCentre(Vector4[] actual,Vector4 expected)=>Assert.That(Vector3.Distance(Rgb(actual[32*Width+32]),Rgb(expected)),
        Is.LessThan(.00015f),$"actual={actual[32*Width+32]}, expected={expected}");
    private static float[] Values(Matrix m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static void WithDevice(Action<GraphicsDevice> action)
    {
        if(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST")!="1")Assert.Ignore("Native GPU is opt-in.");
        var graphics=new GraphicsDeviceServiceMock();try{action(graphics.GraphicsDevice);}finally{graphics.Release();}
    }
}
