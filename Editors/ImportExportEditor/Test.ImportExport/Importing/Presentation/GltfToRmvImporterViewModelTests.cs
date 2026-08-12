using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Presentation;
using Editors.ImportExport.Misc;
using Editors.ImportImport.Importing.Presentation.RmvToGltf;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using SharpGLTF.Schema2;

namespace Test.ImportExport.Importing.Presentation;

public class GltfToRmvImporterViewModelTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void AnimationSampling_DefaultsToAutoAndManualFieldTracksAvailability()
    {
        var viewModel = new RmvToGltfImporterViewModel(null!);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AutoDetectAnimationKeysPerSecond, Is.True);
            Assert.That(viewModel.AutoScaleHumanoid, Is.True);
            Assert.That(
                viewModel.SourceForwardDirection,
                Is.EqualTo(GltfSourceForwardDirection.PositiveZ));
            Assert.That(
                viewModel.SourceForwardDirections.Select(option => option.Value),
                Is.EqualTo(new[]
                {
                    GltfSourceForwardDirection.PositiveZ,
                    GltfSourceForwardDirection.PositiveX,
                    GltfSourceForwardDirection.NegativeX,
                    GltfSourceForwardDirection.NegativeZ,
                }));
            Assert.That(
                viewModel.SourceForwardDirections.Select(option => option.Key),
                Is.EqualTo(new[]
                {
                    "+Z（标准 glTF）",
                    "+X（Unreal/PSK）",
                    "-X",
                    "-Z",
                }));
            Assert.That(viewModel.CanEditAnimationKeysPerSecond, Is.False);
        });

        viewModel.AutoDetectAnimationKeysPerSecond = false;
        Assert.That(viewModel.CanEditAnimationKeysPerSecond, Is.True);

        viewModel.ImportAnimations = false;
        Assert.That(viewModel.CanEditAnimationKeysPerSecond, Is.False);
    }

    [Test]
    public void Initialize_StandardGlb_UsesSkinNameAsEditableSkeletonDefault()
    {
        var modelRoot = ModelRoot.CreateModel();
        var scene = modelRoot.UseScene("default");
        var root = scene.CreateNode("root");
        modelRoot.CreateSkin("ExternalArmature").BindJoints(
            System.Numerics.Matrix4x4.Identity,
            root);
        var inputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(inputPath);
            var viewModel = new RmvToGltfImporterViewModel(null!);

            viewModel.Initialize(new PackFile(
                inputPath,
                new FileSystemSource(inputPath)));

            Assert.That(viewModel.NewSkeletonName, Is.EqualTo("ExternalArmature"));
            viewModel.NewSkeletonName = "CustomSkeleton";
            Assert.That(viewModel.NewSkeletonName, Is.EqualTo("CustomSkeleton"));
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Test]
    public void Initialize_StandardGlbWithoutSkinSkeletonProperty_UsesArmatureName()
    {
        var modelRoot = ModelRoot.CreateModel();
        var scene = modelRoot.UseScene("default");
        var armature = scene.CreateNode("ExternalArmature");
        var root = armature.CreateNode("root");
        var skin = modelRoot.CreateSkin();
        skin.BindJoints(System.Numerics.Matrix4x4.Identity, root);
        skin.Skeleton = null;
        var inputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(inputPath);
            var viewModel = new RmvToGltfImporterViewModel(null!);

            viewModel.Initialize(new PackFile(
                inputPath,
                new FileSystemSource(inputPath)));

            Assert.That(viewModel.NewSkeletonName, Is.EqualTo("ExternalArmature"));
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Test]
    public void Initialize_GltfWithCyclicNonJointAncestors_DoesNotHang()
    {
        var inputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gltf");
        try
        {
            File.WriteAllText(inputPath, """
                {
                  "asset": { "version": "2.0" },
                  "nodes": [
                    { "children": [1] },
                    { "children": [0, 2] },
                    { "name": "root" }
                  ],
                  "skins": [{ "joints": [2] }]
                }
                """);
            var viewModel = new RmvToGltfImporterViewModel(null!);

            var initializeTask = Task.Run(() => viewModel.Initialize(new PackFile(
                inputPath,
                new FileSystemSource(inputPath))));

            Assert.That(initializeTask.Wait(TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(viewModel.NewSkeletonName, Is.EqualTo(Path.GetFileNameWithoutExtension(inputPath)));
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Test]
    public async Task ImportAsync_ReturnsStructuredImporterResult()
    {
        var expected = ImportResult.Failure(["目标 Pack 已存在资源：models\\test.rigid_model_v2"]);
        var importer = new TestImporter(expected);
        var viewModel = new ImporterCoreViewModel(
            [importer],
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));
        var inputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.glb");
        try
        {
            File.WriteAllBytes(inputPath, []);
            viewModel.Initialize(
                new PackFileContainer("test"),
                "models",
                inputPath);

            var actual = await viewModel.ImportAsync();

            Assert.That(actual, Is.SameAs(expected));
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    [Test]
    public void BuildResultMessage_ListsStructuredOutputsWarningsAndErrors()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var successMessage = ImportWindow.BuildResultMessage(new ImportResult(
            true,
            [@"models\test.rigid_model_v2"],
            ["测试警告"],
            [],
            HumanoidScale: new HumanoidScaleImportSummary(
                true,
                4,
                2,
                0.5f,
                "已按原版 humanoid01 绑定姿势应用统一倍率。"),
            MaterialSummary: new MaterialImportSummary(
                [new MaskedMaterialImportSummary("hair_mask", 0.7f)],
                [
                    new SkippedMaterialSemantic("decorated", "Emissive"),
                    new SkippedMaterialSemantic("decorated", "Occlusion"),
                ]),
            SourceForward: new SourceForwardImportSummary(
                "+X（Unreal/PSK）",
                "已将源 +X 确定性旋转到游戏 +Z 正面。")));
        var failureMessage = ImportWindow.BuildResultMessage(
            ImportResult.Failure([
                "目标 Pack 已存在资源：models\\test.rigid_model_v2",
                "目标 Pack 已存在资源：models\\test.anim",
            ]));
        var disabledMessage = ImportWindow.BuildResultMessage(new ImportResult(
            true,
            [],
            [],
            [],
            HumanoidScale: new HumanoidScaleImportSummary(
                false,
                null,
                null,
                1,
                "未应用：用户已关闭自动人形缩放。")));

        Assert.Multiple(() =>
        {
            Assert.That(successMessage, Does.Contain(@"models\test.rigid_model_v2"));
            Assert.That(successMessage, Does.Contain("测试警告"));
            Assert.That(successMessage, Does.Contain("源人物高度：4"));
            Assert.That(successMessage, Does.Contain("humanoid01 参考高度：2"));
            Assert.That(successMessage, Does.Contain("最终倍率：0.5"));
            Assert.That(successMessage, Does.Contain("hair_mask"));
            Assert.That(successMessage, Does.Contain("裁切阈值：0.7"));
            Assert.That(successMessage, Does.Contain("自发光（Emissive）"));
            Assert.That(successMessage, Does.Contain("环境遮蔽（Occlusion）"));
            Assert.That(successMessage, Does.Contain("不会根据文件名或 Blender 节点猜测"));
            Assert.That(successMessage, Does.Contain("源模型正面方向"));
            Assert.That(successMessage, Does.Contain("+X（Unreal/PSK）"));
            Assert.That(successMessage, Does.Contain("游戏 +Z"));
            Assert.That(disabledMessage, Does.Contain("源人物高度：未测量"));
            Assert.That(disabledMessage, Does.Contain("humanoid01 参考高度：未测量"));
            Assert.That(disabledMessage, Does.Contain("最终倍率：1"));
            Assert.That(disabledMessage, Does.Contain("用户已关闭"));
            Assert.That(failureMessage, Does.Contain(@"models\test.rigid_model_v2"));
            Assert.That(failureMessage, Does.Contain(@"models\test.anim"));
        });
    }

    private sealed class TestImporter(ImportResult result) : IImporterViewModel
    {
        public string DisplayName => "test";
        public string OutputExtension => ".rigid_model_v2";
        public string[] InputExtensions => [".glb"];

        public ImportResult Execute(
            PackFile exportSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType) => result;

        public ImportSupportEnum CanImportFile(PackFile file) =>
            ImportSupportEnum.HighPriority;
    }
}
