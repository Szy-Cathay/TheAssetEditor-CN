#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxCrossInstanceDistortionTests
{
    private const string TexturePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private const int Size = 128;
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private (byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels) _image;
    private Vector4 _maskUv, _emissionUv;
    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Projection = Matrix.CreateOrthographic(20, 20, .1f, 50);

    [OneTimeSetUp]
    public void LoadOriginalOfficialDiffuseMaskWithoutChangingItsPixels()
    {
        _official.LoadCurrentOfficialResourcesOnly();
        var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bytes = (byte[]?)read.Invoke(_official, [TexturePath]) ?? throw new FileNotFoundException(TexturePath);
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[], int, int, bool, int))decode.Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
        var selected = Enumerable.Range(0, _image.Width * _image.Height)
            .MaxBy(pixel => _image.Pixels[pixel * 4 + 3]);
        var alpha = _image.Pixels[selected * 4 + 3];
        var linearAlpha = alpha / 255f <= .04045f ? alpha / 255f / 12.92f : MathF.Pow((alpha / 255f + .055f) / 1.055f, 2.4f);
        Assert.That(linearAlpha * 2, Is.GreaterThan(1), "Original texel with particle alpha2 gives saturated coverage.");
        Assert.That(_image.MipLevels, Is.GreaterThan(1));
        _maskUv = new((selected % _image.Width + .5f) / _image.Width,
            (selected / _image.Width + .5f) / _image.Height, 0, 0);
        var emission = Enumerable.Range(0, _image.Width * _image.Height)
            .MaxBy(pixel => _image.Pixels[pixel * 4] - _image.Pixels[pixel * 4 + 3]);
        Assert.That(_image.Pixels[emission * 4], Is.EqualTo(255));
        Assert.That(_image.Pixels[emission * 4 + 3], Is.Zero);
        _emissionUv = new((emission % _image.Width + .5f) / _image.Width,
            (emission / _image.Width + .5f) / _image.Height, 0, 0);
        var origins = typeof(VfxOfficialDistortionPixelTests).GetField("_origins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_official);
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new
        {
            Boundary = "DIAG original full DDS mask and mip chain, constant UV only. Opposite independent shading bases preserve identical quad corners. Untextured checker is scene geometry, not a VFX texture. Not complete-effect or native visual acceptance.",
            TexturePath, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), _image.Width, _image.Height,
            _image.MipLevels, _image.Srgb, Texel = new { X = selected % _image.Width, Y = selected / _image.Width,
                Rgba = _image.Pixels.Skip(selected * 4).Take(4).Select(value => (int)value).ToArray() }, Origins = origins,
        }));
    }

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(4, false)]
    [TestCase(4, true)]
    public void OppositeDirectionsAcrossHandles_CancelInOneSharedResolve(int samples, bool reverse)
    {
        WithDevice(device =>
        {
            using var first = Preview(device);
            using var second = Preview(device);
            var positive = Particle(new(.6f, 0, .8f, 1));
            var negative = Particle(new(-.6f, 0, .8f, 1));
            var pair = reverse ? new[] { negative, positive } : new[] { positive, negative };
            var background = Render(device, first, second, [], samples, DrawMode.Combined);
            var alone = Render(device, first, second, [positive], samples, DrawMode.Combined);
            var oppositeAlone = Render(device, first, second, [negative], samples, DrawMode.Combined);
            var combined = Render(device, first, second, pair, samples, DrawMode.Combined);
            var sequential = Render(device, first, second, pair, samples, DrawMode.Independent);
            var shared = Render(device, first, second, pair, samples, DrawMode.Shared);
            Assert.Multiple(() =>
            {
                Assert.That(ChangedPixels(background, alone), Is.GreaterThan(20), "Positive basis must visibly refract the official-mask diagnostic.");
                Assert.That(ChangedPixels(background, oppositeAlone), Is.GreaterThan(20), "Negative basis must also be active.");
                Assert.That(MaximumDifference(background, combined), Is.LessThanOrEqualTo(1), "Single-handle weighted reference must cancel.");
                Assert.That(ChangedPixels(background, sequential), Is.GreaterThan(20), "The prior per-handle resolve must reproduce the defect.");
                Assert.That(MaximumDifference(combined, shared), Is.LessThanOrEqualTo(1),
                    "All Effect vectors and transmittance must accumulate before one background sample and resolve.");
            });
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnequalOverlappingHandles_MatchSingleHandleWeightsAndOrder(bool reverse)
    {
        WithDevice(device =>
        {
            using var first = Preview(device);
            using var second = Preview(device);
            var a = Particle(new(.6f, .2f, .77f, 1));
            var b = Particle(new(-.3f, .5f, .81f, 1));
            a.Transform *= Matrix.CreateTranslation(-1, 0, 0);
            b.Transform *= Matrix.CreateTranslation(1, 0, 0); b.Colour.W = .7f;
            var pair = reverse ? new[] { b, a } : new[] { a, b };
            var background = Render(device, first, second, [], 0, DrawMode.Combined);
            var combined = Render(device, first, second, pair, 0, DrawMode.Combined);
            var shared = Render(device, first, second, pair, 0, DrawMode.Shared);
            Assert.That(ChangedPixels(background, combined), Is.GreaterThan(20));
            Assert.That(MaximumDifference(combined, shared), Is.LessThanOrEqualTo(1),
                "Different bases, coverage, partial overlap and submission order must preserve native weighted gathering.");
        });
    }

    [Test]
    public void LaterHandleColour_IsIncludedBeforeEarlierHandleRefraction()
    {
        WithDevice(device =>
        {
            using var first = Preview(device);
            using var second = Preview(device);
            var refractor = Particle(new(.6f, 0, .8f, 1));
            var cover = Particle(Vector4.Zero); cover.Layer = 1; cover.Colour = new(1, .1f, .05f, 2);
            cover.Transform = Matrix.CreateScale(5);
            var combined = Render(device, first, second, [refractor, cover], 0, DrawMode.Combined);
            var sequential = Render(device, first, second, [refractor, cover], 0, DrawMode.Independent);
            var shared = Render(device, first, second, [refractor, cover], 0, DrawMode.Shared);
            Assert.That(ChangedPixels(combined, sequential), Is.GreaterThan(20), "Control must detect copying the background before later colour draws.");
            Assert.That(MaximumDifference(combined, shared), Is.LessThanOrEqualTo(1),
                "Scene colour is captured after every Effect's colour pass, rather than when the first distortion is submitted.");
        });
    }

    [Test]
    public void DeferredGather_CopiesSubmittedParticleDataBeforeReturning()
    {
        WithDevice(device =>
        {
            using var first = Preview(device);
            using var second = Preview(device);
            var pair = new[] { Particle(new(.6f, 0, .8f, 1)), Particle(new(-.6f, 0, .8f, 1)) };
            var combined = Render(device, first, second, pair, 0, DrawMode.Combined);
            var shared = Render(device, first, second, pair, 0, DrawMode.Shared, mutateSubmission: true);
            Assert.That(MaximumDifference(combined, shared), Is.LessThanOrEqualTo(1),
                "P/Invoke arrays are temporary; EndScene must consume an owned snapshot, never retained managed pointers.");
        });
    }

    [Test]
    public void QueuedPreviewAndIndependentDepth_SurviveCallerDisposalBeforeResolve()
    {
        WithDevice(device =>
        {
            using var owner = Preview(device);
            using var submitted = Preview(device, softDepth: true);
            using var depth = new SoftDepthSurface((SharpDX.Direct3D11.Device)device.Handle, 10.5f);
            var depthPointer = depth.View.NativePointer;
            var particle = Particle(new(.6f, 0, .8f, 1));
            var background = Render(device, owner, submitted, [], 0, DrawMode.Combined);
            var unsoftened = Render(device, submitted, owner, [particle], 0, DrawMode.Combined);
            var expected = Render(device, submitted, owner, [particle], 0, DrawMode.Combined, softDepth: depthPointer);
            var shared = Render(device, owner, submitted, [particle], 0, DrawMode.Shared, softDepth: depthPointer,
                useSecondForFirstSubmission: true, afterSubmission: () => { submitted.Dispose(); depth.Dispose(); });
            Assert.Multiple(() =>
            {
                Assert.That(ChangedPixels(background, expected), Is.GreaterThan(20), "Retained command must remain visibly active.");
                Assert.That(ChangedPixels(unsoftened, expected), Is.GreaterThan(20), "The independent half-fade depth must affect real pixels.");
                Assert.That(MaximumDifference(expected, shared), Is.LessThanOrEqualTo(1),
                    "Native queue must retain both preview resources and the original independent scene-depth view through EndScene.");
            });
        });
    }

    [TestCase("layer")]
    [TestCase("crush-point-nan")]
    [TestCase("crush-mode-three")]
    public void RejectedPacket_PreservesValidCommandsOnBothSides(string invalidField)
    {
        WithDevice(device =>
        {
            using var first = Preview(device);
            using var second = Preview(device);
            var a = Particle(new(.6f, 0, .8f, 1));
            var b = Particle(new(.2f, .5f, .84f, 1)); b.Transform *= Matrix.CreateTranslation(1, 0, 0);
            var pair = new[] { a, b };
            var background = Render(device, first, second, [], 0, DrawMode.Combined);
            var firstOnly = Render(device, first, second, [a], 0, DrawMode.Combined);
            var expected = Render(device, first, second, pair, 0, DrawMode.Combined);
            var shared = Render(device, first, second, pair, 0, DrawMode.Shared, rejectMalformed: invalidField);
            Assert.That(ChangedPixels(background, shared), Is.GreaterThan(20), "A rejected Effect must not erase valid queued refraction.");
            Assert.That(ChangedPixels(firstOnly, expected), Is.GreaterThan(20), "The valid command after the rejected packet must contribute too.");
            Assert.That(MaximumDifference(expected, shared), Is.LessThanOrEqualTo(1));
        });
    }

    [Test]
    public void DisposingUnendedSelfQueuedOwner_CancelsWithoutDisplayingOrKeepingItsTargetBound()
    {
        WithDevice(device =>
        {
            using var owner = Preview(device);
            var background = Render(device, owner, owner, [], 0, DrawMode.Combined);
            using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24,
                0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            typeof(VfxOfficialDistortionPixelTests).GetMethod("DrawBackground", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [device, (byte)255, false]);
            var boundTarget = NativeTarget(device);
            Assert.That(boundTarget, Is.Not.EqualTo(nint.Zero));
            var lighting = new EffekseerLighting { Display = new(1, 1, .05f, 0), Bloom = new(4, 0, 0, 0) };
            EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(owner, in lighting));
            var particle = Particle(new(.6f, 0, .8f, 1));
            EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(owner.DangerousGetHandle(), owner,
                Values(View), Values(Projection), [particle], 1, in lighting));
            owner.Dispose();
            Assert.That(NativeTarget(device), Is.EqualTo(boundTarget), "Destroy must cancel an unended scene and restore the actual D3D11 target.");
            device.SetRenderTarget(null);
            var pixels = new Color[Size * Size]; target.GetData(pixels);
            Assert.That(MaximumDifference(background, pixels), Is.Zero,
                "Cancellation discards pending colour/refraction and never runs the final display pass.");
        });
    }

    [Test]
    public void AllOpaqueModels_PrecedeOtherHandlesFrontTransparentParticles()
    {
        WithDevice(device =>
        {
            using var first = Preview(device, opaqueModel: true);
            using var second = Preview(device, opaqueModel: true);
            var front = Particle(Vector4.Zero); front.Layer = 3; front.UV = _emissionUv;
            front.Colour = new(0, 0, 10, 1); front.Transform *= Matrix.CreateTranslation(0, 0, 2);
            var opaque = Particle(Vector4.Zero); opaque.Layer = 2; opaque.UV = new(0, 0, 1, 1);
            var expected = Render(device, first, second, [opaque, front], 0, DrawMode.Combined);
            var sequential = Render(device, first, second, [front, opaque], 0, DrawMode.Independent);
            var shared = Render(device, first, second, [front, opaque], 0, DrawMode.Shared);
            Assert.That(ChangedPixels(expected, sequential), Is.GreaterThan(20), "Prior handle order overwrites a front transparent particle with a later opaque model.");
            Assert.That(MaximumDifference(expected, shared), Is.LessThanOrEqualTo(1),
                "Every opaque receiver must establish colour/depth before any handle draws front transparent particles.");
        });
    }

    private enum DrawMode { Combined, Independent, Shared }

    private EffekseerHandle Preview(GraphicsDevice device, bool softDepth = false, bool opaqueModel = false)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        Assert.That(handle.IsInvalid, Is.False);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, _image.Pixels, _image.Pixels.Length,
            _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.MipLevels));
        var distortion = EffekseerLayer.Create();
        distortion.TextureIndex = -1; distortion.DistortionMaskIndex = 0; distortion.Distortion = 3; distortion.ZTest = 0;
        if (softDepth) { distortion.DistortionSoftDepth = 1; distortion.SoftDistance = 1; distortion.DistortionSoftScaleStrength = 0; }
        var colour = EffekseerLayer.Create(); colour.ZTest = 0; colour.MaterialMode = EffekseerLayer.PreciseCoverageMode;
        if (opaqueModel)
        {
            // Diagnostic geometry only; all shading samples still use the original official DDS.
            EffekseerVertex Vertex(float x, float y) => new() { Position = new(x, y, 0), Normal = Vector3.Backward,
                Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(_maskUv.X, _maskUv.Y), Colour = Vector4.One };
            var vertices = new[] { Vertex(-.5f, -.5f), Vertex(.5f, -.5f), Vertex(-.5f, .5f), Vertex(.5f, .5f) };
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(handle, vertices, 4, [0, 1, 2, 2, 1, 3], 6));
            var opaque = EffekseerLayer.Create(); opaque.Kind = 1; opaque.ModelIndex = 0; opaque.OpaqueModel = 1; opaque.Blend = 0;
            var front = EffekseerLayer.Create(); front.Blend = 2; front.MaterialMode = EffekseerLayer.PreciseEmissionMode;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [distortion, colour, opaque, front], 4));
        }
        else EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [distortion, colour], 2));
        return handle;
    }

    private Color[] Render(GraphicsDevice device, EffekseerHandle first, EffekseerHandle second,
        EffekseerParticle[] particles, int samples, DrawMode mode, bool mutateSubmission = false,
        nint softDepth = 0, bool useSecondForFirstSubmission = false, Action? afterSubmission = null, string? rejectMalformed = null)
    {
        using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24,
            samples, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        typeof(VfxOfficialDistortionPixelTests).GetMethod("DrawBackground", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [device, (byte)255, false]);
        var lighting = new EffekseerLighting { Display = new(1, 1, .05f, 0), Bloom = new(4, 0, 0, 0) };
        if (mode == DrawMode.Shared) EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(first, in lighting));
        try
        {
            if (mode == DrawMode.Combined)
            {
                if (particles.Length > 0)
                    EffekseerNative.Check(EffekseerNative.AecnVfxDraw(first, Values(View), Values(Projection), particles, particles.Length, in lighting, softDepth));
            }
            else
                for (var i = 0; i < particles.Length; ++i)
                {
                    var handle = i == 0 && !useSecondForFirstSubmission ? first : second;
                    var submitted = new[] { particles[i] };
                    if (mode == DrawMode.Shared)
                        EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), handle,
                            Values(View), Values(Projection), submitted, 1, in lighting, softDepth));
                    else
                        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(View), Values(Projection), submitted, 1, in lighting, softDepth));
                    if (mutateSubmission) submitted[0] = new EffekseerParticle { Transform = Matrix.CreateTranslation(10000, 0, 0) };
                    if (i == 0 && rejectMalformed != null)
                    {
                        var rejected = particles[0];
                        if (rejectMalformed == "layer") rejected.Layer = -1;
                        else if (rejectMalformed == "crush-point-nan") rejected.AlphaCrush = new(float.NaN, 1);
                        else if (rejectMalformed == "crush-mode-three") rejected.AlphaCrush = new(0, 3);
                        else throw new ArgumentOutOfRangeException(nameof(rejectMalformed));
                        Assert.That(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), second,
                            Values(View), Values(Projection), [rejected], 1, in lighting, softDepth), Is.Zero);
                    }
                }
            afterSubmission?.Invoke();
        }
        finally { if (mode == DrawMode.Shared) EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(first)); }
        device.SetRenderTarget(null);
        var pixels = new Color[Size * Size]; target.GetData(pixels); return pixels;
    }

    private EffekseerParticle Particle(Vector4 basis) => new()
    {
        Transform = Matrix.CreateScale(10), NormalRotation = basis, Colour = new(1, 1, 1, 2),
        UV = _maskUv, ParticleSize = 10,
    };
    private static void WithDevice(Action<GraphicsDevice> action)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try { action(graphics.GraphicsDevice); }
        finally { graphics.Release(); }
    }
    private static nint NativeTarget(GraphicsDevice device)
    {
        var targets = ((SharpDX.Direct3D11.Device)device.Handle).ImmediateContext.OutputMerger.GetRenderTargets(1, out var depth);
        try { return targets[0]?.NativePointer ?? nint.Zero; }
        finally { foreach (var target in targets) target?.Dispose(); depth?.Dispose(); }
    }
    private sealed class SoftDepthSurface : IDisposable
    {
        private readonly SharpDX.Direct3D11.Texture2D _texture;
        private bool _disposed;
        internal SharpDX.Direct3D11.ShaderResourceView View { get; }
        internal SoftDepthSurface(SharpDX.Direct3D11.Device device, float distance)
        {
            var projected = Vector4.Transform(new Vector4(0, 0, -distance, 1), Projection);
            var values = Enumerable.Repeat(projected.Z / projected.W, Size * Size).ToArray();
            using var stream = new SharpDX.DataStream(values.Length * sizeof(float), true, true);
            stream.WriteRange(values); stream.Position = 0;
            _texture = new(device, new SharpDX.Direct3D11.Texture2DDescription
            {
                Width = Size, Height = Size, MipLevels = 1, ArraySize = 1, Format = SharpDX.DXGI.Format.R32_Float,
                SampleDescription = new(1, 0), Usage = SharpDX.Direct3D11.ResourceUsage.Immutable,
                BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
            }, new SharpDX.DataRectangle(stream.DataPointer, Size * sizeof(float)));
            View = new(device, _texture);
        }
        public void Dispose() { if (_disposed) return; _disposed = true; View.Dispose(); _texture.Dispose(); }
    }
    private static int ChangedPixels(Color[] a, Color[] b) => a.Zip(b).Count(pair => Difference(pair.First, pair.Second) > 2);
    private static int MaximumDifference(Color[] a, Color[] b) => a.Zip(b).Max(pair => Difference(pair.First, pair.Second));
    private static int Difference(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
