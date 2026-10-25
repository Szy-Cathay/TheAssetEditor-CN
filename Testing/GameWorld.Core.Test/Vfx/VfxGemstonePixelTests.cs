#nullable enable
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxGemstonePixelTests
{
    [TestCase(1f)]
    [TestCase(.5f)]
    [TestCase(.4f)]
    public void ParticleAlpha_ChangesTheDissolveEdgeAndCutoutInsteadOfOrdinaryTransparency(float alpha)
    {
        var root = VfxGemstoneMaterialTests.Material();
        var actual = Render(root, [Particle(alpha)]);
        var noise = 128f / 255; noise = noise * noise * (3 - 2 * noise) + .1f;
        var field = noise - (1 - MathF.Floor(alpha * 1000) * .001f) * 1.325f;
        var expected = new Vector3(.1f,.2f,.3f);
        if (field + .15f < 0) expected = Background;
        else if (alpha < 1 && field < 0)
        {
            var distance = .15f - 2 * field;
            var weight = Math.Clamp((distance + 1) / 3.5f, 0, 1); weight = weight * weight * (3 - 2 * weight);
            var end = Math.Clamp((distance - .3f) / .3f, 0, 1); end = end * end * (3 - 2 * end);
            expected = Vector3.Lerp(Vector3.Up,Vector3.Right,weight) * Math.Max(Math.Clamp(distance,0,1)-end,0) * 4;
        }
        Assert.That(Vector3.Distance(actual,expected), Is.LessThan(.0002f));
    }

    [Test]
    public void AuthoredColour_UsesSeparateRefractionAndSurfaceEmissionInputs()
    {
        var root = VfxGemstoneMaterialTests.Material();
        VfxGemstoneMaterialTests.Set(root,"refract_color","0.5,0.25,0.75");
        VfxGemstoneMaterialTests.Set(root,"surface_emissive_color","0.2,0.4,0.6");
        VfxGemstoneMaterialTests.Set(root,"surface_emissive_strength","2");
        var actual = Render(root,[Particle(1)]);
        var expected = new Vector3(.1f,.2f,.3f) + new Vector3(64,128,192)/255 * new Vector3(.5f,.25f,.75f)
            + new Vector3(128,64,255)/255 * new Vector3(.2f,.4f,.6f) * 2;
        Assert.That(Vector3.Distance(actual,expected), Is.LessThan(.0002f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Fresnel_RespondsToTheModelFaceAndCamera(bool edgeOn)
    {
        var root = VfxGemstoneMaterialTests.Material();
        VfxGemstoneMaterialTests.Set(root,"fresnel_color","0.1,0.2,0.3");
        VfxGemstoneMaterialTests.Set(root,"fresnel_glow_strength","2");
        VfxGemstoneMaterialTests.Set(root,"fresnel_power_bias","2,0.25");
        var actual = Render(root,[Particle(1)],edgeOn:edgeOn);
        var view = Vector3.Normalize(new Vector3(-.015625f,.015625f,10));
        var facing = Vector3.Dot(edgeOn ? Vector3.Up : Vector3.Backward,view);
        var fresnel = MathF.Pow(1-Math.Max(facing,0),2)*.75f+.25f;
        var expected = new Vector3(.1f,.2f,.3f) + new Vector3(.1f,.2f,.3f)*fresnel*2;
        Assert.That(Vector3.Distance(actual,expected), Is.LessThan(.0002f));
    }

    [Test]
    public void OpaqueCutout_WritesDepthSoAFartherModelCannotCoverTheNearerModel()
    {
        var root = VfxGemstoneMaterialTests.Material();
        VfxGemstoneMaterialTests.Set(root,"back_color","0,0,0");
        VfxGemstoneMaterialTests.Set(root,"surface_emissive_color","1,1,1");
        var near = Particle(1); near.Transform = Matrix.CreateTranslation(0,0,1); near.UV = new(.25f,.5f,0,0);
        var far = Particle(1); far.UV = new(.75f,.5f,0,0);
        Assert.That(Vector3.Distance(Render(root,[near,far],splitEmission:true),Vector3.Right), Is.LessThan(.0002f));
    }

    private static readonly Vector3 Background = new(26f/255,51f/255,77f/255);
    private static EffekseerParticle Particle(float alpha) => new() { Transform=Matrix.Identity,Colour=new(1,1,1,alpha),UV=new(0,0,1,1),ParticleSize=1 };

    private static Vector3 Render(System.Xml.Linq.XElement root, EffekseerParticle[] particles, bool edgeOn=false, bool splitEmission=false)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var material = VfxGemstoneMaterial.Read(root); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
            byte[][] textures = [[128,128,128,255],[128,128,128,255],[128,128,0,255],[64,128,192,255],
                splitEmission ? [255,0,0,255,0,255,0,255] : [128,64,255,255],[128,128,128,255]];
            for(var i=0;i<textures.Length;i++) EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,textures[i],textures[i].Length,splitEmission&&i==4?2:1,1,0));
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,[0,1,2,3,4,5],6,material.Parameters,material.Parameters.Length));
            var normal = edgeOn ? Vector3.Up : Vector3.Backward;
            EffekseerVertex[] vertices =
            [
                new(){Position=new(-1,-1,0),Normal=normal,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(0,1)},
                new(){Position=new(1,-1,0),Normal=normal,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(1,1)},
                new(){Position=new(-1,1,0),Normal=normal,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(0,0)},
                new(){Position=new(1,1,0),Normal=normal,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(1,0)},
            ];
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview,vertices,4,[0,1,2,2,1,3],6));
            var layer=EffekseerLayer.Create();layer.Kind=1;layer.ModelIndex=0;layer.MeshMaterialIndex=0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
            using var target=new RenderTarget2D(device,64,64,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
            device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,new Color(Background),1,0);
            var view=Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up);var projection=Matrix.CreateOrthographic(2,2,.1f,100);
            float[] Values(Matrix m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,Values(view),Values(projection),particles,particles.Length));
            device.SetRenderTarget(null);var pixels=new Vector4[4096];target.GetData(pixels);var pixel=pixels[32*64+32];return new(pixel.X,pixel.Y,pixel.Z);
        }
        finally { graphics.Release(); }
    }
}
