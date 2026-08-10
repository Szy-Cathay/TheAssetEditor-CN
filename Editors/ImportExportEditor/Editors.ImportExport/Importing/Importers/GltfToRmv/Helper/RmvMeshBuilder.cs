using System.Numerics;
using System.IO;
using System.Text;
using Editors.ImportExport.Common;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.Ui.Common.OperationProgress;
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
    private const int MaxVerticesPerSegment = ushort.MaxValue + 1;

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
        string skeletonName) =>
        BuildWithSummary(settings, modelRoot, animSkeletonFile, skeletonName).File;

    internal static RmvMeshBuildResult BuildWithSummary(
        GltfImporterSettings settings,
        ModelRoot modelRoot,
        AnimationFile? animSkeletonFile,
        string skeletonName,
        float scaleFactor = 1,
        IProgress<OperationProgressUpdate>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(modelRoot);

        if (!modelRoot.LogicalNodes.Any())
            throw new InvalidDataException("glTF 场景中没有节点。");

        var meshSources = GetMeshSources(modelRoot);
        if (meshSources.Count == 0)
            return new RmvMeshBuildResult(null, new RmvMeshImportSummary([]), []);

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
        var modelList = new List<RmvModel>();
        var modelSources = new List<MeshSource>();
        var segmentSummaries = new List<RmvMeshSegmentImportSummary>();
        var splitPrimitiveCount = 0;
        var generatedSplitSegmentCount = 0;
        var reservedSourceNames = meshSources
            .Select(source => source.ModelName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedOutputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var sourceIndex = 0;
             sourceIndex < meshSources.Count;
             sourceIndex++)
        {
            var source = meshSources[sourceIndex];
            var sourceSkeleton = source.Node.Skin != null ? animSkeletonFile : null;
            var generationResult = GenerateRmvMeshes(
                source,
                sourceSkeleton,
                scaleFactor,
                settings.SourceForwardDirection,
                sourceIndex,
                meshSources.Count,
                progress);
            var generatedSegments = generationResult.Segments;
            if (generationResult.SourceExceededVertexLimit)
            {
                splitPrimitiveCount++;
                generatedSplitSegmentCount += generatedSegments.Count;
            }

            for (var segmentIndex = 0;
                 segmentIndex < generatedSegments.Count;
                 segmentIndex++)
            {
                var (rmv2Mesh, originalSummary) = generatedSegments[segmentIndex];
                var modelName = GetUniqueOutputModelName(
                    source.ModelName,
                    segmentIndex,
                    generatedSegments.Count,
                    reservedSourceNames,
                    usedOutputNames);
                var summary = originalSummary with { ModelName = modelName };
                modelList.Add(CreateRmvModel(
                    rmv2Mesh,
                    modelName,
                    sourceSkeleton));
                modelSources.Add(source);
                segmentSummaries.Add(summary);
            }
        }

        rmv2File.LodHeaders[0].MeshCount = (uint)modelList.Count;
        rmv2File.ModelList[0] = modelList.ToArray();
        rmv2File.RecalculateOffsets();
        return new RmvMeshBuildResult(
            rmv2File,
            new RmvMeshImportSummary(
                segmentSummaries,
                splitPrimitiveCount,
                generatedSplitSegmentCount),
            modelSources);
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

    private sealed record MeshGenerationResult(
        IReadOnlyList<(RmvMesh Mesh, RmvMeshSegmentImportSummary Summary)> Segments,
        bool SourceExceededVertexLimit);

    private static MeshGenerationResult GenerateRmvMeshes(
        MeshSource source,
        AnimationFile? animSkeletonFile,
        float scaleFactor,
        GltfSourceForwardDirection sourceForwardDirection,
        int sourceIndex,
        int sourceCount,
        IProgress<OperationProgressUpdate>? progress)
    {
        var vertexBufferColumns = source.Primitive.GetVertexColumns();
        if (vertexBufferColumns.Positions == null || !vertexBufferColumns.Positions.Any())
            throw new InvalidDataException($"网格“{source.ModelName}”没有顶点数据。");

        // glTF applies only joint transforms to skinned meshes.
        var worldMatrix = source.Node.Skin == null
            ? source.Node.WorldMatrix
            : Matrix4x4.Identity;
        if (!Matrix4x4.Invert(worldMatrix, out var inverseWorldMatrix))
            throw new InvalidDataException($"节点“{source.Node.Name}”的变换矩阵不可逆，无法安全导入网格。");
        var normalMatrix = Matrix4x4.Transpose(inverseWorldMatrix);

        var positionsCount = vertexBufferColumns.Positions.Count();
        var rebuildNormals = vertexBufferColumns.Normals == null;
        var rebuildTangents = vertexBufferColumns.Tangents == null;
        var defaultTextureCoordinates = vertexBufferColumns.TexCoords0 == null;
        var ignoreVertexColors = vertexBufferColumns.Colors0 != null ||
            vertexBufferColumns.Colors1 != null;
        var ignoreMorphTargets = source.Primitive.MorphTargetsCount > 0;

        if (source.Primitive.DrawPrimitiveType is not (
            PrimitiveType.TRIANGLES or
            PrimitiveType.TRIANGLE_STRIP or
            PrimitiveType.TRIANGLE_FAN))
        {
            throw new InvalidDataException(LocalizationManager.Instance.GetFormat(
                "GltfImporter.Error.UnsupportedPrimitiveType",
                source.ModelName,
                source.Primitive.DrawPrimitiveType));
        }

        progress?.Report(new OperationProgressUpdate(
            LocalizationManager.Instance.GetFormat(
                "ImportWindow.Progress.Meshes",
                sourceIndex + 1,
                sourceCount),
            LocalizationManager.Instance.GetFormat(
                "ImportWindow.Progress.MeshPreparing",
                source.ModelName)));
        var triangles = source.Primitive.GetTriangleIndices()
            .Select(triangle => (
                A: checked((int)triangle.A),
                B: checked((int)triangle.B),
                C: checked((int)triangle.C)))
            .ToList();
        var plans = CreateSegmentPlans(source, positionsCount, triangles);
        var output = new List<(RmvMesh, RmvMeshSegmentImportSummary)>(plans.Count);
        var totalOutputVertices = plans.Sum(plan => plan.SourceVertexIndices.Count);
        var completedVertices = 0L;
        ReportVertexProgress();
        foreach (var plan in plans)
        {
            var rmv2Mesh = new RmvMesh
            {
                VertexList = new CommonVertex[plan.SourceVertexIndices.Count],
                IndexList = plan.Indices,
            };
            var affectedVertices = 0;
            var maximumDiscardedWeight = 0f;
            var verticesAboveTenPercentDiscarded = 0;

            for (var outputVertexIndex = 0;
                 outputVertexIndex < plan.SourceVertexIndices.Count;
                 outputVertexIndex++)
            {
                var sourceVertexIndex = plan.SourceVertexIndices[outputVertexIndex];
                var vertexBuilder = vertexBufferColumns.GetVertex<
                    VertexPositionNormalTangent,
                    VertexTexture1,
                    VertexJoints8>(sourceVertexIndex);
                var converted = ConvertToRmvVertex(
                    vertexBuilder,
                    source.Node.Skin,
                    animSkeletonFile,
                    worldMatrix,
                    normalMatrix,
                    scaleFactor,
                    sourceForwardDirection);
                rmv2Mesh.VertexList[outputVertexIndex] = converted.Vertex;
                completedVertices++;
                if (ShouldReportProgress(completedVertices, totalOutputVertices))
                    ReportVertexProgress();
                if (converted.DiscardedWeight > 0)
                {
                    affectedVertices++;
                    maximumDiscardedWeight = Math.Max(
                        maximumDiscardedWeight,
                        converted.DiscardedWeight);
                    if (converted.DiscardedWeight > 0.100001f)
                        verticesAboveTenPercentDiscarded++;
                }
            }

            progress?.Report(new OperationProgressUpdate(
                LocalizationManager.Instance.GetFormat(
                    "ImportWindow.Progress.Meshes",
                    sourceIndex + 1,
                    sourceCount),
                LocalizationManager.Instance.GetFormat(
                    "ImportWindow.Progress.MeshFinishing",
                    source.ModelName)));
            if (rebuildNormals)
                RebuildNormals(rmv2Mesh);
            TangentBasisCalculator.CalculateForRmv2Mesh(rmv2Mesh);
            output.Add((
                rmv2Mesh,
                new RmvMeshSegmentImportSummary(
                    plan.ModelName,
                    affectedVertices,
                    maximumDiscardedWeight,
                    verticesAboveTenPercentDiscarded,
                    rebuildNormals,
                    rebuildTangents,
                    defaultTextureCoordinates,
                    ignoreVertexColors,
                    ignoreMorphTargets)));
        }

        return new MeshGenerationResult(
            output,
            positionsCount > MaxVerticesPerSegment);

        void ReportVertexProgress() => progress?.Report(
            new OperationProgressUpdate(
                LocalizationManager.Instance.GetFormat(
                    "ImportWindow.Progress.Meshes",
                    sourceIndex + 1,
                    sourceCount),
                LocalizationManager.Instance.GetFormat(
                    "ImportWindow.Progress.MeshVertices",
                    source.ModelName,
                    completedVertices,
                    totalOutputVertices),
                completedVertices,
                totalOutputVertices));
    }

    private static bool ShouldReportProgress(long completed, long total)
    {
        if (completed == total)
            return true;

        var interval = Math.Max(1, total / 100);
        return completed % interval == 0;
    }

    private static string GetUniqueOutputModelName(
        string sourceModelName,
        int segmentIndex,
        int segmentCount,
        ISet<string> reservedSourceNames,
        ISet<string> usedOutputNames)
    {
        if (segmentCount == 1)
        {
            usedOutputNames.Add(sourceModelName);
            return sourceModelName;
        }

        var baseName = $"{sourceModelName}_split{segmentIndex + 1}";
        var modelName = baseName;
        for (var duplicateIndex = 2;
             reservedSourceNames.Contains(modelName) || !usedOutputNames.Add(modelName);
             duplicateIndex++)
        {
            modelName = $"{baseName}_instance{duplicateIndex}";
        }

        return modelName;
    }

    private sealed record MeshSegmentPlan(
        string ModelName,
        IReadOnlyList<int> SourceVertexIndices,
        ushort[] Indices);

    private static IReadOnlyList<MeshSegmentPlan> CreateSegmentPlans(
        MeshSource source,
        int positionsCount,
        IReadOnlyList<(int A, int B, int C)> triangles)
    {
        foreach (var triangle in triangles)
        {
            ValidateVertexIndex(source, positionsCount, triangle.A);
            ValidateVertexIndex(source, positionsCount, triangle.B);
            ValidateVertexIndex(source, positionsCount, triangle.C);
        }

        if (positionsCount <= MaxVerticesPerSegment)
        {
            var indices = new ushort[triangles.Count * 3];
            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                var triangle = triangles[triangleIndex];
                var outputIndex = triangleIndex * 3;
                indices[outputIndex] = checked((ushort)triangle.A);
                indices[outputIndex + 2] = checked((ushort)triangle.B);
                indices[outputIndex + 1] = checked((ushort)triangle.C);
            }

            return
            [
                new MeshSegmentPlan(
                    source.ModelName,
                    Enumerable.Range(0, positionsCount).ToArray(),
                    indices),
            ];
        }

        var rawSegments = new List<(IReadOnlyList<int> Vertices, ushort[] Indices)>();
        var sourceVertices = new List<int>(MaxVerticesPerSegment);
        var localIndices = new List<ushort>();
        var localIndexBySource = new Dictionary<int, ushort>(MaxVerticesPerSegment);

        void FlushSegment()
        {
            if (localIndices.Count == 0)
                return;

            rawSegments.Add((sourceVertices.ToArray(), localIndices.ToArray()));
            sourceVertices.Clear();
            localIndices.Clear();
            localIndexBySource.Clear();
        }

        ushort GetOrAddVertex(int sourceVertexIndex)
        {
            if (localIndexBySource.TryGetValue(sourceVertexIndex, out var localIndex))
                return localIndex;

            localIndex = checked((ushort)sourceVertices.Count);
            localIndexBySource.Add(sourceVertexIndex, localIndex);
            sourceVertices.Add(sourceVertexIndex);
            return localIndex;
        }

        foreach (var triangle in triangles)
        {
            var newVertexCount = localIndexBySource.ContainsKey(triangle.A) ? 0 : 1;
            if (triangle.B != triangle.A &&
                !localIndexBySource.ContainsKey(triangle.B))
            {
                newVertexCount++;
            }
            if (triangle.C != triangle.A &&
                triangle.C != triangle.B &&
                !localIndexBySource.ContainsKey(triangle.C))
            {
                newVertexCount++;
            }
            if (localIndices.Count > 0 &&
                localIndexBySource.Count + newVertexCount > MaxVerticesPerSegment)
            {
                FlushSegment();
            }

            localIndices.Add(GetOrAddVertex(triangle.A));
            localIndices.Add(GetOrAddVertex(triangle.C));
            localIndices.Add(GetOrAddVertex(triangle.B));
        }

        FlushSegment();
        return rawSegments
            .Select((segment, index) => new MeshSegmentPlan(
                rawSegments.Count == 1
                    ? source.ModelName
                    : $"{source.ModelName}_split{index + 1}",
                segment.Vertices,
                segment.Indices))
            .ToList();
    }

    private static void ValidateVertexIndex(
        MeshSource source,
        int positionsCount,
        int vertexIndex)
    {
        if (vertexIndex < 0 || vertexIndex >= positionsCount)
        {
            throw new InvalidDataException(LocalizationManager.Instance.GetFormat(
                "GltfImporter.Error.InvalidTriangleVertex",
                source.ModelName,
                vertexIndex));
        }
    }

    private static (CommonVertex Vertex, float DiscardedWeight) ConvertToRmvVertex(
        VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints8> vertexBuilder,
        Skin? skin,
        AnimationFile? animSkeletonFile,
        Matrix4x4 worldMatrix,
        Matrix4x4 normalMatrix,
        float scaleFactor,
        GltfSourceForwardDirection sourceForwardDirection)
    {
        var position = Vector3.Transform(vertexBuilder.Geometry.Position, worldMatrix) * scaleFactor;
        var normal = Vector3.TransformNormal(vertexBuilder.Geometry.Normal, normalMatrix);
        if (normal.LengthSquared() > 0.000000000001f)
            normal = Vector3.Normalize(normal);

        var gamePosition = GltfSourceForwardConverter.ConvertGameVector(
            new Vector3(-position.X, position.Y, position.Z),
            sourceForwardDirection);
        var gameNormal = GltfSourceForwardConverter.ConvertGameVector(
            new Vector3(-normal.X, normal.Y, normal.Z),
            sourceForwardDirection);
        var rmv2Vertex = new CommonVertex
        {
            Position = new XNA.Vector4(
                gamePosition.X,
                gamePosition.Y,
                gamePosition.Z,
                1),
            Uv = VecConv.GetXna(vertexBuilder.Material.TexCoord),
            Normal = new XNA.Vector3(
                gameNormal.X,
                gameNormal.Y,
                gameNormal.Z),
            Tangent = XNA.Vector3.Zero,
            BiNormal = XNA.Vector3.Zero,
            WeightCount = animSkeletonFile == null || skin == null ? 0 : 4,
        };

        rmv2Vertex.BoneIndex = new byte[rmv2Vertex.WeightCount];
        rmv2Vertex.BoneWeight = new float[rmv2Vertex.WeightCount];
        if (rmv2Vertex.WeightCount == 0)
            return (rmv2Vertex, 0);

        var bindings = Enumerable.Range(0, 8)
            .Select(bindingIndex => new
            {
                BindingIndex = bindingIndex,
                Binding = vertexBuilder.Skinning.GetBinding(bindingIndex),
            })
            .Where(item => item.Binding.Weight > 0)
            .OrderByDescending(item => item.Binding.Weight)
            .ThenBy(item => item.BindingIndex)
            .ToList();
        if (bindings.Count == 0)
        {
            throw new InvalidDataException(LocalizationManager.Instance.Get(
                "GltfImporter.Error.MissingVertexWeights"));
        }

        var totalWeight = bindings.Sum(item => item.Binding.Weight);
        var selected = bindings.Take(rmv2Vertex.WeightCount).ToList();
        var selectedWeight = selected.Sum(item => item.Binding.Weight);
        var discardedWeight = bindings
            .Skip(rmv2Vertex.WeightCount)
            .Sum(item => item.Binding.Weight) / totalWeight;
        var quantizedWeights = QuantizeWeights(
            selected.Select(item => item.Binding.Weight / selectedWeight).ToArray());

        for (var outputIndex = 0; outputIndex < selected.Count; outputIndex++)
        {
            rmv2Vertex.BoneWeight[outputIndex] = quantizedWeights[outputIndex] / (float)byte.MaxValue;
            rmv2Vertex.BoneIndex[outputIndex] = checked((byte)GetMappedBoneTableIndex(
                selected[outputIndex].Binding.Index,
                skin!,
                animSkeletonFile!));
        }

        return (rmv2Vertex, discardedWeight);
    }

    private static int GetMappedBoneTableIndex(
        int jointIndex,
        Skin skin,
        AnimationFile animSkeletonFile)
    {
        var joint = skin.GetJoint(jointIndex);
        var boneTableIndex = Array.FindIndex<BoneInfo>(
            animSkeletonFile.Bones,
            x => string.Equals(x.Name, joint.Joint.Name, StringComparison.OrdinalIgnoreCase));

        if (boneTableIndex < 0)
            throw new InvalidDataException($"glTF 骨骼“{joint.Joint.Name}”在目标游戏骨架中不存在，已停止导入以避免错误绑骨。");
        if (boneTableIndex > byte.MaxValue)
            throw new InvalidDataException($"目标骨骼索引 {boneTableIndex} 超出 RMV2 可表示范围。");

        return boneTableIndex;
    }

    private static byte[] QuantizeWeights(IReadOnlyList<float> weights)
    {
        var scaledWeights = weights
            .Select((weight, index) => new
            {
                Index = index,
                Scaled = weight * byte.MaxValue,
            })
            .ToList();
        var quantized = scaledWeights
            .Select(item => (byte)MathF.Floor(item.Scaled))
            .ToArray();
        var remainder = byte.MaxValue - quantized.Sum(value => value);

        foreach (var item in scaledWeights
            .OrderByDescending(item => item.Scaled - MathF.Floor(item.Scaled))
            .ThenBy(item => item.Index)
            .Take(remainder))
        {
            quantized[item.Index]++;
        }

        return quantized;
    }

    private static void RebuildNormals(RmvMesh mesh)
    {
        foreach (var vertex in mesh.VertexList)
            vertex.Normal = XNA.Vector3.Zero;

        for (var index = 0; index < mesh.IndexList.Length; index += 3)
        {
            var first = mesh.VertexList[mesh.IndexList[index]];
            var second = mesh.VertexList[mesh.IndexList[index + 1]];
            var third = mesh.VertexList[mesh.IndexList[index + 2]];
            var firstEdge = ToVector3(second.Position - first.Position);
            var secondEdge = ToVector3(third.Position - first.Position);
            var faceNormal = XNA.Vector3.Cross(firstEdge, secondEdge);
            if (!IsFinite(faceNormal) || faceNormal.LengthSquared() <= 0.000000000001f)
                continue;

            first.Normal += faceNormal;
            second.Normal += faceNormal;
            third.Normal += faceNormal;
        }

        foreach (var vertex in mesh.VertexList)
        {
            vertex.Normal = IsFinite(vertex.Normal) &&
                vertex.Normal.LengthSquared() > 0.000000000001f
                ? XNA.Vector3.Normalize(vertex.Normal)
                : XNA.Vector3.UnitZ;
        }
    }

    private static XNA.Vector3 ToVector3(XNA.Vector4 value) =>
        new(value.X, value.Y, value.Z);

    private static bool IsFinite(XNA.Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

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
