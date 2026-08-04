using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using AssetEditor.Views.Startup;
using CommunityToolkit.Diagnostics;
using Editors.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

namespace AssetEditor
{
    public partial class App : Application, IAssetEditorMain
    {
        IServiceProvider? _serviceProvider;
        AssetEditorIpcServer? _ipcServer;

        public IServiceProvider ServiceProvider 
        {
            get 
            {
                Guard.IsNotNull(_serviceProvider, nameof(ServiceProvider));
                return _serviceProvider;
            } 
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ApplicationStateRecorder.Initialize();
            PackFileLog.IsLoggingEnabled = false;

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Current.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(DispatcherUnhandledExceptionHandler);

            var forceValidateServiceScopes = Debugger.IsAttached;
            _serviceProvider = new DependencyInjectionConfig().Build(forceValidateServiceScopes);

            _ = _serviceProvider.GetRequiredService<RecentFilesTracker>(); // Force instance of the RecentFilesTracker
            _ = _serviceProvider.GetRequiredService<IScopeRepository>();  // Force instance of the IScopeRepository

            var uiCommandFactory = _serviceProvider.GetRequiredService<IUiCommandFactory>();

            var settingsService = _serviceProvider.GetRequiredService<ApplicationSettingsService>();
            settingsService.AllowSettingsUpdate = true;
            settingsService.Load();

            var localizationManager = _serviceProvider.GetRequiredService<LocalizationManager>();
            localizationManager.LoadLanguage();

            // Show the settings window if its the first time the tool is ran
            if (settingsService.CurrentSettings.IsFirstTimeStartingApplication)
                HandleFirstTimeSettings(uiCommandFactory, settingsService);

            var devConfigManager = _serviceProvider.GetRequiredService<DevelopmentConfigurationManager>();
            devConfigManager.Initialize(e);
            devConfigManager.OverrideSettings();

            // Show window first, then load packs in background.
            // Theme switching is handled by ThemesController (swaps colour dictionaries at runtime).
            ShowMainWindow();

            if (!LoadCAPackFiles(settingsService, uiCommandFactory))
            {
                Shutdown();
                return;
            }

            FinishStartup(devConfigManager);
        }

        private bool LoadCAPackFiles(
            ApplicationSettingsService settingsService,
            IUiCommandFactory uiCommandFactory)
        {
            var packfileService = _serviceProvider
                .GetRequiredService<IPackFileService>();
            var containerLoader = _serviceProvider
                .GetRequiredService<IPackFileContainerLoader>();
            var loadingWindow = new StartupPackLoadingWindow(
                reportProgress =>
                {
                    var gamePath =
                        settingsService.GetGamePathForCurrentGame();
                    if (string.IsNullOrWhiteSpace(gamePath) ||
                        !System.IO.Directory.Exists(gamePath))
                    {
                        return null;
                    }

                    return containerLoader.LoadAllCaFiles(
                        settingsService.CurrentSettings.CurrentGame,
                        reportProgress);
                },
                container => packfileService.AddContainer(container),
                () => uiCommandFactory
                    .Create<OpenSettingsDialogCommand>()
                    .Execute());
            return loadingWindow.Run();
        }

        private void FinishStartup(DevelopmentConfigurationManager devConfigManager)
        {
            devConfigManager.CreateTestPackFiles();
            devConfigManager.OpenFileOnLoad();

            _ipcServer = _serviceProvider.GetRequiredService<AssetEditorIpcServer>();
            _ipcServer.Start();
            _ = CheckVersion(_serviceProvider.GetRequiredService<IUiCommandFactory>());
        }

        private static void HandleFirstTimeSettings(IUiCommandFactory uiCommandFactory, ApplicationSettingsService settingsService)
        {
            uiCommandFactory.Create<OpenSettingsDialogCommand>().Execute();

            settingsService.CurrentSettings.IsFirstTimeStartingApplication = false;
            settingsService.Save();
        }

        void ShowMainWindow()
        {
            var applicationSettingsService = _serviceProvider.GetRequiredService<ApplicationSettingsService>();
            ThemesController.SetTheme(applicationSettingsService.CurrentSettings.Theme);

            // Apply custom font after theme is set (ControlColours.xaml may define a default font)
            var fontUri = FontSettingsHelper.GetFontFamilyUri(
                applicationSettingsService.CurrentSettings.AppFont,
                applicationSettingsService.CurrentSettings.AppFontWeight);
            if (fontUri != null)
                ThemesController.ApplyCustomFont(fontUri);

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.DataContext = _serviceProvider.GetRequiredService<MainViewModel>();
            mainWindow.Closed += OnMainWindowClosed;
            mainWindow.Show();
        }

       private void OnMainWindowClosed(object sender, EventArgs e)
        {
            _ipcServer?.Dispose();
            _ipcServer = null;

            foreach (Window window in Current.Windows)
                window.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _ipcServer?.Dispose();
            _ipcServer = null;
            base.OnExit(e);
        }


        private static async Task CheckVersion(IUiCommandFactory uiCommandFactory)
        {
            var newerReleases = await VersionChecker.GetNewerReleases();
            if (newerReleases != null)
                uiCommandFactory.Create<OpenUpdaterWindowCommand>().Execute(newerReleases);
        }

        void DispatcherUnhandledExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            Logging.Create<App>().Here().Fatal(args.Exception.ToString());

            var exceptionService = _serviceProvider?.GetService<IStandardDialogs>();
            if (exceptionService != null)
               exceptionService.ShowExceptionWindow(args.Exception);   
            else
                MessageBox.Show(args.Exception.ToString(), LocalizationManager.Instance.Get("Msg.GeneralError"));

            args.Handled = true;
        }
    }
}
