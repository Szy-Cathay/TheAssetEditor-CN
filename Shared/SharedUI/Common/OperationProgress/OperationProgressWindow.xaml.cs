using System.ComponentModel;
using System.Windows.Input;

using WindowHandling;

namespace Shared.Ui.Common.OperationProgress;

public partial class OperationProgressWindow : AssetEditorWindow
{
    private bool _allowClose;

    public OperationProgressWindow(OperationProgressWindowHost host)
    {
        InitializeComponent();
        DataContext = host;
        Closing += OnClosing;
    }

    public void Complete()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowClose)
            return;

        eventArgs.Cancel = true;
        if (DataContext is not OperationProgressWindowHost host)
            return;

        ICommand? cancelCommand = host.CancelCommand;
        if (cancelCommand?.CanExecute(null) == true)
            cancelCommand.Execute(null);
    }
}
