#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxIndependentSoftDepthTests
{
    private const string TexturePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private (byte[] Pixels, int Width, int Height, bool Srgb, int Mips) _image;
    private string _sourceHash = "";
    private VfxOfficialDistortionPixelTests _official = null!;

    [OneTimeSetUp]
    public void LoadOnlyTheOriginalOfficialDiffuse()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        _official = new VfxOfficialDistortionPixelTests();
        _official.LoadCurrentOfficialResourcesOnly();
        var bytes = ReadOfficial(TexturePath) ?? throw new FileNotFoundException(TexturePath);
        _sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[], int, int, bool, int))decode.Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
    }

    [Test]
    public void OriginalEffects_KeepNativeSoftDepthOnTheAuthoredOrdinarySpriteBranch()
    {
        const string gyroPath = @"vfx\wh_main_machine_gyrocopter_rotor.xml";
        var gyroBytes = ReadOfficial(gyroPath) ?? throw new FileNotFoundException(gyroPath);
        foreach (var quality in new[] { "0", "1" })
        {
            var conversion = TotalWarVfxConverter.Convert(new(gyroBytes, gyroPath, ReadOfficial, quality));
            var main = conversion.Emitters.Single(emitter => emitter.Name == "rotor_trails");
            var opposite = conversion.Emitters.Single(emitter => emitter.Name == "rotor_trails_opposite");
            Assert.Multiple(() =>
            {
                Assert.That(main.Render.NativeSoftDepth, Is.Zero, "The original main layer has no ps_soft_edge modifier.");
                Assert.That(opposite.Render.Distortion, Is.Zero);
                Assert.That(opposite.Render.NativeSoftDepth, Is.EqualTo(1));
                Assert.That(opposite.Render.SoftDistance, Is.EqualTo(.3f).Within(.000001f));
                Assert.That(opposite.Render.DistortionSoftScaleStrength, Is.EqualTo(1));
            });
            TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Source = gyroPath, Quality = quality,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(gyroBytes)), Main = main.Render.NativeSoftDepth,
                Opposite = opposite.Render.NativeSoftDepth, opposite.Render.SoftDistance, opposite.Render.DistortionSoftScaleStrength }));
        }
        const string modelPath = @"vfx\wh3_main_entity_storm_dragon_attack_impact.xml";
        var modelBytes = ReadOfficial(modelPath) ?? throw new FileNotFoundException(modelPath);
        var models = TotalWarVfxConverter.Convert(new(modelBytes, modelPath, ReadOfficial));
        var protectedLayers = models.Emitters.Where(emitter => emitter.Kind != 0 || emitter.Render.MeshMaterialIndex >= 0).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(protectedLayers.Any(emitter => emitter.Kind == 1), Is.True, "The complete original impact must actually exercise its model branch.");
            Assert.That(protectedLayers.Any(emitter => emitter.Render.MeshMaterialIndex >= 0), Is.True, "The complete original impact must actually exercise a dedicated material.");
            Assert.That(protectedLayers.Select(emitter => emitter.Render.NativeSoftDepth), Is.All.Zero,
                "The ordinary sprite geometry bias must not be enabled on model, decal or dedicated material implementations.");
        });
    }

    [Test]
    public void OriginalSimulation_PreservesAuthoredSoftScaleAcrossAgeAndAttachmentScale()
    {
        var eye = new Vector3(0, 0, 10);
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 4f / 3, .1f, 100), eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var actualParticles = 0; var belowOne = false;
        var sampledSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { @"vfx\wh_main_machine_gyrocopter_rotor.xml", @"vfx\wh3_dlc28_neferata_disappear_reappear.xml" })
        {
            var bytes = ReadOfficial(path) ?? throw new FileNotFoundException(path);
            var conversion = TotalWarVfxConverter.Convert(new(bytes, path, ReadOfficial));
            foreach (var attachmentScale in new[] { 1f, .05f })
            {
                var simulation = new TotalWarVfxSimulation(conversion);
                foreach (var seconds in new[] { .2f, .6f, 1.4f })
                {
                    simulation.Seek(seconds, Matrix.CreateScale(attachmentScale));
                    var particles = simulation.Frame(camera).Where(particle => conversion.Emitters[particle.Layer].Render is
                        { Kind: 0, NativeSoftDepth: 1, MeshMaterialIndex: < 0 }).ToArray();
                    foreach (var particle in particles)
                    {
                        var expectedScale = Math.Min(particle.Transform.Right.Length(), particle.Transform.Up.Length());
                        Assert.That(particle.SoftParticleScale, Is.EqualTo(expectedScale).Within(.0001f),
                            $"{path}/{conversion.Emitters[particle.Layer].Name} at {seconds}s must retain its physical XY scale.");
                        Assert.That(particle.ParticleSize, Is.Zero, "The original ordinary-colour simulation keeps soft scale in its independent field.");
                        if (particle.SoftParticleScale is > 0 and < 1) belowOne = true;
                        sampledSources.Add(path);
                        actualParticles++;
                    }
                    TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Source = path, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                        Time = seconds, AttachmentScale = attachmentScale, ParticleCount = particles.Length,
                        SoftScales = particles.Select(particle => particle.SoftParticleScale),
                        LightingModes = particles.Select(particle => conversion.Emitters[particle.Layer].Render.LightingMode).Distinct(),
                        Scope = "CPU simulation of complete official source at explicit times and diagnostic attachment scales; no lighting submission or native visual acceptance." }));
                }
            }
        }
        Assert.Multiple(() =>
        {
            Assert.That(actualParticles, Is.GreaterThan(0));
            Assert.That(sampledSources, Has.Count.EqualTo(2), "Both complete original sources must contribute to the authored-scale checks.");
            Assert.That(belowOne, Is.True, "The simulation must preserve sub-unit authored scale, rather than replacing it with the default one.");
        });
    }

    private byte[]? ReadOfficial(string path)
    {
        var read = typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (byte[]?)read.Invoke(_official, [path]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IndependentSoftDepth_NonzeroViewportKeepsRasterOcclusionAndSofteningSeparate(bool vertical)
    {
        const int targetWidth = 211, targetHeight = 157, depthWidth = 53, depthHeight = 37;
        var viewport = new Rectangle(37, 29, 96, 72);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = CreatePreview(device);
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), viewport.Width / (float)viewport.Height, .1f, 100);
            var contactDepth = ProjectedDepth(10.5f, projection);
            var occluderDepth = ProjectedDepth(9, projection);
            var values = Enumerable.Range(0, depthWidth * depthHeight).Select(index =>
            {
                var absoluteX = (index % depthWidth + .5f) * targetWidth / depthWidth;
                var absoluteY = (index / depthWidth + .5f) * targetHeight / depthHeight;
                return (vertical ? absoluteY < viewport.Y + viewport.Height / 2f : absoluteX < viewport.X + viewport.Width / 2f)
                    ? contactDepth : 1f;
            }).ToArray();
            using var softDepth = new SoftDepthSurface(direct, depthWidth, depthHeight, values);
            var layer = EffekseerLayer.Create();
            layer.MaterialMode = 1; layer.SoftDistance = 2; layer.ZTest = 1;
            var particle = Particle(Matrix.CreateScale(16, 12, 1));
            var reference = Draw(device, preview, layer, particle, view, projection, viewport, targetWidth, targetHeight, 0, null);
            var softened = Draw(device, preview, layer, particle, view, projection, viewport, targetWidth, targetHeight, softDepth.View.NativePointer, null);
            var occluded = Draw(device, preview, layer, particle, view, projection, viewport, targetWidth, targetHeight,
                softDepth.View.NativePointer, (vertical, occluderDepth));
            var fallback = Draw(device, preview, layer, particle, view, projection, viewport, targetWidth, targetHeight, 0, null);
            float Mean(Vector4[] pixels, bool softHalf, bool occludedHalf) => Enumerable.Range(0, pixels.Length).Where(index =>
            {
                var x = index % viewport.Width; var y = index / viewport.Width;
                if (x < 8 || x >= viewport.Width - 8 || y < 8 || y >= viewport.Height - 8
                    || Math.Abs(x - viewport.Width / 2) < 8 || Math.Abs(y - viewport.Height / 2) < 8) return false;
                return (vertical ? y < viewport.Height / 2 : x < viewport.Width / 2) == softHalf
                    && (vertical ? x < viewport.Width / 2 : y < viewport.Height / 2) == occludedHalf;
            }).Average(index => pixels[index].X);
            var softenedRatio = Mean(softened, true, false) / Mean(reference, true, false);
            var clearRatio = Mean(softened, false, false) / Mean(reference, false, false);
            var visibleContactRatio = Mean(occluded, true, false) / Mean(reference, true, false);
            var visibleClearRatio = Mean(occluded, false, false) / Mean(reference, false, false);
            var rasterBlockedMaximum = Math.Max(Mean(occluded, true, true), Mean(occluded, false, true));
            var fallbackError = Difference(reference, fallback);
            WriteEvidence($"independent-depth-{vertical}", new
            {
                Vertical = vertical, Target = new[] { targetWidth, targetHeight }, Viewport = viewport,
                SoftDepthSize = new[] { depthWidth, depthHeight }, ContactDepth = contactDepth, OccluderDepth = occluderDepth,
                SoftenedRatio = softenedRatio, ClearRatio = clearRatio, VisibleContactRatio = visibleContactRatio,
                VisibleClearRatio = visibleClearRatio, RasterBlockedMaximum = rasterBlockedMaximum, FallbackError = fallbackError,
            }, device, viewport, softened, occluded);
            Assert.Multiple(() =>
            {
                Assert.That(Mean(reference, true, false), Is.GreaterThan(.1f));
                Assert.That(softenedRatio, Is.EqualTo(.25f).Within(.025f), "Only the independent soft SRV supplies the half-unit contact.");
                Assert.That(clearRatio, Is.EqualTo(1).Within(.005f));
                Assert.That(visibleContactRatio, Is.EqualTo(.25f).Within(.025f));
                Assert.That(visibleClearRatio, Is.EqualTo(1).Within(.005f));
                Assert.That(rasterBlockedMaximum, Is.LessThan(.001f), "The independent soft SRV must not replace the actual DSV used for occlusion.");
                Assert.That(fallbackError, Is.LessThan(.005f), "Removing the explicit SRV must restore the current DSV fallback without stale bindings.");
            });
        }
        finally { graphics.Release(); }
    }

    [TestCase(.3f, 1f, 1f)]
    [TestCase(.3f, 1f, .25f)]
    [TestCase(.3f, 0f, .25f)]
    [TestCase(0f, 1f, 1f)]
    public void NativeSoftDepth_BiasKeepsProjectedGeometryAndUsesTheOriginalScale(float factor, float strength, float scale)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = CreatePreview(device);
            var viewport = new Rectangle(19, 13, 128, 96);
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 4f / 3, .1f, 100);
            using var clearDepth = new SoftDepthSurface(direct, 47, 31, Enumerable.Repeat(1f, 47 * 31).ToArray());
            using var contactDepth = new SoftDepthSurface(direct, 47, 31, Enumerable.Repeat(ProjectedDepth(9.9f, projection), 47 * 31).ToArray());
            var referenceLayer = EffekseerLayer.Create();
            referenceLayer.MaterialMode = 1; referenceLayer.ZTest = 0;
            var nativeLayer = referenceLayer;
            nativeLayer.NativeSoftDepth = 1; nativeLayer.SoftDistance = factor; nativeLayer.DistortionSoftScaleStrength = strength;
            var particle = Particle(Matrix.CreateScale(3.5f, 2.5f, 1));
            particle.SoftParticleScale = scale;
            var reference = Draw(device, preview, referenceLayer, particle, view, projection, viewport, 193, 139, clearDepth.View.NativePointer, null);
            var nativeClear = Draw(device, preview, nativeLayer, particle, view, projection, viewport, 193, 139, clearDepth.View.NativePointer, null);
            var nativeContact = Draw(device, preview, nativeLayer, particle, view, projection, viewport, 193, 139, contactDepth.View.NativePointer, null);
            var bias = (float)(Half)factor * (1 + (float)(Half)strength * (Math.Clamp(scale, 0, 1) - 1));
            var expectedFade = Math.Clamp((9.9f - (10 - bias)) / (bias + .001f), 0, 1);
            var signal = Enumerable.Range(0, reference.Length).Where(index => reference[index].X > .1f).ToArray();
            Assert.That(signal, Is.Not.Empty);
            var actualFade = signal.Average(index => nativeContact[index].X / reference[index].X);
            var expectedImage = reference.Select(pixel => pixel * expectedFade).ToArray();
            var shapeError = Difference(reference, nativeClear);
            var fadeImageError = Difference(expectedImage, nativeContact);
            WriteEvidence($"native-depth-{factor}-{strength}-{scale}", new
            {
                Factor = factor, Strength = strength, SoftParticleScale = scale, EffectiveBias = bias,
                ReceiverViewDistance = 9.9f, GeometryViewDistance = 10f, ExpectedFade = expectedFade,
                ActualFade = actualFade, ProjectedImageError = shapeError, FadeImageError = fadeImageError,
            }, device, viewport, nativeClear, nativeContact);
            Assert.Multiple(() =>
            {
                Assert.That(shapeError, Is.LessThan(.005f), "Bias must retain the original geometry clip XY/W, including the projected quad edges.");
                Assert.That(actualFade, Is.EqualTo(expectedFade).Within(.015f), "Native fading uses biased softWorld and the half-precision factor/strength with the original scale.");
                Assert.That(fadeImageError, Is.LessThan(.015f), "The same fade must hold across the quad without moving its projection.");
            });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void NativeDepthBias_ClampsAtTheNearPlaneWithoutMovingOrDiscardingTheQuad()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = CreatePreview(device);
            var viewport = new Rectangle(0, 0, 128, 96);
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 4f / 3, .1f, 100);
            using var clearDepth = new SoftDepthSurface(direct, 41, 29, Enumerable.Repeat(1f, 41 * 29).ToArray());
            var layer = EffekseerLayer.Create(); layer.MaterialMode = 1; layer.ZTest = 0;
            var particle = Particle(Matrix.CreateScale(.04f, .03f, 1) * Matrix.CreateTranslation(0, 0, 9.85f));
            var reference = Draw(device, preview, layer, particle, view, projection, viewport, 128, 96, clearDepth.View.NativePointer, null);
            layer.NativeSoftDepth = 1; layer.SoftDistance = .3f;
            var biased = Draw(device, preview, layer, particle, view, projection, viewport, 128, 96, clearDepth.View.NativePointer, null);
            var maximumError = Difference(reference, biased);
            WriteEvidence("native-depth-near-clamp", new { GeometryViewDistance = .15f, Near = .1f, Bias = .3f, MaximumImageError = maximumError },
                device, viewport, reference, biased);
            Assert.Multiple(() =>
            {
                Assert.That(reference.Max(pixel => pixel.X), Is.GreaterThan(.1f));
                Assert.That(maximumError, Is.LessThan(.005f), "Bias beyond the near plane must clamp clip Z while keeping original clip XY/W.");
            });
        }
        finally { graphics.Release(); }
    }

    private EffekseerHandle CreatePreview(GraphicsDevice device)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, _image.Pixels, _image.Pixels.Length,
            _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.Mips));
        return preview;
    }

    private EffekseerParticle Particle(Matrix transform)
    {
        var sample = Enumerable.Range(0, _image.Width * _image.Height).Where(index => _image.Pixels[index * 4 + 3] >= 192)
            .OrderByDescending(index => _image.Pixels[index * 4] + _image.Pixels[index * 4 + 1] + _image.Pixels[index * 4 + 2]).First();
        return new() { Transform = transform, Colour = Vector4.One,
            UV = new((sample % _image.Width + .5f) / _image.Width, (sample / _image.Width + .5f) / _image.Height, 0, 0), SoftParticleScale = 1 };
    }

    private static Vector4[] Draw(GraphicsDevice device, EffekseerHandle preview, EffekseerLayer layer, EffekseerParticle particle,
        Matrix view, Matrix projection, Rectangle viewport, int width, int height, nint softDepth, (bool Vertical, float Depth)? occlusion)
    {
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        device.Viewport = new(viewport.X, viewport.Y, viewport.Width, viewport.Height); device.ScissorRectangle = viewport;
        if (occlusion is { } receiver)
        {
            var right = receiver.Vertical ? 0f : 1f; var bottom = receiver.Vertical ? -1f : 0f;
            VertexPositionColor[] vertices = [new(new(-1, bottom, receiver.Depth), Color.White), new(new(right, bottom, receiver.Depth), Color.White),
                new(new(-1, 1, receiver.Depth), Color.White), new(new(-1, 1, receiver.Depth), Color.White),
                new(new(right, bottom, receiver.Depth), Color.White), new(new(right, 1, receiver.Depth), Color.White)];
            using var effect = new BasicEffect(device) { VertexColorEnabled = true, World = Matrix.Identity, View = Matrix.Identity, Projection = Matrix.Identity };
            using var noColour = new BlendState { ColorWriteChannels = ColorWriteChannels.None };
            device.BlendState = noColour; device.DepthStencilState = DepthStencilState.Default; device.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in effect.CurrentTechnique.Passes) { pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2); }
        }
        device.BlendState = BlendState.Opaque; device.DepthStencilState = DepthStencilState.Default;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
        EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(projection), [particle], 1, softDepth: softDepth));
        device.SetRenderTarget(null);
        var full = new Vector4[width * height]; target.GetData(full);
        var crop = new Vector4[viewport.Width * viewport.Height];
        for (var y = 0; y < viewport.Height; y++) Array.Copy(full, (viewport.Y + y) * width + viewport.X, crop, y * viewport.Width, viewport.Width);
        return crop;
    }

    private sealed class SoftDepthSurface : IDisposable
    {
        private readonly SharpDX.Direct3D11.Texture2D _texture;
        internal SharpDX.Direct3D11.ShaderResourceView View { get; }
        internal SoftDepthSurface(SharpDX.Direct3D11.Device device, int width, int height, float[] values)
        {
            using var stream = new SharpDX.DataStream(values.Length * sizeof(float), true, true);
            stream.WriteRange(values); stream.Position = 0;
            _texture = new(device, new SharpDX.Direct3D11.Texture2DDescription
            {
                Width = width, Height = height, MipLevels = 1, ArraySize = 1, Format = SharpDX.DXGI.Format.R32_Float,
                SampleDescription = new(1, 0), Usage = SharpDX.Direct3D11.ResourceUsage.Immutable,
                BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
            }, new SharpDX.DataRectangle(stream.DataPointer, width * sizeof(float)));
            View = new(device, _texture);
        }
        public void Dispose() { View.Dispose(); _texture.Dispose(); }
    }

    private static float ProjectedDepth(float distance, Matrix projection)
    {
        var clip = Vector4.Transform(new Vector4(0, 0, -distance, 1), projection);
        return clip.Z / clip.W;
    }

    private static float Difference(Vector4[] left, Vector4[] right) => left.Zip(right).Max(pair => Math.Max(Math.Abs(pair.First.X - pair.Second.X),
        Math.Max(Math.Abs(pair.First.Y - pair.Second.Y), Math.Abs(pair.First.Z - pair.Second.Z))));

    private void WriteEvidence(string name, object metrics, GraphicsDevice device, Rectangle viewport, params Vector4[][] frames)
    {
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(metrics));
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using (var stream = new FileStream(Path.Combine(output, name + ".json"), FileMode.CreateNew))
            JsonSerializer.Serialize(stream, new { TexturePath, OriginalDdsSha256 = _sourceHash, Metrics = metrics,
                Scope = "Official original diffuse and fixed original texel; diagnostic quads and explicit scene depth only. Not complete official-effect or native visual acceptance." }, new JsonSerializerOptions { WriteIndented = true });
        for (var index = 0; index < frames.Length; index++)
        {
            using var png = new Texture2D(device, viewport.Width, viewport.Height);
            png.SetData(frames[index].Select(pixel => new Color(pixel)).ToArray());
            using var stream = new FileStream(Path.Combine(output, $"{name}-{index}.png"), FileMode.CreateNew);
            png.SaveAsPng(stream, viewport.Width, viewport.Height);
        }
    }

    private static float[] Values(Matrix m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
