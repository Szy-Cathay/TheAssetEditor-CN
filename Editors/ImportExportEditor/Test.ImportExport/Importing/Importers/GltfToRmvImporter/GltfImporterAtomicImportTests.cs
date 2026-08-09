using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.TestUtility;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;

namespace Test.ImportExport.Importing.Importers.GltfImporterTest;

public class GltfImporterAtomicImportTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void Import_ModelTargetExists_ReturnsConflictAndLeavesPackUnchanged()
    {
        var glbPath = CreateStaticGlb();
        try
        {
            var packFileService = PackFileSerivceTestHelper.Create(
                TestData.InputPack);
            var destination = new PackFileContainer("test");
            var targetPath = $"models\\{Path.GetFileNameWithoutExtension(glbPath)}.rigid_model_v2"
                .ToLowerInvariant();
            var existingFile = new PackFile(
                Path.GetFileName(targetPath),
                new MemorySource([1, 2, 3]));
            destination.FileList[targetPath] = existingFile;
            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new RmvMaterialBuilder());

            var result = importer.Import(new GltfImporterSettings(
                glbPath,
                " models ",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                false,
                false,
                false,
                false,
                20,
                true));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Errors, Has.Some.Contains(targetPath));
                Assert.That(destination.FileList, Has.Count.EqualTo(1));
                Assert.That(destination.FileList[targetPath], Is.SameAs(existingFile));
                Assert.That(
                    destination.FileList[targetPath].DataSource.ReadData(),
                    Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    [Test]
    public void Import_InvalidAnimationRateAfterSkeletonAndMeshBuild_ReturnsFailureAndLeavesPackUnchanged()
    {
        var glbPath = CreateAnimatedSkinnedGlb();
        try
        {
            var packFileService = PackFileSerivceTestHelper.Create(
                TestData.InputPack);
            var destination = new PackFileContainer("test");
            const string existingPath = @"models\existing.bin";
            var existingFile = new PackFile(
                "existing.bin",
                new MemorySource([7, 8, 9]));
            destination.FileList[existingPath] = existingFile;
            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new RmvMaterialBuilder());
            var settings = new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                false,
                false,
                false,
                true,
                0,
                true,
                false);

            ImportResult? result = null;
            Exception? thrownException = null;
            try
            {
                result = importer.Import(settings);
            }
            catch (Exception exception)
            {
                thrownException = exception;
            }

            Assert.Multiple(() =>
            {
                Assert.That(thrownException, Is.Null);
                Assert.That(result?.Succeeded, Is.False);
                Assert.That(result?.Errors, Is.Not.Empty);
                Assert.That(destination.FileList, Has.Count.EqualTo(1));
                Assert.That(destination.FileList[existingPath], Is.SameAs(existingFile));
                Assert.That(
                    existingFile.DataSource.ReadData(),
                    Is.EqualTo(new byte[] { 7, 8, 9 }));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    [Test]
    public void Import_FolderProjectDiskConflictsMissingFromFileList_ReportsEveryConflictAndLeavesDiskUnchanged()
    {
        var glbPath = CreateAnimatedSkinnedGlb();
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"AssetEditorGltfImport-{Guid.NewGuid():N}");
        var baseName = Path.GetFileNameWithoutExtension(glbPath)
            .ToLowerInvariant();
        var conflictPaths = new[]
        {
            $@"models\{baseName}.anim",
            $@"models\{baseName}.rigid_model_v2",
        };
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "models"));
            for (var index = 0; index < conflictPaths.Length; index++)
            {
                File.WriteAllBytes(
                    Path.Combine(projectRoot, conflictPaths[index]),
                    [(byte)(index + 1)]);
            }

            using var destination = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "test" });
            foreach (var conflictPath in conflictPaths)
                destination.FileList.Remove(conflictPath);
            var packFileService = PackFileSerivceTestHelper.Create(
                TestData.InputPack);
            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new RmvMaterialBuilder());

            var result = importer.Import(new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                false,
                false,
                false,
                true,
                20,
                true));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Errors, Has.Count.EqualTo(conflictPaths.Length));
                foreach (var conflictPath in conflictPaths)
                    Assert.That(result.Errors, Has.Some.Contains(conflictPath));
                for (var index = 0; index < conflictPaths.Length; index++)
                {
                    Assert.That(
                        File.ReadAllBytes(Path.Combine(projectRoot, conflictPaths[index])),
                        Is.EqualTo(new[] { (byte)(index + 1) }));
                }
                Assert.That(
                    File.Exists(Path.Combine(
                        projectRoot,
                        @"animations\skeletons\test_skeleton.anim")),
                    Is.False);
            });
        }
        finally
        {
            File.Delete(glbPath);
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Test]
    public void Import_ModelSkeletonAnimationAndTexture_SucceedsWithOnePackWriteAndReportsEveryOutput()
    {
        var imagePath = CreatePng();
        var glbPath = CreateAnimatedSkinnedGlb(imagePath);
        try
        {
            var innerPackFileService = PackFileSerivceTestHelper.Create(
                TestData.InputPack);
            var destination = new PackFileContainer("test");
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.AddFilesToPack(
                    destination,
                    It.IsAny<List<NewPackFileEntry>>(),
                    It.IsAny<bool>()))
                .Callback<PackFileContainer, List<NewPackFileEntry>, bool>(
                    (container, entries, overwriteExisting) =>
                        innerPackFileService.AddFilesToPack(
                            container,
                            entries,
                            overwriteExisting));
            var importer = new GltfImporter(
                packFileService.Object,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new RmvMaterialBuilder());

            var result = importer.Import(new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                true,
                false,
                false,
                true,
                20,
                true));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Errors, Is.Empty);
                Assert.That(result.Warnings, Is.Empty);
                Assert.That(result.OutputPaths, Is.EquivalentTo(destination.FileList.Keys));
                Assert.That(destination.FileList.Keys, Has.Some.EndsWith(".rigid_model_v2"));
                Assert.That(destination.FileList.Keys, Has.Some.EndsWith(".dds"));
                Assert.That(destination.FileList.Keys.Count(path => path.EndsWith(".anim")), Is.EqualTo(2));
            });
            packFileService.Verify(
                service => service.AddFilesToPack(
                    destination,
                    It.IsAny<List<NewPackFileEntry>>(),
                    It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            File.Delete(glbPath);
            File.Delete(imagePath);
        }
    }

    [Test]
    public void Import_AllOutputTargetsExist_ReportsEveryConflictAndLeavesPackUnchanged()
    {
        var imagePath = CreatePng();
        var glbPath = CreateAnimatedSkinnedGlb(imagePath);
        try
        {
            var packFileService = PackFileSerivceTestHelper.Create(
                TestData.InputPack);
            var destination = new PackFileContainer("test");
            var baseName = Path.GetFileNameWithoutExtension(glbPath)
                .ToLowerInvariant();
            var conflictPaths = new[]
            {
                @"animations\skeletons\test_skeleton.anim",
                $@"models\{baseName}.anim",
                @"models\tex\mesh_node_base_colour.dds",
                $@"models\{baseName}.rigid_model_v2",
            };
            var existingFiles = conflictPaths
                .Select((path, index) => new
                {
                    Path = index == 1 ? path.ToUpperInvariant() : path,
                    Content = (byte)(index + 1),
                    File = new PackFile(
                        Path.GetFileName(path),
                        new MemorySource([(byte)(index + 1)])),
                })
                .ToList();
            foreach (var existing in existingFiles)
                destination.FileList[existing.Path] = existing.File;

            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new RmvMaterialBuilder());
            var result = importer.Import(new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                true,
                false,
                false,
                true,
                20,
                true));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Errors, Has.Count.EqualTo(conflictPaths.Length));
                foreach (var conflictPath in conflictPaths)
                    Assert.That(result.Errors, Has.Some.Contains(conflictPath));
                Assert.That(destination.FileList, Has.Count.EqualTo(existingFiles.Count));
                foreach (var existing in existingFiles)
                {
                    Assert.That(destination.FileList[existing.Path], Is.SameAs(existing.File));
                    Assert.That(
                        existing.File.DataSource.ReadData(),
                        Is.EqualTo(new[] { existing.Content }));
                }
            });
        }
        finally
        {
            File.Delete(glbPath);
            File.Delete(imagePath);
        }
    }

    [Test]
    public void Import_MaterialAtPackRoot_NormalizesTextureTargetPath()
    {
        var imagePath = CreatePng();
        var glbPath = CreateAnimatedSkinnedGlb(imagePath);
        try
        {
            var packFileService = PackFileSerivceTestHelper.Create(
                TestData.InputPack);
            var destination = new PackFileContainer("test");
            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new RmvMaterialBuilder());

            var result = importer.Import(new GltfImporterSettings(
                glbPath,
                "",
                destination,
                GameTypeEnum.Warhammer3,
                false,
                true,
                false,
                false,
                false,
                20,
                true));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.OutputPaths, Is.EqualTo(new[]
                {
                    @"tex\mesh_node_base_colour.dds",
                }));
                Assert.That(destination.FileList.Keys, Is.EquivalentTo(result.OutputPaths));
            });
        }
        finally
        {
            File.Delete(glbPath);
            File.Delete(imagePath);
        }
    }

    private static string CreateStaticGlb()
    {
        var material = new MaterialBuilder("material")
            .WithMetallicRoughness();
        var geometry = new MeshBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>("mesh");
        var primitive = geometry.UsePrimitive(material);
        primitive.AddTriangle(
            CreateVertex(0, 0),
            CreateVertex(1, 0),
            CreateVertex(0, 1));

        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        modelRoot.UseScene("default").CreateNode("mesh_node").WithMesh(mesh);

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        modelRoot.SaveGLB(path);
        return path;
    }

    private static string CreateAnimatedSkinnedGlb(string? imagePath = null)
    {
        var material = new MaterialBuilder("material")
            .WithMetallicRoughness();
        if (imagePath != null)
            material.WithChannelImage(KnownChannel.BaseColor, imagePath);
        var geometry = new MeshBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>("mesh");
        var primitive = geometry.UsePrimitive(material);
        primitive.AddTriangle(
            CreateVertex(0, 0),
            CreateVertex(1, 0),
            CreateVertex(0, 1));

        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        var scene = modelRoot.UseScene("default");
        scene.CreateNode("//skeleton//test_skeleton");
        var root = scene.CreateNode("root");
        scene.CreateNode("mesh_node").WithSkinnedMesh(
            mesh,
            (root, Matrix4x4.Identity));
        modelRoot.CreateAnimation("idle").CreateTranslationChannel(
            root,
            new Dictionary<float, Vector3>
            {
                [0] = Vector3.Zero,
                [0.05f] = Vector3.UnitY,
            });

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        modelRoot.SaveGLB(path);
        return path;
    }

    private static string CreatePng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.White);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static VertexBuilder<
        VertexPositionNormalTangent,
        VertexTexture1,
        VertexJoints4> CreateVertex(float x, float y)
    {
        var vertex = new VertexBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>();
        vertex.Geometry.Position = new Vector3(x, y, 0);
        vertex.Geometry.Normal = Vector3.UnitZ;
        vertex.Geometry.Tangent = new Vector4(1, 0, 0, 1);
        vertex.Material.TexCoord = new Vector2(x, y);
        vertex.Skinning.SetBindings((0, 1), (0, 0), (0, 0), (0, 0));
        return vertex;
    }
}
