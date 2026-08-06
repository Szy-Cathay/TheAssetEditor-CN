using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows;

using CommonControls;

using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditor.Views.FolderProject;

public partial class FolderProjectProgressWindow : Window
{
    private readonly Func<
        Action<OperationProgressUpdate>,
        FolderProjectContainer?> _operation;
    private FolderProjectContainer? _result;
    private ExceptionDispatchInfo? _failure;
    private bool _operationCompleted;
    private readonly OperationProgressVisibilityController
        _visibilityController;

    public FolderProjectProgressWindow(
        string title,
        string message,
        Func<Action<OperationProgressUpdate>,
            FolderProjectContainer?> operation)
    {
        _operation = operation;
        InitializeComponent();
        _visibilityController = new OperationProgressVisibilityController(
            Dispatcher,
            SetWindowFeedbackVisibility);
        DarkTitleBarHelper.Enable(this);
        Title = title;
        FolderProjectOperationProgress.Report(
            new OperationProgressUpdate(message));
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
        ShowDialog();
        _failure?.Throw();
        return _result;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _visibilityController.Begin();
        try
        {
            _result = await Task.Run(
                () => _operation(
                    FolderProjectOperationProgress.Report));
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
            e.Cancel = true;
    }

    private void SetWindowFeedbackVisibility(bool isVisible)
    {
        Opacity = isVisible ? 1 : 0;
        IsHitTestVisible = isVisible;
    }
}
