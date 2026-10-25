#nullable enable
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxVertexAnimatedMeshTests
{
    private const string Beam = @"vfx\wh2_dlc17_entity_beam.xml";
    private const string RegimentBeam = @"vfx\wh2_dlc17_entity_beam_ror.xml";
    private const string MaterialPath = @"vfx\materials\wh2_dlc17_coatl_beam.xml.material";
    private const string GeometryPath = @"vfx\models\heirotitan_laser.rigid_model_v2";
    private const string ShaderPath = "shaders/wh_vfx_mesh_vertex_anim_battle.xml.shader";
    private readonly VfxOfficialDistortionPixelTests _official = new();

    [OneTimeSetUp]
    public void RequireCurrentOfficialResources() => _official.LoadCurrentOfficialResourcesOnly();

    [Test]
    public void CompleteOfficialBeam_RetainsTheOriginalVertexAnimatedModelAndAllMaterialInputs()
        => AssertCompleteOfficialBeam(Beam);

    [Test]
    public void CompleteOfficialRegimentBeam_RetainsTheOriginalVertexAnimatedModelAndAllMaterialInputs()
        => AssertCompleteOfficialBeam(RegimentBeam);

    [Test]
    public void OfficialMaterialFogControl_IsPreservedWithoutMisreadingItsNativeDirection()
    {
        var bytes = Read(MaterialPath) ?? throw new FileNotFoundException(MaterialPath);
        using var stream = new MemoryStream(bytes);
        var root = XElement.Load(stream);
        root.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == "fog")
            .Element("value")!.Value = "1";
        var derived = Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        var material = VfxScrollingMeshMaterial.Read(derived);
        Assert.Multiple(() =>
        {
            Assert.That(material.Parameters, Has.Length.EqualTo(64));
            Assert.That(material.Parameters[18], Is.EqualTo(1), "The original PS uses one for the unfogged base; zero selects fog, not the other way round.");
        });
        TestContext.Progress.WriteLine("DIAG: only fog=1 in the current official beam material; control preserved, native scene fog remains diagnosed rather than silently claimed implemented.");
    }

    private void AssertCompleteOfficialBeam(string path)
    {
        var bytes = Read(path) ?? throw new FileNotFoundException(path);
        var sourceHash = Hash(bytes);
        var conversion = TotalWarVfxConverter.Convert(new(bytes, path, Read));
        var beams = conversion.Emitters.Where(emitter => emitter.Name == "Beam").ToArray();
        Assert.That(beams, Has.Length.EqualTo(1),
            "The complete official reference must retain its unique main beam, not only the surrounding lightning; diagnostics: "
            + JsonSerializer.Serialize(conversion.Diagnostics));
        var emitter = beams.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Kind, Is.EqualTo(1), "The authored beam is a real model, not a replacement sprite.");
            Assert.That(emitter.Render.ModelIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(emitter.Render.MeshMaterialIndex, Is.GreaterThanOrEqualTo(0), "A static diffuse fallback is insufficient.");
            Assert.That(conversion.Diagnostics.Where(item => item.Layer == "Beam"
                && item.ReasonKey is "Vfx.Preview.MaterialUnsupported" or "Vfx.Preview.GeometryUnsupported" or "Vfx.Preview.MissingResource"), Is.Empty);
        });
        var binding = conversion.MeshMaterials[emitter.Render.MeshMaterialIndex];
        var materialBytes = Read(MaterialPath) ?? throw new FileNotFoundException(MaterialPath);
        using var materialStream = new MemoryStream(materialBytes);
        var materialRoot = XElement.Load(materialStream);
        var authoredParameters = materialRoot.Elements("params").Elements("param").SelectMany(node =>
            node.Element("value")!.Value.Split(',').Select(value => float.Parse(value, CultureInfo.InvariantCulture))).ToArray();
        var slots = new[] { "Texture_A", "Texture_B", "Texture_C", "colour_overlay", "global_mask", "vertex_disp_texture" };
        var authoredTextures = slots.Select(slot => materialRoot.Elements("textures").Elements("texture")
            .Single(node => node.Element("slot")!.Value == "t_xml_" + slot).Element("source")!.Value).ToArray();
        var parsedMaterial = VfxScrollingMeshMaterial.Read(materialBytes);
        Assert.Multiple(() =>
        {
            Assert.That(materialRoot.Element("shader")!.Value, Is.EqualTo(ShaderPath));
            Assert.That(materialRoot.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == "vertex_anim").Element("value")!.Value, Is.EqualTo("1"));
            Assert.That(materialRoot.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == "vertex_colour").Element("value")!.Value, Is.EqualTo("0"));
            Assert.That(authoredParameters, Has.Length.EqualTo(64), "The official AUTO_PARAMETERS layout has 256 bytes.");
            Assert.That(binding.Parameters, Is.EqualTo(authoredParameters), "All source parameters must retain their original order and values.");
            Assert.That(binding.TextureIndexes, Has.Length.EqualTo(6), "Keep all six logical slots even when the original DDS is shared.");
            Assert.That(binding.TextureIndexes.Select(index => Normalize(conversion.TexturePaths[index])),
                Is.EqualTo(authoredTextures.Select(Normalize)));
            Assert.That(parsedMaterial.UsesVertexColour, Is.True, "vertex_anim uses the original red channel even when vertex_colour=0.");
        });
        var original = ModelFactory.Create().Load(Read(GeometryPath) ?? throw new FileNotFoundException(GeometryPath)).ModelList[0].Single();
        var model = conversion.Models[emitter.Render.ModelIndex];
        var originalColours = original.Mesh.VertexList.Select(vertex => vertex.UnormColour).ToArray();
        var authoredPivot = (original.Material.PivotPoint + new Vector3(.5f)) - new Vector3(.5f);
        Assert.Multiple(() =>
        {
            Assert.That(originalColours.All(colour => colour.HasValue), Is.True);
            Assert.That(originalColours.Any(colour => colour is { } value && value.X < 1), Is.True,
                "The official mesh must exercise the red-channel animation mask; an all-white replacement cannot satisfy this gate.");
            Assert.That(model.Vertices, Has.Length.EqualTo(original.Mesh.VertexList.Length));
            Assert.That(model.Vertices.Select(vertex => vertex.Position), Is.EqualTo(original.Mesh.VertexList.Select(vertex =>
                new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z) + authoredPivot)));
            Assert.That(model.Vertices.Select(vertex => vertex.UV), Is.EqualTo(original.Mesh.VertexList.Select(vertex => vertex.Uv)));
            Assert.That(model.Indices, Is.EqualTo(original.Mesh.IndexList.Select(index => (int)index)));
            Assert.That(model.Vertices.Select(vertex => vertex.Colour), Is.EqualTo(originalColours.Select(colour => colour ?? Vector4.One)));
            Assert.That(model.ApproximateMaterialShader, Is.Null, "The dedicated material must not silently fall back to static shading.");
            Assert.That(Hash(bytes), Is.EqualTo(sourceHash));
        });
        TestContext.Progress.WriteLine(JsonSerializer.Serialize(new { Source = path, Sha256 = sourceHash,
            Material = MaterialPath, MaterialSha256 = Hash(materialBytes), Geometry = GeometryPath,
            Parameters = binding.Parameters.Length, Textures = binding.TextureIndexes.Length, Vertices = model.Vertices.Length,
            Scope = "CPU conversion of the complete current official reference and original dependencies; no native visual-equivalence claim." }));
    }

    private byte[]? Read(string path) => (byte[]?)typeof(VfxOfficialDistortionPixelTests)
        .GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_official, [path]);

    private static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxVertexAnimatedMeshPixelTests
{
    private const int Size = 256;
    private readonly VfxOfficialDistortionPixelTests _official = new();

    [OneTimeSetUp]
    public void RequireOfficialOffscreenResources() => _official.LoadCurrentOfficialResourcesOnly();

    [TestCase(@"vfx\wh2_dlc17_entity_beam.xml")]
    [TestCase(@"vfx\wh2_dlc17_entity_beam_ror.xml")]
    public void CompleteOfficialEffect_ReceivesPixelContributionFromItsActualMainBeam(string path)
    {
        var source = Read(path) ?? throw new FileNotFoundException(path);
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var complete = new EffekseerVfxPreview(graphics.GraphicsDevice);
            using var omitted = new EffekseerVfxPreview(graphics.GraphicsDevice);
            complete.Reload(new(source, path, Read));
            omitted.Reload(new(source, path, Read));
            OmitOnlyBeamDraws(omitted);
            CheckDiagnostics(complete);
            CheckDiagnostics(omitted);
            var eye = new Vector3(25, 20, 40);
            var target = new Vector3(0, 3, 0);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, target, Vector3.Up),
                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 25000) * Matrix.CreateScale(-1, 1, 1),
                eye, target, 0, 0, 0, 1, Vector3.One, []);
            var times = new[] { .3f, .6f, 1.2f };
            var changed = new List<int>();
            var name = path.Replace('\\', '/').Split('/').Last().Replace(".xml", "");
            foreach (var time in times)
            {
                complete.Seek(time, Matrix.Identity);
                omitted.Seek(time, Matrix.Identity);
                var stamp = time.ToString("F1", CultureInfo.InvariantCulture);
                var actual = Draw(graphics.GraphicsDevice, complete, camera, $"vertex-anim-{name}-complete-{stamp}");
                var control = Draw(graphics.GraphicsDevice, omitted, camera, $"vertex-anim-{name}-omit-beam-{stamp}");
                changed.Add(actual.Zip(control).Count(pair => Vector3.DistanceSquared(new(pair.First.X, pair.First.Y, pair.First.Z),
                    new(pair.Second.X, pair.Second.Y, pair.Second.Z)) > .00000001f));
                CheckDiagnostics(complete);
                CheckDiagnostics(omitted);
            }
            Assert.That(changed.Max(), Is.GreaterThan(2),
                "The actual original Beam must contribute pixels to the complete effect; unrelated lightning cannot satisfy this gate.");
            Evidence($"vertex-anim-{name}-contribution.json", new { Source = path,
                Sha256 = Convert.ToHexString(SHA256.HashData(source)), Times = times, ChangedPixels = changed,
                Eye = new[] { eye.X, eye.Y, eye.Z }, Target = new[] { target.X, target.Y, target.Z },
                Scope = "Complete official source versus DIAG suppressing only Beam draw IDs; source, models, materials, DDS and simulation preserved. Product-only contribution gate; no matched native camera or visual-equivalence claim." });
        }
        finally { graphics.Release(); }
    }

    [TestCase("vertex_anim", "0", false)]
    [TestCase("vertex_displacement", "1", true)]
    public void OfficialModelControls_PreserveZeroRedOscillationAndReachTheVertexDisplacementBinding(string field, string value, bool expectDifference)
    {
        const string path = @"vfx\wh2_dlc17_entity_beam.xml";
        var source = Read(path) ?? throw new FileNotFoundException(path);
        var changedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        byte[]? ResolveControl(string resource)
        {
            var bytes = Read(resource);
            if (bytes == null || !resource.EndsWith(".material", StringComparison.OrdinalIgnoreCase)) return bytes;
            using var stream = new MemoryStream(bytes);
            var root = XElement.Load(stream);
            if (root.Element("shader")?.Value.Replace('\\', '/') != "shaders/wh_vfx_mesh_vertex_anim_battle.xml.shader") return bytes;
            root.Elements("params").Elements("param").Single(node => node.Element("name")!.Value == field).Element("value")!.Value = value;
            changedMaterials.Add(resource);
            return Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        }
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var original = new EffekseerVfxPreview(graphics.GraphicsDevice);
            using var control = new EffekseerVfxPreview(graphics.GraphicsDevice);
            original.Reload(new(source, path, Read)); control.Reload(new(source, path, ResolveControl));
            Assert.That(changedMaterials, Has.Count.EqualTo(1));
            OmitOnlyBeamDraws(original, true); OmitOnlyBeamDraws(control, true);
            original.Seek(.6f, Matrix.Identity); control.Seek(.6f, Matrix.Identity);
            var eye = new Vector3(25, 20, 40); var target = new Vector3(0, 3, 0);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(eye, target, Vector3.Up),
                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 25000) * Matrix.CreateScale(-1, 1, 1),
                eye, target, 0, 0, 0, 1, Vector3.One, []);
            var before = Draw(graphics.GraphicsDevice, original, camera, $"vertex-anim-control-{field}-original");
            var after = Draw(graphics.GraphicsDevice, control, camera, $"vertex-anim-control-{field}-modified");
            var differences = before.Zip(after).Select(pair => Vector4.DistanceSquared(pair.First, pair.Second)).ToArray();
            var changedPixels = differences.Count(difference => difference > .00000001f);
            Assert.That(before.Any(pixel => pixel.X + pixel.Y + pixel.Z > .0001f), Is.True, "The original Beam must actually be visible.");
            if (expectDifference) Assert.That(changedPixels, Is.GreaterThan(2), "Original mip0 displacement must reach the VS/PS material path.");
            else Assert.That(differences.Max(), Is.LessThanOrEqualTo(.00000001f), "Zero original model red must keep oscillation at zero even when vertex_anim=1.");
            CheckDiagnostics(original); CheckDiagnostics(control);
            Evidence($"vertex-anim-control-{field}.json", new { Source = path, Field = field, Value = value, ChangedPixels = changedPixels,
                Scope = "DIAG: one authored material parameter changed, only original Beam draw retained; original model, zero vertex colours, DDS, particles and time preserved. Not a native visual-equivalence result." });
        }
        finally { graphics.Release(); }
    }

    private static void OmitOnlyBeamDraws(EffekseerVfxPreview preview, bool keepBeam = false)
    {
        var conversion = (VfxConversion)typeof(EffekseerVfxPreview).GetField("_conversion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        var matches = conversion.Emitters.Select((emitter, index) => (emitter, index)).Where(item => item.emitter.Name == "Beam").ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), "The draw-only control must find the original main Beam.");
        var beam = matches.Single();
        Assert.That(beam.emitter.Render.MeshMaterialIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(conversion.MeshMaterials[beam.emitter.Render.MeshMaterialIndex].Parameters, Has.Length.EqualTo(64));
        var layers = (Array)typeof(EffekseerVfxPreview).GetField("_renderLayers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        for (var index = 0; index < layers.Length; ++index)
            if ((index == beam.index) != keepBeam)
                foreach (var variant in (Array)layers.GetValue(index)!)
                    foreach (var name in new[] { "Coverage", "Emissive" })
                        Array.Fill((int[])variant!.GetType().GetProperty(name)!.GetValue(variant)!, -1);
    }

    private static Vector4[] Draw(GraphicsDevice device, EffekseerVfxPreview preview, CommonShaderParameters camera, string name)
    {
        using var surface = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(surface);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        preview.Draw(camera);
        device.SetRenderTarget(null);
        var pixels = new Vector4[Size * Size];
        surface.GetData(pixels);
        Assert.That(pixels.All(pixel => float.IsFinite(pixel.X) && float.IsFinite(pixel.Y)
            && float.IsFinite(pixel.Z) && float.IsFinite(pixel.W)), Is.True);
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var image = new Texture2D(device, Size, Size);
            image.SetData(pixels.Select(pixel => new Color(pixel)).ToArray());
            using var stream = new FileStream(Path.Combine(output, name + ".png"), FileMode.CreateNew);
            image.SaveAsPng(stream, Size, Size);
        }
        return pixels;
    }

    private static void CheckDiagnostics(EffekseerVfxPreview preview) => Assert.That(preview.Diagnostics.Where(diagnostic =>
        diagnostic.ReasonKey is "Vfx.Preview.RuntimeUnavailable" or "Vfx.Preview.TextureUnsupported"
        || diagnostic.Field.Contains("wh_vfx_mesh_vertex_anim_battle", StringComparison.OrdinalIgnoreCase)
            && diagnostic.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.Empty);

    private byte[]? Read(string path) => (byte[]?)typeof(VfxOfficialDistortionPixelTests)
        .GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_official, [path]);

    private static void Evidence(string name, object value)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }
}
