#nullable enable
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxMeshColourDistortionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void OriginalMaterials_PreserveTheirReflectedLayoutAndOptionalSeventhSlot(bool oneShot)
    {
        var original=Original(oneShot);var hash=Convert.ToHexStringLower(SHA256.HashData(original));
        Assert.That(hash,Is.EqualTo(oneShot?OneShotSha:BasicSha));
        var material=VfxScrollingMeshMaterial.Read(original);
        Assert.That(material.Parameters,Has.Length.EqualTo(oneShot?54:55));
        Assert.That(material.TexturePaths,Has.Length.EqualTo(oneShot?6:7));
        if(!oneShot)Assert.That(material.Parameters[34],Is.EqualTo(3),"The native switch is a scalar amplitude, not a boolean clamp.");
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)),Is.EqualTo(hash));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActiveColourDistortion_RequiresItsExactSeventhOriginalInput(bool oneShot)
    {
        var root=XElement.Parse(Encoding.UTF8.GetString(Original(oneShot)));
        Set(root,oneShot?"colour_distortion_switch":"distortion_colour_switch",".35");
        root.Element("textures")!.Elements("texture").Single(t=>t.Element("slot")!.Value=="t_xml_distortion_texture").Remove();
        var failure=Assert.Throws<VfxMappingException>(()=>VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(failure!.Field,Is.EqualTo("t_xml_distortion_texture"));
        Assert.That(failure.Reason,Is.EqualTo("Vfx.Preview.MissingResource"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SeventhSlot_ReachesActualModelConversionWithoutLosingTheOriginalSix(bool oneShot)
    {
        var root=XElement.Parse(Encoding.UTF8.GetString(Original(oneShot)));
        Set(root,oneShot?"colour_distortion_switch":"distortion_colour_switch",".35");
        var raw=Encoding.UTF8.GetBytes(root.ToString());var parsed=VfxScrollingMeshMaterial.Read(raw);
        var conversion=TotalWarVfxConverter.Convert(new(VfxOpaqueModelMaterialTests.Source(Vector3.One,1),"DIAG/native-seven-slot-model.xml",path=>
            path.Replace('\\','/')=="vfx/character.xml.material"?raw:VfxOpaqueModelMaterialTests.Resolve(path,VfxModelVariantPixelTests.Dds(Color.White))));
        Assert.That(conversion.Emitters,Has.Count.EqualTo(1),string.Join(';',conversion.Diagnostics.Select(d=>d.Field+":"+d.ReasonKey)));
        var packet=conversion.MeshMaterials.Single();
        Assert.That(packet.Parameters,Is.EqualTo(parsed.Parameters));
        Assert.That(packet.TextureIndexes.Select(i=>conversion.TexturePaths[i]),Is.EqualTo(parsed.TexturePaths));
        Assert.That(packet.TextureIndexes,Has.Length.EqualTo(7));
    }

    internal static void Set(XElement root,string field,string value)=>root.Element("params")!.Elements("param").Single(p=>p.Element("name")!.Value==field).Element("value")!.Value=value;
    internal static byte[] Original(bool oneShot)
    {
        using var input=new MemoryStream(Convert.FromBase64String(oneShot?OneShot:Basic));using var gzip=new GZipStream(input,CompressionMode.Decompress);
        using var output=new MemoryStream();gzip.CopyTo(output);return output.ToArray();
    }
    private const string BasicSha="911f8c620ac90855a6badaf45934ef9a1c2ba04d4065610ef0a944cf71555caf";
    private const string OneShotSha="6c91b8da455c37e5bf2fec20b1f3e2f0fb3ae9e80787dc0ba844d13dc51ac5c9";
    private const string Basic="H4sIAAAAAAACCsWYzW7bMAyA7wP2DkXPmf+yABvg+Sl2F1SbjoTqx5DoOH37yZKTtECzRWrcIYdYNsWPFCmKdi0pguFUNF+/PNSKSmgmRjrRVj+J1aMgaCgXpCizoxR17gVmSctoB6YJfzaf2JYc+iN5opa3RIJls3wWHtf5Iu0naoNEH8AY3kFT7Xbu6ZtbsxDCEUcDdh6cR37gFAiND07Ycq1+PVaPDRKHIq0WejRejaAvTqkTO81wD1poWi2lVifVeQc9HQWSiXGErOvsbIgX9ND8NfU2Ezre96MFUr5Hd6uT/z6x/aKGVdszbfG0ytVJx13tqZLtKVexZ5tqz32Nsejyzt0ki/x/yRlJ7fMNCUPFwCiBbg9hhpX6Ge66Hl5tdd81OA/8Rq4Haqhc9rS/Xji+qLzdv2Sg6lJtZh9eBmh6oSk6G/0g3D9QMUJTbL4VmUurMAp2XAj/hFk0oPbIrhDfAZbJLOQCYjzLdpsIWAcDMtK7akuo1KPC212qkih24tius3DnckqeDN8zVGDt7aBdCqhlVCkQ1zDf33NoM/+KFFpsjkel+AUTnXE/NgmcKilKLr1TUMlxKlNokXHKqjRMZJySnNkmRSkJ9Jkx2kbHyG3ZrEoBxUYpK4oiroRfGpPl6IitsRG7dy/0tGCu6N9ei1MkJP6M3UUU1dBGxVfUaMK6x3dowlZ3Y91aExrUNVsqpY2kIjQgXBE94uqYQU9gVmmmlhY5llDGnJyo0TkSXiJWjYxrcp0/C6l1bw2G2nV60IX019r1QV/6o/+SEbz5OGK5nF+A6vzVZ5c/G3bxr4MRAAA=";
    private const string OneShot="H4sIAAAAAAACCs1YwY6bMBC9V+o/rPacDRvY3lK+onfLgSFYNTayh5Ds13dsSEKlUK3ZOFtFUWwy9nvDzLwxbBuOYASX+fdvT1vFG8j7OmOlLNJXtuOIEpgU+xqVUHumFTBba1wfG7lNvLVbZmtegsmHH5u4DQ7V8WLNGrC1W7IeLLbJuMCv1QaZPoAxooQ8zV7p378uOSOEI3YGrJtcZn5CG0iNT2RshVY/n9PnHBlBsUJL3Rm/jeQn2pTMzivojwJyopj8GjdOrk5PvB3WsnRdltbR8ss8hWTK4WOESmGRHKOLbLS/RarQTaPV2d+khIp3EllfC4S70NhLveOSNdz+/hJ8SoohJ2YIzESFv/N3kMz22pRMt7wQeKJbWlWdvQ8vMmKbAEIoJJSTZLk3l/Q/4pLF5nKZ+ArfttzwZix2Px6RveCMhT0pJ9sLLOqrHjnXTi3kldQcaWs3Hi4fuOwgJ4kZBgOPK8Is2FkJWq5mYNJbOKvlSBYNqD0GeLVZjOVCFuDWZhUAVUKLNatI7hlvdKfw4w6li1AiJsNEOmNmwhQmMDShOP3YJmcgslsQ6x+r8RsIFZ7SAQhKm4Zumk8BoZjuMEoKTGFa3YOJgjLtkSR0Bgqnc0F58JIthKOMI9mOJQe+y7Kd8e0BrI0ibwNIUXOlQM5BvN0uH/oEI4VqwUswQqgMUH2+hWGkjwhK+rCgpNGDkj4gKNkjgpItCQoV/Sq48LPoQcniB8WdUkOlfxPSK1EjdRgu25ovaJkBztDpz92zAamghz7DbZzj2Yj0z7PG5zqme93g3jIM3ny+VsahewbZJpNXJH8Ao6/5TC8RAAA=";
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxMeshColourDistortionPixelTests
{
    [SetUp]
    public void RequireOffscreen(){if(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST")!="1")Assert.Ignore("Native GPU is opt-in.");}

    [TestCase(false,.35f)]
    [TestCase(false,1f)]
    [TestCase(false,3f)]
    [TestCase(true,.35f)]
    [TestCase(true,1f)]
    [TestCase(true,3f)]
    public void NativeRbDistortionOverlayUv_MatchesIndependentlyBakedUv(bool oneShot,float amplitude)
    {
        WithDevice(device=>
        {
            var p=Parameters(oneShot);p[oneShot?0:34]=amplitude;
            const float global=.73f;var uv=new Vector2(.31f,.61f);
            var expectedUv=(uv+amplitude*new Vector2(23f/255,173f/255)*.1f+global*new Vector2(.12f,-.17f))*new Vector2(1.37f,.73f);
            var reference=p.ToArray();reference[oneShot?0:34]=0;var pan=oneShot?1:0;var tile=oneShot?4:3;
            reference[pan]=reference[pan+1]=0;reference[tile]=reference[tile+1]=1;
            var actual=Render(device,p,uv,global,[23,211,173,255]);
            var expected=Render(device,reference,expectedUv,global,[23,211,173,255]);
            Assert.That(Vector4.Distance(actual,expected),Is.LessThanOrEqualTo(.0001f),$"native RB UV actual={actual}, independent baked={expected}");
            var undistortedUv=(uv+global*new Vector2(.12f,-.17f))*new Vector2(1.37f,.73f);
            var wrong=Render(device,reference,undistortedUv,global,[23,211,173,255]);
            Assert.That(Vector3.Distance(new(actual.X,actual.Y,actual.Z),new(wrong.X,wrong.Y,wrong.Z)),Is.GreaterThan(.02f),"A source-distortion no-op cannot satisfy the baked control.");
            Assert.That(actual.X+actual.Y+actual.Z,Is.GreaterThan(.1f));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeColourDistortion_UsesBlueAndIgnoresGreen(bool oneShot)
    {
        WithDevice(device=>
        {
            var p=Parameters(oneShot);p[oneShot?0:34]=1;var uv=new Vector2(.31f,.61f);
            var original=Render(device,p,uv,.73f,[23,211,173,255]);var green=Render(device,p,uv,.73f,[23,3,173,255]);
            var blue=Render(device,p,uv,.73f,[23,211,17,255]);
            Assert.That(Vector4.Distance(original,green),Is.LessThanOrEqualTo(.0001f));
            Assert.That(Vector4.Distance(original,blue),Is.GreaterThan(.01f));
        });
    }

    [TestCase(false,false)]
    [TestCase(false,true)]
    [TestCase(true,false)]
    [TestCase(true,true)]
    public void ProductionFrameAndSubmission_KeepRawAlphaForTheMappedPartAndClampOnlyThePlainPart(bool oneShot,bool mappedFirst)
    {
        WithDevice(device=>
        {
            var actual=RenderProductionAlpha(device,oneShot,mappedFirst,2.5f);
            var control=RenderProductionAlpha(device,oneShot,mappedFirst,1);
            Assert.That(actual.Packets.Select(p=>p.Colour.W).Order(),Is.EqualTo(new[]{1f,2.5f}));
            var red=actual.Pixels.Select((p,i)=>(Pixel:p,Index:i)).Where(x=>x.Pixel.X>.1f&&x.Pixel.Y<.001f).ToArray();
            Assert.That(red,Has.Length.GreaterThan(20));
            Assert.That(red.Max(x=>Math.Abs(x.Pixel.X-control.Pixels[x.Index].X*2.5f)),Is.LessThan(.0001f),
                "Native floor(alpha*1000) scales the unsaturated colour mask; a production alpha clamp cannot match the independent 2.5x control.");
            var green=actual.Pixels.Select((p,i)=>(Pixel:p,Index:i)).Where(x=>x.Pixel.Y>.2f&&x.Pixel.X<.001f).ToArray();
            Assert.That(green,Has.Length.GreaterThan(20));
            Assert.That(green.Max(x=>Vector4.Distance(x.Pixel,control.Pixels[x.Index])),Is.LessThan(.0001f),
                "The ordinary part of the same actual model must retain alpha1 in either material order.");
        });
    }

    private static (Vector4[] Pixels,EffekseerParticle[] Packets) RenderProductionAlpha(GraphicsDevice device,bool oneShot,bool mappedFirst,float alpha)
    {
        var material=XElement.Parse(Encoding.UTF8.GetString(VfxMeshColourDistortionTests.Original(oneShot)));
        foreach(var p in material.Element("params")!.Elements("param"))p.Element("value")!.Value=string.Join(',',p.Element("value")!.Value.Split(',').Select(_=>"0"));
        void Set(string field,string value)=>VfxMeshColourDistortionTests.Set(material,field,value);
        foreach(var field in new[]{"colour_overlay_strength","glow_strength","normal_fade_in_out","normal_fade_power","texture_power","vertex_alpha_contrast","vfx_mesh_alpha"})Set(field,"1");
        Set("glow_colour","1,1,1");Set("colour_overlay_tile","1,1");Set("total_alpha_strength",".2");
        foreach(var i in new[]{1,2,3}){var prefix=(oneShot?"tex_":"diffuse_")+i;Set(prefix+"_brightness","1");Set(prefix+"_channels","1,0,0,0");Set(prefix+"_tile","1,1");}
        if(oneShot){Set("global_mask_tile","1,1");Set("oneshot_mask_tiling","1,1");}
        else{Set("mask_1_tile","1,1");Set("mask_2_tile","1,1");Set("mask_strength","1");}
        foreach(var texture in material.Element("textures")!.Elements("texture"))texture.Element("source")!.Value=texture.Element("slot")!.Value=="t_xml_colour_overlay"?"vfx/red.dds":"vfx/white.dds";
        var materialBytes=Encoding.UTF8.GetBytes(material.ToString());
        var file=ModelFactory.Create().Load(VfxModelVariantTests.Model(0));
        file.ModelList[0]=[file.ModelList[0][0],ModelFactory.Create().Load(VfxModelVariantTests.Model(1)).ModelList[0][0]];file.RecalculateOffsets();var model=ModelFactory.Create().Save(file);
        var wrapper=Encoding.UTF8.GetBytes($"<model version=\"1\"><geometry>vfx/mixed.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"{(mappedFirst?0:1)}\">vfx/mapped.xml.material</material></materials></model>");
        var source=VfxDocument.Read(VfxOpaqueModelMaterialTests.Source(Vector3.One,alpha));source.Root.Descendants("emitter").Single().Add(new XElement("property",new XAttribute("name","MaximumParticles"),new XAttribute("value",1)));
        byte[]? Resolve(string path)=>path.Replace('\\','/') switch
        {
            "vfx/character.wsmodel"=>wrapper,"vfx/mixed.rigid_model_v2"=>model,"vfx/mapped.xml.material"=>materialBytes,
            "vfx/red.dds"=>VfxModelVariantPixelTests.Dds(Color.Red),"vfx/white.dds"=>VfxModelVariantPixelTests.Dds(Color.White),
            "vfx/texture-0.dds" or "vfx/texture-1.dds"=>VfxModelVariantPixelTests.Dds(Color.Lime),_=>null,
        };
        using var preview=new EffekseerVfxPreview(device);preview.Reload(new(source.Write(),"DIAG/raw-basic-oneshot-mixed.xml",Resolve));
        Assert.That(preview.EmitterCount,Is.EqualTo(1),string.Join(';',preview.Diagnostics.Select(d=>d.Field+":"+d.ReasonKey)));preview.Seek(.2f,Matrix.Identity);
        using var target=new RenderTarget2D(device,128,64,false,SurfaceFormat.Vector4,DepthFormat.Depth24);device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);
        var eye=new Vector3(1,.5f,10);preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(eye,new(1,.5f,0),Vector3.Up),Matrix.CreateOrthographic(2.6f,1.3f,.1f,100),eye,Vector3.Zero,0,0,0,1,Vector3.One,[]));
        device.SetRenderTarget(null);var pixels=new Vector4[128*64];target.GetData(pixels);
        var packets=((List<EffekseerParticle>)typeof(EffekseerVfxPreview).GetField("_renderedParticles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview)!).ToArray();
        return(pixels,packets);
    }

    private static float[] Parameters(bool oneShot)
    {
        float[] p;
        if(oneShot)
        {
            p=new float[54];foreach(var i in new[]{3,4,5,10,11,12,13,14,15,16,17,20,21,22,23,29,30,31,32,38,39,40,41,47,48,49,50,51})p[i]=1;
        }
        else p=VfxBasicMeshMaterial.Read(VfxBasicMeshMaterialTests.Material()).Parameters;
        var pan=oneShot?1:0;var tile=oneShot?4:3;p[pan]=.12f;p[pan+1]=-.17f;p[tile]=1.37f;p[tile+1]=.73f;return p;
    }

    private static Vector4 Render(GraphicsDevice device,float[] p,Vector2 uv,float global,byte[] distortion)
    {
        var d3d=(SharpDX.Direct3D11.Device)device.Handle;using var preview=EffekseerNative.AecnVfxCreate(d3d.NativePointer,d3d.ImmediateContext.NativePointer);
        byte[] white=[255,255,255,255];
        for(var i=0;i<7;++i)
        {
            if(i==3)
            {
                var gradient=Enumerable.Range(0,16).SelectMany(n=>new byte[]{(byte)(n%4*64),(byte)(n/4*64),(byte)((n%4+2*(n/4))*28),255}).ToArray();
                EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,gradient,gradient.Length,4,4,0));
            }
            else{var bytes=i==6?distortion:white;EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview,bytes,4,1,1,0));}
        }
        var textureCount=p[p.Length==54?0:34]==0?6:7;
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview,Enumerable.Range(0,textureCount).ToArray(),textureCount,p,p.Length));
        var vertices=new[]{new Vector3(-1,-1,0),new(1,-1,0),new(-1,1,0),new(1,1,0)}.Select(position=>new EffekseerVertex
        {Position=position,Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=uv,Colour=Vector4.One}).ToArray();
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview,vertices,4,[0,1,2,2,1,3],6));
        var layer=EffekseerLayer.Create();layer.Kind=1;layer.ModelIndex=0;layer.MeshMaterialIndex=0;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview,[layer],1));
        using var target=new RenderTarget2D(device,64,64,false,SurfaceFormat.Vector4,DepthFormat.Depth24);device.SetRenderTarget(target);device.Clear(ClearOptions.Target|ClearOptions.DepthBuffer,Color.Black,1,0);
        var particle=new EffekseerParticle{Transform=Matrix.Identity,Colour=Vector4.One,UV=new(0,0,1,1),ParticleSize=1,MaterialTime=global,Time=.17f,AppearancePhase=.31f};
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,Values(Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up)),Values(Matrix.CreateOrthographic(2,2,.1f,100)),[particle],1));
        device.SetRenderTarget(null);var pixels=new Vector4[4096];target.GetData(pixels);return pixels[32*64+32];
    }
    private static float[] Values(Matrix m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static void WithDevice(Action<GraphicsDevice> action){var graphics=new GraphicsDeviceServiceMock();try{action(graphics.GraphicsDevice);}finally{graphics.Release();}}
}
