using System.IO;
using System.Windows;
using Editors.ImportExport.Common;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using WindowHandling;

namespace Editors.ImportExport.Exporting.Presentation;

public partial class ExportWindow : AssetEditorWindow
{
    private readonly ExporterCoreViewModel _viewModel;
    private readonly IStandardDialogs _standardDialogs;

    public ExportWindow(
        ExporterCoreViewModel viewModel,
        IStandardDialogs standardDialogs)
    {
        InitializeComponent();
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
        try
        {
            succeeded = await _viewModel.ExportAsync();
        }
        catch (Exception ex)
        {
            _standardDialogs.ShowExceptionWindow(ex, "高级 glTF 导出失败。");
        }
        finally
        {
            _viewModel.IsOperationActive = false;
            ExportButton.IsEnabled = true;
        }

        if (succeeded)
            Close();
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
