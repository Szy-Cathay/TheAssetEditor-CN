using System.Windows;
using Editors.ImportExport.Common;
using Editors.ImportExport.Exporting.Presentation;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using WindowHandling;

namespace Editors.ImportExport.Importing.Presentation;

public partial class ImportWindow : AssetEditorWindow
{
    private readonly ImporterCoreViewModel _viewModel;
    private readonly IStandardDialogs _standardDialogs;

    public ImportWindow(
        ImporterCoreViewModel viewModel,
        IStandardDialogs standardDialogs)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _standardDialogs = standardDialogs;
        DataContext = _viewModel;
    }

    internal void Initialize(
        PackFileContainer packFileContainer,
        string packPath,
        string diskFile)
    {
        _viewModel.Initialize(packFileContainer, packPath, diskFile);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PathValidator.IsValid(_viewModel.SystemPath))
        {
            _standardDialogs.ShowDialogBox(
                LocalizationManager.Instance.Get("Msg.InvalidOrEmptyPath"),
                LocalizationManager.Instance.Get("Msg.GeneralError"));
            return;
        }

        ImportButton.IsEnabled = false;
        _viewModel.IsOperationActive = true;
        var succeeded = false;
        try
        {
            succeeded = await _viewModel.ImportAsync();
        }
        catch (Exception ex)
        {
            _standardDialogs.ShowExceptionWindow(ex, "高级 glTF/GLB 导入失败。");
        }
        finally
        {
            _viewModel.IsOperationActive = false;
            ImportButton.IsEnabled = true;
        }

        if (succeeded)
            Close();
    }
}
