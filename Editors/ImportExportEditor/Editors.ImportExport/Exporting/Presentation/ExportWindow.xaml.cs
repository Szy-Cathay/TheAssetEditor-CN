using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using Editors.ImportExport.Common;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.Common.OperationProgress;
using WindowHandling;

namespace Editors.ImportExport.Exporting.Presentation;

public partial class ExportWindow : AssetEditorWindow
{
    private readonly ExporterCoreViewModel _viewModel;
    private readonly IStandardDialogs _standardDialogs;
    private readonly OperationProgressWindowHost _exportOperationProgress;

    public ExportWindow(
        ExporterCoreViewModel viewModel,
        IStandardDialogs standardDialogs)
    {
        InitializeComponent();
        _exportOperationProgress = ((Grid)Content).Children
            .OfType<OperationProgressWindowHost>()
            .Single();
        _viewModel = viewModel;
        _standardDialogs = standardDialogs;
        DataContext = _viewModel;
    }

    internal void Initialize(PackFile packFile)
    {
        _viewModel.Initialize(packFile);
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsOperationActive)
            return;

        if (!PathValidator.IsValid(_viewModel.SystemPath))
        {
            _standardDialogs.ShowDialogBox(
                LocalizationManager.Instance.Get("Msg.InvalidOrEmptyPath"),
                LocalizationManager.Instance.Get("Msg.GeneralError"));
            return;
        }

        ExportButton.IsEnabled = false;
        _viewModel.IsOperationActive = true;
        var succeeded = false;
        Exception? exportException = null;
        try
        {
            succeeded = await _viewModel.ExportAsync();
        }
        catch (Exception ex)
        {
            exportException = ex;
        }
        finally
        {
            await _exportOperationProgress.CompleteAsync();
            _viewModel.IsOperationActive = false;
            ExportButton.IsEnabled = true;
        }

        if (exportException != null)
        {
            _standardDialogs.ShowExceptionWindow(exportException, "高级 glTF 导出失败。");
            return;
        }

        if (succeeded)
            Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsOperationActive)
            e.Cancel = true;
        base.OnClosing(e);
    }
}

public static class PathValidator
{
    public static bool IsValid(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var fileNameOnly = Path.GetFileName(fileName);
        if (fileNameOnly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        var folder = Path.GetDirectoryName(fileName);
        return !string.IsNullOrWhiteSpace(folder) &&
               folder.IndexOfAny(Path.GetInvalidPathChars()) < 0;
    }
}
