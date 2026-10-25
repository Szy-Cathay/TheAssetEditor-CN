#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxRigidBasicDitherTests
{
    [Test]
    public void SourceMaterial_KeepsTheSixNativeFloatsFourBindingsAndPadding()
    {
        var root = Material(); var original = Encoding.UTF8.GetBytes(root.ToString());
        var material = VfxScrollingMeshMaterial.Read(original);
        Assert.Multiple(() =>
        {
            Assert.That(material.Parameters, Is.EqualTo(new float[] { .2f, -.3f, .4f, -.5f, 2, 3, 0, 0 }));
            Assert.That(material.TexturePaths, Is.EqualTo(new[] { "base_colour.dds", "dissolve.dds", "material_map.dds", "normal.dds" }));
            Assert.That(Encoding.UTF8.GetBytes(root.ToString()), Is.EqualTo(original));
        });
    }

    [TestCase("dissolve_offset")]
    [TestCase("dissolve_scroll")]
    [TestCase("dissolve_tile")]
    public void MissingNativeParameter_IsRejected(string name)
    {
        var root = Material(); root.Descendants("param").Single(p => p.Element("name")!.Value == name).Remove();
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(error!.Field, Is.EqualTo(name));
    }

    [TestCase("dissolve")]
    [TestCase("normal")]
    public void MissingAuthoredTexture_IsNotReplacedByTheBaseColour(string name)
    {
        var root = Material(); root.Descendants("texture").Single(p => p.Element("slot")!.Value == "t_xml_" + name).Remove();
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(error!.Field, Is.EqualTo("t_xml_" + name));
    }

    [Test]
    public void DuplicateDissolveParameter_IsRejectedInsteadOfChoosingOne()
    {
        var root = Material(); root.Element("params")!.Add(new XElement(root.Descendants("param").First()));
        var error = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(error!.Field, Is.EqualTo("dissolve_offset"));
    }

    [Test]
    public void ModelConversion_UsesTheExplicitDitherMaterialAndOpaqueDepth()
    {
        var bytes = Source(); var conversion = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(conversion.Models, Has.Count.EqualTo(1));
            Assert.That(conversion.MeshMaterials, Has.Count.EqualTo(1));
            Assert.That(conversion.MeshMaterials.Single().Parameters, Has.Length.EqualTo(8));
            Assert.That(conversion.Models.Single().OpaqueMaterial, Is.True);
            Assert.That(conversion.Emitters.Single().Render.MeshMaterialIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(conversion.Emitters.Single().Render.OpaqueModel, Is.EqualTo(1));
            Assert.That(conversion.TexturePaths, Has.Count.EqualTo(4));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.ModelMaterialFallback"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void RawParticleAlpha_ReachesTheDedicatedMaterialWithoutTheOrdinaryBlendClamp()
    {
        var source = VfxDocument.Read(Source()); var emitter = source.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialAlpha"),
            "x_min,0,0;0,2.5,linear,linear,0,0,0,0,1:x,0;0,0"));
        var conversion = Convert(source.Write());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        Assert.That(simulation.Frame(camera).Single().Colour.W, Is.EqualTo(2.5f));
    }

    internal static XElement Material()
    {
        (string Name, string Value)[] fields = [("dissolve_offset", ".2,-.3"), ("dissolve_scroll", ".4,-.5"), ("dissolve_tile", "2,3")];
        return new XElement("material", new XElement("name", "diagnostic-rigid-dither"),
            new XElement("shader", "shaders/vfx_rigid_basic_dither.xml.shader"),
            new XElement("textures", new[] { "base_colour", "dissolve", "material_map", "normal" }.Select(name =>
                new XElement("texture", new XElement("slot", "t_xml_" + name), new XElement("source", name + ".dds")))),
            new XElement("params", fields.Select(p => new XElement("param", new XElement("name", p.Name),
                new XElement("type", "float2"), new XElement("value", p.Value)))));
    }

    private static byte[] Source()
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/rigid-dither.wsmodel");
        return document.Write();
    }

    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "diagnostic/rigid-dither.xml", path => path switch
    {
        @"vfx\rigid-dither.wsmodel" => Encoding.UTF8.GetBytes("<model><geometry>vfx/model-0.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"0\">vfx/rigid-dither.xml.material</material></materials></model>"),
        @"vfx\rigid-dither.xml.material" => Encoding.UTF8.GetBytes(Material().ToString()),
        _ => VfxModelVariantTests.Resolve(path),
    }));
}


[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxRigidBasicDitherPixelTests
{
    private const int Width = 64;
    private const float Tolerance = .00001f;
    private static readonly Vector4 Background = new Color(64, 128, 192).ToVector4();
    private static readonly byte[] Colour = [32, 96, 160, 0];
    private static readonly float[] DefaultParameters = [0, 0, 0, 0, 1, 1, 0, 0];

    [SetUp]
    public void RequireExplicitOffscreenOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Native offscreen rendering is opt-in; no real editor window or desktop input.");
    }

    [Test]
    public void BlackRgbAndZeroTextureAlpha_StillDrawOpaqueBlackGeometry()
    {
        WithDevice(device =>
        {
            var pixels = Render(device, [0, 0, 0, 0], SolidNoise(255), 1, 1, 1, Vector3.One);
            Near(pixels[Centre], new(0, 0, 0, 1));
            Assert.That(pixels.Count(p => p.X == 0 && p.Y == 0 && p.Z == 0), Is.GreaterThan(500));
        });
    }

    [Test]
    public void BaseTextureAlpha_IsNotUsedAsModelCoverage()
    {
        WithDevice(device =>
        {
            var expected = Render(device, [32, 96, 160, 255], SolidNoise(255), 1, 1, .5f, Vector3.One);
            foreach (var alpha in new byte[] { 0, 64, 128 })
            {
                var actual = Render(device, [32, 96, 160, alpha], SolidNoise(255), 1, 1, .5f, Vector3.One);
                Assert.That(actual.Zip(expected).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThanOrEqualTo(Tolerance));
            }
            Near(expected[Centre], BaseColour());
        });
    }

    [Test]
    public void ParticleRgb_DoesNotTintTheSourceGbufferBaseColour()
    {
        WithDevice(device =>
        {
            foreach (var colour in new[] { new Vector3(.1f, 2, .3f), Vector3.Zero, new Vector3(4, .2f, 1) })
                Near(Render(device, Colour, SolidNoise(255), 1, 1, .5f, colour)[Centre], BaseColour());
        });
    }

    [Test]
    public void Dissolve_UsesTheRedChannelAsAHardCutout()
    {
        WithDevice(device =>
        {
            Near(Render(device, Colour, [0, 255, 255, 255], 1, 1, .5f, Vector3.One)[Centre], Background);
            Near(Render(device, Colour, [255, 0, 0, 0], 1, 1, .5f, Vector3.One)[Centre], BaseColour());
        });
    }

    [Test]
    public void NativeByteAlphaPacking_ChangesTheHalfOpacityDissolveBoundary()
    {
        WithDevice(device =>
        {
            const float alpha = .5f, u = .49975f;
            // A linear 0..1 two-texel ramp samples to .4995 at this constant UV.
            var noise = Ramp(); var sampled = SampleNoise(noise, 2, 1, new(u, .5f));
            var byteAlpha = MathF.Floor(alpha * 255 + .5f);
            var nativeAlpha = MathF.Floor(byteAlpha / 255 * 1000) * .001f;
            Assert.That(sampled, Is.GreaterThan(1 - nativeAlpha));
            Assert.That(sampled, Is.LessThan(1 - MathF.Floor(alpha * 1000) * .001f),
                "The independent input must distinguish byte-alpha packing from the old raw-alpha hypothesis.");
            Near(Render(device, Colour, noise, 2, 1, alpha, Vector3.One, uv: new(u, .5f))[Centre], BaseColour());
        });
    }

    [Test]
    public void Dissolve_UsesImplicitMipSamplingInsteadOfForcingMipZero()
    {
        WithDevice(device =>
        {
            Vector4[] RenderMipChain(bool whiteSecondMip)
            {
                var direct = (SharpDX.Direct3D11.Device)device.Handle;
                using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
                Upload(preview, Colour, 1, 1);
                const int noiseWidth = 128, nextMipWidth = 64;
                var pixels = new byte[(noiseWidth * noiseWidth + nextMipWidth * nextMipWidth) * 4];
                // Only mip 1 carries coverage. A forced mip-0 sample would discard every fragment.
                if (whiteSecondMip) Array.Fill(pixels, (byte)255, noiseWidth * noiseWidth * 4, nextMipWidth * nextMipWidth * 4);
                EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, pixels, pixels.Length,
                    noiseWidth, noiseWidth, 0, 2));
                Upload(preview, [0, 128, 0, 255], 1, 1); Upload(preview, [0, 128, 0, 128], 1, 1);
                EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0, 1, 2, 3], 4, DefaultParameters, 8));
                var vertices = Quad(Vector2.Zero);
                vertices[1].UV = Vector2.UnitX; vertices[2].UV = Vector2.UnitY; vertices[3].UV = Vector2.One;
                EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0, 1, 2, 2, 1, 3], 6));
                EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [Layer(0)], 1));
                // A 128-texel UV span across the 64-pixel quad selects mip 1 by derivatives.
                return Draw(device, preview, [Particle(.5f, Vector3.One, Matrix.Identity)], false);
            }
            Near(RenderMipChain(false)[Centre], Background);
            Near(RenderMipChain(true)[Centre], BaseColour());
        });
    }

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void DissolveUv_UsesOffsetThenGlobalScrollThenTile(float materialTime)
    {
        WithDevice(device =>
        {
            float[] parameters = [.05f, .1f, .2f, -.15f, 2, 3, 0, 0];
            byte[] noise = [0, 0, 0, 255, 64, 0, 0, 255, 192, 0, 0, 255, 255, 0, 0, 255];
            var uv = new Vector2(.1f, .2f);
            var sampledUv = (uv + new Vector2(.05f, .1f) + materialTime * new Vector2(.2f, -.15f)) * new Vector2(2, 3);
            var expected = SampleNoise(noise, 2, 2, sampledUv) >= 1 - .501f ? BaseColour() : Background;
            var actual = Render(device, Colour, noise, 2, 2, .5f, Vector3.One, materialTime, 37, parameters, uv);
            Near(actual[Centre], expected);
            var differentParticleAge = Render(device, Colour, noise, 2, 2, .5f, Vector3.One, materialTime, .03f, parameters, uv);
            Assert.That(actual.Zip(differentParticleAge).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThanOrEqualTo(Tolerance),
                "Particle age must not replace the original global material clock.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OpaqueMaterial_CullsTheBackFaceUnderBothProjectionSigns(bool reflected)
    {
        WithDevice(device =>
        {
            var front = Render(device, Colour, SolidNoise(255), 1, 1, 1, Vector3.One, reflected: reflected);
            var back = Render(device, Colour, SolidNoise(255), 1, 1, 1, Vector3.One, reflected: reflected,
                transform: Matrix.CreateRotationY(MathHelper.Pi));
            Near(front[Centre], BaseColour());
            Near(back[Centre], Background);
        });
    }

    [Test]
    public void OpaqueMaterial_WritesDepthBeforeLaterModels()
    {
        WithDevice(device =>
        {
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            Upload(preview, [0, 0, 0, 0], 1, 1); Upload(preview, SolidNoise(255), 1, 1);
            Upload(preview, [0, 128, 0, 255], 1, 1); Upload(preview, [0, 128, 0, 128], 1, 1);
            Upload(preview, [0, 255, 0, 0], 1, 1);
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0, 1, 2, 3], 4, DefaultParameters, 8));
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [4, 1, 2, 3], 4, DefaultParameters, 8));
            var vertices = Quad(new(.5f));
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0, 1, 2, 2, 1, 3], 6));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [Layer(0), Layer(1)], 2));
            var front = Particle(1, Vector3.One, Matrix.Identity);
            var back = Particle(1, Vector3.One, Matrix.CreateTranslation(0, 0, -1)); back.Layer = 1;
            var backOnly = Draw(device, preview, [back], false);
            Near(backOnly[Centre], new(0, 1, 0, 1));
            Near(Draw(device, preview, [front, back], false)[Centre], new(0, 0, 0, 1));
        });
    }

    private static int Centre => Width / 2 * Width + Width / 2;
    private static Vector4 BaseColour() => new(32f / 255, 96f / 255, 160f / 255, 1);
    private static byte[] SolidNoise(byte r) => [r, r, r, 255];
    private static byte[] Ramp() => [0, 0, 0, 255, 255, 0, 0, 255];
    private static void WithDevice(Action<GraphicsDevice> action)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try { action(graphics.GraphicsDevice); }
        finally { graphics.Release(); }
    }
    private static EffekseerLayer Layer(int material)
    {
        var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0;
        layer.MeshMaterialIndex = material; layer.OpaqueModel = 1;
        return layer;
    }
    private static EffekseerParticle Particle(float alpha, Vector3 rgb, Matrix transform) => new()
    {
        Transform = transform, Colour = new(rgb, alpha), UV = new(0, 0, 1, 1), ParticleSize = 1,
    };
    private static void Upload(EffekseerHandle preview, byte[] pixels, int width, int height) =>
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, pixels, pixels.Length, width, height, 0));
    private static Vector4[] Render(GraphicsDevice device, byte[] baseColour, byte[] noise, int noiseWidth, int noiseHeight,
        float alpha, Vector3 rgb, float materialTime = 0, float age = 0, float[]? parameters = null, Vector2? uv = null,
        bool reflected = false, Matrix? transform = null)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        Upload(preview, baseColour, 1, 1); Upload(preview, noise, noiseWidth, noiseHeight);
        Upload(preview, [0, 128, 0, 255], 1, 1); Upload(preview, [0, 128, 0, 128], 1, 1);
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0, 1, 2, 3], 4, parameters ?? DefaultParameters, 8));
        var vertices = Quad(uv ?? new Vector2(.5f));
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, 4, [0, 1, 2, 2, 1, 3], 6));
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [Layer(0)], 1));
        var particle = Particle(alpha, rgb, transform ?? Matrix.Identity); particle.Time = age; particle.MaterialTime = materialTime;
        return Draw(device, preview, [particle], reflected);
    }
    private static EffekseerVertex[] Quad(Vector2 uv) =>
    [
        new(){Position=new(-1,-1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=uv,Colour=Vector4.One},
        new(){Position=new(1,-1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=uv,Colour=Vector4.One},
        new(){Position=new(-1,1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=uv,Colour=Vector4.One},
        new(){Position=new(1,1,0),Normal=Vector3.Backward,Tangent=Vector3.Right,Binormal=Vector3.Up,UV=uv,Colour=Vector4.One},
    ];
    private static Vector4[] Draw(GraphicsDevice device, EffekseerHandle preview, EffekseerParticle[] particles, bool reflected)
    {
        using var target = new RenderTarget2D(device, Width, Width, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(Background), 1, 0);
        var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreateOrthographic(2, 2, .1f, 100) * Matrix.CreateScale(reflected ? -1 : 1, 1, 1);
        float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), particles, particles.Length));
        device.SetRenderTarget(null); var result = new Vector4[Width * Width]; target.GetData(result); return result;
    }
    private static float SampleNoise(byte[] pixels, int width, int height, Vector2 uv)
    {
        var x = uv.X * width - .5f; var y = uv.Y * height - .5f;
        var left = (int)MathF.Floor(x); var top = (int)MathF.Floor(y);
        var fx = x - left; var fy = y - top;
        float At(int column, int row) => pixels[(((row % height + height) % height) * width + (column % width + width) % width) * 4] / 255f;
        return MathHelper.Lerp(MathHelper.Lerp(At(left, top), At(left + 1, top), fx),
            MathHelper.Lerp(At(left, top + 1), At(left + 1, top + 1), fx), fy);
    }
    private static void Near(Vector4 actual, Vector4 expected) =>
        Assert.That(Vector4.Distance(actual, expected), Is.LessThanOrEqualTo(Tolerance));
}
