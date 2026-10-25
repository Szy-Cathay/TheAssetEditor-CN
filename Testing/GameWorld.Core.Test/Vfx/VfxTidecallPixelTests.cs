#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxTidecallPixelTests
{
    private readonly VfxOfficialDistortionPixelTests _official = new();
    private const string Storm = @"vfx\wh3_main_entity_storm_dragon_attack_impact.xml";
    private const string Breath = @"vfx\wh2_dlc11_deep_tidecall_breath_area.xml";
    private const int Size = 256;

    [OneTimeSetUp]
    public void RequireOfficialOffscreenResources() => _official.LoadCurrentOfficialResourcesOnly();

    [TestCase(Storm)]
    [TestCase(Breath)]
    public void CompleteOfficialEffects_RenderTheirTidecallLayersWithoutResourceFallback(string path)
    {
        var source = Read(path) ?? throw new FileNotFoundException(path);
        var conversion = TotalWarVfxConverter.Convert(new(source, path, Read));
        Assert.That(conversion.Emitters.Count(emitter => IsTidecall(conversion, emitter)), Is.GreaterThanOrEqualTo(2));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(new(source, path, Read));
            using var withoutWater = new EffekseerVfxPreview(graphics.GraphicsDevice);
            withoutWater.Reload(new(source, path, Read));
            KeepTidecallDraws(withoutWater, false);
            CheckDiagnostics(preview);
            var times = path == Breath ? new[] { 1.5f, 1.9f, 2.3f } : new[] { .1f, .3f, .6f };
            var changed = new List<int>();
            foreach (var time in times)
            {
                preview.Seek(time, Matrix.Identity);
                withoutWater.Seek(time, Matrix.Identity);
                var pixels = Draw(graphics.GraphicsDevice, preview, Camera(preview), $"{Path.GetFileNameWithoutExtension(path)}-full-{time:F1}");
                var control = Draw(graphics.GraphicsDevice, withoutWater, Camera(preview), $"{Path.GetFileNameWithoutExtension(path)}-omit-water-{time:F1}");
                CheckDiagnostics(preview);
                CheckDiagnostics(withoutWater);
                changed.Add(pixels.Zip(control).Count(pair => Vector3.DistanceSquared(new(pair.First.X, pair.First.Y, pair.First.Z),
                    new(pair.Second.X, pair.Second.Y, pair.Second.Z)) > .00000001f));
            }
            Assert.That(changed.Max(), Is.GreaterThan(2), "The actual tidecall draw must change the complete effect at an authored stage; unrelated ripple pixels are insufficient.");
            Evidence($"{Path.GetFileNameWithoutExtension(path)}-full.json", new { Source = path, Sha256 = Hash(source), Times = times, Changed = changed,
                Scope = "Complete official source versus DIAG suppressing only tidecall draws; original models/materials/textures and simulation preserved. A material-contribution gate, not native visual equivalence." });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OfficialWaveDisplacement_ChangesGeometryThroughTheVertexParameterAndTextureBindings()
    {
        var source = Read(Breath) ?? throw new FileNotFoundException(Breath);
        var changedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        byte[]? WithoutDisplacement(string path)
        {
            var bytes = Read(path);
            if (bytes == null || !path.EndsWith(".material", StringComparison.OrdinalIgnoreCase)) return bytes;
            using var stream = new MemoryStream(bytes);
            var root = XElement.Load(stream);
            if (root.Element("shader")?.Value.Replace('\\', '/') != "shaders/wh2_vfx_cst_tidecall.xml.shader") return bytes;
            root.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == "displacement_scale").Element("value")!.Value = "0";
            changedMaterials.Add(path);
            return System.Text.Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        }
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var original = new EffekseerVfxPreview(graphics.GraphicsDevice);
            using var flat = new EffekseerVfxPreview(graphics.GraphicsDevice);
            original.Reload(new(source, Breath, Read)); flat.Reload(new(source, Breath, WithoutDisplacement));
            Assert.That(changedMaterials, Has.Count.EqualTo(2));
            KeepTidecallDraws(original); KeepTidecallDraws(flat);
            var camera = Camera(original);
            original.Seek(1.9f, Matrix.Identity); flat.Seek(1.9f, Matrix.Identity);
            var displaced = Draw(graphics.GraphicsDevice, original, camera, "breath-displacement-original");
            var control = Draw(graphics.GraphicsDevice, flat, camera, "breath-displacement-zero");
            CheckDiagnostics(original); CheckDiagnostics(flat);
            var changed = displaced.Zip(control).Count(pair => Vector3.DistanceSquared(new(pair.First.X, pair.First.Y, pair.First.Z),
                new(pair.Second.X, pair.Second.Y, pair.Second.Z)) > .00000001f);
            Assert.That(changed, Is.GreaterThan(2), "The original nonzero displacement must reach the VS independently of PS material masks.");
            Evidence("breath-displacement-zero.json", new { Source = Breath, Sha256 = Hash(source), ChangedPixels = changed,
                Scope = "DIAG: only displacement_scale=0 in both original wave materials, other draws suppressed; original DDS/models and particle age preserved. Not native equivalence." });
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OfficialMaterialAlphaZero_ClipsActualWaterModelsThroughTheSharedParameterBuffer()
    {
        var source = Read(Storm) ?? throw new FileNotFoundException(Storm);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var original = new EffekseerVfxPreview(graphics.GraphicsDevice);
            original.Reload(new(source, Storm, Read));
            KeepTidecallDraws(original);
            var camera = Camera(original);
            var changedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[]? AlphaZero(string path)
            {
                var bytes = Read(path);
                if (bytes == null || !path.EndsWith(".material", StringComparison.OrdinalIgnoreCase)) return bytes;
                using var stream = new MemoryStream(bytes);
                var root = XElement.Load(stream);
                if (!string.Equals(root.Element("shader")?.Value.Replace('\\', '/'), "shaders/wh2_vfx_cst_tidecall.xml.shader", StringComparison.OrdinalIgnoreCase)) return bytes;
                root.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == "total_alpha_strength").Element("value")!.Value = "0";
                changedMaterials.Add(path);
                return System.Text.Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
            }
            using var hidden = new EffekseerVfxPreview(graphics.GraphicsDevice);
            hidden.Reload(new(source, Storm, AlphaZero));
            KeepTidecallDraws(hidden);
            Assert.That(changedMaterials, Has.Count.EqualTo(2), "Both original water materials must actually reach the resolver.");
            var originalSignal = 0; var hiddenSignal = 0;
            foreach (var time in new[] { .1f, .3f, .6f })
            {
                original.Seek(time, Matrix.Identity); hidden.Seek(time, Matrix.Identity);
                var visiblePixels = Draw(graphics.GraphicsDevice, original, camera, $"storm-tidecall-original-{time:F1}");
                var hiddenPixels = Draw(graphics.GraphicsDevice, hidden, camera, $"storm-tidecall-alpha-zero-{time:F1}");
                originalSignal += visiblePixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .0001f);
                hiddenSignal += hiddenPixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .0001f);
                CheckDiagnostics(original); CheckDiagnostics(hidden);
            }
            Assert.Multiple(() =>
            {
                Assert.That(originalSignal, Is.GreaterThan(2), "Original water surfaces must actually draw before checking the control.");
                Assert.That(hiddenSignal, Is.Zero, "Zero authored alpha must clip the original surfaces, rather than silently keeping an opaque fallback.");
            });
            Evidence("storm-tidecall-alpha-zero.json", new { Source = Storm, Sha256 = Hash(source), ChangedMaterials = changedMaterials,
                OriginalSignal = originalSignal, HiddenSignal = hiddenSignal,
                Scope = "DIAG: only total_alpha_strength=0 in the two original water materials; other draws suppressed, original models/DDS/simulation unchanged. Not a complete-effect or native appearance acceptance." });
        }
        finally { graphics.Release(); }
    }

    private static bool IsTidecall(VfxConversion conversion, VfxPreviewEmitter emitter) => emitter.Render.MeshMaterialIndex >= 0
        && conversion.MeshMaterials[emitter.Render.MeshMaterialIndex].Parameters.Length == VfxTidecallMaterial.ParameterCount;

    private static void KeepTidecallDraws(EffekseerVfxPreview preview, bool keepTidecall = true)
    {
        var conversion = (VfxConversion)typeof(EffekseerVfxPreview).GetField("_conversion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        var layers = (Array)typeof(EffekseerVfxPreview).GetField("_renderLayers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        Assert.That(conversion.Emitters.Any(emitter => IsTidecall(conversion, emitter)), Is.True);
        for (var index = 0; index < conversion.Emitters.Count; index++)
            if (IsTidecall(conversion, conversion.Emitters[index]) != keepTidecall)
                foreach (var variant in (Array)layers.GetValue(index)!)
                    foreach (var name in new[] { "Coverage", "Emissive" })
                        Array.Fill((int[])variant!.GetType().GetProperty(name)!.GetValue(variant)!, -1);
    }

    private static CommonShaderParameters Camera(EffekseerVfxPreview preview)
    {
        var box = preview.PreviewBounds;
        var centre = box is { } bounds ? (bounds.Min + bounds.Max) / 2 : Vector3.Zero;
        var radius = box is { } extent ? Math.Max(5, Vector3.Distance(extent.Min, extent.Max) / 2) : 15;
        var eye = centre + Vector3.Normalize(new Vector3(1, .7f, 1)) * radius * 3.5f;
        return new(Matrix.CreateLookAt(eye, centre, Vector3.Up), Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 25000)
            * Matrix.CreateScale(-1, 1, 1), eye, centre, 0, 0, 0, 1, Vector3.One, []);
    }

    private static Vector4[] Draw(GraphicsDevice device, EffekseerVfxPreview preview, CommonShaderParameters camera, string name)
    {
        using var surface = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(surface); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        preview.Draw(camera); device.SetRenderTarget(null);
        var pixels = new Vector4[Size * Size]; surface.GetData(pixels);
        Assert.That(pixels.All(pixel => float.IsFinite(pixel.X) && float.IsFinite(pixel.Y) && float.IsFinite(pixel.Z) && float.IsFinite(pixel.W)), Is.True);
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var image = new Texture2D(device, Size, Size); image.SetData(pixels.Select(pixel => new Color(pixel)).ToArray());
            using var stream = new FileStream(Path.Combine(output, name + ".png"), FileMode.CreateNew); image.SaveAsPng(stream, Size, Size);
        }
        return pixels;
    }

    private static void CheckDiagnostics(EffekseerVfxPreview preview) => Assert.That(preview.Diagnostics.Where(diagnostic =>
        diagnostic.ReasonKey is "Vfx.Preview.RuntimeUnavailable" or "Vfx.Preview.TextureUnsupported"
        || diagnostic.Field.Contains("tidecall", StringComparison.OrdinalIgnoreCase) && diagnostic.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.Empty);

    private byte[]? Read(string path) => (byte[]?)typeof(VfxOfficialDistortionPixelTests).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_official, [path]);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Evidence(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }
}
