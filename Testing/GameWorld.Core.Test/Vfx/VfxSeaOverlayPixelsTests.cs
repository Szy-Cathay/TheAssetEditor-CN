#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Shared.Core.PackFiles.Utility;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxSeaOverlayPixelsTests
{
    private const int Width = 96, Height = 80;
    private const string TexturePath = @"vfx\textures\generic_cells_b_diffuse.dds";
    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 6, 8), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Billboard = BillboardRotation();
    private static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), Width / (float)Height, .1f, 50);
    private (byte[] Pixels, int Width, int Height, bool Srgb, int Mips) _image;

    [OneTimeSetUp]
    public void LoadOnlyTheOriginalManifestTextureAndTheActualNativeModule()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("The original game manifest is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(row => row.Length > 1)
            .ToDictionary(row => row[0], row => long.Parse(row[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var packPath = Path.Combine(data!, "vfx.pack");
        Assert.That(new FileInfo(packPath).Length, Is.EqualTo(manifest["vfx.pack"]));
        var pack = new PackFileContainerLoader(null!).Load(packPath)!;
        var bytes = pack.FileList.Single(pair => pair.Key.Replace('/', '\\').Equals(TexturePath, StringComparison.OrdinalIgnoreCase)).Value.DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo("0f9f9d442fc80bd338c9826d25df2d0fef4820918c9a68d5fb32d6650cc989ce"));
        _image = ((byte[], int, int, bool, int))typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [bytes, VfxTextureUsage.Diffuse])!;
        Assert.That(_image.Srgb, Is.True);
        Assert.That(_image.Mips, Is.GreaterThan(1));
        Assert.That(EffekseerNative.AecnVfxAbi(132, 148, 72, 176), Is.EqualTo(1));
        var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(item => item.ModuleName.Equals("AssetEditor.CN.Effekseer.dll", StringComparison.OrdinalIgnoreCase));
        var nativeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName)));
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_NATIVE_SHA256") is { Length: > 0 } expected)
            Assert.That(nativeHash, Is.EqualTo(expected).IgnoreCase);
        TestContext.Out.WriteLine($"DIAG raw SEA quads over the complete official {TexturePath} and original mips; material/MRT controls, not complete-effect ocean acceptance. Native={module.FileName} SHA256={nativeHash}");
    }

    [TestCase(0)]
    [TestCase(4)]
    public void WeightedAccumulation_IsSharedAcrossHandlesAndOrderAndAddsToTheExistingScene(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device);
            using var second = Preview(device);
            var a = Particle(new(.73125f, 0, 0, .375f));
            var b = Particle(new(0, 0, .73125f, .375f));
            var together = Render(device, first, second, [a, b], samples);
            var shared = Render(device, first, second, [a, b], samples, split: true);
            var reversed = Render(device, first, second, [b, a], samples, split: true);
            Assert.That(shared, Is.EqualTo(together), "One scene owns SEA accumulation across independent effect handles.");
            Assert.That(reversed, Is.EqualTo(together), "Two colour channels must not retain ordered SRC_ALPHA composition.");
            var background = Render(device, first, second, [], samples, colouredBackground: true);
            var overBackground = Render(device, first, second, [a, b], samples, colouredBackground: true);
            var changed = 0;
            for (var index = 0; index < together.Length; index++)
            {
                var addition = Unpack(together[index]);
                var original = Unpack(background[index]);
                var actual = Unpack(overBackground[index]);
                if (addition.X + addition.Z > .001f) changed++;
                Assert.That(Vector3.Distance(Rgb(actual), Rgb(original) + Rgb(addition)), Is.LessThan(.002f),
                    $"Battle-water adds resolved RGB without attenuating the existing background at pixel{index}.");
                Assert.That(actual.W, Is.EqualTo(Math.Max(original.W, addition.W)).Within(.001f), "The preview scene keeps its MAX alpha contract.");
            }
            Assert.That(changed, Is.GreaterThan(16), "Both original-texture layers must contribute actual coloured pixels.");
            foreach (var index in new[] { 0, Width - 1, Width * (Height - 1), Width * Height - 1 })
                Assert.That(overBackground[index], Is.EqualTo(background[index]), "Untouched scene pixels remain bit-identical.");
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OrthographicScene_UsesTheSameSeaAccumulationForTheMetadataPreviewCamera()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device);
            using var second = Preview(device);
            var a = Particle(new(.73125f, 0, 0, .375f));
            var b = Particle(new(0, 0, .73125f, .375f));
            var background = Render(device, first, second, [], 0, orthographic: true);
            var together = Render(device, first, second, [a, b], 0, orthographic: true);
            var reversed = Render(device, first, second, [b, a], 0, split: true, orthographic: true);
            Assert.That(reversed, Is.EqualTo(together));
            Assert.That(together, Is.Not.EqualTo(background));
            Assert.That(together.Select(Unpack).Count(pixel => pixel.X > .001f && pixel.Z > .001f), Is.GreaterThan(16));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void UnequalOpacityAndDepth_MatchAnIndependentNativeWeightAndRevealageOracle()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device);
            using var second = Preview(device);
            var a = Particle(new(.73125f, .125f, 0, .375f));
            var b = Particle(new(0, .125f, .73125f, .65f));
            // Perspective scaling preserves the same original atlas footprint at a different view depth.
            b.Transform = Matrix.CreateScale(2 * 4.98f, .75f * 4.98f, 1) * Billboard
                * Matrix.CreateTranslation(0, -23.88f, -31.84f);
            var red = Render(device, first, second, [a], 0);
            var blue = Render(device, first, second, [b], 0);
            var combined = Render(device, first, second, [a, b], 0, split: true);
            var compared = 0;
            for (var index = 0; index < combined.Length; index++)
            {
                var firstSample = Unpack(red[index]);
                var secondSample = Unpack(blue[index]);
                if (firstSample.W < .02f || secondSample.W < .02f || firstSample.W > .9f || secondSample.W > .9f) continue;
                var weightA = Weight(firstSample.W, 10);
                var weightB = Weight(secondSample.W, 49.8);
                var accumulatedA = firstSample.W * weightA;
                var accumulatedB = secondSample.W * weightB;
                var accumulatedAlpha = accumulatedA + accumulatedB;
                if (new[] { accumulatedA, accumulatedB, accumulatedAlpha }.Any(value => Math.Abs(value - .001) < .00005)) continue;
                // Invert each real one-layer consumer, including the native low-alpha divisor branch.
                var colourA = Rgb(firstSample) * (float)((accumulatedA < .001 ? 1 : accumulatedA) / firstSample.W);
                var colourB = Rgb(secondSample) * (float)((accumulatedB < .001 ? 1 : accumulatedB) / secondSample.W);
                var colour = (colourA + colourB) / (float)(accumulatedAlpha < .001 ? 1 : accumulatedAlpha);
                var coverage = 1 - (1 - (double)firstSample.W) * (1 - secondSample.W);
                var expected = new Vector4(colour * (float)coverage, (float)coverage);
                var actual = Unpack(combined[index]);
                Assert.That(Vector4.Distance(actual, expected), Is.LessThan(.003f),
                    $"Independent double-precision weight/revealage oracle at pixel{index}; original atlas sampling is measured by separate one-layer controls.");
                compared++;
            }
            Assert.That(compared, Is.GreaterThan(16));
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void TransparentParticlesAndOrdinarySprites_DoNotAcquireSeaCoverageOrChangeTheirPixels(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var configured = Preview(device);
            using var ordinary = Preview(device, sea: false);
            var background = Render(device, configured, configured, [], samples, colouredBackground: true);
            var transparent = Particle(new(.7f, .2f, .4f, 0));
            Assert.That(Render(device, configured, configured, [transparent], samples, colouredBackground: true), Is.EqualTo(background));
            var normal = Particle(new(.7f, .2f, .4f, .375f)); normal.Layer = 1;
            var seaConfigured = Render(device, configured, configured, [normal], samples, colouredBackground: true);
            var normalOnly = Render(device, ordinary, ordinary, [normal], samples, colouredBackground: true);
            Assert.That(seaConfigured, Is.EqualTo(normalOnly), "Configuring SEA layer zero must not reroute ordinary layer one.");
            Assert.That(normalOnly, Is.Not.EqualTo(background));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void RawConfiguration_RejectsInvalidAbiAndEntriesAtomicallyAndKeepsIndependentColourMultipliers()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device);
            var particle = Particle(new(.73125f, .127f, .33f, .375f));
            var before = Render(device, preview, preview, [particle], 0);
            var valid = Config();
            Assert.That(EffekseerNative.AecnVfxConfigureSea(preview, [valid], 1, 20), Is.Zero);
            foreach (var control in new[] { "layer", "coverage", "emission", "premultiplied", "nan", "infinity", "negative" })
            {
                var invalid = valid;
                switch (control)
                {
                    case "layer": invalid.LayerIndex = 999; break;
                    case "coverage": invalid.LegacyCoverageIndex = 1; break;
                    case "emission": invalid.LegacyEmissionIndex = 1; break;
                    case "premultiplied": invalid.Premultiplied = 2; break;
                    case "nan": invalid.CoveredColourMultiplier = float.NaN; break;
                    case "infinity": invalid.EmissionMultiplier = float.PositiveInfinity; break;
                    case "negative": invalid.EmissionMultiplier = -1; break;
                }
                Assert.That(EffekseerNative.AecnVfxConfigureSea(preview, [invalid], 1, EffekseerHalfLayer.Bytes), Is.Zero, control);
            }
            Assert.That(EffekseerNative.AecnVfxConfigureSea(preview, [valid, valid], 2, EffekseerHalfLayer.Bytes), Is.Zero);
            var bad = valid; bad.LayerIndex = 999;
            Assert.That(EffekseerNative.AecnVfxConfigureSea(preview, [valid, bad], 2, EffekseerHalfLayer.Bytes), Is.Zero);
            Assert.That(Render(device, preview, preview, [particle], 0), Is.EqualTo(before), "Rejected input must not publish a valid prefix or discard the previous configuration.");
            var zeroColour = valid; zeroColour.CoveredColourMultiplier = zeroColour.EmissionMultiplier = 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigureSea(preview, [zeroColour], 1, EffekseerHalfLayer.Bytes));
            var muted = Render(device, preview, preview, [particle], 0);
            Assert.That(muted.Select(Unpack).All(pixel => Rgb(pixel).LengthSquared() < .00000001f), Is.True);
            Assert.That(muted.Select(Unpack).Max(pixel => pixel.W), Is.GreaterThan(.01f), "Colour multipliers cannot erase independent particle coverage.");
            Assert.That(muted, Is.Not.EqualTo(before));
        }
        finally { graphics.Release(); }
    }

    [TestCase(false, 0)]
    [TestCase(false, 4)]
    [TestCase(true, 0)]
    [TestCase(true, 4)]
    public void ExplicitWaterReceiver_BlocksTheOverlayBehindForegroundAndKeepsVisibleWater(bool orthographic, int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device);
            var background = RenderReceiver(device, preview, samples, orthographic, false);
            var visible = RenderReceiver(device, preview, samples, orthographic, true);
            var foreground = RenderReceiver(device, preview, samples, orthographic, false, opaqueHeight: 1);
            var occluded = RenderReceiver(device, preview, samples, orthographic, true, opaqueHeight: 1);
            var behind = RenderReceiver(device, preview, samples, orthographic, false, opaqueHeight: -1);
            var behindWithWater = RenderReceiver(device, preview, samples, orthographic, true, opaqueHeight: -1);
            var missingReceiver = RenderReceiver(device, preview, samples, orthographic, true, receiver: false);
            Assert.That(missingReceiver, Is.EqualTo(background), "A clear depth buffer must not invent a water receiver when no explicit plane was supplied.");
            var blockedCount = 0;
            var visibleCount = 0;
            var behindCount = 0;
            for (var index = 0; index < visible.Length; index++)
            {
                var addition = Rgb(Unpack(visible[index])) - Rgb(Unpack(background[index]));
                if (addition.LengthSquared() < .000004f) continue;
                var x = index % Width;
                if (x < Width / 2 - 4 && Unpack(foreground[index]).W > .999f)
                {
                    Assert.That(occluded[index], Is.EqualTo(foreground[index]),
                        $"Visible opaque foreground must remain bit-identical; SEA accumulation itself has no DSV (pixel{index}).");
                    blockedCount++;
                }
                if (x > Width / 2 + 4 && foreground[index] == background[index])
                {
                    Assert.That(occluded[index], Is.EqualTo(visible[index]), $"Unoccluded water keeps its full overlay at pixel{index}.");
                    visibleCount++;
                }
                if (x < Width / 2 - 4 && Unpack(behind[index]).W > .999f)
                {
                    var actualAddition = Rgb(Unpack(behindWithWater[index])) - Rgb(Unpack(behind[index]));
                    Assert.That(Vector3.Distance(actualAddition, addition), Is.LessThan(.003f),
                        $"Scene depth behind the explicit Y0 receiver must not suppress visible water (pixel{index}).");
                    behindCount++;
                }
            }
            Assert.That(blockedCount, Is.GreaterThan(8));
            Assert.That(visibleCount, Is.GreaterThan(8));
            Assert.That(behindCount, Is.GreaterThan(8));
        }
        finally { graphics.Release(); }
    }

    [TestCase(false, 0)]
    [TestCase(false, 4)]
    [TestCase(true, 0)]
    [TestCase(true, 4)]
    public void CoplanarDepth24Receiver_PreservesEveryOverlayPixelAtTheOriginalMetadataCameraRange(bool orthographic, int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device);
            var background = RenderCoplanarReceiver(device, preview, samples, orthographic, false, false);
            var withoutDepth = RenderCoplanarReceiver(device, preview, samples, orthographic, true, false);
            var withDepth = RenderCoplanarReceiver(device, preview, samples, orthographic, true, true);
            Assert.That(withoutDepth.Zip(background).Count(pair => pair.First != pair.Second), Is.GreaterThan(24),
                "The same-state no-ground-depth control must contain actual original-atlas coverage.");
            Assert.That(withDepth, Is.EqualTo(withoutDepth),
                "Writing the exact Y0 receiver through VfxFlatGroundDepth cannot self-occlude any SEA pixel; only the depth contents changed.");
        }
        finally { graphics.Release(); }
    }

    private EffekseerHandle Preview(GraphicsDevice device, bool sea = true)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        try
        {
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, _image.Pixels, _image.Pixels.Length,
                _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.Mips));
            var layer = EffekseerLayer.Create(); layer.TextureIndex = 0; layer.MaterialMode = 1; layer.Blend = 1;
            layer.ZTest = 0; layer.EmissionShapeWeight = 1;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, [layer, layer], 2));
            if (sea) EffekseerNative.Check(EffekseerNative.AecnVfxConfigureSea(handle, [Config()], 1, EffekseerHalfLayer.Bytes));
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static EffekseerHalfLayer Config() => new()
    {
        LayerIndex = 0, LegacyCoverageIndex = -1, LegacyEmissionIndex = -1, Premultiplied = 1,
        CoveredColourMultiplier = 1, EmissionMultiplier = 0,
    };
    private static EffekseerParticle Particle(Vector4 colour) => new()
    {
        Transform = Matrix.CreateScale(2, .75f, 1) * Billboard, Colour = colour, UV = new(.09375f, 1f / 6, .03125f, 1f / 24),
        Time = .171051f, AppearancePhase = .474981f,
    };

    private static ulong[] Render(GraphicsDevice device, EffekseerHandle first, EffekseerHandle second,
        EffekseerParticle[] particles, int samples, bool split = false, bool colouredBackground = false, bool orthographic = false)
    {
        using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.HalfVector4,
            DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        Assert.That(target.MultiSampleCount, Is.EqualTo(samples));
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, colouredBackground ? new Color(32, 64, 128, 191) : Color.Transparent, 1, 0);
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var targets = direct.ImmediateContext.OutputMerger.GetRenderTargets(1, out var depth);
        try
        {
            using var resource = targets[0].Resource;
            using var texture = resource.QueryInterface<SharpDX.Direct3D11.Texture2D>();
            Assert.That(texture.Description.SampleDescription.Count, Is.EqualTo(samples == 0 ? 1 : samples));
        }
        finally { foreach (var view in targets) view?.Dispose(); depth?.Dispose(); }
        var lighting = new EffekseerLighting { Parameters = new(0, 0, 1, 0) };
        var projection = orthographic ? Matrix.CreateOrthographic(4, 4f * Height / Width, .1f, 50) : Projection;
        EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(first, in lighting));
        try
        {
            if (split)
            {
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), first, Values(View), Values(projection), [particles[0]], 1, in lighting));
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), second, Values(View), Values(projection), [particles[1]], 1, in lighting));
            }
            else if (particles.Length > 0)
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), first, Values(View), Values(projection), particles, particles.Length, in lighting));
        }
        finally { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(first)); }
        device.SetRenderTarget(null);
        var pixels = new HalfVector4[Width * Height]; target.GetData(pixels);
        return pixels.Select(pixel => pixel.PackedValue).ToArray();
    }

    private static ulong[] RenderReceiver(GraphicsDevice device, EffekseerHandle handle, int samples, bool orthographic,
        bool drawParticle, float? opaqueHeight = null, bool receiver = true)
    {
        var projection = orthographic ? Matrix.CreateOrthographic(6, 6f * Height / Width, .1f, 50) : Projection;
        using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.HalfVector4,
            DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        Assert.That(target.MultiSampleCount, Is.EqualTo(samples));
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(32, 64, 128, 191), 1, 0);
        device.DepthStencilState = DepthStencilState.Default;
        device.BlendState = BlendState.Opaque;
        device.RasterizerState = RasterizerState.CullNone;
        if (opaqueHeight is { } height)
        {
            using var effect = new BasicEffect(device) { View = View, Projection = projection, VertexColorEnabled = true };
            var colour = new Color(24, 48, 72, 255);
            var vertices = new[]
            {
                new VertexPositionColor(new(-20, height, -20), colour), new VertexPositionColor(new(-20, height, 20), colour),
                new VertexPositionColor(new(-.2f, height, -20), colour), new VertexPositionColor(new(-.2f, height, 20), colour),
            };
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, 4, new short[] { 0, 1, 2, 1, 3, 2 }, 0, 2);
            }
        }
        var lighting = new EffekseerLighting { Parameters = new(0, 0, receiver ? 1 : 0, 0) };
        var left = Particle(new(.73125f, .25f, .125f, .5f));
        left.Transform = Matrix.CreateScale(2.5f, 4, 1) * Matrix.CreateRotationX(MathHelper.PiOver2)
            * Matrix.CreateTranslation(-1.75f, 0, 0);
        var right = left;
        right.Transform.Translation = new(1.75f, 0, 0);
        EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(handle, in lighting));
        try
        {
            if (drawParticle)
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(handle.DangerousGetHandle(), handle, Values(View), Values(projection), [left, right], 2, in lighting));
        }
        finally { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(handle)); }
        device.SetRenderTarget(null);
        var pixels = new HalfVector4[Width * Height]; target.GetData(pixels);
        return pixels.Select(pixel => pixel.PackedValue).ToArray();
    }

    private static ulong[] RenderCoplanarReceiver(GraphicsDevice device, EffekseerHandle handle, int samples, bool orthographic,
        bool drawParticle, bool writeGroundDepth)
    {
        const int size = 448;
        var focus = new Vector3(2.2666378f, 0, .65890205f);
        var eye = focus + new Vector3(0, 15, 25);
        var view = Matrix.CreateLookAt(eye, focus, Vector3.Up);
        var projection = orthographic ? Matrix.CreateOrthographic(32, 32, .1f, 200)
            : Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), 1, .1f, 200);
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.HalfVector4,
            DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        Assert.That(target.MultiSampleCount, Is.EqualTo(samples));
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(32, 64, 128, 191), 1, 0);
        device.DepthStencilState = DepthStencilState.Default;
        device.BlendState = BlendState.Opaque;
        device.RasterizerState = RasterizerState.CullNone;
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var targets = direct.ImmediateContext.OutputMerger.GetRenderTargets(1, out var depth);
        try
        {
            Assert.That(depth.Description.Format, Is.EqualTo(SharpDX.DXGI.Format.D24_UNorm_S8_UInt));
        }
        finally { foreach (var targetView in targets) targetView?.Dispose(); depth?.Dispose(); }
        if (writeGroundDepth)
        {
            using var ground = new VfxFlatGroundDepth(device);
            var camera = new CommonShaderParameters(view, projection, eye, focus, 0, 0, 0, 1, Vector3.One, [],
                ViewportHeight: size, ViewportWidth: size, VfxGroundHeight: 0);
            Assert.That(ground.Draw(camera), Is.True);
        }
        var lighting = new EffekseerLighting { Parameters = new(0, 0, 1, 0) };
        var particle = Particle(new(.73125f, .25f, .125f, .5f));
        particle.Transform = Matrix.CreateScale(4, 4, 1) * Matrix.CreateRotationX(MathHelper.PiOver2)
            * Matrix.CreateTranslation(focus);
        EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(handle, in lighting));
        try
        {
            if (drawParticle)
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(handle.DangerousGetHandle(), handle,
                    Values(view), Values(projection), [particle], 1, in lighting));
        }
        finally { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(handle)); }
        device.SetRenderTarget(null);
        var pixels = new HalfVector4[size * size]; target.GetData(pixels);
        return pixels.Select(pixel => pixel.PackedValue).ToArray();
    }

    private static Matrix BillboardRotation()
    {
        var rotation = Matrix.Invert(View);
        rotation.Translation = Vector3.Zero;
        return rotation;
    }

    private static double Weight(double opacity, double depth)
    {
        var normalized = Math.Clamp((depth - .1) / (50 - .1), 0, 1);
        return Math.Clamp(Math.Min(Math.Pow(1 - .99 * normalized, 3) * 10000, 1) * opacity, .001, 150);
    }
    private static Vector4 Unpack(ulong value) => new HalfVector4 { PackedValue = value }.ToVector4();
    private static Vector3 Rgb(Vector4 value) => new(value.X, value.Y, value.Z);
    private static float[] Values(Matrix matrix) => [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44];
}
