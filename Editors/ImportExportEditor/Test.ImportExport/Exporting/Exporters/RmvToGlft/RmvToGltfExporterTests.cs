using Editors.ImportExport.Exporting.Exporters.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Exporters.DdsToNormalPng;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.TestUtility;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using Test.TestingUtility.TestUtility;

namespace Test.ImportExport.Exporting.Exporters.RmvToGlft
{

    public class RmvToGltfExporterTests
    {
        private readonly string _inputPackFileKarl = PathHelper.GetDataFolder("Data\\Karl_and_celestialgeneral_Pack");
        private readonly string _rmvFilePathKarl = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";

        [Test]
        public void Test()
        {
            // Arrange 
            var pfs = PackFileSerivceTestHelper.Create(_inputPackFileKarl);
            var meshBuilder = new GltfMeshBuilder();
            var normalExporter = new Mock<IDdsToNormalPngExporter>();
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            var eventHub = new Mock<IGlobalEventHub>();
            var skeletontonLookupHelper = new SkeletonAnimationLookUpHelper(pfs, eventHub.Object);            
            var skeletontonBuilder = new GltfSkeletonBuilder(pfs);
            var animationBuilder = new GltfAnimationBuilder(pfs);
            var textureHandler = new GltfTextureHandler(
                normalExporter.Object,
                materialExporter.Object,
                pfs);
            var sceneSaver = new TestGltfSceneSaver();

            // Act
            var mesh = pfs.FindFile(_rmvFilePathKarl);
            var exporter = new RmvToGltfExporter(sceneSaver, meshBuilder, textureHandler, skeletontonBuilder, animationBuilder, skeletontonLookupHelper);
            var settings = new RmvToGltfExporterSettings(mesh!, [], @"C:\test\myExport.gltf", false, true, true, true, true);
            exporter.Export(settings);

            // Assert
            Assert.That(sceneSaver.IsSaveCalled, Is.True);

            Assert.That(sceneSaver.ModelRoot, Is.Not.Null);
            Assert.That(sceneSaver.ModelRoot!.LogicalMaterials.Count(), Is.EqualTo(4));

            // Validate a materials and textues. Texture paths are not easy to validate, as gltf check file exists on disk 
            // which we do not want
            //sceneSaver.ModelRoot!.LogicalMaterials[0].Channels

            Assert.That(sceneSaver.ModelRoot!.LogicalMeshes.Count(), Is.EqualTo(4));
            Assert.That(sceneSaver.ModelRoot.LogicalSkins, Is.Not.Empty);
            Assert.That(
                sceneSaver.ModelRoot.DefaultScene!.VisualChildren
                    .Where(node => node.Mesh != null),
                Has.All.Matches<Node>(node => node.Skin != null));
            // Validate a mesh

            // Validate skeleton

        }

        [Test]
        public void ExportSkeletonDisabled_ExportsUnskinnedMeshes()
        {
            var pfs = PackFileSerivceTestHelper.Create(_inputPackFileKarl);
            var sceneSaver = new TestGltfSceneSaver();
            var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            var exporter = new RmvToGltfExporter(
                sceneSaver,
                new GltfMeshBuilder(),
                Mock.Of<IGltfTextureHandler>(handler =>
                    handler.HandleTextures(It.IsAny<RmvFile>(), It.IsAny<RmvToGltfExporterSettings>()) ==
                    new List<TextureResult>()),
                new GltfSkeletonBuilder(pfs),
                new GltfAnimationBuilder(pfs),
                skeletonLookup.Object);
            var settings = new RmvToGltfExporterSettings(
                pfs.FindFile(_rmvFilePathKarl)!,
                [],
                @"C:\test\unskinned.gltf",
                false,
                false,
                false,
                false,
                true,
                ExportSkeleton: false);

            exporter.Export(settings);

            Assert.Multiple(() =>
            {
                Assert.That(sceneSaver.ModelRoot, Is.Not.Null);
                Assert.That(sceneSaver.ModelRoot!.LogicalSkins, Is.Empty);
                Assert.That(
                    sceneSaver.ModelRoot.DefaultScene!.VisualChildren
                        .Where(node => node.Mesh != null),
                    Has.All.Matches<Node>(node => node.Skin == null));
            });
            skeletonLookup.Verify(
                lookup => lookup.GetSkeletonFileFromName(It.IsAny<string>()),
                Times.Never);
        }

        [Test]
        public void MissingTexture_StopsExportInsteadOfSilentlyOmittingIt()
        {
            var pfs = PackFileSerivceTestHelper.Create(_inputPackFileKarl);
            var rmvPackFile = pfs.FindFile(_rmvFilePathKarl)!;
            var rmvFile = new Shared.GameFormats.RigidModel.ModelFactory().Load(
                rmvPackFile.DataSource.ReadData());
            var normalExporter = new Mock<IDdsToNormalPngExporter>();
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            var textureHandler = new GltfTextureHandler(
                normalExporter.Object,
                materialExporter.Object,
                pfs);
            var settings = new RmvToGltfExporterSettings(
                rmvPackFile,
                [],
                @"C:\test\myExport.gltf",
                true,
                true,
                true,
                false,
                true);

            Assert.Throws<FileNotFoundException>(() =>
                textureHandler.HandleTextures(rmvFile, settings));
        }

        [Test]
        public void TextureAtUniqueMatchingFileName_IsResolvedWhenReferencedPathIsStale()
        {
            const string referencedPath =
                @"variantmeshes\wh_variantmodels\dr1b\chs\chs_dragon\tex\chs_knights_sword_1h_01_base_colour.dds";
            const string loadedPath =
                @"variantmeshes\wh_variantmodels\hu1c\chs\chs_props\tex\chs_knights_sword_1h_01_base_colour.dds";
            var material = new WeightedMaterial();
            material.SetTexture(TextureType.BaseColour, referencedPath);
            var rmvFile = new RmvFile
            {
                ModelList =
                [
                    [new RmvModel { Material = material }],
                ],
            };
            var textureFile = new PackFile(
                Path.GetFileName(loadedPath),
                new MemorySource([]));
            var container = new PackFileContainer("game");
            container.FileList[loadedPath] = textureFile;
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(service => service.FindFile(
                    It.IsAny<string>(),
                    It.IsAny<PackFileContainer?>()))
                .Returns((string path, PackFileContainer? _) =>
                    string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase)
                        ? textureFile
                        : null);
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            materialExporter
                .Setup(exporter => exporter.Export(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>()))
                .Returns((string path, string _, bool _, string _) =>
                    packFileService.Object.FindFile(path) == null
                        ? string.Empty
                        : @"C:\test\chs_knights_sword_1h_01_base_colour.png");
            var textureHandler = new GltfTextureHandler(
                Mock.Of<IDdsToNormalPngExporter>(),
                materialExporter.Object,
                packFileService.Object);
            var settings = new RmvToGltfExporterSettings(
                new PackFile("model.rigid_model_v2", new MemorySource([])),
                [],
                @"C:\test\model.gltf",
                true,
                false,
                false,
                false,
                true,
                SelectedGame: GameTypeEnum.Warhammer3);

            Assert.DoesNotThrow(() => textureHandler.HandleTextures(rmvFile, settings));
            materialExporter.Verify(exporter => exporter.Export(
                loadedPath,
                It.IsAny<string>(),
                false,
                It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void MultipleMatchingTextureFileNames_AreNotSelectedArbitrarily()
        {
            const string referencedPath = @"stale\body_base_colour.dds";
            const string firstLoadedPath = @"textures\first\body_base_colour.dds";
            const string secondLoadedPath = @"textures\second\body_base_colour.dds";
            var material = new WeightedMaterial();
            material.SetTexture(TextureType.BaseColour, referencedPath);
            var rmvFile = new RmvFile
            {
                ModelList =
                [
                    [new RmvModel { Material = material }],
                ],
            };
            var container = new PackFileContainer("game");
            container.FileList[firstLoadedPath] = new PackFile(
                Path.GetFileName(firstLoadedPath),
                new MemorySource([]));
            container.FileList[secondLoadedPath] = new PackFile(
                Path.GetFileName(secondLoadedPath),
                new MemorySource([]));
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetAllPackfileContainers())
                .Returns([container]);
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            var textureHandler = new GltfTextureHandler(
                Mock.Of<IDdsToNormalPngExporter>(),
                materialExporter.Object,
                packFileService.Object);
            var settings = new RmvToGltfExporterSettings(
                new PackFile("model.rigid_model_v2", new MemorySource([])),
                [],
                @"C:\test\model.gltf",
                true,
                false,
                false,
                false,
                true,
                SelectedGame: GameTypeEnum.Warhammer3);

            Assert.Throws<FileNotFoundException>(() =>
                textureHandler.HandleTextures(rmvFile, settings));
            materialExporter.Verify(exporter => exporter.Export(
                referencedPath,
                It.IsAny<string>(),
                false,
                It.IsAny<string>()), Times.Once);
            materialExporter.Verify(exporter => exporter.Export(
                firstLoadedPath,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>()), Times.Never);
            materialExporter.Verify(exporter => exporter.Export(
                secondLoadedPath,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Warhammer3_LegacySpecularPlaceholder_IsIgnored()
        {
            var material = new WeightedMaterial();
            material.SetTexture(TextureType.BaseColour, @"textures\body_base_colour.dds");
            material.SetTexture(TextureType.Specular, @"textures\body_specular.dds");
            var rmvFile = new RmvFile
            {
                ModelList =
                [
                    [new RmvModel { Material = material }],
                ],
            };
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            materialExporter
                .Setup(exporter => exporter.Export(
                    @"textures\body_base_colour.dds",
                    It.IsAny<string>(),
                    false,
                    It.IsAny<string>()))
                .Returns(@"C:\test\body_base_colour.png");
            var textureHandler = new GltfTextureHandler(
                Mock.Of<IDdsToNormalPngExporter>(),
                materialExporter.Object,
                Mock.Of<IPackFileService>());
            var settings = new RmvToGltfExporterSettings(
                new PackFile("model.rigid_model_v2", new MemorySource([])),
                [],
                @"C:\test\model.gltf",
                true,
                false,
                false,
                false,
                true,
                SelectedGame: GameTypeEnum.Warhammer3);

            var results = textureHandler.HandleTextures(rmvFile, settings);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].GlftTexureType, Is.EqualTo(KnownChannel.BaseColor));
            materialExporter.Verify(exporter => exporter.Export(
                @"textures\body_specular.dds",
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Warhammer2_SpecularTexture_IsStillExported()
        {
            var material = new WeightedMaterial();
            material.SetTexture(TextureType.Specular, @"textures\body_specular.dds");
            var rmvFile = new RmvFile
            {
                ModelList =
                [
                    [new RmvModel { Material = material }],
                ],
            };
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            materialExporter
                .Setup(exporter => exporter.Export(
                    @"textures\body_specular.dds",
                    It.IsAny<string>(),
                    false,
                    It.IsAny<string>()))
                .Returns(@"C:\test\body_specular.png");
            var textureHandler = new GltfTextureHandler(
                Mock.Of<IDdsToNormalPngExporter>(),
                materialExporter.Object,
                Mock.Of<IPackFileService>());
            var settings = new RmvToGltfExporterSettings(
                new PackFile("model.rigid_model_v2", new MemorySource([])),
                [],
                @"C:\test\model.gltf",
                true,
                false,
                false,
                false,
                true,
                SelectedGame: GameTypeEnum.Warhammer2);

            var results = textureHandler.HandleTextures(rmvFile, settings);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].GlftTexureType, Is.EqualTo(KnownChannel.SpecularColor));
        }

        [Test]
        public void SameBaseNameFromDifferentPackPaths_GetsDistinctOutputNames()
        {
            var firstMaterial = new WeightedMaterial();
            firstMaterial.SetTexture(TextureType.BaseColour, @"textures\first\shared.dds");
            var secondMaterial = new WeightedMaterial();
            secondMaterial.SetTexture(TextureType.BaseColour, @"textures\second\shared.dds");
            var rmvFile = new RmvFile
            {
                ModelList =
                [
                    [
                        new RmvModel { Material = firstMaterial },
                        new RmvModel { Material = secondMaterial },
                    ],
                ],
            };
            var materialExporter = new Mock<IDdsToMaterialPngExporter>();
            materialExporter
                .Setup(exporter => exporter.Export(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>()))
                .Returns((string _, string _, bool _, string outputFileName) =>
                    Path.Combine(@"C:\test", outputFileName));
            var textureHandler = new GltfTextureHandler(
                Mock.Of<IDdsToNormalPngExporter>(),
                materialExporter.Object,
                Mock.Of<IPackFileService>());
            var settings = new RmvToGltfExporterSettings(
                new PackFile("model.rigid_model_v2", new MemorySource([])),
                [],
                @"C:\test\model.gltf",
                true,
                false,
                false,
                false,
                true);

            var results = textureHandler.HandleTextures(rmvFile, settings);

            Assert.That(
                results.Select(result => Path.GetFileName(result.SystemFilePath)).Distinct().ToList(),
                Has.Count.EqualTo(2));
        }
    }
}
