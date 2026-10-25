#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelVariantTests
{
    [TestCase(1)]
    [TestCase(2)]
    public void StaticModelChoices_LoadEveryGeometryAndTextureWithoutDuplicatingEmitters(int choices)
    {
        var bytes = Source(choices);
        var conversion = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(conversion.Models, Has.Count.EqualTo(choices));
            Assert.That(conversion.TexturePaths, Has.Count.EqualTo(choices));
            Assert.That(conversion.Models.Select(m => m.Vertices[0].Position.X), Is.EqualTo(Enumerable.Range(0, choices).Select(i => (float)i)));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void MissingAnimation_KeepsTheWholeLayerDiagnosedRatherThanDroppingThatChoice()
    {
        var document = VfxDocument.Read(Source(2));
        document.Root.Descendants("AnimFile").Last().SetAttributeValue("value", "animations/test.anim");
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/variants.xml",
            path => path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ? null : Resolve(path)));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
    }

    [Test]
    public void Choice_IsRetainedDuringLifeAndRebuiltDeterministicallyAfterSeeking()
    {
        var conversion = Convert(Source(2));
        var simulation = new TotalWarVfxSimulation(conversion);
        var camera = Camera;
        simulation.Seek(.1f, Matrix.Identity);
        var first = simulation.ProjectedFrame(camera).Single();
        simulation.Seek(.8f, Matrix.Identity);
        Assert.That(simulation.ProjectedFrame(camera).Single().ModelVariant, Is.EqualTo(first.ModelVariant));
        var choices = new List<int>();
        foreach (var time in Enumerable.Range(0, 24).Select(i => i + .2f))
        {
            simulation.Seek(time, Matrix.Identity);
            choices.Add(simulation.ProjectedFrame(camera).Single().ModelVariant);
        }
        Assert.That(choices.Distinct(), Is.EquivalentTo(new[] { 0, 1 }));
        simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.ProjectedFrame(camera).Single(), Is.EqualTo(first));
    }

    [Test]
    public void ReferenceComposition_OffsetsEveryChoiceAndKeepsOneParticlePerChild()
    {
        var first = VfxDocument.Read(Source(1)); var second = VfxDocument.Read(Source(2));
        var parent = VfxDocument.CreateComposition();
        parent.AddReference("first", first).Apply(); parent.AddReference("second", second).Apply();
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", path => path switch
        {
            @"vfx\first.xml" => first.Write(), @"vfx\second.xml" => second.Write(), _ => Resolve(path),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(conversion.Emitters[1].RenderParts(1).Single().ModelIndex, Is.EqualTo(2));
        Assert.That(conversion.Emitters[1].RenderParts(1).Single().TextureIndex, Is.EqualTo(2));
        Assert.That(conversion.Emitters[1].RenderParts(1).Single().MeshMaterialIndex, Is.EqualTo(-1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(2));
    }

    [Test]
    public void MissingChoice_DiagnosesTheWholeLayerInsteadOfOnlyLoadingTheFirstModel()
    {
        var conversion = TotalWarVfxConverter.Convert(new(Source(2), "vfx/variants.xml",
            path => path.EndsWith("model-1.rigid_model_v2") ? null : Resolve(path)));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
    }

    [Test]
    public void InitialFraming_CoversChoicesNotSelectedByTheFirstParticles()
    {
        var conversion = Convert(Source(32));
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.That(bounds, Is.Not.Null);
        Assert.That(bounds!.Value.Min.X, Is.LessThanOrEqualTo(0));
        Assert.That(bounds.Value.Max.X, Is.GreaterThanOrEqualTo(32));
    }

    [Test]
    public void ReferenceBudget_RejectsAnOverflowingChoiceGroupWithoutDisablingTheAlreadyLoadedGroups()
    {
        var child = VfxDocument.Read(Source(32)); var parent = VfxDocument.CreateComposition();
        foreach (var index in Enumerable.Range(0, 17)) parent.AddReference("child" + index, child).Apply();
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", path =>
            path.EndsWith(".xml") ? child.Write() : Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(16));
        Assert.That(conversion.Emitters.Sum(emitter => emitter.RenderLayerCount), Is.EqualTo(512));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "vfx_ref" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
    }

    private static CommonShaderParameters Camera => new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    internal static byte[] Source(int choices)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.SetAttributeValue("type", "continuous_rigid");
        emitter.Add(new XElement("property_rigid_data", new XAttribute("name", "RigidData"),
            Enumerable.Range(0, choices).Select(i => new XElement("rigid_entry",
                new XElement("ModelFile", new XAttribute("value", $"vfx/model-{i}.rigid_model_v2")),
                new XElement("PivotPoint", new XAttribute("x", .5), new XAttribute("y", .5), new XAttribute("z", .5)),
                new XElement("AnimFile", new XAttribute("value", ""))))));
        return document.Write();
    }

    internal static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/variants.xml", Resolve));

    internal static byte[]? Resolve(string path)
    {
        if (!path.EndsWith(".rigid_model_v2")) return [1];
        var index = int.Parse(Path.GetFileNameWithoutExtension(path.Replace('\\', '/')).Split('-')[1]);
        return Model(index);
    }

    internal static byte[] Model(int index)
    {
        var version = RmvVersionEnum.RMV2_V8;
        var material = (WeightedMaterial)MaterialFactory.Create().CreateMaterial(ModelMaterialEnum.default_type);
        material.BinaryVertexFormat = VertexFormat.Static;
        material.ModelName = "variant";
        material.SetTexture(TextureType.Diffuse, $"vfx/texture-{index}.dds");
        var header = RmvCommonHeader.CreateDefault(); header.ModelTypeFlag = ModelMaterialEnum.default_type;
        CommonVertex Vertex(float x, float y) => new()
        {
            Position = new(x + index, y, 0, 1), Normal = Vector3.Backward, Tangent = Vector3.Right, BiNormal = Vector3.Up,
            Uv = new(x, y), Colour = Vector4.One, BoneIndex = [], BoneWeight = [],
        };
        var file = new RmvFile
        {
            Header = new() { _fileType = Encoding.ASCII.GetBytes("RMV2"), Version = version, LodCount = 1, SkeletonName = "" },
            LodHeaders = [LodHeaderFactory.Create().CreateEmpty(version, 0, 0, 0)],
            ModelList = [[new RmvModel { CommonHeader = header, Material = material,
                Mesh = new() { VertexList = [Vertex(0, 0), Vertex(1, 0), Vertex(0, 1)], IndexList = [0, 1, 2] } }]],
        };
        file.RecalculateOffsets();
        return ModelFactory.Create().Save(file);
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxModelVariantPixelTests
{
    [Test]
    public void ActualPreview_RendersOnlyTheChosenModelsTextureForEachParticle()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(VfxModelVariantTests.Source(2), "vfx/variants.xml", path =>
                path.EndsWith(".dds") ? Dds(path.EndsWith("texture-0.dds") ? Color.Red : Color.Lime) : VfxModelVariantTests.Resolve(path)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
            using var target = new RenderTarget2D(device, 160, 100, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var position = new Vector3(1, .5f, 10);
            var camera = new CommonShaderParameters(Matrix.CreateLookAt(position, new(1, .5f, 0), Vector3.Up),
                Matrix.CreateOrthographic(4, 2.5f, .1f, 100), position, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            var choices = new List<string>();
            foreach (var time in Enumerable.Range(0, 16).Select(i => i + .2f))
            {
                preview.Seek(time, Matrix.Identity);
                device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(camera); device.SetRenderTarget(null);
                var pixels = new Color[160 * 100]; target.GetData(pixels);
                var red = pixels.Count(p => p.R > 100 && p.G < 30 && p.B < 30);
                var green = pixels.Count(p => p.G > 100 && p.R < 30 && p.B < 30);
                Assert.That((red > 20) ^ (green > 20), Is.True, $"At {time}s exactly one model must be visible: red={red}, green={green}");
                choices.Add(red > 20 ? "red" : "green");
            }
            Assert.That(choices.Distinct(), Is.EquivalentTo(new[] { "red", "green" }));
        }
        finally { graphics.Release(); }
    }

    internal static byte[] Dds(Color colour)
    {
        var bytes = new byte[128 + 4 * 4 * 4];
        void U(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        U(0, 0x20534444); U(4, 124); U(8, 0x100F); U(12, 4); U(16, 4); U(20, 16); U(28, 1);
        U(76, 32); U(80, 0x41); U(88, 32); U(92, 255); U(96, 65280); U(100, 16711680); U(104, 4278190080); U(108, 4096);
        for (var i = 128; i < bytes.Length; i += 4) { bytes[i] = colour.R; bytes[i + 1] = colour.G; bytes[i + 2] = colour.B; bytes[i + 3] = 255; }
        return bytes;
    }
}
