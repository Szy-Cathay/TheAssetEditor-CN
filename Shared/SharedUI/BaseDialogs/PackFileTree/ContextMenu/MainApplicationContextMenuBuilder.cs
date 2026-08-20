using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu
{
    public class MainApplicationContextMenuBuilder : ContextMenuBuilder
    {
        private readonly IPackFileService _packFileService;

        public MainApplicationContextMenuBuilder(IPackFileService packFileService, IUiCommandFactory commandFactory) : base(ContextMenuType.MainApplication, commandFactory)
        {
            _packFileService = packFileService;
        }

        protected override void Create(ContextMenuItem2 rootNode, TreeNode selectedNode)
        {
            var nodeType = selectedNode.NodeType;
            switch (nodeType)
            {
                case NodeType.File:
                    CreateForFile(rootNode, selectedNode);
                    break;
                case NodeType.Root:
                    CreateForDirectory(rootNode, selectedNode);
                    break;
                case NodeType.Directory:
                    CreateForDirectory(rootNode, selectedNode);
                    break;
            }
        }

        void CreateForFile(ContextMenuItem2 rootNode, TreeNode selectedNode)
        {
            var isCurrentProject = IsCurrentProject(selectedNode);
            if (!isCurrentProject)
            {
                Add<CopyToFolderProjectCommand>(
                    selectedNode,
                    rootNode,
                    includeWhenDisabled: true);
            }

            if (isCurrentProject)
            {
                AddSeperator(rootNode);
                Add<DuplicateFileCommand>(selectedNode, rootNode);
                Add<OnRenameNodeCommand>(selectedNode, rootNode);
                Add<DeleteNodeCommand>(selectedNode, rootNode);
                AddSeperator(rootNode);
            }

            Add<CopyNodePathCommand>(selectedNode, rootNode);
            if (isCurrentProject)
                Add<ToggleFolderProjectIgnoreCommand>(
                    selectedNode,
                    rootNode);

            var exportFolder = AddChildMenu(
                LocalizationManager.Instance.Get("General.Export"),
                rootNode);
            Add<ExportToDirectoryCommand>(selectedNode, exportFolder);
            Add<IExportCAVp8AsIvfCommand>(selectedNode, exportFolder);
            Add<IExportCAVp8AsWebMCommand>(selectedNode, exportFolder);
            Add<AdvancedExportCommand>(selectedNode, exportFolder);

            var openFolder = AddChildMenu(
                LocalizationManager.Instance.Get("ContextMenu.Open"),
                rootNode);
            Add<OpenNodeInHxDCommand>(selectedNode, openFolder);
            Add<OpenNodeInNotepadCommand>(selectedNode, openFolder);

            var reportsFolder = AddChildMenu(
                LocalizationManager.Instance.Get("ContextMenu.Reports"),
                rootNode);
            Add<IRmvToTextCommand>(selectedNode, reportsFolder);
            if (reportsFolder.ContextMenu.Count == 0)
                rootNode.ContextMenu.Remove(reportsFolder);
        }

        void CreateForDirectory(ContextMenuItem2 rootNode, TreeNode selectedNode)
        {
            var isCaPack = selectedNode.FileOwner.IsCaPackFile;
            var isCurrentProject = IsCurrentProject(selectedNode);

            if (selectedNode.NodeType == NodeType.Root)
            {
                // Close
                Add<ClosePackContainerFileCommand>(selectedNode, rootNode);
                AddSeperator(rootNode);

                if (selectedNode.FileOwner is FolderProjectContainer &&
                    !isCurrentProject)
                {
                    Add<SetAsEditableFolderProjectCommand>(
                        selectedNode,
                        rootNode);
                    AddSeperator(rootNode);
                }

                if (isCurrentProject)
                {
                    Add<SavePackFileContainerCommand>(selectedNode, rootNode);
                    Add<SaveAsPackFileContainerCommand>(selectedNode, rootNode);
                    if (selectedNode.FileOwner is FolderProjectContainer)
                    {
                        Add<ChangeFolderProjectOutputCommand>(
                            selectedNode,
                            rootNode);
                        Add<
                            ToggleFolderProjectCorruptionDetectionCommand>(
                            selectedNode,
                            rootNode);
                    }
                    AddSeperator(rootNode);
                }
            }

            if (!isCurrentProject)
            {
                Add<CopyToFolderProjectCommand>(
                    selectedNode,
                    rootNode,
                    includeWhenDisabled: true);
            }

            if (isCurrentProject)
            {
                var importFolder = AddChildMenu(
                    LocalizationManager.Instance.Get("ContextMenu.Import"),
                    rootNode);
                Add<ImportFileCommand>(selectedNode, importFolder);
                Add<ImportDirectoryCommand>(selectedNode, importFolder);

                if (selectedNode.NodeType != NodeType.Root)
                    Add<AdvancedImportCommand>(selectedNode, importFolder);

                var createMenu = AddChildMenu(
                    LocalizationManager.Instance.Get("ContextMenu.Create"),
                    rootNode);
                Add<CreateFolderCommand>(selectedNode, createMenu);

                AddSeperator(rootNode);
                Add<OnRenameNodeCommand>(selectedNode, rootNode);
                Add<DeleteNodeCommand>(selectedNode, rootNode);
                if (selectedNode.FileOwner is FolderProjectContainer &&
                    selectedNode.NodeType != NodeType.Root)
                {
                    Add<ToggleFolderProjectIgnoreCommand>(
                        selectedNode,
                        rootNode);
                }
                AddSeperator(rootNode);
            }

            Add<ExpandNodeCommand>(selectedNode, rootNode);
            Add<CollapseNodeCommand>(selectedNode, rootNode);
            Add<ExportToDirectoryCommand>(selectedNode, rootNode);

            if (!isCaPack)
                Add<OpenPackInFileExplorerCommand>(selectedNode, rootNode);
        }

        private bool IsCurrentProject(TreeNode selectedNode) =>
            selectedNode.FileOwner is FolderProjectContainer &&
            ReferenceEquals(
                _packFileService.GetEditablePack(),
                selectedNode.FileOwner);
    }
}
