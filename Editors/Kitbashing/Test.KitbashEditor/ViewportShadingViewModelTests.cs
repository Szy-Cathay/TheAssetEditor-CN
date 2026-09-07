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

    [Test]
    public void LocalLighting_SeedsFromSceneAndResetDoesNotChangeSceneOrSolidSettings()
    {
        var renderer = CreateRenderEngine();
        var scene = new SceneRenderParametersStore { LightIntensityMult = 1.4f, EnvLightRotationDegrees_Y = 75 };
        var model = new ViewportShadingViewModel(renderer, scene) { CavityStrength = 0.8 };
        model.Environment = (int)ViewportEnvironment.Sunset;
        Assert.That(model.LightIntensity, Is.EqualTo(1.4f));
        Assert.That(model.EnvironmentRotation, Is.EqualTo(75));
        model.LightIntensity = 2;
        model.EnvironmentRotation = 190;
        model.IsSolid = true;
        model.IsMaterialPreview = true;
        Assert.That(model.LightIntensity, Is.EqualTo(2));
        model.ResetCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(renderer.ShadingSettings.UseLocalLighting, Is.False);
            Assert.That(model.Environment, Is.EqualTo((int)ViewportEnvironment.Game));
            Assert.That(model.LightIntensity, Is.EqualTo(1.4f));
            Assert.That(model.CavityStrength, Is.EqualTo(0.8f));
            Assert.That(scene.LightIntensityMult, Is.EqualTo(1.4f));
            Assert.That(scene.EnvLightRotationDegrees_Y, Is.EqualTo(75));
        });
    }

    [Test]
    public void Settings_RejectNonFiniteValuesAndClampSliderBoundaries()
    {
        var renderer = CreateRenderEngine();
        var model = new ViewportShadingViewModel(renderer);
        var initial = renderer.ShadingSettings;
        model.CavityStrength = double.NaN;
        model.ShadowStrength = double.PositiveInfinity;
        model.LightIntensity = double.NaN;
        model.EnvironmentRotation = double.NegativeInfinity;
        model.SolidLighting = -1;
        model.Environment = 999;
        Assert.That(renderer.ShadingSettings, Is.EqualTo(initial));
        model.XRayOpacity = -1;
        model.WireframeOpacity = 100;
        model.CavityStrength = 2;
        model.LightIntensity = 100;
        Assert.Multiple(() =>
        {
            Assert.That(model.XRayOpacity, Is.EqualTo(0.05f));
            Assert.That(model.WireframeOpacity, Is.EqualTo(1));
            Assert.That(model.CavityStrength, Is.EqualTo(1));
            Assert.That(model.LightIntensity, Is.EqualTo(3));
        });
    }

    [Test]
    public void SolidReset_PreservesMaterialLightingAndSelectionMode()
    {
        var renderer = CreateRenderEngine();
        var model = new ViewportShadingViewModel(renderer)
        {
            Environment = (int)ViewportEnvironment.Studio,
            SolidLighting = (int)ViewportSolidLighting.Metal,
            CavityStrength = 0,
            IsSolid = true
        };
        renderer.ShadingSettings = renderer.ShadingSettings with { XRay = true };
        model.ResetCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(model.SolidLighting, Is.EqualTo((int)ViewportSolidLighting.Studio));
            Assert.That(model.CavityStrength, Is.EqualTo(0.5));
            Assert.That(model.Environment, Is.EqualTo((int)ViewportEnvironment.Studio));
            Assert.That(renderer.ShadingSettings.XRay, Is.True);
            Assert.That(model.HasSurface, Is.True);
        });
        model.IsWireframe = true;
        Assert.That(model.HasSurface, Is.False);
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
