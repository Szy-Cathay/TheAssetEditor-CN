using System;
using System.ComponentModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;

namespace AssetEditor.ViewModels;

public partial class FolderProjectHistoryWorkspaceViewModel : ObservableObject
{
    private readonly SynchronizationContext? _synchronizationContext;
    private FolderProjectContainer? _currentProject;
    private string? _currentProjectRoot;
    private bool _detachPending;
    private bool _refreshPending;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _selectedSidebarTabIndex;

    public FolderProjectHistoryViewModel History { get; }

    public FolderProjectHistoryWorkspaceViewModel(
        FolderProjectHistoryViewModel history,
        IGlobalEventHub eventHub)
    {
        History = history;
        _synchronizationContext = SynchronizationContext.Current;
        History.PropertyChanged += OnHistoryPropertyChanged;
        eventHub.Register<FolderProjectChangedEvent>(
            this,
            OnFolderProjectChanged);
    }

    public void SetEditableContainer(PackFileContainer? container)
    {
        if (container is not FolderProjectContainer project)
        {
            if (container == null && IsEnabled && History.IsBusy)
            {
                _detachPending = true;
                return;
            }

            _currentProject = null;
            _currentProjectRoot = null;
            _detachPending = false;
            _refreshPending = false;
            History.OpenProject(null);
            IsEnabled = false;
            SelectedSidebarTabIndex = 0;
            return;
        }

        var containerChanged = !ReferenceEquals(
            _currentProject,
            project);
        var projectChanged = !string.Equals(
            _currentProjectRoot,
            project.ProjectRoot,
            StringComparison.OrdinalIgnoreCase);
        _currentProject = project;
        _currentProjectRoot = project.ProjectRoot;
        _detachPending = false;
        IsEnabled = true;
        if (!projectChanged && !containerChanged)
            return;

        _refreshPending = true;
        History.OpenProject(project);
        if (SelectedSidebarTabIndex == 1)
            StartRefresh(false);
    }

    [RelayCommand]
    public void ShowHistory()
    {
        if (IsEnabled)
            SelectedSidebarTabIndex = 1;
    }

    partial void OnSelectedSidebarTabIndexChanged(int value)
    {
        if (value == 1 && IsEnabled)
            StartRefresh(false);
    }

    private void OnFolderProjectChanged(FolderProjectChangedEvent e)
    {
        if (_currentProjectRoot == null ||
            !string.Equals(
                _currentProjectRoot,
                e.Container.ProjectRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _refreshPending = true;
        RunOnSynchronizationContext(
            () =>
            {
                if (SelectedSidebarTabIndex == 1)
                    StartRefresh(false);
            });
    }

    private void OnHistoryPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderProjectHistoryViewModel.IsBusy) ||
            History.IsBusy)
            return;

        if (_detachPending)
        {
            SetEditableContainer(null);
            return;
        }

        if (SelectedSidebarTabIndex == 1)
            StartRefresh(false);
    }

    private void StartRefresh(bool force)
    {
        if (History.IsBusy || (!force && !_refreshPending))
            return;
        if (!History.RefreshCommand.CanExecute(null))
            return;

        _refreshPending = false;
        History.RefreshCommand.Execute(null);
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
}
