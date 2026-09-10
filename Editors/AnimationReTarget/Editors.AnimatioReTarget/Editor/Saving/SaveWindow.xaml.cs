using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using CommonControls.SelectionListDialog;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.SelectionListDialog;
using Shared.Ui.Common.OperationProgress;
using WindowHandling;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    /// <summary>
    /// Interaction logic for SaveWindow.xaml
    /// </summary>
    public partial class SaveWindow : AssetEditorWindow
    {
        private readonly SaveSettings _settings;
        private readonly IStandardDialogs _standardDialogs;
        private readonly OperationProgressWindowHost _batchProgress;
        private SaveManager _saveManager = null!;
        private CancellationTokenSource? _batchCancellation;

        public SaveWindow(
            SaveSettings viewModel,
            IStandardDialogs standardDialogs)
        {
            InitializeComponent();
            _batchProgress = ((Grid)Content).Children
                .OfType<OperationProgressWindowHost>()
                .Single();
            _settings = viewModel;
            _standardDialogs = standardDialogs;
            DataContext = viewModel;
        }

        public void Initialize(SaveManager saveManager)
        {
            _saveManager = saveManager;
        }

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            _saveManager.SaveAnimation();
            Close();
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BrowseButtonClick(object sender, RoutedEventArgs e)
        {
            var outputProject = _saveManager.GetEditableFolderProject();
            if (outputProject == null)
            {
                ShowBatchWarning(
                    "AnimReTarget.Batch.Error.FolderProjectRequired");
                return;
            }

            var result = _standardDialogs.DisplayBrowseFolderDialog(
                outputProject);
            if (result.Result)
                _settings.BatchTargetFolder = result.Folder;
        }

        private async void BatchButtonClick(
            object sender,
            RoutedEventArgs e)
        {
            if (!_saveManager.CanBatchRetarget(out var validationError))
            {
                _standardDialogs.ShowDialogBox(
                    validationError,
                    LocalizationManager.Instance.Get("Msg.GeneralError"),
                    UiMessageBoxIcon.Warning);
                return;
            }

            if (!_saveManager.ConfirmSparseMapping())
                return;

            var candidates = _saveManager.GetBatchCandidates();
            if (candidates.Count == 0)
            {
                ShowBatchWarning("AnimReTarget.Batch.Error.NoCandidates");
                return;
            }

            var selectionItems = candidates.Select(animation =>
                new SelectionListViewModel<AnimationReference>.Item
                {
                    IsChecked = new NotifyAttr<bool>(
                        !SaveManager.IsTechAnimation(animation)),
                    DisplayName = animation.ToString(),
                    ItemValue = animation,
                }).ToList();
            var selectionWindow = new SelectionListWindow
            {
                Owner = this,
            };
            var selectionViewModel =
                new SelectionListViewModel<AnimationReference>
                {
                    WindowTitle = LocalizationManager.Instance.Get(
                        "AnimReTarget.Batch.SelectAnimationsTitle"),
                };
            foreach (var item in selectionItems)
                selectionViewModel.ItemList.Add(item);
            selectionWindow.SetDataContextAndFilterConfig(selectionViewModel);
            selectionWindow.ShowDialog();
            if (!selectionWindow.Result)
                return;

            var selectedAnimations = selectionItems
                .Where(item => item.IsChecked.Value)
                .Select(item => item.ItemValue)
                .ToList();
            if (selectedAnimations.Count == 0)
            {
                ShowBatchWarning("AnimReTarget.Batch.Error.NoSelection");
                return;
            }

            if (_settings.BatchUseSelectedFolder &&
                string.IsNullOrWhiteSpace(_settings.BatchTargetFolder))
            {
                ShowBatchWarning(
                    "AnimReTarget.Batch.Error.TargetFolderRequired");
                return;
            }

            var request = new BatchAnimationRetargetRequest(
                selectedAnimations,
                _settings.BatchUseSelectedFolder
                    ? BatchRetargetPathMode.SelectedFolder
                    : BatchRetargetPathMode.SourcePath,
                _settings.BatchTargetFolder,
                _settings.BatchOverwriteExisting);
            BatchAnimationRetargetResult? result = null;
            SetBatchOperationState(true);
            ResetProgress();
            _batchCancellation = new CancellationTokenSource();
            _batchProgress.CancelCommand = new RelayCommand(
                () => _batchCancellation?.Cancel());
            _batchProgress.IsOperationActive = true;
            try
            {
                var progress = new Progress<OperationProgressUpdate>(
                    ApplyProgress);
                result = await _saveManager.ExecuteBatchAsync(
                    request,
                    progress,
                    _batchCancellation.Token);
            }
            catch (Exception exception)
            {
                _standardDialogs.ShowExceptionWindow(
                    exception,
                    LocalizationManager.Instance.Get(
                        "AnimReTarget.Batch.Error.ExecutionFailed"));
            }
            finally
            {
                _batchCancellation.Dispose();
                _batchCancellation = null;
                await _batchProgress.CompleteAsync();
                SetBatchOperationState(false);
            }

            if (result != null)
                ShowBatchResult(result);
        }

        private void ShowBatchWarning(string localizationKey)
        {
            _standardDialogs.ShowDialogBox(
                LocalizationManager.Instance.Get(localizationKey),
                LocalizationManager.Instance.Get("Msg.GeneralError"),
                UiMessageBoxIcon.Warning);
        }

        private void ResetProgress()
        {
            _batchProgress.StatusText = LocalizationManager.Instance.Get(
                "AnimReTarget.Batch.Progress");
            _batchProgress.CurrentDetailText = string.Empty;
            _batchProgress.ProgressValue = 0;
            _batchProgress.ProgressMaximum = 1;
            _batchProgress.IsProgressIndeterminate = false;
        }

        private void ApplyProgress(OperationProgressUpdate progress)
        {
            _batchProgress.StatusText = progress.Status;
            _batchProgress.CurrentDetailText = progress.Detail ?? string.Empty;
            _batchProgress.ProgressMaximum = Math.Max(1, progress.Total);
            _batchProgress.ProgressValue = Math.Clamp(
                progress.Completed,
                0,
                _batchProgress.ProgressMaximum);
        }

        private void SetBatchOperationState(bool isRunning)
        {
            OptionsPanel.IsEnabled = !isRunning;
            SaveButton.IsEnabled = !isRunning;
            CloseButton.IsEnabled = !isRunning;
        }

        private void ShowBatchResult(BatchAnimationRetargetResult result)
        {
            var items = new ErrorList();
            foreach (var item in result.Items)
            {
                var path = item.OutputPath == null
                    ? item.SourcePath
                    : LocalizationManager.Instance.GetFormat(
                        "AnimReTarget.Batch.Result.Path",
                        item.SourcePath,
                        item.OutputPath);
                switch (item.Status)
                {
                    case BatchAnimationRetargetItemStatus.Success:
                        items.Ok(
                            path,
                            LocalizationManager.Instance.Get(
                                "AnimReTarget.Batch.Result.Success"));
                        break;
                    case BatchAnimationRetargetItemStatus.Skipped:
                        items.Warning(
                            path,
                            LocalizationManager.Instance.Get(
                                "AnimReTarget.Batch.Result.Skipped"));
                        break;
                    case BatchAnimationRetargetItemStatus.Failed:
                        items.Error(
                            path,
                            LocalizationManager.Instance.GetFormat(
                                "AnimReTarget.Batch.Result.Failed",
                                item.ErrorMessage ?? string.Empty));
                        break;
                    case BatchAnimationRetargetItemStatus.NotProcessed:
                        items.Warning(
                            path,
                            LocalizationManager.Instance.Get(
                                "AnimReTarget.Batch.Result.NotProcessed"));
                        break;
                }
            }

            var title = LocalizationManager.Instance.GetFormat(
                "AnimReTarget.Batch.Result.Title",
                result.SuccessCount,
                result.SkippedCount,
                result.FailureCount,
                result.NotProcessedCount);
            _standardDialogs.ShowErrorViewDialog(title, items);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_batchCancellation != null)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
