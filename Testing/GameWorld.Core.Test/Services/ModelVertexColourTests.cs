using System.Runtime.InteropServices;
using GameWorld.Core.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.GameFormats.RigidModel.Vertex.Formats;

namespace GameWorld.Core.Test.Services;

public class ModelVertexColourTests
{
    [TestCase(2, 0)]
    [TestCase(2, 64)]
    [TestCase(2, 128)]
    [TestCase(2, 255)]
    [TestCase(4, 0)]
    [TestCase(4, 64)]
    [TestCase(4, 128)]
    [TestCase(4, 255)]
    public void WeightedV8_OriginalUnormRgbaReachesTheIndependentGpuColourChannel(int weights, int alpha)
    {
        var creator = Creator(weights);
        var size = (int)creator.GetVertexSize(RmvVersionEnum.RMV2_V8);
        const int offset = 7;
        var bytes = new byte[offset + size];
        var weightBytes = weights == 2 ? new byte[] { 64, 191 } : new byte[] { 32, 64, 96, 63 };
        var expectedWeights = weights == 2 ? new Vector4(64 / 255f, 191 / 255f, 0, 0) :
            new Vector4(32 / 255f, 64 / 255f, 96 / 255f, 63 / 255f);
        for (var index = 0; index < weights; index++)
        {
            bytes[offset + 8 + index] = (byte)(3 + index * 4);
            bytes[offset + 8 + weights + index] = weightBytes[index];
        }
        var colourOffset = offset + size - 4;
        bytes[colourOffset] = 17;
        bytes[colourOffset + 1] = 88;
        bytes[colourOffset + 2] = 170;
        bytes[colourOffset + 3] = (byte)alpha;
        var original = bytes.ToArray();

        var vertex = creator.ReadArray(RmvVersionEnum.RMV2_V8, bytes, offset, size, 1).Single();
        var expected = new Vector4(17, 88, 170, alpha) / 255f;
        var oldSignedColour = new Vector4(17 / 255f * 2 - 1, 88 / 255f * 2 - 1, 170 / 255f * 2 - 1, alpha / 255f * 2 - 1);
        if (oldSignedColour.W > 0)
        {
            oldSignedColour.X *= oldSignedColour.W;
            oldSignedColour.Y *= oldSignedColour.W;
            oldSignedColour.Z *= oldSignedColour.W;
        }
        var contexts = new TestGeometryGraphicsContextFactory();
        using var mesh = new MeshBuilderService(contexts).BuildMeshFromRmvModel(Model(vertex, weights), "test");

        Assert.That(vertex.UnormColour, Is.EqualTo(expected));
        Assert.That(vertex.Colour, Is.EqualTo(oldSignedColour), "The legacy signed colour must not change.");
        var expectedColour = new Color(17, 88, 170, alpha);
        Assert.That(mesh.VertexArray[0].VertexColour, Is.EqualTo(expectedColour));
        Assert.That(mesh.VertexArray[0].VertexColour.ToVector4(), Is.EqualTo(expectedColour.ToVector4()));
        var uploadedColour = contexts.CreatedContexts.Single().UploadedVertexArray[0].VertexColour;
        Assert.That(new[] { uploadedColour.R, uploadedColour.G, uploadedColour.B, uploadedColour.A },
            Is.EqualTo(new byte[] { 17, 88, 170, (byte)alpha }));
        Assert.That(contexts.CreatedContexts.Single().UploadedVertexArray[0].BlendWeights,
            Is.EqualTo(expectedWeights), "COLOR0 still carries bone weights.");
        Assert.That(bytes, Is.EqualTo(original));
    }

    [TestCase(2)]
    [TestCase(4)]
    public void PackedGpuColour_PreservesEveryOriginalByteValueInAllFourChannels(int weights)
    {
        var creator = Creator(weights);
        var size = (int)creator.GetVertexSize(RmvVersionEnum.RMV2_V8);
        var bytes = new byte[size * 256];
        for (var index = 0; index < 256; index++)
        {
            var offset = size * (index + 1) - 4;
            bytes[offset] = (byte)index;
            bytes[offset + 1] = (byte)(255 - index);
            bytes[offset + 2] = (byte)(index * 73 % 256);
            bytes[offset + 3] = (byte)(index * 17 % 256);
        }
        var vertices = creator.ReadArray(RmvVersionEnum.RMV2_V8, bytes, 0, size, 256);
        var model = Model(vertices[0], weights);
        model.Mesh.VertexList = vertices;
        var contexts = new TestGeometryGraphicsContextFactory();
        using var mesh = new MeshBuilderService(contexts).BuildMeshFromRmvModel(model, "test");
        var uploaded = contexts.CreatedContexts.Single().UploadedVertexArray;

        for (var index = 0; index < 256; index++)
        {
            var colour = uploaded[index].VertexColour;
            Assert.That(new[] { colour.R, colour.G, colour.B, colour.A },
                Is.EqualTo(bytes.AsSpan(size * (index + 1) - 4, 4).ToArray()), $"Original RGBA vertex {index}");
        }
    }

    [TestCase(2)]
    [TestCase(4)]
    public void LegacyWeightedVertices_KeepMissingColourDistinctAndUseWhiteForTheGpuInput(int weights)
    {
        var creator = Creator(weights);
        var size = (int)creator.GetVertexSize(RmvVersionEnum.RMV2_V7);
        var vertex = creator.ReadArray(RmvVersionEnum.RMV2_V7, new byte[size], 0, size, 1).Single();
        var contexts = new TestGeometryGraphicsContextFactory();
        using var mesh = new MeshBuilderService(contexts).BuildMeshFromRmvModel(Model(vertex, weights), "test");

        Assert.That(vertex.UnormColour, Is.Null);
        Assert.That(vertex.Colour, Is.EqualTo(new Vector4(0, 0, 0, 1)));
        Assert.That(mesh.VertexArray[0].VertexColour, Is.EqualTo(Color.White));
    }

    [Test]
    public void StaticV8_OriginalRgbaAlsoReachesTheSameGpuInput()
    {
        var bytes = new byte[32];
        bytes[28] = 255;
        bytes[29] = 64;
        bytes[30] = 0;
        bytes[31] = 17;
        var vertex = new StaticVertexCreator().ReadArray(RmvVersionEnum.RMV2_V8, bytes, 0, 32, 1).Single();
        var contexts = new TestGeometryGraphicsContextFactory();
        using var mesh = new MeshBuilderService(contexts).BuildMeshFromRmvModel(Model(vertex, 0), "test");

        Assert.That(mesh.VertexArray[0].VertexColour, Is.EqualTo(new Color(255, 64, 0, 17)));
        Assert.That(mesh.VertexArray[0].BlendIndices, Is.EqualTo(Vector4.Zero));
        Assert.That(mesh.VertexArray[0].BlendWeights, Is.EqualTo(Vector4.Zero));
    }

    [Test]
    public void GpuDeclaration_AppendsColourOneWithoutMovingExistingBoneOrUvInputs()
    {
        var declaration = VertexPositionNormalTextureCustom.VertexDeclaration;
        var elements = declaration.GetVertexElements();
        var colour = elements.Single(element => element.VertexElementUsage == VertexElementUsage.Color && element.UsageIndex == 1);
        Assert.That(colour.Offset, Is.EqualTo(Marshal.OffsetOf<VertexPositionNormalTextureCustom>(nameof(VertexPositionNormalTextureCustom.VertexColour)).ToInt32()));
        Assert.That(colour.Offset, Is.EqualTo(100));
        Assert.That(colour.VertexElementFormat, Is.EqualTo(VertexElementFormat.Color));
        Assert.That(elements.Single(element => element.VertexElementUsage == VertexElementUsage.Color && element.UsageIndex == 0).Offset, Is.EqualTo(60));
        Assert.That(elements.Single(element => element.VertexElementUsage == VertexElementUsage.BlendIndices).Offset, Is.EqualTo(76));
        Assert.That(elements.Single(element => element.VertexElementUsage == VertexElementUsage.TextureCoordinate && element.UsageIndex == 1).Offset, Is.EqualTo(92));
        Assert.That(declaration.VertexStride, Is.EqualTo(104));
        Assert.That(Marshal.SizeOf<VertexPositionNormalTextureCustom>(), Is.EqualTo(declaration.VertexStride));
    }

    [Test]
    public void CloneAndGeometryTransforms_PreserveColourAndBoneWeights()
    {
        var expectedColour = new Color(17, 88, 170, 229);
        var vertex = new CommonVertex
        {
            Position = new Vector4(1, 2, 3, 1), Normal = Vector3.Up, Tangent = Vector3.Right, BiNormal = Vector3.Forward,
            BoneIndex = [2, 5], BoneWeight = [0.25f, 0.75f], WeightCount = 2, UnormColour = expectedColour.ToVector4(),
        };
        using var original = new MeshBuilderService(new TestGeometryGraphicsContextFactory())
            .BuildMeshFromRmvModel(Model(vertex, 2), "test");
        using var clone = original.Clone();
        var expectedWeights = clone.VertexArray[0].BlendWeights;
        var expectedIndices = clone.VertexArray[0].BlendIndices;
        var rotation = Matrix.CreateRotationY(0.5f);

        clone.TransformVertexTranslation(0, Matrix.CreateTranslation(1, 2, 3));
        clone.TransformVertexRotation(0, rotation, rotation);
        clone.TransformVertex(0, Matrix.CreateScale(0.5f, 2, 1));
        clone.RebuildVertexBuffer();

        Assert.That(original.VertexArray[0].VertexColour, Is.EqualTo(expectedColour));
        Assert.That(clone.VertexArray[0].VertexColour, Is.EqualTo(expectedColour));
        Assert.That(clone.VertexArray[0].BlendWeights, Is.EqualTo(expectedWeights));
        Assert.That(clone.VertexArray[0].BlendIndices, Is.EqualTo(expectedIndices));
        Assert.That(clone.VertexArray[0].Position, Is.Not.EqualTo(original.VertexArray[0].Position));
        clone.ChangeVertexType(UiVertexFormat.Cinematic);
        Assert.That(clone.VertexArray[0].VertexColour, Is.EqualTo(expectedColour));
    }

    [TestCase(2)]
    [TestCase(4)]
    public void IndependentUnormReadChannel_DoesNotChangeTheExistingWeightedWriter(int weights)
    {
        var creator = Creator(weights);
        var size = (int)creator.GetVertexSize(RmvVersionEnum.RMV2_V8);
        var bytes = new byte[size];
        bytes[size - 4] = 17;
        bytes[size - 3] = 88;
        bytes[size - 2] = 170;
        bytes[size - 1] = 255;
        var vertex = creator.ReadArray(RmvVersionEnum.RMV2_V8, bytes, 0, size, 1).Single();
        var withoutUnorm = new CommonVertex
        {
            Position = vertex.Position, Normal = vertex.Normal, BiNormal = vertex.BiNormal, Tangent = vertex.Tangent,
            Uv = vertex.Uv, Uv1 = vertex.Uv1, Colour = vertex.Colour,
            BoneIndex = vertex.BoneIndex, BoneWeight = vertex.BoneWeight, WeightCount = vertex.WeightCount,
        };

        Assert.That(creator.Write(RmvVersionEnum.RMV2_V8, vertex),
            Is.EqualTo(creator.Write(RmvVersionEnum.RMV2_V8, withoutUnorm)));
    }

    private static IVertexCreator Creator(int weights) =>
        weights == 2 ? new Weighted2VertexCreator() : new Weighted4VertexCreator();

    private static RmvModel Model(CommonVertex vertex, int weights) => new()
    {
        Material = new WeightedMaterial
        {
            BinaryVertexFormat = weights switch
            {
                0 => VertexFormat.Static,
                2 => VertexFormat.Weighted,
                _ => VertexFormat.Cinematic,
            },
        },
        Mesh = new RmvMesh { VertexList = [vertex], IndexList = [0, 0, 0] },
    };
}
