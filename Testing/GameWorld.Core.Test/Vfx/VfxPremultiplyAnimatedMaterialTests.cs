#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxPremultiplyAnimatedMaterialTests
{
    [Test]
    public void Material_PreservesAllSixBindingsAndTheAuthoredParameterLayout()
    {
        var source = VfxOpaqueModelMaterialTests.Source(Vector3.One, 1); var material = Material(Vector3.UnitY);
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/premultiply.xml", path => Resolve(path, material)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.MeshMaterials, Has.Count.EqualTo(1));
        Assert.That(conversion.MeshMaterials[0].TextureIndexes, Has.Length.EqualTo(6));
        Assert.That(conversion.MeshMaterials[0].Parameters, Has.Length.EqualTo(32));
        Assert.That(conversion.MeshMaterials[0].Parameters[13..16], Is.EqualTo(new[] { 0f, 1f, 0f }));
    }

    [TestCase(0, 1, 0)]
    [TestCase(1, 0, 0)]
    public void NativePixels_UseMaterialColourRatherThanOnlyTheFirstDiffuse(float r, float g, float b)
    {
        var pixels = Render(Material(new(r, g, b)));
        Assert.That(pixels.Count(p => r > 0 ? p.R > 100 && p.G < 15 && p.B < 15 : p.G > 100 && p.R < 15 && p.B < 15), Is.GreaterThan(100));
    }

    [Test]
    public void ZeroEmission_PreservesCoverageWithoutGeneratingDiffuseWhite()
    {
        var pixels = Render(Material(Vector3.One, emission: 0));
        Assert.That(pixels.Count(p => p.R < 10 && p.G < 10 && p.B < 10), Is.GreaterThan(100));
    }

    [Test]
    public void MaskRedChannel_ControlsOpacityIndependentlyOfOtherChannels()
    {
        var pixels = Render(Material(Vector3.One), texture: name => name == "mask2" ? Dds(new(0, 255, 255)) : Dds(Color.White));
        Assert.That(pixels.Count(p => p != Background), Is.Zero);
    }

    [Test]
    public void GlobalMaterialTime_ScrollsTheSecondDiffuseWithoutMovingGeometry()
    {
        var material = Material(Vector3.One, secondDirection: 1);
        var first = Render(material, time: 0, texture: Texture); var second = Render(material, time: .5f, texture: Texture);
        Assert.That(first.Zip(second).Count(p => p.First != p.Second), Is.GreaterThan(100));

        static byte[] Texture(string name)
        {
            var bytes = Dds(Color.White);
            if (name == "diffuse2") for (var y = 0; y < 4; y++) for (var x = 2; x < 4; x++)
            { var at = 128 + (y * 4 + x) * 4; bytes[at] = bytes[at + 1] = bytes[at + 2] = 0; }
            return bytes;
        }
    }

    [Test]
    public void MissingMask_IsNotIgnoredAsAnUnusedMaterialResource()
    {
        var material = Material(Vector3.One);
        var conversion = TotalWarVfxConverter.Convert(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/premultiply.xml",
            path => path.Replace('\\', '/') == "vfx/mask2.dds" ? null : Resolve(path, material)));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
    }

    [Test]
    public void UncalibratedMetallicReflection_RemainsExplicitlyUnsupported()
    {
        var material = Material(Vector3.One, metallic: .5f);
        var conversion = TotalWarVfxConverter.Convert(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/premultiply.xml", path => Resolve(path, material)));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True);
    }

    private static readonly Color Background = new(20, 23, 28);
    private static byte[] Dds(Color colour) => VfxModelVariantPixelTests.Dds(colour);
    private static byte[]? Resolve(string path, byte[] material, Func<string, byte[]>? texture = null) => path.Replace('\\', '/') switch
    {
        "vfx/character.xml.material" => material,
        _ when path.EndsWith(".dds") => texture?.Invoke(Path.GetFileNameWithoutExtension(path)) ?? Dds(Color.White),
        _ => VfxOpaqueModelMaterialTests.Resolve(path, Dds(Color.White)),
    };

    internal static byte[] Material(Vector3 colour, float emission = 1, float metallic = 0, float secondDirection = 0)
    {
        var names = new[] { "diffuse1", "diffuse2", "distortion", "global", "mask1", "mask2" };
        var slots = new[] { "t_xml_diffuse_01", "t_xml_diffuse_02", "t_xml_distortion_01", "t_xml_global_opacity", "t_xml_mask_01", "t_xml_mask_02" };
        (string Name, string Type, string Value)[] values =
        [
            ("Emission", "float", Number(emission)), ("depth_fade", "float", "0.3"),
            ("diffuse_direction_01", "float2", "0,0"), ("diffuse_direction_02", "float2", Number(secondDirection) + ",0"),
            ("diffuse_power", "float", "1"), ("diffuse_scale_01", "float2", "1,1"), ("diffuse_scale_02", "float2", "1,1"),
            ("distortion_strenght_01", "float", "0"), ("fog_strenght", "float", "0"),
            ("main_colour", "float3", string.Join(',', Number(colour.X), Number(colour.Y), Number(colour.Z))),
            ("mask_direction_01", "float2", "0,0"), ("mask_direction_02", "float2", "0,0"),
            ("mask_scale_01", "float2", "1,1"), ("mask_scale_02", "float2", "1,1"), ("mask_strenght", "float", "1"),
            ("metallic", "float", Number(metallic)), ("opacity_power", "float", "0"), ("opacity_strenght", "float", "1"), ("roughness", "float", "0"),
        ];
        return Encoding.UTF8.GetBytes(new XElement("material", new XElement("name", "fixture.xml"), new XElement("shader", "shaders/fx_premultiply_animated_uvs.xml.shader"),
            new XElement("textures", names.Select((name, i) => new XElement("texture", new XElement("slot", slots[i]), new XElement("source", "vfx/" + name + ".dds")))),
            new XElement("params", values.Select(v => new XElement("param", new XElement("name", v.Name), new XElement("type", v.Type), new XElement("value", v.Value))))).ToString());

        static string Number(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Color[] Render(byte[] material, float time = .2f, Func<string, byte[]>? texture = null)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1), "vfx/premultiply.xml", path => Resolve(path, material, texture)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
            preview.Seek(time, Matrix.Identity); using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Vector3(.5f, .5f, 10); device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Background, 1, 0);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(camera, new(.5f, .5f, 0), Vector3.Up), Matrix.CreateOrthographic(2, 2, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            device.SetRenderTarget(null); var pixels = new Color[128 * 128]; target.GetData(pixels); return pixels;
        }
        finally { graphics.Release(); }
    }
}
