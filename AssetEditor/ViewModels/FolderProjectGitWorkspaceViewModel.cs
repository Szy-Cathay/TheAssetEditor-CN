using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetEditor.Services;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;

namespace AssetEditor.ViewModels;

public partial class FolderProjectGitWorkspaceViewModel : ObservableObject
{
    private readonly IEditorManager _editorManager;
    private readonly IFolderProjectVersionControlWindowService
        _versionControlWindowService;
    private string? _currentProjectRoot;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _selectedSidebarTabIndex;
    [ObservableProperty] private bool _isBranchPickerOpen;
    [ObservableProperty] private string _branchFilter = "";

    public FolderProjectVersionControlViewModel VersionControl { get; }

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
            versionControlWindowService)
    {
        VersionControl = versionControl;
        _editorManager = editorManager;
        _versionControlWindowService = versionControlWindowService;
        VersionControl.Branches.CollectionChanged +=
            (_, _) => OnPropertyChanged(nameof(FilteredBranches));
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
            IsEnabled = false;
            SelectedSidebarTabIndex = 0;
            BranchFilter = "";
            return;
        }

        if (!string.Equals(
                _currentProjectRoot,
                project.ProjectRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            CloseRepositoryEditor();
        }

        _currentProjectRoot = project.ProjectRoot;
        IsEnabled = true;
        VersionControl.OpenProject(
            project.ProjectRoot,
            project.ProjectSettings.Name,
            false);
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
        if (value == 1 &&
            IsEnabled &&
            VersionControl.RefreshCommand.CanExecute(null))
        {
            VersionControl.RefreshCommand.Execute(null);
        }
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
