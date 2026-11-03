#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxDissolveChannelPixelTests
{
    public static IEnumerable<TestCaseData> Channels()
    {
        foreach(var family in new[]{44,184,208})
            foreach(var channel in family==208 ? new[]{"noise-r","global-r"} : new[]{"noise-r","global-r","emission-a"})
                foreach(var packed in new[]{false,true})
                    yield return new TestCaseData(family,channel,packed);
    }

    [TestCaseSource(nameof(Channels))]
    public void NativeDestinationMasks_SelectTheOriginalTextureChannels(int family,string channel,bool packed)
    {
        if(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST")!="1")Assert.Ignore("Native GPU is opt-in.");
        // Every RGBA vector is asymmetric. Other mask channels remain safely on the same side of their clip.
        byte[][] colours=[[51,102,153,201],[17,39,230,111],
            channel=="noise-r" ? [230,30,80,250] : [230,200,80,250],
            [51,77,99,130],[128,64,32,160],
            channel=="global-r" ? [200,30,70,100] : channel=="emission-a" ? [200,160,70,30] : [230,200,70,100]];
        var xml=family==208 ? VfxLegacyRigidDissolveMaterialTests.Material()
            : family==44 ? VfxNativeRigidDissolveMaterialTests.Material() : VfxRigidDissolveMaterialTests.Material();
        void Set(string name,string value)=>VfxGemstoneMaterialTests.Set(xml,name,value);
        Set("diffuse_tint",channel=="emission-a" ? "0,0,0" : "1,1,1");
        Set("edge_emissive_colour","0,0,0");Set("edge_emissive_intensity","0");
        Set("emissive_a_brightness",channel=="emission-a" ? "0.5" : "0");Set("emissive_a_channels","1,0,0,0");
        Set("emissive_b_brightness","0.7");Set("emissive_b_channels","0,1,0,0");
        Set("emissive_tint","0.9,0.6,0.3");
        if(family!=208)Set("emissive_intensity","2.1");
        Set("threshold_source","1");Set("threshold_value","0.5");Set("manual_dither_wangle","0");
        var textureNodes=xml.Elements("textures").Elements("texture").ToArray();
        var roles=family==208 ? new[]{0,1,2,3,4,4,4,5,5,5} : new[]{0,1,2,3,4,4,5,5,5,5};
        for(var index=0;index<textureNodes.Length;index++)
            textureNodes[index].Element("source")!.Value=packed ? $"unique-{index}.dds" : $"role-{roles[index]}.dds";
        var parsed=VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(xml.ToString()));
        Assert.That(parsed.Parameters[family==208 ? 67 : 59],Is.EqualTo(packed ? 1 : 0));
        var inputs=colours;
        if(packed)
        {
            var atlas=VfxMaterialMaskAtlas.PackThree((colours[1],1,1),(colours[2],1,1),(colours[4],1,1));
            atlas.Rectangles.CopyTo(parsed.Parameters,family==208 ? 52 : 46);
            inputs=[colours[0],colours[3],colours[5],colours[family==208 ? 4 : 5],colours[4],atlas.Pixels];
            // Keep the real packed image dimensions for the texture upload below.
            RenderAndCompare(atlas.Width,atlas.Height);
        }
        else RenderAndCompare(1,1);

        void RenderAndCompare(int atlasWidth,int atlasHeight)
        {
            var graphics=new GraphicsDeviceServiceMock();
            try
            {
                var device=graphics.GraphicsDevice;var direct=(SharpDX.Direct3D11.Device)device.Handle;
                using var handle=EffekseerNative.AecnVfxCreate(direct.NativePointer,direct.ImmediateContext.NativePointer);
                for(var index=0;index<inputs.Length;index++)
                    EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle,inputs[index],inputs[index].Length,
                        packed && index==5 ? atlasWidth : 1,packed && index==5 ? atlasHeight : 1,0));
                EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle,[0,1,2,3,4,5],6,parsed.Parameters,parsed.Parameters.Length));
                var vertices=new[]{new Vector3(-1,-1,0),new(1,-1,0),new(-1,1,0),new(1,1,0)}.Select(position=>new EffekseerVertex
                {Position=position,Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=new(.375f,.625f),Colour=Vector4.One}).ToArray();
                EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle,vertices,4,[0,1,2,2,1,3],6));
                var layer=EffekseerLayer.Create();layer.Kind=1;layer.ModelIndex=0;layer.MeshMaterialIndex=0;layer.TextureIndex=0;layer.OpaqueModel=1;
                EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle,[layer],1));
                var particle=new EffekseerParticle {Transform=Matrix.Identity,UV=new(0,0,1,1),Colour=Vector4.One};
                using var target=new RenderTarget2D(device,32,32,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
                device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);
                var view=Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up);var projection=Matrix.CreateOrthographic(2,2,.1f,100);
                try{EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle,Values(view),Values(projection),[particle],1));}
                finally{device.SetRenderTarget(null);}
                var pixels=new Vector4[1024];target.GetData(pixels);
                // Original PS: dissolve.B*noise.R-.5 is above the edge interval, so coverage=global.R.
                var field=colours[1][2]/255.0*colours[2][0]/255.0-.5;
                Assert.That(field,Is.GreaterThan(.09));Assert.That(colours[5][0],Is.GreaterThan(colours[3][0]));
                var expected=new Vector4(colours[0][0]/255f,colours[0][1]/255f,colours[0][2]/255f,1);
                if(channel=="emission-a")
                {
                    // With zero base and a subunit emission peak, the native G-buffer intrinsic reduces to this glow.
                    var emission=(128/255.0*.5)*(64/255.0*.7)*(30/255.0)*2.1;
                    expected=new((float)(emission*.9),(float)(emission*.6),(float)(emission*.3),1);
                }
                Assert.That(Vector4.Distance(pixels[16*32+16],expected),Is.LessThan(.00002f),
                    $"Family={family}, channel={channel}, packed={packed}: actual={pixels[16*32+16]}, expected={expected}");
            }
            finally{graphics.Release();}
        }
    }

    private static float[] Values(Matrix m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
