using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Vertex.Formats;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelVertexColourTests
{
    [TestCase(0)]
    [TestCase(64)]
    [TestCase(128)]
    [TestCase(255)]
    public void StaticMesh_ExposesOriginalUnormColourIncludingAlphaAboveHalf(int alpha)
    {
        var bytes = new byte[32];
        bytes[28] = 17; bytes[29] = 88; bytes[30] = 170; bytes[31] = (byte)alpha;
        var vertex = new StaticVertexCreator().ReadArray(RmvVersionEnum.RMV2_V8, bytes, 0, 32, 1).Single();
        Assert.That(vertex.UnormColour, Is.EqualTo(new Vector4(17, 88, 170, alpha) / 255f));
        Assert.That(bytes[31], Is.EqualTo(alpha));
    }
}

[NonParallelizable]
public class VfxScrollingMeshMaterialTests
{
    [SetUp]
    public void RequireNativeRenderer()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("ASSETEDITOR_VFX_HEADLESS_TEST is not set.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeMaterial_CombinesAllMasksAndVertexAlphaWithPremultipliedSceneCoverage(bool transparentVertex)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            var source = new[] { new byte[]{64,0,0,255}, new byte[]{128,0,0,255}, new byte[]{192,0,0,255},
                new byte[]{77,102,128,128}, new byte[]{255,255,255,255}, new byte[]{204,0,0,255} };
            foreach (var texture in source) EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, texture, 4, 1, 1, 0));
            var parameters = Parameters();
            parameters[15] = 3; parameters[23] = 1; parameters[24] = 2; parameters[25] = 3; parameters[26] = 4;
            parameters[30] = 2; parameters[46] = 2; parameters[47] = 1; parameters[48] = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0,1,2,3,4,5], 6, parameters, parameters.Length));
            var colour = new Vector4(.6f,.8f,1,transparentVertex ? 0 : 128f/255);
            var vertices = Quad(colour);
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, vertices.Length, [0,1,2,2,1,3], 6));
            var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
            var background = new Color(26,51,77).ToVector4();
            var actual = Render(device, preview, new(.5f,1,.25f,.5f), 0, background, 32);
            var mask = transparentVertex ? 0 : (64f/255*2)*(128f/255)*(192f/255)*(204f/255)*MathF.Pow(128f/255,2)*.5f;
            var sourceRgb = mask * (1 + 128f/255 * 2) * new Vector3(77f/255,102f/255,128f/255)
                * new Vector3(.6f,.8f,1) * new Vector3(1,2,3) * 4 * new Vector3(.5f,1,.25f);
            var expected = sourceRgb + new Vector3(background.X,background.Y,background.Z) * (1-mask);
            Assert.That(Vector3.Distance(new(actual.X,actual.Y,actual.Z),expected), Is.LessThan(.0001f),
                "Mask channels, source vertex alpha, independent particle colour, and ONE / INV_SRC_ALPHA coverage must survive the CPU-to-material path.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeMaterial_PansAndRepeatsTheOriginalMaskWithoutClampingOrFreezing()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,[32,0,0,255,224,0,0,255],8,2,1,0));
            for (var i=1;i<6;i++) EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,[255,255,255,255],4,1,1,0));
            var parameters = Parameters(); parameters[33] = .5f;
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,[0,1,2,3,4,5],6,parameters,parameters.Length));
            var vertices = Quad(Vector4.One); EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview,vertices,vertices.Length,[0,1,2,2,1,3],6));
            var layer=EffekseerLayer.Create(); layer.Kind=1; layer.ModelIndex=0; layer.MeshMaterialIndex=0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
            var first=Render(device,preview,Vector4.One,0,Vector4.Zero,16);
            var next=Render(device,preview,Vector4.One,1,Vector4.Zero,16);
            var repeated=Render(device,preview,Vector4.One,2,Vector4.Zero,16);
            Assert.That(Math.Abs(first.X-next.X), Is.GreaterThan(.5f));
            Assert.That(Vector4.Distance(first,repeated), Is.LessThan(.0001f));
        }
        finally { graphics.Release(); }
    }

    [TestCase(SurfaceFormat.Color, 0, false)]
    [TestCase(SurfaceFormat.Color, 4, false)]
    [TestCase(SurfaceFormat.Vector4, 0, false)]
    [TestCase(SurfaceFormat.Color, 0, true)]
    [TestCase(SurfaceFormat.Color, 4, true)]
    public void NativeMaterial_ThinBrightMeshKeepsItsParticleColourInTheOrdinarySceneTarget(SurfaceFormat format, int samples, bool occluded)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device=graphics.GraphicsDevice; var direct=(SharpDX.Direct3D11.Device)device.Handle;
            using var preview=EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
            for(var index=0;index<6;index++) EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,[255,255,255,255],4,1,1,0));
            var parameters=Parameters(); parameters[26]=100; parameters[45]=.01f;
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,[0,1,2,3,4,5],6,parameters,49));
            var vertices=Quad(Vector4.One); EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview,vertices,4,[0,1,2,2,1,3],6));
            var layer=EffekseerLayer.Create(); layer.Kind=1; layer.ModelIndex=0; layer.MeshMaterialIndex=0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
            var background=new Color(26,51,77).ToVector4();
            var actual=Render(device,preview,new(.6f,.4f,.1f,1),0,background,32,format,samples,occluded);
            var expected=occluded ? new Vector3(background.X,background.Y,background.Z) : new Vector3(.6f,.4f,.1f)+new Vector3(background.X,background.Y,background.Z)*.99f;
            Assert.That(Vector3.Distance(new(actual.X,actual.Y,actual.Z),expected),Is.LessThan(.009f),
                "A thin premultiplied mesh must preserve colour before the ordinary scene's final 8-bit conversion.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ScrollingMesh_CullsBackFacesWithoutChangingTheVisibleSideUnderReflectedProjection(bool reversed, bool reflected)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            for (var index = 0; index < 6; index++)
                EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [255, 255, 255, 255], 4, 1, 1, 0));
            var parameters = Parameters();
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0, 1, 2, 3, 4, 5], 6, parameters, parameters.Length));
            var vertices = Quad(Vector4.One);
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, vertices.Length,
                reversed ? [0, 2, 1, 2, 3, 1] : [0, 1, 2, 2, 1, 3], 6));
            var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var pixel = Render(device, preview, Vector4.One, 0, Vector4.Zero, 32, reflected: reflected);
            Assert.That(pixel.X, reversed ? Is.LessThan(.001f) : Is.GreaterThan(.99f),
                "wh_vfx_mesh declares CULL_MODE_CCW; back faces must not contribute another transparent layer.");
        }
        finally { graphics.Release(); }
    }

    private static float[] Parameters()
    {
        var values=new float[49]; values[0]=values[4]=values[8]=1;
        values[15]=values[16]=values[17]=values[21]=values[22]=1;
        values[23]=values[24]=values[25]=values[26]=1;
        values[27]=1; values[29]=values[30]=values[31]=values[32]=1;
        for(var index=39;index<=44;index++) values[index]=1;
        values[45]=values[46]=1;
        return values;
    }

    [Test]
    public void OneShotMaterial_MovesItsMaskByParticlePhaseIndependentlyOfGlobalPanningTime()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            foreach (var texture in new[] { new byte[]{255,0,0,255}, new byte[]{255,0,0,255}, new byte[]{255,0,0,255}, new byte[]{100,50,25,255} })
                EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, texture, 4, 1, 1, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [32,0,0,255,224,0,0,255], 8, 1, 2, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [255,255,255,255], 4, 1, 1, 0));
            var parameters = new float[54];
            foreach (var index in new[]{3,4,5,10,11,12,13,14,15,19,20,21,22,23,29,30,31,32,38,39,40,41,47,48,49,50,51,52}) parameters[index] = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0,1,2,3,4,5], 6, parameters, 54));
            var vertices = Quad(Vector4.One); EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0,1,2,2,1,3], 6));
            var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var first = Render(device, preview, Vector4.One, 0, Vector4.Zero, 32);
            var globalTimeOnly = Render(device, preview, Vector4.One, 5, Vector4.Zero, 32);
            var particlePhase = Render(device, preview, Vector4.One, 0, Vector4.Zero, 32, appearancePhase: .25f);
            Assert.That(Vector4.Distance(first, globalTimeOnly), Is.LessThan(.0001f));
            Assert.That(particlePhase.X - first.X, Is.GreaterThan(.1f));
        }
        finally { graphics.Release(); }
    }

    [TestCase(SurfaceFormat.Color)]
    [TestCase(SurfaceFormat.Vector4)]
    public void OrdinaryPremultipliedSprite_ThinCoverageKeepsItsHdrTint(SurfaceFormat format)
    {
        var graphics=new GraphicsDeviceServiceMock();
        try
        {
            var device=graphics.GraphicsDevice; var direct=(SharpDX.Direct3D11.Device)device.Handle;
            using var preview=EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,[255,255,255,64],4,1,1,0));
            var layer=EffekseerLayer.Create(); layer.Kind=0; layer.MaterialMode=1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
            var background=new Color(26,51,77).ToVector4();
            var actual=Render(device,preview,new(4,2,1,1),0,background,32,format);
            var alpha=MathF.Pow((64f/255+.055f)/1.055f,2.4f);
            var expected=new Vector3(4,4*128f/255,4*64f/255)*alpha+new Vector3(background.X,background.Y,background.Z)*(1-alpha);
            Assert.That(Vector3.Distance(new(actual.X,actual.Y,actual.Z),expected),Is.LessThan(.009f),
                "The same 8-bit truncation must not desaturate ordinary premultiplied sprites after model-only recovery.");
        }
        finally { graphics.Release(); }
    }

    private static EffekseerVertex[] Quad(Vector4 colour) =>
    [
        new(){Position=new(-1,-1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(0,1),Colour=colour},
        new(){Position=new(1,-1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(1,1),Colour=colour},
        new(){Position=new(-1,1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(0,0),Colour=colour},
        new(){Position=new(1,1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(1,0),Colour=colour},
    ];

    private static Vector4 Render(GraphicsDevice device, EffekseerHandle preview, Vector4 colour, float seconds, Vector4 background, int x, SurfaceFormat format=SurfaceFormat.Vector4, int samples=0, bool occluded=false, float appearancePhase=0, bool reflected=true)
    {
        using var target=new RenderTarget2D(device,64,64,false,format,DepthFormat.Depth24,samples,RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,new Color(background),occluded ? 0 : 1,0); device.DepthStencilState=DepthStencilState.Default;
        var view=Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up);
        var projection=Matrix.CreateOrthographic(2,2,.1f,100);
        if (reflected) projection *= Matrix.CreateScale(-1,1,1);
        var particle=new EffekseerParticle{Transform=Matrix.Identity,Colour=colour,UV=new(0,0,1,1),MaterialTime=seconds,AppearancePhase=appearancePhase};
        var context=((SharpDX.Direct3D11.Device)device.Handle).ImmediateContext;
        using var previousTarget=context.OutputMerger.GetRenderTargets(1,out var previousDepth)[0]; using var previousDepthView=previousDepth;
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,Values(view),Values(projection),[particle],1));
        using var restoredTarget=context.OutputMerger.GetRenderTargets(1,out var restoredDepth)[0]; using var restoredDepthView=restoredDepth;
        Assert.That(restoredTarget.NativePointer,Is.EqualTo(previousTarget.NativePointer),"Restore the actual DX11 colour target, beyond MonoGame's cached binding.");
        Assert.That(restoredDepthView.NativePointer,Is.EqualTo(previousDepthView.NativePointer),"Keep the original scene depth attachment.");
        device.SetRenderTarget(null);
        if(format==SurfaceFormat.Color) { var colours=new Color[4096]; target.GetData(colours); return colours[32*64+x].ToVector4(); }
        var pixels=new Vector4[4096]; target.GetData(pixels); return pixels[32*64+x];
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
