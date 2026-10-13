using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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
using Shared.Ui.Common.OperationProgress;

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

        protected override async void OnStartup(StartupEventArgs e)
        {
            ApplicationStateRecorder.Initialize();
            PackFileLog.IsLoggingEnabled = false;

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Current.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(DispatcherUnhandledExceptionHandler);

            var forceValidateServiceScopes = Debugger.IsAttached;
            _serviceProvider = new DependencyInjectionConfig().Build(forceValidateServiceScopes);
            LogRuntimeEnvironment();

            _ = _serviceProvider.GetRequiredService<RecentFilesTracker>(); // Force instance of the RecentFilesTracker
            _ = _serviceProvider.GetRequiredService<IScopeRepository>();  // Force instance of the IScopeRepository

            var uiCommandFactory = _serviceProvider.GetRequiredService<IUiCommandFactory>();

            var settingsService = _serviceProvider.GetRequiredService<ApplicationSettingsService>();
            settingsService.AllowSettingsUpdate = true;
            settingsService.Load();

            var localizationManager = _serviceProvider.GetRequiredService<LocalizationManager>();
            localizationManager.LoadLanguage();

            var devConfigManager = _serviceProvider.GetRequiredService<DevelopmentConfigurationManager>();
            devConfigManager.Initialize(e);
            devConfigManager.OverrideSettings();

            ApplyApplicationAppearance();
            if (!devConfigManager.HasActiveConfiguration)
            {
                if (!await LoadCAPackFiles(settingsService, uiCommandFactory))
                {
                    Shutdown();
                    return;
                }
            }
            else
                await ShowMainWindowAsync();

            if (Dispatcher.HasShutdownStarted || MainWindow?.IsVisible != true)
                return;
            if (MainWindow != null)
            {
                MainWindow.IsEnabled = true;
                MainWindow.Activate();
            }
            FinishStartup(devConfigManager, e.Args);
        }

        private async Task<bool> LoadCAPackFiles(
            ApplicationSettingsService settingsService,
            IUiCommandFactory uiCommandFactory)
        {
            var packfileService = _serviceProvider
                .GetRequiredService<IPackFileService>();
            var containerLoader = _serviceProvider
                .GetRequiredService<IPackFileContainerLoader>();
            StartupPackLoadingWindow? loadingWindow = null;
            loadingWindow = new StartupPackLoadingWindow(
                (reportProgress, cancellationToken) =>
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
                        reportProgress,
                        cancellationToken);
                },
                container =>
                {
                    if (packfileService.AddContainer(container) == null)
                        throw new InvalidOperationException("Unable to register the CA pack container.");
                },
                () => uiCommandFactory
                    .Create<OpenSettingsDialogCommand>()
                    .Execute(),
                (progress, cancellationToken) => ShowMainWindowAsync(loadingWindow, progress, cancellationToken),
                () =>
                {
                    if (settingsService.CurrentSettings.IsFirstTimeStartingApplication)
                        HandleFirstTimeSettings(uiCommandFactory, settingsService);
                });
            return await loadingWindow.RunAsync();
        }

        private void FinishStartup(
            DevelopmentConfigurationManager devConfigManager,
            IReadOnlyList<string> startupArguments)
        {
            devConfigManager.CreateTestPackFiles();
            devConfigManager.OpenFileOnLoad();

            _ipcServer = _serviceProvider.GetRequiredService<AssetEditorIpcServer>();
            _ipcServer.Start();
            OpenStartupPack(startupArguments);
            _ = CheckVersion(_serviceProvider.GetRequiredService<IUiCommandFactory>());
        }

        private void OpenStartupPack(
            IReadOnlyList<string> startupArguments)
        {
            var packPath = ExternalPackStartupArgumentParser.FindPackPath(
                startupArguments);
            if (packPath == null)
                return;

            var workflow = _serviceProvider
                .GetRequiredService<IExternalPackOpenWorkflow>();
            _ = workflow.OpenAsync(packPath, CancellationToken.None);
        }

        private static void HandleFirstTimeSettings(IUiCommandFactory uiCommandFactory, ApplicationSettingsService settingsService)
        {
            uiCommandFactory.Create<OpenSettingsDialogCommand>().Execute();

            settingsService.CurrentSettings.IsFirstTimeStartingApplication = false;
            settingsService.Save();
        }

        private void ApplyApplicationAppearance()
        {
            var applicationSettingsService = _serviceProvider.GetRequiredService<ApplicationSettingsService>();
            ThemesController.SetTheme(applicationSettingsService.CurrentSettings.Theme);

            // Apply custom font after theme is set (ControlColours.xaml may define a default font)
            var fontFamily = FontSettingsHelper.GetFontFamily(
                applicationSettingsService.CurrentSettings.AppFont);
            ThemesController.ApplyCustomFont(
                fontFamily,
                FontSettingsHelper.GetFontWeight(
                    applicationSettingsService.CurrentSettings
                        .AppFontWeight));

        }

        private async Task ShowMainWindowAsync(
            StartupPackLoadingWindow? startupWindow = null,
            Action<OperationProgressUpdate>? reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var treeStatus = LocalizationManager.Instance.Get("StartupPackLoading.BuildingTree");
            reportProgress?.Invoke(new OperationProgressUpdate(treeStatus));
            await viewModel.FileTree.InitializeCaFilesAsync(cancellationToken,
                (completed, total) => reportProgress?.Invoke(new OperationProgressUpdate(
                    treeStatus, null, completed, total)));
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke(new OperationProgressUpdate(
                LocalizationManager.Instance.Get("StartupPackLoading.OpeningWorkspace")));
            await Dispatcher.Yield(DispatcherPriority.Background);
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.DataContext = viewModel;
            mainWindow.Closed += OnMainWindowClosed;
            if (startupWindow != null)
                await startupWindow.HandOffToAsync(mainWindow, cancellationToken);
            else
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
            try
            {
                var newerReleases = await VersionChecker.GetNewerReleases();
                if (newerReleases != null)
                    uiCommandFactory.Create<OpenUpdaterWindowCommand>().Execute(newerReleases);
            }
            catch (Exception exception)
            {
                Logging.Create<App>().Here().Warning(
                    $"Unable to check for updates: {exception}");
                if (Current?.MainWindow?.DataContext is MainViewModel viewModel)
                {
                    viewModel.UpdateCheckStatus = LocalizationManager.Instance.Get(
                        "UpdaterWindow.CheckFailed");
                    viewModel.HasUpdateCheckFailure = true;
                }
            }
        }

        private static void LogRuntimeEnvironment()
        {
            Logging.Create<App>().Here().Information(
                "Runtime environment: OS={OSDescription}; OSVersion={OSVersion}; Architecture={Architecture}; Framework={Framework}",
                RuntimeInformation.OSDescription,
                Environment.OSVersion.VersionString,
                RuntimeInformation.ProcessArchitecture,
                RuntimeInformation.FrameworkDescription);
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
