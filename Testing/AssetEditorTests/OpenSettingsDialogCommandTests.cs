using System.Windows;
using AssetEditor.Services.Settings;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using AssetEditor.Views.Settings;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class OpenSettingsDialogCommandTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void Execute_WhenSettingsWindowIsFirstWindow_ShowsWithoutSelfOwnerException()
    {
        var settings = new ApplicationSettingsService();
        var autoSave = new PackAutoSaveService(
            Mock.Of<IPackFileService>(),
            settings);
        var viewModel = new SettingsViewModel(
            settings,
            new ApplicationSettingsApplier(
                settings,
                autoSave,
                new TestEventHub()),
            Mock.Of<IStandardDialogs>());

        try
        {
            WpfTestApplicationHost.InvokeWithThemeResources(
                Mock.Of<IServiceProvider>(),
                () =>
                {
                    Application.Current.MainWindow = null;
                    using var services = new SettingsServiceProvider(viewModel);
                    var command = new OpenSettingsDialogCommand(services);

                    NUnitAssert.That(
                        Application.Current.MainWindow,
                        Is.Null);
                    NUnitAssert.DoesNotThrow(command.Execute);
                    NUnitAssert.That(services.WasShown, Is.True);
                });
        }
        finally
        {
            autoSave.Stop();
        }
    }

    private sealed class SettingsServiceProvider : IServiceProvider, IDisposable
    {
        private readonly SettingsViewModel _viewModel;
        private SettingsWindow? _window;

        public bool WasShown { get; private set; }

        public SettingsServiceProvider(SettingsViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(SettingsViewModel))
                return _viewModel;
            if (serviceType != typeof(SettingsWindow))
                return null;

            _window = new SettingsWindow();
            _window.Loaded += (_, _) =>
            {
                WasShown = true;
                _window.DataContext = null;
                _window.Close();
            };
            return _window;
        }

        public void Dispose()
        {
            if (_window != null)
            {
                _window.DataContext = null;
                _window.Close();
            }

            Application.Current.MainWindow = null;
        }
    }
}
