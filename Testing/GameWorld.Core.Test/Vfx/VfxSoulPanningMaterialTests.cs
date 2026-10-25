#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSoulPanningMaterialTests
{
    // Field order is the shipped 68-byte structured material buffer, before layout padding.
    private static readonly (string Name, int Count)[] Fields =
    [
        ("depth_fade_distance", 1), ("emissive_strength", 1), ("fresnel_bias", 1), ("fresnel_power", 1),
        ("is_world_space", 1), ("uv_noise1panningdirection", 2), ("uv_noise1tiling", 2),
        ("uv_noise2panningdirection", 2), ("uv_noise2tiling", 2), ("uv_panningdirection", 2), ("uv_tiling", 2),
    ];

    [TestCase("shaders/vfx_soulpanning.xml.shader")]
    [TestCase("shaders/vfx_soulpanning_w2.xml.shader")]
    [TestCase("shaders/vfx_soulpanning_w4.xml.shader")]
    public void NativeMaterial_PreservesAllFourNumericInputsAndThePackedParameters(string shader)
    {
        var source = Material(shader); var bytes = Encoding.UTF8.GetBytes(source.ToString());
        var actual = VfxScrollingMeshMaterial.Read(bytes);
        Assert.That(actual.TexturePaths, Is.EqualTo(new[] { "base.dds", "noise1.dds", "noise2.dds", "opacity.dds" }));
        Assert.That(actual.Parameters, Has.Length.EqualTo(28));
        Assert.That(actual.Parameters.Take(17), Is.EqualTo(Parameters().Take(17)));
        Assert.That(actual.UsesVertexColour, Is.False, "The source PS uses instance colour, not mesh vertex colour.");
        Assert.That(source.ToString(), Is.EqualTo(Encoding.UTF8.GetString(bytes)));
    }

    [Test]
    public void MissingNoiseTexture_IsDiagnosedInsteadOfSilentlyUsingOnlyTheBaseMap()
    {
        var material = Material(); material.Descendants("texture").Single(t => t.Element("slot")!.Value == "t_xml_noise_tex_02").Remove();
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(material.ToString())));
        Assert.That(error!.Field, Is.EqualTo("t_xml_noise_tex_02"));
    }

    [Test]
    public void BooleanWorldSpace_UsesItsExplicitStoredValue()
    {
        var material = Material(); Set(material, "is_world_space", "true");
        var actual = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(material.ToString()));
        Assert.That(actual.Parameters[4], Is.EqualTo(1));
    }

    [TestCase(-12, 10)]
    [TestCase(-5, 10)]
    [TestCase(-1, 10)]
    [TestCase(-2, -1)]
    [TestCase(-5, 2)]
    public void SignedStrengthAndBias_PreserveTheCombinationsUsedByOriginalMaterials(float strength, float bias)
    {
        var material = Material(); Set(material, "emissive_strength", strength.ToString(CultureInfo.InvariantCulture));
        Set(material, "fresnel_bias", bias.ToString(CultureInfo.InvariantCulture));
        var actual = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(material.ToString()));
        Assert.That(actual.Parameters[1], Is.EqualTo(strength)); Assert.That(actual.Parameters[2], Is.EqualTo(bias));
    }

    [TestCase(-2, -1)]
    [TestCase(-5, 2)]
    [TestCase(-12, 10)]
    public void Gpu_SignedStrengthAndBiasRetainTheirProductInTheFloatingScene(float strength, float bias)
    {
        var parameters = Parameters(); parameters[1] = strength; parameters[2] = bias;
        var textures = Enumerable.Range(0, 4).Select(_ => Uniform(255, 255, 255)).ToArray();
        var pixel = Render(parameters, textures, Vector4.One, 0);
        var ndv = 10 / MathF.Sqrt(100 + 2 * .015625f * .015625f);
        Assert.That(pixel.X, Is.EqualTo(strength * (1 - bias) * ndv).Within(.0005f));
    }

    [Test]
    public void Converter_UsesTheDedicatedMaterialWithoutChangingSourceOrTextureUsage()
    {
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        source.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/soul.wsmodel");
        var bytes = source.Write(); var original = bytes.ToArray();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/soul.xml", path => path.Replace('\\', '/') switch
        {
            "vfx/soul.wsmodel" => Encoding.UTF8.GetBytes("<model version=\"1\"><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/soul.xml.material</material></materials></model>"),
            "vfx/soul.xml.material" => Encoding.UTF8.GetBytes(Material().ToString()),
            "vfx/model-0.rigid_model_v2" => VfxModelVariantTests.Model(0),
            _ => VfxModelVariantPixelTests.Dds(Color.White),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.MeshMaterials, Has.Count.EqualTo(1));
        Assert.That(conversion.MeshMaterials[0].TextureIndexes, Has.Length.EqualTo(4));
        Assert.That(conversion.TextureUsages, Is.All.EqualTo(VfxTextureUsage.MaterialValue));
        Assert.That(bytes, Is.EqualTo(original));
    }

    [TestCase(SurfaceFormat.Vector4, .25f)]
    [TestCase(SurfaceFormat.Vector4, .8f)]
    [TestCase(SurfaceFormat.Color, .25f)]
    [TestCase(SurfaceFormat.Color, .8f)]
    public void Gpu_CombinesSquareRootAlphaBothNoiseMapsGradientFresnelAndRawParticleTint(SurfaceFormat format, float alpha)
    {
        var textures = new[] { Uniform(64, 128, 192, 128), Uniform(128, 64, 192), Uniform(192, 128, 64), Uniform(200, 150, 100) };
        var parameters = Parameters(); var tint = new Vector4(.5f, .75f, 1, alpha); var background = new Color(26, 51, 77).ToVector4();
        var actual = Render(parameters, textures, tint, .4f, format: format, background: background);
        var ndv = 10 / MathF.Sqrt(100 + 2 * .015625f * .015625f);
        var response = (1 - .1f) * ndv;
        var expected = new Vector3(64, 128, 192) / 255 * MathF.Sqrt(128f / 255)
            * (new Vector3(128, 64, 192) + new Vector3(192, 128, 64)) / (2 * 255)
            * new Vector3(200, 150, 100) / 255 * 4 * new Vector3(tint.X, tint.Y, tint.Z)
            * (MathF.Floor(alpha * 1000) / 1000) * response;
        expected += new Vector3(background.X, background.Y, background.Z) * .99999f;
        Assert.That(Vector3.Distance(new(actual.X, actual.Y, actual.Z), expected), Is.LessThan(format == SurfaceFormat.Vector4 ? .0002f : .009f));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Gpu_RetainsNativeSingleSidedFacesWithEitherProjection(bool reversedWinding, bool reflected)
    {
        var parameters = Parameters(); parameters[1] = 1; parameters[2] = 0;
        var textures = Enumerable.Range(0, 4).Select(_ => Uniform(255, 255, 255)).ToArray();
        var pixel = Render(parameters, textures, Vector4.One, 0, reversedWinding, reflected);
        Assert.That(pixel.X, reversedWinding ? Is.GreaterThan(.9f) : Is.LessThan(.001f), "Native geometry controls show only the reversed winding, with the same normal.");
    }

    [Test]
    public void Gpu_SecondPanningParameterMovesTheFirstNoiseTextureAsInTheShippedShader()
    {
        var parameters = Parameters(); parameters[1] = 1; parameters[2] = 0; parameters[9] = .25f;
        var textures = new[] { Uniform(255, 255, 255), Stripe(), Uniform(0, 0, 0), Uniform(255, 255, 255) };
        var first = Render(parameters, textures, Vector4.One, 0, pixelX: 16);
        var next = Render(parameters, textures, Vector4.One, 1, pixelX: 16);
        var repeated = Render(parameters, textures, Vector4.One, 4, pixelX: 16);
        Assert.That(first.X, Is.GreaterThan(.49f));
        // Pixel 16 has u=16.5/64; a quarter-UV pan linearly samples 46.875% of the red texel.
        Assert.That(next.X / first.X, Is.EqualTo(.46875f).Within(.0005f));
        Assert.That(Vector4.Distance(first, repeated), Is.LessThan(.0005f));
    }

    [Test]
    public void Gpu_OpacityGradientStaysOnOriginalMeshUvWhenTheBaseTexturePans()
    {
        var parameters = Parameters(); parameters[13] = .25f;
        var textures = new[] { Uniform(255, 255, 255), Uniform(255, 255, 255), Uniform(255, 255, 255), Stripe() };
        var first = Render(parameters, textures, Vector4.One, 0, pixelX: 16);
        var next = Render(parameters, textures, Vector4.One, 1, pixelX: 16);
        Assert.That(first.X, Is.GreaterThan(.1f));
        Assert.That(Vector4.Distance(first, next), Is.LessThan(.0005f));
    }

    private static XElement Material(string shader = "shaders/vfx_soulpanning.xml.shader")
    {
        var values = Parameters(); var offset = 0;
        return new XElement("material", new XElement("shader", shader),
            new XElement("textures", new[] { ("base_colour", "base.dds"), ("noise_tex_01", "noise1.dds"), ("noise_tex_02", "noise2.dds"), ("opacity_grad", "opacity.dds") }
                .Select(t => new XElement("texture", new XElement("slot", "t_xml_" + t.Item1), new XElement("source", t.Item2)))),
            new XElement("params", Fields.Select(field =>
            {
                var text = field.Name == "is_world_space" ? "false" : string.Join(',', values.Skip(offset).Take(field.Count).Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                offset += field.Count;
                return new XElement("param", new XElement("name", field.Name), new XElement("type", field.Name == "is_world_space" ? "bool" : field.Count == 2 ? "float2" : "float"), new XElement("value", text));
            })));
    }

    private static float[] Parameters()
    {
        var values = new float[28]; values[1] = 4; values[2] = .1f; values[3] = 1;
        foreach (var index in new[] { 7, 8, 11, 12, 15, 16 }) values[index] = 1;
        return values;
    }

    private static void Set(XElement material, string name, string value) => material.Descendants("param").Single(p => p.Element("name")!.Value == name).Element("value")!.Value = value;
    private static byte[] Uniform(byte r, byte g, byte b, byte a = 255) => [r, g, b, a];
    private static byte[] Stripe() => Enumerable.Range(0, 4).SelectMany(_ => new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 }).ToArray();

    private static Vector4 Render(float[] parameters, byte[][] textures, Vector4 tint, float time, bool reversed = true, bool reflected = true,
        SurfaceFormat format = SurfaceFormat.Vector4, Vector4 background = default, int pixelX = 32)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            foreach (var bytes in textures) EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, bytes, bytes.Length, bytes.Length == 4 ? 1 : 4, bytes.Length == 4 ? 1 : 4, 0));
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0, 1, 2, 3], 4, parameters, parameters.Length));
            EffekseerVertex Vertex(float x, float y) => new() { Position = new(x, y, 0), Normal = Vector3.Forward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new((x + 1) / 2, (y + 1) / 2), Colour = Vector4.One };
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, [Vertex(-1, -1), Vertex(1, -1), Vertex(-1, 1), Vertex(1, 1)], 4, reversed ? [0, 2, 1, 2, 3, 1] : [0, 1, 2, 2, 1, 3], 6));
            var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            using var target = new RenderTarget2D(device, 64, 64, false, format, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(background), 1, 0);
            var view = Matrix.CreateLookAt(new(0, 0, -10), Vector3.Zero, Vector3.Up); var projection = Matrix.CreateOrthographic(2, 2, .1f, 100);
            if (reflected) projection *= Matrix.CreateScale(-1, 1, 1);
            float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
            var particle = new EffekseerParticle { Transform = Matrix.Identity, Colour = tint, UV = new(0, 0, 1, 1), MaterialTime = time };
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1));
            device.SetRenderTarget(null);
            if (format == SurfaceFormat.Color) { var pixels = new Color[4096]; target.GetData(pixels); return pixels[32 * 64 + pixelX].ToVector4(); }
            var floats = new Vector4[4096]; target.GetData(floats); return floats[32 * 64 + pixelX];
        }
        finally { graphics.Release(); }
    }
}
