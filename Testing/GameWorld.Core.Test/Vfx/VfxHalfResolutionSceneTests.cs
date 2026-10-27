#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Shared.Core.PackFiles.Utility;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxHalfResolutionSceneTests
{
    private const string TexturePath = @"vfx\textures\generic_cells_b_diffuse.dds";
    private (byte[] Pixels, int Width, int Height, bool Srgb, int Mips) _image;
    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60), Width / (float)Height, .1f, 50);
    private const int Width = 96, Height = 80;

    [OneTimeSetUp]
    public void LoadTheUnmodifiedOfficialAtlasAndCheckTheActualNativeModule()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("The original game manifest is required.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(row => row.Length > 1)
            .ToDictionary(row => row[0], row => long.Parse(row[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(data!, "vfx.pack");
        Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest["vfx.pack"]));
        var pack = new PackFileContainerLoader(null!).Load(path)!;
        var original = pack.FileList.Single(pair => pair.Key.Replace('/', '\\').Equals(TexturePath, StringComparison.OrdinalIgnoreCase)).Value.DataSource.ReadData();
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(original));
        Assert.That(sourceHash, Is.EqualTo("0f9f9d442fc80bd338c9826d25df2d0fef4820918c9a68d5fb32d6650cc989ce"));
        var decode = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!;
        _image = ((byte[], int, int, bool, int))decode.Invoke(null, [original, VfxTextureUsage.Diffuse])!;
        Assert.That(_image.Mips, Is.GreaterThan(1));
        Assert.That(_image.Srgb, Is.True);
        Assert.That(EffekseerNative.AecnVfxAbi(132, 148, 72, 176), Is.EqualTo(1));
        var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Single(item => item.ModuleName.Equals("AssetEditor.CN.Effekseer.dll", StringComparison.OrdinalIgnoreCase));
        var nativeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName)));
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EXPECTED_NATIVE_SHA256") is { Length: > 0 } expected)
            Assert.That(nativeHash, Is.EqualTo(expected).IgnoreCase);
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new
        {
            Boundary = "DIAG two float particles over the full original official DDS/mips. No VFX source edits, synthetic texture, lifetime or complete-effect visual acceptance claim.",
            TexturePath, SourcePack = path, SourceSha256 = sourceHash, _image.Width, _image.Height, _image.Mips,
            NativeModule = module.FileName, NativeSha256 = nativeHash,
        }));
    }

    [TestCase(0, "half")]
    [TestCase(4, "half")]
    [TestCase(0, "full")]
    [TestCase(4, "full")]
    [TestCase(0, "mixed")]
    [TestCase(4, "mixed")]
    public void WholeSceneMomentsAreSharedAcrossHandlesAndSubmissionOrder(int samples, string family)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var first = Preview(device, family);
            using var second = Preview(device, family);
            // Distinct colour channels make a conventional ordered SRC_ALPHA composition fail this control.
            // Equal depth/opacity avoids inventing a tolerance for half accumulation order.
            var a = Particle(new(.73125f, 0, 0, .375f));
            var b = Particle(new(0, 0, .73125f, .375f));
            if (family == "mixed") b.Layer = 5;
            var combined = Render(device, first, second, [a, b], samples);
            var shared = Render(device, first, second, [a, b], samples, split: true);
            var reversed = Render(device, first, second, [b, a], samples, split: true);
            Assert.That(shared, Is.EqualTo(combined), "Independent handles must contribute to one global moments/colour/resolve sequence.");
            Assert.That(reversed, Is.EqualTo(combined), "The two raw particles must not retain legacy interleaved coverage order.");
            var background = Render(device, first, second, [], samples);
            var redOnly = Render(device, first, second, [a], samples);
            var blueOnly = Render(device, first, second, [b], samples);
            var transparent = Render(device, first, second, [a, b], samples, transparent: true);
            Assert.That(combined, Is.Not.EqualTo(background));
            Assert.That(combined, Is.Not.EqualTo(redOnly));
            Assert.That(combined, Is.Not.EqualTo(blueOnly));
            Assert.That(transparent.Any(pixel => pixel >> 48 != 0), Is.True, "Transparent scene receives effective particle coverage alpha.");
            for (var index = 0; index < combined.Length; index++)
                Assert.That(combined[index] >> 48, Is.EqualTo(Math.Max(background[index] >> 48, transparent[index] >> 48)),
                    $"Existing MAX scene-alpha contract at pixel{index}.");
            foreach (var index in new[] { 0, Width - 1, Width * (Height - 1), Width * Height - 1 })
                Assert.That(combined[index], Is.EqualTo(background[index]), "Untouched half/compose pixels must preserve all original scene channels.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(0, "full")]
    [TestCase(4, "full")]
    [TestCase(0, "model")]
    [TestCase(4, "model")]
    [TestCase(0, "viewport")]
    [TestCase(4, "viewport")]
    public void UnsupportedWholeSceneConditionsFallBackToExactLegacyHalfPixels(int samples, string condition)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var configured = Preview(device, "half");
            using var legacy = Preview(device, "legacy");
            var raw = new List<EffekseerParticle> { Particle(new(.73125f, .127f, .33f, .375f)), Particle(new(.21f, .52f, .63f, .28125f)) };
            if (condition != "viewport")
            {
                var extra = Particle(new(.125f, .25f, .5f, .5f)); extra.Layer = condition == "model" ? 4 : 3;
                extra.Transform = Matrix.CreateScale(.7f) * Matrix.CreateTranslation(.6f, 0, 0);
                raw.Add(extra);
            }
            var expected = new List<EffekseerParticle>();
            foreach (var particle in raw)
            {
                if (particle.Layer != 2) { expected.Add(particle); continue; }
                var coverage = particle; coverage.Layer = 0; ScaleColour(ref coverage, .25f); expected.Add(coverage);
                var emission = particle; emission.Layer = 1; ScaleColour(ref emission, 1.5f); expected.Add(emission);
            }
            var offset = condition == "viewport";
            var actual = Render(device, configured, configured, raw.ToArray(), samples, offset: offset);
            var baseline = Render(device, legacy, legacy, expected.ToArray(), samples, offset: offset);
            Assert.That(actual, Is.EqualTo(baseline), "Compare every resolved R16G16B16A16 half bit, including alpha; no pixel-count or brightness tolerance.");
            Assert.That(actual, Is.Not.EqualTo(Render(device, legacy, legacy, [], samples, offset: offset)),
                "The complete fallback control must include real original-texture colour, not an empty draw.");
        }
        finally { graphics.Release(); }
    }

    [TestCase(0)]
    [TestCase(4)]
    public void ExplicitFullKeepsItsPixelFootprintInsteadOfUsingHalfReconstruction(int samples)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var half = Preview(device, "half");
            using var full = Preview(device, "full");
            var particle = Particle(new(.73125f, .127f, .33f, .375f));
            particle.Transform *= Matrix.CreateTranslation(.037f, .041f, 0);
            var halfPixels = Render(device, half, half, [particle], samples);
            var fullPixels = Render(device, full, full, [particle], samples);
            Assert.That(fullPixels, Is.Not.EqualTo(halfPixels),
                "The same official atlas and off-grid geometry must retain distinct FULL sampling instead of silently shrinking both families.");
        }
        finally { graphics.Release(); }
    }

    [TestCase("half")]
    [TestCase("full")]
    public void InvalidRawConfigurationIsRejectedAtomicallyAndRetainsTheValidPixels(string family)
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device, family);
            var particle = Particle(new(.73125f, .127f, .33f, .375f));
            var before = Render(device, preview, preview, [particle], 0);
            var valid = Config();
            Assert.That(Configure(preview, family, [valid], 1, 20), Is.Zero, "24-byte configuration ABI is mandatory.");
            foreach (var mutation in new[] { "raw-id", "self", "coverage-mode", "emission-mode", "missing", "nan", "infinity", "negative", "premultiplied" })
            {
                var invalid = valid;
                switch (mutation)
                {
                    case "raw-id": invalid.LayerIndex = 999; break;
                    case "self": invalid.LegacyCoverageIndex = 2; break;
                    case "coverage-mode": invalid.LegacyCoverageIndex = 1; break;
                    case "emission-mode": invalid.LegacyEmissionIndex = 0; break;
                    case "missing": invalid.LegacyCoverageIndex = invalid.LegacyEmissionIndex = -1; break;
                    case "nan": invalid.CoveredColourMultiplier = float.NaN; break;
                    case "infinity": invalid.EmissionMultiplier = float.PositiveInfinity; break;
                    case "negative": invalid.EmissionMultiplier = -1; break;
                    case "premultiplied": invalid.Premultiplied = 2; break;
                }
                Assert.That(Configure(preview, family, [invalid], 1, EffekseerHalfLayer.Bytes), Is.Zero, mutation);
            }
            Assert.That(Configure(preview, family, [valid, valid], 2, EffekseerHalfLayer.Bytes), Is.Zero, "Duplicate raw layer IDs are ambiguous.");
            Assert.That(Configure(preview, family, [valid], -1, EffekseerHalfLayer.Bytes), Is.Zero);
            var bad = valid; bad.LayerIndex = 999;
            Assert.That(Configure(preview, family, [valid, bad], 2, EffekseerHalfLayer.Bytes), Is.Zero,
                "A later invalid entry must not publish the validated prefix.");
            Assert.That(Render(device, preview, preview, [particle], 0), Is.EqualTo(before));
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void CrossFamilyLayerIdsAreRejectedWithoutLosingEitherValidConfiguration()
    {
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = Preview(device, "mixed");
            var a = Particle(new(.73125f, 0, 0, .375f));
            var b = Particle(new(0, 0, .73125f, .375f)); b.Layer = 5;
            var before = Render(device, preview, preview, [a, b], 0);
            Assert.That(Configure(preview, "full", [Config()], 1, EffekseerHalfLayer.Bytes), Is.Zero,
                "The same raw LayerIndex cannot mean both HALF and FULL.");
            var full = Config(); full.LayerIndex = 5;
            Assert.That(Configure(preview, "half", [full], 1, EffekseerHalfLayer.Bytes), Is.Zero);
            Assert.That(Render(device, preview, preview, [a, b], 0), Is.EqualTo(before));
        }
        finally { graphics.Release(); }
    }

    private EffekseerHandle Preview(GraphicsDevice device, string family)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        try
        {
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, _image.Pixels, _image.Pixels.Length,
                _image.Width, _image.Height, _image.Srgb ? 1 : 0, _image.Mips));
            var coverage = EffekseerLayer.Create(); coverage.TextureIndex = 0; coverage.MaterialMode = 1; coverage.Blend = 1; coverage.ZTest = 0;
            var emission = coverage; emission.MaterialMode = 2; emission.Blend = 2;
            var raw = coverage;
            var full = coverage;
            var model = coverage; model.Kind = 1; model.ModelIndex = 0;
            var vertices = new[]
            {
                Vertex(-.5f, -.5f, 0, 0), Vertex(.5f, -.5f, 1, 0), Vertex(-.5f, .5f, 0, 1), Vertex(.5f, .5f, 1, 1),
            };
            EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, vertices.Length, [0, 1, 2, 2, 1, 3], 6));
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [coverage, emission, raw, full, model, raw], 6));
            if (family is "half" or "full") EffekseerNative.Check(Configure(preview, family, [Config()], 1, EffekseerHalfLayer.Bytes));
            if (family == "mixed")
            {
                EffekseerNative.Check(Configure(preview, "half", [Config()], 1, EffekseerHalfLayer.Bytes));
                var fullConfig = Config(); fullConfig.LayerIndex = 5;
                EffekseerNative.Check(Configure(preview, "full", [fullConfig], 1, EffekseerHalfLayer.Bytes));
            }
            return preview;
        }
        catch { preview.Dispose(); throw; }
    }

    private static EffekseerVertex Vertex(float x, float y, float u, float v) => new()
    {
        Position = new(x, y, 0), Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, Binormal = Vector3.UnitY,
        UV = new(.09375f + u * .03125f, 1f / 6 + v / 24), Colour = Vector4.One,
    };

    private static EffekseerHalfLayer Config() => new()
    {
        LayerIndex = 2, LegacyCoverageIndex = 0, LegacyEmissionIndex = 1, Premultiplied = 1,
        CoveredColourMultiplier = .25f, EmissionMultiplier = 1.5f,
    };

    private static EffekseerParticle Particle(Vector4 colour) => new()
    {
        Layer = 2, Transform = Matrix.CreateScale(2, .75f, 1), Colour = colour,
        UV = new(.09375f, 1f / 6, .03125f, 1f / 24), Time = .171051f, AppearancePhase = .474981f,
    };

    private static ulong[] Render(GraphicsDevice device, EffekseerHandle first, EffekseerHandle second,
        EffekseerParticle[] particles, int samples, bool split = false, bool offset = false, bool transparent = false)
    {
        using var target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.HalfVector4,
            DepthFormat.Depth24, samples, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(32, 64, 128, transparent ? 0 : 191), 1, 0);
        if (offset) device.Viewport = new(8, 6, Width - 16, Height - 12);
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        var views = direct.ImmediateContext.OutputMerger.GetRenderTargets(1, out var depth);
        try
        {
            using var resource = views[0].Resource;
            using var texture = resource.QueryInterface<SharpDX.Direct3D11.Texture2D>();
            Assert.That(texture.Description.SampleDescription.Count, Is.EqualTo(samples == 0 ? 1 : samples), "The requested 1x/4x target must really exist.");
        }
        finally { foreach (var view in views) view?.Dispose(); depth?.Dispose(); }
        var lighting = new EffekseerLighting();
        EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(first, in lighting));
        try
        {
            if (split)
            {
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), first, Values(View), Values(Projection), [particles[0]], 1, in lighting));
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), second, Values(View), Values(Projection), [particles[1]], 1, in lighting));
            }
            else if (particles.Length > 0)
                EffekseerNative.Check(EffekseerNative.AecnVfxDrawInScene(first.DangerousGetHandle(), first, Values(View), Values(Projection), particles, particles.Length, in lighting));
        }
        finally { EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(first)); }
        device.SetRenderTarget(null);
        var pixels = new HalfVector4[Width * Height]; target.GetData(pixels);
        return pixels.Select(pixel => pixel.PackedValue).ToArray();
    }

    private static void ScaleColour(ref EffekseerParticle particle, float scale)
    { particle.Colour.X *= scale; particle.Colour.Y *= scale; particle.Colour.Z *= scale; }

    private static int Configure(EffekseerHandle preview, string family, EffekseerHalfLayer[] config, int count, int bytes)
        => family == "half" ? EffekseerNative.AecnVfxConfigureHalf(preview, config, count, bytes)
            : EffekseerNative.AecnVfxConfigureFull(preview, config, count, bytes);

    private static float[] Values(Matrix matrix) => [matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44];
}
