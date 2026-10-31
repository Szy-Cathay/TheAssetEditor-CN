#nullable enable
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxGemstoneOpaqueSceneTests
{
    private const string OriginalWrapper = @"vfx\models\wh3_dlc24_jadegem_1.wsmodel";
    private const string OriginalGeometry = @"vfx\models\dlc23_vfx_gem_1.rigid_model_v2";
    private const string OriginalMaterial = @"materials\vfx\wh3_dlc24_jadestone_gem.xml.material";
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(5, 5, .1f, 100), new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(1)]
    [TestCase(4)]
    public void EveryGemstoneVariantIsClassifiedAsOpaqueCutout(int choices)
    {
        var source = VfxDocument.Read(VfxModelVariantTests.Source(choices));
        foreach (var file in source.Root.Descendants("ModelFile")) file.SetAttributeValue("value", file.Attribute("value")!.Value.Replace(".rigid_model_v2", ".wsmodel"));
        var xml = VfxGemstoneMaterialTests.Material();
        xml.AddFirst(new XElement("name", "diagnostic_gemstone"));
        foreach (var parameter in xml.Element("params")!.Elements("param"))
        {
            var components = parameter.Element("value")!.Value.Split(',').Length;
            parameter.Add(new XElement("type", components == 1 ? "float" : "float" + components));
        }
        var material = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        byte[]? Resolve(string path)
        {
            if (path.EndsWith(".wsmodel")) return Encoding.UTF8.GetBytes($"<model><geometry>{path.Replace(".wsmodel", ".rigid_model_v2")}</geometry><materials><material lod_index=\"0\" part_index=\"0\">diagnostic/gem.material</material></materials></model>");
            return path.EndsWith(".material") ? material : VfxModelVariantTests.Resolve(path);
        }
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "diagnostic/gemstone-variants.xml", Resolve));
        Assert.That(conversion.Models, Has.Count.EqualTo(choices));
        Assert.That(conversion.Models.All(model => model.OpaqueMaterial), Is.True);
        Assert.That(conversion.Emitters.SelectMany(emitter => Enumerable.Range(0, emitter.ModelVariants.Count + 1).SelectMany(emitter.RenderParts))
            .All(part => part.OpaqueModel == 1), Is.True);
    }

    [Test]
    public void CompleteOfficialCircleClassifiesEveryCrystalPartAsOpaqueWithoutEditingAssets()
    {
        var original = new Originals();
        const string root = @"vfx\wh3_dlc24_cth_jade_dragon_human_circle_attk.xml";
        var bytes = original.Read(root)!;
        Assert.That(Hash(bytes), Is.EqualTo("211c6b5d32c1b6fd7523a6e222b4ad32c72c95642caf3a2239abfe92ff5d7f0b"));
        var conversion = TotalWarVfxConverter.Convert(new(bytes, root, original.Read));
        var parts = conversion.Emitters.SelectMany(emitter => Enumerable.Range(0, emitter.ModelVariants.Count + 1).SelectMany(emitter.RenderParts))
            .Where(part => part.MeshMaterialIndex >= 0 && conversion.MeshMaterials[part.MeshMaterialIndex].Parameters.Length == 40).ToArray();
        Assert.That(parts.Length, Is.GreaterThanOrEqualTo(4));
        Assert.That(parts.All(part => part.OpaqueModel == 1), Is.True);
        Assert.That(original.Read(root), Is.EqualTo(bytes));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FrontTransparentSpriteRemainsOverTheLaterOfficialCrystal(bool shared)
    {
        RequireGpu(); var original = new Originals();
        var gem = GemSource(); var sprite = SpriteSource();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            byte[]? Resolve(string path) => path.Replace('/', '\\') switch
            {
                @"vfx\front.xml" => sprite, @"vfx\gem.xml" => gem,
                @"vfx\texture.dds" => VfxModelVariantPixelTests.Dds(Color.White), _ => original.Read(path),
            };
            using var crystal = Preview(device, gem, Resolve, Matrix.Identity);
            using var front = Preview(device, sprite, Resolve, Matrix.CreateTranslation(0, 0, 2));
            var basePixels = Render(device, () => crystal.Draw(Camera));
            var frontPixels = Render(device, () => front.Draw(Camera));
            Vector4[] combined;
            if (shared)
                combined = Render(device, () => { using var scene = crystal.BeginScene(Camera); front.Draw(Camera, scene); crystal.Draw(Camera, scene); });
            else
            {
                var parent = VfxDocument.CreateComposition();
                var first = parent.AddReference("front", VfxDocument.Read(sprite)); first.Apply(); first.Selection.SetAttributeValue("pos_z", 2);
                parent.AddReference("gem", VfxDocument.Read(gem)).Apply();
                using var both = Preview(device, parent.Write(), Resolve, Matrix.Identity);
                combined = Render(device, () => both.Draw(Camera));
            }
            var checkedPixels = 0;
            for (var index = 0; index < combined.Length; index++)
            {
                var gemRgb = new Vector3(basePixels[index].X, basePixels[index].Y, basePixels[index].Z);
                if (gemRgb.LengthSquared() < .001f) continue;
                // Native Colour rounds the authored0.5 alpha to128/255 before blending.
                const float coverage = 128f / 255;
                Assert.That(frontPixels[index].X, Is.EqualTo(coverage).Within(.00002f));
                Assert.That(frontPixels[index].Y, Is.Zero); Assert.That(frontPixels[index].Z, Is.Zero);
                var expected = gemRgb * (1 - coverage) + new Vector3(frontPixels[index].X, frontPixels[index].Y, frontPixels[index].Z);
                var actual = new Vector3(combined[index].X, combined[index].Y, combined[index].Z);
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.001f), $"DIAG front-alpha blend at pixel{index}; original crystal geometry/material/textures retained.");
                checkedPixels++;
            }
            Assert.That(checkedPixels, Is.GreaterThan(20));
        }
        finally { graphics.Release(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OfficialTriangleBackFaceIsCulledAndProjectionReflectionKeepsItsVisibleSide(bool mirrored)
    {
        RequireGpu(); var original = new Originals(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var model = ModelFactory.Create().Load(original.Read(OriginalGeometry)!);
            var mesh = model.ModelList[0][0].Mesh;
            var source = mesh.IndexList.Take(3).Select(index => mesh.VertexList[index]).ToArray();
            var a = new Vector3(source[0].Position.X, source[0].Position.Y, source[0].Position.Z);
            var b = new Vector3(source[1].Position.X, source[1].Position.Y, source[1].Position.Z);
            var c = new Vector3(source[2].Position.X, source[2].Position.Y, source[2].Position.Z);
            var u = Vector3.Normalize(b - a); var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a)); var v = Vector3.Cross(normal, u);
            var positions = source.Select(vertex => new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z)).ToArray();
            var planar = positions.Select(position => new Vector2(Vector3.Dot(position - a, u), Vector3.Dot(position - a, v))).ToArray();
            var centre = (planar[0] + planar[1] + planar[2]) / 3;
            var size = planar.Max(position => Vector2.Distance(position, centre));
            var vertices = source.Select((vertex, index) => new EffekseerVertex { Position = new((planar[index] - centre) / size, 0),
                Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = vertex.Uv, Colour = Vector4.One }).ToArray();
            var front = TrianglePixels(graphics.GraphicsDevice, original, vertices, [0, 1, 2], mirrored);
            var back = TrianglePixels(graphics.GraphicsDevice, original, vertices, [0, 2, 1], mirrored);
            Assert.That(front, Is.GreaterThan(20)); Assert.That(back, Is.Zero);
        }
        finally { graphics.Release(); }
    }

    private static int TrianglePixels(GraphicsDevice device, Originals original, EffekseerVertex[] vertices, int[] indices, bool mirrored)
    {
        var direct = (SharpDX.Direct3D11.Device)device.Handle;
        using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
        var material = VfxScrollingMeshMaterial.Read(original.Read(OriginalMaterial)!);
        for (var index = 0; index < material.TexturePaths.Length; index++)
        {
            var bytes = original.Read(material.TexturePaths[index])!;
            var image = ((byte[] Pixels, int Width, int Height, bool Srgb, int MipLevels))typeof(EffekseerVfxPreview)
                .GetMethod("DecodeTexture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [bytes, VfxTextureUsage.MaterialValue])!;
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgbaMipChain(preview, image.Pixels, image.Pixels.Length,
                image.Width, image.Height, image.Srgb ? 1 : 0, image.MipLevels));
        }
        EffekseerNative.Check(EffekseerNative.AecnVfxMeshMaterial(preview, [0, 1, 2, 3, 4, 5], 6, material.Parameters, material.Parameters.Length));
        EffekseerNative.Check(EffekseerNative.AecnVfxModel(preview, vertices, vertices.Length, indices, indices.Length));
        var layer = EffekseerLayer.Create(); layer.Kind = 1; layer.ModelIndex = 0; layer.MeshMaterialIndex = 0; layer.OpaqueModel = 1;
        EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
        var projection = mirrored ? Camera.Projection * Matrix.CreateScale(-1, 1, 1) : Camera.Projection;
        var pixels = Render(device, () => EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(Camera.View), Values(projection),
            [new EffekseerParticle { Transform = Matrix.Identity, Colour = Vector4.One, UV = new(0, 0, 1, 1) }], 1)));
        return pixels.Count(pixel => new Vector3(pixel.X, pixel.Y, pixel.Z).LengthSquared() > .001f);
    }

    private static byte[] GemSource()
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        document.Root.Descendants("ModelFile").Single().SetAttributeValue("value", OriginalWrapper);
        var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "MaximumParticles", "1", true); Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 10));
        return document.Write();
    }
    private static byte[] SpriteSource()
    {
        var doc = VfxDocument.Read(VfxLibraryVisibilityTests.Source($"<modifier fragment=\"vs_colour\"><property name=\"curve_colour\">{Curve(("r", 1), ("g", 0), ("b", 0), ("brightness", 1))}</property></modifier><modifier fragment=\"vs_vertex_alpha\"><property name=\"curve_alpha\">{Curve(("alpha", .5f))}</property></modifier>"));
        var emitter = doc.Root.Descendants("emitter").Single(); Set(emitter, "MaximumParticles", "1", true);
        Set(emitter, "ParticleScale", Curve(("x", 8), ("y", 8), ("z", 1)));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 10));
        return doc.Write();
    }
    private static string Curve(params (string Name, float Value)[] values) => string.Join('|', values.Select(value => VfxLibraryVisibilityTests.Constant(value.Name, value.Value).Split(':')[0]))
        + ":" + string.Join('|', values.Select((value, index) => $"{value.Name},0;{index},{index}"));
    private static void Set(XElement emitter, string name, string value, bool attribute = false)
    {
        emitter.Elements().Where(node => (string?)node.Attribute("name") == name).Remove();
        emitter.Add(attribute ? new XElement("property", new XAttribute("name", name), new XAttribute("value", value)) : new XElement("property", new XAttribute("name", name), value));
    }
    private static EffekseerVfxPreview Preview(GraphicsDevice device, byte[] source, Func<string, byte[]?> resolve, Matrix attachment)
    { var preview = new EffekseerVfxPreview(device); preview.Reload(new(source, "diagnostic/official-gem-opaque.xml", resolve)); preview.Seek(.1f, attachment); return preview; }
    private static Vector4[] Render(GraphicsDevice device, Action draw)
    {
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0); draw(); device.SetRenderTarget(null);
        var pixels = new Vector4[128 * 128]; target.GetData(pixels); return pixels;
    }
    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void RequireGpu() { if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen GPU is opt-in; no desktop input."); }

    private sealed class Originals
    {
        private readonly Dictionary<string, PackFile> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);
        internal Originals()
        {
            var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
            if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original official game data is required.");
            var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(parts => parts.Length > 1)
                .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "commontextures.pack", "data.pack" })
            {
                var path = Path.Combine(data!, name); Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
                foreach (var file in new PackFileContainerLoader(null!).Load(path)!.FileList) _files[file.Key.Replace('/', '\\')] = file.Value;
            }
            Assert.That(Hash(Read(OriginalGeometry)!), Is.EqualTo("74dfe16d8ae2ec4dfa77c5575cc89d31531eb5b19995b0b1b8e073a527ab6af7"));
            Assert.That(Hash(Read(OriginalMaterial)!), Is.EqualTo("f7efec011b26d02833949b92a00918f780f9c2a190d1243ccc18c43311c8dfb4"));
        }
        internal byte[]? Read(string path)
        {
            path = path.Replace('/', '\\'); if (_bytes.TryGetValue(path, out var cached)) return cached;
            return _files.TryGetValue(path, out var file) ? _bytes[path] = file.DataSource.ReadData() : null;
        }
    }
}
