#nullable enable
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxFallbackResourceReloadPixelTests
{
    private const string Wrapper = @"vfx\models\wh3_dlc27_comet_shard_02.wsmodel";
    private const string Shader = "shaders/rigid_character_emissive.xml.shader";
    private static readonly (string Pack, string Path, string Sha256)[] OfficialResources =
    [
        ("vfx_models.pack", Wrapper, "96ba575e51dc6639ed798b6bd3daeaf3babe732340502883ff252259acc40950"),
        ("vfx_models.pack", @"vfx\models\comet_shard_02.rigid_model_v2", "eeb676e93f6f376d3dd4424c218cef611cd2d0d44c8ece9e23911e4cf6c0f0ee"),
        ("data.pack", @"art\vfx\meshes\materials\heaven_lore_comet_alpha_off.xml.material", "5a727c1b58636eb2a84a29f37f95bbe204716871546f7b2a72ab8820afa5c507"),
        ("vfx_models_dds.pack", @"vfx\models\tex\heaven_lore_comet_base_colour.dds", "143a036b33c10b79742481dadba4d093d0a9bfe069c3930a029147a88e53c4bc"),
    ];

    [Test]
    public void TheUnchangedOfficialModelFallbackReusesUploadsAndMatchesFreshPixelsWithoutSuppressingItsDiagnostic()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var resources = ReadOfficialResources();
        byte[] Resolve(string path) => resources.TryGetValue(path.Replace('/', '\\'), out var bytes)
            ? bytes : throw new InvalidDataException("Unexpected dependency outside the original fallback model: " + path);
        var original = Snapshot(1, Resolve);
        var conversion = TotalWarVfxConverter.Convert(original);
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        Assert.That(conversion.Models[0].ApproximateMaterialShader, Is.EqualTo(Shader));
        Assert.That(VfxPlaybackResourceIdentity.CanReuse(conversion), Is.True);
        var bounds = BoundingBox.CreateFromPoints(conversion.Models[0].Vertices.Select(vertex => vertex.Position));
        var size = Math.Max(.01f, (bounds.Max - bounds.Min).Length());
        var focus = (bounds.Min + bounds.Max) * .5f;
        var distance = size * 8 + 10;
        var eye = focus + Vector3.Normalize(new Vector3(.75f, .5f, 1)) * distance;
        var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, focus, Vector3.Up),
            Matrix.CreateOrthographic(size * 3, size * 3, .001f, distance + size * 20), eye, focus, 0, 0, 0, 1, Vector3.One, []);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(original);
            var owner = Handle(preview);
            preview.Seek(.2f, Matrix.Identity);
            var baseline = Render(device, preview, camera);
            foreach (var scale in new[] { .6f, 1f, .75f })
            {
                var snapshot = Snapshot(scale, Resolve);
                preview.Reload(snapshot);
                Assert.That(Handle(preview), Is.SameAs(owner), "A parameter edit must reuse the complete, exclusive uploads without changing the model fallback.");
                Assert.That(owner.IsClosed, Is.False);
                using var fresh = new EffekseerVfxPreview(device);
                fresh.Reload(snapshot);
                Assert.That(Handle(fresh), Is.Not.SameAs(owner));
                Assert.That(preview.Diagnostics, Is.EqualTo(fresh.Diagnostics));
                foreach (var phase in new[] { .2f, .7f })
                {
                    preview.Seek(phase, Matrix.Identity);
                    fresh.Seek(phase, Matrix.Identity);
                    var reusedPixels = Render(device, preview, camera);
                    var freshPixels = Render(device, fresh, camera);
                    Assert.That(reusedPixels.Zip(freshPixels, Vector4.Distance).Max(), Is.LessThan(.000001f),
                        "Every pixel must match a fresh full upload at scale " + scale + " and phase " + phase);
                    if (scale == .6f && phase == .2f)
                        Assert.That(reusedPixels.SequenceEqual(baseline), Is.False, "The real size edit must visibly change this model.");
                    if (scale == 1 && phase == .2f)
                        Assert.That(reusedPixels, Is.EqualTo(baseline), "Restoring the parameter must recover the original result.");
                }
            }
        }
        finally { graphics.Release(); }
        foreach (var resource in OfficialResources)
            Assert.That(Hash(resources[resource.Path]), Is.EqualTo(resource.Sha256), "Original model, wrapper, material and DDS bytes must remain unchanged.");
    }

    private static VfxPreviewSnapshot Snapshot(float scale, Func<string, byte[]?> resolve)
    {
        // DIAG: isolate one real 22,508-byte official Comet model and its unchanged original dependencies.
        // This preserves the existing diffuse-only fallback; it is not a native-material support claim.
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", Wrapper);
        document.Root.Descendants("property").Single(property => (string?)property.Attribute("name") == "ParticleScale").Value =
            string.Join('|', new[] { "x", "y", "z" }.Select(channel => VfxLibraryVisibilityTests.Constant(channel, scale).Split(':')[0]))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
        return new(document.Write(), "vfx/official-comet-model-upload-DIAG.xml", resolve);
    }

    private static Vector4[] Render(GraphicsDevice device, EffekseerVfxPreview preview, CommonShaderParameters camera)
    {
        Assert.That(preview.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.ModelMaterialFallback" && diagnostic.Field == Shader), Is.True);
        Assert.That(preview.Diagnostics.Any(diagnostic => diagnostic.ReasonKey is "Vfx.Preview.RuntimeUnavailable" or "Vfx.Preview.TextureUnsupported"
            or "Vfx.Preview.MissingResource" or "Vfx.Preview.GeometryUnsupported" or "Vfx.Preview.MaterialUnsupported"), Is.False);
        using var target = new RenderTarget2D(device, 96, 96, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        try { preview.Draw(camera); }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[96 * 96];
        target.GetData(pixels);
        Assert.That(pixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .001f), Is.GreaterThan(10));
        return pixels;
    }

    private static EffekseerHandle Handle(EffekseerVfxPreview preview) => (EffekseerHandle)typeof(EffekseerVfxPreview)
        .GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static Dictionary<string, byte[]> ReadOfficialResources()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("The official game data and manifest are required for this original-model control.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(row => row.Length > 1)
            .ToDictionary(row => row[0], row => long.Parse(row[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in OfficialResources.GroupBy(resource => resource.Pack))
        {
            var packPath = Path.Combine(data!, group.Key);
            Assert.That(new FileInfo(packPath).Length, Is.EqualTo(manifest[group.Key]), "Only the manifest's original Pack is allowed.");
            var pack = new PackFileContainerLoader(null!).Load(packPath)!;
            foreach (var resource in group)
            {
                var bytes = pack.FileList.Single(pair => pair.Key.Replace('/', '\\').Equals(resource.Path, StringComparison.OrdinalIgnoreCase)).Value.DataSource.ReadData();
                Assert.That(Hash(bytes), Is.EqualTo(resource.Sha256));
                result.Add(resource.Path, bytes);
            }
        }
        return result;
    }
}
