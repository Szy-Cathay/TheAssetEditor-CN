#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxNativeMeshMaterialTests
{
    internal sealed record Fixture(string Path, byte[] Original, string Hash, float[] Parameters, string[] Textures, int Mode);
    internal static readonly Fixture[] Fixtures = ReadFixtures();
    public static IEnumerable<string> OriginalPaths => Fixtures.Select(f => f.Path);
    public static IEnumerable<string> ParameterPaths => Fixtures.Where(f => f.Textures.Length > 0).Select(f => f.Path);

    [TestCaseSource(nameof(OriginalPaths))]
    public void CompleteOfficialMaterial_MatchesFreshNativeReflectionAndKeepsItsOriginalBytes(string path)
    {
        var fixture = Fixtures.Single(f => f.Path == path); var original = (byte[])fixture.Original.Clone();
        var material = VfxScrollingMeshMaterial.Read(original);
        Assert.Multiple(() =>
        {
            Assert.That(material.Parameters, Is.EqualTo(fixture.Parameters), "Expected offsets come from official DXBC reflection, independent of the parser's field list.");
            Assert.That(material.TexturePaths, Is.EqualTo(fixture.Textures));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo(fixture.Hash));
            Assert.That(original, Is.EqualTo(fixture.Original));
        });
    }

    [TestCaseSource(nameof(OriginalPaths))]
    public void CompleteMaterial_ReachesTheActualModelConverterAndLayerPacket(string path)
    {
        var fixture = Fixtures.Single(f => f.Path == path);
        var source = VfxOpaqueModelMaterialTests.Source(Vector3.One, 1);
        var conversion = TotalWarVfxConverter.Convert(new(source, "DIAG/native-original-material.xml", resource =>
            resource.Replace('\\', '/') == "vfx/character.xml.material" ? fixture.Original
                : VfxOpaqueModelMaterialTests.Resolve(resource, VfxModelVariantPixelTests.Dds(Color.White))));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.MeshMaterials, Has.Count.EqualTo(1));
            Assert.That(conversion.MeshMaterials.Single().Parameters, Is.EqualTo(fixture.Parameters));
            Assert.That(conversion.Emitters.Single().Render.MeshMaterialIndex, Is.EqualTo(0));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey is "Vfx.Preview.ModelMaterialFallback" or "Vfx.Preview.MaterialUnsupported"), Is.False);
            if (fixture.Textures.Length == 0)
            {
                Assert.That(conversion.TexturePaths, Is.Empty);
                Assert.That(conversion.Emitters.Single().Render.TextureIndex, Is.EqualTo(-1));
                Assert.That(conversion.Emitters.Single().Render.OpaqueModel, Is.EqualTo(1));
            }
        });
    }

    [TestCaseSource(nameof(ParameterPaths))]
    public void MissingOrDuplicateParameter_RemainsAnExplicitMappingFailure(string path)
    {
        foreach (var duplicate in new[] { false, true })
        {
            var root = XElement.Parse(Encoding.UTF8.GetString(Fixtures.Single(f => f.Path == path).Original));
            var parameter = root.Element("params")!.Elements("param").First();
            if (duplicate) parameter.Parent!.Add(new XElement(parameter)); else parameter.Remove();
            Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        }
    }

    [TestCaseSource(nameof(ParameterPaths))]
    public void MissingTexture_IsNotReplacedWithAWhiteOrAnotherSlot(string path)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(Fixtures.Single(f => f.Path == path).Original));
        root.Element("textures")!.Elements("texture").First().Remove();
        var failure = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(failure!.Reason, Is.EqualTo("Vfx.Preview.MissingResource"));
    }

    [Test]
    public void RigidGlowMaterial_SchedulesBothTheColourAndNativeDistortionTechniques()
    {
        var fixture = Fixtures.Single(f => f.Mode == 4);
        var conversion = TotalWarVfxConverter.Convert(new(VfxModelVariantTests.Source(1), "DIAG/rigid-glow-passes.xml", VfxModelVariantTests.Resolve));
        conversion.MeshMaterials.Add(new([0,0,0], fixture.Parameters));
        var definition = conversion.Emitters.Single(); var layer = definition.Render;
        layer.MeshMaterialIndex = 0; definition.Render = layer;
        using var preview = new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_conversion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(preview, conversion);
        var built = preview.BuildRenderLayers([null]);
        Assert.Multiple(() =>
        {
            Assert.That(built.Layers, Has.Length.EqualTo(2));
            Assert.That(built.Layers[0].MeshMaterialIndex, Is.EqualTo(0));
            Assert.That(built.Layers[0].Distortion, Is.Zero);
            Assert.That(built.Layers[1].MeshMaterialIndex, Is.EqualTo(0));
            Assert.That(built.Layers[1].Distortion, Is.EqualTo(1));
            Assert.That(built.Layers[1].MaterialMode, Is.Zero);
            Assert.That(built.HalfLayers, Is.Empty);
            Assert.That(built.FullLayers, Is.Empty);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    public void NewColourModels_PreserveRawSourceAlphaAndKeepOrdinaryPartsClamped(int mode)
    {
        var fixture = Fixtures.First(f => f.Mode == mode);
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var emitterXml = source.Root.Descendants("emitter").Single();
        emitterXml.Add(new XElement("property", new XAttribute("name", "ParticleInitialAlpha"),
            "x_min,0,0;0,2.5,linear,linear,0,0,0,0,1:x,0;0,0"));
        var conversion = TotalWarVfxConverter.Convert(new(source.Write(), "DIAG/native-material-mixed.xml", VfxModelVariantTests.Resolve));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        conversion.MeshMaterials.Add(new(Enumerable.Repeat(0, fixture.Textures.Length).ToArray(), fixture.Parameters));
        var definition = conversion.Emitters.Single(); var ordinary = definition.Render;
        var native = ordinary; native.MeshMaterialIndex = 0;
        definition.AdditionalModelParts = [native];
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        var projected = simulation.ProjectedFrame(camera).ToArray();
        Assert.That(projected, Has.Length.EqualTo(1));
        Assert.That(projected[0].Render.Colour.W, Is.EqualTo(2.5f), "A later mapped part must preserve its native alpha input.");
        using var preview = new EffekseerVfxPreview(null!);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(EffekseerVfxPreview).GetField("_conversion", flags)!.SetValue(preview, conversion);
        var layers = preview.BuildRenderLayers([null]).Layers;
        var count = (int)typeof(EffekseerVfxPreview).GetMethod("PrepareSubmission", flags)!.Invoke(preview, [projected])!;
        var packets = (EffekseerParticle[])typeof(EffekseerVfxPreview).GetField("_submissionParticles", flags)!.GetValue(preview)!;
        Assert.That(count, Is.EqualTo(2));
        Assert.That(packets.Take(count).Single(p => layers[p.Layer].MeshMaterialIndex < 0).Colour.W, Is.EqualTo(1));
        Assert.That(packets.Take(count).Single(p => layers[p.Layer].MeshMaterialIndex == 0).Colour.W, Is.EqualTo(2.5f));
    }

    private static Fixture[] ReadFixtures()
    {
        using var stream = typeof(VfxNativeMeshMaterialTests).Assembly.GetManifestResourceStream("Test.GameWorld.Core.Vfx.Fixtures.NativeRejectedMeshMaterials.json")
            ?? typeof(VfxNativeMeshMaterialTests).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".NativeRejectedMeshMaterials.json", StringComparison.Ordinal))
                .Select(n => typeof(VfxNativeMeshMaterialTests).Assembly.GetManifestResourceStream(n)).Single()!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("materials").EnumerateArray().Select(r => new Fixture(
            r.GetProperty("path").GetString()!, Convert.FromBase64String(r.GetProperty("originalBase64").GetString()!),
            r.GetProperty("sha256").GetString()!, r.GetProperty("expectedParameters").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
            r.GetProperty("expectedTextures").EnumerateArray().Select(v => v.GetString()!).ToArray(), r.GetProperty("mode").GetInt32())).ToArray();
    }
}
