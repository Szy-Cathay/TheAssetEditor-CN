using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using CommonControls;

using AssetEditor.UiCommands;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditor.Views.FolderProject;

public partial class FolderProjectProgressWindow : Window
{
    private readonly Func<
        Action<OperationProgressUpdate>,
        CancellationToken,
        FolderProjectContainer?> _operation;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly bool _canCancel;
    private FolderProjectContainer? _result;
    private ExceptionDispatchInfo? _failure;
    private bool _cancelled;
    private bool _operationCompleted;
    private OperationProgressVisibilityController
        _visibilityController = null!;

    public FolderProjectProgressWindow(
        string title,
        string message,
        Func<Action<OperationProgressUpdate>,
            FolderProjectContainer?> operation)
    {
        _operation = (reportProgress, _) => operation(reportProgress);
        _cancellationTokenSource = new CancellationTokenSource();
        Initialize(title, message);
    }

    public FolderProjectProgressWindow(
        string title,
        string message,
        Func<Action<OperationProgressUpdate>,
            CancellationToken,
            FolderProjectContainer?> operation,
        CancellationToken cancellationToken)
    {
        _operation = operation;
        _cancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        _canCancel = true;
        Initialize(title, message);
    }

    private void Initialize(string title, string message)
    {
        InitializeComponent();
        _visibilityController = new OperationProgressVisibilityController(
            Dispatcher,
            SetWindowFeedbackVisibility);
        DarkTitleBarHelper.Enable(this);
        Title = title;
        FolderProjectOperationProgress.Report(
            new OperationProgressUpdate(message));
        CancelFooter.Visibility = _canCancel
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (Application.Current?.MainWindow is { } owner &&
            !ReferenceEquals(owner, this))
        {
            Owner = owner;
        }

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public FolderProjectContainer? Run()
    {
        try
        {
            ShowDialog();
            _failure?.Throw();
            return _result;
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    public FolderProjectProgressResult RunCancelable()
    {
        try
        {
            ShowDialog();
            _failure?.Throw();
            return new FolderProjectProgressResult(
                _result,
                _cancelled);
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _visibilityController.Begin();
        try
        {
            _result = await Task.Run(
                () => _operation(
                    FolderProjectOperationProgress.Report,
                    _cancellationTokenSource.Token));
        }
        catch (OperationCanceledException)
            when (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancelled = true;
        }
        catch (Exception exception)
        {
            _failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            await _visibilityController.EndAsync();
            _operationCompleted = true;
            DialogResult = _failure == null;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_operationCompleted)
        {
            if (_canCancel)
                _cancellationTokenSource.Cancel();
            e.Cancel = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource.Cancel();
    }

    private void SetWindowFeedbackVisibility(bool isVisible)
    {
        Opacity = isVisible ? 1 : 0;
        IsHitTestVisible = isVisible;
    }
}
