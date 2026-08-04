using AssetEditor.Services.Settings;
using AssetEditor.ViewModels;
using AssetEditor.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using System.Windows;
using System.Windows.Media;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class SettingsViewModelTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void ViewportPreview_DoesNotPersistAndCancelRestoresOriginal()
    {
        var settings = new ApplicationSettingsService();
        settings.CurrentSettings.ViewportGridColour = "10,20,30";
        var eventHub = new TestEventHub();
        var previews = new List<ViewportRenderSettings>();
        eventHub.Register<ViewportRenderSettingsChangedEvent>(
            this,
            value => previews.Add(value.Settings));
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        var applier = new ApplicationSettingsApplier(
            settings,
            autoSave,
            eventHub);

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider: Mock.Of<IServiceProvider>(),
            () =>
            {
                var viewModel = new SettingsViewModel(
                    settings,
                    applier,
                    Mock.Of<IStandardDialogs>());

                viewModel.ViewportGridColourR = "90";

                NUnitAssert.That(
                    settings.CurrentSettings.ViewportGridColour,
                    Is.EqualTo("10,20,30"));
                NUnitAssert.That(
                    previews.Last().GridColour,
                    Is.EqualTo("90,20,30"));

                viewModel.Cancel();

                NUnitAssert.That(
                    previews.Last().GridColour,
                    Is.EqualTo("10,20,30"));
            });
        autoSave.Stop();
    }

    [Test]
    public void Save_PersistsViewportAndOnlyPromptsForApplicationRestart()
    {
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        var eventHub = new TestEventHub();
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        var dialogs = new Mock<IStandardDialogs>();

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider: Mock.Of<IServiceProvider>(),
            () =>
            {
                var viewModel = new SettingsViewModel(
                    settings,
                    new ApplicationSettingsApplier(
                        settings,
                        autoSave,
                        eventHub),
                    dialogs.Object);
                viewModel.SimulateGameBackfaces = true;
                viewModel.SaveCommand.Execute(null);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(viewModel.IsSaved, Is.True);
                    NUnitAssert.That(
                        settings.CurrentSettings.SimulateGameBackfaces,
                        Is.True);
                });
                dialogs.Verify(
                    value => value.ShowDialogBox(
                        It.IsAny<string>(),
                        It.IsAny<string>()),
                    Times.Never);
            });

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider: Mock.Of<IServiceProvider>(),
            () =>
            {
                var viewModel = new SettingsViewModel(
                    settings,
                    new ApplicationSettingsApplier(
                        settings,
                        autoSave,
                        eventHub),
                    dialogs.Object);
                viewModel.CurrentGame = GameTypeEnum.Rome2;
                viewModel.SaveCommand.Execute(null);

                dialogs.Verify(
                    value => value.ShowDialogBox(
                        It.Is<string>(message =>
                            message.Contains("重新启动")),
                        It.IsAny<string>()),
                    Times.Once);
            });
        autoSave.Stop();
    }

    [Test]
    public void SettingsWindow_CloseWithoutSave_RestoresViewportPreview()
    {
        var settings = new ApplicationSettingsService();
        settings.CurrentSettings.ViewportGridColour = "1,2,3";
        var eventHub = new TestEventHub();
        var previews = new List<ViewportRenderSettings>();
        eventHub.Register<ViewportRenderSettingsChangedEvent>(
            this,
            value => previews.Add(value.Settings));
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var viewModel = new SettingsViewModel(
                    settings,
                    new ApplicationSettingsApplier(
                        settings,
                        autoSave,
                        eventHub),
                    Mock.Of<IStandardDialogs>());
                var window = new SettingsWindow
                {
                    DataContext = viewModel,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10000,
                    Top = -10000
                };
                window.Show();
                viewModel.ViewportGridColourR = "9";

                window.Close();

                NUnitAssert.That(
                    previews.Last().GridColour,
                    Is.EqualTo("1,2,3"));
            });
        autoSave.Stop();
    }

    [Test]
    public void FontPreview_CancelRestoresDefaultFontResource()
    {
        var settings = new ApplicationSettingsService();
        settings.CurrentSettings.AppFont = AppFontFamily.Default;
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider: Mock.Of<IServiceProvider>(),
            () =>
            {
                var originalFont = (FontFamily)Application.Current
                    .FindResource("AppFontFamily");
                var viewModel = new SettingsViewModel(
                    settings,
                    new ApplicationSettingsApplier(
                        settings,
                        autoSave,
                        new TestEventHub()),
                    Mock.Of<IStandardDialogs>());

                viewModel.SelectedFont = AppFontFamily.HarmonyOS;
                var previewFont = (FontFamily)Application.Current
                    .FindResource("AppFontFamily");
                NUnitAssert.That(
                    previewFont.Source,
                    Is.Not.EqualTo(originalFont.Source));

                viewModel.Cancel();

                var restoredFont = (FontFamily)Application.Current
                    .FindResource("AppFontFamily");
                NUnitAssert.That(
                    restoredFont.Source,
                    Is.EqualTo(originalFont.Source));
            });
        autoSave.Stop();
    }

    [Test]
    public void InvalidLightingText_DoesNotSaveOrCloseSettings()
    {
        var settings = new ApplicationSettingsService();
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        var dialogs = new Mock<IStandardDialogs>();

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider: Mock.Of<IServiceProvider>(),
            () =>
            {
                var viewModel = new SettingsViewModel(
                    settings,
                    new ApplicationSettingsApplier(
                        settings,
                        autoSave,
                        new TestEventHub()),
                    dialogs.Object)
                {
                    ViewportLightIntensity = "不是数字"
                };

                viewModel.SaveCommand.Execute(null);

                NUnitAssert.That(viewModel.IsSaved, Is.False);
                dialogs.Verify(
                    value => value.ShowDialogBox(
                        It.Is<string>(message =>
                            message.Contains("数值")),
                        It.IsAny<string>()),
                    Times.Once);
            });
        autoSave.Stop();
    }
}
