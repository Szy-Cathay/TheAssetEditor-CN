using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace AssetEditor.ViewModels;

public interface IFolderProjectGitRepositoryEditor : IEditorInterface;

public partial class FolderProjectGitRepositoryViewModel :
    ObservableObject,
    IFolderProjectGitRepositoryEditor
{
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _branchFilter = "";
    [ObservableProperty] private string _historyFilter = "";

    public FolderProjectVersionControlViewModel VersionControl =>
        Workspace.VersionControl;
    public FolderProjectGitWorkspaceViewModel Workspace { get; private set; } =
        null!;

    public IEnumerable<FolderProjectCommitSummary> FilteredHistory =>
        string.IsNullOrWhiteSpace(HistoryFilter)
            ? VersionControl.History
            : VersionControl.History.Where(
                commit =>
                    commit.Message.Contains(
                        HistoryFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase) ||
                    commit.AuthorName.Contains(
                        HistoryFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase) ||
                    commit.Description.Contains(
                        HistoryFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase) ||
                    commit.ShortId.Contains(
                        HistoryFilter.Trim(),
                        StringComparison.OrdinalIgnoreCase));
    public IEnumerable<FolderProjectBranchInfo> FilteredBranches =>
        string.IsNullOrWhiteSpace(BranchFilter)
            ? VersionControl.Branches
            : VersionControl.Branches.Where(
                branch => branch.Name.Contains(
                    BranchFilter.Trim(),
                    StringComparison.OrdinalIgnoreCase));
    public bool HasFilteredBranches => FilteredBranches.Any();
    public bool HasFilteredHistory => FilteredHistory.Any();
    public bool HasSelectedCommit => VersionControl.SelectedCommit != null;
    public string BranchEmptyMessage => LocalizationManager.Instance.Get(
        string.IsNullOrWhiteSpace(BranchFilter)
            ? "FolderProject.Git.NoBranches"
            : "FolderProject.Git.NoBranchMatches");
    public string HistoryEmptyMessage => LocalizationManager.Instance.Get(
        string.IsNullOrWhiteSpace(HistoryFilter)
            ? "FolderProject.Git.NoCommits"
            : "FolderProject.Git.NoCommitMatches");

    public FolderProjectGitRepositoryViewModel()
    {
        DisplayName = "Git 存储库";
    }

    public void Open(FolderProjectGitWorkspaceViewModel workspace)
    {
        Workspace = workspace;
        DisplayName = LocalizationManager.Instance.GetFormat(
            "FolderProject.Git.RepositoryTabTitle",
            VersionControl.ProjectName);
        VersionControl.History.CollectionChanged +=
            OnHistoryChanged;
        VersionControl.Branches.CollectionChanged +=
            OnBranchesChanged;
        VersionControl.PropertyChanged +=
            OnVersionControlPropertyChanged;
        VersionControl.OpenRepositoryHistory();
    }

    public void Close()
    {
        if (Workspace != null)
        {
            VersionControl.History.CollectionChanged -=
                OnHistoryChanged;
            VersionControl.Branches.CollectionChanged -=
                OnBranchesChanged;
            VersionControl.PropertyChanged -=
                OnVersionControlPropertyChanged;
        }
    }

    partial void OnBranchFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredBranches));
        OnPropertyChanged(nameof(HasFilteredBranches));
        OnPropertyChanged(nameof(BranchEmptyMessage));
    }

    partial void OnHistoryFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredHistory));
        OnPropertyChanged(nameof(HasFilteredHistory));
        OnPropertyChanged(nameof(HistoryEmptyMessage));
    }

    private void OnHistoryChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilteredHistory));
        OnPropertyChanged(nameof(HasFilteredHistory));
    }

    private void OnBranchesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilteredBranches));
        OnPropertyChanged(nameof(HasFilteredBranches));
    }

    private void OnVersionControlPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(FolderProjectVersionControlViewModel.SelectedCommit))
        {
            OnPropertyChanged(nameof(HasSelectedCommit));
        }
    }
}
