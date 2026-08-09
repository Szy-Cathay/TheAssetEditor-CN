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
using SharpGLTF.Schema2;

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
                Is.EqualTo(0.125f).Within(0.001f));
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
        });
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

    private static AnimationFile.Frame CloneFrame(AnimationFile.Frame frame) => new()
    {
        Transforms = frame.Transforms.ToList(),
        Quaternion = frame.Quaternion.ToList(),
    };
}
