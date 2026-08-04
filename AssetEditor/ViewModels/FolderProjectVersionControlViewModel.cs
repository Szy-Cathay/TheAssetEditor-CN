using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
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
    public string KindSummaryText { get; }
    public bool IsDeleted => Source.Kind.HasFlag(
        FolderProjectWorkingChangeKind.Deleted);
    public string StatusGlyph => IsDeleted
        ? ""
        : Source.Kind.HasFlag(FolderProjectWorkingChangeKind.Added) ||
          Source.Kind.HasFlag(FolderProjectWorkingChangeKind.Untracked)
            ? "A"
            : Source.Kind.HasFlag(FolderProjectWorkingChangeKind.Modified) ||
              Source.Kind.HasFlag(FolderProjectWorkingChangeKind.Renamed) ||
              Source.Kind.HasFlag(FolderProjectWorkingChangeKind.TypeChanged)
                ? "M"
                : "";

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
        var summaryKind = new[]
        {
            FolderProjectWorkingChangeKind.Conflicted,
            FolderProjectWorkingChangeKind.Unreadable,
            FolderProjectWorkingChangeKind.Renamed,
            FolderProjectWorkingChangeKind.Deleted,
            FolderProjectWorkingChangeKind.Added,
            FolderProjectWorkingChangeKind.TypeChanged,
            FolderProjectWorkingChangeKind.Modified,
            FolderProjectWorkingChangeKind.Untracked,
        }.First(kind => source.Kind.HasFlag(kind));
        KindSummaryText = localization.Get(
            $"FolderProject.VersionControl.Change.{summaryKind}");
    }
}

public sealed class FolderProjectWorkingChangeTreeNode
{
    private readonly List<FolderProjectWorkingChangeTreeNode> _children = [];

    public string Name { get; }
    public string RepositoryPath { get; }
    public bool IsRoot { get; }
    public bool IsStagedTree { get; }
    public bool IsFolder => Change == null;
    public bool IsExpanded { get; set; } = true;
    public FolderProjectWorkingChangeRow? Change { get; }
    public IReadOnlyList<FolderProjectWorkingChangeTreeNode> Children =>
        _children;
    public IReadOnlyList<FolderProjectWorkingChangeRow> Changes =>
        Change == null
            ? _children.SelectMany(child => child.Changes).ToList()
            : [Change];
    public string KindText => Change?.KindText ?? "";
    public string KindSummaryText => Change?.KindSummaryText ?? "";
    public bool IsDeleted => Change?.IsDeleted == true;
    public string StatusGlyph => Change?.StatusGlyph ?? "";

    private FolderProjectWorkingChangeTreeNode(
        string name,
        string repositoryPath,
        bool isRoot,
        bool isStagedTree,
        FolderProjectWorkingChangeRow? change = null)
    {
        Name = name;
        RepositoryPath = repositoryPath;
        IsRoot = isRoot;
        IsStagedTree = isStagedTree;
        Change = change;
    }

    public static IReadOnlyList<FolderProjectWorkingChangeTreeNode> Build(
        string projectRoot,
        IEnumerable<FolderProjectWorkingChangeRow> changes,
        bool isStagedTree = false)
    {
        var root = new FolderProjectWorkingChangeTreeNode(
            projectRoot,
            projectRoot,
            isRoot: true,
            isStagedTree: isStagedTree);
        foreach (var change in changes.OrderBy(
                     item => item.RepositoryPath,
                     StringComparer.OrdinalIgnoreCase))
        {
            var segments = change.RepositoryPath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var parent = root;
            var folderPath = "";
            foreach (var segment in segments[..^1])
            {
                folderPath = string.IsNullOrEmpty(folderPath)
                    ? segment
                    : $"{folderPath}/{segment}";
                var folder = parent._children.FirstOrDefault(
                    item =>
                        item.IsFolder &&
                        item.Name.Equals(
                            segment,
                            StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                {
                    folder = new FolderProjectWorkingChangeTreeNode(
                        segment,
                        folderPath,
                        isRoot: false,
                        isStagedTree: isStagedTree);
                    parent._children.Add(folder);
                }
                parent = folder;
            }

            parent._children.Add(
                new FolderProjectWorkingChangeTreeNode(
                    segments[^1],
                    change.RepositoryPath,
                    isRoot: false,
                    isStagedTree: isStagedTree,
                    change: change));
        }

        if (root._children.Count == 0)
            return [];

        root.SortChildren();
        return [root];
    }

    private void SortChildren()
    {
        _children.Sort(
            (left, right) =>
            {
                var folderOrder = right.IsFolder.CompareTo(left.IsFolder);
                return folderOrder != 0
                    ? folderOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        left.Name,
                        right.Name);
            });
        foreach (var child in _children)
            child.SortChildren();
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
    public bool IsDeleted => Source.Kind ==
        FolderProjectCommitChangeKind.Deleted;
    public string StatusGlyph => Source.Kind switch
    {
        FolderProjectCommitChangeKind.Added => "A",
        FolderProjectCommitChangeKind.Deleted => "",
        _ => "M",
    };

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

public sealed class FolderProjectCommitChangeTreeNode
{
    private readonly List<FolderProjectCommitChangeTreeNode> _children = [];

    public string Name { get; }
    public string RepositoryPath { get; }
    public bool IsRoot { get; }
    public bool IsFolder => Change == null;
    public bool IsExpanded { get; set; } = true;
    public FolderProjectCommitChangeRow? Change { get; }
    public IReadOnlyList<FolderProjectCommitChangeTreeNode> Children =>
        _children;
    public IReadOnlyList<FolderProjectCommitChangeRow> Changes =>
        Change == null
            ? _children.SelectMany(child => child.Changes).ToList()
            : [Change];
    public string KindText => Change?.KindText ?? "";
    public bool IsDeleted => Change?.IsDeleted == true;
    public string StatusGlyph => Change?.StatusGlyph ?? "";

    private FolderProjectCommitChangeTreeNode(
        string name,
        string repositoryPath,
        bool isRoot,
        FolderProjectCommitChangeRow? change = null)
    {
        Name = name;
        RepositoryPath = repositoryPath;
        IsRoot = isRoot;
        Change = change;
    }

    public static IReadOnlyList<FolderProjectCommitChangeTreeNode> Build(
        string projectRoot,
        IEnumerable<FolderProjectCommitChangeRow> changes)
    {
        var root = new FolderProjectCommitChangeTreeNode(
            projectRoot,
            projectRoot,
            isRoot: true);
        foreach (var change in changes.OrderBy(
                     item => item.RepositoryPath,
                     StringComparer.OrdinalIgnoreCase))
        {
            var segments = change.RepositoryPath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var parent = root;
            var folderPath = "";
            foreach (var segment in segments[..^1])
            {
                folderPath = string.IsNullOrEmpty(folderPath)
                    ? segment
                    : $"{folderPath}/{segment}";
                var folder = parent._children.FirstOrDefault(
                    item =>
                        item.IsFolder &&
                        item.Name.Equals(
                            segment,
                            StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                {
                    folder = new FolderProjectCommitChangeTreeNode(
                        segment,
                        folderPath,
                        isRoot: false);
                    parent._children.Add(folder);
                }
                parent = folder;
            }

            parent._children.Add(
                new FolderProjectCommitChangeTreeNode(
                    segments[^1],
                    change.RepositoryPath,
                    isRoot: false,
                    change));
        }

        if (root._children.Count == 0)
            return [];

        root.SortChildren();
        return [root];
    }

    private void SortChildren()
    {
        _children.Sort(
            (left, right) =>
            {
                var folderOrder = right.IsFolder.CompareTo(left.IsFolder);
                return folderOrder != 0
                    ? folderOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        left.Name,
                        right.Name);
            });
        foreach (var child in _children)
            child.SortChildren();
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
    private readonly IFolderProjectUnsavedChangesService _unsavedChanges;
    private readonly IFolderProjectUnsavedChangesPrompt
        _unsavedChangesPrompt;
    private readonly LocalizationManager _localization;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly object _progressUpdateGate = new();
    private readonly object _commitChangesCacheGate = new();
    private readonly Dictionary<string,
        IReadOnlyList<FolderProjectCommitChange>> _commitChangesCache =
            new(StringComparer.Ordinal);
    private readonly string _defaultIdentityName;
    private readonly string _defaultIdentityEmail;
    private readonly ILogger _logger =
        Logging.Create<FolderProjectVersionControlViewModel>();
    private bool _refreshing;
    private bool _suppressRepositoryTabHistoryLoad;
    private bool _hasHistorySnapshot;
    private string? _requestedMergeSourceBranchName;
    private bool _mergeStateKnown;
    private int _commitChangesRequestId;
    private int _historyRequestId;
    private int _workingChangesRevision;
    private readonly SemaphoreSlim _workingChangesRefreshGate = new(1, 1);
    private FolderProjectCommitEditSession? _commitEditSession;
    private FolderProjectVersionControlProgress? _pendingProgressUpdate;
    private bool _progressUpdateScheduled;

    [ObservableProperty] private string _projectRoot = "";
    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private bool _openWhenComplete;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isStatusRefreshing;
    [ObservableProperty] private bool _isCommitChangesLoading;
    [ObservableProperty] private bool _hasRepositorySnapshot;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _loadingProgressStatusText = "";
    [ObservableProperty] private string _loadingProgressDetailText = "";
    [ObservableProperty] private long _loadingProgressValue;
    [ObservableProperty] private long _loadingProgressMaximum = 1;
    [ObservableProperty]
    private bool _loadingProgressIsIndeterminate = true;
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
    [ObservableProperty] private string _primaryBranchName = "master";
    [ObservableProperty] private string _branchName = "";
    [ObservableProperty] private string _recoveryBranchName = "";
    [ObservableProperty] private bool _isBranchSwitchChoiceOpen;
    [ObservableProperty] private string _pendingBranchName = "";
    [ObservableProperty] private string _mergeMessage = "";
    [ObservableProperty] private bool _isMergeSectionExpanded;
    [ObservableProperty]
    private FolderProjectMergePhase _mergePhase =
        FolderProjectMergePhase.None;
    [ObservableProperty] private string _mergeSummary = "";
    [ObservableProperty]
    private FolderProjectCommitSummary? _selectedCommit;
    [ObservableProperty]
    private FolderProjectCommitChangeRow? _selectedCommitChange;
    [ObservableProperty]
    private FolderProjectCommitChangeTreeNode?
        _selectedCommitChangeTreeNode;
    [ObservableProperty]
    private IReadOnlyList<FolderProjectCommitChangeRow>
        _selectedCommitChanges = [];
    [ObservableProperty]
    private FolderProjectWorkingChangeRow? _selectedUnstagedChange;
    [ObservableProperty]
    private FolderProjectWorkingChangeRow? _selectedStagedChange;
    [ObservableProperty]
    private IReadOnlyList<FolderProjectWorkingChangeRow>
        _selectedUnstagedChanges = [];
    [ObservableProperty]
    private IReadOnlyList<FolderProjectWorkingChangeRow>
        _selectedStagedChanges = [];
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedHistoryBranch;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedBranch;
    [ObservableProperty]
    private FolderProjectStashInfo? _selectedStash;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedMergeSource;
    [ObservableProperty]
    private FolderProjectBranchInfo? _selectedMergeTarget;
    [ObservableProperty]
    private FolderProjectMergeConflictRow? _selectedMergeConflict;
    [ObservableProperty]
    private IReadOnlyList<FolderProjectMergeConflictRow>
        _selectedMergeConflicts = [];

    public bool IsLoadingOperation =>
        IsBusy || IsStatusRefreshing || IsCommitChangesLoading;

    public string LoadingOperationMessage =>
        IsBusy
            ? BusyMessage
            : IsStatusRefreshing
                ? _localization.Get(
                    "FolderProject.VersionControl.Busy.Refreshing")
                : IsCommitChangesLoading
                    ? _localization.Get(
                        "FolderProject.VersionControl.Busy.LoadingCommit")
                    : "";

    public ObservableCollection<FolderProjectWorkingChangeRow>
        WorkingChanges
    { get; } = [];
    public ObservableCollection<FolderProjectWorkingChangeRow>
        UnstagedChanges
    { get; } = [];
    public ObservableCollection<FolderProjectWorkingChangeRow>
        StagedChanges
    { get; } = [];
    public ObservableCollection<FolderProjectWorkingChangeTreeNode>
        UnstagedChangeTree
    { get; } = [];
    public ObservableCollection<FolderProjectWorkingChangeTreeNode>
        StagedChangeTree
    { get; } = [];
    public ObservableCollection<FolderProjectCommitSummary> History { get; } =
        [];
    public ObservableCollection<FolderProjectCommitChangeRow> CommitChanges
    {
        get;
    } = [];
    public ObservableCollection<FolderProjectCommitChangeTreeNode>
        CommitChangeTree
    { get; } = [];
    public ObservableCollection<FolderProjectBranchInfo> Branches { get; } =
        [];
    public ObservableCollection<FolderProjectStashInfo> Stashes { get; } = [];
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
    public bool HasStagedChanges =>
        WorkingChanges.Any(
            change => change.Source.Kind.HasFlag(
                FolderProjectWorkingChangeKind.Staged));
    public string CommitActionText =>
        _localization.Get(
            HasStagedChanges
                ? "FolderProject.VersionControl.CommitStaged"
                : "FolderProject.VersionControl.CommitAll");
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
        IFolderProjectUnsavedChangesService unsavedChanges,
        IFolderProjectUnsavedChangesPrompt unsavedChangesPrompt,
        LocalizationManager localization)
    {
        _versionControlService = versionControlService;
        _coordinator = coordinator;
        _dialogs = dialogs;
        _unsavedChanges = unsavedChanges;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _localization = localization;
        _synchronizationContext =
            SynchronizationContext.Current is DispatcherSynchronizationContext
                ? SynchronizationContext.Current
                : null;
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
        bool openWhenComplete,
        bool refresh = true)
    {
        var normalizedProjectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        var projectChanged = !string.Equals(
            ProjectRoot,
            normalizedProjectRoot,
            StringComparison.OrdinalIgnoreCase);
        if (projectChanged)
        {
            _refreshing = true;
            try
            {
                IsInitialized = false;
                ClearRepositoryData();
                HasRepositorySnapshot = false;
                _hasHistorySnapshot = false;
            }
            finally
            {
                _refreshing = false;
            }
        }

        ProjectRoot = normalizedProjectRoot;
        ProjectName = projectName;
        OpenWhenComplete = openWhenComplete;
        _mergeStateKnown = false;
        if (refresh)
            RefreshCommand.Execute(null);
    }

    public void OpenRepositoryHistory()
    {
        _suppressRepositoryTabHistoryLoad = true;
        try
        {
            SelectedTabIndex = 1;
        }
        finally
        {
            _suppressRepositoryTabHistoryLoad = false;
        }

        if (_hasHistorySnapshot && HasRepositorySnapshot)
            return;

        RefreshCommand.Execute(null);
    }

    public void OpenMergeProject(
        string projectRoot,
        string projectName,
        string sourceBranch)
    {
        _requestedMergeSourceBranchName = sourceBranch;
        IsMergeSectionExpanded = true;
        _suppressRepositoryTabHistoryLoad = true;
        try
        {
            SelectedTabIndex = 1;
        }
        finally
        {
            _suppressRepositoryTabHistoryLoad = false;
        }

        OpenProject(projectRoot, projectName, false);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task Refresh()
    {
        if (IsBusy || IsStatusRefreshing)
            return;

        BeginLoadingProgress(_localization.Get(
            "FolderProject.VersionControl.Busy.Refreshing"));
        IsStatusRefreshing = true;
        StatusMessage = _localization.Get(
            "FolderProject.VersionControl.Busy.Refreshing");
        try
        {
            await RefreshCoreAsync();
            HasRepositorySnapshot = true;
            StatusMessage = _localization.Get(
                "FolderProject.VersionControl.Status.Refreshed");
        }
        catch (FolderProjectVersionControlException exception)
        {
            ShowVersionControlError(exception.Code);
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Folder-project version-control refresh failed.");
            ShowGenericError();
        }
        finally
        {
            IsStatusRefreshing = false;
        }
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
                        identity,
                        PrimaryBranchName,
                        ReportVersionControlProgress));
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
        if (HasStagedChanges)
            await CommitCore(true);
        else
            await CommitAll();
    }

    [RelayCommand(CanExecute = nameof(CanCommitStaged))]
    private Task CommitStaged()
    {
        return CommitCore(true);
    }

    [RelayCommand(CanExecute = nameof(CanCommitAll))]
    private async Task CommitAll()
    {
        if (_unsavedChanges.HasUnsavedChanges(ProjectRoot, null))
        {
            var choice = _unsavedChangesPrompt.Show(
                FolderProjectUnsavedChangesOperation.CommitAll);
            if (choice == FolderProjectUnsavedChangesChoice.Cancel)
                return;
            if (choice == FolderProjectUnsavedChangesChoice.Save &&
                !_unsavedChanges.SaveUnsavedChanges(ProjectRoot, null))
            {
                return;
            }
        }

        await CommitCore(false);
    }

    private async Task CommitCore(bool commitStaged)
    {
        var message = CommitMessage.Trim();
        var selection = CurrentSelection();
        var knownChanges = WorkingChanges
            .Select(change => change.Source)
            .ToList();
        var knownPaths = knownChanges
            .Where(
                change =>
                    !change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Conflicted) &&
                    !change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Unreadable))
            .SelectMany(
                change => new[]
                {
                    change.RepositoryPath,
                    change.PreviousRepositoryPath,
                })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await RunOperationAsync(
            async () =>
            {
                FolderProjectCommitSummary? commit = null;
                await Task.Run(
                    () =>
                    {
                        if (commitStaged)
                        {
                            commit = _versionControlService.CommitStaged(
                                ProjectRoot,
                                message);
                        }
                        else if (knownPaths.Count != 0)
                        {
                            _versionControlService.StageChanges(
                                ProjectRoot,
                                knownPaths);
                            commit = _versionControlService.CommitStaged(
                                ProjectRoot,
                                message);
                        }
                        else
                        {
                            commit = _versionControlService.CommitAll(
                                ProjectRoot,
                                message);
                        }
                    });
                ApplyCommittedSnapshot(
                    commit!,
                    knownChanges,
                    commitStaged,
                    selection);
                _commitEditSession = null;
                CommitMessage = "";
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Committed");
            },
            "FolderProject.VersionControl.Busy.Committing",
            RefreshMode.None,
            failureRefreshMode: RefreshMode.Full);
    }

    [RelayCommand(CanExecute = nameof(CanStageSelected))]
    private Task StageSelected()
    {
        return Stage(
            GetSelectedUnstagedChanges()
                .Where(IsUsableWorkingChange)
                .Select(change => change.RepositoryPath));
    }

    [RelayCommand(CanExecute = nameof(CanStageAll))]
    private Task StageAll()
    {
        return Stage(UnstagedChanges.Select(change => change.RepositoryPath));
    }

    [RelayCommand(CanExecute = nameof(CanStageChange))]
    private Task StageChange(FolderProjectWorkingChangeRow? change)
    {
        return Stage(change == null ? [] : [change.RepositoryPath]);
    }

    [RelayCommand(CanExecute = nameof(CanStageTreeNode))]
    private Task StageTreeNode(FolderProjectWorkingChangeTreeNode? node)
    {
        return Stage(
            node?.Changes
                .OrderBy(
                    change => change.RepositoryPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(change => change.RepositoryPath) ?? []);
    }

    [RelayCommand(CanExecute = nameof(CanUnstageSelected))]
    private Task UnstageSelected()
    {
        return Unstage(
            GetSelectedStagedChanges()
                .Where(IsUsableWorkingChange)
                .Select(change => change.RepositoryPath));
    }

    [RelayCommand(CanExecute = nameof(CanUnstageAll))]
    private Task UnstageAll()
    {
        return Unstage(StagedChanges.Select(change => change.RepositoryPath));
    }

    [RelayCommand(CanExecute = nameof(CanUnstageChange))]
    private Task UnstageChange(FolderProjectWorkingChangeRow? change)
    {
        return Unstage(change == null ? [] : [change.RepositoryPath]);
    }

    [RelayCommand(CanExecute = nameof(CanUnstageTreeNode))]
    private Task UnstageTreeNode(FolderProjectWorkingChangeTreeNode? node)
    {
        return Unstage(
            node?.Changes
                .OrderBy(
                    change => change.RepositoryPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(change => change.RepositoryPath) ?? []);
    }

    [RelayCommand(CanExecute = nameof(CanDiscardUnstaged))]
    private Task DiscardUnstaged()
    {
        return Discard(
            GetSelectedUnstagedChanges()
                .Where(IsUsableWorkingChange)
                .Select(change => change.RepositoryPath));
    }

    [RelayCommand(CanExecute = nameof(CanDiscardStaged))]
    private Task DiscardStaged()
    {
        return Discard(
            GetSelectedStagedChanges()
                .Where(IsUsableWorkingChange)
                .Select(change => change.RepositoryPath));
    }

    [RelayCommand(CanExecute = nameof(CanDiscardChange))]
    private Task DiscardChange(FolderProjectWorkingChangeRow? change)
    {
        return Discard(change == null ? [] : [change.RepositoryPath]);
    }

    [RelayCommand(CanExecute = nameof(CanDiscardTreeNode))]
    private Task DiscardTreeNode(FolderProjectWorkingChangeTreeNode? node)
    {
        return Discard(
            node?.Changes
                .OrderBy(
                    change => change.RepositoryPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(change => change.RepositoryPath) ?? []);
    }

    [RelayCommand(CanExecute = nameof(CanDiscardAll))]
    private Task DiscardAll()
    {
        return Discard(
            WorkingChanges
                .Where(IsUsableWorkingChange)
                .Select(change => change.RepositoryPath),
            "DiscardAllChanges");
    }

    private async Task Stage(IEnumerable<string> paths)
    {
        var selectedPaths = paths.Distinct().ToList();
        var servicePaths = ExpandWorkingChangePaths(selectedPaths);
        var selection = CurrentSelection();
        if (_unsavedChanges.HasUnsavedChanges(ProjectRoot, selectedPaths))
        {
            var choice = _unsavedChangesPrompt.Show(
                FolderProjectUnsavedChangesOperation.Stage);
            if (choice == FolderProjectUnsavedChangesChoice.Cancel)
                return;
            if (choice == FolderProjectUnsavedChangesChoice.Save &&
                !_unsavedChanges.SaveUnsavedChanges(
                    ProjectRoot,
                    selectedPaths))
            {
                return;
            }
        }

        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.StageChanges(
                        ProjectRoot,
                        servicePaths));
                ApplyStagingSnapshot(selectedPaths, stage: true, selection);
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Staged");
            },
            "FolderProject.VersionControl.Busy.UpdatingStage",
            RefreshMode.None,
            failureRefreshMode: RefreshMode.Full);
    }

    private async Task Unstage(IEnumerable<string> paths)
    {
        var selectedPaths = paths.Distinct().ToList();
        var servicePaths = ExpandWorkingChangePaths(selectedPaths);
        var selection = CurrentSelection();
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.UnstageChanges(
                        ProjectRoot,
                        servicePaths));
                ApplyStagingSnapshot(selectedPaths, stage: false, selection);
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Unstaged");
            },
            "FolderProject.VersionControl.Busy.UpdatingStage",
            RefreshMode.None,
            failureRefreshMode: RefreshMode.Full);
    }

    private async Task Discard(
        IEnumerable<string> paths,
        string confirmationKey = "DiscardChanges")
    {
        if (!Confirm(confirmationKey))
            return;

        var selectedPaths = paths.Distinct().ToList();
        var servicePaths = ExpandWorkingChangePaths(selectedPaths);
        var selection = CurrentSelection();
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () =>
                    {
                        _versionControlService.DiscardChanges(
                            ProjectRoot,
                            servicePaths);
                        return true;
                    });
                var discardedPaths = selectedPaths.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
                ApplyWorkingChanges(
                    WorkingChanges
                        .Select(change => change.Source)
                        .Where(change => !discardedPaths.Contains(
                            change.RepositoryPath))
                        .ToList(),
                    selection);
                return _localization.Get(
                    "FolderProject.VersionControl.Status.Discarded");
            },
            "FolderProject.VersionControl.Busy.Discarding",
            RefreshMode.None,
            failureRefreshMode: RefreshMode.Full);
    }

    [RelayCommand(CanExecute = nameof(CanRestoreStash))]
    private Task ApplyStash()
    {
        return RestoreSelectedStash(pop: false);
    }

    [RelayCommand(CanExecute = nameof(CanRestoreStash))]
    private Task PopStash()
    {
        return RestoreSelectedStash(pop: true);
    }

    private async Task RestoreSelectedStash(bool pop)
    {
        var index = SelectedStash!.Index;
        var succeeded = await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () =>
                    {
                        if (pop)
                            _versionControlService.PopStash(ProjectRoot, index);
                        else
                            _versionControlService.ApplyStash(ProjectRoot, index);
                        return true;
                    });
                return _localization.Get(
                    pop
                        ? "FolderProject.VersionControl.Status.StashPopped"
                        : "FolderProject.VersionControl.Status.StashApplied");
            },
            "FolderProject.VersionControl.Busy.RestoringStash");
        if (succeeded)
            SelectedTabIndex = 0;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteStash))]
    private async Task DeleteStash()
    {
        if (!Confirm("DeleteStash"))
            return;

        var index = SelectedStash!.Index;
        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.DeleteStash(
                        ProjectRoot,
                        index));
                var remainingStashes = Stashes
                    .Where(stash => stash.Index != index)
                    .Select(
                        stash => stash.Index > index
                            ? stash with { Index = stash.Index - 1 }
                            : stash)
                    .OrderBy(stash => stash.Index)
                    .ToList();
                Replace(Stashes, remainingStashes);
                SelectedStash = Stashes.FirstOrDefault(
                    stash => stash.Index == index) ??
                    Stashes.LastOrDefault();
                return _localization.Get(
                    "FolderProject.VersionControl.Status.StashDeleted");
            },
            "FolderProject.VersionControl.Busy.UpdatingStashes",
            RefreshMode.None,
            failureRefreshMode: RefreshMode.Full);
    }

    [RelayCommand(CanExecute = nameof(CanClearStashes))]
    private async Task ClearStashes()
    {
        if (!Confirm("ClearStashes"))
            return;

        await RunOperationAsync(
            async () =>
            {
                await Task.Run(
                    () => _versionControlService.ClearStashes(ProjectRoot));
                Stashes.Clear();
                SelectedStash = null;
                return _localization.Get(
                    "FolderProject.VersionControl.Status.StashesCleared");
            },
            "FolderProject.VersionControl.Busy.UpdatingStashes",
            RefreshMode.None,
            failureRefreshMode: RefreshMode.Full);
    }

    [RelayCommand(CanExecute = nameof(CanRestoreFile))]
    private async Task RestoreFile()
    {
        if (!Confirm("Restore"))
            return;

        var commit = SelectedCommit!;
        var requests = GetSelectedCommitChanges()
            .Where(IsRestorableCommitChange)
            .Select(
                row =>
                {
                    var change = row.Source;
                    return (
                        CommitId: change.Kind ==
                            FolderProjectCommitChangeKind.Deleted
                                ? commit.ParentIds[0]
                                : commit.Id,
                        change.RepositoryPath);
                })
            .ToList();
        var overwrite = requests.Any(
            request => WorkingChanges.Any(
                change => change.RepositoryPath.Equals(
                    request.RepositoryPath,
                    StringComparison.OrdinalIgnoreCase)));
        if (overwrite && !Confirm("RestoreOverwrite"))
            return;

        await RunOperationAsync(
            async () =>
            {
                try
                {
                    await RestoreSelectedFilesAsync(overwrite);
                }
                catch (FolderProjectVersionControlException exception)
                    when (!overwrite &&
                          exception.Code ==
                              FolderProjectVersionControlError
                                  .WorkingTreeNotClean)
                {
                    if (!Confirm("RestoreOverwrite"))
                    {
                        return _localization.Get(
                            "FolderProject.VersionControl.Status.Cancelled");
                    }

                    await RestoreSelectedFilesAsync(true);
                }

                return _localization.Get(
                    "FolderProject.VersionControl.Status.Restored");

                Task<bool> RestoreSelectedFilesAsync(bool overwriteFiles)
                {
                    return ExecuteCoordinatedAsync(
                        () =>
                        {
                            foreach (var request in requests)
                            {
                                _versionControlService.RestoreFile(
                                    ProjectRoot,
                                    request.CommitId,
                                    request.RepositoryPath,
                                    overwriteFiles);
                            }
                            return true;
                        });
                }
            },
            "FolderProject.VersionControl.Busy.Restoring");
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedCommitChanges))]
    private Task DiscardCommitChanges()
    {
        return EditLatestCommitChanges(
            FolderProjectCommitChangeEditMode.Discard,
            "DiscardCommitChanges",
            "FolderProject.VersionControl.Status.CommitChangesDiscarded");
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedCommitChanges))]
    private Task RestoreCommitChangesToStage()
    {
        return EditLatestCommitChanges(
            FolderProjectCommitChangeEditMode.StageForEdit,
            "RestoreCommitChangesToStage",
            "FolderProject.VersionControl.Status.CommitChangesStaged");
    }

    private async Task EditLatestCommitChanges(
        FolderProjectCommitChangeEditMode mode,
        string confirmationKey,
        string statusKey)
    {
        var commit = SelectedCommit!;
        if (!Confirm(confirmationKey, commit.Message))
            return;

        var paths = GetSelectedCommitChanges()
            .Where(IsRestorableCommitChange)
            .Select(change => change.RepositoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await RunOperationAsync(
            async () =>
            {
                var session = await ExecuteCoordinatedAsync(
                    () => _versionControlService.EditLatestCommitChanges(
                        ProjectRoot,
                        commit.Id,
                        paths,
                        mode));
                _commitEditSession = mode ==
                    FolderProjectCommitChangeEditMode.StageForEdit
                        ? session
                        : null;
                return _localization.Get(statusKey);
            },
            "FolderProject.VersionControl.Busy.EditingCommit");
        if (mode == FolderProjectCommitChangeEditMode.StageForEdit &&
            _commitEditSession != null)
        {
            SelectedTabIndex = 0;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetCommitChanges))]
    private Task ResetCommitChangesKeep(
        FolderProjectCommitChangeTreeNode? node)
    {
        return ResetCommitChanges(
            node,
            FolderProjectCommitChangeEditMode.KeepChanges,
            "ResetCommitChangesKeep",
            "FolderProject.VersionControl.Status.CommitChangesResetKeep");
    }

    [RelayCommand(CanExecute = nameof(CanResetCommitChanges))]
    private Task ResetCommitChangesAndDiscard(
        FolderProjectCommitChangeTreeNode? node)
    {
        return ResetCommitChanges(
            node,
            FolderProjectCommitChangeEditMode.Discard,
            "ResetCommitChangesAndDiscard",
            "FolderProject.VersionControl.Status.CommitChangesResetDiscard");
    }

    private async Task ResetCommitChanges(
        FolderProjectCommitChangeTreeNode? node,
        FolderProjectCommitChangeEditMode mode,
        string confirmationKey,
        string statusKey)
    {
        var commit = SelectedCommit!;
        if (!Confirm(confirmationKey, commit.Message))
            return;

        var paths = node!.Changes
            .Where(IsRestorableCommitChange)
            .Select(change => change.RepositoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.EditLatestCommitChanges(
                        ProjectRoot,
                        commit.Id,
                        paths,
                        mode));
                _commitEditSession = null;
                return _localization.Get(statusKey);
            },
            "FolderProject.VersionControl.Busy.ResettingCommitChanges");
    }

    [RelayCommand(CanExecute = nameof(CanRevertCommitChanges))]
    private async Task RevertCommitChanges(
        FolderProjectCommitChangeTreeNode? node)
    {
        var commit = SelectedCommit!;
        if (!Confirm("RevertCommitChanges", commit.Message))
            return;

        var paths = node!.Changes
            .Where(IsRestorableCommitChange)
            .Select(change => change.RepositoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.RevertCommitChanges(
                        ProjectRoot,
                        commit.Id,
                        paths));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.CommitChangesReverted");
            },
            "FolderProject.VersionControl.Busy.RevertingCommitChanges");
    }

    [RelayCommand(CanExecute = nameof(CanReturnChangesToOriginalCommit))]
    private async Task ReturnChangesToOriginalCommit()
    {
        if (!Confirm("ReturnChangesToOriginalCommit"))
            return;

        var session = _commitEditSession!;
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.CompleteLatestCommitEdit(
                        ProjectRoot,
                        session));
                _commitEditSession = null;
                return _localization.Get(
                    "FolderProject.VersionControl.Status.CommitChangesReturned");
            },
            "FolderProject.VersionControl.Busy.EditingCommit");
    }

    [RelayCommand(CanExecute = nameof(CanRevertCommit))]
    private async Task RevertCommit()
    {
        if (!Confirm("RevertCommit", SelectedCommit!.Message))
            return;

        var commitId = SelectedCommit.Id;
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.RevertCommit(
                        ProjectRoot,
                        commitId));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.CommitReverted");
            },
            "FolderProject.VersionControl.Busy.RevertingCommit");
    }

    [RelayCommand(CanExecute = nameof(CanResetCommit))]
    private Task ResetCommitKeepChanges()
    {
        return ResetCommit(
            FolderProjectCommitUndoMode.KeepChanges,
            "ResetCommitKeepChanges",
            "FolderProject.VersionControl.Status.CommitResetKeepChanges");
    }

    [RelayCommand(CanExecute = nameof(CanResetCommit))]
    private Task ResetCommitAndDiscardChanges()
    {
        return ResetCommit(
            FolderProjectCommitUndoMode.DiscardChanges,
            "ResetCommitAndDiscardChanges",
            "FolderProject.VersionControl.Status.CommitResetAndDiscarded");
    }

    private async Task ResetCommit(
        FolderProjectCommitUndoMode mode,
        string confirmationKey,
        string statusKey)
    {
        if (!Confirm(confirmationKey, SelectedCommit!.Message))
            return;

        var commitId = SelectedCommit.Id;
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () =>
                    {
                        _versionControlService.ResetToCommit(
                            ProjectRoot,
                            commitId,
                            mode);
                        return true;
                    });
                return _localization.Get(statusKey);
            },
            "FolderProject.VersionControl.Busy.ResettingCommit");
    }

    [RelayCommand(CanExecute = nameof(CanCreateRecoveryBranch))]
    private async Task CreateRecoveryBranch()
    {
        var branchName = PromptForText(
            "FolderProject.VersionControl.RecoveryBranchName",
            RecoveryBranchName);
        if (branchName == null)
            return;

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
        var branchName = PromptForText(
            "FolderProject.VersionControl.CreateBranch",
            BranchName);
        if (branchName == null)
            return;

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
        var newName = PromptForText(
            "FolderProject.VersionControl.RenameBranch",
            string.IsNullOrWhiteSpace(BranchName)
                ? oldName
                : BranchName);
        if (newName == null)
            return;

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

    [RelayCommand(CanExecute = nameof(CanCreateAndSwitchBranch))]
    private async Task CreateAndSwitchBranch()
    {
        var branchName = PromptForText(
            "FolderProject.VersionControl.CreateAndSwitchBranch");
        if (branchName == null)
            return;
        if (!IsClean &&
            _dialogs.ShowYesNoBox(
                _localization.Get(
                    "FolderProject.VersionControl.Confirm.CarryChangesToNewBranch"),
                _localization.Get(
                    "FolderProject.VersionControl.CreateAndSwitchBranch")) !=
            ShowMessageBoxResult.OK)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => CreateAndSwitchBranchCore(branchName));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.BranchCreatedAndSwitched");
            },
            "FolderProject.VersionControl.Busy.SwitchingBranch");
        SelectCurrentBranch();
    }

    private FolderProjectBranchInfo CreateAndSwitchBranchCore(
        string branchName)
    {
        _versionControlService.CreateBranch(ProjectRoot, branchName);
        try
        {
            return _versionControlService.SwitchBranch(
                ProjectRoot,
                branchName);
        }
        catch
        {
            _versionControlService.DeleteBranch(ProjectRoot, branchName);
            throw;
        }
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
        var branchName = SelectedBranch!.Name;
        if (!IsClean)
        {
            PendingBranchName = branchName;
            IsBranchSwitchChoiceOpen = true;
            NotifyCommands();
            return;
        }

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
        SelectCurrentBranch();
    }

    [RelayCommand(CanExecute = nameof(CanCompleteBranchSwitchChoice))]
    private Task CarryChangesAndSwitch()
    {
        return CompleteBranchSwitchChoice(
            FolderProjectBranchSwitchMode.CarryChanges);
    }

    [RelayCommand(CanExecute = nameof(CanCompleteBranchSwitchChoice))]
    private Task StashChangesAndSwitch()
    {
        return CompleteBranchSwitchChoice(
            FolderProjectBranchSwitchMode.StashChanges);
    }

    [RelayCommand(CanExecute = nameof(CanCompleteBranchSwitchChoice))]
    private Task DiscardChangesAndSwitch()
    {
        return CompleteBranchSwitchChoice(
            FolderProjectBranchSwitchMode.DiscardChanges);
    }

    [RelayCommand(CanExecute = nameof(CanCompleteBranchSwitchChoice))]
    private void CancelBranchSwitch()
    {
        IsBranchSwitchChoiceOpen = false;
        PendingBranchName = "";
        SelectCurrentBranch();
        NotifyCommands();
    }

    private async Task CompleteBranchSwitchChoice(
        FolderProjectBranchSwitchMode mode)
    {
        var branchName = PendingBranchName;
        IsBranchSwitchChoiceOpen = false;
        PendingBranchName = "";
        NotifyCommands();
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () => _versionControlService.SwitchBranch(
                        ProjectRoot,
                        branchName,
                        mode,
                        mode == FolderProjectBranchSwitchMode.StashChanges
                            ? $"WIP on {CurrentBranch}"
                            : null));
                return _localization.Get(
                    "FolderProject.VersionControl.Status.BranchSwitched");
            },
            "FolderProject.VersionControl.Busy.SwitchingBranch");
        SelectCurrentBranch();
    }

    [RelayCommand(CanExecute = nameof(CanPrepareMerge))]
    private void PrepareMerge()
    {
        SelectedMergeSource = MergeSources.FirstOrDefault(
            branch => branch.Name == SelectedBranch!.Name);
        SelectedMergeTarget = GetDefaultMergeTarget(
            SelectedMergeSource);
        IsMergeSectionExpanded = true;
        _suppressRepositoryTabHistoryLoad = true;
        try
        {
            SelectedTabIndex = 1;
        }
        finally
        {
            _suppressRepositoryTabHistoryLoad = false;
        }
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
                ReportVersionControlProgress(
                    new FolderProjectVersionControlProgress(
                        FolderProjectVersionControlProgressStage.PreparingMerge,
                        $"{sourceBranch} → {targetBranch}"));
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
                sourceBranch,
                ReportVersionControlProgress);
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
        var message = PromptForCommitMessage(MergeMessage);
        if (message == null)
            return;

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
        var conflictIds = GetSelectedMergeConflicts()
            .Select(conflict => conflict.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        await RunOperationAsync(
            async () =>
            {
                await ExecuteCoordinatedAsync(
                    () =>
                    {
                        FolderProjectMergeState? state = null;
                        foreach (var conflictId in conflictIds)
                        {
                            state = _versionControlService
                                .ResolveMergeConflict(
                                    ProjectRoot,
                                    conflictId,
                                    choice);
                        }
                        return state!;
                    });
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
        RefreshMode refreshMode = RefreshMode.Full,
        RefreshMode? failureRefreshMode = null)
    {
        if (IsBusy)
            return false;

        var succeeded = false;
        BusyMessage = _localization.Get(busyMessageKey);
        BeginLoadingProgress(BusyMessage);
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
            await RefreshAfterOperationAsync(
                succeeded
                    ? refreshMode
                    : failureRefreshMode ?? refreshMode);
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
        var includeHistory = SelectedTabIndex == 1 || _hasHistorySnapshot;
        var snapshot = await Task.Run(
            () => CaptureRefreshSnapshot(
                selection.SelectedCommitId,
                selection.SelectedHistoryBranchName,
                includeHistory));
        ApplyRefreshSnapshot(snapshot, selection);
    }

    private FolderProjectRefreshSnapshot CaptureRefreshSnapshot(
        string? selectedCommitId,
        string? selectedHistoryBranchName,
        bool includeHistory)
    {
        var status = _versionControlService.GetStatus(
            ProjectRoot,
            ReportVersionControlProgress);
        if (!status.IsInitialized)
        {
            return new FolderProjectRefreshSnapshot(
                status,
                null,
                [],
                [],
                [],
                null,
                null,
                [],
                includeHistory);
        }

        FolderProjectGitIdentity? identity = null;
        ReportVersionControlProgress(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ReadingIdentity,
            ProjectRoot));
        try
        {
            identity = _versionControlService.GetIdentity(ProjectRoot);
        }
        catch (FolderProjectVersionControlException exception)
            when (exception.Code ==
                  FolderProjectVersionControlError.IdentityMissing)
        {
        }
        ReportCompletedProgress(
            FolderProjectVersionControlProgressStage.ReadingIdentity,
            ProjectRoot);

        ReportVersionControlProgress(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ReadingBranches,
            ProjectRoot));
        var branches = _versionControlService.GetBranches(ProjectRoot);
        ReportCompletedProgress(
            FolderProjectVersionControlProgressStage.ReadingBranches,
            ProjectRoot);
        ReportVersionControlProgress(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ReadingStashes,
            ProjectRoot));
        var stashes = _versionControlService.GetStashes(ProjectRoot);
        ReportCompletedProgress(
            FolderProjectVersionControlProgressStage.ReadingStashes,
            ProjectRoot);
        var historyBranchName =
            branches.Any(
                branch => branch.Name == selectedHistoryBranchName)
                ? selectedHistoryBranchName!
                : status.CurrentBranch ??
                  branches.FirstOrDefault()?.Name;
        IReadOnlyList<FolderProjectCommitSummary> history = [];
        if (includeHistory && historyBranchName != null)
        {
            ReportVersionControlProgress(
                new FolderProjectVersionControlProgress(
                    FolderProjectVersionControlProgressStage.ReadingHistory,
                    historyBranchName));
            history = _versionControlService.GetHistory(
                ProjectRoot,
                historyBranchName,
                100);
            ReportCompletedProgress(
                FolderProjectVersionControlProgressStage.ReadingHistory,
                historyBranchName);
        }
        var selectedCommit =
            history.FirstOrDefault(
                commit => commit.Id == selectedCommitId) ??
            history.FirstOrDefault();
        IReadOnlyList<FolderProjectCommitChange> commitChanges = [];
        if (includeHistory && selectedCommit != null)
        {
            if (!TryGetCachedCommitChanges(
                    selectedCommit.Id,
                    out commitChanges))
            {
                commitChanges = _versionControlService.GetCommitChanges(
                    ProjectRoot,
                    selectedCommit.Id,
                    ReportVersionControlProgress);
                CacheCommitChanges(selectedCommit.Id, commitChanges);
            }
        }
        ReportVersionControlProgress(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ReadingMergeState,
            ProjectRoot));
        var mergeState = _versionControlService.GetMergeState(ProjectRoot);
        ReportCompletedProgress(
            FolderProjectVersionControlProgressStage.ReadingMergeState,
            ProjectRoot);
        return new FolderProjectRefreshSnapshot(
            status,
            identity,
            history,
            branches,
            stashes,
            historyBranchName,
            mergeState,
            commitChanges,
            includeHistory);
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
            ApplyWorkingChanges(status.Changes, selection);

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
            Replace(Stashes, snapshot.Stashes);
            SelectedStash = selection.SelectedStashIndex == null
                ? null
                : Stashes.FirstOrDefault(
                    stash => stash.Index == selection.SelectedStashIndex);
            if (snapshot.IncludesHistory)
            {
                _hasHistorySnapshot = true;
                Replace(History, snapshot.History);
                SelectedCommit =
                    History.FirstOrDefault(
                        commit =>
                            commit.Id == selection.SelectedCommitId) ??
                    History.FirstOrDefault();
                ApplyCommitChanges(
                    snapshot.CommitChanges,
                    selection.SelectedCommitChangePath);
            }
            ApplyMergeState(
                snapshot.MergeState!,
                selection.SelectedConflictId,
                selection.SelectedMergeSourceName,
                selection.SelectedMergeTargetName);
            _requestedMergeSourceBranchName = null;
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
        ReportVersionControlProgress(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ReadingBranches,
            ProjectRoot));
        var branches = await Task.Run(
            () => _versionControlService.GetBranches(ProjectRoot));
        ReportCompletedProgress(
            FolderProjectVersionControlProgressStage.ReadingBranches,
            ProjectRoot);
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
                             branch =>
                                 branch.Name == selectedBranchName) ??
                         Branches.FirstOrDefault(
                             branch => branch.IsCurrent);
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
        SelectedMergeConflicts = [];
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
        lock (_commitChangesCacheGate)
            _commitChangesCache.Clear();
        CurrentBranch = "";
        HeadCommitId = "";
        IsDetached = false;
        OperationState = FolderProjectRepositoryOperationState.None;
        IsClean = true;
        HasIdentity = false;
        WorkingChanges.Clear();
        UnstagedChanges.Clear();
        StagedChanges.Clear();
        UnstagedChangeTree.Clear();
        StagedChangeTree.Clear();
        Stashes.Clear();
        History.Clear();
        _hasHistorySnapshot = false;
        CommitChanges.Clear();
        CommitChangeTree.Clear();
        Branches.Clear();
        MergeSources.Clear();
        MergeTargets.Clear();
        MergeConflicts.Clear();
        SelectedCommit = null;
        SelectedCommitChange = null;
        SelectedCommitChangeTreeNode = null;
        SelectedCommitChanges = [];
        SelectedUnstagedChange = null;
        SelectedStagedChange = null;
        SelectedUnstagedChanges = [];
        SelectedStagedChanges = [];
        SelectedHistoryBranch = null;
        SelectedBranch = null;
        SelectedStash = null;
        SelectedMergeSource = null;
        SelectedMergeTarget = null;
        SelectedMergeConflict = null;
        SelectedMergeConflicts = [];
        _commitEditSession = null;
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
        Replace(
            CommitChangeTree,
            FolderProjectCommitChangeTreeNode.Build(
                ProjectRoot,
                CommitChanges));
        SelectedCommitChanges = [];
        SelectedCommitChange = CommitChanges.FirstOrDefault(
            change => change.RepositoryPath == selectedPath);
        SelectedCommitChangeTreeNode = null;
    }

    private async Task LoadSelectedCommitChangesAsync(
        FolderProjectCommitSummary? commit)
    {
        var requestId = Interlocked.Increment(
            ref _commitChangesRequestId);
        if (commit == null)
        {
            IsCommitChangesLoading = false;
            ApplyCommitChanges([], null);
            return;
        }

        if (TryGetCachedCommitChanges(commit.Id, out var cachedChanges))
        {
            IsCommitChangesLoading = false;
            ApplyCommitChanges(
                cachedChanges,
                SelectedCommitChange?.RepositoryPath);
            return;
        }

        var selectedPath = SelectedCommitChange?.RepositoryPath;
        ApplyCommitChanges([], null);
        BeginLoadingProgress(_localization.Get(
            "FolderProject.VersionControl.Busy.LoadingCommit"));
        IsCommitChangesLoading = true;
        try
        {
            var changes = await Task.Run(
                () => _versionControlService.GetCommitChanges(
                    ProjectRoot,
                    commit.Id,
                    ReportVersionControlProgress));
            CacheCommitChanges(commit.Id, changes);
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
                IsCommitChangesLoading = false;
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
        BeginLoadingProgress(BusyMessage);
        IsBusy = true;
        try
        {
            ReportVersionControlProgress(
                new FolderProjectVersionControlProgress(
                    FolderProjectVersionControlProgressStage.ReadingHistory,
                    branch.Name));
            var history = await Task.Run(
                () => _versionControlService.GetHistory(
                    ProjectRoot,
                    branch.Name,
                    100));
            ReportCompletedProgress(
                FolderProjectVersionControlProgressStage.ReadingHistory,
                branch.Name);
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
            SelectedUnstagedChange?.RepositoryPath,
            SelectedStagedChange?.RepositoryPath,
            SelectedCommit?.Id,
            SelectedCommitChange?.RepositoryPath,
            SelectedHistoryBranch?.Name,
            SelectedBranch?.Name,
            _requestedMergeSourceBranchName ??
                SelectedMergeSource?.Name,
            SelectedMergeTarget?.Name,
            SelectedMergeConflict?.Id,
            SelectedStash?.Index);
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

    private string? PromptForText(
        string titleKey,
        string initialText = "")
    {
        var result = _dialogs.ShowTextInputDialog(
            _localization.Get(titleKey),
            initialText);
        if (!result.Result)
            return null;

        var text = result.Text.Trim();
        return text.Length == 0 ? null : text;
    }

    private string? PromptForCommitMessage(string initialTitle)
    {
        var result = _dialogs.ShowTitleDescriptionInputDialog(
            _localization.Get(
                "FolderProject.VersionControl.CommitDialogTitle"),
            _localization.Get(
                "FolderProject.VersionControl.CommitTitle"),
            _localization.Get(
                "FolderProject.VersionControl.CommitDescription"),
            initialTitle,
            "");
        if (!result.Result)
            return null;

        var title = result.Title.Trim();
        if (title.Length == 0)
            return null;

        var description = result.Description.Trim();
        return description.Length == 0
            ? title
            : $"{title}\n\n{description}";
    }

    private void SelectCurrentBranch()
    {
        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
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
        !IsBusy &&
        !IsStatusRefreshing &&
        !string.IsNullOrWhiteSpace(ProjectRoot);

    private bool CanInitialize() =>
        CanRefresh() &&
        !IsInitialized &&
        !string.IsNullOrWhiteSpace(PrimaryBranchName);

    private bool CanSaveIdentity() =>
        CanUseRepository();

    private bool CanCommit() =>
        HasStagedChanges ? CanCommitStaged() : CanCommitAll();

    private bool CanCommitStaged() =>
        CanUseRepository() &&
        HasIdentity &&
        HasStagedChanges &&
        !string.IsNullOrWhiteSpace(CommitMessage);

    private bool CanCommitAll() =>
        CanUseRepository() &&
        HasIdentity &&
        (WorkingChanges.Count != 0 ||
         _unsavedChanges.HasUnsavedChanges(ProjectRoot, null)) &&
        !string.IsNullOrWhiteSpace(CommitMessage);

    private bool CanStageSelected() =>
        CanUseRepository() &&
        GetSelectedUnstagedChanges().Any(IsUsableWorkingChange);

    private bool CanStageAll() =>
        CanUseRepository() &&
        UnstagedChanges.Any(IsUsableWorkingChange);

    private bool CanStageChange(FolderProjectWorkingChangeRow? change) =>
        CanUseRepository() &&
        change != null &&
        UnstagedChanges.Contains(change) &&
        IsUsableWorkingChange(change);

    private bool CanStageTreeNode(
        FolderProjectWorkingChangeTreeNode? node) =>
        CanUseRepository() &&
        node is { IsStagedTree: false } &&
        node.Changes.Any(IsUsableWorkingChange);

    private bool CanUnstageSelected() =>
        CanUseRepository() &&
        GetSelectedStagedChanges().Any(IsUsableWorkingChange);

    private bool CanUnstageAll() =>
        CanUseRepository() &&
        StagedChanges.Any(IsUsableWorkingChange);

    private bool CanUnstageChange(FolderProjectWorkingChangeRow? change) =>
        CanUseRepository() &&
        change != null &&
        StagedChanges.Contains(change) &&
        IsUsableWorkingChange(change);

    private bool CanUnstageTreeNode(
        FolderProjectWorkingChangeTreeNode? node) =>
        CanUseRepository() &&
        node is { IsStagedTree: true } &&
        node.Changes.Any(IsUsableWorkingChange);

    private bool CanDiscardUnstaged() =>
        CanUseRepository() &&
        GetSelectedUnstagedChanges().Any(IsUsableWorkingChange);

    private bool CanDiscardStaged() =>
        CanUseRepository() &&
        GetSelectedStagedChanges().Any(IsUsableWorkingChange);

    private bool CanDiscardChange(FolderProjectWorkingChangeRow? change) =>
        CanUseRepository() &&
        change != null &&
        WorkingChanges.Contains(change) &&
        IsUsableWorkingChange(change);

    private bool CanDiscardTreeNode(
        FolderProjectWorkingChangeTreeNode? node) =>
        CanUseRepository() &&
        node != null &&
        node.Changes.Any(IsUsableWorkingChange);

    private bool CanDiscardAll() =>
        CanUseRepository() &&
        WorkingChanges.Count != 0 &&
        WorkingChanges.All(IsUsableWorkingChange);

    private bool CanRestoreStash() =>
        CanUseRepository() &&
        IsClean &&
        SelectedStash != null;

    private bool CanDeleteStash() =>
        CanUseRepository() && SelectedStash != null;

    private bool CanClearStashes() =>
        CanUseRepository() && Stashes.Count != 0;

    private IReadOnlyList<FolderProjectWorkingChangeRow>
        GetSelectedUnstagedChanges()
    {
        return SelectedUnstagedChanges.Count != 0
            ? SelectedUnstagedChanges
            : SelectedUnstagedChange == null
                ? []
                : [SelectedUnstagedChange];
    }

    private IReadOnlyList<FolderProjectWorkingChangeRow>
        GetSelectedStagedChanges()
    {
        return SelectedStagedChanges.Count != 0
            ? SelectedStagedChanges
            : SelectedStagedChange == null
                ? []
                : [SelectedStagedChange];
    }

    private static bool IsUsableWorkingChange(
        FolderProjectWorkingChangeRow? change)
    {
        return change != null &&
               !change.Source.Kind.HasFlag(
                   FolderProjectWorkingChangeKind.Conflicted) &&
               !change.Source.Kind.HasFlag(
                   FolderProjectWorkingChangeKind.Unreadable);
    }

    private bool CanRestoreFile()
    {
        return CanUseRepository() &&
               SelectedHistoryBranch is { IsCurrent: true } &&
               SelectedCommit is { ParentIds.Count: > 0 } &&
               GetSelectedCommitChanges().Any(IsRestorableCommitChange);
    }

    private bool CanEditSelectedCommitChanges()
    {
        return CanUseRepository() &&
               IsClean &&
               SelectedHistoryBranch is { IsCurrent: true } &&
               SelectedCommit is { ParentIds.Count: 1 } &&
               SelectedCommit.Id == HeadCommitId &&
               GetSelectedCommitChanges().Any(IsRestorableCommitChange);
    }

    private bool CanResetCommitChanges(
        FolderProjectCommitChangeTreeNode? node)
    {
        return CanUseRepository() &&
               IsClean &&
               !IsDetached &&
               SelectedHistoryBranch is { IsCurrent: true } &&
               SelectedCommit is { ParentIds.Count: 1 } &&
               SelectedCommit.Id == HeadCommitId &&
               node != null &&
               node.Changes.Any(IsRestorableCommitChange);
    }

    private bool CanRevertCommitChanges(
        FolderProjectCommitChangeTreeNode? node)
    {
        return CanUseRepository() &&
               IsClean &&
               !IsDetached &&
               SelectedHistoryBranch is { IsCurrent: true } &&
               SelectedCommit is { ParentIds.Count: 1 } &&
               node != null &&
               node.Changes.Any(IsRestorableCommitChange);
    }

    private bool CanReturnChangesToOriginalCommit()
    {
        return CanUseRepository() &&
               _commitEditSession is
               {
                   CanReturnToOriginalCommit: true,
               } session &&
               session.ExpectedHeadCommitId == HeadCommitId &&
               StagedChanges.Any(
                   change => session.RepositoryPaths.Contains(
                       change.RepositoryPath,
                       StringComparer.OrdinalIgnoreCase));
    }

    private IReadOnlyList<FolderProjectCommitChangeRow>
        GetSelectedCommitChanges()
    {
        return SelectedCommitChanges.Count != 0
            ? SelectedCommitChanges
            : SelectedCommitChange == null
                ? []
                : [SelectedCommitChange];
    }

    private static bool IsRestorableCommitChange(
        FolderProjectCommitChangeRow change)
    {
        return change.Source.Kind is
            FolderProjectCommitChangeKind.Added or
            FolderProjectCommitChangeKind.Modified or
            FolderProjectCommitChangeKind.Deleted or
            FolderProjectCommitChangeKind.Renamed;
    }

    private bool CanRevertCommit() =>
        CanUseRepository() &&
        IsClean &&
        !IsDetached &&
        SelectedHistoryBranch is { IsCurrent: true } &&
        SelectedCommit?.ParentIds.Count == 1;

    private bool CanResetCommit() =>
        CanUseRepository() &&
        IsClean &&
        !IsDetached &&
        SelectedHistoryBranch is { IsCurrent: true } &&
        SelectedCommit != null &&
        !string.Equals(
            SelectedCommit.Id,
            HeadCommitId,
            StringComparison.Ordinal);

    private bool CanCreateRecoveryBranch() =>
        IsInitialized &&
        !IsBusy &&
        SelectedCommit != null;

    private bool CanCreateBranch() =>
        CanUseRepository();

    private bool CanRenameBranch() =>
        CanUseRepository() && SelectedBranch is { IsPrimary: false };

    private bool CanCreateAndSwitchBranch() =>
        CanUseRepository() &&
        !IsDetached;

    private bool CanDeleteBranch() =>
        CanUseRepository() &&
        SelectedBranch is { IsPrimary: false };

    private bool CanSwitchBranch() =>
        CanUseRepository() &&
        !IsDetached &&
        SelectedBranch is { IsCurrent: false };

    private bool CanCompleteBranchSwitchChoice() =>
        CanUseRepository() &&
        IsBranchSwitchChoiceOpen &&
        !string.IsNullOrWhiteSpace(PendingBranchName);

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
        GetSelectedMergeConflicts().Count != 0;

    private IReadOnlyList<FolderProjectMergeConflictRow>
        GetSelectedMergeConflicts()
    {
        return SelectedMergeConflicts.Count != 0
            ? SelectedMergeConflicts
            : SelectedMergeConflict == null
                ? []
                : [SelectedMergeConflict];
    }

    private bool CanCompleteMerge() =>
        IsInitialized &&
        !IsBusy &&
        HasIdentity &&
        MergePhase == FolderProjectMergePhase.ReadyToCommit;

    private bool CanAbortMerge() =>
        IsInitialized &&
        !IsBusy &&
        MergePhase != FolderProjectMergePhase.None;

    private bool CanUseRepository() =>
        IsInitialized &&
        !IsBusy &&
        !IsStatusRefreshing &&
        MergePhase == FolderProjectMergePhase.None &&
        OperationState == FolderProjectRepositoryOperationState.None;

    partial void OnIsBusyChanged(bool value)
    {
        if (value)
            BeginLoadingProgress(BusyMessage);
        NotifyLoadingOperationChanged();
        NotifyCommands();
    }

    private void ApplyWorkingChanges(
        IReadOnlyList<FolderProjectWorkingChange> changes,
        FolderProjectSelection selection)
    {
        Interlocked.Increment(ref _workingChangesRevision);
        var workingRows = changes
            .Select(
                change => new FolderProjectWorkingChangeRow(
                    change,
                    _localization))
            .ToList();
        Replace(WorkingChanges, workingRows);
        var unstagedRows = workingRows.Where(
                change => change.Source.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Unstaged))
            .ToList();
        var stagedRows = workingRows.Where(
                change => change.Source.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged))
            .ToList();
        Replace(UnstagedChanges, unstagedRows);
        Replace(StagedChanges, stagedRows);
        Replace(
            UnstagedChangeTree,
            FolderProjectWorkingChangeTreeNode.Build(
                ProjectRoot,
                unstagedRows));
        Replace(
            StagedChangeTree,
            FolderProjectWorkingChangeTreeNode.Build(
                ProjectRoot,
                stagedRows,
                isStagedTree: true));
        IsClean = workingRows.Count == 0;
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(CommitActionText));
        SelectedUnstagedChanges = [];
        SelectedStagedChanges = [];
        SelectedUnstagedChange = UnstagedChanges.FirstOrDefault(
            change =>
                string.Equals(
                    change.RepositoryPath,
                    selection.SelectedUnstagedChangePath,
                    StringComparison.OrdinalIgnoreCase)) ??
            UnstagedChanges.FirstOrDefault();
        SelectedStagedChange = StagedChanges.FirstOrDefault(
            change =>
                string.Equals(
                    change.RepositoryPath,
                    selection.SelectedStagedChangePath,
                    StringComparison.OrdinalIgnoreCase)) ??
            StagedChanges.FirstOrDefault();
    }

    private void ApplyStagingSnapshot(
        IReadOnlyCollection<string> paths,
        bool stage,
        FolderProjectSelection selection)
    {
        var selectedPaths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changes = WorkingChanges
            .Select(change => change.Source)
            .Select(
                change => selectedPaths.Contains(change.RepositoryPath)
                    ? change with
                    {
                        Kind = stage
                            ? (change.Kind |
                               FolderProjectWorkingChangeKind.Staged) &
                              ~FolderProjectWorkingChangeKind.Unstaged
                            : (change.Kind |
                               FolderProjectWorkingChangeKind.Unstaged) &
                              ~FolderProjectWorkingChangeKind.Staged,
                    }
                    : change)
            .ToList();
        ApplyWorkingChanges(changes, selection);
    }

    private IReadOnlyList<string> ExpandWorkingChangePaths(
        IReadOnlyCollection<string> repositoryPaths)
    {
        var selectedPaths = repositoryPaths.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        return repositoryPaths
            .Concat(
                WorkingChanges
                    .Select(change => change.Source)
                    .Where(change => selectedPaths.Contains(
                        change.RepositoryPath))
                    .Select(change => change.PreviousRepositoryPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyCommittedSnapshot(
        FolderProjectCommitSummary commit,
        IReadOnlyList<FolderProjectWorkingChange> previousChanges,
        bool commitStaged,
        FolderProjectSelection selection)
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        try
        {
            var remainingChanges = commitStaged
                ? previousChanges
                    .Where(change => change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Unstaged))
                    .Select(
                        change => change with
                        {
                            Kind = change.Kind &
                                   ~FolderProjectWorkingChangeKind.Staged,
                        })
                    .ToList()
                : [];
            ApplyWorkingChanges(remainingChanges, selection);
            HeadCommitId = commit.Id;
            var currentBranch = Branches.FirstOrDefault(
                branch => branch.IsCurrent);
            if (currentBranch != null)
            {
                var updatedBranches = Branches
                    .Select(
                        branch => branch.IsCurrent
                            ? branch with { TipCommitId = commit.Id }
                            : branch)
                    .ToList();
                ApplyBranches(
                    updatedBranches,
                    SelectedBranch?.Name,
                    SelectedMergeSource?.Name,
                    SelectedMergeTarget?.Name,
                    SelectedHistoryBranch?.Name);
            }

            var updatedHistory = History
                .Where(item => item.Id != commit.Id)
                .Prepend(commit)
                .ToList();
            Replace(History, updatedHistory);
            SelectedCommit = commit;
            ApplyCommitChanges([], null);
        }
        finally
        {
            _refreshing = wasRefreshing;
        }
    }

    private bool TryGetCachedCommitChanges(
        string commitId,
        out IReadOnlyList<FolderProjectCommitChange> changes)
    {
        lock (_commitChangesCacheGate)
            return _commitChangesCache.TryGetValue(commitId, out changes!);
    }

    private void CacheCommitChanges(
        string commitId,
        IReadOnlyList<FolderProjectCommitChange> changes)
    {
        lock (_commitChangesCacheGate)
            _commitChangesCache[commitId] = changes;
    }

    public async Task RefreshWorkingChanges(
        IReadOnlyCollection<string> repositoryPaths)
    {
        if (repositoryPaths.Count == 0 ||
            !HasRepositorySnapshot ||
            IsBusy ||
            IsStatusRefreshing)
        {
            return;
        }

        var normalizedPaths = repositoryPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/').TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedPaths.Count == 0)
            return;

        var projectRoot = ProjectRoot;
        var fallbackToFullRefresh = false;
        await _workingChangesRefreshGate.WaitAsync();
        try
        {
            if (!HasRepositorySnapshot ||
                IsBusy ||
                IsStatusRefreshing)
            {
                return;
            }

            var selection = CurrentSelection();
            var workingChangesRevision = Volatile.Read(
                ref _workingChangesRevision);
            var status = await Task.Run(
                () => _versionControlService.GetStatus(
                    projectRoot,
                    normalizedPaths));
            if (!string.Equals(
                    projectRoot,
                    ProjectRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (workingChangesRevision != Volatile.Read(
                    ref _workingChangesRevision))
            {
                return;
            }

            var refreshedPaths = normalizedPaths.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            var mergedChanges = WorkingChanges
                .Select(change => change.Source)
                .Where(change => !refreshedPaths.Contains(
                    change.RepositoryPath))
                .Concat(status.Changes)
                .GroupBy(
                    change => change.RepositoryPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(
                    change => change.RepositoryPath,
                    StringComparer.Ordinal)
                .ToList();
            ApplyWorkingChanges(mergedChanges, selection);
        }
        catch (Exception exception)
        {
            if (string.Equals(
                    projectRoot,
                    ProjectRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                HasRepositorySnapshot = false;
                fallbackToFullRefresh = true;
            }
            _logger.Error(
                exception,
                "Folder-project incremental status refresh failed.");
        }
        finally
        {
            _workingChangesRefreshGate.Release();
        }

        if (fallbackToFullRefresh)
            await Refresh();
    }
    partial void OnIsStatusRefreshingChanged(bool value)
    {
        if (value)
        {
            BeginLoadingProgress(_localization.Get(
                "FolderProject.VersionControl.Busy.Refreshing"));
        }
        NotifyLoadingOperationChanged();
        NotifyCommands();
    }
    partial void OnIsCommitChangesLoadingChanged(bool value)
    {
        if (value)
        {
            BeginLoadingProgress(_localization.Get(
                "FolderProject.VersionControl.Busy.LoadingCommit"));
        }
        NotifyLoadingOperationChanged();
    }
    partial void OnBusyMessageChanged(string value) =>
        OnPropertyChanged(nameof(LoadingOperationMessage));
    partial void OnIsInitializedChanged(bool value) => NotifyCommands();
    partial void OnIsCleanChanged(bool value)
    {
        OnPropertyChanged(nameof(BranchActionHint));
        NotifyCommands();
    }
    partial void OnIsDetachedChanged(bool value) => NotifyCommands();
    partial void OnCurrentBranchChanged(string value) =>
        OnPropertyChanged(nameof(BranchActionHint));
    partial void OnIsBranchSwitchChoiceOpenChanged(bool value) =>
        NotifyCommands();
    partial void OnPendingBranchNameChanged(string value) => NotifyCommands();
    partial void OnHasIdentityChanged(bool value) => NotifyCommands();
    partial void OnOperationStateChanged(
        FolderProjectRepositoryOperationState value) =>
        NotifyCommands();
    partial void OnIdentityNameChanged(string value) => NotifyCommands();
    partial void OnIdentityEmailChanged(string value) => NotifyCommands();
    partial void OnPrimaryBranchNameChanged(string value) => NotifyCommands();
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value != 1 ||
            _refreshing ||
            IsBusy ||
            _suppressRepositoryTabHistoryLoad)
        {
            return;
        }

        if (SelectedCommit != null)
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
    partial void OnSelectedCommitChangeTreeNodeChanged(
        FolderProjectCommitChangeTreeNode? value)
    {
        SelectedCommitChange = value?.Change;
        NotifyCommands();
    }
    partial void OnSelectedCommitChangesChanged(
        IReadOnlyList<FolderProjectCommitChangeRow> value) =>
        NotifyCommands();
    partial void OnSelectedUnstagedChangeChanged(
        FolderProjectWorkingChangeRow? value) =>
        NotifyCommands();
    partial void OnSelectedStagedChangeChanged(
        FolderProjectWorkingChangeRow? value) =>
        NotifyCommands();
    partial void OnSelectedUnstagedChangesChanged(
        IReadOnlyList<FolderProjectWorkingChangeRow> value) =>
        NotifyCommands();
    partial void OnSelectedStagedChangesChanged(
        IReadOnlyList<FolderProjectWorkingChangeRow> value) =>
        NotifyCommands();

    private void BeginLoadingProgress(string status)
    {
        lock (_progressUpdateGate)
            _pendingProgressUpdate = null;

        LoadingProgressStatusText = status;
        LoadingProgressDetailText = "";
        LoadingProgressValue = 0;
        LoadingProgressMaximum = 1;
        LoadingProgressIsIndeterminate = true;
    }

    private void ReportCompletedProgress(
        FolderProjectVersionControlProgressStage stage,
        string? detail)
    {
        ReportVersionControlProgress(
            new FolderProjectVersionControlProgress(
                stage,
                detail,
                1,
                1));
    }

    private void ReportVersionControlProgress(
        FolderProjectVersionControlProgress progress)
    {
        if (_synchronizationContext == null ||
            ReferenceEquals(
                SynchronizationContext.Current,
                _synchronizationContext))
        {
            ApplyVersionControlProgress(progress);
            return;
        }

        lock (_progressUpdateGate)
        {
            _pendingProgressUpdate = progress;
            if (_progressUpdateScheduled)
                return;

            _progressUpdateScheduled = true;
        }

        _synchronizationContext.Post(
            _ => FlushVersionControlProgress(),
            null);
    }

    private void FlushVersionControlProgress()
    {
        FolderProjectVersionControlProgress? progress;
        lock (_progressUpdateGate)
        {
            progress = _pendingProgressUpdate;
            _pendingProgressUpdate = null;
            _progressUpdateScheduled = false;
        }

        if (progress != null)
            ApplyVersionControlProgress(progress);
    }

    private void ApplyVersionControlProgress(
        FolderProjectVersionControlProgress progress)
    {
        LoadingProgressStatusText = _localization.Get(
            $"FolderProject.VersionControl.Progress.{progress.Stage}");
        LoadingProgressDetailText = GetProgressDetail(progress);
        LoadingProgressIsIndeterminate = progress.Total <= 0;
        LoadingProgressMaximum = Math.Max(1, progress.Total);
        LoadingProgressValue = Math.Clamp(
            progress.Completed,
            0,
            LoadingProgressMaximum);
    }

    private string GetProgressDetail(
        FolderProjectVersionControlProgress progress)
    {
        if (progress.Stage !=
                FolderProjectVersionControlProgressStage
                    .ReadingCommitChanges ||
            string.IsNullOrEmpty(progress.Detail))
        {
            return progress.Detail ?? "";
        }

        var shortCommitId = progress.Detail.Length > 7
            ? progress.Detail[..7]
            : progress.Detail;
        return _localization.GetFormat(
            "FolderProject.VersionControl.Progress." +
            "ReadingCommitChangesDetail",
            shortCommitId);
    }

    private void NotifyLoadingOperationChanged()
    {
        OnPropertyChanged(nameof(IsLoadingOperation));
        OnPropertyChanged(nameof(LoadingOperationMessage));
    }
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
        if (_refreshing || value == null)
            return;

        SelectedStash = null;
    }
    partial void OnSelectedStashChanged(FolderProjectStashInfo? value)
    {
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
    partial void OnSelectedMergeConflictsChanged(
        IReadOnlyList<FolderProjectMergeConflictRow> value) =>
        NotifyCommands();

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        InitializeCommand.NotifyCanExecuteChanged();
        SaveIdentityCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
        CommitStagedCommand.NotifyCanExecuteChanged();
        CommitAllCommand.NotifyCanExecuteChanged();
        StageSelectedCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        StageChangeCommand.NotifyCanExecuteChanged();
        StageTreeNodeCommand.NotifyCanExecuteChanged();
        UnstageSelectedCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        UnstageChangeCommand.NotifyCanExecuteChanged();
        UnstageTreeNodeCommand.NotifyCanExecuteChanged();
        DiscardUnstagedCommand.NotifyCanExecuteChanged();
        DiscardStagedCommand.NotifyCanExecuteChanged();
        DiscardChangeCommand.NotifyCanExecuteChanged();
        DiscardTreeNodeCommand.NotifyCanExecuteChanged();
        DiscardAllCommand.NotifyCanExecuteChanged();
        ApplyStashCommand.NotifyCanExecuteChanged();
        PopStashCommand.NotifyCanExecuteChanged();
        DeleteStashCommand.NotifyCanExecuteChanged();
        ClearStashesCommand.NotifyCanExecuteChanged();
        RestoreFileCommand.NotifyCanExecuteChanged();
        DiscardCommitChangesCommand.NotifyCanExecuteChanged();
        RestoreCommitChangesToStageCommand.NotifyCanExecuteChanged();
        ResetCommitChangesKeepCommand.NotifyCanExecuteChanged();
        ResetCommitChangesAndDiscardCommand.NotifyCanExecuteChanged();
        RevertCommitChangesCommand.NotifyCanExecuteChanged();
        ReturnChangesToOriginalCommitCommand.NotifyCanExecuteChanged();
        RevertCommitCommand.NotifyCanExecuteChanged();
        ResetCommitKeepChangesCommand.NotifyCanExecuteChanged();
        ResetCommitAndDiscardChangesCommand.NotifyCanExecuteChanged();
        CreateRecoveryBranchCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        CreateAndSwitchBranchCommand.NotifyCanExecuteChanged();
        RenameBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        SwitchBranchCommand.NotifyCanExecuteChanged();
        CarryChangesAndSwitchCommand.NotifyCanExecuteChanged();
        StashChangesAndSwitchCommand.NotifyCanExecuteChanged();
        DiscardChangesAndSwitchCommand.NotifyCanExecuteChanged();
        CancelBranchSwitchCommand.NotifyCanExecuteChanged();
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
        string? SelectedUnstagedChangePath,
        string? SelectedStagedChangePath,
        string? SelectedCommitId,
        string? SelectedCommitChangePath,
        string? SelectedHistoryBranchName,
        string? SelectedBranchName,
        string? SelectedMergeSourceName,
        string? SelectedMergeTargetName,
        string? SelectedConflictId,
        int? SelectedStashIndex);

    private sealed record FolderProjectRefreshSnapshot(
        FolderProjectRepositoryStatus Status,
        FolderProjectGitIdentity? Identity,
        IReadOnlyList<FolderProjectCommitSummary> History,
        IReadOnlyList<FolderProjectBranchInfo> Branches,
        IReadOnlyList<FolderProjectStashInfo> Stashes,
        string? HistoryBranchName,
        FolderProjectMergeState? MergeState,
        IReadOnlyList<FolderProjectCommitChange> CommitChanges,
        bool IncludesHistory);
}
