#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxBasicMeshMaterialTests
{
    [Test]
    public void SourceMaterial_BindsAllSixActiveInputsAndKeepsTheOriginalDocument()
    {
        var root = Material(); var bytes = Encoding.UTF8.GetBytes(root.ToString());
        var material = VfxScrollingMeshMaterial.Read(bytes);
        Assert.That(material.Parameters, Has.Length.EqualTo(55));
        Assert.That(material.TexturePaths, Is.EqualTo(new[] { "diffuse_1.dds", "diffuse_2.dds", "diffuse_3.dds", "colour_overlay.dds", "mask_1.dds", "mask_2.dds" }));
        Assert.That(material.UsesVertexColour, Is.True);
        Assert.That(Encoding.UTF8.GetBytes(root.ToString()), Is.EqualTo(bytes));
    }

    [Test]
    public void ActiveColourDistortion_IsRejectedRatherThanDroppingTheSeventhInput()
    {
        var root = Material(); Set(root, "distortion_colour_switch", "1");
        var exception = Assert.Throws<VfxMappingException>(() => VfxBasicMeshMaterial.Read(root));
        Assert.That(exception!.Field, Is.EqualTo("distortion_colour_switch"));
        Assert.That(exception.Reason, Is.EqualTo("Vfx.Preview.MaterialUnsupported"));
    }

    [Test]
    public void MissingSecondMask_IsNotReplacedByTheColourOverlay()
    {
        var root = Material(); root.Descendants("texture").Single(n => n.Element("slot")!.Value == "t_xml_mask_2").Remove();
        Assert.That(Assert.Throws<VfxMappingException>(() => VfxBasicMeshMaterial.Read(root))!.Field, Is.EqualTo("t_xml_mask_2"));
    }

    internal static XElement Material()
    {
        var root = new XElement("material", new XElement("shader", "shaders/wh3_vfx_basic_mesh.xml.shader"),
            new XElement("textures", new[] { "diffuse_1", "diffuse_2", "diffuse_3", "colour_overlay", "mask_1", "mask_2", "distortion_texture" }
                .Select(name => new XElement("texture", new XElement("slot", "t_xml_" + name), new XElement("source", name + ".dds")))),
            new XElement("params", VfxBasicMeshMaterial.Fields.Select(field => new XElement("param", new XElement("name", field.Name),
                new XElement("value", string.Join(',', Enumerable.Repeat("0", field.Components)))))));
        foreach (var name in new[] { "colour_overlay_tile", "diffuse_1_tile", "diffuse_2_tile", "diffuse_3_tile", "mask_1_tile", "mask_2_tile" }) Set(root, name, "1,1");
        foreach (var name in new[] { "colour_overlay_strength", "diffuse_1_brightness", "diffuse_2_brightness", "diffuse_3_brightness", "glow_strength",
            "mask_strength", "normal_fade_in_out", "normal_fade_power", "texture_power", "total_alpha_strength", "vertex_alpha_contrast", "vertex_colour", "vfx_mesh_alpha" }) Set(root, name, "1");
        foreach (var name in new[] { "diffuse_1_channels", "diffuse_2_channels", "diffuse_3_channels" }) Set(root, name, "1,0,0,0");
        Set(root, "glow_colour", "1,1,1");
        return root;
    }

    internal static void Set(XElement root, string name, string value) => root.Descendants("param").Single(p => p.Element("name")!.Value == name).Element("value")!.Value = value;
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxBasicMeshPixelTests
{
    [TestCase(false, .5f)]
    [TestCase(false, 4f)]
    [TestCase(true, .5f)]
    public void NativeMaterial_CombinesChannelMasksPowerBothEdgeMasksAndVertexAlpha(bool transparent, float maskStrength)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var root = VfxBasicMeshMaterialTests.Material();
        void Set(string name, string value) => VfxBasicMeshMaterialTests.Set(root, name, value);
        Set("diffuse_1_channels", "0,1,0,0"); Set("diffuse_1_brightness", "2");
        Set("diffuse_2_channels", "0,0,0,1"); Set("diffuse_2_brightness", "0.5");
        Set("diffuse_3_channels", "0,0,1,0"); Set("diffuse_3_brightness", "3");
        Set("texture_power", "2"); Set("mask_strength", maskStrength.ToString(CultureInfo.InvariantCulture));
        Set("vertex_alpha_contrast", "2"); Set("total_alpha_strength", "0.75");
        Set("colour_overlay_strength", "3"); Set("glow_colour", "1,2,3"); Set("glow_strength", "4");
        var parameters = VfxBasicMeshMaterial.Read(root).Parameters;
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            foreach (var texture in new[] { new byte[] { 64,128,192,255 }, new byte[] { 128,64,128,255 }, new byte[] { 255,192,64,255 },
                new byte[] { 77,102,128,128 }, new byte[] { 204,0,0,255 }, new byte[] { 128,0,0,255 } })
                EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, texture, 4, 1, 1, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0,1,2,3,4,5], 6, parameters, parameters.Length));
            var alpha = transparent ? 0 : 128f/255;
            var vertices = Quad(new(.6f,.8f,1,alpha));
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0,1,2,2,1,3], 6));
            var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            var background = new Color(26,51,77).ToVector4();
            var actual = Render(device, preview, new(.5f,1,.25f,.5f), background);
            var mask = MathF.Pow((128f/255*2) * .5f * (64f/255*3), 2)
                * Math.Clamp(204f/255*128f/255*maskStrength,0,1) * alpha*alpha * .75f * .5f;
            var rgb = new Vector3(77f/255,102f/255,128f/255) * (1 + 128f/255*2) * new Vector3(.6f,.8f,1)
                * new Vector3(1,2,3) * 4 * new Vector3(.5f,1,.25f) * mask;
            var expected = rgb + new Vector3(background.X,background.Y,background.Z)*(1-Math.Clamp(mask,0,1));
            Assert.That(Vector3.Distance(new(actual.X,actual.Y,actual.Z),expected), Is.LessThan(.0001f));
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

    private static Vector4 Render(GraphicsDevice device, EffekseerHandle preview, Vector4 colour, Vector4 background)
    {
        using var target = new RenderTarget2D(device,64,64,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(background), 1, 0);
        var particle = new EffekseerParticle { Transform=Matrix.Identity,Colour=colour,UV=new(0,0,1,1),ParticleSize=1 };
        var view=Matrix.CreateLookAt(new(0,0,10),Vector3.Zero,Vector3.Up); var projection=Matrix.CreateOrthographic(2,2,.1f,100);
        float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview,Values(view),Values(projection),[particle],1));
        device.SetRenderTarget(null);var pixels=new Vector4[64*64];target.GetData(pixels);return pixels[32*64+32];
    }
}
