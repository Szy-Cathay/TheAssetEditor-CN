#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelAnimationTests
{
    [Test]
    public void AuthoredWeightedModelAndClip_LoadWithoutChangingTheSource()
    {
        var bytes = Source();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/animated.xml", Resolve));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
            Assert.That(conversion.Models, Has.Count.EqualTo(1));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void Poses_UseCanonicalInverseBindAndInterpolateTheAuthoredTracks()
    {
        var model = Convert().Models.Single();
        Assert.Multiple(() =>
        {
            Assert.That(model.AnimationFrameCount, Is.EqualTo(240));
            Assert.That(model.AnimationCycles, Is.EqualTo(1));
            Assert.That(model.Vertices[0].Position, Is.EqualTo(new Vector3(2, 1, 0)));
            Assert.That(model.AnimationVertices![60 * 3].Position.X, Is.EqualTo(2.5f).Within(.00001));
            Assert.That(model.AnimationVertices[120 * 3].Position.X, Is.EqualTo(3).Within(.00001));
            Assert.That(model.AnimationVertices.All(v => Math.Abs(v.Normal.Length() - 1) < .00001), Is.True);
        });
    }

    [TestCase(2)]
    [TestCase(4)]
    public void ClipFileDuration_DoesNotReplaceParticleLifePhase(float duration)
    {
        var altered = AnimationFile.Create(new Shared.Core.PackFiles.Models.PackFile("clip", new Shared.Core.PackFiles.Models.MemorySource(Animation(false))));
        altered.Header.AnimationTotalPlayTimeInSec = duration;
        altered.Header.FrameRate /= duration;
        var conversion = TotalWarVfxConverter.Convert(new(Source(), "vfx/animated.xml", path => path.Replace('\\', '/') == "animations/animated.anim"
            ? AnimationFile.ConvertToBytes(altered) : Resolve(path)));
        Assert.That(conversion.Models.Single().AnimationVertices![60 * 3].Position.X, Is.EqualTo(2.5f).Within(.00001));
    }

    [Test]
    public void NativeClipClock_UsesTheAuthoredFrameRateAndHoldsItsLastKey()
    {
        var animation = AnimationFile.Create(new Shared.Core.PackFiles.Models.PackFile("clip", new Shared.Core.PackFiles.Models.MemorySource(Animation(false))));
        var first = animation.AnimationParts[0].DynamicFrames[0];
        animation.AnimationParts[0].DynamicFrames = Enumerable.Range(0, 27).Select(index =>
        {
            var frame = new AnimationFile.Frame
            {
                Quaternion = first.Quaternion.ToList(),
                Transforms = first.Transforms.ToList(),
            };
            frame.Transforms[0] = new RmvVector3(2 + index, 0, 0);
            return frame;
        }).ToList();
        animation.Header.FrameRate = 20;
        animation.Header.AnimationTotalPlayTimeInSec = 1.3f;
        var conversion = TotalWarVfxConverter.Convert(new(Source(), "vfx/animated.xml", path => path.Replace('\\', '/') == "animations/animated.anim"
            ? AnimationFile.ConvertToBytes(animation) : Resolve(path)));
        Assert.That(conversion.Models.Single().AnimationVertices![120 * 3].Position.X, Is.EqualTo(15).Within(.00001),
            "The shipped 27-key / 20-fps / 1.3-second convention has 26 intervals, not 27.");

        var twoKeys = Convert().Models.Single();
        Assert.That(twoKeys.AnimationVertices![180 * 3].Position.X, Is.EqualTo(3).Within(.00001),
            "An authored duration beyond the final key holds that key instead of interpolating back to the first pose.");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void AnimationSpeed_IsRetainedAsCyclesAndZeroKeepsTheFirstPose(float speed)
    {
        var model = Convert(speed).Models.Single();
        Assert.That(model.AnimationCycles, Is.EqualTo(speed));
        Assert.That(model.AnimationVertices == null, Is.EqualTo(speed == 0));
    }

    [Test]
    public void InitialFraming_ContainsTheAnimatedPosesBeyondTheBindMesh()
    {
        var bounds = VfxPreviewBounds.Estimate(Convert());
        Assert.That(bounds!.Value.Max.X, Is.GreaterThanOrEqualTo(4));
    }

    [TestCase("animations/animated.anim")]
    [TestCase("animations/skeletons/vfx_test.anim")]
    public void MissingClipOrBindSkeleton_DiagnosesOnlyItsDependentLayer(string missing)
    {
        var conversion = TotalWarVfxConverter.Convert(new(Source(), "vfx/animated.xml", path => path.Replace('\\', '/') == missing ? null : Resolve(path)));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
    }

    [Test]
    public void CorruptAnimation_IsAFieldDiagnosticInsteadOfAnUnhandledLoadFailure()
    {
        var conversion = TotalWarVfxConverter.Convert(new(Source(), "vfx/animated.xml", path => path.Replace('\\', '/') == "animations/animated.anim" ? [] : Resolve(path)));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/Animation" && d.ReasonKey == "Vfx.Preview.InvalidData"), Is.True);
    }

    [Test]
    public void ExcessivePoseBudget_IsExplicitlyRejected()
    {
        var conversion = Convert(1, 31);
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/Animation" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
    }

    [Test]
    public void MultiplePlainModelParts_ShareTheParticleAndRetainAllAnimatedGeometry()
    {
        var first = ModelFactory.Create().Load(Model());
        var second = ModelFactory.Create().Load(Model());
        first.ModelList[0] = [first.ModelList[0][0], second.ModelList[0][0]];
        first.RecalculateOffsets();
        var bytes = ModelFactory.Create().Save(first);
        var conversion = TotalWarVfxConverter.Convert(new(Source(), "vfx/animated.xml", path => path.EndsWith(".rigid_model_v2") ? bytes : Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Emitters[0].RenderParts(0).Select(part => part.ModelIndex), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(conversion.Models.All(model => model.AnimationFrameCount == 240), Is.True);
    }

    [TestCase(2, 2f)]
    [TestCase(4, 1.8f)]
    public void Skinning_UsesTheImplicitComplementInsteadOfTheStoredPaddingWeight(int weights, float expectedY)
    {
        var file = ModelFactory.Create().Load(Model());
        if (weights == 4) ((WeightedMaterial)file.ModelList[0][0].Material).BinaryVertexFormat = VertexFormat.Cinematic;
        foreach (var vertex in file.ModelList[0][0].Mesh.VertexList)
        {
            vertex.BoneWeight = weights == 2 ? [.5f, 0] : [.2f, .3f, .1f, 0];
            if (weights == 4) { vertex.BoneIndex = [0, 0, 0, 1]; vertex.WeightCount = 4; }
        }
        file.RecalculateOffsets();
        var modelBytes = ModelFactory.Create().Save(file);
        var animation = AnimationFile.Create(new Shared.Core.PackFiles.Models.PackFile("clip", new Shared.Core.PackFiles.Models.MemorySource(Animation(false))));
        animation.AnimationParts[0].DynamicFrames[1].Transforms[1] = new RmvVector3(0, 3, 0);
        var conversion = TotalWarVfxConverter.Convert(new(Source(), "vfx/animated.xml", path => path.Replace('\\', '/') switch
        {
            "vfx/model-0.rigid_model_v2" => modelBytes,
            "animations/animated.anim" => AnimationFile.ConvertToBytes(animation),
            _ => Resolve(path),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Models.Single().AnimationVertices![120 * 3].Position.Y, Is.EqualTo(expectedY).Within(.02));
    }

    private static VfxConversion Convert(float speed = 1, float life = 4) => TotalWarVfxConverter.Convert(new(Source(speed, life), "vfx/animated.xml", Resolve));

    [Test]
    public void FailedAnimationLayer_ReleasesItsPoseBudgetBeforeTheNextLayerLoads()
    {
        var giant = ModelFactory.Create().Load(Model());
        var part = giant.ModelList[0][0];
        var originalVertices = part.Mesh.VertexList;
        part.Mesh.VertexList = Enumerable.Range(0, 4166).Select(index => originalVertices[index % 3]).ToArray();
        part.Material.SetTexture(TextureType.Diffuse, "vfx/missing.dds");
        giant.RecalculateOffsets();
        var giantBytes = ModelFactory.Create().Save(giant);
        var document = VfxDocument.Read(Source());
        var first = document.Layers.Single(layer => layer.Kind == "Emitter");
        document.DuplicateLayer(first).Apply();
        first.Element.Descendants("ModelFile").Single().SetAttributeValue("value", "vfx/budget.rigid_model_v2");
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "vfx/animated.xml", path => path.Replace('\\', '/') switch
        {
            "vfx/budget.rigid_model_v2" => giantBytes,
            "vfx/missing.dds" => null,
            _ => Resolve(path),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), "A missing texture in a previous layer must not consume the valid layer's animation budget.");
        Assert.That(conversion.Models, Has.Count.EqualTo(1));
        Assert.That(conversion.Models[0].AnimationVertices, Has.Length.EqualTo(720));
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
    }

    internal static byte[] Source(float speed = 1, float life = 4)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Single(p => (string?)p.Attribute("name") == "ParticleLifeTime").Value = Scalar(life);
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        var entry = document.Root.Descendants("rigid_entry").Single();
        entry.Element("AnimFile")!.SetAttributeValue("value", "animations/animated.anim");
        entry.Add(new XElement("AnimSpeed", new XAttribute("value", speed)));
        return document.Write();
    }

    internal static string Scalar(float value) => $"x_min,0,0;0,{value},linear,linear,-.05,0,.05,0,0|x_max,0,0;0,{value},linear,linear,-.05,0,.05,0,0:x,0;0,1";

    internal static byte[]? Resolve(string path) => path.Replace('\\', '/') switch
    {
        "vfx/model-0.rigid_model_v2" => Model(),
        "animations/animated.anim" => Animation(false),
        "animations/skeletons/vfx_test.anim" => Animation(true),
        _ when path.EndsWith(".dds") => VfxModelVariantPixelTests.Dds(Color.White),
        _ => null,
    };

    internal static byte[] Animation(bool bind)
    {
        var file = new AnimationFile
        {
            Header = new() { Version = 7, SkeletonName = "vfx_test", FrameRate = 2, AnimationTotalPlayTimeInSec = 1 },
            Bones = [new() { Id = 0, Name = "root", ParentId = -1 }, new() { Id = 1, Name = "child", ParentId = 0 }],
            AnimationParts = [new() { RotationMappings = [new(0), new(1)], TranslationMappings = [new(0), new(1)],
                DynamicFrames = [Frame(2), Frame(bind ? 2 : 3)] }],
        };
        return AnimationFile.ConvertToBytes(file);

        static AnimationFile.Frame Frame(float rootX) => new()
        {
            Transforms = [new RmvVector3(rootX, 0, 0), new RmvVector3(0, 1, 0)],
            Quaternion = [new RmvVector4(0, 0, 0, 1), new RmvVector4(0, 0, 0, 1)],
        };
    }

    internal static byte[] Model()
    {
        var version = RmvVersionEnum.RMV2_V8;
        var material = (WeightedMaterial)MaterialFactory.Create().CreateMaterial(ModelMaterialEnum.default_type);
        material.BinaryVertexFormat = VertexFormat.Weighted;
        material.ModelName = "animated";
        material.SetTexture(TextureType.Diffuse, "vfx/texture.dds");
        var header = RmvCommonHeader.CreateDefault(); header.ModelTypeFlag = ModelMaterialEnum.default_type;
        CommonVertex Vertex(float x, float y) => new()
        {
            Position = new(x, y, 0, 1), Normal = Vector3.Backward, Tangent = Vector3.Right, BiNormal = Vector3.Up,
            Uv = new(x, y), Colour = Vector4.One, BoneIndex = [1, 0], BoneWeight = [1, 0], WeightCount = 2,
        };
        var file = new RmvFile
        {
            Header = new() { _fileType = Encoding.ASCII.GetBytes("RMV2"), Version = version, LodCount = 1, SkeletonName = "vfx_test" },
            LodHeaders = [LodHeaderFactory.Create().CreateEmpty(version, 0, 0, 0)],
            ModelList = [[new RmvModel { CommonHeader = header, Material = material,
                Mesh = new() { VertexList = [Vertex(2, 1), Vertex(3, 1), Vertex(2, 2)], IndexList = [0, 1, 2] } }]],
        };
        file.RecalculateOffsets();
        return ModelFactory.Create().Save(file);
    }
}
