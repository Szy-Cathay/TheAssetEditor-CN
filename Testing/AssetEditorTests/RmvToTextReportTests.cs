using System.Globalization;
using System.Reflection;
using AssetEditor.Services;
using Editors.Reports.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace AssetEditorTests
{
    [TestClass]
    public class RmvToTextReportTests
    {
        [TestMethod]
        public void Format_IncludesChineseModelDiagnostics()
        {
            var report = new RmvToTextReport().Format(CreateRmvFile());

            StringAssert.Contains(report, "RMV2 模型诊断报告");
            StringAssert.Contains(report, "版本: RMV2_V7");
            StringAssert.Contains(report, "骨架: test_skeleton");
            StringAssert.Contains(report, "LOD 数量: 1");
            StringAssert.Contains(report, "LOD 0");
            StringAssert.Contains(report, "显示距离: 12.5");
            StringAssert.Contains(report, "网格数量: 1");
            StringAssert.Contains(report, "网格 0: test_mesh");
            StringAssert.Contains(report, "面数: 1");
            StringAssert.Contains(report, "材质: weighted_skin");
            StringAssert.Contains(report, "顶点格式: Cinematic");
            StringAssert.Contains(report, @"纹理 BaseColour: textures\body_base.dds");
            StringAssert.Contains(report, @"纹理 Normal: textures\body_normal.dds");
            StringAssert.Contains(report, "顶点数量: 3");
            StringAssert.Contains(report, "顶点 0");
            StringAssert.Contains(report, "位置: (1.5, 2.25, 3.75, 1)");
            StringAssert.Contains(report, "骨骼权重: 2=0.75, 5=0.25");
        }

        [TestMethod]
        public void Format_UsesInvariantCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

                var report = new RmvToTextReport().Format(CreateRmvFile());

                StringAssert.Contains(report, "显示距离: 12.5");
                StringAssert.Contains(report, "位置: (1.5, 2.25, 3.75, 1)");
                StringAssert.Contains(report, "骨骼权重: 2=0.75, 5=0.25");
                Assert.IsFalse(report.Contains("显示距离: 12,5", StringComparison.Ordinal));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [TestMethod]
        public void CommandAndMenu_AreAvailableOnlyForRigidModelFiles()
        {
            var serviceProvider = new DependencyInjectionConfig(false).Build(true);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var services = scope.ServiceProvider;
                services.GetRequiredService<LocalizationManager>()
                    .LoadLanguage();
                var command = services.GetRequiredService<IRmvToTextCommand>();
                var owner = new PackFileContainer("test.pack");
                var rigidModelNode = CreateFileNode(owner, "model.rigid_model_v2");
                var uppercaseRigidModelNode = CreateFileNode(owner, "MODEL.RIGID_MODEL_V2");
                var textureNode = CreateFileNode(owner, "texture.dds");

                Assert.IsTrue(command.IsEnabled(rigidModelNode));
                Assert.IsTrue(command.IsEnabled(uppercaseRigidModelNode));
                Assert.IsFalse(command.IsEnabled(textureNode));

                var menuBuilder = services.GetServices<IContextMenuBuilder>()
                    .Single(builder => builder.Type == ContextMenuType.MainApplication);
                var menu = menuBuilder.Build(rigidModelNode);
                var textureMenu = menuBuilder.Build(textureNode);
                var displayName = command.GetDisplayName(rigidModelNode);
                var reportsDisplayName =
                    LocalizationManager.Instance.Get("ContextMenu.Reports");

                Assert.IsTrue(ContainsMenuItem(menu, displayName));
                Assert.IsFalse(
                    ContainsMenuItem(textureMenu, displayName));
                Assert.IsFalse(
                    ContainsMenuItem(textureMenu, reportsDisplayName));
            }
            finally
            {
                (serviceProvider as IDisposable)?.Dispose();
            }
        }

        [TestMethod]
        public void Generate_RealRigidModel_WritesReadableReport()
        {
            var outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "AssetEditorRmvReportTests",
                Guid.NewGuid().ToString("N"));
            var selectedOutput = string.Empty;
            var report = CreateReport(
                outputDirectory,
                path => selectedOutput = path);
            var sourcePath = Path.Combine(
                FindRepositoryRoot(),
                "Data",
                "Rome_Man_And_Shield_Pack",
                "variantmeshes",
                "_variantmodels",
                "man",
                "shield",
                "celtic_oval_shield_a.rigid_model_v2");
            var packFile = PackFile.CreateFromBytes(
                Path.GetFileName(sourcePath),
                File.ReadAllBytes(sourcePath));

            try
            {
                var outputPath = report.Generate(
                    packFile,
                    @"units\first\shared_name.rigid_model_v2");
                var secondOutputPath = report.Generate(
                    packFile,
                    @"units\second\shared_name.rigid_model_v2");

                Assert.IsTrue(File.Exists(outputPath));
                Assert.IsTrue(File.Exists(secondOutputPath));
                Assert.AreNotEqual(outputPath, secondOutputPath);
                Assert.AreEqual(secondOutputPath, selectedOutput);
                var generatedText = File.ReadAllText(outputPath);
                StringAssert.Contains(
                    generatedText,
                    "RMV2 模型诊断报告");
                StringAssert.Contains(generatedText, "LOD 数量:");
                StringAssert.Contains(generatedText, "顶点:");
            }
            finally
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, true);
            }
        }

        private static RmvToTextReport CreateReport(
            string outputDirectory,
            Action<string> openOutput)
        {
            var constructor = typeof(RmvToTextReport).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(string), typeof(Action<string>)],
                modifiers: null);

            Assert.IsNotNull(
                constructor,
                "RMV report must expose an internal injectable constructor.");
            return (RmvToTextReport)constructor.Invoke(
                [outputDirectory, openOutput]);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "AssetEditor.CN.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Unable to locate AssetEditor.CN.sln.");
        }

        private static TreeNode CreateFileNode(PackFileContainer owner, string name)
        {
            var packFile = PackFile.CreateFromBytes(name, []);
            return new TreeNode(name, NodeType.File, owner, null, packFile);
        }

        private static bool ContainsMenuItem(
            IEnumerable<ContextMenuItem2?> items,
            string displayName)
        {
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (item.Name == displayName ||
                    ContainsMenuItem(item.ContextMenu, displayName))
                {
                    return true;
                }
            }

            return false;
        }

        private static RmvFile CreateRmvFile()
        {
            var material = new WeightedMaterial
            {
                MaterialId = ModelMaterialEnum.weighted_skin,
                BinaryVertexFormat = VertexFormat.Cinematic,
                ModelName = "test_mesh"
            };
            material.SetTexture(TextureType.BaseColour, @"textures\body_base.dds");
            material.SetTexture(TextureType.Normal, @"textures\body_normal.dds");

            var vertices = new[]
            {
                new CommonVertex
                {
                    Position = new Vector4(1.5f, 2.25f, 3.75f, 1),
                    Uv = new Vector2(0.125f, 0.5f),
                    WeightCount = 2,
                    BoneIndex = [2, 5],
                    BoneWeight = [0.75f, 0.25f]
                },
                new CommonVertex
                {
                    Position = new Vector4(0, 0, 0, 1),
                    BoneIndex = [],
                    BoneWeight = []
                },
                new CommonVertex
                {
                    Position = new Vector4(1, 0, 0, 1),
                    BoneIndex = [],
                    BoneWeight = []
                }
            };

            var model = new RmvModel
            {
                CommonHeader = new RmvCommonHeader
                {
                    ModelTypeFlag = ModelMaterialEnum.weighted_skin,
                    VertexCount = 3,
                    IndexCount = 3
                },
                Material = material,
                Mesh = new RmvMesh
                {
                    VertexList = vertices,
                    IndexList = [0, 1, 2]
                }
            };

            return new RmvFile
            {
                Header = new RmvFileHeader
                {
                    _fileType = [0x52, 0x4D, 0x56, 0x32],
                    Version = RmvVersionEnum.RMV2_V7,
                    LodCount = 1,
                    SkeletonName = "test_skeleton"
                },
                LodHeaders =
                [
                    new Rmv2LodHeader_V7_V8
                    {
                        MeshCount = 1,
                        LodCameraDistance = 12.5f,
                        QualityLvl = 2
                    }
                ],
                ModelList = [[model]]
            };
        }
    }
}
