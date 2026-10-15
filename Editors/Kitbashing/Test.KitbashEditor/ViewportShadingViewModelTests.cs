using Shared.Core.Settings;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using KitbasherEditor.ViewModels.MenuBarViews;
using Moq;
using Shared.Core.Events;

namespace Test.KitbashEditor;

public class ViewportShadingViewModelTests
{
    [Test]
    public void Modes_RemainExclusiveAndPreserveSharedLighting()
    {
        var applicationSettings = new ApplicationSettingsService();
        applicationSettings.CurrentSettings.ViewportLightIntensity = 1.7f;
        applicationSettings.CurrentSettings.ViewportEnvironmentLightRotationY = 42;
        var lighting = new SceneRenderParametersStore();
        var renderer = new RenderEngineComponent(null!, null!, null!, null!, applicationSettings,
            lighting, Mock.Of<IEventHub>(), new GridComponent(null!, null!, null!));
        var model = new ViewportShadingViewModel(renderer);
        var settings = renderer.ShadingSettings;
        Assert.That(model.IsMaterialPreview, Is.True);

        model.IsSolid = true;
        Assert.That(renderer.ShadingMode, Is.EqualTo(ViewportShadingMode.Solid));
        Assert.That(model.IsMaterialPreview, Is.False);
        model.IsMaterialPreview = true;
        Assert.Multiple(() =>
        {
            Assert.That(model.IsSolid, Is.False);
            Assert.That(renderer.ShadingMode, Is.EqualTo(ViewportShadingMode.MaterialPreview));
            Assert.That(renderer.ShadingSettings, Is.EqualTo(settings));
            Assert.That(lighting.LightIntensityMult, Is.EqualTo(1.7f));
            Assert.That(lighting.EnvLightRotationDegrees_Y, Is.EqualTo(42));
        });
    }
}
