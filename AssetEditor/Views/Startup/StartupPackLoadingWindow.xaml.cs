using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

using CommonControls;

using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditor.Views.Startup;

public partial class StartupPackLoadingWindow : Window
{
    private readonly Func<
        Action<CaPackLoadProgress>, CancellationToken,
        PackFileContainer?> _loadPacks;
    private readonly Action<PackFileContainer> _registerPacks;
    private readonly Action _openSettings;
    private bool _canClose;
    private bool _isLoading;
    private bool _isShowingDialog;
    private bool _succeeded;
    private bool _closed;
    private bool _registered;
    private bool _initialized;
    private PackFileContainer? _container;
    private CancellationTokenSource? _cancellation;
    private readonly Func<Action<OperationProgressUpdate>, CancellationToken, Task>? _prepareWorkspace;
    private readonly Action? _initialize;
    private OperationProgressUpdate? _pendingProgress;
    private int _progressQueued;
    private int _attempt;
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly OperationProgressWindowTransition _transition;
    private readonly OperationProgressVisibilityController
        _visibilityController;

    public StartupPackLoadingWindow(
        Func<Action<CaPackLoadProgress>, PackFileContainer?> loadPacks,
        Action<PackFileContainer> registerPacks,
        Action openSettings)
        : this((progress, _) => loadPacks(progress), registerPacks, openSettings)
    {
    }

    public StartupPackLoadingWindow(
        Func<Action<CaPackLoadProgress>, CancellationToken, PackFileContainer?> loadPacks,
        Action<PackFileContainer> registerPacks,
        Action openSettings,
        Func<Action<OperationProgressUpdate>, CancellationToken, Task>? prepareWorkspace = null,
        Action? initialize = null)
    {
        _loadPacks = loadPacks;
        _registerPacks = registerPacks;
        _openSettings = openSettings;
        _prepareWorkspace = prepareWorkspace;
        _initialize = initialize;

        InitializeComponent();
        _transition = new OperationProgressWindowTransition(this);
        _visibilityController = new OperationProgressVisibilityController(
            Dispatcher,
            SetWindowFeedbackVisibility);
        DarkTitleBarHelper.Enable(this);
        if (_prepareWorkspace != null)
        {
            Width = Math.Min(520, SystemParameters.WorkArea.Width - 48);
            MaxHeight = Math.Min(700, SystemParameters.WorkArea.Height - 24);
            Template = (ControlTemplate)FindResource("StartupSplashTemplate");
            WindowChrome.SetWindowChrome(this, null);
            Background = Brushes.Transparent;
            StartupOperationProgress.Resources["AeBrush.Accent"] = FindResource("StartupAccentBrush");
            StartupOperationProgress.Resources["AeBrush.Surface3"] = Brushes.WhiteSmoke;
            StartupOperationProgress.Resources["AeBrush.Border"] = Brushes.LightGray;
            StartupOperationProgress.UseLeadingIndicator = true;
            StartupOperationProgress.Resources[typeof(Expander)] = FindResource("StartupHiddenDetails");
            ExitButton.Visibility = Visibility.Collapsed;
            FailureCloseHint.Visibility = Visibility.Visible;
            StartupArtwork.Visibility = Visibility.Visible;
            StartupIdentity.Visibility = Visibility.Visible;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _transition.Show();
        }
        if (Application.Current?.MainWindow is { IsVisible: true } owner &&
            !ReferenceEquals(owner, this))
        {
            Owner = owner;
        }

        ContentRendered += OnLoaded;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellation?.Cancel();
            _visibilityController.ForceHide();
            _completion.TrySetResult(_succeeded);
        };
    }

    public bool Run()
    {
        _isShowingDialog = true;
        try
        {
            ShowDialog();
        }
        finally
        {
            _isShowingDialog = false;
        }

        return _succeeded;
    }

    public Task<bool> RunAsync()
    {
        Show();
        return _completion.Task;
    }

    public async Task HandOffToAsync(Window mainWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendered(object? sender, EventArgs e) => rendered.TrySetResult();
        void OnClosed(object? sender, EventArgs e) => rendered.TrySetCanceled();
        mainWindow.ContentRendered += OnRendered;
        mainWindow.Closed += OnClosed;
        // WPF rejects ShowActivated=false when the initial state is maximized.
        mainWindow.ShowActivated = mainWindow.WindowState == WindowState.Maximized;
        mainWindow.IsEnabled = false;
        try
        {
            new WindowInteropHelper(mainWindow).EnsureHandle();
            Owner = mainWindow;
            mainWindow.Show();
            await rendered.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            mainWindow.ContentRendered -= OnRendered;
            mainWindow.Closed -= OnClosed;
        }
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        ContentRendered -= OnLoaded;
        await StartLoadingAsync();
    }

    private async Task StartLoadingAsync()
    {
        if (_isLoading || _closed)
            return;

        _isLoading = true;
        var attempt = ++_attempt;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        ExitButton.IsEnabled = true;
        CheckGamePathButton.IsEnabled = !_registered;
        FailurePanel.Visibility = Visibility.Collapsed;
        StartupOperationProgress.IsOperationActive = true;
        _visibilityController.Begin();
        if (_prepareWorkspace != null)
            _visibilityController.RevealImmediately();
        StartupOperationProgress.Report(new OperationProgressUpdate(
            GetText("StartupPackLoading.Preparing")));

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (!_initialized)
            {
                _initialize?.Invoke();
                _initialized = true;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            _container ??= await Task.Run(() =>
                _loadPacks(progress => ReportProgress(progress, attempt), cancellation.Token),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed)
                return;
            if (_container == null)
            {
                ShowFailure();
                return;
            }

            Interlocked.Exchange(ref _pendingProgress, null);
            StartupOperationProgress.Report(new OperationProgressUpdate(
                GetText("StartupPackLoading.Registering"),
                _container.Name,
                0,
                1));
            await Dispatcher.Yield(DispatcherPriority.Render);
            if (!_registered)
            {
                _registerPacks(_container);
                _registered = true;
            }
            if (_prepareWorkspace != null)
                await _prepareWorkspace(update => QueueProgress(update, attempt), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _pendingProgress, null);
            StartupOperationProgress.Report(new OperationProgressUpdate(
                GetText("StartupPackLoading.Completed"),
                _container.Name,
                1,
                1));

            await _visibilityController.EndAsync();
            await _transition.HideAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_closed)
            {
                _succeeded = true;
                _canClose = true;
                if (_isShowingDialog)
                    DialogResult = true;
                else
                    Close();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await CloseCancelledAsync();
        }
        catch (Exception exception)
        {
            Logging.Create<StartupPackLoadingWindow>().Here().Error(exception.ToString());
            if (!_registered)
                _container = null;
            if (cancellation.IsCancellationRequested)
                await CloseCancelledAsync();
            else if (!_closed)
                ShowFailure();
        }
        finally
        {
            _isLoading = false;
            _cancellation = null;
        }
    }

    private void ReportProgress(CaPackLoadProgress progress, int attempt)
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
        QueueProgress(new OperationProgressUpdate(
            GetText(statusKey),
            progress.Detail,
            progress.Completed,
            progress.Total), attempt);
    }

    private void QueueProgress(OperationProgressUpdate progress, int attempt)
    {
        if (_closed || attempt != _attempt || _cancellation?.IsCancellationRequested == true)
            return;
        if (Dispatcher.CheckAccess())
        {
            Interlocked.Exchange(ref _pendingProgress, null);
            StartupOperationProgress.Report(progress);
            return;
        }
        Interlocked.Exchange(ref _pendingProgress, progress);
        if (Interlocked.Exchange(ref _progressQueued, 1) != 0)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _progressQueued, 0);
            var latest = Interlocked.Exchange(ref _pendingProgress, null);
            if (latest != null && !_closed && _isLoading &&
                _cancellation?.IsCancellationRequested != true)
                StartupOperationProgress.Report(latest);
        });
    }

    private void ShowFailure()
    {
        Interlocked.Exchange(ref _pendingProgress, null);
        CheckGamePathButton.IsEnabled = !_registered;
        CheckGamePathButton.Visibility = _registered ? Visibility.Collapsed : Visibility.Visible;
        FailureHelpText.Text = GetText(_registered
            ? "StartupPackLoading.WorkspaceFailureHelp"
            : "StartupPackLoading.FailureHelp");
        StartupOperationProgress.Report(new OperationProgressUpdate(
            GetText(_registered ? "StartupPackLoading.WorkspaceFailed" : "StartupPackLoading.Failed"),
            GetText(_registered ? "StartupPackLoading.WorkspaceFailureDetail" : "StartupPackLoading.FailureDetail")));
        StartupOperationProgress.IsOperationActive = false;
        FailurePanel.Visibility = Visibility.Visible;
        _visibilityController.RevealImmediately();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) =>
        _ = StartLoadingAsync();

    private void OnCheckGamePathClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isLoading && !_registered)
            _openSettings();
    }

    private async void OnExitClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            ExitButton.IsEnabled = false;
            _cancellation?.Cancel();
            StartupOperationProgress.Report(new OperationProgressUpdate(
                GetText("StartupPackLoading.Cancelling")));
            return;
        }
        await CloseCancelledAsync();
    }

    private async Task CloseCancelledAsync()
    {
        _canClose = true;
        _visibilityController.ForceHide();
        await _transition.HideAsync();
        // Leave WPF's Closing callback before an immediate transition closes again.
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (_closed)
            return;
        if (_isShowingDialog)
            DialogResult = false;
        else if (IsLoaded)
            Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true;
            OnExitClick(this, new RoutedEventArgs());
        }
    }

    private static string GetText(string key) =>
        LocalizationManager.Instance.Get(key);

    private void SetWindowFeedbackVisibility(bool isVisible)
    {
        if (isVisible)
            _transition.Show();
        else
            _ = _transition.HideAsync();
    }
}
