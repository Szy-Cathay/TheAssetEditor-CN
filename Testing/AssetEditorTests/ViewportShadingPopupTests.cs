using System.Windows.Controls;
using System.Windows.Data;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using KitbasherEditor.ViewModels.MenuBarViews;
using KitbasherEditor.Views;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Settings;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class ViewportShadingPopupTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void ShadingMenu_KeepsSurfaceModesAndRemovesOnlyWireframeAndSettings(ThemeType theme)
    {
        PlaybackAndViewportRefinementTests.WithTheme(theme, () =>
        {
            var renderer = new RenderEngineComponent(null!, null!, null!, null!, new ApplicationSettingsService(),
                new SceneRenderParametersStore(), Mock.Of<IEventHub>(), new GridComponent(null!, null!, null!));
            var model = new ViewportShadingViewModel(renderer);
            var menu = new MenuBarView
            {
                DataContext = new { ViewportShading = model, MenuItems = Array.Empty<object>(), CustomButtons = Array.Empty<object>() }
            };
            PlaybackAndViewportRefinementTests.Layout(menu, 800);
            var modes = PlaybackAndViewportRefinementTests.Descendants<RadioButton>(menu)
                .Where(button => button.GroupName == "ViewportShading").ToArray();
            Assert.That(modes.Length, Is.EqualTo(2));
            Assert.That(modes.Select(button => BindingOperations.GetBinding(button, RadioButton.IsCheckedProperty)!.Path.Path),
                Is.EquivalentTo(new[] { "ViewportShading.IsSolid", "ViewportShading.IsMaterialPreview" }));
            Assert.That(menu.FindName("ShadingArrowBtn"), Is.Null);
            Assert.That(menu.FindName("ShadingPopup"), Is.Null);
            model.IsSolid = true;
            Assert.That(renderer.ShadingMode, Is.EqualTo(ViewportShadingMode.Solid));
            model.IsMaterialPreview = true;
            Assert.That(renderer.ShadingMode, Is.EqualTo(ViewportShadingMode.MaterialPreview));
        });
    }
}
