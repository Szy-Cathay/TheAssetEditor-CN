using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Xml.Linq;

using AssetEditor.Views.Startup;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class StartupPackLoadingWindowTests
{
    [Test]
    public void Run_DisablesMainWindowUntilCaPacksAreRegistered()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var services = new ServiceCollection()
            .AddSingleton(localization)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var owner = new Window
            {
                Width = 640,
                Height = 480,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            var registered = false;
            var ownerWasEnabledDuringLoad = true;
            var loadingFeedbackWasVisible = true;
            var renderedProgressWasExpandable = false;
            StartupPackLoadingWindow? loadingWindow = null;
            try
            {
                owner.Show();
                Application.Current.MainWindow = owner;
                loadingWindow = new StartupPackLoadingWindow(
                    reportProgress =>
                    {
                        reportProgress(new CaPackLoadProgress(
                            CaPackLoadProgressStage.ReadingPacks,
                            "data.pack",
                            1,
                            1));
                        var window = loadingWindow!;
                        window.Dispatcher.Invoke(() =>
                        {
                            ownerWasEnabledDuringLoad = IsWindowEnabled(
                                new WindowInteropHelper(owner).Handle);
                            loadingFeedbackWasVisible = window.Opacity > 0;
                            window.StartupOperationProgress
                                .IsDetailsExpanded = true;
                            window.UpdateLayout();
                            renderedProgressWasExpandable =
                                window.StartupOperationProgress
                                    .DetailsHeaderText == "收起详情" &&
                                window.StartupOperationProgress
                                    .DetailHistory.Contains("data.pack") &&
                                window.StartupOperationProgress
                                    .ProgressValue == 1 &&
                                window.StartupOperationProgress
                                    .ProgressMaximum == 1;
                        });
                        return new PackFileContainer("All Game Packs")
                        {
                            IsCaPackFile = true,
                        };
                    },
                    _ => registered = true,
                    () => { });

                var succeeded = loadingWindow.Run();

                Assert.Multiple(() =>
                {
                    Assert.That(succeeded, Is.True);
                    Assert.That(
                        ownerWasEnabledDuringLoad,
                        Is.False,
                        "The main window remained interactive while CA packs loaded.");
                    Assert.That(registered, Is.True);
                    Assert.That(
                        loadingFeedbackWasVisible,
                        Is.False,
                        "A short startup operation displayed loading feedback.");
                    Assert.That(
                        renderedProgressWasExpandable,
                        Is.True,
                        "The rendered startup dialog did not expose real progress details.");
                    Assert.That(owner.IsEnabled, Is.True);
                });
            }
            finally
            {
                if (loadingWindow?.IsVisible == true)
                {
                    loadingWindow.ExitButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                }
                owner.Close();
            }
        });
    }

    [Test]
    public void Run_WhenLoadFails_AllowsRetryWithoutUnlockingMainWindow()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var services = new ServiceCollection()
            .AddSingleton(localization)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var owner = new Window
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            var attempts = 0;
            var ownerWasEnabledAfterFailure = true;
            StartupPackLoadingWindow? loadingWindow = null;
            try
            {
                owner.Show();
                Application.Current.MainWindow = owner;
                loadingWindow = new StartupPackLoadingWindow(
                    _ =>
                    {
                        attempts++;
                        if (attempts == 1)
                        {
                            var window = loadingWindow!;
                            window.Dispatcher.BeginInvoke(
                                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                                () =>
                                {
                                    ownerWasEnabledAfterFailure =
                                        IsWindowEnabled(
                                            new WindowInteropHelper(owner)
                                                .Handle);
                                    window.RetryButton.RaiseEvent(
                                        new RoutedEventArgs(
                                            Button.ClickEvent));
                                });
                            return null;
                        }

                        return new PackFileContainer("All Game Packs")
                        {
                            IsCaPackFile = true,
                        };
                    },
                    _ => { },
                    () => { });

                var succeeded = loadingWindow.Run();

                Assert.Multiple(() =>
                {
                    Assert.That(succeeded, Is.True);
                    Assert.That(attempts, Is.EqualTo(2));
                    Assert.That(ownerWasEnabledAfterFailure, Is.False);
                });
            }
            finally
            {
                if (loadingWindow?.IsVisible == true)
                {
                    loadingWindow.ExitButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                }
                owner.Close();
            }
        });
    }

    [Test]
    public void Run_WhenLoadFails_AllowsPathCheckAndExplicitExit()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var services = new ServiceCollection()
            .AddSingleton(localization)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var owner = new Window
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            var settingsOpened = false;
            StartupPackLoadingWindow? loadingWindow = null;
            try
            {
                owner.Show();
                Application.Current.MainWindow = owner;
                loadingWindow = new StartupPackLoadingWindow(
                    _ =>
                    {
                        var window = loadingWindow!;
                        window.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority
                                .ApplicationIdle,
                            () =>
                            {
                                window.CheckGamePathButton.RaiseEvent(
                                    new RoutedEventArgs(Button.ClickEvent));
                                window.ExitButton.RaiseEvent(
                                    new RoutedEventArgs(Button.ClickEvent));
                            });
                        return null;
                    },
                    _ => { },
                    () => settingsOpened = true);

                var succeeded = loadingWindow.Run();

                Assert.Multiple(() =>
                {
                    Assert.That(succeeded, Is.False);
                    Assert.That(settingsOpened, Is.True);
                    Assert.That(owner.IsEnabled, Is.True);
                });
            }
            finally
            {
                if (loadingWindow?.IsVisible == true)
                {
                    loadingWindow.ExitButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                }
                owner.Close();
            }
        });
    }

    [Test]
    public void StartupPolicy_DoesNotExposeOrHonorLegacyOptOut()
    {
        var solutionRoot = FindSolutionRoot();
        var settingsView = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Views",
            "Settings",
            "SettingsView.xaml"));
        var appSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "App.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(
                settingsView.ToString(),
                Does.Not.Contain("LoadCaPacksByDefault"));
            Assert.That(
                appSource,
                Does.Not.Contain(
                    "CurrentSettings.LoadCaPacksByDefault"));
        });
    }

    [Test]
    public void DevelopmentProfile_SkipsCaPacksAndKeepsItsExistingTestPackPath()
    {
        var solutionRoot = FindSolutionRoot();
        var appSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "App.xaml.cs"));
        var managerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Shared",
            "SharedCore",
            "DevConfig",
            "DevelopmentConfigurationManager.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(
                managerSource,
                Does.Contain("HasActiveConfiguration"));
            Assert.That(
                appSource,
                Does.Contain(
                    "!devConfigManager.HasActiveConfiguration"));
            Assert.That(
                appSource,
                Does.Contain("FinishStartup(devConfigManager)"));
        });
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr windowHandle);
}
