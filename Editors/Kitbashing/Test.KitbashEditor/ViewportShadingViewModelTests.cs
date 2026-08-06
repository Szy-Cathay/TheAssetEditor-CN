using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.MenuBarViews;
using Moq;
using Shared.Core.Events;
using Shared.Core.Settings;

namespace Test.KitbashEditor;

public class ViewportShadingViewModelTests
{
    [Test]
    public void Constructor_SelectsMaterialPreviewForKitbash()
    {
        var renderEngine = CreateRenderEngine();
        var viewModel = new ViewportShadingViewModel(
            renderEngine);

        Assert.Multiple(() =>
        {
            Assert.That(
                renderEngine.ShadingMode,
                Is.EqualTo(ViewportShadingMode.MaterialPreview));
            Assert.That(viewModel.IsWireframe, Is.False);
            Assert.That(viewModel.IsSolid, Is.False);
            Assert.That(viewModel.IsMaterialPreview, Is.True);
        });
    }

    [Test]
    public void SelectionAndEngineChangesStaySynchronized()
    {
        var renderEngine = CreateRenderEngine();
        var viewModel = new ViewportShadingViewModel(
            renderEngine);

        viewModel.IsWireframe = true;

        Assert.Multiple(() =>
        {
            Assert.That(
                renderEngine.ShadingMode,
                Is.EqualTo(ViewportShadingMode.Wireframe));
            Assert.That(viewModel.IsWireframe, Is.True);
            Assert.That(viewModel.IsSolid, Is.False);
            Assert.That(viewModel.IsMaterialPreview, Is.False);
        });

        renderEngine.ShadingMode =
            ViewportShadingMode.MaterialPreview;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsWireframe, Is.False);
            Assert.That(viewModel.IsSolid, Is.False);
            Assert.That(viewModel.IsMaterialPreview, Is.True);
        });
    }

    private static RenderEngineComponent CreateRenderEngine()
    {
        return new RenderEngineComponent(
            null!,
            null!,
            null!,
            null!,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            Mock.Of<IEventHub>(),
            new GridComponent(null!, null!, null!));
    }
}
