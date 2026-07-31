using System.Globalization;
using System.Text;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Editors.Reports.Geometry
{
    public class RmvToTextCommand(RmvToTextReport report) : IRmvToTextCommand
    {
        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get("ContextMenu.GenerateRmvTextReport");

        public bool IsEnabled(TreeNode node) =>
            node.NodeType == NodeType.File &&
            node.Item != null &&
            node.Name.EndsWith(
                ".rigid_model_v2",
                StringComparison.OrdinalIgnoreCase);

        public void Execute(TreeNode node)
        {
            if (!IsEnabled(node))
                return;

            report.Generate(node.Item!, node.GetFullPath());
        }
    }

    public class RmvToTextReport
    {
        private readonly string _outputDirectory;
        private readonly Action<string> _openOutput;

        public RmvToTextReport()
            : this(
                Path.Combine(
                    DirectoryHelper.ReportsDirectory,
                    "RmvToText"),
                DirectoryHelper.OpenFolderAndSelectFile)
        {
        }

        internal RmvToTextReport(
            string outputDirectory,
            Action<string> openOutput)
        {
            _outputDirectory = outputDirectory;
            _openOutput = openOutput;
        }

        public string Generate(PackFile packFile)
        {
            return Generate(packFile, packFile.Name);
        }

        public string Generate(
            PackFile packFile,
            string logicalPath)
        {
            var rmvFile = ModelFactory.Create().Load(
                packFile.DataSource.ReadData());
            var report = Format(rmvFile);

            var outputPath = Path.Combine(
                _outputDirectory,
                GetReportRelativePath(logicalPath));
            var outputPathDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputPathDirectory))
                DirectoryHelper.EnsureCreated(outputPathDirectory);

            File.WriteAllText(outputPath, report, Encoding.UTF8);
            _openOutput(outputPath);
            return outputPath;
        }

        private static string GetReportRelativePath(string logicalPath)
        {
            var pathSegments = logicalPath
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizePathSegment)
                .ToArray();
            if (pathSegments.Length == 0)
                pathSegments = ["model"];

            pathSegments[^1] =
                Path.GetFileNameWithoutExtension(pathSegments[^1]) +
                ".txt";
            return Path.Combine(pathSegments);
        }

        private static string SanitizePathSegment(string segment)
        {
            if (segment is "." or "..")
                return "_";

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(
                segment.Select(character =>
                        invalidCharacters.Contains(character)
                            ? '_'
                            : character)
                    .ToArray());
            return string.IsNullOrWhiteSpace(sanitized)
                ? "_"
                : sanitized;
        }

        public string Format(RmvFile rmvFile)
        {
            var output = new StringBuilder();
            output.AppendLine("=== RMV2 模型诊断报告 ===");
            output.AppendLine($"文件类型: {rmvFile.Header.FileType}");
            output.AppendLine($"版本: {rmvFile.Header.Version}");
            output.AppendLine($"骨架: {rmvFile.Header.SkeletonName}");
            output.AppendLine($"LOD 数量: {rmvFile.Header.LodCount}");
            output.AppendLine();

            var lodCount = Math.Min(
                (int)rmvFile.Header.LodCount,
                Math.Min(
                    rmvFile.LodHeaders.Length,
                    rmvFile.ModelList.Length));

            long totalFaces = 0;
            for (var lodIndex = 0; lodIndex < lodCount; lodIndex++)
            {
                var lodHeader = rmvFile.LodHeaders[lodIndex];
                var models = rmvFile.ModelList[lodIndex];
                var lodFaces = models.Sum(
                    model => (long)model.CommonHeader.IndexCount / 3);
                totalFaces += lodFaces;

                output.AppendLine($"=== LOD {lodIndex} ===");
                output.AppendLine(
                    $"显示距离: {FormatNumber(lodHeader.LodCameraDistance)}");
                output.AppendLine($"质量等级: {lodHeader.QualityLvl}");
                output.AppendLine($"网格数量: {models.Length}");
                output.AppendLine(
                    $"顶点缓冲区大小: {lodHeader.TotalLodVertexSize}");
                output.AppendLine(
                    $"索引缓冲区大小: {lodHeader.TotalLodIndexSize}");
                output.AppendLine(
                    $"首个网格偏移: {lodHeader.FirstMeshOffset}");
                output.AppendLine($"面数: {lodFaces}");
                output.AppendLine();

                for (var modelIndex = 0;
                     modelIndex < models.Length;
                     modelIndex++)
                {
                    WriteModel(
                        output,
                        models[modelIndex],
                        modelIndex);
                }
            }

            output.AppendLine("=== 汇总 ===");
            output.AppendLine($"总面数: {totalFaces}");
            return output.ToString();
        }

        private static void WriteModel(
            StringBuilder output,
            RmvModel model,
            int modelIndex)
        {
            var modelName = string.IsNullOrWhiteSpace(
                model.Material.ModelName)
                ? $"model_{modelIndex}"
                : model.Material.ModelName;

            output.AppendLine($"--- 网格 {modelIndex}: {modelName} ---");
            output.AppendLine(
                $"模型类型: {model.CommonHeader.ModelTypeFlag}");
            output.AppendLine(
                $"渲染标志: {model.CommonHeader.RenderFlag}");
            output.AppendLine($"材质: {model.Material.MaterialId}");
            output.AppendLine(
                $"顶点格式: {model.Material.BinaryVertexFormat}");
            output.AppendLine(
                $"顶点数量: {model.CommonHeader.VertexCount}");
            output.AppendLine(
                $"索引数量: {model.CommonHeader.IndexCount}");
            output.AppendLine($"面数: {model.CommonHeader.IndexCount / 3}");
            output.AppendLine(
                $"枢轴: {FormatVector(model.Material.PivotPoint)}");
            output.AppendLine(
                $"纹理目录: {model.Material.TextureDirectory}");

            foreach (var texture in model.Material.GetAllTextures())
            {
                output.AppendLine(
                    $"纹理 {texture.TexureType}: {texture.Path}");
            }

            if (model.Material is WeightedMaterial weightedMaterial)
            {
                output.AppendLine($"过滤器: {weightedMaterial.Filters}");
                output.AppendLine(
                    $"矩阵索引: {weightedMaterial.MatrixIndex}");
                output.AppendLine(
                    $"父矩阵索引: {weightedMaterial.ParentMatrixIndex}");
                output.AppendLine(
                    $"材质提示: {weightedMaterial.MaterialHint}");
                output.AppendLine(
                    $"工具顶点格式: {weightedMaterial.ToolVertexFormat}");
                output.AppendLine(
                    $"附着点数量: " +
                    $"{weightedMaterial.AttachmentPointParams.Count}");
                foreach (var attachmentPoint in
                         weightedMaterial.AttachmentPointParams)
                {
                    output.AppendLine(
                        $"  附着点 {attachmentPoint.Name}: " +
                        $"骨骼 {attachmentPoint.BoneIndex}");
                }

                output.AppendLine(
                    $"字符串参数数量: " +
                    $"{weightedMaterial.StringParams.Values.Count}");
                output.AppendLine(
                    $"浮点参数数量: " +
                    $"{weightedMaterial.FloatParams.Values.Count}");
                output.AppendLine(
                    $"整数参数数量: " +
                    $"{weightedMaterial.IntParams.Values.Count}");
                output.AppendLine(
                    $"四维向量参数数量: " +
                    $"{weightedMaterial.Vec4Params.Values.Count}");
            }

            output.AppendLine("顶点:");
            var vertices = model.Mesh.VertexList;
            for (var vertexIndex = 0;
                 vertexIndex < vertices.Length;
                 vertexIndex++)
            {
                WriteVertex(
                    output,
                    vertices[vertexIndex],
                    vertexIndex);
            }

            output.AppendLine();
        }

        private static void WriteVertex(
            StringBuilder output,
            CommonVertex vertex,
            int vertexIndex)
        {
            output.AppendLine($"  顶点 {vertexIndex}");
            output.AppendLine(
                $"    位置: {FormatVector(vertex.Position)}");
            output.AppendLine(
                $"    法线: {FormatVector(vertex.Normal)}");
            output.AppendLine(
                $"    副法线: {FormatVector(vertex.BiNormal)}");
            output.AppendLine(
                $"    切线: {FormatVector(vertex.Tangent)}");
            output.AppendLine($"    UV0: {FormatVector(vertex.Uv)}");
            output.AppendLine($"    UV1: {FormatVector(vertex.Uv1)}");
            output.AppendLine(
                $"    颜色: {FormatVector(vertex.Colour)}");
            output.AppendLine(
                $"    骨骼权重: {FormatBoneWeights(vertex)}");
        }

        private static string FormatBoneWeights(CommonVertex vertex)
        {
            if (vertex.BoneIndex == null ||
                vertex.BoneWeight == null)
            {
                return "无";
            }

            var count = Math.Min(
                vertex.BoneIndex.Length,
                vertex.BoneWeight.Length);
            if (count == 0)
                return "无";

            return string.Join(
                ", ",
                Enumerable.Range(0, count).Select(
                    index =>
                        $"{vertex.BoneIndex[index]}=" +
                        FormatNumber(vertex.BoneWeight[index])));
        }

        private static string FormatVector(
            Microsoft.Xna.Framework.Vector2 value) =>
            $"({FormatNumber(value.X)}, {FormatNumber(value.Y)})";

        private static string FormatVector(
            Microsoft.Xna.Framework.Vector3 value) =>
            $"({FormatNumber(value.X)}, {FormatNumber(value.Y)}, " +
            $"{FormatNumber(value.Z)})";

        private static string FormatVector(
            Microsoft.Xna.Framework.Vector4 value) =>
            $"({FormatNumber(value.X)}, {FormatNumber(value.Y)}, " +
            $"{FormatNumber(value.Z)}, {FormatNumber(value.W)})";

        private static string FormatNumber(float value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
