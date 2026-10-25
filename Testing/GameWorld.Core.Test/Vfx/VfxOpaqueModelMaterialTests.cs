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
public class VfxOpaqueModelMaterialTests
{
    [TestCase(true, 1f, 255)]
    [TestCase(false, 0f, 255)]
    [TestCase(false, 1f, 0)]
    public void OpaqueCharacterMaterial_PreservesItsOwnColourAndCoverage(bool red, float alpha, byte textureAlpha)
    {
        var source = Source(red ? new(1, 0, 0) : Vector3.One, alpha);
        var texture = Texture(Color.Lime, textureAlpha); var original = texture.ToArray();
        var pixels = Render(source, path => Resolve(path, texture));
        Assert.That(pixels.Count(p => p.G > 100 && p.R < 30 && p.B < 30), Is.GreaterThan(100));
        Assert.That(texture, Is.EqualTo(original), "An opaque upload must not rewrite shared source bytes.");
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void SharedZeroAlphaTexture_RemainsInvisibleOnOrdinarySprites()
    {
        var model = VfxDocument.Read(Source(Vector3.One, 1));
        var sprite = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var parent = VfxDocument.CreateComposition();
        var first = parent.AddReference("model", model); first.Apply(); first.Selection.SetAttributeValue("pos_x", -1.2f);
        var second = parent.AddReference("sprite", sprite); second.Apply(); second.Selection.SetAttributeValue("pos_x", .6f);
        var texture = Texture(Color.Lime, 0);
        var pixels = Render(parent.Write(), path => path.Replace('\\', '/') switch
        {
            "vfx/model.xml" => model.Write(), "vfx/sprite.xml" => sprite.Write(), _ => Resolve(path, texture),
        }, 4);
        var left = pixels.Where((_, index) => index % 128 < 64).Count(p => p.G > 100);
        var right = pixels.Where((_, index) => index % 128 >= 64).Count(p => p.G > 100);
        Assert.That(left, Is.GreaterThan(100));
        Assert.That(right, Is.Zero, "The ordinary sprite must retain the same DDS alpha.");
    }

    [Test]
    public void FrontTransparentSprite_CompositesOverTheOpaqueModelRegardlessOfSourceOrder()
    {
        var model = VfxDocument.Read(Source(Vector3.One, 1));
        var sprite = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var parent = VfxDocument.CreateComposition();
        var front = parent.AddReference("sprite", sprite); front.Apply();
        front.Selection.SetAttributeValue("pos_x", .5f); front.Selection.SetAttributeValue("pos_y", .5f); front.Selection.SetAttributeValue("pos_z", 2);
        var back = parent.AddReference("model", model); back.Apply(); back.Selection.SetAttributeValue("pos_z", 1);
        var pixels = Render(parent.Write(), path => path.Replace('\\', '/') switch
        {
            "vfx/model.xml" => model.Write(), "vfx/sprite.xml" => sprite.Write(),
            "vfx/texture.dds" => Texture(Color.Red, 128), _ => Resolve(path, Texture(Color.Lime, 255)),
        });
        Assert.That(pixels.Count(p => p.R > 60 && p.G > 60 && p.B < 30), Is.GreaterThan(100));
    }

    [TestCase("fixture_rigid_alpha_on.xml", "shaders/rigid_character.xml.shader")]
    [TestCase("fixture_rigid_alpha_off.xml", "shaders/unverified_character.xml.shader")]
    public void UnverifiedMaterial_RetainsTheExistingParticleAlphaPath(string name, string shader)
    {
        var pixels = Render(Source(Vector3.One, 0), path => Resolve(path, Texture(Color.Lime, 255), name, shader));
        Assert.That(pixels.Count(p => p.G > 100), Is.Zero);
    }

    internal static byte[] Source(Vector3 colour, float alpha)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/character.wsmodel");
        var quality = document.Root.Descendants("quality").Single();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_colour"), new XElement("property", new XAttribute("name", "curve_colour"), Curve(("r", colour.X), ("g", colour.Y), ("b", colour.Z), ("brightness", 1)))),
            new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"), new XElement("property", new XAttribute("name", "curve_alpha"), Curve(("alpha", alpha)))));
        return document.Write();
    }

    private static string Curve(params (string Axis, float Value)[] values) => string.Join('|', values.Select(v =>
        $"{v.Axis},0,0;0,{v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":" +
        string.Join('|', values.Select((v, i) => $"{v.Axis},0;{i},{i}"));

    internal static byte[]? Resolve(string path, byte[] texture, string name = "fixture_rigid_alpha_off.xml", string shader = "shaders/rigid_character.xml.shader") => path.Replace('\\', '/') switch
    {
        "vfx/character.wsmodel" => Encoding.UTF8.GetBytes("<model version=\"1\"><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/character.xml.material</material></materials></model>"),
        "vfx/character.xml.material" => Encoding.UTF8.GetBytes(new XElement("material", new XElement("name", name), new XElement("shader", shader),
            new XElement("textures", new XElement("texture", new XElement("slot", "t_xml_base_colour"), new XElement("source", "vfx/shared.dds")))).ToString()),
        "vfx/model-0.rigid_model_v2" => VfxModelVariantTests.Model(0),
        _ when path.EndsWith(".dds") => texture,
        _ => null,
    };

    private static byte[] Texture(Color colour, byte alpha)
    {
        var bytes = VfxModelVariantPixelTests.Dds(colour);
        for (var index = 131; index < bytes.Length; index += 4) bytes[index] = alpha;
        return bytes;
    }

    private static Color[] Render(byte[] source, Func<string, byte[]?> resolve, float width = 2)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device); preview.Reload(new(source, "vfx/opaque.xml", resolve));
            Assert.That(preview.EmitterCount, Is.GreaterThan(0), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
            preview.Seek(.2f, Matrix.Identity);
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Vector3(.5f, .5f, 10);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(camera, new(.5f, .5f, 0), Vector3.Up),
                Matrix.CreateOrthographic(width, 2, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            device.SetRenderTarget(null); var pixels = new Color[128 * 128]; target.GetData(pixels); return pixels;
        }
        finally { graphics.Release(); }
    }
}
