using System.Collections.ObjectModel;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.TestUtility;
using Test.TestingUtility.TestUtility;

namespace Test.ImportExport.Exporting.Presentation;

public class RmvToGltfExporterViewModelTests
{
    [Test]
    public void CanExportFile_InvalidRmv_DoesNotBreakSupportDetection()
    {
        var exporter = new RmvToGltfExporter(null!, null!, null!, null!, null!, null!);
        var viewModel = new RmvToGltfExporterViewModel(
            exporter,
            Mock.Of<IPackFileService>(),
            Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));
        var invalidRmv = PackFile.CreateFromBytes("invalid.rigid_model_v2", []);

        var support = viewModel.CanExportFile(invalidRmv);

        Assert.Multiple(() =>
        {
            Assert.That(support, Is.EqualTo(ExportSupportEnum.HighPriority));
            Assert.That(viewModel.HasSkeleton, Is.False);
            Assert.That(viewModel.AvailableAnimations, Is.Empty);
        });
    }

    [Test]
    public void CanExportFile_OffersOnlyActionAnimationsForTheModelSkeleton()
    {
        const string actionPath = @"animations\battle\humanoid01\stand_idle.anim";
        const string skeletonPath = @"animations\skeletons\humanoid01.anim";
        var packFileService = PackFileSerivceTestHelper.Create(
            PathHelper.GetDataFolder("Data\\Karl_and_celestialgeneral_Pack"));
        var model = packFileService.FindFile(
            @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2")!;
        var rmvFile = new ModelFactory().Load(model.DataSource.ReadData());
        var container = new PackFileContainer("game");
        var action = PackFile.CreateFromBytes("stand_idle.anim", []);
        var skeleton = PackFile.CreateFromBytes("humanoid01.anim", []);
        var actionReference = new AnimationReference(actionPath, container);
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup
            .Setup(service => service.GetAnimationsForSkeleton(rmvFile.Header.SkeletonName))
            .Returns(new ObservableCollection<AnimationReference>
            {
                actionReference,
                new(skeletonPath, container, isSkeletonFile: true),
            });
        lookup
            .Setup(service => service.FindAnimationRefFromPackFile(action))
            .Returns(actionReference);
        var packFiles = new Mock<IPackFileService>();
        packFiles.Setup(service => service.FindFile(actionPath, null)).Returns(action);
        packFiles.Setup(service => service.FindFile(skeletonPath, null)).Returns(skeleton);
        var exporter = new RmvToGltfExporter(null!, null!, null!, null!, null!, null!);
        var viewModel = new RmvToGltfExporterViewModel(
            exporter,
            packFiles.Object,
            lookup.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3));

        viewModel.CanExportFile(model);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasSkeleton, Is.True);
            Assert.That(viewModel.AvailableAnimations, Has.Count.EqualTo(1));
            Assert.That(viewModel.AvailableAnimations.Single().PackPath, Is.EqualTo(actionPath));
        });
        packFiles.Verify(service => service.FindFile(skeletonPath, null), Times.Never);
    }
}
