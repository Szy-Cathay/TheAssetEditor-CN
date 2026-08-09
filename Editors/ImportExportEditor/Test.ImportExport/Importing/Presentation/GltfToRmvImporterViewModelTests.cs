using Editors.ImportExport.Importing;
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
    [Test]
    public void AnimationSampling_DefaultsToAutoAndManualFieldTracksAvailability()
    {
        var viewModel = new RmvToGltfImporterViewModel(null!);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AutoDetectAnimationKeysPerSecond, Is.True);
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
            []));
        var failureMessage = ImportWindow.BuildResultMessage(
            ImportResult.Failure([
                "目标 Pack 已存在资源：models\\test.rigid_model_v2",
                "目标 Pack 已存在资源：models\\test.anim",
            ]));

        Assert.Multiple(() =>
        {
            Assert.That(successMessage, Does.Contain(@"models\test.rigid_model_v2"));
            Assert.That(successMessage, Does.Contain("测试警告"));
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
