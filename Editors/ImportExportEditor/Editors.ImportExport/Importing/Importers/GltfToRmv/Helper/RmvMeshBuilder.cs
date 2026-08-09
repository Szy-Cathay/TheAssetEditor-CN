using System.Numerics;
using System.IO;
using System.Text;
using Editors.ImportExport.Common;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.RigidModel.Vertex;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Schema2;
using static Shared.GameFormats.Animation.AnimationFile;
using XNA = Microsoft.Xna.Framework;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

/// <summary>
/// Builds an RMV mesh from the visible mesh instances in a glTF scene.
/// </summary>
public class RmvMeshBuilder
{
    public sealed record MeshSource(
        Node Node,
        Mesh Mesh,
        MeshPrimitive Primitive,
        int PrimitiveIndex,
        string ModelName);

    public static RmvFile? Build(
        GltfImporterSettings settings,
        ModelRoot modelRoot,
        AnimationFile? animSkeletonFile,
        string skeletonName)
    {
        ArgumentNullException.ThrowIfNull(modelRoot);

        if (!modelRoot.LogicalNodes.Any())
            throw new InvalidDataException("glTF 场景中没有节点。");

        var meshSources = GetMeshSources(modelRoot);
        if (meshSources.Count == 0)
            return null;

        const int lodCount = 1;
        var rmv2File = new RmvFile
        {
            Header = new RmvFileHeader
            {
                _fileType = Encoding.ASCII.GetBytes("RMV2"),
                SkeletonName = skeletonName,
                Version = RmvVersionEnum.RMV2_V7,
                LodCount = lodCount,
            },
            ModelList = new RmvModel[lodCount][],
            LodHeaders = new RmvLodHeader[lodCount],
        };

        rmv2File.LodHeaders[0] = LodHeaderFactory.Create().CreateEmpty(
            RmvVersionEnum.RMV2_V7,
            100.0f,
            0,
            0);
        rmv2File.LodHeaders[0].MeshCount = (uint)meshSources.Count;

        var modelList = new List<RmvModel>();
        foreach (var source in meshSources)
        {
            var sourceSkeleton = source.Node.Skin != null ? animSkeletonFile : null;
            var rmv2Mesh = GenerateRmvMesh(source, sourceSkeleton);
            modelList.Add(CreateRmvModel(rmv2Mesh, source.ModelName, sourceSkeleton));
        }

        rmv2File.ModelList[0] = modelList.ToArray();
        rmv2File.RecalculateOffsets();
        return rmv2File;
    }

    public static IReadOnlyList<MeshSource> GetMeshSources(ModelRoot modelRoot)
    {
        var scene = modelRoot.DefaultScene ?? modelRoot.LogicalScenes.FirstOrDefault();
        if (scene == null)
            return [];

        var output = new List<MeshSource>();
        var usedModelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Traverse(scene.VisualChildren))
        {
            var mesh = node.Mesh;
            if (mesh == null)
                continue;

            for (var primitiveIndex = 0; primitiveIndex < mesh.Primitives.Count; primitiveIndex++)
            {
                var candidateName = GetModelName(node, mesh, primitiveIndex);
                var modelName = candidateName;
                for (var duplicateIndex = 2; !usedModelNames.Add(modelName); duplicateIndex++)
                    modelName = $"{candidateName}_instance{duplicateIndex}";

                output.Add(new MeshSource(
                    node,
                    mesh,
                    mesh.Primitives[primitiveIndex],
                    primitiveIndex,
                    modelName));
            }
        }

        return output;
    }

    private static string GetModelName(Node node, Mesh mesh, int primitiveIndex)
    {
        var baseName = string.IsNullOrWhiteSpace(node.Name) ? mesh.Name : node.Name;
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "mesh";

        return mesh.Primitives.Count > 1
            ? $"{baseName}_part{primitiveIndex + 1}"
            : baseName;
    }

    private static IEnumerable<Node> Traverse(IEnumerable<Node> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Traverse(node.VisualChildren))
                yield return child;
        }
    }

    private static RmvMesh GenerateRmvMesh(MeshSource source, AnimationFile? animSkeletonFile)
    {
        var vertexBufferColumns = source.Primitive.GetVertexColumns();
        if (vertexBufferColumns.Positions == null || !vertexBufferColumns.Positions.Any())
            throw new InvalidDataException($"网格“{source.ModelName}”没有顶点数据。");
        if (vertexBufferColumns.Positions.Count() > ushort.MaxValue + 1)
            throw new InvalidDataException($"网格“{source.ModelName}”超过 RMV2 单网格 65536 顶点限制。");

        var worldMatrix = source.Node.WorldMatrix;
        if (!Matrix4x4.Invert(worldMatrix, out var inverseWorldMatrix))
            throw new InvalidDataException($"节点“{source.Node.Name}”的变换矩阵不可逆，无法安全导入网格。");
        var normalMatrix = Matrix4x4.Transpose(inverseWorldMatrix);

        var positionsCount = vertexBufferColumns.Positions.Count();
        var rmv2Mesh = new RmvMesh
        {
            VertexList = new CommonVertex[positionsCount],
        };

        for (var vertexIndex = 0; vertexIndex < positionsCount; vertexIndex++)
        {
            var vertexBuilder = vertexBufferColumns.GetVertex<
                VertexPositionNormalTangent,
                VertexTexture1,
                VertexJoints4>(vertexIndex);
            rmv2Mesh.VertexList[vertexIndex] = ConvertToRmvVertex(
                vertexBuilder,
                source.Node.Skin,
                animSkeletonFile,
                worldMatrix,
                normalMatrix);
        }

        var indices = source.Primitive.GetIndices();
        if (indices.Count % 3 != 0)
            throw new InvalidDataException($"网格“{source.ModelName}”的索引不是三角形列表。");

        rmv2Mesh.IndexList = new ushort[indices.Count];
        for (var index = 0; index < indices.Count; index += 3)
        {
            rmv2Mesh.IndexList[index] = checked((ushort)indices[index]);
            rmv2Mesh.IndexList[index + 2] = checked((ushort)indices[index + 1]);
            rmv2Mesh.IndexList[index + 1] = checked((ushort)indices[index + 2]);
        }

        TangentBasisCalculator.CalculateForRmv2Mesh(rmv2Mesh);
        return rmv2Mesh;
    }

    private static CommonVertex ConvertToRmvVertex(
        VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> vertexBuilder,
        Skin? skin,
        AnimationFile? animSkeletonFile,
        Matrix4x4 worldMatrix,
        Matrix4x4 normalMatrix)
    {
        var position = Vector3.Transform(vertexBuilder.Geometry.Position, worldMatrix);
        var normal = Vector3.TransformNormal(vertexBuilder.Geometry.Normal, normalMatrix);
        if (normal.LengthSquared() > 0.000000000001f)
            normal = Vector3.Normalize(normal);

        var rmv2Vertex = new CommonVertex
        {
            Position = new XNA.Vector4(-position.X, position.Y, position.Z, 1),
            Uv = VecConv.GetXna(vertexBuilder.Material.TexCoord),
            Normal = new XNA.Vector3(-normal.X, normal.Y, normal.Z),
            Tangent = XNA.Vector3.Zero,
            BiNormal = XNA.Vector3.Zero,
            WeightCount = animSkeletonFile == null || skin == null ? 0 : 4,
        };

        rmv2Vertex.BoneIndex = new byte[rmv2Vertex.WeightCount];
        rmv2Vertex.BoneWeight = new float[rmv2Vertex.WeightCount];

        for (var bindingIndex = 0; bindingIndex < rmv2Vertex.WeightCount; bindingIndex++)
        {
            var weight = vertexBuilder.Skinning.Weights[bindingIndex];
            rmv2Vertex.BoneWeight[bindingIndex] = weight;
            if (weight <= 0)
                continue;

            var boneTableIndex = GetMappedBoneTableIndex(
                vertexBuilder,
                skin!,
                animSkeletonFile!,
                bindingIndex);
            rmv2Vertex.BoneIndex[bindingIndex] = checked((byte)boneTableIndex);
        }

        return rmv2Vertex;
    }

    private static int GetMappedBoneTableIndex(
        VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> vertexBuilder,
        Skin skin,
        AnimationFile animSkeletonFile,
        int bindingIndex)
    {
        var binding = vertexBuilder.Skinning.GetBinding(bindingIndex);
        var joint = skin.GetJoint(binding.Index);
        var boneTableIndex = Array.FindIndex<BoneInfo>(
            animSkeletonFile.Bones,
            x => string.Equals(x.Name, joint.Joint.Name, StringComparison.OrdinalIgnoreCase));

        if (boneTableIndex < 0)
            throw new InvalidDataException($"glTF 骨骼“{joint.Joint.Name}”在目标游戏骨架中不存在，已停止导入以避免错误绑骨。");
        if (boneTableIndex > byte.MaxValue)
            throw new InvalidDataException($"目标骨骼索引 {boneTableIndex} 超出 RMV2 可表示范围。");

        return boneTableIndex;
    }

    private static RmvModel CreateRmvModel(
        RmvMesh rmv2Mesh,
        string modelName,
        AnimationFile? animSkeletonFile,
        bool addBonesAsAttachmentPoints = false)
    {
        var materialHeader = new WeightedMaterial();
        if (animSkeletonFile != null)
        {
            materialHeader.MaterialId = ModelMaterialEnum.weighted;
            materialHeader.BinaryVertexFormat = VertexFormat.Cinematic;
            MeshWeightValidator.Validate(rmv2Mesh);
        }
        else
        {
            materialHeader.MaterialId = ModelMaterialEnum.default_type;
            materialHeader.BinaryVertexFormat = VertexFormat.Static;
        }

        var newModel = new RmvModel
        {
            CommonHeader = RmvCommonHeader.CreateDefault(),
            Material = materialHeader,
            Mesh = rmv2Mesh,
        };
        newModel.Material.ModelName = modelName;
        CalculateBoundBox(newModel);

        if (addBonesAsAttachmentPoints && animSkeletonFile != null)
        {
            var boneNames = animSkeletonFile.Bones.Select(x => x.Name).ToList();
            var attachmentPoints = AttachmentPointHelper.CreateFromBoneList(boneNames);
            newModel.Material.EnrichDataBeforeSaving(attachmentPoints, -1);
        }

        return newModel;
    }

    private static void CalculateBoundBox(RmvModel model)
    {
        var points = model.Mesh.VertexList
            .Select(vertex => new XNA.Vector3(
                vertex.Position.X,
                vertex.Position.Y,
                vertex.Position.Z));
        model.UpdateBoundingBox(XNA.BoundingBox.CreateFromPoints(points));
    }
}
