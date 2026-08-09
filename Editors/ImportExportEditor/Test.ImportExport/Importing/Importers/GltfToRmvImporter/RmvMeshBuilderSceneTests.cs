using System.Numerics;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.TestUtility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Core.Settings;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;

namespace Test.ImportExport.Importing.Importers.GltfImporterTest;

public class RmvMeshBuilderSceneTests
{
    [Test]
    public void Build_UsesEveryPrimitiveAndSceneNodeInstance()
    {
        var geometry = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>("shared_mesh");
        AddTriangle(geometry.UsePrimitive(CreateMaterial("first")), 0);
        AddTriangle(geometry.UsePrimitive(CreateMaterial("second")), 2);

        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        var scene = modelRoot.UseScene("default");
        scene.CreateNode("first_instance").WithMesh(mesh);
        var translatedNode = scene.CreateNode("second_instance").WithMesh(mesh);
        translatedNode.LocalMatrix = Matrix4x4.CreateTranslation(5, 0, 0);

        var result = RmvMeshBuilder.Build(CreateSettings(), modelRoot, null, "");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ModelList[0], Has.Length.EqualTo(4));
        Assert.That(result.LodHeaders[0].MeshCount, Is.EqualTo(4));
        Assert.That(result.ModelList[0].Select(x => x.Material.ModelName), Is.EqualTo(new[]
        {
            "first_instance_part1",
            "first_instance_part2",
            "second_instance_part1",
            "second_instance_part2",
        }));

        var firstInstanceX = result.ModelList[0][0].Mesh.VertexList[0].Position.X;
        var secondInstanceX = result.ModelList[0][2].Mesh.VertexList[0].Position.X;
        Assert.That(secondInstanceX, Is.EqualTo(firstInstanceX - 5).Within(0.0001f));
    }

    [Test]
    public void Import_GlbContainer_AddsRmvFile()
    {
        var geometry = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>("mesh");
        AddTriangle(geometry.UsePrimitive(CreateMaterial("material")), 0);
        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        modelRoot.UseScene("default").CreateNode("mesh_node").WithMesh(mesh);

        var glbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(glbPath);
            var packFileService = PackFileSerivceTestHelper.Create(TestData.InputPack);
            var standardDialogs = Mock.Of<IStandardDialogs>();
            var materialBuilder = new RmvMaterialBuilder(packFileService, standardDialogs);
            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                materialBuilder);
            var destination = new PackFileContainer("test");
            var settings = new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                true,
                false,
                false,
                false,
                20,
                true);

            var succeeded = importer.Import(settings);

            Assert.That(succeeded, Is.True);
            Assert.That(
                destination.FileList.Keys,
                Does.Contain($"models\\{Path.GetFileNameWithoutExtension(glbPath)}.rigid_model_v2".ToLowerInvariant()));
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    [Test]
    public void Import_MultiplePartialAnimations_CreatesOneAnimPerClip()
    {
        var geometry = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>("mesh");
        AddTriangle(geometry.UsePrimitive(CreateMaterial("material")), 0);
        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        var scene = modelRoot.UseScene("default");
        scene.CreateNode("mesh_node").WithMesh(mesh);
        scene.CreateNode("//skeleton//test_skeleton");
        var boneNode = scene.CreateNode("root");

        foreach (var animationName in new[] { "Idle Pose", "Walk" })
        {
            var animation = modelRoot.CreateAnimation(animationName);
            animation.CreateTranslationChannel(boneNode, new Dictionary<float, Vector3>
            {
                [0] = Vector3.Zero,
                [0.1f] = Vector3.One,
            });
        }

        var skeleton = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "test_skeleton" },
            Bones =
            [
                new AnimationFile.BoneInfo { Id = 0, ParentId = -1, Name = "root" },
            ],
            AnimationParts =
            [
                new AnimationFile.AnimationPart
                {
                    DynamicFrames =
                    [
                        new AnimationFile.Frame
                        {
                            Transforms = [new RmvVector3(0, 0, 0)],
                            Quaternion = [new RmvVector4(0, 0, 0, 1)],
                        },
                    ],
                },
            ],
        };

        var glbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(glbPath);
            var packFileService = PackFileSerivceTestHelper.Create(TestData.InputPack);
            var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            skeletonLookup
                .Setup(service => service.GetSkeletonFileFromName("test_skeleton"))
                .Returns(skeleton);
            var materialBuilder = new RmvMaterialBuilder(
                packFileService,
                Mock.Of<IStandardDialogs>());
            var importer = new GltfImporter(
                packFileService,
                skeletonLookup.Object,
                materialBuilder);
            var destination = new PackFileContainer("test");
            var settings = new GltfImporterSettings(
                glbPath,
                "animations",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                false,
                false,
                false,
                true,
                20,
                true);

            importer.Import(settings);

            var baseName = Path.GetFileNameWithoutExtension(glbPath).ToLowerInvariant();
            Assert.Multiple(() =>
            {
                Assert.That(destination.FileList.Keys, Does.Contain($"animations\\{baseName}_idle_pose.anim"));
                Assert.That(destination.FileList.Keys, Does.Contain($"animations\\{baseName}_walk.anim"));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    [Test]
    public void Import_MissingGameSkeleton_CreatesSkeletonAnim()
    {
        var modelRoot = CreateSkinnedModelRoot("test_skeleton");
        var glbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(glbPath);
            var packFileService = PackFileSerivceTestHelper.Create(TestData.InputPack);
            var materialBuilder = new RmvMaterialBuilder(
                packFileService,
                Mock.Of<IStandardDialogs>());
            var importer = new GltfImporter(
                packFileService,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                materialBuilder);
            var destination = new PackFileContainer("test");
            var settings = new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                false,
                false,
                false,
                false,
                20,
                true);

            importer.Import(settings);

            var skeletonFile = destination.FileList["animations\\skeletons\\test_skeleton.anim"];
            var skeleton = AnimationFile.Create(skeletonFile);
            Assert.Multiple(() =>
            {
                Assert.That(skeleton.Header.SkeletonName, Is.EqualTo("test_skeleton"));
                Assert.That(skeleton.Bones.Select(bone => bone.Name), Is.EqualTo(new[] { "root", "child" }));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    [Test]
    public void Import_ExistingGameSkeleton_DoesNotCopySkeletonAnim()
    {
        var modelRoot = CreateSkinnedModelRoot("test_skeleton");
        var glbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(glbPath);
            var packFileService = PackFileSerivceTestHelper.Create(TestData.InputPack);
            var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            skeletonLookup
                .Setup(service => service.GetSkeletonFileFromName("test_skeleton"))
                .Returns(CreateSkeletonFile("test_skeleton"));
            var materialBuilder = new RmvMaterialBuilder(
                packFileService,
                Mock.Of<IStandardDialogs>());
            var importer = new GltfImporter(
                packFileService,
                skeletonLookup.Object,
                materialBuilder);
            var destination = new PackFileContainer("test");
            var settings = new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                true,
                false,
                false,
                false,
                false,
                20,
                true);

            importer.Import(settings);

            Assert.Multiple(() =>
            {
                Assert.That(
                    destination.FileList.Keys,
                    Does.Contain($"models\\{Path.GetFileNameWithoutExtension(glbPath)}.rigid_model_v2".ToLowerInvariant()));
                Assert.That(
                    destination.FileList.Keys,
                    Does.Not.Contain("animations\\skeletons\\test_skeleton.anim"));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    private static GltfImporterSettings CreateSettings() => new(
        "scene.gltf",
        "models",
        new PackFileContainer("test"),
        GameTypeEnum.Warhammer3,
        true,
        false,
        false,
        false,
        false,
        20,
        true);

    private static ModelRoot CreateSkinnedModelRoot(string skeletonName)
    {
        var geometry = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>("mesh");
        AddTriangle(geometry.UsePrimitive(CreateMaterial("material")), 0);
        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        var scene = modelRoot.UseScene("default");
        scene.CreateNode($"//skeleton//{skeletonName}");
        var root = scene.CreateNode("root");
        var child = root.CreateNode("child");
        scene.CreateNode("mesh_node").WithSkinnedMesh(
            mesh,
            (root, Matrix4x4.Identity),
            (child, Matrix4x4.Identity));
        return modelRoot;
    }

    private static AnimationFile CreateSkeletonFile(string skeletonName)
    {
        var frame = new AnimationFile.Frame
        {
            Transforms = [new RmvVector3(0, 0, 0), new RmvVector3(0, 1, 0)],
            Quaternion = [new RmvVector4(0, 0, 0, 1), new RmvVector4(0, 0, 0, 1)],
        };
        return new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                SkeletonName = skeletonName,
                AnimationTotalPlayTimeInSec = 0.1f,
            },
            Bones =
            [
                new AnimationFile.BoneInfo { Id = 0, ParentId = -1, Name = "root" },
                new AnimationFile.BoneInfo { Id = 1, ParentId = 0, Name = "child" },
            ],
            AnimationParts =
            [
                new AnimationFile.AnimationPart
                {
                    DynamicFrames = [frame, frame],
                },
            ],
        };
    }

    private static MaterialBuilder CreateMaterial(string name) =>
        new MaterialBuilder(name).WithMetallicRoughness();

    private static void AddTriangle(
        IPrimitiveBuilder primitive,
        float xOffset)
    {
        primitive.AddTriangle(
            CreateVertex(xOffset, 0),
            CreateVertex(xOffset + 1, 0),
            CreateVertex(xOffset, 1));
    }

    private static VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> CreateVertex(
        float x,
        float y)
    {
        var vertex = new VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>();
        vertex.Geometry.Position = new Vector3(x, y, 0);
        vertex.Geometry.Normal = Vector3.UnitZ;
        vertex.Geometry.Tangent = new Vector4(1, 0, 0, 1);
        vertex.Material.TexCoord = new Vector2(x, y);
        vertex.Skinning.SetBindings((0, 1), (0, 0), (0, 0), (0, 0));
        return vertex;
    }
}
