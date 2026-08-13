using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetEditor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditor.ViewModels;

public partial class FolderProjectHistoryViewModel : ObservableObject
{
    private const int MaxRestorePoints = 100;
    private readonly IFolderProjectHistoryService _historyService;
    private readonly IFolderProjectUnsavedChangesService
        _unsavedChangesService;
    private readonly IFolderProjectUnsavedChangesPrompt
        _unsavedChangesPrompt;
    private readonly IStandardDialogs _dialogs;
    private readonly LocalizationManager _localization;
    private readonly SynchronizationContext? _synchronizationContext;
    private FolderProjectContainer? _project;
    private int _selectedRestorePointRequestVersion;
    private int _activeOperationCount;
    private bool _isUpdatingSelectedRestorePoint;

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasUnrecordedChanges;
    [ObservableProperty] private bool _hasRestorePoints;
    [ObservableProperty] private bool _hasSelectedRestorePointChanges;
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _restorePointDescription = "";
    [ObservableProperty] private string _unrecordedSummaryText = "";
    [ObservableProperty] private string _historySummaryText = "";
    [ObservableProperty] private string _availabilityText = "";
    [ObservableProperty] private string _operationStatusText = "";
    [ObservableProperty] private string _operationDetailText = "";
    [ObservableProperty] private double _operationProgressValue;
    [ObservableProperty] private double _operationProgressMaximum = 1;
    [ObservableProperty] private bool _isOperationProgressIndeterminate = true;
    [ObservableProperty] private FolderProjectRestorePoint?
        _selectedRestorePoint;

    public ObservableCollection<FolderProjectUnrecordedChange>
        UnrecordedChanges { get; } = [];
    public ObservableCollection<FolderProjectRestorePoint>
        RestorePoints { get; } = [];
    public ObservableCollection<FolderProjectRestorePointChange>
        SelectedRestorePointChanges { get; } = [];
    public Task SelectedChangesLoadTask { get; private set; } =
        Task.CompletedTask;

    public FolderProjectHistoryViewModel(
        IFolderProjectHistoryService historyService,
        IFolderProjectUnsavedChangesService unsavedChangesService,
        IFolderProjectUnsavedChangesPrompt unsavedChangesPrompt,
        IStandardDialogs dialogs,
        LocalizationManager localization)
    {
        _historyService = historyService;
        _unsavedChangesService = unsavedChangesService;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _dialogs = dialogs;
        _localization = localization;
        _synchronizationContext = SynchronizationContext.Current;
        RestorePointDescription = _localization.Get(
            "FolderProject.History.DefaultDescription");
        ClearDisplayedState();
    }

    public void OpenProject(FolderProjectContainer? project)
    {
        _project = project;
        ProjectName = project?.ProjectSettings.Name ?? "";
        RestorePointDescription = _localization.Get(
            "FolderProject.History.DefaultDescription");
        ClearDisplayedState();
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        var project = _project;
        if (project == null)
            return;

        await RunOperation(
            () => LoadSnapshot(project.ProjectRoot),
            ApplySnapshot);
    }

    [RelayCommand(CanExecute = nameof(CanCreateRestorePoint))]
    private async Task CreateRestorePoint()
    {
        var project = _project;
        if (project == null)
            return;

        if (_unsavedChangesService.HasUnsavedChanges(
                project.ProjectRoot,
                null))
        {
            var choice = _unsavedChangesPrompt.Show(
                FolderProjectUnsavedChangesOperation.CreateRestorePoint);
            if (choice == FolderProjectUnsavedChangesChoice.Cancel)
                return;
            if (choice == FolderProjectUnsavedChangesChoice.Save &&
                !_unsavedChangesService.SaveUnsavedChanges(
                    project.ProjectRoot,
                    null))
            {
                return;
            }
        }

        var description = string.IsNullOrWhiteSpace(
            RestorePointDescription)
            ? _localization.Get(
                "FolderProject.History.DefaultDescription")
            : RestorePointDescription.Trim();
        await RunOperation(
            () =>
            {
                project.RefreshFromDisk();
                _historyService.CreateRestorePoint(
                    project.ProjectRoot,
                    description,
                    ReportProgress);
                return LoadSnapshot(project.ProjectRoot);
            },
            snapshot =>
            {
                ApplySnapshot(snapshot);
                RestorePointDescription = _localization.Get(
                    "FolderProject.History.DefaultDescription");
            });
    }

    partial void OnSelectedRestorePointChanged(
        FolderProjectRestorePoint? value)
    {
        if (_isUpdatingSelectedRestorePoint)
            return;

        SelectedRestorePointChanges.Clear();
        HasSelectedRestorePointChanges = false;
        var requestVersion = ++_selectedRestorePointRequestVersion;
        if (value == null ||
            _project == null)
        {
            SelectedChangesLoadTask = Task.CompletedTask;
            return;
        }

        SelectedChangesLoadTask = LoadSelectedRestorePointChanges(
            _project.ProjectRoot,
            value,
            requestVersion);
    }

    private async Task LoadSelectedRestorePointChanges(
        string projectRoot,
        FolderProjectRestorePoint restorePoint,
        int requestVersion)
    {
        BeginOperation();
        try
        {
            var changes = await Task.Run(
                () => _historyService.GetRestorePointChanges(
                projectRoot,
                restorePoint.Id,
                ReportProgress));
            if (requestVersion != _selectedRestorePointRequestVersion ||
                SelectedRestorePoint?.Id != restorePoint.Id)
            {
                return;
            }

            ReplaceCollection(SelectedRestorePointChanges, changes);
            HasSelectedRestorePointChanges = changes.Count != 0;
            var loadedRestorePoint = restorePoint with
            {
                ChangeSummary = Summarize(changes),
            };
            var index = RestorePoints.IndexOf(restorePoint);
            if (index >= 0)
            {
                _isUpdatingSelectedRestorePoint = true;
                try
                {
                    RestorePoints[index] = loadedRestorePoint;
                    SelectedRestorePoint = loadedRestorePoint;
                }
                finally
                {
                    _isUpdatingSelectedRestorePoint = false;
                }
            }
        }
        catch (FolderProjectHistoryException exception)
        {
            if (requestVersion == _selectedRestorePointRequestVersion)
                ShowHistoryError(exception);
        }
        catch (Exception exception)
        {
            if (requestVersion == _selectedRestorePointRequestVersion)
                ShowUnexpectedError(exception);
        }
        finally
        {
            EndOperation();
        }
    }

    private HistorySnapshot LoadSnapshot(string projectRoot)
    {
        var status = _historyService.GetStatus(
            projectRoot,
            ReportProgress);
        var restorePoints = status.Availability ==
                            FolderProjectHistoryAvailability.NotInitialized
            ? []
            : _historyService.GetRestorePoints(
                projectRoot,
                MaxRestorePoints,
                ReportProgress);
        return new HistorySnapshot(status, restorePoints);
    }

    private void ApplySnapshot(HistorySnapshot snapshot)
    {
        IsReady = snapshot.Status.Availability ==
                  FolderProjectHistoryAvailability.Ready;
        AvailabilityText = _localization.Get(
            $"FolderProject.History.Availability.{snapshot.Status.Availability}");
        ReplaceCollection(
            UnrecordedChanges,
            snapshot.Status.UnrecordedChanges);
        ReplaceCollection(RestorePoints, snapshot.RestorePoints);
        HasUnrecordedChanges = UnrecordedChanges.Count != 0;
        HasRestorePoints = RestorePoints.Count != 0;
        UnrecordedSummaryText = HasUnrecordedChanges
            ? _localization.GetFormat(
                "FolderProject.History.UnrecordedSummary",
                UnrecordedChanges.Count)
            : _localization.Get(
                "FolderProject.History.NoUnrecordedChanges");
        HistorySummaryText = HasRestorePoints
            ? _localization.GetFormat(
                "FolderProject.History.RestorePointSummary",
                RestorePoints.Count)
            : _localization.Get(
                "FolderProject.History.NoRestorePoints");
        SelectedRestorePoint = null;
        SelectedRestorePointChanges.Clear();
        HasSelectedRestorePointChanges = false;
        NotifyCommands();
    }

    private async Task RunOperation<T>(
        Func<T> operation,
        Action<T> applyResult)
    {
        BeginOperation();
        try
        {
            var result = await Task.Run(operation);
            applyResult(result);
        }
        catch (FolderProjectHistoryException exception)
        {
            ShowHistoryError(exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception);
        }
        finally
        {
            EndOperation();
        }
    }

    private void ReportProgress(FolderProjectHistoryProgress progress)
    {
        void Apply()
        {
            OperationStatusText = _localization.Get(
                $"FolderProject.History.Progress.{progress.Stage}");
            OperationDetailText = progress.Detail ?? "";
            OperationProgressValue = progress.Completed;
            OperationProgressMaximum = Math.Max(1, progress.Total);
            IsOperationProgressIndeterminate = progress.Total <= 0;
        }

        if (_synchronizationContext == null ||
            ReferenceEquals(
                _synchronizationContext,
                SynchronizationContext.Current))
        {
            Apply();
        }
        else
        {
            _synchronizationContext.Post(_ => Apply(), null);
        }
    }

    private void ClearDisplayedState()
    {
        IsReady = false;
        AvailabilityText = "";
        UnrecordedChanges.Clear();
        RestorePoints.Clear();
        SelectedRestorePointChanges.Clear();
        SelectedRestorePoint = null;
        HasUnrecordedChanges = false;
        HasRestorePoints = false;
        HasSelectedRestorePointChanges = false;
        UnrecordedSummaryText = _localization.Get(
            "FolderProject.History.NoUnrecordedChanges");
        HistorySummaryText = _localization.Get(
            "FolderProject.History.NoRestorePoints");
    }

    private bool CanRefresh() => _project != null && !IsBusy;

    private bool CanCreateRestorePoint() =>
        _project != null &&
        IsReady &&
        !IsBusy;

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateRestorePointCommand.NotifyCanExecuteChanged();
    }

    private void BeginOperation()
    {
        _activeOperationCount++;
        IsBusy = true;
        NotifyCommands();
    }

    private void EndOperation()
    {
        _activeOperationCount = Math.Max(0, _activeOperationCount - 1);
        IsBusy = _activeOperationCount != 0;
        NotifyCommands();
    }

    private void ShowHistoryError(FolderProjectHistoryException exception)
    {
        _dialogs.ShowDialogBox(
            _localization.Get(
                $"FolderProject.History.Error.{exception.Code}"),
            _localization.Get("FolderProject.History.Error.Title"));
    }

    private void ShowUnexpectedError(Exception exception)
    {
        _dialogs.ShowExceptionWindow(
            exception,
            _localization.Get("FolderProject.History.Error.Unexpected"));
    }

    private static FolderProjectRestorePointChangeSummary Summarize(
        IReadOnlyList<FolderProjectRestorePointChange> changes) =>
        new(
            changes.Count(change =>
                change.Kind == FolderProjectRestorePointChangeKind.Added),
            changes.Count(change =>
                change.Kind == FolderProjectRestorePointChangeKind.Modified),
            changes.Count(change =>
                change.Kind == FolderProjectRestorePointChangeKind.Deleted),
            changes.Count(change =>
                change.Kind == FolderProjectRestorePointChangeKind.Renamed),
            changes.Count(change =>
                change.Kind ==
                FolderProjectRestorePointChangeKind.TypeChanged));

    private static void ReplaceCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private sealed record HistorySnapshot(
        FolderProjectHistoryStatus Status,
        IReadOnlyList<FolderProjectRestorePoint> RestorePoints);
}
