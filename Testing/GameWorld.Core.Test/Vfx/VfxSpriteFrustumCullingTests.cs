#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Utility;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSpriteFrustumCullingTests
{
    private const int Width = 128, Height = 96;
    private const string OfficialDiffuse = @"vfx\textures\wh_campaign_misc_diffuse.dds";
    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) _image;
    private Vector4 _uv;
    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Projection = Matrix.CreateOrthographic(4, 3, .1f, 100);

    [OneTimeSetUp]
    public void LoadOnlyManifestVerifiedOriginalDiffuseForDiagnosticQuads()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        Assert.That(EffekseerNative.AecnVfxAbi(Marshal.SizeOf<EffekseerLayer>(), Marshal.SizeOf<EffekseerParticle>(), Marshal.SizeOf<EffekseerVertex>(), Marshal.SizeOf<EffekseerLighting>()), Is.EqualTo(1));
        var native = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(module => module.ModuleName.Equals("AssetEditor.CN.Effekseer.dll", StringComparison.OrdinalIgnoreCase));
        var nativeSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native.FileName)));
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_NATIVE_SHA256") is { Length: > 0 } expected)
            Assert.That(nativeSha, Is.EqualTo(expected).IgnoreCase, "The real loaded native DLL must match this run's frozen candidate.");
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { NativeModule = native.FileName, NativeSha256 = nativeSha }));
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data) || !File.Exists(Path.Combine(data, "manifest.txt")))
            Assert.Ignore("The original game data and official manifest are required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var packPath = Path.Combine(data!, "vfx.pack");
        Assert.That(new FileInfo(packPath).Length, Is.EqualTo(manifest["vfx.pack"]));
        var pack = new PackFileContainerLoader(null!).Load(packPath)!;
        var original = pack.FileList[OfficialDiffuse].DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo("3b5d9ba89822b85634e668b1b9c6cf0c76139408f13873d7423c69ab825090a3"));
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[], int, int, bool, int))decode.Invoke(null, [original, VfxTextureUsage.Diffuse])!;
        var selected = Enumerable.Range(0, _image.Width * _image.Height)
            .MaxBy(i => (_image.Pixels[i * 4] + _image.Pixels[i * 4 + 1] + _image.Pixels[i * 4 + 2]) * _image.Pixels[i * 4 + 3]);
        Assert.That(_image.Pixels[selected * 4 + 3], Is.GreaterThan(0));
        _uv = new((selected % _image.Width + .5f) / _image.Width, (selected / _image.Width + .5f) / _image.Height, 0, 0);
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new
        {
            Boundary = "DIAG quads and constant-UV geometry controls using the entire unchanged official DDS mip chain; this is not complete-effect/native visual acceptance.",
            OfficialDiffuse, Pack = packPath, Sha256 = Convert.ToHexStringLower(SHA256.HashData(original)), _image.Width, _image.Height, _image.MipLevels, SelectedOriginalTexel = selected,
        }));
    }

    [TestCase(-100, 0, 0)]
    [TestCase(100, 0, 0)]
    [TestCase(0, -100, 0)]
    [TestCase(0, 100, 0)]
    [TestCase(-100, 0, 4)]
    [TestCase(100, 0, 4)]
    [TestCase(0, -100, 4)]
    [TestCase(0, 100, 4)]
    public void DIAG_CompletelyOutsideXYBatchPreservesBackgroundWithoutNativeDraw(int x, int y, int samples)
    {
        WithDevice(device =>
        {
            var layer = Layer(); using var handle = Preview(device, layer);
            var background = Render(device, handle, [], samples);
            var actual = Render(device, handle, [Particle(Matrix.CreateScale(2) * Matrix.CreateTranslation(x, y, 0))], samples);
            Assert.That(actual.Pixels, Is.EqualTo(background.Pixels), "XY rejection must preserve every background channel.");
            Assert.That(actual.DrawCalls, Is.Zero, "A wholly clipped DIAG batch must avoid native sprite submission.");
        });
    }

    [Test]
    public void DIAG_CentralQuadRemainsVisibleAndSubmitted()
    {
        WithDevice(device =>
        {
            using var handle = Preview(device, Layer()); var background = Render(device, handle, []);
            var actual = Render(device, handle, [Particle(Matrix.CreateScale(2))]);
            Assert.Multiple(() =>
            {
                Assert.That(actual.DrawCalls, Is.GreaterThan(0));
                Assert.That(actual.Pixels.Zip(background.Pixels).Count(p => p.First != p.Second), Is.GreaterThan(100));
            });
        });
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void DIAG_OffCentrePivotReflectedCameraAndUvRotationKeepCrossingEdgePixels(bool reflected, int uvRotation)
    {
        WithDevice(device =>
        {
            var actualLayer = Layer(); actualLayer.CameraFacing = 1; actualLayer.UvRotation = uvRotation; actualLayer.Pivot = new(.25f, .75f);
            var referenceLayer = actualLayer; referenceLayer.Pivot = new(.5f);
            using var actualHandle = Preview(device, actualLayer); using var referenceHandle = Preview(device, referenceLayer);
            var projection = Projection * Matrix.CreateScale(reflected ? -1 : 1, 1, 1);
            var sign = reflected ? -1 : 1;
            var actualParticle = Particle(Matrix.CreateScale(2, 1.5f, 1) * Matrix.CreateTranslation(2 - sign * .5f, 0, 0));
            var referenceParticle = Particle(Matrix.CreateScale(2, 1.5f, 1) * Matrix.CreateTranslation(2, -.375f, 0));
            // A known nonzero original texel keeps every rotated edge control visible; the atlas has authored empty regions.
            actualParticle.UV = referenceParticle.UV = _uv;
            var background = Render(device, actualHandle, [], projection: projection);
            var actual = Render(device, actualHandle, [actualParticle], projection: projection);
            var reference = Render(device, referenceHandle, [referenceParticle], projection: projection);
            Assert.Multiple(() =>
            {
                Assert.That(actual.DrawCalls, Is.GreaterThan(0), "The centre is outside, but the actual reflected/pivoted corners intersect the viewport.");
                Assert.That(actual.Pixels, Is.EqualTo(reference.Pixels), "Independent binary-exact pivot compensation must preserve all sampled RGBA.");
                Assert.That(actual.Pixels.Zip(background.Pixels).Count(p => p.First != p.Second), Is.GreaterThan(20));
            });
        });
    }

    [Test]
    public void DIAG_MixedBatchKeepsVisibleParticleAndOriginalSharedColourQuantisation()
    {
        WithDevice(device =>
        {
            using var handle = Preview(device, Layer());
            var visible = Particle(Matrix.CreateScale(2)); visible.Colour = new(.23f, .57f, .91f, 1);
            var outside = Particle(Matrix.CreateScale(2) * Matrix.CreateTranslation(100, 0, 0)); outside.Colour = new(4, 3, 2, 1);
            var transparent = outside; transparent.Transform = Matrix.CreateScale(2); transparent.Colour.W = 0;
            // Both batches retain brightness4. The in-view alpha0 control contributes no fragment colour.
            var reference = Render(device, handle, [transparent, visible]);
            var actual = Render(device, handle, [outside, visible]);
            Assert.Multiple(() =>
            {
                Assert.That(actual.DrawCalls, Is.GreaterThan(0));
                Assert.That(actual.Pixels, Is.EqualTo(reference.Pixels), "Do not compact individual offscreen particles and change the retained batch's byte colour normalisation.");
                Assert.That(actual.Pixels.Any(p => p.Z > .4f), Is.True, "The visible DIAG quad must actually contribute.");
            });
        });
    }

    [Test]
    public void DIAG_NearPlaneCrossingRetainsTheOriginalNativeSubmission()
    {
        WithDevice(device =>
        {
            using var handle = Preview(device, Layer());
            var particle = Particle(Matrix.CreateScale(4) * Matrix.CreateRotationY(1.4f) * Matrix.CreateTranslation(0, 0, -.05f));
            var actual = Render(device, handle, [particle], view: Matrix.Identity,
                projection: Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, Width / (float)Height, .1f, 100));
            Assert.That(actual.DrawCalls, Is.GreaterThan(0), "A quad crossing w=0 or the near plane must keep the established native clipping path.");
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void DIAG_OffscreenInvalidTransformStillReportsOriginalValidationError(int badIndex)
    {
        WithDevice(device =>
        {
            using var handle = Preview(device, Layer());
            var particles = new[] { Particle(Matrix.CreateTranslation(100, 0, 0)), Particle(Matrix.CreateTranslation(101, 0, 0)) };
            particles[badIndex].Transform.M14 = float.NaN;
            var error = Assert.Throws<InvalidOperationException>(() => Render(device, handle, particles));
            Assert.That(error!.Message, Does.Contain("Invalid particle transform"), "Even the otherwise unused matrix column must retain the original validation.");
        });
    }

    private static EffekseerLayer Layer()
    {
        var layer = EffekseerLayer.Create(); layer.ZTest = 0; layer.MaterialMode = 1; return layer;
    }
    private EffekseerHandle Preview(GraphicsDevice device, EffekseerLayer layer)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, _image.Pixels, _image.Pixels.Length, _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.MipLevels));
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer], 1)); return handle;
    }
    private EffekseerParticle Particle(Matrix transform) => new() { Transform = transform, Colour = Vector4.One, UV = _uv };
    private sealed record Result(Vector4[] Pixels, int DrawCalls);
    private static Result Render(GraphicsDevice device, EffekseerHandle handle, EffekseerParticle[] particles, int samples = 0, Matrix? view = null, Matrix? projection = null)
    {
        using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.Vector4, DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        try
        {
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(20, 40, 60, 100), 1, 0);
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(view ?? View), Values(projection ?? Projection), particles, particles.Length));
            var calls = EffekseerNative.AecnVfxDrawCalls(handle); device.SetRenderTarget(null);
            var pixels = new Vector4[Width * Height]; target.GetData(pixels); return new(pixels, calls);
        }
        finally { device.SetRenderTarget(null); }
    }
    private static void WithDevice(Action<GraphicsDevice> action)
    {
        var graphics = new GraphicsDeviceServiceMock(); try { action(graphics.GraphicsDevice); } finally { graphics.Release(); }
    }
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
