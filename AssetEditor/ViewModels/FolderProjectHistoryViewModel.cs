using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetEditor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Events.Global;
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
    private readonly IFolderProjectGitOperationCoordinator _coordinator;
    private readonly IStandardDialogs _dialogs;
    private readonly LocalizationManager _localization;
    private readonly IGlobalEventHub? _eventHub;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly Dictionary<string,
        IReadOnlyList<FolderProjectRestorePointChange>>
        _restorePointChangesCache = new(StringComparer.Ordinal);
    private FolderProjectContainer? _project;
    private string? _projectRoot;
    private bool _closeAfterRecovery;
    private string? _currentRestorePointId;
    private int _selectedRestorePointRequestVersion;
    private int _activeOperationCount;

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasUnrecordedChanges;
    [ObservableProperty] private bool _hasRestorePoints;
    [ObservableProperty] private bool _hasSelectedRestorePointChanges;
    [ObservableProperty] private bool _isRecoveryRequired;
    [ObservableProperty] private bool _canRecover;
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _loadedProjectText = "";
    [ObservableProperty] private string _restorePointDescription = "";
    [ObservableProperty] private string _unrecordedSummaryText = "";
    [ObservableProperty] private string _historySummaryText = "";
    [ObservableProperty] private string _availabilityText = "";
    [ObservableProperty] private string _recoveryText = "";
    [ObservableProperty] private string _operationStatusText = "";
    [ObservableProperty] private string _operationDetailText = "";
    [ObservableProperty] private double _operationProgressValue;
    [ObservableProperty] private double _operationProgressMaximum = 1;
    [ObservableProperty] private bool _isOperationProgressIndeterminate = true;
    [ObservableProperty] private FolderProjectRestorePoint?
        _selectedRestorePoint;
    [ObservableProperty] private FolderProjectRestorePointChange?
        _selectedRestorePointChange;
    [ObservableProperty] private FolderProjectUnrecordedChange?
        _selectedUnrecordedChange;

    public ObservableCollection<FolderProjectUnrecordedChange>
        UnrecordedChanges { get; } = [];
    public ObservableCollection<FolderProjectRestorePoint>
        RestorePoints { get; } = [];
    public ObservableCollection<FolderProjectRestorePointChange>
        SelectedRestorePointChanges { get; } = [];
    public Task SelectedChangesLoadTask { get; private set; } =
        Task.CompletedTask;
    public event EventHandler? RecoveryCompleted;

    public FolderProjectHistoryViewModel(
        IFolderProjectHistoryService historyService,
        IFolderProjectUnsavedChangesService unsavedChangesService,
        IFolderProjectUnsavedChangesPrompt unsavedChangesPrompt,
        IFolderProjectGitOperationCoordinator coordinator,
        IStandardDialogs dialogs,
        LocalizationManager localization,
        IGlobalEventHub? eventHub = null)
    {
        _historyService = historyService;
        _unsavedChangesService = unsavedChangesService;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _coordinator = coordinator;
        _dialogs = dialogs;
        _localization = localization;
        _eventHub = eventHub;
        _synchronizationContext = SynchronizationContext.Current;
        LoadedProjectText = _localization.GetFormat(
            "FolderProject.History.CurrentProject",
            "");
        ClearDisplayedState();
    }

    public void OpenProject(FolderProjectContainer? project)
    {
        _project = project;
        var projectRoot = project?.ProjectRoot;
        if (!string.Equals(
                _projectRoot,
                projectRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            _restorePointChangesCache.Clear();
        }
        _projectRoot = projectRoot;
        _closeAfterRecovery = false;
        ProjectName = project?.ProjectSettings.Name ?? "";
        LoadedProjectText = _localization.GetFormat(
            "FolderProject.History.CurrentProject",
            ProjectName);
        RestorePointDescription = "";
        ClearDisplayedState();
        NotifyCommands();
    }

    public void OpenRecoveryProject(
        string projectRoot,
        string projectName)
    {
        _project = null;
        var normalizedProjectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        if (!string.Equals(
                _projectRoot,
                normalizedProjectRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            _restorePointChangesCache.Clear();
        }
        _projectRoot = normalizedProjectRoot;
        _closeAfterRecovery = true;
        ProjectName = projectName;
        LoadedProjectText = _localization.GetFormat(
            "FolderProject.History.CurrentProject",
            ProjectName);
        RestorePointDescription = "";
        ClearDisplayedState();
        IsRecoveryRequired = true;
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        var projectRoot = _projectRoot;
        if (projectRoot == null)
            return;

        await RunOperation(
            () => LoadSnapshot(projectRoot, validateUnrecordedChanges: true),
            ApplySnapshot);
    }

    [RelayCommand(CanExecute = nameof(CanRecoverHistory))]
    private async Task RecoverHistory()
    {
        var projectRoot = _projectRoot;
        if (projectRoot == null || !Confirm("RecoverHistory"))
            return;

        await RunOperation(
            async () =>
            {
                await _coordinator.ExecuteTransactionalAsync(
                    projectRoot,
                    () => _historyService.BeginRecoverToSafeState(
                        projectRoot,
                        ReportProgress),
                    _historyService.CompleteRecoverToSafeState,
                    _historyService.RollbackRecoverToSafeState,
                    openWhenComplete: true);
                return LoadSnapshot(projectRoot);
            },
            snapshot =>
            {
                ApplySnapshot(snapshot);
                if (_closeAfterRecovery)
                    RecoveryCompleted?.Invoke(this, EventArgs.Empty);
            });
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
            var choice = _unsavedChangesPrompt.Show();
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
            () => CreateRestorePointSnapshot(project, description),
            snapshot =>
            {
                if (snapshot == null)
                    return;

                ApplySnapshot(snapshot);
                _eventHub?.PublishGlobalEvent(
                    new FolderProjectRestorePointCreatedEvent(project));
                RestorePointDescription = "";
            });
    }

    [RelayCommand(CanExecute = nameof(CanRestoreProject))]
    private async Task RestoreProject()
    {
        var project = _project;
        var restorePoint = SelectedRestorePoint;
        if (project == null || restorePoint == null)
            return;
        int impactCount;
        try
        {
            impactCount = await Task.Run(() =>
                _historyService.GetRestoreImpactCount(
                    project.ProjectRoot,
                    restorePoint.Id));
        }
        catch (FolderProjectHistoryException exception)
        {
            ShowHistoryError(exception);
            return;
        }
        if (!Confirm(
                "RestoreProject",
                restorePoint.Description,
                impactCount))
        {
            return;
        }

        await RunOperation(
            async () =>
            {
                HistorySnapshot? snapshot = null;
                ReportProgress(new FolderProjectHistoryProgress(
                    FolderProjectHistoryProgressStage.PreparingEditors,
                    project.ProjectRoot));
                await _coordinator.ExecuteTransactionalAsync(
                    project.ProjectRoot,
                    () =>
                    {
                        var result = _historyService.RestoreProject(
                            project.ProjectRoot,
                            restorePoint,
                            ReportProgress);
                        ReportProgress(new FolderProjectHistoryProgress(
                            FolderProjectHistoryProgressStage
                                .ReconcilingProject,
                            project.ProjectRoot));
                        return result;
                    },
                    _ =>
                    {
                        ReportProgress(new FolderProjectHistoryProgress(
                            FolderProjectHistoryProgressStage
                                .RefreshingInterface,
                            project.ProjectRoot));
                        snapshot = LoadSnapshot(project.ProjectRoot);
                    },
                    result => _historyService.RollbackProjectRestore(
                        project.ProjectRoot,
                        result));
                return snapshot!;
            },
            ApplySnapshot);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRestorePoint))]
    private async Task DeleteRestorePoint()
    {
        var project = _project;
        var restorePoint = SelectedRestorePoint;
        if (project == null || restorePoint == null || restorePoint.IsInitial)
            return;
        if (!Confirm("DeleteRestorePoint", restorePoint.Description))
            return;

        await RunOperation(
            async () =>
            {
                HistorySnapshot? snapshot = null;
                await _coordinator.ExecuteInPlaceTransactionalAsync(
                    project.ProjectRoot,
                    () => _historyService.BeginDeleteRestorePoint(
                        project.ProjectRoot,
                        restorePoint.Id,
                        ReportProgress),
                    operation =>
                    {
                        _restorePointChangesCache.Clear();
                        ReportProgress(new FolderProjectHistoryProgress(
                            FolderProjectHistoryProgressStage
                                .RefreshingInterface,
                            project.ProjectRoot));
                        snapshot = LoadSnapshot(project.ProjectRoot);
                        _historyService.CompleteDeleteRestorePoint(operation);
                    },
                    operation => _historyService.RollbackDeleteRestorePoint(
                        project.ProjectRoot,
                        operation));
                return snapshot!;
            },
            ApplySnapshot);
    }

    [RelayCommand(CanExecute = nameof(CanRestoreFile))]
    private async Task RestoreFile()
    {
        var project = _project;
        var restorePoint = SelectedRestorePoint;
        var change = SelectedRestorePointChange;
        if (project == null || restorePoint == null || change == null)
            return;
        if (!Confirm("RestoreFile", change.Path))
            return;

        var overwrite = HasUnrecordedChange(change.Path);
        if (overwrite && !Confirm("RestoreFileOverwrite", change.Path))
            return;

        await RunOperation(
            () => ExecuteWorkspaceFileOperation(
                project,
                () =>
                {
                    ReportProgress(new FolderProjectHistoryProgress(
                        FolderProjectHistoryProgressStage.WritingProjectFiles,
                        change.Path,
                        0,
                        1));
                    var operation = _historyService.BeginRestoreFile(
                        project.ProjectRoot,
                        change.Kind == FolderProjectRestorePointChangeKind.Deleted
                            ? restorePoint.PreviousRestorePointId!
                            : restorePoint.Id,
                        change.Path,
                        overwrite);
                    ReportProgress(new FolderProjectHistoryProgress(
                        FolderProjectHistoryProgressStage.WritingProjectFiles,
                        change.Path,
                        1,
                        1));
                    return operation;
                },
                _historyService.CompleteRestoreFile,
                _historyService.RollbackRestoreFile),
            ApplySnapshot);
    }

    [RelayCommand(CanExecute = nameof(CanDiscardSelected))]
    private async Task DiscardSelected()
    {
        var project = _project;
        var change = SelectedUnrecordedChange;
        if (project == null || change == null)
            return;
        if (!Confirm("DiscardSelected", change.Path))
            return;

        await RunOperation(
            () => ExecuteWorkspaceFileOperation(
                project,
                () => _historyService.BeginDiscardChanges(
                    project.ProjectRoot,
                    [change.Path],
                    ReportProgress),
                _historyService.CompleteDiscardChanges,
                operation => _historyService.RollbackDiscardChanges(
                    project.ProjectRoot,
                    operation)),
            ApplySnapshot);
    }

    [RelayCommand(CanExecute = nameof(CanDiscardAll))]
    private async Task DiscardAll()
    {
        var project = _project;
        if (project == null || UnrecordedChanges.Count == 0)
            return;
        FolderProjectHistoryStatus status;
        BeginOperation();
        try
        {
            status = await Task.Run(() => _historyService.GetStatus(
                project.ProjectRoot,
                ReportProgress));
        }
        catch (FolderProjectHistoryException exception)
        {
            ShowHistoryError(exception);
            return;
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception);
            return;
        }
        finally
        {
            EndOperation();
        }

        var paths = status.UnrecordedChanges
            .Select(change => change.Path)
            .ToArray();
        if (paths.Length == 0)
        {
            await RefreshCommand.ExecuteAsync(null);
            return;
        }
        if (!Confirm("DiscardAll", paths.Length))
            return;

        await RunOperation(
            async () =>
            {
                HistorySnapshot? snapshot = null;
                ReportProgress(new FolderProjectHistoryProgress(
                    FolderProjectHistoryProgressStage.PreparingEditors));
                await _coordinator.ExecuteTransactionalAsync(
                    project.ProjectRoot,
                    () =>
                    {
                        var result = _historyService.BeginDiscardChanges(
                            project.ProjectRoot,
                            paths,
                            ReportProgress);
                        ReportProgress(new FolderProjectHistoryProgress(
                            FolderProjectHistoryProgressStage
                                .ReconcilingProject));
                        return result;
                    },
                    result =>
                    {
                        ReportProgress(new FolderProjectHistoryProgress(
                            FolderProjectHistoryProgressStage
                                .RefreshingInterface));
                        snapshot = LoadSnapshot(project.ProjectRoot);
                        _historyService.CompleteDiscardChanges(result);
                    },
                    result => _historyService.RollbackDiscardChanges(
                        project.ProjectRoot,
                        result));
                return snapshot!;
            },
            ApplySnapshot);
    }

    private async Task<HistorySnapshot> ExecuteWorkspaceFileOperation<T>(
        FolderProjectContainer project,
        Func<T> operation,
        Action<T> complete,
        Action<T> rollback)
    {
        HistorySnapshot? snapshot = null;
        await _coordinator.ExecuteInPlaceTransactionalAsync(
            project.ProjectRoot,
            () =>
            {
                var result = operation();
                ReportProgress(new FolderProjectHistoryProgress(
                    FolderProjectHistoryProgressStage.ReconcilingProject,
                    project.ProjectRoot));
                return result;
            },
            result =>
            {
                project.RefreshFromDisk();
                ReportProgress(new FolderProjectHistoryProgress(
                    FolderProjectHistoryProgressStage.RefreshingInterface,
                    project.ProjectRoot));
                snapshot = LoadSnapshot(project.ProjectRoot);
                complete(result);
            },
            rollback);
        return snapshot!;
    }

    partial void OnSelectedRestorePointChanged(
        FolderProjectRestorePoint? value)
    {
        RestoreProjectCommand.NotifyCanExecuteChanged();
        DeleteRestorePointCommand.NotifyCanExecuteChanged();
        SelectedRestorePointChanges.Clear();
        HasSelectedRestorePointChanges = false;
        SelectedRestorePointChange = null;
        var requestVersion = ++_selectedRestorePointRequestVersion;
        if (value == null ||
            _projectRoot == null)
        {
            SelectedChangesLoadTask = Task.CompletedTask;
            return;
        }

        if (_restorePointChangesCache.TryGetValue(
                value.Id,
                out var cachedChanges))
        {
            ReplaceCollection(
                SelectedRestorePointChanges,
                cachedChanges);
            HasSelectedRestorePointChanges = cachedChanges.Count != 0;
            SelectedChangesLoadTask = Task.CompletedTask;
            return;
        }

        SelectedChangesLoadTask = LoadSelectedRestorePointChanges(
            _projectRoot,
            value,
            requestVersion);
    }

    partial void OnSelectedRestorePointChangeChanged(
        FolderProjectRestorePointChange? value) =>
        RestoreFileCommand.NotifyCanExecuteChanged();

    partial void OnSelectedUnrecordedChangeChanged(
        FolderProjectUnrecordedChange? value) =>
        DiscardSelectedCommand.NotifyCanExecuteChanged();

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
            _restorePointChangesCache[restorePoint.Id] = changes;
            if (requestVersion != _selectedRestorePointRequestVersion ||
                SelectedRestorePoint?.Id != restorePoint.Id)
            {
                return;
            }

            ReplaceCollection(SelectedRestorePointChanges, changes);
            HasSelectedRestorePointChanges = changes.Count != 0;
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

    private HistorySnapshot LoadSnapshot(
        string projectRoot,
        bool validateUnrecordedChanges = false)
    {
        var status = validateUnrecordedChanges
            ? _historyService.GetStatus(projectRoot, ReportProgress)
            : _historyService.GetDisplayStatus(projectRoot);
        var restorePoints = status.Availability ==
                            FolderProjectHistoryAvailability.NotInitialized
            ? []
            : _historyService.GetRestorePoints(
                projectRoot,
                MaxRestorePoints,
                ReportProgress);
        return new HistorySnapshot(status, restorePoints);
    }

    private HistorySnapshot? CreateRestorePointSnapshot(
        FolderProjectContainer project,
        string description)
    {
        if (!ReferenceEquals(_project, project))
            return null;

        try
        {
            project.RefreshFromDisk();
        }
        catch (ObjectDisposedException exception) when (
            string.Equals(
                exception.ObjectName,
                typeof(FolderProjectContainer).FullName,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (!ReferenceEquals(_project, project))
            return null;

        _historyService.CreateRestorePoint(
            project.ProjectRoot,
            description,
            ReportProgress);
        return LoadSnapshot(project.ProjectRoot);
    }

    private void ApplySnapshot(HistorySnapshot snapshot)
    {
        IsReady = snapshot.Status.Availability ==
                  FolderProjectHistoryAvailability.Ready;
        IsRecoveryRequired = snapshot.Status.Availability ==
                             FolderProjectHistoryAvailability
                                 .RecoveryRequired;
        CanRecover = snapshot.Status.CanRecover;
        _currentRestorePointId = snapshot.Status.CurrentRestorePointId;
        AvailabilityText = _localization.Get(
            $"FolderProject.History.Availability.{snapshot.Status.Availability}");
        RecoveryText = IsRecoveryRequired
            ? _localization.Get(
                $"FolderProject.History.Recovery.{snapshot.Status.RecoveryReason}")
            : "";
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

    private async Task RunOperation<T>(
        Func<Task<T>> operation,
        Action<T> applyResult)
    {
        BeginOperation();
        try
        {
            var result = await operation();
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
        IsRecoveryRequired = false;
        CanRecover = false;
        _currentRestorePointId = null;
        AvailabilityText = "";
        RecoveryText = "";
        UnrecordedChanges.Clear();
        RestorePoints.Clear();
        SelectedRestorePointChanges.Clear();
        SelectedRestorePoint = null;
        SelectedRestorePointChange = null;
        SelectedUnrecordedChange = null;
        HasUnrecordedChanges = false;
        HasRestorePoints = false;
        HasSelectedRestorePointChanges = false;
        UnrecordedSummaryText = _localization.Get(
            "FolderProject.History.NoUnrecordedChanges");
        HistorySummaryText = _localization.Get(
            "FolderProject.History.NoRestorePoints");
    }

    private bool CanRefresh() => _projectRoot != null && !IsBusy;

    private bool CanRecoverHistory() =>
        _projectRoot != null && CanRecover && !IsBusy;

    private bool CanCreateRestorePoint() =>
        _project != null &&
        IsReady &&
        !IsBusy;

    private bool CanRestoreProject() =>
        _project != null && IsReady && !IsBusy &&
        SelectedRestorePoint != null &&
        SelectedRestorePoint.Id != _currentRestorePointId;

    private bool CanDeleteRestorePoint() =>
        _project != null && IsReady && !IsBusy &&
        SelectedRestorePoint is { IsInitial: false };

    private bool CanRestoreFile() =>
        _project != null && IsReady && !IsBusy &&
        SelectedRestorePoint != null &&
        SelectedRestorePointChange != null &&
        (SelectedRestorePointChange.Kind !=
             FolderProjectRestorePointChangeKind.Deleted ||
         SelectedRestorePoint.PreviousRestorePointId != null);

    private bool CanDiscardSelected() =>
        _project != null && IsReady && !IsBusy &&
        SelectedUnrecordedChange != null;

    private bool CanDiscardAll() =>
        _project != null && IsReady && !IsBusy &&
        UnrecordedChanges.Count != 0;

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RecoverHistoryCommand.NotifyCanExecuteChanged();
        CreateRestorePointCommand.NotifyCanExecuteChanged();
        RestoreProjectCommand.NotifyCanExecuteChanged();
        DeleteRestorePointCommand.NotifyCanExecuteChanged();
        RestoreFileCommand.NotifyCanExecuteChanged();
        DiscardSelectedCommand.NotifyCanExecuteChanged();
        DiscardAllCommand.NotifyCanExecuteChanged();
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

    private bool Confirm(string operation, params object[] arguments) =>
        _dialogs.ShowYesNoBox(
            _localization.GetFormat(
                $"FolderProject.History.Confirm.{operation}",
                arguments),
            _localization.Get("FolderProject.History.Confirm.Title")) ==
        ShowMessageBoxResult.OK;

    private bool HasUnrecordedChange(string path) =>
        UnrecordedChanges.Any(change =>
            change.Path.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            change.PreviousPath?.Equals(
                path,
                StringComparison.OrdinalIgnoreCase) == true);

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
