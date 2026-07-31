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
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditor.ViewModels;

public sealed class FolderProjectWorkingChangeRow
{
    public FolderProjectWorkingChange Source { get; }
    public string RepositoryPath => Source.RepositoryPath;
    public string KindText { get; }

    public FolderProjectWorkingChangeRow(
        FolderProjectWorkingChange source,
        LocalizationManager localization)
    {
        Source = source;
        KindText = string.Join(
            "、",
            Enum.GetValues<FolderProjectWorkingChangeKind>()
                .Where(
                    kind =>
                        kind != FolderProjectWorkingChangeKind.None &&
                        source.Kind.HasFlag(kind))
                .Select(
                    kind => localization.Get(
                        $"FolderProject.VersionControl.Change.{kind}")));
    }
}

public sealed class FolderProjectCommitChangeRow
{
    public FolderProjectCommitChange Source { get; }
    public string RepositoryPath => Source.RepositoryPath;
    public string PreviousRepositoryPath =>
        Source.PreviousRepositoryPath ?? "";
    public string KindText { get; }
    public string BinaryText { get; }

    public FolderProjectCommitChangeRow(
        FolderProjectCommitChange source,
        LocalizationManager localization)
    {
        Source = source;
        KindText = localization.Get(
            $"FolderProject.VersionControl.CommitChange.{source.Kind}");
        BinaryText = localization.Get(
            source.IsBinary
                ? "FolderProject.VersionControl.Binary.Yes"
                : "FolderProject.VersionControl.Binary.No");
    }
}

public sealed class FolderProjectMergeConflictRow
{
    public FolderProjectMergeConflict Source { get; }
    public string Id => Source.Id;
    public string DisplayPath { get; }
    public string AncestorText { get; }
    public string CurrentText { get; }
    public string IncomingText { get; }

    public FolderProjectMergeConflictRow(
        FolderProjectMergeConflict source,
        LocalizationManager localization)
    {
        Source = source;
        DisplayPath =
            source.Current?.RepositoryPath ??
            source.Incoming?.RepositoryPath ??
            source.Ancestor?.RepositoryPath ??
            localization.Get(
                "FolderProject.VersionControl.Merge.UnknownPath");
        AncestorText = FormatSide(source.Ancestor, localization);
        CurrentText = FormatSide(source.Current, localization);
        IncomingText = FormatSide(source.Incoming, localization);
    }

    private static string FormatSide(
        FolderProjectMergeSide? side,
        LocalizationManager localization)
    {
        if (side == null)
        {
            return localization.Get(
                "FolderProject.VersionControl.Merge.SideMissing");
        }

        return localization.GetFormat(
            "FolderProject.VersionControl.Merge.SideFormat",
            side.RepositoryPath,
            side.Size,
            localization.Get(
                side.IsBinary
                    ? "FolderProject.VersionControl.Binary.Yes"
                    : "FolderProject.VersionControl.Binary.No"));
    }
}

public partial class FolderProjectVersionControlViewModel :
    ObservableObject
{
    private readonly IFolderProjectVersionControlService
        _versionControlService;
    private readonly IFolderProjectGitOperationCoordinator _coordinator;
    private readonly IStandardDialogs _dialogs;
    private readonly LocalizationManager _localization;
    private readonly string _defaultIdentityName;
    private readonly string _defaultIdentityEmail;
    private readonly ILogger _logger =
        Logging.Create<FolderProjectVersionControlViewModel>();
    private bool _refreshing;
    private bool _mergeStateKnown;
    private int _commitChangesRequestId;
    private int _historyRequestId;

    [ObservableProperty] private string _projectRoot = "";
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private bool _openWhenComplete;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isInitialized;
    [ObservableProperty] private string _currentBranch = "";
    [ObservableProperty] private string _headCommitId = "";
    [ObservableProperty] private bool _isDetached;
    [ObservableProperty]
    private FolderProjectRepositoryOperationState _operationState;
    [ObservableProperty] private bool _isClean = true;
    [ObservableProperty] private bool _hasIdentity;
    [ObservableProperty] private string _identityName = "";
    [ObservableProperty] private string _identityEmail = "";
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private string _branchName = "";
    [ObservableProperty] private string _recoveryBranchName = "";
    [ObservableProperty] private string _mergeMessage = "";
    [ObservableProperty]
    private FolderProjectMergePhase _mergePhase =
        FolderProjectMergePhase.None;
    [ObservableProperty] private string _mergeSummary = "";
    [ObservableProperty]
    private FolderProjectCommitSummary? _selectedCommit;
    [ObservableProperty]
    private FolderProjectCommitChangeRow? _selectedCommitChange;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedHistoryBranch;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedBranch;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedMergeSource;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedMergeTarget;
    [ObservableProperty]
    private FolderProjectMergeConflictRow? _selectedMergeConflict;

    public ObservableCollection<FolderProjectWorkingChangeRow>
        WorkingChanges
    { get; } = [];
    public ObservableCollection<FolderProjectCommitSummary> History { get; } =
        [];
    public ObservableCollection<FolderProjectCommitChangeRow> CommitChanges
    {
        get;
    } = [];
    public ObservableCollection<FolderProjectBranchInfo> Branches { get; } =
        [];
    public ObservableCollection<FolderProjectBranchInfo> MergeSources
    {
        get;
    } = [];
    public ObservableCollection<FolderProjectBranchInfo> MergeTargets
    {
        get;
    } = [];
    public ObservableCollection<FolderProjectMergeConflictRow> MergeConflicts
    {
        get;
    } = [];
    public Task CommitChangesLoadTask { get; private set; } =
        Task.CompletedTask;
    public Task HistoryLoadTask { get; private set; } =
        Task.CompletedTask;

    public bool HasActiveMerge =>
        MergePhase != FolderProjectMergePhase.None;
    public bool IsRecoveryRequired =>
        MergePhase == FolderProjectMergePhase.RecoveryRequired;
    public bool CanSelectMergeSource =>
        MergePhase == FolderProjectMergePhase.None;
    public string HistoryBranchHint =>
        SelectedHistoryBranch == null
            ? _localization.Get(
                "FolderProject.VersionControl.HistoryBranchHint.Select")
            : SelectedHistoryBranch.IsCurrent
                ? _localization.GetFormat(
                    "FolderProject.VersionControl.HistoryBranchHint.Current",
                    SelectedHistoryBranch.Name)
                : _localization.GetFormat(
                    "FolderProject.VersionControl.HistoryBranchHint.ReadOnly",
                    SelectedHistoryBranch.Name,
                    CurrentBranch);
    public string BranchActionHint
    {
        get
        {
            if (SelectedBranch == null)
            {
                return _localization.Get(
                    "FolderProject.VersionControl.BranchHint.Select");
            }

            if (!IsClean)
            {
                return _localization.Get(
                    "FolderProject.VersionControl.BranchHint.Dirty");
            }

            if (SelectedBranch.IsCurrent)
            {
                var target = GetDefaultMergeTarget(SelectedBranch);
                return target == null
                    ? _localization.Get(
                        "FolderProject.VersionControl.BranchHint.NoTarget")
                    : _localization.GetFormat(
                        "FolderProject.VersionControl.BranchHint.Current",
                        target.Name);
            }

            return _localization.GetFormat(
                "FolderProject.VersionControl.BranchHint.Selected",
                SelectedBranch.Name,
                CurrentBranch);
        }
    }

    public FolderProjectVersionControlViewModel(
        IFolderProjectVersionControlService versionControlService,
        IFolderProjectGitOperationCoordinator coordinator,
        IStandardDialogs dialogs,
        LocalizationManager localization)
    {
        _versionControlService = versionControlService;
        _coordinator = coordinator;
        _dialogs = dialogs;
        _localization = localization;
        _defaultIdentityName = _localization.Get(
            "FolderProject.VersionControl.DefaultIdentityName");
        _defaultIdentityEmail = _localization.Get(
            "FolderProject.VersionControl.DefaultIdentityEmail");
        IdentityName = _defaultIdentityName;
        IdentityEmail = _defaultIdentityEmail;
    }

    public void OpenProject(
        string projectRoot,
        string projectName,
        bool openWhenComplete)
    {
        ProjectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        ProjectName = projectName;
        OpenWhenComplete = openWhenComplete;
        _mergeStateKnown = false;
        RefreshCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task Refresh()
    {
        await RunOperationAsync(
            async () =>
            {
                await RefreshCoreAsync();
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Refreshed");
            },
            "FolderProject.VersionControl.Busy.Refreshing",
            RefreshMode.None);
    }

    [RelayCommand(CanExecute = nameof(CanInitialize))]
    private async Task Initialize()
    {
        if (!Confirm("Initialize"))
            return;

        var identity = CurrentIdentity();
        IdentityName = identity.Name;
        IdentityEmail = identity.Email;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.Initialize(
                        ProjectRoot,
                        identity));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Initialized");
            },
            "FolderProject.VersionControl.Busy.Initializing");
    }

    [RelayCommand(CanExecute = nameof(CanSaveIdentity))]
    private async Task SaveIdentity()
    {
        var identity = CurrentIdentity();
        IdentityName = identity.Name;
        IdentityEmail = identity.Email;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.SetIdentity(
                        ProjectRoot,
                        identity));
                HasIdentity = true;
                return _localization.Get(
                    "FolderProject.VersionControl.Status.IdentitySaved");
            },
            "FolderProject.VersionControl.Busy.SavingIdentity",
            RefreshMode.None);
    }

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task Commit()
    {
        var message = CommitMessage;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.CommitAll(
                        ProjectRoot,
                        message));
                CommitMessage = "";
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Committed");
            },
            "FolderProject.VersionControl.Busy.Committing");
    }

    [RelayCommand(CanExecute = nameof(CanRestoreFile))]
    private async Task RestoreFile()
    {
        if (!Confirm("Restore"))
            return;

        var commit = SelectedCommit!;
        var change = SelectedCommitChange!.Source;
        await RunOperationAsync(
            async () =>
            {
                try
                {
                    await ExecuteCoordinatedAsync(
                        () => _versionControlService.RestoreFile(
                            ProjectRoot,
                            commit.Id,
                            change.RepositoryPath,
                            false));
                }
                catch (FolderProjectVersionControlException exception)
                    when (exception.Code ==
                          FolderProjectVersionControlError
                              .WorkingTreeNotClean)
                {
                    if (!Confirm("RestoreOverwrite"))
                    {
                        return _localization.Get(
                            "FolderProject.VersionControl.Status.Cancelled");
                    }

                    await ExecuteCoordinatedAsync(
                        () => _versionControlService.RestoreFile(
                            ProjectRoot,
                            commit.Id,
                            change.RepositoryPath,
                            true));
                }

                return _localization.Get(
                    "FolderProject.VersionControl.Status.Restored");
            },
            "FolderProject.VersionControl.Busy.Restoring");
    }

    [RelayCommand(CanExecute = nameof(CanCreateRecoveryBranch))]
    private async Task CreateRecoveryBranch()
    {
        var branchName = RecoveryBranchName;
        var commitId = SelectedCommit!.Id;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.CreateRecoveryBranch(
                        ProjectRoot,
                        branchName,
                        commitId));
                RecoveryBranchName = "";
                return _localization.Get(
                    "FolderProject.VersionControl.Status.RecoveryBranchCreated");
            },
            "FolderProject.VersionControl.Busy.UpdatingBranches",
            RefreshMode.Branches);
    }

    [RelayCommand(CanExecute = nameof(CanCreateBranch))]
    private async Task CreateBranch()
    {
        var branchName = BranchName;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.CreateBranch(
                        ProjectRoot,
                        branchName));
                BranchName = "";
                return _localization.Get(
                    "FolderProject.VersionControl.Status.BranchCreated");
            },
            "FolderProject.VersionControl.Busy.UpdatingBranches",
            RefreshMode.Branches);
    }

    [RelayCommand(CanExecute = nameof(CanRenameBranch))]
    private async Task RenameBranch()
    {
        var oldName = SelectedBranch!.Name;
        var newName = BranchName;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.RenameBranch(
                        ProjectRoot,
                        oldName,
                        newName));
                BranchName = "";
                return _localization.Get(
                    "FolderProject.VersionControl.Status.BranchRenamed");
            },
            "FolderProject.VersionControl.Busy.UpdatingBranches",
            RefreshMode.Branches);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteBranch))]
    private async Task DeleteBranch()
    {
        if (!Confirm("DeleteBranch"))
            return;

        var branchName = SelectedBranch!.Name;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.DeleteBranch(
                        ProjectRoot,
                        branchName));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.BranchDeleted");
            },
            "FolderProject.VersionControl.Busy.UpdatingBranches",
            RefreshMode.Branches);
    }

    [RelayCommand(CanExecute = nameof(CanSwitchBranch))]
    private async Task SwitchBranch()
    {
        if (!Confirm("SwitchBranch"))
            return;

        var branchName = SelectedBranch!.Name;
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.SwitchBranch(
                        ProjectRoot,
                        branchName));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.BranchSwitched");
            },
            "FolderProject.VersionControl.Busy.SwitchingBranch");
    }

    [RelayCommand(CanExecute = nameof(CanPrepareMerge))]
    private void PrepareMerge()
    {
        SelectedMergeSource = MergeSources.FirstOrDefault(
            branch => branch.Name == SelectedBranch!.Name);
        SelectedMergeTarget = GetDefaultMergeTarget(
            SelectedMergeSource);
        SelectedTabIndex = 3;
    }

    [RelayCommand(CanExecute = nameof(CanBeginMerge))]
    private async Task BeginMerge()
    {
        var sourceBranch = SelectedMergeSource!.Name;
        var targetBranch = SelectedMergeTarget!.Name;
        var targetTipCommitId = SelectedMergeTarget.TipCommitId;
        if (!Confirm("BeginMerge", sourceBranch, targetBranch))
            return;

        await RunOperationAsync(
            async () =>
            {
                var result = await ExecuteCoordinatedAsync(
                    () => BeginMergeIntoTarget(
                        sourceBranch,
                        targetBranch,
                        targetTipCommitId));
                return _localization.Get(
                    $"FolderProject.VersionControl.Merge.Outcome.{result.Outcome}");
            },
            "FolderProject.VersionControl.Busy.Merging");
    }

    private FolderProjectMergeStartResult BeginMergeIntoTarget(
        string sourceBranch,
        string targetBranch,
        string targetTipCommitId)
    {
        var status = _versionControlService.GetStatus(ProjectRoot);
        var originalBranch = status.CurrentBranch;
        var switched = false;
        try
        {
            if (!string.Equals(
                    originalBranch,
                    targetBranch,
                    StringComparison.Ordinal))
            {
                _versionControlService.SwitchBranch(
                    ProjectRoot,
                    targetBranch);
                switched = true;
            }

            return _versionControlService.BeginMerge(
                ProjectRoot,
                sourceBranch);
        }
        catch (Exception mergeException)
        {
            if (!switched ||
                string.IsNullOrWhiteSpace(originalBranch) ||
                !CanRestoreOriginalBranch(
                    targetBranch,
                    targetTipCommitId))
            {
                throw;
            }

            try
            {
                _versionControlService.SwitchBranch(
                    ProjectRoot,
                    originalBranch);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The merge failed and the original branch could not be restored.",
                    mergeException,
                    rollbackException);
            }

            throw;
        }
    }

    private bool CanRestoreOriginalBranch(
        string targetBranch,
        string targetTipCommitId)
    {
        try
        {
            if (_versionControlService
                    .GetMergeState(ProjectRoot)
                    .Phase != FolderProjectMergePhase.None)
            {
                return false;
            }

            return _versionControlService
                .GetBranches(ProjectRoot)
                .Any(
                    branch =>
                        branch.Name == targetBranch &&
                        branch.TipCommitId == targetTipCommitId);
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResolveConflict))]
    private async Task UseCurrent()
    {
        await ResolveConflict(FolderProjectMergeChoice.Current);
    }

    [RelayCommand(CanExecute = nameof(CanResolveConflict))]
    private async Task UseIncoming()
    {
        await ResolveConflict(FolderProjectMergeChoice.Incoming);
    }

    [RelayCommand(CanExecute = nameof(CanCompleteMerge))]
    private async Task CompleteMerge()
    {
        var message = MergeMessage;
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.CompleteMerge(
                        ProjectRoot,
                        message));
                MergeMessage = "";
                return _localization.Get(
                    "FolderProject.VersionControl.Status.MergeCompleted");
            },
            "FolderProject.VersionControl.Busy.Merging");
    }

    [RelayCommand(CanExecute = nameof(CanAbortMerge))]
    private async Task AbortMerge()
    {
        if (!Confirm("AbortMerge"))
            return;

        await RunAbortAsync();
    }

    public bool CanCloseWindow()
    {
        if (IsBusy)
            return false;

        if (!EnsureMergeStateForClose())
            return false;

        if (MergePhase == FolderProjectMergePhase.None)
            return true;

        if (MergePhase != FolderProjectMergePhase.RecoveryRequired)
        {
            _dialogs.ShowDialogBox(
                _localization.Get(
                    "FolderProject.VersionControl.Close.MergeActive"),
                _localization.Get(
                    "FolderProject.VersionControl.Title"));
            return false;
        }

        if (!Confirm("CloseAbortRecovery"))
            return false;

        if (RunAbortForClose() &&
            MergePhase == FolderProjectMergePhase.None)
        {
            return true;
        }

        return Confirm("ForceCloseRecovery");
    }

    private bool EnsureMergeStateForClose()
    {
        if (_mergeStateKnown ||
            MergePhase != FolderProjectMergePhase.None)
            return true;

        try
        {
            var mergeState =
                _versionControlService.GetMergeState(ProjectRoot);
            _mergeStateKnown = true;
            MergePhase = mergeState.Phase;
            MergeSummary = _localization.Get(
                $"FolderProject.VersionControl.Merge.Phase.{mergeState.Phase}");
            return true;
        }
        catch (FolderProjectVersionControlException exception)
            when (exception.Code ==
                  FolderProjectVersionControlError
                      .RepositoryNotInitialized)
        {
            _mergeStateKnown = true;
            return true;
        }
        catch (FolderProjectVersionControlException exception)
        {
            ShowVersionControlError(exception.Code);
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Folder-project merge-state close check failed.");
            ShowGenericError();
        }

        return false;
    }

    private async Task<bool> RunAbortAsync()
    {
        return await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () =>
                    {
                        _versionControlService.AbortMerge(ProjectRoot);
                        return true;
                    });
                return _localization.Get(
                    "FolderProject.VersionControl.Status.MergeAborted");
            },
            "FolderProject.VersionControl.Busy.Merging");
    }

    private bool RunAbortForClose()
    {
        try
        {
            ExecuteCoordinated(
                () =>
                {
                    _versionControlService.AbortMerge(ProjectRoot);
                    return true;
                });
            MergePhase = _versionControlService
                .GetMergeState(ProjectRoot)
                .Phase;
            StatusMessage = _localization.Get(
                "FolderProject.VersionControl.Status.MergeAborted");
            return true;
        }
        catch (FolderProjectVersionControlException exception)
        {
            ShowVersionControlError(exception.Code);
        }
        catch (FolderProjectGitHostException exception)
        {
            _logger.Error(
                exception,
                "Folder-project Git host coordination failed.");
            ShowGenericError();
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Folder-project version-control operation failed.");
            ShowGenericError();
        }

        return false;
    }

    private async Task ResolveConflict(FolderProjectMergeChoice choice)
    {
        var conflictId = SelectedMergeConflict!.Id;
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.ResolveMergeConflict(
                        ProjectRoot,
                        conflictId,
                        choice));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.ConflictResolved");
            },
            "FolderProject.VersionControl.Busy.Merging");
    }

    private Task<T> ExecuteCoordinatedAsync<T>(Func<T> operation)
    {
        return _coordinator.ExecuteAsync(
            ProjectRoot,
            operation,
            OpenWhenComplete);
    }

    private T ExecuteCoordinated<T>(Func<T> operation)
    {
        return _coordinator.Execute(
            ProjectRoot,
            operation,
            OpenWhenComplete);
    }

    private async Task<bool> RunOperationAsync(
        Func<Task<string?>> operation,
        string busyMessageKey,
        RefreshMode refreshMode = RefreshMode.Full)
    {
        if (IsBusy)
            return false;

        var succeeded = false;
        BusyMessage = _localization.Get(busyMessageKey);
        IsBusy = true;
        try
        {
            var message = await operation();
            if (message != null)
                StatusMessage = message;
            succeeded = true;
        }
        catch (FolderProjectGitOperationCanceledException)
        {
            StatusMessage = _localization.Get(
                "FolderProject.VersionControl.Status.Cancelled");
        }
        catch (FolderProjectVersionControlException exception)
        {
            ShowVersionControlError(exception.Code);
        }
        catch (FolderProjectGitHostException exception)
        {
            _logger.Error(
                exception,
                "Folder-project Git host coordination failed.");
            ShowGenericError();
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Folder-project version-control operation failed.");
            ShowGenericError();
        }
        finally
        {
            await RefreshAfterOperationAsync(refreshMode);
            BusyMessage = "";
            IsBusy = false;
            NotifyCommands();
        }

        return succeeded;
    }

    private void ShowVersionControlError(
        FolderProjectVersionControlError code)
    {
        StatusMessage = _localization.Get(
            $"FolderProject.VersionControl.Error.{code}");
        _dialogs.ShowDialogBox(
            StatusMessage,
            _localization.Get(
                "FolderProject.VersionControl.ErrorTitle"));
    }

    private void ShowGenericError()
    {
        StatusMessage = _localization.Get(
            "FolderProject.VersionControl.Error.Generic");
        _dialogs.ShowDialogBox(
            StatusMessage,
            _localization.Get(
                "FolderProject.VersionControl.ErrorTitle"));
    }

    private async Task RefreshAfterOperationAsync(RefreshMode refreshMode)
    {
        if (refreshMode == RefreshMode.None)
            return;

        try
        {
            BusyMessage = _localization.Get(
                "FolderProject.VersionControl.Busy.Refreshing");
            if (refreshMode == RefreshMode.Branches)
                await RefreshBranchesCoreAsync();
            else
                await RefreshCoreAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Folder-project version-control refresh failed.");
        }
    }

    private async Task RefreshCoreAsync()
    {
        var selection = CurrentSelection();
        var includeCommitChanges = SelectedTabIndex == 1;
        var snapshot = await Task.Run(
            () => CaptureRefreshSnapshot(
                selection.SelectedCommitId,
                selection.SelectedHistoryBranchName,
                includeCommitChanges));
        ApplyRefreshSnapshot(snapshot, selection);
    }

    private FolderProjectRefreshSnapshot CaptureRefreshSnapshot(
        string? selectedCommitId,
        string? selectedHistoryBranchName,
        bool includeCommitChanges)
    {
        var status = _versionControlService.GetStatus(ProjectRoot);
        if (!status.IsInitialized)
        {
            return new FolderProjectRefreshSnapshot(
                status,
                null,
                [],
                [],
                null,
                null,
                []);
        }

        FolderProjectGitIdentity? identity = null;
        try
        {
            identity = _versionControlService.GetIdentity(ProjectRoot);
        }
        catch (FolderProjectVersionControlException exception)
            when (exception.Code ==
                  FolderProjectVersionControlError.IdentityMissing)
        {
        }

        var branches = _versionControlService.GetBranches(ProjectRoot);
        var historyBranchName =
            branches.Any(
                branch => branch.Name == selectedHistoryBranchName)
                ? selectedHistoryBranchName!
                : status.CurrentBranch ??
                  branches.FirstOrDefault()?.Name;
        var history = historyBranchName == null
            ? []
            : _versionControlService.GetHistory(
                ProjectRoot,
                historyBranchName,
                100);
        var selectedCommit =
            history.FirstOrDefault(
                commit => commit.Id == selectedCommitId) ??
            history.FirstOrDefault();
        var commitChanges =
            !includeCommitChanges || selectedCommit == null
            ? []
            : _versionControlService.GetCommitChanges(
                ProjectRoot,
                selectedCommit.Id);
        return new FolderProjectRefreshSnapshot(
            status,
            identity,
            history,
            branches,
            historyBranchName,
            _versionControlService.GetMergeState(ProjectRoot),
            commitChanges);
    }

    private void ApplyRefreshSnapshot(
        FolderProjectRefreshSnapshot snapshot,
        FolderProjectSelection selection)
    {
        _refreshing = true;
        _mergeStateKnown = false;
        try
        {
            var status = snapshot.Status;
            IsInitialized = status.IsInitialized;
            CurrentBranch = status.CurrentBranch ?? "";
            HeadCommitId = status.HeadCommitId ?? "";
            IsDetached = status.IsDetached;
            OperationState = status.OperationState;
            IsClean = status.IsClean;
            Replace(
                WorkingChanges,
                status.Changes.Select(
                    change => new FolderProjectWorkingChangeRow(
                        change,
                        _localization)));

            if (!status.IsInitialized)
            {
                ClearRepositoryData();
                _mergeStateKnown = true;
                return;
            }

            if (snapshot.Identity != null)
            {
                IdentityName = snapshot.Identity.Name;
                IdentityEmail = snapshot.Identity.Email;
                HasIdentity = true;
            }
            else
            {
                EnsureDefaultIdentityInput();
                HasIdentity = false;
            }

            ApplyBranches(
                snapshot.Branches,
                selection.SelectedBranchName,
                selection.SelectedMergeSourceName,
                selection.SelectedMergeTargetName,
                snapshot.HistoryBranchName);
            Replace(History, snapshot.History);
            SelectedCommit =
                History.FirstOrDefault(
                    commit =>
                        commit.Id == selection.SelectedCommitId) ??
                History.FirstOrDefault();
            ApplyCommitChanges(
                snapshot.CommitChanges,
                selection.SelectedCommitChangePath);
            ApplyMergeState(
                snapshot.MergeState!,
                selection.SelectedConflictId,
                selection.SelectedMergeSourceName,
                selection.SelectedMergeTargetName);
        }
        finally
        {
            _refreshing = false;
            NotifyCommands();
        }
    }

    private async Task RefreshBranchesCoreAsync()
    {
        var selectedBranchName = SelectedBranch?.Name;
        var selectedHistoryBranchName = SelectedHistoryBranch?.Name;
        var selectedMergeSourceName = SelectedMergeSource?.Name;
        var selectedMergeTargetName = SelectedMergeTarget?.Name;
        var branches = await Task.Run(
            () => _versionControlService.GetBranches(ProjectRoot));
        _refreshing = true;
        try
        {
            ApplyBranches(
                branches,
                selectedBranchName,
                selectedMergeSourceName,
                selectedMergeTargetName,
                selectedHistoryBranchName);
        }
        finally
        {
            _refreshing = false;
            NotifyCommands();
        }
    }

    private void ApplyBranches(
        IReadOnlyList<FolderProjectBranchInfo> branches,
        string? selectedBranchName,
        string? selectedMergeSourceName,
        string? selectedMergeTargetName,
        string? selectedHistoryBranchName)
    {
        Replace(Branches, branches);
        Replace(MergeSources, Branches);
        Replace(MergeTargets, Branches);
        SelectedBranch = Branches.FirstOrDefault(
            branch => branch.Name == selectedBranchName);
        SelectedHistoryBranch =
            Branches.FirstOrDefault(
                branch => branch.Name == selectedHistoryBranchName) ??
            Branches.FirstOrDefault(branch => branch.IsCurrent);
        SelectedMergeSource =
            MergeSources.FirstOrDefault(
                branch => branch.Name == selectedMergeSourceName) ??
            MergeSources.FirstOrDefault(branch => !branch.IsCurrent);
        SelectedMergeTarget =
            MergeTargets.FirstOrDefault(
                branch =>
                    branch.Name == selectedMergeTargetName &&
                    branch.Name != SelectedMergeSource?.Name) ??
            GetDefaultMergeTarget(SelectedMergeSource);
    }

    private void ApplyMergeState(
        FolderProjectMergeState mergeState,
        string? selectedConflictId,
        string? selectedMergeSourceName,
        string? selectedMergeTargetName)
    {
        _mergeStateKnown = true;
        MergePhase = mergeState.Phase;
        MergeSummary = _localization.Get(
            $"FolderProject.VersionControl.Merge.Phase.{mergeState.Phase}");
        if (mergeState.Phase != FolderProjectMergePhase.None &&
            !string.IsNullOrWhiteSpace(mergeState.SourceBranch) &&
            MergeSources.All(
                branch => branch.Name != mergeState.SourceBranch))
        {
            MergeSources.Add(
                new FolderProjectBranchInfo(
                    mergeState.SourceBranch,
                    mergeState.SourceHeadCommitId ?? "",
                    false));
        }

        SelectedMergeSource =
            mergeState.Phase == FolderProjectMergePhase.None
                ? MergeSources.FirstOrDefault(
                      branch =>
                          branch.Name == selectedMergeSourceName) ??
                  MergeSources.FirstOrDefault(
                      branch => !branch.IsCurrent)
                : MergeSources.FirstOrDefault(
                    branch => branch.Name == mergeState.SourceBranch);
        SelectedMergeTarget =
            mergeState.Phase == FolderProjectMergePhase.None
                ? MergeTargets.FirstOrDefault(
                      branch =>
                          branch.Name == selectedMergeTargetName &&
                          branch.Name != SelectedMergeSource?.Name) ??
                  GetDefaultMergeTarget(SelectedMergeSource)
                : MergeTargets.FirstOrDefault(
                    branch => branch.Name == mergeState.CurrentBranch);
        Replace(
            MergeConflicts,
            mergeState.Conflicts.Select(
                conflict => new FolderProjectMergeConflictRow(
                    conflict,
                    _localization)));
        SelectedMergeConflict =
            MergeConflicts.FirstOrDefault(
                conflict => conflict.Id == selectedConflictId) ??
            MergeConflicts.FirstOrDefault();
        if (mergeState.Phase != FolderProjectMergePhase.None &&
            string.IsNullOrWhiteSpace(MergeMessage))
        {
            MergeMessage = mergeState.SuggestedMessage ?? "";
        }
        if (mergeState.Phase == FolderProjectMergePhase.None)
            MergeMessage = "";
    }

    private void ClearRepositoryData()
    {
        CurrentBranch = "";
        HeadCommitId = "";
        IsDetached = false;
        OperationState = FolderProjectRepositoryOperationState.None;
        IsClean = true;
        HasIdentity = false;
        History.Clear();
        CommitChanges.Clear();
        Branches.Clear();
        MergeSources.Clear();
        MergeTargets.Clear();
        MergeConflicts.Clear();
        SelectedCommit = null;
        SelectedCommitChange = null;
        SelectedHistoryBranch = null;
        SelectedBranch = null;
        SelectedMergeSource = null;
        SelectedMergeTarget = null;
        SelectedMergeConflict = null;
        MergePhase = FolderProjectMergePhase.None;
        MergeSummary = _localization.Get(
            "FolderProject.VersionControl.Merge.Phase.None");
    }

    private void ApplyCommitChanges(
        IReadOnlyList<FolderProjectCommitChange> changes,
        string? selectedPath)
    {
        Replace(
            CommitChanges,
            changes.Select(
                change => new FolderProjectCommitChangeRow(
                    change,
                    _localization)));
        SelectedCommitChange = CommitChanges.FirstOrDefault(
            change => change.RepositoryPath == selectedPath);
    }

    private async Task LoadSelectedCommitChangesAsync(
        FolderProjectCommitSummary? commit)
    {
        var requestId = Interlocked.Increment(
            ref _commitChangesRequestId);
        if (commit == null)
        {
            ApplyCommitChanges([], null);
            return;
        }

        var selectedPath = SelectedCommitChange?.RepositoryPath;
        ApplyCommitChanges([], null);
        BusyMessage = _localization.Get(
            "FolderProject.VersionControl.Busy.LoadingCommit");
        IsBusy = true;
        try
        {
            var changes = await Task.Run(
                () => _versionControlService.GetCommitChanges(
                    ProjectRoot,
                    commit.Id));
            if (requestId == _commitChangesRequestId &&
                SelectedCommit?.Id == commit.Id)
            {
                ApplyCommitChanges(changes, selectedPath);
            }
        }
        catch (FolderProjectVersionControlException exception)
        {
            if (requestId == _commitChangesRequestId)
                ShowVersionControlError(exception.Code);
        }
        catch (Exception exception)
        {
            if (requestId == _commitChangesRequestId)
            {
                _logger.Error(
                    exception,
                    "Folder-project commit change refresh failed.");
                ShowGenericError();
            }
        }
        finally
        {
            if (requestId == _commitChangesRequestId)
            {
                BusyMessage = "";
                IsBusy = false;
                NotifyCommands();
            }
        }
    }

    private async Task LoadHistoryAsync(
        FolderProjectBranchInfo? branch)
    {
        var requestId = Interlocked.Increment(ref _historyRequestId);
        var selectedCommitId = SelectedCommit?.Id;
        Replace(History, []);
        SelectedCommit = null;
        ApplyCommitChanges([], null);
        if (branch == null)
            return;

        BusyMessage = _localization.Get(
            "FolderProject.VersionControl.Busy.LoadingHistory");
        IsBusy = true;
        try
        {
            var history = await Task.Run(
                () => _versionControlService.GetHistory(
                    ProjectRoot,
                    branch.Name,
                    100));
            if (requestId != _historyRequestId ||
                SelectedHistoryBranch?.Name != branch.Name)
            {
                return;
            }

            _refreshing = true;
            try
            {
                Replace(History, history);
                SelectedCommit =
                    History.FirstOrDefault(
                        commit => commit.Id == selectedCommitId) ??
                    History.FirstOrDefault();
            }
            finally
            {
                _refreshing = false;
            }

            if (SelectedTabIndex == 1)
            {
                CommitChangesLoadTask =
                    LoadSelectedCommitChangesAsync(SelectedCommit);
                await CommitChangesLoadTask;
            }
        }
        catch (FolderProjectVersionControlException exception)
        {
            if (requestId == _historyRequestId)
                ShowVersionControlError(exception.Code);
        }
        catch (Exception exception)
        {
            if (requestId == _historyRequestId)
            {
                _logger.Error(
                    exception,
                    "Folder-project history refresh failed.");
                ShowGenericError();
            }
        }
        finally
        {
            if (requestId == _historyRequestId)
            {
                BusyMessage = "";
                IsBusy = false;
                NotifyCommands();
            }
        }
    }

    private FolderProjectGitIdentity CurrentIdentity()
    {
        return new FolderProjectGitIdentity(
            string.IsNullOrWhiteSpace(IdentityName)
                ? _defaultIdentityName
                : IdentityName.Trim(),
            string.IsNullOrWhiteSpace(IdentityEmail)
                ? _defaultIdentityEmail
                : IdentityEmail.Trim());
    }

    private void EnsureDefaultIdentityInput()
    {
        if (string.IsNullOrWhiteSpace(IdentityName))
            IdentityName = _defaultIdentityName;
        if (string.IsNullOrWhiteSpace(IdentityEmail))
            IdentityEmail = _defaultIdentityEmail;
    }

    private FolderProjectBranchInfo? GetDefaultMergeTarget(
        FolderProjectBranchInfo? source)
    {
        if (source == null)
            return null;

        if (!source.IsCurrent)
        {
            return MergeTargets.FirstOrDefault(
                branch => branch.IsCurrent);
        }

        return MergeTargets.FirstOrDefault(
                   branch =>
                       branch.Name.Equals(
                           "master",
                           StringComparison.OrdinalIgnoreCase) &&
                       branch.Name != source.Name) ??
               MergeTargets.FirstOrDefault(
                   branch => branch.Name != source.Name);
    }

    private FolderProjectSelection CurrentSelection()
    {
        return new FolderProjectSelection(
            SelectedCommit?.Id,
            SelectedCommitChange?.RepositoryPath,
            SelectedHistoryBranch?.Name,
            SelectedBranch?.Name,
            SelectedMergeSource?.Name,
            SelectedMergeTarget?.Name,
            SelectedMergeConflict?.Id);
    }

    private bool Confirm(string key, params object[] arguments)
    {
        var localizationKey =
            $"FolderProject.VersionControl.Confirm.{key}";
        var message = arguments.Length == 0
            ? _localization.Get(localizationKey)
            : _localization.GetFormat(localizationKey, arguments);
        return _dialogs.ShowYesNoBox(
                   message,
                   _localization.Get(
                       "FolderProject.VersionControl.ConfirmTitle")) ==
               ShowMessageBoxResult.OK;
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private bool CanRefresh() =>
        !IsBusy && !string.IsNullOrWhiteSpace(ProjectRoot);

    private bool CanInitialize() =>
        CanRefresh() &&
        !IsInitialized;

    private bool CanSaveIdentity() =>
        CanUseRepository();

    private bool CanCommit() =>
        CanUseRepository() &&
        HasIdentity &&
        !IsClean &&
        !string.IsNullOrWhiteSpace(CommitMessage);

    private bool CanRestoreFile()
    {
        return CanUseRepository() &&
               SelectedHistoryBranch is { IsCurrent: true } &&
               SelectedCommit != null &&
               SelectedCommitChange?.Source.Kind is
                   FolderProjectCommitChangeKind.Added or
                   FolderProjectCommitChangeKind.Modified or
                   FolderProjectCommitChangeKind.Renamed;
    }

    private bool CanCreateRecoveryBranch() =>
        IsInitialized &&
        !IsBusy &&
        SelectedCommit != null &&
        !string.IsNullOrWhiteSpace(RecoveryBranchName);

    private bool CanCreateBranch() =>
        CanUseRepository() &&
        !string.IsNullOrWhiteSpace(BranchName);

    private bool CanRenameBranch() =>
        CanCreateBranch() && SelectedBranch != null;

    private bool CanDeleteBranch() =>
        CanUseRepository() &&
        SelectedBranch is { IsCurrent: false };

    private bool CanSwitchBranch() =>
        CanUseRepository() &&
        IsClean &&
        !IsDetached &&
        SelectedBranch is { IsCurrent: false };

    private bool CanPrepareMerge() =>
        CanUseRepository() &&
        IsClean &&
        !IsDetached &&
        SelectedBranch != null &&
        Branches.Any(
            branch => branch.Name != SelectedBranch.Name);

    private bool CanBeginMerge() =>
        CanUseRepository() &&
        IsClean &&
        !IsDetached &&
        SelectedMergeSource != null &&
        SelectedMergeTarget != null &&
        SelectedMergeSource.Name != SelectedMergeTarget.Name;

    private bool CanResolveConflict() =>
        IsInitialized &&
        !IsBusy &&
        MergePhase == FolderProjectMergePhase.Conflicts &&
        SelectedMergeConflict != null;

    private bool CanCompleteMerge() =>
        IsInitialized &&
        !IsBusy &&
        HasIdentity &&
        MergePhase == FolderProjectMergePhase.ReadyToCommit &&
        !string.IsNullOrWhiteSpace(MergeMessage);

    private bool CanAbortMerge() =>
        IsInitialized &&
        !IsBusy &&
        MergePhase != FolderProjectMergePhase.None;

    private bool CanUseRepository() =>
        IsInitialized &&
        !IsBusy &&
        MergePhase == FolderProjectMergePhase.None &&
        OperationState == FolderProjectRepositoryOperationState.None;

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnIsInitializedChanged(bool value) => NotifyCommands();
    partial void OnIsCleanChanged(bool value)
    {
        OnPropertyChanged(nameof(BranchActionHint));
        NotifyCommands();
    }
    partial void OnIsDetachedChanged(bool value) => NotifyCommands();
    partial void OnCurrentBranchChanged(string value) =>
        OnPropertyChanged(nameof(BranchActionHint));
    partial void OnHasIdentityChanged(bool value) => NotifyCommands();
    partial void OnOperationStateChanged(
        FolderProjectRepositoryOperationState value) =>
        NotifyCommands();
    partial void OnIdentityNameChanged(string value) => NotifyCommands();
    partial void OnIdentityEmailChanged(string value) => NotifyCommands();
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1 &&
            !_refreshing &&
            !IsBusy &&
            SelectedCommit != null)
        {
            CommitChangesLoadTask =
                LoadSelectedCommitChangesAsync(SelectedCommit);
        }
    }
    partial void OnCommitMessageChanged(string value) => NotifyCommands();
    partial void OnBranchNameChanged(string value) => NotifyCommands();
    partial void OnRecoveryBranchNameChanged(string value) =>
        NotifyCommands();
    partial void OnMergeMessageChanged(string value) => NotifyCommands();
    partial void OnMergePhaseChanged(FolderProjectMergePhase value)
    {
        OnPropertyChanged(nameof(HasActiveMerge));
        OnPropertyChanged(nameof(IsRecoveryRequired));
        OnPropertyChanged(nameof(CanSelectMergeSource));
        NotifyCommands();
    }

    partial void OnSelectedCommitChanged(
        FolderProjectCommitSummary? value)
    {
        NotifyCommands();
        if (_refreshing)
            return;

        CommitChangesLoadTask =
            LoadSelectedCommitChangesAsync(value);
    }

    partial void OnSelectedCommitChangeChanged(
        FolderProjectCommitChangeRow? value) =>
        NotifyCommands();
    partial void OnSelectedHistoryBranchChanged(
        FolderProjectBranchInfo? value)
    {
        OnPropertyChanged(nameof(HistoryBranchHint));
        NotifyCommands();
        if (_refreshing)
            return;

        HistoryLoadTask = LoadHistoryAsync(value);
    }
    partial void OnSelectedBranchChanged(
        FolderProjectBranchInfo? value)
    {
        OnPropertyChanged(nameof(BranchActionHint));
        NotifyCommands();
    }
    partial void OnSelectedMergeSourceChanged(
        FolderProjectBranchInfo? value)
    {
        if (!_refreshing &&
            MergePhase == FolderProjectMergePhase.None)
        {
            SelectedMergeTarget = GetDefaultMergeTarget(value);
        }

        NotifyCommands();
    }
    partial void OnSelectedMergeTargetChanged(
        FolderProjectBranchInfo? value) =>
        NotifyCommands();
    partial void OnSelectedMergeConflictChanged(
        FolderProjectMergeConflictRow? value) =>
        NotifyCommands();

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        InitializeCommand.NotifyCanExecuteChanged();
        SaveIdentityCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
        RestoreFileCommand.NotifyCanExecuteChanged();
        CreateRecoveryBranchCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        RenameBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        SwitchBranchCommand.NotifyCanExecuteChanged();
        PrepareMergeCommand.NotifyCanExecuteChanged();
        BeginMergeCommand.NotifyCanExecuteChanged();
        UseCurrentCommand.NotifyCanExecuteChanged();
        UseIncomingCommand.NotifyCanExecuteChanged();
        CompleteMergeCommand.NotifyCanExecuteChanged();
        AbortMergeCommand.NotifyCanExecuteChanged();
    }

    private enum RefreshMode
    {
        None,
        Full,
        Branches,
    }

    private sealed record FolderProjectSelection(
        string? SelectedCommitId,
        string? SelectedCommitChangePath,
        string? SelectedHistoryBranchName,
        string? SelectedBranchName,
        string? SelectedMergeSourceName,
        string? SelectedMergeTargetName,
        string? SelectedConflictId);

    private sealed record FolderProjectRefreshSnapshot(
        FolderProjectRepositoryStatus Status,
        FolderProjectGitIdentity? Identity,
        IReadOnlyList<FolderProjectCommitSummary> History,
        IReadOnlyList<FolderProjectBranchInfo> Branches,
        string? HistoryBranchName,
        FolderProjectMergeState? MergeState,
        IReadOnlyList<FolderProjectCommitChange> CommitChanges);
}
