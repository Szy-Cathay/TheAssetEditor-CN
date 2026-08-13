using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetEditor.Services;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;

namespace AssetEditor.ViewModels;

public partial class FolderProjectGitWorkspaceViewModel : ObservableObject
{
    private const int MaxIncrementalStatusPaths = 512;
    private readonly IEditorManager _editorManager;
    private readonly IFolderProjectVersionControlWindowService
        _versionControlWindowService;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly object _pendingChangesLock = new();
    private readonly HashSet<string> _pendingWorkingChangePaths =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _currentProjectRoot;
    private bool _requiresFullRefresh;
    private bool _historyRefreshPending;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _selectedSidebarTabIndex;
    [ObservableProperty] private bool _isBranchPickerOpen;
    [ObservableProperty] private string _branchFilter = "";
    [ObservableProperty] private bool _isRepositoryEditorOpen;

    public FolderProjectVersionControlViewModel VersionControl { get; }
    public FolderProjectHistoryViewModel? History { get; }
    public bool IsLoadingOperationVisibleInPanel =>
        VersionControl.IsLoadingOperation && !IsRepositoryEditorOpen;
    public Task WorkingChangesRefreshTask { get; private set; } =
        Task.CompletedTask;

    public IEnumerable<FolderProjectBranchInfo> FilteredBranches =>
        string.IsNullOrWhiteSpace(BranchFilter)
            ? VersionControl.Branches
            : VersionControl.Branches.Where(
                branch => branch.Name.Contains(
                    BranchFilter.Trim(),
                    StringComparison.OrdinalIgnoreCase));

    public FolderProjectGitWorkspaceViewModel(
        FolderProjectVersionControlViewModel versionControl,
        IEditorManager editorManager,
        IFolderProjectVersionControlWindowService
            versionControlWindowService,
        IGlobalEventHub? eventHub = null,
        FolderProjectHistoryViewModel? history = null)
    {
        VersionControl = versionControl;
        History = history;
        _editorManager = editorManager;
        _versionControlWindowService = versionControlWindowService;
        _synchronizationContext = SynchronizationContext.Current;
        VersionControl.Branches.CollectionChanged +=
            (_, _) => OnPropertyChanged(nameof(FilteredBranches));
        VersionControl.PropertyChanged += OnVersionControlPropertyChanged;
        if (History != null)
            History.PropertyChanged += OnHistoryPropertyChanged;
        eventHub?.Register<FolderProjectChangedEvent>(
            this,
            OnFolderProjectChanged);
    }

    public void SetEditableContainer(PackFileContainer? container)
    {
        if (container == null &&
            VersionControl.IsBusy &&
            _currentProjectRoot != null)
        {
            return;
        }

        if (container is not FolderProjectContainer project)
        {
            CloseRepositoryEditor();
            _currentProjectRoot = null;
            History?.OpenProject(null);
            _historyRefreshPending = false;
            ClearPendingWorkingChanges();
            IsEnabled = false;
            SelectedSidebarTabIndex = 0;
            BranchFilter = "";
            return;
        }

        var projectChanged = !string.Equals(
                _currentProjectRoot,
                project.ProjectRoot,
                StringComparison.OrdinalIgnoreCase);
        if (projectChanged)
        {
            CloseRepositoryEditor();
            ClearPendingWorkingChanges();
        }

        _currentProjectRoot = project.ProjectRoot;
        IsEnabled = true;
        if (!projectChanged)
            return;

        VersionControl.OpenProject(
            project.ProjectRoot,
            project.ProjectSettings.Name,
            false,
            refresh: false);
        History?.OpenProject(project);
        if (SelectedSidebarTabIndex == 1 &&
            History != null)
        {
            StartHistoryRefresh(true);
        }
        else if (SelectedSidebarTabIndex == 1 &&
                 VersionControl.RefreshCommand.CanExecute(null))
        {
            VersionControl.RefreshCommand.Execute(null);
        }
    }

    [RelayCommand]
    public void ShowGitManagement()
    {
        if (IsEnabled)
            SelectedSidebarTabIndex = 1;
    }

    [RelayCommand]
    public void OpenRepository()
    {
        var existing = (_editorManager.GetAllEditors() ?? [])
            .OfType<IFolderProjectGitRepositoryEditor>()
            .FirstOrDefault();
        if (existing != null)
        {
            _editorManager.SetEditorAsCurrent(existing);
            return;
        }

        _editorManager.Create(
            EditorEnums.FolderProjectGitRepository,
            editor =>
                ((FolderProjectGitRepositoryViewModel)editor)
                .Open(this));
    }

    [RelayCommand]
    private async Task SwitchBranch(FolderProjectBranchInfo? branch)
    {
        IsBranchPickerOpen = false;
        if (branch == null || branch.IsCurrent)
            return;

        VersionControl.SelectedBranch = branch;
        if (VersionControl.SwitchBranchCommand.CanExecute(null))
            await VersionControl.SwitchBranchCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteBranch))]
    private async Task DeleteBranch(FolderProjectBranchInfo? branch)
    {
        if (branch == null)
            return;

        VersionControl.SelectedBranch = branch;
        if (VersionControl.DeleteBranchCommand.CanExecute(null))
            await VersionControl.DeleteBranchCommand.ExecuteAsync(null);
    }

    private bool CanDeleteBranch(FolderProjectBranchInfo? branch) =>
        CanUseBranchAction(branch) &&
        branch is { IsPrimary: false };

    [RelayCommand(CanExecute = nameof(CanMergeBranch))]
    private void MergeBranch(FolderProjectBranchInfo? branch)
    {
        if (branch == null)
            return;

        _versionControlWindowService.ShowMergeDialog(
            VersionControl.ProjectRoot,
            VersionControl.ProjectName,
            branch.Name);
    }

    private bool CanMergeBranch(FolderProjectBranchInfo? branch) =>
        CanUseBranchAction(branch) &&
        VersionControl.IsClean &&
        !VersionControl.IsDetached &&
        branch is { IsPrimary: false };

    private bool CanUseBranchAction(FolderProjectBranchInfo? branch) =>
        branch != null &&
        VersionControl.IsInitialized &&
        !VersionControl.IsBusy &&
        VersionControl.MergePhase == FolderProjectMergePhase.None &&
        VersionControl.OperationState ==
            FolderProjectRepositoryOperationState.None;

    partial void OnBranchFilterChanged(string value) =>
        OnPropertyChanged(nameof(FilteredBranches));

    partial void OnSelectedSidebarTabIndexChanged(int value)
    {
        if (value != 1 || !IsEnabled)
            return;

        if (History != null)
        {
            StartHistoryRefresh(true);
            return;
        }

        if (!VersionControl.HasRepositorySnapshot &&
            VersionControl.RefreshCommand.CanExecute(null))
        {
            ClearPendingWorkingChanges();
            VersionControl.RefreshCommand.Execute(null);
            return;
        }

        StartPendingWorkingChangesRefresh();
    }

    public void SetRepositoryEditorOpen(bool isOpen)
    {
        IsRepositoryEditorOpen = isOpen;
    }

    private void OnFolderProjectChanged(FolderProjectChangedEvent e)
    {
        var projectRoot = e.Container.ProjectRoot;
        if (_currentProjectRoot == null ||
            !string.Equals(
                _currentProjectRoot,
                projectRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (History != null)
        {
            _historyRefreshPending = true;
            RunOnSynchronizationContext(
                () =>
                {
                    if (SelectedSidebarTabIndex == 1)
                        StartHistoryRefresh(false);
                });
            return;
        }

        bool requiresFullRefresh;
        lock (_pendingChangesLock)
        {
            _requiresFullRefresh |= e.ChangeSet.RequiresReload;
            foreach (var change in e.ChangeSet.FileChanges)
            {
                _pendingWorkingChangePaths.Add(change.Path);
                if (!string.IsNullOrWhiteSpace(change.PreviousPath))
                    _pendingWorkingChangePaths.Add(change.PreviousPath);
            }
            if (e.ChangeSet.DirectoryChanges.Count != 0)
            {
                _pendingWorkingChangePaths.Add(
                    FolderProjectSettings.CnFileName);
            }
            if (_pendingWorkingChangePaths.Count >
                MaxIncrementalStatusPaths)
            {
                _requiresFullRefresh = true;
            }
            requiresFullRefresh = _requiresFullRefresh;
        }

        RunOnSynchronizationContext(
            () =>
            {
                if (!string.Equals(
                        _currentProjectRoot,
                        projectRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (requiresFullRefresh)
                {
                    VersionControl.HasRepositorySnapshot = false;
                    if (SelectedSidebarTabIndex == 1 &&
                        VersionControl.RefreshCommand.CanExecute(null))
                    {
                        ClearPendingWorkingChanges();
                        VersionControl.RefreshCommand.Execute(null);
                    }
                    return;
                }

                if (SelectedSidebarTabIndex != 1)
                    return;

                StartPendingWorkingChangesRefresh();
            });
    }

    private void OnVersionControlPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VersionControl.IsLoadingOperation))
        {
            OnPropertyChanged(
                nameof(IsLoadingOperationVisibleInPanel));
        }

        if (e.PropertyName == nameof(VersionControl.IsBusy) &&
            !VersionControl.IsBusy &&
            SelectedSidebarTabIndex == 1)
        {
            StartPendingWorkingChangesRefresh();
        }
    }

    private void OnHistoryPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderProjectHistoryViewModel.IsBusy) &&
            History is { IsBusy: false } &&
            SelectedSidebarTabIndex == 1)
        {
            StartHistoryRefresh(false);
        }
    }

    private void StartHistoryRefresh(bool force)
    {
        if (History == null || History.IsBusy)
            return;
        if (!force && !_historyRefreshPending)
            return;
        if (!History.RefreshCommand.CanExecute(null))
            return;

        _historyRefreshPending = false;
        History.RefreshCommand.Execute(null);
    }

    partial void OnIsRepositoryEditorOpenChanged(bool value) =>
        OnPropertyChanged(nameof(IsLoadingOperationVisibleInPanel));

    private void StartPendingWorkingChangesRefresh()
    {
        if (!VersionControl.HasRepositorySnapshot ||
            VersionControl.IsBusy ||
            VersionControl.IsStatusRefreshing)
        {
            return;
        }

        List<string> paths;
        lock (_pendingChangesLock)
        {
            if (_requiresFullRefresh ||
                _pendingWorkingChangePaths.Count == 0)
            {
                return;
            }

            paths = _pendingWorkingChangePaths.ToList();
            _pendingWorkingChangePaths.Clear();
        }

        WorkingChangesRefreshTask =
            VersionControl.RefreshWorkingChanges(paths);
    }

    private void ClearPendingWorkingChanges()
    {
        lock (_pendingChangesLock)
        {
            _pendingWorkingChangePaths.Clear();
            _requiresFullRefresh = false;
        }
    }

    private void RunOnSynchronizationContext(Action action)
    {
        if (_synchronizationContext == null ||
            ReferenceEquals(
                _synchronizationContext,
                SynchronizationContext.Current))
        {
            action();
            return;
        }

        _synchronizationContext.Post(_ => action(), null);
    }

    private void CloseRepositoryEditor()
    {
        var editor = (_editorManager.GetAllEditors() ?? [])
            .OfType<IFolderProjectGitRepositoryEditor>()
            .FirstOrDefault();
        if (editor != null)
            _editorManager.CloseTool(editor);
    }
}
