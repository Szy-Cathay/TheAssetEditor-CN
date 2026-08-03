using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using CommonControls;

using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditor.Views.Startup;

public partial class StartupPackLoadingWindow : Window
{
    private readonly Func<
        Action<CaPackLoadProgress>,
        PackFileContainer?> _loadPacks;
    private readonly Action<PackFileContainer> _registerPacks;
    private readonly Action _openSettings;
    private bool _canClose;
    private bool _isLoading;
    private bool _succeeded;

    public StartupPackLoadingWindow(
        Func<Action<CaPackLoadProgress>, PackFileContainer?> loadPacks,
        Action<PackFileContainer> registerPacks,
        Action openSettings)
    {
        _loadPacks = loadPacks;
        _registerPacks = registerPacks;
        _openSettings = openSettings;

        InitializeComponent();
        DarkTitleBarHelper.Enable(this);
        if (Application.Current?.MainWindow is { } owner &&
            !ReferenceEquals(owner, this))
        {
            Owner = owner;
        }

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public bool Run()
    {
        ShowDialog();
        return _succeeded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await StartLoadingAsync();

    private async Task StartLoadingAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        FailurePanel.Visibility = Visibility.Collapsed;
        StartupOperationProgress.IsOperationActive = true;
        StartupOperationProgress.Report(new OperationProgressUpdate(
            GetText("StartupPackLoading.Preparing")));

        try
        {
            var container = await Task.Run(() =>
                _loadPacks(ReportProgress));
            if (container == null)
            {
                ShowFailure();
                return;
            }

            StartupOperationProgress.Report(new OperationProgressUpdate(
                GetText("StartupPackLoading.Registering"),
                container.Name,
                0,
                1));
            await Dispatcher.Yield(DispatcherPriority.Render);
            _registerPacks(container);
            StartupOperationProgress.Report(new OperationProgressUpdate(
                GetText("StartupPackLoading.Completed"),
                container.Name,
                1,
                1));

            _succeeded = true;
            _canClose = true;
            DialogResult = true;
        }
        catch
        {
            ShowFailure();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ReportProgress(CaPackLoadProgress progress)
    {
        void ApplyProgress()
        {
            var statusKey = progress.Stage switch
            {
                CaPackLoadProgressStage.DiscoveringPacks =>
                    "StartupPackLoading.Discovering",
                CaPackLoadProgressStage.ReadingPacks =>
                    "StartupPackLoading.Reading",
                CaPackLoadProgressStage.MergingPacks =>
                    "StartupPackLoading.Merging",
                _ => "StartupPackLoading.Preparing",
            };
            StartupOperationProgress.Report(new OperationProgressUpdate(
                GetText(statusKey),
                progress.Detail,
                progress.Completed,
                progress.Total));
        }

        if (Dispatcher.CheckAccess())
            ApplyProgress();
        else
            Dispatcher.Invoke(ApplyProgress);
    }

    private void ShowFailure()
    {
        StartupOperationProgress.Report(new OperationProgressUpdate(
            GetText("StartupPackLoading.Failed"),
            GetText("StartupPackLoading.FailureDetail")));
        StartupOperationProgress.IsOperationActive = false;
        FailurePanel.Visibility = Visibility.Visible;
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) =>
        _ = StartLoadingAsync();

    private void OnCheckGamePathClick(
        object sender,
        RoutedEventArgs e) => _openSettings();

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        _canClose = true;
        DialogResult = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_canClose)
            e.Cancel = true;
    }

    private static string GetText(string key) =>
        LocalizationManager.Instance.Get(key);
}
