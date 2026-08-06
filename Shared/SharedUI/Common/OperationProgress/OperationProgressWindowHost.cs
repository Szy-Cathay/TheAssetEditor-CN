using System.Windows;
using System.Windows.Input;

namespace Shared.Ui.Common.OperationProgress;

public sealed class OperationProgressWindowHost : FrameworkElement
{
    public static readonly DependencyProperty WindowTitleProperty =
        DependencyProperty.Register(
            nameof(WindowTitle),
            typeof(string),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText),
            typeof(string),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CurrentDetailTextProperty =
        DependencyProperty.Register(
            nameof(CurrentDetailText),
            typeof(string),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsOperationActiveProperty =
        DependencyProperty.Register(
            nameof(IsOperationActive),
            typeof(bool),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(false, OnIsOperationActiveChanged));

    public static readonly DependencyProperty ProgressValueProperty =
        DependencyProperty.Register(
            nameof(ProgressValue),
            typeof(double),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(0d));

    public static readonly DependencyProperty ProgressMaximumProperty =
        DependencyProperty.Register(
            nameof(ProgressMaximum),
            typeof(double),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(1d));

    public static readonly DependencyProperty IsProgressIndeterminateProperty =
        DependencyProperty.Register(
            nameof(IsProgressIndeterminate),
            typeof(bool),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(
            nameof(CancelCommand),
            typeof(ICommand),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(
            nameof(CancelText),
            typeof(string),
            typeof(OperationProgressWindowHost),
            new PropertyMetadata(string.Empty));

    private OperationProgressWindow? _window;
    private readonly OperationProgressVisibilityController
        _visibilityController;

    public OperationProgressWindowHost()
    {
        IsHitTestVisible = false;
        _visibilityController = new OperationProgressVisibilityController(
            Dispatcher,
            SetWindowVisibility);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string WindowTitle
    {
        get => (string)GetValue(WindowTitleProperty);
        set => SetValue(WindowTitleProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string CurrentDetailText
    {
        get => (string)GetValue(CurrentDetailTextProperty);
        set => SetValue(CurrentDetailTextProperty, value);
    }

    public bool IsOperationActive
    {
        get => (bool)GetValue(IsOperationActiveProperty);
        set => SetValue(IsOperationActiveProperty, value);
    }

    public double ProgressValue
    {
        get => (double)GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public double ProgressMaximum
    {
        get => (double)GetValue(ProgressMaximumProperty);
        set => SetValue(ProgressMaximumProperty, value);
    }

    public bool IsProgressIndeterminate
    {
        get => (bool)GetValue(IsProgressIndeterminateProperty);
        set => SetValue(IsProgressIndeterminateProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (IsOperationActive)
            _visibilityController.Begin();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _visibilityController.ForceHide();
    }

    private void SetWindowVisibility(bool isVisible)
    {
        if (isVisible)
            ShowWindowNow();
        else
            CloseWindowNow();
    }

    private void ShowWindowNow()
    {
        if (!IsLoaded || _window is not null)
            return;

        var application = Application.Current;
        if (application is not null &&
            !application.Dispatcher.CheckAccess())
        {
            return;
        }

        var owner = Window.GetWindow(this) ?? application?.MainWindow;
        _window = new OperationProgressWindow(this);
        if (owner is { IsLoaded: true } && owner != _window)
            _window.Owner = owner;
        else
            _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _window.Show();
    }

    private void CloseWindowNow()
    {
        if (_window is null)
            return;

        var window = _window;
        _window = null;
        window.Complete();
    }

    private static void OnIsOperationActiveChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var host = (OperationProgressWindowHost)dependencyObject;
        if (eventArgs.NewValue is true)
        {
            if (host.IsLoaded)
                host._visibilityController.Begin();
        }
        else
        {
            _ = host._visibilityController.EndAsync();
        }
    }
}
