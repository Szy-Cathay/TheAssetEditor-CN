using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameWorld.Core.Services;
using GameWorld.Core.WpfWindow.FactionColourSettings;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class FactionColourSettingsWindowTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void Window_RendersSharedFactionSettingsControls()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var settings = new ApplicationSettingsService();
                var service = new FactionColourSettingsService(
                    settings,
                    new TestEventHub());
                var window = new FactionColourSettingsWindow(
                    new FactionColourSettingsViewModel(service));

                try
                {
                    window.Show();
                    window.UpdateLayout();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(window.Title,
                            Is.EqualTo("阵营着色"));
                        NUnitAssert.That(
                            FindVisualChildren<ColourPickerButtonView>(
                                window).Count(),
                            Is.EqualTo(3));
                        NUnitAssert.That(
                            FindVisualChildren<CheckBox>(window)
                                .Any(item => Equals(
                                    item.Content,
                                    "启用阵营着色")),
                            Is.True);
                        NUnitAssert.That(
                            FindVisualChildren<Button>(window)
                                .Any(item => Equals(
                                    item.Content,
                                    "保存")),
                            Is.True);
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
