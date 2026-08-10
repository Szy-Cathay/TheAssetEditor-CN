using System.Windows;
using Editors.ImportExport.Common;
using Editors.ImportExport.Exporting.Presentation;
using Editors.ImportExport.Importing;
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
        ImportResult? result = null;
        try
        {
            result = await _viewModel.ImportAsync();
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

        if (result == null)
            return;

        var resultMessage = BuildResultMessage(result);
        if (result.Succeeded)
        {
            _standardDialogs.ShowDialogBox(
                resultMessage,
                LocalizationManager.Instance.Get("ImportWindow.SuccessTitle"),
                UiMessageBoxIcon.Information);
            Close();
            return;
        }

        if (result.Exception != null)
        {
            _standardDialogs.ShowExceptionWindow(
                result.Exception,
                resultMessage);
            return;
        }

        _standardDialogs.ShowDialogBox(
            resultMessage,
            LocalizationManager.Instance.Get("ImportWindow.FailureTitle"),
            UiMessageBoxIcon.Error);
    }

    internal static string BuildResultMessage(ImportResult result)
    {
        var sections = new List<string>();
        if (result.Succeeded)
        {
            sections.Add(result.OutputPaths.Count == 0
                ? LocalizationManager.Instance.Get("ImportWindow.NoOutputs")
                : BuildSection(
                    LocalizationManager.Instance.Get("ImportWindow.OutputPathsHeading"),
                    result.OutputPaths));
        }
        else
        {
            sections.Add(BuildSection(
                LocalizationManager.Instance.Get("ImportWindow.ErrorHeading"),
                result.Errors));
        }

        if (result.Warnings.Count != 0)
        {
            sections.Add(BuildSection(
                LocalizationManager.Instance.Get("ImportWindow.WarningHeading"),
                result.Warnings));
        }

        if (result.MaterialSummary != null)
        {
            var materialItems = result.MaterialSummary.MaskedMaterials
                .Select(item => LocalizationManager.Instance.GetFormat(
                    "GltfImporter.MaterialSummary.Mask",
                    item.MaterialName,
                    item.AlphaCutoff))
                .Concat(result.MaterialSummary.SkippedSemantics.Select(item =>
                    LocalizationManager.Instance.GetFormat(
                        "GltfImporter.MaterialSummary.Skipped",
                        item.MaterialName,
                        GetMaterialSemanticDisplayName(item.Semantic))))
                .ToList();
            if (materialItems.Count != 0)
            {
                sections.Add(BuildSection(
                    LocalizationManager.Instance.Get("ImportWindow.MaterialHeading"),
                    materialItems));
            }
        }

        if (result.HumanoidScale != null)
        {
            var scaleItems = new List<string>();
            scaleItems.Add(result.HumanoidScale.SourceHeight != null
                ? LocalizationManager.Instance.GetFormat(
                    "GltfImporter.ScaleSummary.SourceHeight",
                    result.HumanoidScale.SourceHeight.Value)
                : LocalizationManager.Instance.Get(
                    "GltfImporter.ScaleSummary.SourceHeightUnavailable"));
            scaleItems.Add(result.HumanoidScale.ReferenceHeight != null
                ? LocalizationManager.Instance.GetFormat(
                    "GltfImporter.ScaleSummary.ReferenceHeight",
                    result.HumanoidScale.ReferenceHeight.Value)
                : LocalizationManager.Instance.Get(
                    "GltfImporter.ScaleSummary.ReferenceHeightUnavailable"));
            scaleItems.Add(LocalizationManager.Instance.GetFormat(
                "GltfImporter.ScaleSummary.ScaleFactor",
                result.HumanoidScale.ScaleFactor));
            scaleItems.Add(result.HumanoidScale.Reason);
            sections.Add(BuildSection(
                LocalizationManager.Instance.Get("ImportWindow.HumanoidScaleHeading"),
                scaleItems));
        }

        if (result.SourceForward != null)
        {
            sections.Add(BuildSection(
                LocalizationManager.Instance.Get(
                    "ImportWindow.SourceForwardHeading"),
                [
                    LocalizationManager.Instance.GetFormat(
                        "GltfImporter.SourceForward.Summary.Direction",
                        result.SourceForward.SourceDirection),
                    result.SourceForward.Conversion,
                ]));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string GetMaterialSemanticDisplayName(string semantic) =>
        semantic switch
        {
            "Emissive" => LocalizationManager.Instance.Get(
                "GltfImporter.MaterialSemantic.Emissive"),
            "Occlusion" => LocalizationManager.Instance.Get(
                "GltfImporter.MaterialSemantic.Occlusion"),
            _ => LocalizationManager.Instance.GetFormat(
                "GltfImporter.MaterialSemantic.Unsupported",
                semantic),
        };

    private static string BuildSection(
        string heading,
        IReadOnlyList<string> items) =>
        heading + Environment.NewLine +
        string.Join(Environment.NewLine, items.Select(item => $"• {item}"));
}
