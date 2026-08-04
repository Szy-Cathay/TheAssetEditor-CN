using System.Windows;
using AssetEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditorTests;

[NUnit.Framework.NonParallelizable]
public class MainWindowStartupTests
{
    [NUnit.Framework.SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [NUnit.Framework.Test]
    public void DefaultWindow_IsCenteredAtBalancedDesktopSize()
    {
        var settings = new ApplicationSettingsService();

        RunWithMainWindow(settings, window =>
        {
            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(window.Width,
                    NUnit.Framework.Is.EqualTo(1440));
                NUnit.Framework.Assert.That(window.Height,
                    NUnit.Framework.Is.EqualTo(900));
                NUnit.Framework.Assert.That(
                    window.WindowStartupLocation,
                    NUnit.Framework.Is.EqualTo(
                        WindowStartupLocation.CenterScreen));
                NUnit.Framework.Assert.That(
                    window.WindowState,
                    NUnit.Framework.Is.EqualTo(WindowState.Normal));
            });
        });
    }

    [NUnit.Framework.Test]
    public void StartMaximisedSetting_IsAppliedBeforeFirstShow()
    {
        var settings = new ApplicationSettingsService();
        settings.CurrentSettings.StartMaximised = true;

        RunWithMainWindow(settings, window =>
            NUnit.Framework.Assert.That(
                window.WindowState,
                NUnit.Framework.Is.EqualTo(WindowState.Maximized)));
    }

    private static void RunWithMainWindow(
        ApplicationSettingsService settings,
        Action<MainWindow> assertion)
    {
        var autoSaveService = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .AddSingleton(settings)
            .AddSingleton(autoSaveService)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var window = new MainWindow(services);
            try
            {
                assertion(window);
            }
            finally
            {
                autoSaveService.Stop();
                window.Close();
            }
        });
    }
}
