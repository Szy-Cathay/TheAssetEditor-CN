using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Importing.Presentation;
using Editors.ImportImport.Importing.Presentation.RmvToGltf;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.RigidModel.Vertex;
using Shared.TestUtility;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using Numerics = System.Numerics;

namespace Test.ImportExport.Importing.Importers.GltfImporterTest;

public class GltfFullWorkflowImportTests
{
    private const string HumanoidSkeletonPath =
        @"animations\skeletons\humanoid01.anim";

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void Import_OfficialBlenderFullWorkflowFixture_CreatesEveryAssetAndChineseSummary()
    {
        var glbPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "TestData",
            "Gltf",
            "external_full_workflow.glb");
        var modelRoot = ModelRoot.Load(glbPath);
        Assert.Multiple(() =>
        {
            Assert.That(
                modelRoot.Asset.Generator,
                Does.StartWith("Khronos glTF Blender I/O"));
            Assert.That(
                modelRoot.LogicalNodes,
                Has.None.Matches<Node>(node =>
                    node.Name?.StartsWith(
                        "//skeleton//",
                        StringComparison.OrdinalIgnoreCase) == true));
            Assert.That(modelRoot.LogicalMeshes.Single().Primitives, Has.Count.EqualTo(2));
            Assert.That(modelRoot.LogicalAnimations.Select(animation => animation.Name),
                Is.EquivalentTo(new[] { "Move", "Nod" }));
            Assert.That(
                modelRoot.LogicalAnimations.Single(animation => animation.Name == "Move")
                    .Channels.Select(channel => channel.TargetNode.Name),
                Is.EqualTo(new[] { "root" }));
            Assert.That(
                modelRoot.LogicalAnimations.Single(animation => animation.Name == "Nod")
                    .Channels.Select(channel => channel.TargetNode.Name),
                Is.EqualTo(new[] { "head" }));
        });

        var destination = new PackFileContainer("test");
        var importer = CreateImporter(destination);
        var result = importer.Import(new GltfImporterSettings(
            glbPath,
            "models",
            destination,
            GameTypeEnum.Warhammer3,
            ImportMeshes: true,
            ImportMaterials: true,
            ConvertMaterialFromBlenderType: true,
            ConvertNormalTextureFromBlueToOrangeType: true,
            ImportAnimations: true,
            AnimationKeysPerSecond: 24,
            MirrorMesh: true,
            AutoDetectAnimationKeysPerSecond: true,
            AutoScaleHumanoid: true));

        Assert.That(
            result.Succeeded,
            Is.True,
            string.Join(Environment.NewLine, result.Errors));

        const string rmvPath = @"models\external_full_workflow.rigid_model_v2";
        const string skeletonPath =
            @"animations\skeletons\externalworkflowarmature.anim";
        var animationPaths = new[]
        {
            @"models\external_full_workflow_move.anim",
            @"models\external_full_workflow_nod.anim",
        };
        var rmv = ModelFactory.Create().Load(
            destination.FileList[rmvPath].DataSource.ReadData());
        var skeleton = AnimationFile.Create(destination.FileList[skeletonPath]);
        var moveAnimation = AnimationFile.Create(
            destination.FileList[animationPaths[0]]);
        var nodAnimation = AnimationFile.Create(
            destination.FileList[animationPaths[1]]);
        var rootBoneIndex = Array.FindIndex(
            skeleton.Bones,
            bone => bone.Name == "root");
        var headBoneIndex = Array.FindIndex(
            skeleton.Bones,
            bone => bone.Name == "head");
        var texturePaths = rmv.ModelList[0]
            .SelectMany(model => model.Material.GetAllTextures())
            .Select(texture => texture.Path.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var message = ImportWindow.BuildResultMessage(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.OutputPaths, Is.EquivalentTo(destination.FileList.Keys));
            Assert.That(result.OutputPaths, Does.Contain(rmvPath));
            Assert.That(result.OutputPaths, Does.Contain(skeletonPath));
            Assert.That(result.OutputPaths, Is.SupersetOf(animationPaths));
            Assert.That(rmv.LodHeaders, Has.Length.EqualTo(1));
            Assert.That(rmv.ModelList[0], Has.Length.EqualTo(2));
            Assert.That(rmv.Header.SkeletonName, Is.EqualTo("ExternalWorkflowArmature"));
            Assert.That(
                rmv.ModelList[0].Select(model => model.Material.ModelName),
                Is.EquivalentTo(new[]
                {
                    "ExternalWorkflowMesh_part1",
                    "ExternalWorkflowMesh_part2",
                }));
            Assert.That(
                rmv.ModelList[0]
                    .SelectMany(model => model.Mesh.VertexList)
                    .Max(vertex => vertex.Position.Y),
                Is.EqualTo(2).Within(0.0001f));
            Assert.That(
                rmv.ModelList[0]
                    .SelectMany(model => model.Mesh.VertexList),
                Has.All.Matches<CommonVertex>(vertex =>
                    vertex.BoneWeight
                        .Select(weight => (byte)(weight * byte.MaxValue))
                        .Sum(value => value) == byte.MaxValue));
            Assert.That(
                skeleton.Bones.Select(bone => bone.Name),
                Is.EqualTo(new[]
                {
                    "root",
                    "pelvis",
                    "head",
                    "left_foot",
                    "right_foot",
                    "helper",
                }));
            Assert.That(animationPaths, Has.All.Matches<string>(path =>
                AnimationFile.Create(destination.FileList[path])
                    .Header.SkeletonName == "ExternalWorkflowArmature"));
            Assert.That(
                moveAnimation.AnimationParts[0].DynamicFrames[^1]
                    .Transforms[rootBoneIndex].Z,
                Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(
                Math.Abs(nodAnimation.AnimationParts[0].DynamicFrames[^1]
                    .Quaternion[headBoneIndex].X),
                Is.EqualTo(MathF.Sin(MathF.PI / 18)).Within(0.001f));
            Assert.That(texturePaths, Has.Count.GreaterThanOrEqualTo(5));
            Assert.That(texturePaths, Has.All.Matches<string>(path =>
                destination.FileList.ContainsKey(path)));
            Assert.That(result.HumanoidScale!.Applied, Is.True);
            Assert.That(result.HumanoidScale.SourceHeight, Is.EqualTo(4).Within(0.0001f));
            Assert.That(result.HumanoidScale.ReferenceHeight, Is.EqualTo(2).Within(0.0001f));
            Assert.That(result.HumanoidScale.ScaleFactor, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(result.Warnings, Has.Some.Contains("6 个顶点"));
            Assert.That(result.Warnings, Has.Some.Contains("15.0%"));
            Assert.That(result.Warnings, Has.Some.Contains("法线"));
            Assert.That(result.Warnings, Has.Some.Contains("切线"));
            Assert.That(
                result.MaterialSummary!.MaskedMaterials.Single().MaterialName,
                Is.EqualTo("MaskMaterial"));
            Assert.That(
                result.MaterialSummary.MaskedMaterials.Single().AlphaCutoff,
                Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(
                result.MaterialSummary.SkippedSemantics.Select(item => item.Semantic),
                Does.Contain("Emissive"));
            Assert.That(
                result.MaterialSummary.SkippedSemantics.Select(item => item.Semantic),
                Does.Contain("Occlusion"));
            Assert.That(message, Does.Contain("已写入资源"));
            Assert.That(message, Does.Contain("自动人形缩放"));
            Assert.That(message, Does.Contain("最终倍率：0.5"));
            Assert.That(message, Does.Contain("蒙皮权重"));
            Assert.That(message, Does.Contain("已根据最终三角形重建"));
            Assert.That(message, Does.Contain("MaskMaterial"));
            Assert.That(message, Does.Contain("自发光（Emissive）"));
            Assert.That(message, Does.Contain("环境遮蔽（Occlusion）"));
            Assert.That(result.SourceForward!.SourceDirection,
                Is.EqualTo("+Z（标准 glTF）"));
            Assert.That(message, Does.Contain("源模型正面方向"));
            Assert.That(message, Does.Contain("+Z（标准 glTF）"));
            Assert.That(message, Does.Contain("未增加额外旋转"));
        });
    }

    [Test]
    public void Import_SourcePositiveX_RotatesEveryAssetAfterAutoScaleAndReportsDirection()
    {
        var glbPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "TestData",
            "Gltf",
            "external_full_workflow.glb");
        var positiveZDestination = new PackFileContainer("positive-z");
        var positiveXDestination = new PackFileContainer("positive-x");
        var positiveZResult = CreateImporter(positiveZDestination).Import(
            CreateFullWorkflowSettings(
                glbPath,
                positiveZDestination,
                GltfSourceForwardDirection.PositiveZ));
        var positiveXViewModel = new RmvToGltfImporterViewModel(
            CreateImporter(positiveXDestination))
        {
            SourceForwardDirection = GltfSourceForwardDirection.PositiveX,
            AnimationKeysPerSecond = 24,
        };
        positiveXViewModel.Initialize(new PackFile(
            glbPath,
            new FileSystemSource(glbPath)));
        var positiveXResult = positiveXViewModel.Execute(
            new PackFile(glbPath, new FileSystemSource(glbPath)),
            "models",
            positiveXDestination,
            GameTypeEnum.Warhammer3);

        Assert.Multiple(() =>
        {
            Assert.That(positiveZResult.Succeeded, Is.True,
                string.Join(Environment.NewLine, positiveZResult.Errors));
            Assert.That(positiveXResult.Succeeded, Is.True,
                string.Join(Environment.NewLine, positiveXResult.Errors));
            Assert.That(
                positiveXResult.HumanoidScale!.SourceHeight,
                Is.EqualTo(positiveZResult.HumanoidScale!.SourceHeight).Within(0.0001f));
            Assert.That(
                positiveXResult.HumanoidScale.ReferenceHeight,
                Is.EqualTo(positiveZResult.HumanoidScale.ReferenceHeight).Within(0.0001f));
            Assert.That(
                positiveXResult.HumanoidScale.ScaleFactor,
                Is.EqualTo(positiveZResult.HumanoidScale.ScaleFactor).Within(0.0001f));
            Assert.That(
                positiveXResult.SourceForward!.SourceDirection,
                Is.EqualTo("+X（Unreal/PSK）"));
            Assert.That(
                positiveXResult.SourceForward.Conversion,
                Does.Contain("游戏 +Z"));
        });

        const string rmvPath = @"models\external_full_workflow.rigid_model_v2";
        const string skeletonPath =
            @"animations\skeletons\externalworkflowarmature.anim";
        var positiveZRmv = ModelFactory.Create().Load(
            positiveZDestination.FileList[rmvPath].DataSource.ReadData());
        var positiveXRmv = ModelFactory.Create().Load(
            positiveXDestination.FileList[rmvPath].DataSource.ReadData());
        var positiveZVertices = positiveZRmv.ModelList[0]
            .SelectMany(model => model.Mesh.VertexList)
            .ToList();
        var positiveXVertices = positiveXRmv.ModelList[0]
            .SelectMany(model => model.Mesh.VertexList)
            .ToList();
        Assert.That(positiveXVertices, Has.Count.EqualTo(positiveZVertices.Count));
        for (var vertexIndex = 0; vertexIndex < positiveZVertices.Count; vertexIndex++)
        {
            AssertVectorRotated(
                ToNumerics(positiveZVertices[vertexIndex].Position),
                ToNumerics(positiveXVertices[vertexIndex].Position));
            AssertVectorRotated(
                ToNumerics(positiveZVertices[vertexIndex].Normal),
                ToNumerics(positiveXVertices[vertexIndex].Normal),
                0.01f);
        }

        var positiveZSkeleton = AnimationFile.Create(
            positiveZDestination.FileList[skeletonPath]);
        var positiveXSkeleton = AnimationFile.Create(
            positiveXDestination.FileList[skeletonPath]);
        AssertAnimationRotated(positiveZSkeleton, positiveXSkeleton);

        foreach (var animationPath in new[]
                 {
                     @"models\external_full_workflow_move.anim",
                     @"models\external_full_workflow_nod.anim",
                 })
        {
            var positiveZAnimation = AnimationFile.Create(
                positiveZDestination.FileList[animationPath]);
            var positiveXAnimation = AnimationFile.Create(
                positiveXDestination.FileList[animationPath]);
            AssertAnimationRotated(positiveZAnimation, positiveXAnimation);
        }

        Assert.That(
            ImportWindow.BuildResultMessage(positiveXResult),
            Does.Contain("源模型正面方向")
                .And.Contain("+X（Unreal/PSK）")
                .And.Contain("游戏 +Z"));
    }

    [Test]
    public void Import_SkinnedMeshUnderScaledAncestor_KeepsMeshSkeletonAndRootInSameSpace()
    {
        var glbPath = CreateScaledAncestorGlb();
        try
        {
            var destination = new PackFileContainer("test");
            var result = CreateImporter(destination).Import(new GltfImporterSettings(
                glbPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                ImportMeshes: true,
                ImportMaterials: false,
                ConvertMaterialFromBlenderType: false,
                ConvertNormalTextureFromBlueToOrangeType: false,
                ImportAnimations: true,
                AnimationKeysPerSecond: 1,
                MirrorMesh: true,
                AutoDetectAnimationKeysPerSecond: false,
                AutoScaleHumanoid: true));

            Assert.That(
                result.Succeeded,
                Is.True,
                string.Join(Environment.NewLine, result.Errors));

            var baseName = Path.GetFileNameWithoutExtension(glbPath).ToLowerInvariant();
            var rmv = ModelFactory.Create().Load(
                destination.FileList[$@"models\{baseName}.rigid_model_v2"]
                    .DataSource.ReadData());
            var skeleton = AnimationFile.Create(
                destination.FileList[@"animations\skeletons\scaledarmature.anim"]);
            var animation = AnimationFile.Create(
                destination.FileList[$@"models\{baseName}_root_move.anim"]);
            var vertices = rmv.ModelList[0]
                .SelectMany(model => model.Mesh.VertexList)
                .ToList();
            var modelHeight = vertices.Max(vertex => vertex.Position.Y) -
                              vertices.Min(vertex => vertex.Position.Y);
            var rootIndex = Array.FindIndex(
                skeleton.Bones,
                bone => bone.Name == "root");
            var headIndex = Array.FindIndex(
                skeleton.Bones,
                bone => bone.Name == "head");

            Assert.Multiple(() =>
            {
                Assert.That(result.HumanoidScale!.SourceHeight, Is.EqualTo(1).Within(0.0001f));
                Assert.That(result.HumanoidScale.ReferenceHeight, Is.EqualTo(2).Within(0.0001f));
                Assert.That(result.HumanoidScale.ScaleFactor, Is.EqualTo(2).Within(0.0001f));
                Assert.That(modelHeight, Is.EqualTo(2).Within(0.0001f));
                Assert.That(
                    skeleton.AnimationParts[0].DynamicFrames[0]
                        .Transforms[headIndex].Y,
                    Is.EqualTo(2).Within(0.0001f));
                Assert.That(
                    animation.AnimationParts[0].DynamicFrames[^1]
                        .Transforms[rootIndex].Y,
                    Is.EqualTo(0.6f).Within(0.0001f));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    [Test]
    public void Import_SkinnedMeshWithSingularAncestor_ReturnsChineseErrorAndLeavesPackUnchanged()
    {
        var fixtureDirectory = CreateSingularAncestorGltf();
        var gltfPath = Path.Combine(fixtureDirectory, "singular_ancestor.gltf");
        try
        {
            const string existingPath = @"models\existing.bin";
            var destination = new PackFileContainer("test");
            var existingFile = new PackFile(
                "existing.bin",
                new MemorySource([7, 8, 9]));
            destination.FileList[existingPath] = existingFile;

            var result = CreateImporter(destination).Import(new GltfImporterSettings(
                gltfPath,
                "models",
                destination,
                GameTypeEnum.Warhammer3,
                ImportMeshes: true,
                ImportMaterials: false,
                ConvertMaterialFromBlenderType: false,
                ConvertNormalTextureFromBlueToOrangeType: false,
                ImportAnimations: false,
                AnimationKeysPerSecond: 20,
                MirrorMesh: true,
                AutoScaleHumanoid: false));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Errors, Has.Some.Contains("不可逆"));
                Assert.That(destination.FileList, Has.Count.EqualTo(1));
                Assert.That(destination.FileList[existingPath], Is.SameAs(existingFile));
                Assert.That(
                    existingFile.DataSource.ReadData(),
                    Is.EqualTo(new byte[] { 7, 8, 9 }));
            });
        }
        finally
        {
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ImportWindowSelection_OfficialBlenderGlb_UsesCompleteWorkflowDefaults()
    {
        var glbPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "TestData",
            "Gltf",
            "external_full_workflow.glb");
        var destination = new PackFileContainer("test");
        var importerViewModel = new RmvToGltfImporterViewModel(
            CreateImporter(destination));
        var windowViewModel = new ImporterCoreViewModel(
            [importerViewModel],
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));

        windowViewModel.Initialize(destination, "models", glbPath);

        Assert.Multiple(() =>
        {
            Assert.That(windowViewModel.SelectedImporter, Is.SameAs(importerViewModel));
            Assert.That(windowViewModel.PossibleImporters, Has.Count.EqualTo(1));
            Assert.That(importerViewModel.ImportMeshes, Is.True);
            Assert.That(importerViewModel.ImportMaterials, Is.True);
            Assert.That(importerViewModel.ImportAnimations, Is.True);
            Assert.That(importerViewModel.AutoScaleHumanoid, Is.True);
            Assert.That(
                importerViewModel.NewSkeletonName,
                Is.EqualTo("ExternalWorkflowArmature"));
        });

        var result = await windowViewModel.ImportAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Succeeded,
                Is.True,
                string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.OutputPaths, Is.EquivalentTo(destination.FileList.Keys));
            Assert.That(result.OutputPaths, Has.Some.EndsWith(".rigid_model_v2"));
            Assert.That(result.OutputPaths.Count(path => path.EndsWith(".anim")),
                Is.EqualTo(3));
            Assert.That(result.OutputPaths, Has.Some.EndsWith(".dds"));
        });
    }

    private static GltfImporter CreateImporter(PackFileContainer destination)
    {
        var innerPackFileService = PackFileSerivceTestHelper.Create(TestData.InputPack);
        var referenceContainer = CreateReferenceContainer();
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetAllPackfileContainers())
            .Returns([referenceContainer]);
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
        return new GltfImporter(
            packFileService.Object,
            Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new RmvMaterialBuilder());
    }

    private static GltfImporterSettings CreateFullWorkflowSettings(
        string glbPath,
        PackFileContainer destination,
        GltfSourceForwardDirection sourceForwardDirection) =>
        new(
            glbPath,
            "models",
            destination,
            GameTypeEnum.Warhammer3,
            ImportMeshes: true,
            ImportMaterials: true,
            ConvertMaterialFromBlenderType: true,
            ConvertNormalTextureFromBlueToOrangeType: true,
            ImportAnimations: true,
            AnimationKeysPerSecond: 24,
            MirrorMesh: true,
            AutoDetectAnimationKeysPerSecond: true,
            AutoScaleHumanoid: true,
            SourceForwardDirection: sourceForwardDirection);

    private static void AssertAnimationRotated(
        AnimationFile positiveZ,
        AnimationFile positiveX)
    {
        Assert.Multiple(() =>
        {
            Assert.That(positiveX.Bones.Select(bone => bone.Name),
                Is.EqualTo(positiveZ.Bones.Select(bone => bone.Name)));
            Assert.That(positiveX.AnimationParts,
                Has.Count.EqualTo(positiveZ.AnimationParts.Count));
        });
        for (var partIndex = 0; partIndex < positiveZ.AnimationParts.Count; partIndex++)
        {
            var positiveZPart = positiveZ.AnimationParts[partIndex];
            var positiveXPart = positiveX.AnimationParts[partIndex];
            if (positiveZPart.StaticFrame != null)
            {
                Assert.That(positiveXPart.StaticFrame, Is.Not.Null);
                AssertFrameRotated(
                    positiveZPart.StaticFrame,
                    positiveXPart.StaticFrame!);
            }

            Assert.That(positiveXPart.DynamicFrames,
                Has.Count.EqualTo(positiveZPart.DynamicFrames.Count));
            for (var frameIndex = 0;
                 frameIndex < positiveZPart.DynamicFrames.Count;
                 frameIndex++)
            {
                AssertFrameRotated(
                    positiveZPart.DynamicFrames[frameIndex],
                    positiveXPart.DynamicFrames[frameIndex]);
            }
        }
    }

    private static void AssertFrameRotated(
        AnimationFile.Frame positiveZ,
        AnimationFile.Frame positiveX)
    {
        Assert.Multiple(() =>
        {
            Assert.That(positiveX.Transforms,
                Has.Count.EqualTo(positiveZ.Transforms.Count));
            Assert.That(positiveX.Quaternion,
                Has.Count.EqualTo(positiveZ.Quaternion.Count));
        });
        for (var transformIndex = 0;
             transformIndex < positiveZ.Transforms.Count;
             transformIndex++)
        {
            AssertVectorRotated(
                ToNumerics(positiveZ.Transforms[transformIndex]),
                ToNumerics(positiveX.Transforms[transformIndex]));
        }
        for (var rotationIndex = 0;
             rotationIndex < positiveZ.Quaternion.Count;
             rotationIndex++)
        {
            var expected = RotatePositiveX(
                ToNumerics(positiveZ.Quaternion[rotationIndex]));
            var actual = ToNumerics(positiveX.Quaternion[rotationIndex]);
            Assert.That(
                Math.Abs(Numerics.Quaternion.Dot(expected, actual)),
                Is.EqualTo(1).Within(0.0001f));
        }
    }

    private static void AssertVectorRotated(
        Numerics.Vector3 positiveZ,
        Numerics.Vector3 positiveX,
        float tolerance = 0.0001f) =>
        Assert.That(
            Numerics.Vector3.Distance(
                positiveX,
                Numerics.Vector3.Transform(
                    positiveZ,
                    Numerics.Matrix4x4.CreateRotationY(MathF.PI / 2))),
            Is.LessThanOrEqualTo(tolerance));

    private static Numerics.Quaternion RotatePositiveX(
        Numerics.Quaternion positiveZ)
    {
        var basis = Numerics.Matrix4x4.CreateRotationY(MathF.PI / 2);
        var converted = Numerics.Matrix4x4.Transpose(basis) *
                        Numerics.Matrix4x4.CreateFromQuaternion(positiveZ) *
                        basis;
        Assert.That(Numerics.Matrix4x4.Decompose(
            converted,
            out _,
            out var rotation,
            out _), Is.True);
        return Numerics.Quaternion.Normalize(rotation);
    }

    private static Numerics.Vector3 ToNumerics(
        Microsoft.Xna.Framework.Vector4 value) =>
        new(value.X, value.Y, value.Z);

    private static Numerics.Vector3 ToNumerics(
        Microsoft.Xna.Framework.Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static Numerics.Vector3 ToNumerics(RmvVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static Numerics.Quaternion ToNumerics(RmvVector4 value) =>
        new(value.X, value.Y, value.Z, value.W);


    private static PackFileContainer CreateReferenceContainer()
    {
        var container = new PackFileContainer("ca")
        {
            IsCaPackFile = true,
        };
        container.FileList[HumanoidSkeletonPath] = new PackFile(
            "humanoid01.anim",
            new MemorySource(AnimationFile.ConvertToBytes(
                CreateHumanoidSkeleton())));
        return container;
    }

    private static AnimationFile CreateHumanoidSkeleton()
    {
        var frame = new AnimationFile.Frame
        {
            Transforms =
            [
                new RmvVector3(0, 0, 0),
                new RmvVector3(-0.25f, 0, 0),
                new RmvVector3(0.25f, 0, 0),
                new RmvVector3(0, 2, 0),
            ],
            Quaternion = Enumerable.Repeat(
                new RmvVector4(0, 0, 0, 1),
                4).ToList(),
        };
        var part = new AnimationFile.AnimationPart
        {
            DynamicFrames = [frame, CloneFrame(frame)],
        };
        for (var boneIndex = 0; boneIndex < 4; boneIndex++)
        {
            part.TranslationMappings.Add(
                new AnimationFile.AnimationBoneMapping(boneIndex));
            part.RotationMappings.Add(
                new AnimationFile.AnimationBoneMapping(boneIndex));
        }

        return new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                SkeletonName = "humanoid01",
                AnimationTotalPlayTimeInSec = 0.1f,
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                    { Id = 0, ParentId = -1, Name = "root" },
                new AnimationFile.BoneInfo
                    { Id = 1, ParentId = 0, Name = "toe_left_0" },
                new AnimationFile.BoneInfo
                    { Id = 2, ParentId = 0, Name = "toe_right_0" },
                new AnimationFile.BoneInfo
                    { Id = 3, ParentId = 0, Name = "head" },
            ],
            AnimationParts = [part],
        };
    }

    private static string CreateScaledAncestorGlb()
    {
        const float ancestorScale = 0.003f;
        const float sourceHeight = 1;
        var modelRoot = ModelRoot.CreateModel();
        var scene = modelRoot.UseScene("default");
        var armature = scene.CreateNode("ScaledArmature");
        armature.LocalMatrix = Numerics.Matrix4x4.CreateScale(ancestorScale);
        var root = armature.CreateNode("root");
        var leftFoot = root.CreateNode("toe_left_0");
        leftFoot.LocalMatrix = Numerics.Matrix4x4.CreateTranslation(-25, 0, 0);
        var rightFoot = root.CreateNode("toe_right_0");
        rightFoot.LocalMatrix = Numerics.Matrix4x4.CreateTranslation(25, 0, 0);
        var head = root.CreateNode("head");
        head.LocalMatrix = Numerics.Matrix4x4.CreateTranslation(
            0,
            sourceHeight / ancestorScale,
            0);

        var geometry = new MeshBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>("scaled_mesh");
        geometry.UsePrimitive(
                new MaterialBuilder("material").WithMetallicRoughness())
            .AddTriangle(
                CreateScaledAncestorVertex(0, 0),
                CreateScaledAncestorVertex(1, 0),
                CreateScaledAncestorVertex(0, sourceHeight));
        var mesh = modelRoot.CreateMesh(geometry);
        Assert.That(Numerics.Matrix4x4.Invert(
            root.WorldMatrix,
            out var rootInverseBind), Is.True);
        Assert.That(Numerics.Matrix4x4.Invert(
            leftFoot.WorldMatrix,
            out var leftFootInverseBind), Is.True);
        Assert.That(Numerics.Matrix4x4.Invert(
            rightFoot.WorldMatrix,
            out var rightFootInverseBind), Is.True);
        Assert.That(Numerics.Matrix4x4.Invert(
            head.WorldMatrix,
            out var headInverseBind), Is.True);
        armature.CreateNode("mesh_node").WithSkinnedMesh(
            mesh,
            (root, rootInverseBind),
            (leftFoot, leftFootInverseBind),
            (rightFoot, rightFootInverseBind),
            (head, headInverseBind));
        modelRoot.LogicalSkins.Single().Name = "ScaledArmature";
        modelRoot.CreateAnimation("Root Move").CreateTranslationChannel(
            root,
            new Dictionary<float, Numerics.Vector3>
            {
                [0] = Numerics.Vector3.Zero,
                [1] = new Numerics.Vector3(0, 100, 0),
            });

        var path = Path.Combine(
            Path.GetTempPath(),
            $"scaled_ancestor_{Guid.NewGuid():N}.glb");
        modelRoot.SaveGLB(path);
        return path;
    }

    private static string CreateSingularAncestorGltf()
    {
        var modelRoot = ModelRoot.CreateModel();
        var scene = modelRoot.UseScene("default");
        var armature = scene.CreateNode("InvalidArmature");
        armature.LocalMatrix = Numerics.Matrix4x4.CreateScale(2);
        var root = armature.CreateNode("root");
        var geometry = new MeshBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>("invalid_mesh");
        geometry.UsePrimitive(
                new MaterialBuilder("material").WithMetallicRoughness())
            .AddTriangle(
                CreateScaledAncestorVertex(0, 0),
                CreateScaledAncestorVertex(1, 0),
                CreateScaledAncestorVertex(0, 1));
        var mesh = modelRoot.CreateMesh(geometry);
        Assert.That(Numerics.Matrix4x4.Invert(
            root.WorldMatrix,
            out var rootInverseBind), Is.True);
        armature.CreateNode("mesh_node").WithSkinnedMesh(
            mesh,
            (root, rootInverseBind));
        modelRoot.LogicalSkins.Single().Name = "InvalidArmature";

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"singular_ancestor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "singular_ancestor.gltf");
        modelRoot.SaveGLTF(path);

        var json = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(path))!;
        var armatureJson = json["nodes"]![armature.LogicalIndex]!.AsObject();
        armatureJson.Remove("matrix");
        armatureJson.Remove("rotation");
        armatureJson.Remove("translation");
        armatureJson["scale"] = new System.Text.Json.Nodes.JsonArray(0, 1, 1);
        File.WriteAllText(path, json.ToJsonString());
        return directory;
    }

    private static VertexBuilder<
        VertexPositionNormalTangent,
        VertexTexture1,
        VertexJoints4> CreateScaledAncestorVertex(float x, float y)
    {
        var vertex = new VertexBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>();
        vertex.Geometry.Position = new Numerics.Vector3(x, y, 0);
        vertex.Geometry.Normal = Numerics.Vector3.UnitZ;
        vertex.Geometry.Tangent = new Numerics.Vector4(1, 0, 0, 1);
        vertex.Material.TexCoord = new Numerics.Vector2(x, y);
        vertex.Skinning.SetBindings((0, 1), (0, 0), (0, 0), (0, 0));
        return vertex;
    }

    private static AnimationFile.Frame CloneFrame(AnimationFile.Frame frame) => new()
    {
        Transforms = frame.Transforms.ToList(),
        Quaternion = frame.Quaternion.ToList(),
    };
}
