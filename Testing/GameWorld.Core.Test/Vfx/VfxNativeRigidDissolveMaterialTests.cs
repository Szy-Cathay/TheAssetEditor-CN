#nullable enable
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeRigidDissolveMaterialTests
{
    [Test]
    public void CompleteOfficialMaterial_MatchesItsOriginalNativeFloatOffsetsAndAllTenTextureBindings()
    {
        using var stream = typeof(VfxNativeRigidDissolveMaterialTests).Assembly.GetManifestResourceStream(
            "Test.GameWorld.Core.Vfx.Fixtures.NativeRigidDissolveMaterials.json")!;
        using var document = JsonDocument.Parse(stream);
        foreach (var fixture in document.RootElement.GetProperty("materials").EnumerateArray())
        {
            var original = Convert.FromBase64String(fixture.GetProperty("originalBase64").GetString()!);
            var expected = fixture.GetProperty("expectedNativeFloats").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var result = VfxScrollingMeshMaterial.Read(original);
            Assert.That(result.Parameters.Take(5), Is.EqualTo(expected.Take(5)));
            Assert.That(result.Parameters.Skip(7).Take(39), Is.EqualTo(expected.Skip(5)));
            var textures = fixture.GetProperty("logicalTextures").EnumerateArray().Select(value => value.GetString()).ToArray();
            var actual = result.Parameters[59] == 1 ? result.TexturePaths
                : Enumerable.Range(0, 10).Select(index => result.TexturePaths[(int)result.Parameters[48+index]]).ToArray();
            Assert.That(actual, Is.EqualTo(textures));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo(fixture.GetProperty("sha256").GetString()));
        }
    }

    [Test]
    public void Native176ByteLayout_PreservesAllOffsetsAndSourceWithoutInventingAnAuthoredOffset()
    {
        var root = Material(); var index = 0;
        foreach (var field in root.Elements("params").Elements("param"))
        {
            var count = field.Element("value")!.Value.Split(',').Length;
            field.Element("value")!.Value = string.Join(',', Enumerable.Range(index, count).Select(value => value + .25f));
            index += count;
        }
        var before = root.ToString();
        var result = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(before));
        Assert.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(44));
            Assert.That(result.Parameters.Take(5), Is.EqualTo(Enumerable.Range(0, 5).Select(value => value + .25f)));
            Assert.That(result.Parameters.Skip(5).Take(2), Is.EqualTo(new[] { 0f, 0f }));
            Assert.That(result.Parameters.Skip(7).Take(39), Is.EqualTo(Enumerable.Range(5, 39).Select(value => value + .25f)));
            Assert.That(result.Parameters[58], Is.EqualTo(1));
            Assert.That(result.Parameters[59], Is.Zero);
            Assert.That(root.ToString(), Is.EqualTo(before));
        });
    }

    [Test]
    public void PackedMasks_LeaveTheVariantOutsideBothAtlasRectanglesAndAliases()
    {
        var root = Material(); var index = 0;
        foreach (var source in root.Descendants("source")) source.Value = $"unique-{index++}.dds";
        var parsed = VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString()));
        Assert.That(parsed.TexturePaths, Has.Length.EqualTo(10));
        Assert.That(parsed.Parameters[58], Is.EqualTo(1));
        Assert.That(parsed.Parameters[59], Is.EqualTo(1));
    }

    [Test]
    public void AnUnprovedOneShotOffset_IsNotSilentlyAcceptedByTheNative176ByteFamily()
    {
        var root = Material();
        root.Element("params")!.Add(new XElement("param", new XElement("name", "disintegrate_1_offset"), new XElement("value", "2,3")));
        Assert.Throws<VfxMappingException>(() => VfxNativeRigidDissolveMaterial.Read(root));
    }

    [TestCase(.1f)]
    [TestCase(.4f)]
    [TestCase(.9f)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void NativeGBufferIntrinsicAndDither_MatchIndependentOfficialPsEquations(float threshold)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            byte[][] samples = [[51,102,153,255], [0,0,153,255], [204,38,19,224], [26,51,77,255], [102,77,51,179], [204,77,51,102]];
            foreach (var sample in samples) EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(handle, sample, 4, 1, 1, 0));
            var parameters = new float[60];
            void Put(int start, params float[] values) => values.CopyTo(parameters, start);
            Put(0,.7f,.8f,.9f,0,0,0,0,1,1,0,0,1,1,.3f,.2f,.1f,.4f,.7f,1,0,0,0,0,0,1,1,.6f,0,1,0,0,0,0,1,1,1.5f,1.1f,.9f,.7f,0,0,1,1,0,1,threshold);
            Put(48,0,1,2,3,4,4,5,5,2,2,1,0);
            EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(handle, [0,1,2,3,4,5], 6, parameters, parameters.Length));
            var vertices = new[] { new Vector3(-1,-1,0), new(1,-1,0), new(-1,1,0), new(1,1,0) }
                .Select(position => new EffekseerVertex { Position = position, Normal = Vector3.Backward, Tangent = Vector3.Right,
                    Binormal = Vector3.Up, UV = new(.375f,.625f), Colour = Vector4.One }).ToArray();
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, vertices, 4, [0,1,2,2,1,3], 6));
            var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0; layer.TextureIndex = 0; layer.OpaqueModel = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1));
            var particle = new EffekseerParticle { Layer = 0, Transform = Matrix.Identity, Colour = new(.8f,1.2f,.6f,.371f), UV = new(0,0,1,1), NormalRotation = new(0,0,0,1) };
            var eye = new Vector3(0,0,10); var view = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up); var projection = Matrix.CreateOrthographic(2,2,.1f,100);
            using var target = new RenderTarget2D(device, 32,32,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view), Values(projection), [particle],1));
            device.SetRenderTarget(null); var pixels = new Vector4[1024]; target.GetData(pixels);
            var field = .6 * .8 - threshold;
            var edge = field < 0 ? 0 : Math.Max(1 - Math.Sqrt(field) * 3.333333, 0);
            var coverage = .8 * (Math.Ceiling(Math.Clamp(field, 0, 1)) + edge);
            var expected = Vector4.UnitW;
            if (coverage - 26 / 255.0 >= 0)
            {
                // Native t2.yxzw -> destination Y reads noise.R; global also reads R, emissive mask reads A.
                var emission = (102 / 255.0 * .7) * (77 / 255.0 * .6) * .4;
                var rgb = new double[3];
                for (var channel = 0; channel < 3; channel++)
                {
                    var mask = Math.Max(Math.Ceiling(.8 * (coverage - new[] { 26,51,77 }[channel] / 255.0)),0);
                    var glow = mask * (emission * 1.5 * new[] { 1.1,.9,.7 }[channel] * new[] { .8,1.2,.6 }[channel]
                        + edge * new[] { .3,.2,.1 }[channel] * .4);
                    var intrinsic = 1 - (1 - new[] { .2,.4,.6 }[channel] * new[] { .7,.8,.9 }[channel]) * (1 - glow);
                    rgb[channel] = Math.Clamp(intrinsic + Math.Min(emission * Math.Ceiling(.8 * (coverage - 26 / 255.0)),1) * (glow-intrinsic),0,1);
                }
                expected = new((float)rgb[0],(float)rgb[1],(float)rgb[2],1);
            }
            Assert.That(Vector4.Distance(pixels[16*32+16], expected), Is.LessThan(.00002f));
        }
        finally { graphics.Release(); }
    }

    internal static XElement Material()
    {
        var root = VfxRigidDissolveMaterialTests.Material();
        root.Element("shader")!.Value = "shaders/wh3_vfx_rigid_emissive_dissolve.xml.shader";
        root.Elements("params").Elements("param").Single(field => field.Element("name")!.Value == "disintegrate_1_offset").Remove();
        return root;
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
        m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
