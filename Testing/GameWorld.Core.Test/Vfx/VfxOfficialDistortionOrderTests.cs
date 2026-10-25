#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxOfficialDistortionOrderTests
{
    private const int Size = 256;
    private const string SourcePath = @"vfx\wh3_dlc28_lib_neferata.xml";
    private const string NormalPath = @"vfx\textures\general_anim_normal.dds";
    private const string DiffusePath = @"vfx\textures\wh_fire_loop_a_grayscale_diffuse.dds";
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private byte[] _source = [], _normal = [], _diffuse = [];
    private static readonly Matrix View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
    private static readonly Matrix Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45), 1, .01f, 100);

    [OneTimeSetUp]
    public void LoadCurrentOfficialResourcesOnly()
    {
        // Reuse the existing explicit opt-in, manifest-only resolver and native ABI gate.
        _official.LoadCurrentOfficialResourcesOnly();
        _source = Read(SourcePath); _normal = Read(NormalPath); _diffuse = Read(DiffusePath);
        var texturePaths = VfxDocument.Read(_source).Root.Descendants("texture_property")
            .Select(property => ((string?)property.Attribute("path") ?? "").Replace('/', '\\').ToLowerInvariant()).ToArray();
        Assert.That(texturePaths, Does.Contain(NormalPath).And.Contain(DiffusePath),
            "The original official library must reference both unmodified diagnostic textures.");
    }

    [Test]
    public void OppositeOfficialNormals_AccumulateAcrossTransparentColourBeforeOneResolve()
    {
        var directory = EvidenceDirectory();
        var distortion = EffekseerLayer.Create();
        distortion.TextureIndex = 0; distortion.Distortion = 1; distortion.ZTest = 0;
        var colour = EffekseerLayer.Create(); colour.TextureIndex = 1; colour.ZTest = 0;
        EffekseerLayer[] layers = [distortion, colour, distortion];
        var positive = new EffekseerParticle
        {
            Layer = 0, Transform = Matrix.CreateScale(5), Colour = Vector4.One,
            UV = new(.25f, .625f, .125f, .125f), ParticleSize = 5,
            NormalRotation = new(1, 0, 0, 1),
        };
        var negative = positive; negative.Layer = 2; negative.NormalRotation = -positive.NormalRotation;
        var transparent = positive; transparent.Layer = 1; transparent.Colour.W = 0;
        transparent.NormalRotation = Vector4.Zero;
        var sameDirection = positive; sameDirection.Layer = 2;
        SaveInputs(directory, layers);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var background = Render(device, layers, [], "background", directory);
            var colourOnly = Render(device, layers, [transparent], "transparent-colour-only", directory);
            var positiveOnly = Render(device, layers, [positive], "positive-direction-only", directory);
            var negativeOnly = Render(device, layers, [negative], "negative-direction-only", directory);
            var consecutive = Render(device, layers, [positive, negative], "consecutive-opposites", directory);
            var colourBefore = Render(device, layers, [transparent, positive, negative], "colour-before-opposites", directory);
            var interleaved = Render(device, layers, [positive, transparent, negative], "colour-between-opposites", directory);
            var positiveControl = Render(device, layers, [positive, transparent, sameDirection], "colour-between-same-directions", directory);
            var metrics = new
            {
                TransparentColourMaximumDifference = MaximumDifference(background, colourOnly),
                PositiveDirectionChangedPixels = ChangedPixels(background, positiveOnly),
                NegativeDirectionChangedPixels = ChangedPixels(background, negativeOnly),
                ConsecutiveOppositesMaximumDifference = MaximumDifference(background, consecutive),
                ColourBeforeMaximumDifference = MaximumDifference(consecutive, colourBefore),
                ColourBetweenMaximumDifference = MaximumDifference(consecutive, interleaved),
                SameDirectionControlChangedPixels = ChangedPixels(background, positiveControl),
                SameDirectionVersusOppositeChangedPixels = ChangedPixels(consecutive, positiveControl),
            };
            if (directory != null) SaveJson(Path.Combine(directory, "metrics.json"), metrics);
            TestContext.Progress.WriteLine(JsonSerializer.Serialize(metrics));
            Assert.Multiple(() =>
            {
                Assert.That(metrics.TransparentColourMaximumDifference, Is.LessThanOrEqualTo(1),
                    "The submitted ordinary colour draw must be transparent on its own.");
                Assert.That(metrics.PositiveDirectionChangedPixels, Is.GreaterThan(20));
                Assert.That(metrics.NegativeDirectionChangedPixels, Is.GreaterThan(20));
                Assert.That(metrics.ConsecutiveOppositesMaximumDifference, Is.LessThanOrEqualTo(1),
                    "Equal official normal fields with opposite explicit bases must cancel on the original background.");
                Assert.That(metrics.ColourBeforeMaximumDifference, Is.LessThanOrEqualTo(1),
                    "The final distortion group reference must retain the same cancellation.");
                Assert.That(metrics.ColourBetweenMaximumDifference, Is.LessThanOrEqualTo(1),
                    "An ordinary transparent draw inside one effect must not split the native shared distortion accumulation.");
                Assert.That(metrics.SameDirectionControlChangedPixels, Is.GreaterThan(20));
                Assert.That(metrics.SameDirectionVersusOppositeChangedPixels, Is.GreaterThan(20),
                    "Changing only the second shading direction must remain visible; dormant distortion cannot pass.");
            });
        }
        finally { graphics.Release(); }
    }

    private Color[] Render(GraphicsDevice device, EffekseerLayer[] layers, EffekseerParticle[] particles, string label, string? directory)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var handle = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        foreach (var (bytes, usage) in new[] { (_normal, VfxTextureUsage.DistortionNormal), (_diffuse, VfxTextureUsage.Diffuse) })
        {
            var decoder = typeof(EffekseerVfxPreview).GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("DecodeTexture");
            var image = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))decoder.Invoke(null, [bytes, usage])!;
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(handle, image.Pixels, image.Pixels.Length,
                image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
        }
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(handle, layers, layers.Length));
        using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        device.Viewport = new(0, 0, Size, Size); device.ScissorRectangle = new(0, 0, Size, Size);
        ExistingHelper("DrawBackground", BindingFlags.Static).Invoke(null, [device, (byte)255, false]);
        if (particles.Length != 0)
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(handle, Values(View), Values(Projection), particles, particles.Length));
        device.SetRenderTarget(null);
        var pixels = new Color[Size * Size]; target.GetData(pixels);
        if (directory != null)
        {
            using (var stream = File.Create(Path.Combine(directory, label + ".png"))) target.SaveAsPng(stream, Size, Size);
            SaveJson(Path.Combine(directory, label + ".json"), new
            {
                Scenario = label, Calls = particles.Length == 0 ? 0 : 1, EffectInstances = 1, SubmittedLayers = particles.Select(particle => particle.Layer).ToArray(),
                Particles = particles.Select(particle => new
                {
                    particle.Layer, Transform = Values(particle.Transform), Colour = Values(particle.Colour),
                    UV = Values(particle.UV), BasisPayload = Values(particle.NormalRotation), particle.ParticleSize,
                }).ToArray(),
                View = Values(View), Projection = Values(Projection),
                RenderTarget = new[] { Size, Size }, Viewport = new[] { 0, 0, Size, Size }, SceneDepth = "Cleared to 1; particle ZTest disabled",
            });
        }
        return pixels;
    }

    private void SaveInputs(string? directory, EffekseerLayer[] layers)
    {
        if (directory == null) return;
        File.WriteAllBytes(Path.Combine(directory, "official-original.xml"), _source);
        File.WriteAllBytes(Path.Combine(directory, "official-normal.dds"), _normal);
        File.WriteAllBytes(Path.Combine(directory, "official-diffuse.dds"), _diffuse);
        var originsField = typeof(VfxOfficialDistortionPixelTests).GetField("_origins", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Official resource provenance");
        var origins = (Dictionary<string, object>)originsField.GetValue(_official)!;
        var manifestPath = Path.Combine(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA")!, "manifest.txt");
        SaveJson(Path.Combine(directory, "inputs.json"), new
        {
            SourcePath, SourceSha256 = Hash(_source), NormalPath, NormalSha256 = Hash(_normal), DiffusePath, DiffuseSha256 = Hash(_diffuse),
            ManifestPath = manifestPath, ManifestSha256 = Hash(File.ReadAllBytes(manifestPath)), Origins = origins.Values,
            LayerAbiBytes = Marshal.SizeOf<EffekseerLayer>(),
            Runtime = new[] { "GameWorld.Core.dll", "AssetEditor.CN.Effekseer.dll" }.Select(name => new
            {
                Name = name, Path = Path.Combine(AppContext.BaseDirectory, name), Sha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name))),
            }).ToArray(),
            Layers = layers.Select((layer, index) => new { Index = index, layer.Kind, layer.Distortion, layer.TextureIndex, layer.DistortionMaskIndex,
                layer.MaterialMode, layer.Blend, layer.ZTest, layer.CameraFacing, layer.SoftDistance, layer.DistortionSoftDepth }).ToArray(),
            Scope = "Official-resource-derived fixed-quad draw-order diagnostic. Original official XML and DDS bytes are retained. The normal field is sampled from the original DDS through the current distortion-normal loader; the ordinary alpha-zero draw samples the original diffuse DDS. The contrasting background is untextured scene geometry from the existing helper, never a replacement VFX texture. This does not establish complete Gyro/native timing or visual acceptance.",
            DiagnosticChanges = new[]
            {
                "Replace source simulation with two coincident fixed 5x5 normal-only distortion quads; no diffuse/gradient/crush/soft-depth on distortion; alpha1; UV(.25,.625,.125,.125).",
                "Use exact explicit shading bases T.xy/B.xy=(1,0,0,1) and (-1,0,0,-1), preserving identical physical geometry and original normal pixels.",
                "Submit an ordinary diffuse-textured colour quad with vertex alpha0; keep its draw in the same native effect call. Change only particle submission order across scenarios.",
                "Positive control changes the second shading basis to the same direction; all other geometry, resources and parameters remain identical.",
            },
        });
    }

    private byte[] Read(string path) => (byte[]?)ExistingHelper("Read", BindingFlags.Instance).Invoke(_official, [path])
        ?? throw new FileNotFoundException(path);
    private static MethodInfo ExistingHelper(string name, BindingFlags flags) => typeof(VfxOfficialDistortionPixelTests)
        .GetMethod(name, flags | BindingFlags.NonPublic) ?? throw new MissingMethodException("Official diagnostic helper: " + name);
    private static string? EvidenceDirectory()
    {
        var root = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(root)) return null;
        var path = Path.Combine(root, "official-distortion-order", TestContext.CurrentContext.Test.Name);
        if (Directory.Exists(path)) throw new IOException("Preserve prior evidence; choose a new ASSETEDITOR_VFX_RENDER_OUTPUT: " + path);
        Directory.CreateDirectory(path); return path;
    }
    private static void SaveJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static float[] Values(Vector4 value) => [value.X, value.Y, value.Z, value.W];
    private static float[] Values(Matrix matrix) => [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44];
    private static int PixelDifference(Color left, Color right) => Math.Max(Math.Abs(left.R - right.R), Math.Max(Math.Abs(left.G - right.G), Math.Abs(left.B - right.B)));
    private static int MaximumDifference(Color[] left, Color[] right) => left.Zip(right).Max(pair => PixelDifference(pair.First, pair.Second));
    private static int ChangedPixels(Color[] left, Color[] right) => left.Zip(right).Count(pair => PixelDifference(pair.First, pair.Second) > 2);
}
