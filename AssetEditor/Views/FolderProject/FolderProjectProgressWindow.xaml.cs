using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows;

using CommonControls;

using Shared.Core.PackFiles.Models;

namespace AssetEditor.Views.FolderProject;

public partial class FolderProjectProgressWindow : Window
{
    private readonly Func<FolderProjectContainer?> _operation;
    private FolderProjectContainer? _result;
    private ExceptionDispatchInfo? _failure;
    private bool _operationCompleted;

    public FolderProjectProgressWindow(
        string title,
        string message,
        Func<FolderProjectContainer?> operation)
    {
        _operation = operation;
        InitializeComponent();
        DarkTitleBarHelper.Enable(this);
        Title = title;
        StatusTextBlock.Text = message;
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
        try
        {
            _result = await Task.Run(_operation);
        }
        catch (Exception exception)
        {
            _failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            _operationCompleted = true;
            DialogResult = _failure == null;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_operationCompleted)
            e.Cancel = true;
    }
}
