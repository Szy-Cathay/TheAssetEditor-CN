using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AssetEditor.Services;
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

        [ObservableProperty] public partial IEditorManager EditorManager { get; set; }
        [ObservableProperty] public partial bool IsClosingWithoutPrompt { get; set; }
        [ObservableProperty] public partial string ApplicationTitle { get; set; }
        [ObservableProperty] public partial string CurrentGame { get; set; }
        [ObservableProperty] public partial string EditablePackFile { get; set; }
        [ObservableProperty] public partial bool IsPackFileExplorerVisible { get; set; } = true;
        [ObservableProperty] public partial GridLength FileTreeColumnWidth { get; set; } = new GridLength(0.28, GridUnitType.Star);
        [ObservableProperty] public partial bool IsLoadingPacks { get; set; } = false;
        [ObservableProperty] public partial string LoadingStatusText { get; set; } = "";


        public MainViewModel(
                IEditorManager editorManager,
                PackFileTreeViewFactory packFileBrowserBuilder,
                MenuBarViewModel menuViewModel, 
                IPackFileService packfileService, 
                IEditorDatabase toolFactory, 
                IUiCommandFactory uiCommandFactory, 
                IEventHub eventHub,
                ApplicationSettingsService applicationSettingsService,
                IFolderProjectCloseGuard folderProjectCloseGuard)
        {
            MenuBar = menuViewModel;

            EditorManager = editorManager;
            _uiCommandFactory = uiCommandFactory;
            _packFileService = packfileService;
            _folderProjectCloseGuard = folderProjectCloseGuard;

            eventHub.Register<PackFileContainerSetAsMainEditableEvent>(this, SetStatusBarEditablePackFile);

            FileTree = packFileBrowserBuilder.Create(ContextMenuType.MainApplication, showCaFiles: true, showFoldersOnly: false);
            FileTree.FileOpen += OpenFile;

            ToolsFactory = toolFactory;

            ApplicationTitle = LocalizationManager.Instance.GetFormat("Title.AppTitle", VersionChecker.GetCurrentVersion());
            CurrentGame = LocalizationManager.Instance.GetFormat("Title.CurrentGame", GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame).DisplayName);
        }

        void OpenFile(PackFile file) => _uiCommandFactory.Create<OpenEditorCommand>().Execute(file);

        [RelayCommand]
        internal async Task Closing(IEditorInterface? editor)
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

            IsLoadingPacks = true;
            LoadingStatusText = LocalizationManager.Instance.Get(
                "FolderProject.Close.CheckingStatus");
            try
            {
                var project =
                    _packFileService.GetEditablePack() as
                        FolderProjectContainer;
                IsClosingWithoutPrompt =
                    await _folderProjectCloseGuard.CanCloseAsync(project);
            }
            finally
            {
                LoadingStatusText = "";
                IsLoadingPacks = false;
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
        }
    }
}
