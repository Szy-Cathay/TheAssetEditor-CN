using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.Common;

namespace AssetEditor.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDropTarget<IEditorInterface, bool>
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IPackFileService _packFileService;
        private readonly IFolderProjectCloseGuard _folderProjectCloseGuard;

        public PackFileBrowserViewModel FileTree { get; private set; }
        public MenuBarViewModel MenuBar { get; set; }
        public IEditorDatabase ToolsFactory { get; set; }
        public FolderProjectGitWorkspaceViewModel? GitWorkspace { get; }

        [ObservableProperty] public partial IEditorManager EditorManager { get; set; }
        [ObservableProperty] public partial bool IsClosingWithoutPrompt { get; set; }
        [ObservableProperty] public partial string ApplicationTitle { get; set; }
        [ObservableProperty] public partial string CurrentGame { get; set; }
        [ObservableProperty] public partial string EditablePackFile { get; set; }
        [ObservableProperty] public partial bool IsPackFileExplorerVisible { get; set; } = true;
        [ObservableProperty] public partial GridLength FileTreeColumnWidth { get; set; } = new GridLength(0.28, GridUnitType.Star);
        [ObservableProperty] public partial bool IsLoadingPacks { get; set; } = false;
        [ObservableProperty] public partial string LoadingStatusText { get; set; } = "";
        [ObservableProperty] public partial string LoadingProgressDetailText { get; set; } = "";
        [ObservableProperty] public partial int LoadingProgressValue { get; set; }
        [ObservableProperty] public partial int LoadingProgressMaximum { get; set; } = 3;
        [ObservableProperty] public partial bool LoadingProgressIsIndeterminate { get; set; }


        public MainViewModel(
                IEditorManager editorManager,
                PackFileTreeViewFactory packFileBrowserBuilder,
                MenuBarViewModel menuViewModel, 
                IPackFileService packfileService, 
                IEditorDatabase toolFactory, 
                IUiCommandFactory uiCommandFactory, 
                IEventHub eventHub,
                ApplicationSettingsService applicationSettingsService,
                IFolderProjectCloseGuard folderProjectCloseGuard,
                FolderProjectGitWorkspaceViewModel? gitWorkspace = null)
        {
            MenuBar = menuViewModel;

            EditorManager = editorManager;
            _uiCommandFactory = uiCommandFactory;
            _packFileService = packfileService;
            _folderProjectCloseGuard = folderProjectCloseGuard;
            GitWorkspace = gitWorkspace;

            eventHub.Register<PackFileContainerSetAsMainEditableEvent>(this, SetStatusBarEditablePackFile);
            eventHub.Register<OpenFolderProjectGitPanelEvent>(
                this,
                _ => GitWorkspace?.ShowGitManagement());

            FileTree = packFileBrowserBuilder.Create(ContextMenuType.MainApplication, showCaFiles: true, showFoldersOnly: false);
            FileTree.FileOpen += OpenFile;

            ToolsFactory = toolFactory;

            ApplicationTitle = LocalizationManager.Instance.GetFormat("Title.AppTitle", VersionChecker.GetCurrentVersion());
            CurrentGame = LocalizationManager.Instance.GetFormat("Title.CurrentGame", GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame).DisplayName);
        }

        void OpenFile(PackFile file) => _uiCommandFactory.Create<OpenEditorCommand>().Execute(file);

        [RelayCommand]
        internal Task Closing(IEditorInterface? editor) =>
            ClosingCore(editor, null);

        internal Task ClosingWithProgressCompletion(
            IEditorInterface? editor,
            Func<Task> completeProgressWindowAsync) =>
            ClosingCore(editor, completeProgressWindowAsync);

        private async Task ClosingCore(
            IEditorInterface? editor,
            Func<Task>? completeProgressWindowAsync)
        {
            IsClosingWithoutPrompt = false;
            var hasUnsavedPackFiles = FileTree.Files.Any(
                node =>
                    node.UnsavedChanged &&
                    node.FileOwner is not FolderProjectContainer);
            if (!EditorManager.ShouldBlockCloseCommand(
                    editor!,
                    hasUnsavedPackFiles) &&
                MessageBox.Show(
                    LocalizationManager.Instance.Get(
                        "Msg.UnsavedChangesOnQuit"),
                    LocalizationManager.Instance.Get(
                        "Msg.UnsavedChangesOnQuitTitle"),
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var currentProgress = new FolderProjectCloseProgress(
                FolderProjectCloseProgressStage.Preparing,
                1,
                3);
            var progressTimer = new DispatcherTimer(
                DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            void ApplyProgress(FolderProjectCloseProgress progress)
            {
                currentProgress = progress;
                LoadingProgressValue = progress.CurrentStep;
                LoadingProgressMaximum = progress.TotalSteps;
                LoadingProgressIsIndeterminate =
                    progress.Stage ==
                    FolderProjectCloseProgressStage.ReadingRepositoryStatus;
                LoadingStatusText = progress.Stage switch
                {
                    FolderProjectCloseProgressStage.Preparing =>
                        LocalizationManager.Instance.Get(
                            "FolderProject.Close.Progress.Preparing"),
                    FolderProjectCloseProgressStage
                        .ReadingRepositoryStatus =>
                        LocalizationManager.Instance.Get(
                            "FolderProject.Close.Progress.ReadingStatus"),
                    FolderProjectCloseProgressStage.SummarizingChanges =>
                        LocalizationManager.Instance.GetFormat(
                            "FolderProject.Close.Progress.Summarizing",
                            progress.ChangeCount ?? 0),
                    _ => "",
                };
                LoadingProgressDetailText =
                    progress.Stage ==
                    FolderProjectCloseProgressStage.ReadingRepositoryStatus
                        ? LocalizationManager.Instance.GetFormat(
                            "FolderProject.Close.Progress.StepWithElapsed",
                            progress.CurrentStep,
                            progress.TotalSteps,
                            (int)stopwatch.Elapsed.TotalSeconds)
                        : LocalizationManager.Instance.GetFormat(
                            "FolderProject.Close.Progress.Step",
                            progress.CurrentStep,
                            progress.TotalSteps);
            }
            progressTimer.Tick += (_, _) => ApplyProgress(currentProgress);

            var progressCompleted = false;
            async Task CompleteProgressAsync()
            {
                if (progressCompleted)
                    return;

                progressCompleted = true;
                progressTimer.Stop();
                stopwatch.Stop();
                LoadingStatusText = "";
                LoadingProgressDetailText = "";
                LoadingProgressValue = 0;
                LoadingProgressIsIndeterminate = false;
                IsLoadingPacks = false;
                if (completeProgressWindowAsync != null)
                    await completeProgressWindowAsync();
            }

            IsLoadingPacks = true;
            ApplyProgress(currentProgress);
            progressTimer.Start();
            try
            {
                var project =
                    _packFileService.GetEditablePack() as
                        FolderProjectContainer;
                IsClosingWithoutPrompt =
                    await _folderProjectCloseGuard.CanCloseAsync(
                        project,
                        ApplyProgress,
                        CompleteProgressAsync);
            }
            finally
            {
                await CompleteProgressAsync();
            }
        }

        [RelayCommand] void CloseTool(IEditorInterface tool) => EditorManager.CloseTool(tool);
      
        [RelayCommand] void CloseOtherTools(IEditorInterface tool) => EditorManager.CloseOtherTools(tool);
        [RelayCommand] void CloseAllTools(IEditorInterface tool) => EditorManager.CloseAllTools(tool);
        [RelayCommand] void CloseToolsToLeft(IEditorInterface tool) => EditorManager.CloseToolsToLeft(tool);
        [RelayCommand] void CloseToolsToRight(IEditorInterface tool) => EditorManager.CloseToolsToRight(tool);

        public bool AllowDrop(IEditorInterface node, IEditorInterface targetNode = default, bool insertAfterTargetNode = default) => true;
        public bool Drop(IEditorInterface node, IEditorInterface targetNode = default, bool insertAfterTargetNode = default) => EditorManager.Drop(node, targetNode, insertAfterTargetNode);

        private void SetStatusBarEditablePackFile(PackFileContainerSetAsMainEditableEvent e)
        {
            EditablePackFile = e.Container != null ? LocalizationManager.Instance.GetFormat("Title.EditablePack", e.Container.Name) : LocalizationManager.Instance.Get("Title.EditablePackNone");
            GitWorkspace?.SetEditableContainer(e.Container);
        }
    }
}
