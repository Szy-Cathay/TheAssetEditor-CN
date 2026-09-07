using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using KitbasherEditor.ViewModels.MenuBarViews;
using KitbasherEditor.Views;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.ValueConverters;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class ViewportShadingPopupTests
{
    [TestCase(ThemeType.DarkTheme, 1.0)]
    [TestCase(ThemeType.LightTheme, 1.25)]
    [TestCase(ThemeType.HighContrastDark, 1.5)]
    [TestCase(ThemeType.HighContrastLight, 1.0)]
    public void ShadingPopup_RendersModesAndKeepsControlsWithinBounds(ThemeType theme, double scale)
    {
        new LocalizationManager().LoadLanguage();
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            ThemesController.SetTheme(theme);
            Application.Current.Resources["BoolToCollapsedConverter"] = new BoolToVisibilityConverter
                { TrueValue = Visibility.Visible, FalseValue = Visibility.Collapsed };
            var renderer = new RenderEngineComponent(null!, null!, null!, null!, new ApplicationSettingsService(),
                new SceneRenderParametersStore(), Mock.Of<IEventHub>(), new GridComponent(null!, null!, null!));
            var model = new ViewportShadingViewModel(renderer);
            var menu = new MenuBarView
            {
                DataContext = new
                {
                    ViewportShading = model,
                    CanUseMeshSelectionTools = new { Value = true },
                    SelectionSettings = new KitbashSelectionSettings { IsXRay = true },
                    MenuItems = Array.Empty<object>(),
                    CustomButtons = Array.Empty<object>()
                }
            };
            var window = new Window { Content = menu, Width = 800, Height = 160, ShowActivated = false, ShowInTaskbar = false };
            var popup = (Popup)menu.FindName("ShadingPopup");
            try
            {
                window.Show();
                window.UpdateLayout();
                popup.IsOpen = true;
                var panel = (FrameworkElement)popup.Child;
                foreach (var mode in Enum.GetValues<ViewportShadingMode>())
                {
                    renderer.ShadingMode = mode;
                    panel.UpdateLayout();
                    var sliders = Descendants<Slider>(panel).Where(item => item.IsVisible).ToArray();
                    NUnitAssert.That(sliders.Length, Is.EqualTo(mode == ViewportShadingMode.Wireframe ? 1 : 3));
                    foreach (var control in Descendants<Control>(panel).Where(item => item.IsVisible && item is Slider or ComboBox or Button))
                    {
                        var bounds = control.TransformToAncestor(panel).TransformBounds(new Rect(control.RenderSize));
                        NUnitAssert.That(bounds.Right, Is.LessThanOrEqualTo(panel.ActualWidth + 1));
                        NUnitAssert.That(bounds.Bottom, Is.LessThanOrEqualTo(panel.ActualHeight + 1));
                        NUnitAssert.That(control.ActualHeight, Is.GreaterThan(0));
                    }
                    var image = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth * scale),
                        (int)Math.Ceiling(panel.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    image.Render(panel);
                    var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
                    image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                    NUnitAssert.That(pixels.Any(value => value > 0), Is.True);
                    var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        using var stream = File.Create(Path.Combine(output, $"shading-{theme}-{mode}-{scale:0.00}.png"));
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(stream);
                    }
                }
            }
            finally
            {
                popup.IsOpen = false;
                window.Close();
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
