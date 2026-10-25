#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSolidMeshMaterialTests
{
    [Test]
    public void SolidMaterial_PreservesBindingsAndWritesOpaqueDepth()
    {
        var conversion = Convert(Material());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.MeshMaterials.Single().Parameters, Has.Length.EqualTo(56));
        Assert.That(conversion.MeshMaterials.Single().TextureIndexes, Has.Length.EqualTo(6));
        Assert.That(conversion.Emitters.Single().Render.OpaqueModel, Is.EqualTo(1));
    }

    [Test]
    public void SolidDissolve_RetainsParticleAlphaAboveOneUntilTextureSampling()
    {
        var conversion = Convert(Material(), alpha: 3);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var camera = Camera();
        Assert.That(simulation.Frame(camera).Single().Colour.W, Is.EqualTo(3).Within(.0001));
    }

    [TestCase(0f, false)]
    [TestCase(.49f, false)]
    [TestCase(1f, true)]
    [TestCase(3f, true)]
    public void NativePixels_ClipMasksRatherThanBlendingAnOpaqueRectangle(float alpha, bool visible)
    {
        var pixels = Render(Material(), Vector3.UnitX, alpha);
        var red = pixels.Count(p => p.R > 100 && p.G < 15 && p.B < 15);
        Assert.That(red, visible ? Is.GreaterThan(100) : Is.Zero);
        if (!visible) Assert.That(pixels.Count(p => p != Background), Is.Zero);
    }

    [Test]
    public void NativePixels_UseTheSelectedMaskChannelAndMaterialColour()
    {
        var material = Material();
        var pixels = Render(material, Vector3.One, 1, name => name == "a" ? Dds(new(0, 255, 255)) : Dds(Color.White));
        Assert.That(pixels.Count(p => p != Background), Is.Zero);
        var green = Render(Material(glow: "0,1,0"), Vector3.One, 1);
        Assert.That(green.Count(p => p.G > 100 && p.R < 15 && p.B < 15), Is.GreaterThan(100));
    }

    [Test]
    public void NativePixels_UseGlobalTimeForThePanningMask()
    {
        var material = Material(pan: "1,0");
        var first = Render(material, Vector3.One, 1, Texture, 0);
        var second = Render(material, Vector3.One, 1, Texture, .5f);
        Assert.That(first.Zip(second).Count(p => p.First != p.Second), Is.GreaterThan(100));

        static byte[] Texture(string name)
        {
            var bytes = Dds(Color.White);
            if (name == "a") for (var y = 0; y < 4; y++) for (var x = 2; x < 4; x++)
            { var at = 128 + (y * 4 + x) * 4; bytes[at] = bytes[at + 1] = bytes[at + 2] = 0; }
            return bytes;
        }
    }

    [TestCase("time", "0")]
    [TestCase("t_xml_dither_texture", "vfx/noise.dds")]
    [TestCase("t_xml_material_map", "vfx/material_map.dds")]
    public void UnverifiedMaterialBranches_RemainExplicitlyUnsupported(string field, string value)
    {
        var material = XElement.Parse(Encoding.UTF8.GetString(Material()));
        var param = material.Descendants("param").SingleOrDefault(p => p.Element("name")?.Value == field);
        if (param != null) param.Element("value")!.Value = value;
        else material.Descendants("texture").Single(t => t.Element("slot")?.Value == field).Element("source")!.Value = value;
        var conversion = Convert(Encoding.UTF8.GetBytes(material.ToString()));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True);
    }

    [Test]
    public void OverriddenDefaultDither_IsValidatedByPixelsRatherThanOnlyItsName()
    {
        var conversion = Convert(Material(), texture: name => name == "default_white" ? Dds(Color.Black) : Dds(Color.White));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True);
    }

    [Test]
    public void InvalidDitherImage_SkipsTheLayerWithADiagnostic()
    {
        var conversion = Convert(Material(), texture: name => name == "default_white" ? [1, 2, 3] : Dds(Color.White));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True);
    }

    [Test]
    public void NativePixels_ClippedHolesDoNotOccludeTheBackgroundParticle()
    {
        var model = VfxDocument.Read(VfxOpaqueModelMaterialTests.Source(Vector3.UnitY, 1));
        var sprite = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var parent = VfxDocument.CreateComposition(); var front = parent.AddReference("front", model); front.Apply();
        front.Selection.SetAttributeValue("pos_z", 1);
        var back = parent.AddReference("back", sprite); back.Apply(); back.Selection.SetAttributeValue("pos_x", .5f); back.Selection.SetAttributeValue("pos_y", .5f);
        var pixels = RenderScene(parent.Write(), path => path.Replace('\\', '/') switch
        {
            "vfx/front.xml" => model.Write(), "vfx/back.xml" => sprite.Write(), "vfx/texture.dds" => Dds(Color.Blue),
            _ => Resolve(path, Material(), Texture),
        }, .2f);
        Assert.That(pixels.Count(p => p.G > 100 && p.R < 15 && p.B < 15), Is.GreaterThan(100));
        Assert.That(pixels.Count(p => p.B > 100 && p.R < 15 && p.G < 15), Is.GreaterThan(100));

        static byte[] Texture(string name)
        {
            var bytes = Dds(Color.White);
            if (name == "a") for (var y = 0; y < 4; y++) for (var x = 2; x < 4; x++)
            { var at = 128 + (y * 4 + x) * 4; bytes[at] = bytes[at + 1] = bytes[at + 2] = 0; }
            return bytes;
        }
    }

    internal static byte[] Material(string glow = "1,1,1", string pan = "0,0")
    {
        (string Name, string Value)[] values =
        [
            ("colour_overlay_pan", "0,0"), ("colour_overlay_strength", "1"), ("colour_overlay_tile", "1,1"),
            ("depth_fade_switch", "0"), ("global_mask_pan", "0,0"), ("global_mask_tiling", "1,1"),
            ("glow_colour", glow), ("glow_strength", "1"), ("normal_pan", "0,0"), ("normal_tiling", "1,1"),
            ("pixel_depth_fade", "1"), ("texture_a_brightness", "1"), ("texture_a_channels", "1,0,0,0"),
            ("texture_a_pan", pan), ("texture_a_tiling", "1,1"), ("texture_b_channels", "1,0,0,0"),
            ("texture_b_pan", "0,0"), ("texture_b_tiling", "1,1"), ("texture_c_brightness", "1"),
            ("texture_c_channels", "1,0,0,0"), ("texture_c_pan", "0,0"), ("texture_c_tiling", "1,1"),
            ("time", "1"), ("total_alpha_strength", "2"), ("vertex_alpha_contrast", "1"), ("vertex_colour", "0"),
        ];
        var slots = new[] { "texture_a", "texture_b", "texture_c", "colour_overlay", "global_mask", "normal", "dither_texture", "material_map" };
        var names = new[] { "a", "b", "c", "overlay", "global", "normal", "default_white", "default_black" };
        return Encoding.UTF8.GetBytes(new XElement("material", new XElement("name", "solid.xml"),
            new XElement("shader", "shaders/vfx_mesh_solid.xml.shader"),
            new XElement("textures", slots.Select((slot, i) => new XElement("texture", new XElement("slot", "t_xml_" + slot),
                new XElement("source", (i >= 6 ? "commontextures/" : "vfx/") + names[i] + ".dds")))),
            new XElement("params", values.Select(v => new XElement("param", new XElement("name", v.Name),
                new XElement("type", v.Value.Contains(',') ? "float" + v.Value.Split(',').Length : "float"), new XElement("value", v.Value))))).ToString());
    }

    private static readonly Color Background = new(20, 23, 28);
    private static byte[] Dds(Color colour) => VfxModelVariantPixelTests.Dds(colour);
    private static byte[]? Resolve(string path, byte[] material, Func<string, byte[]>? texture) => path.Replace('\\', '/') switch
    {
        "vfx/character.xml.material" => material,
        "commontextures/default_black.dds" => Dds(Color.Black),
        _ when path.EndsWith(".dds") => texture?.Invoke(Path.GetFileNameWithoutExtension(path)) ?? Dds(Color.White),
        _ => VfxOpaqueModelMaterialTests.Resolve(path, Dds(Color.White)),
    };
    private static VfxConversion Convert(byte[] material, float alpha = 1, Func<string, byte[]>? texture = null) =>
        TotalWarVfxConverter.Convert(new(VfxOpaqueModelMaterialTests.Source(Vector3.One, alpha), "vfx/solid.xml", path => Resolve(path, material, texture)));
    private static CommonShaderParameters Camera()
    {
        var position = new Vector3(.5f, .5f, 10);
        return new(Matrix.CreateLookAt(position, new(.5f, .5f, 0), Vector3.Up), Matrix.CreateOrthographic(2, 2, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
    }
    private static Color[] Render(byte[] material, Vector3 colour, float alpha, Func<string, byte[]>? texture = null, float time = .2f)
        => RenderScene(VfxOpaqueModelMaterialTests.Source(colour, alpha), path => Resolve(path, material, texture), time);
    private static Color[] RenderScene(byte[] source, Func<string, byte[]?> resolve, float time)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(source, "vfx/solid.xml", resolve));
            Assert.That(preview.EmitterCount, Is.GreaterThan(0), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
            preview.Seek(time, Matrix.Identity); using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Background, 1, 0); preview.Draw(Camera());
            device.SetRenderTarget(null); var pixels = new Color[128 * 128]; target.GetData(pixels); return pixels;
        }
        finally { graphics.Release(); }
    }
}
