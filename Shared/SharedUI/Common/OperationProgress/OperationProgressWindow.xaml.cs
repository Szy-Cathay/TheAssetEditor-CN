using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

using WindowHandling;

namespace Shared.Ui.Common.OperationProgress;

public partial class OperationProgressWindow : AssetEditorWindow
{
    private bool _allowClose;
    private bool _closed;
    private readonly OperationProgressWindowTransition _transition;

    public OperationProgressWindow(OperationProgressWindowHost host)
    {
        InitializeComponent();
        DataContext = host;
        _transition = new OperationProgressWindowTransition(this);
        _transition.Show();
        Closing += OnClosing;
        Closed += (_, _) => _closed = true;
    }

    public void Complete()
    {
        _transition.HideImmediately();
        CloseCompletedWindow();
    }

    public async Task CompleteAsync()
    {
        await _transition.HideAsync();
        CloseCompletedWindow();
    }

    private void CloseCompletedWindow()
    {
        if (_closed)
            return;
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
