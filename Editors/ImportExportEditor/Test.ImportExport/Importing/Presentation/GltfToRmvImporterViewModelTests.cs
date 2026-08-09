using Editors.ImportImport.Importing.Presentation.RmvToGltf;

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
}
