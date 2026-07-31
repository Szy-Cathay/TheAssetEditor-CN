using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    public class PackFileTreeViewFactory
    {
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IPackFileService _packFileService;
        private readonly IEventHub _eventHub;
        private readonly ContextMenuFactory _contextMenuFactory;
        private readonly IFolderProjectVersionControlService?
            _versionControlService;

        public PackFileTreeViewFactory(ApplicationSettingsService applicationSettingsService, IPackFileService packFileService, IEventHub eventHub, ContextMenuFactory contextMenuFactory, IFolderProjectVersionControlService? versionControlService = null)
        {
            _applicationSettingsService = applicationSettingsService;
            _packFileService = packFileService;
            _eventHub = eventHub;
            _contextMenuFactory = contextMenuFactory;
            _versionControlService = versionControlService;
        }

        public PackFileBrowserViewModel Create(ContextMenuType contextMenu, bool showCaFiles, bool showFoldersOnly)
        {
            var contextMenuBuilder = _contextMenuFactory.GetContextMenu(contextMenu);
            var fileTree = new PackFileBrowserViewModel(_applicationSettingsService, contextMenuBuilder, _packFileService, _eventHub, showCaFiles, showFoldersOnly, _versionControlService);
            return fileTree;
        }
    }
}
