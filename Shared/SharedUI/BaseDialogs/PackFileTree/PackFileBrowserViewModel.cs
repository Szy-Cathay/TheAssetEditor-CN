using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.Common;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    public delegate void FileSelectedDelegate(PackFile file);
    public delegate void NodeSelectedDelegate(TreeNode node);

    public partial class PackFileBrowserViewModel : ObservableObject, IDisposable, IDropTarget<TreeNode>
    {
        protected IPackFileService _packFileService;
        private readonly IEventHub? _eventHub;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IContextMenuBuilder _contextMenuBuilder;
        private readonly IFolderProjectHistoryService? _historyService;
        private readonly bool _showCaFiles;
        private readonly Dictionary<string, FolderProjectTreeState>
            _detachedFolderProjectStates =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FolderProjectHistoryStatus>
            _folderProjectHistoryStatusCache =
                new(StringComparer.OrdinalIgnoreCase);
        private int _historyStatusRequestId;

        public event FileSelectedDelegate FileOpen;
        public event NodeSelectedDelegate NodeSelected;

        public ObservableCollection<TreeNode> Files { get; set; } = [];
        public SearchFilter Filter { get; private set; }
        public Task HistoryStatusRefreshTask { get; private set; } =
            Task.CompletedTask;
        public Task CaWemRefreshTask { get; private set; } =
            Task.CompletedTask;

        [ObservableProperty] TreeNode _selectedItem;
        [ObservableProperty] ObservableCollection<ContextMenuItem2> _contextMenu = [];
        [ObservableProperty] bool _isRefreshingCaWemFiles;
        [ObservableProperty] string _caWemRefreshDetail = string.Empty;
        [ObservableProperty] int _caWemRefreshValue;
        [ObservableProperty] int _caWemRefreshMaximum;
        [ObservableProperty] bool _caWemRefreshIsIndeterminate = true;

        public bool ShowFoldersOnly { get; }

        public PackFileBrowserViewModel(ApplicationSettingsService applicationSettingsService, IContextMenuBuilder contextMenuBuilder, IPackFileService packFileService, IEventHub? eventHub, bool showCaFiles, bool showFoldersOnly, IFolderProjectHistoryService? historyService = null)
        {
            _packFileService = packFileService;
            _eventHub = eventHub;
            _applicationSettingsService = applicationSettingsService;
            _contextMenuBuilder = contextMenuBuilder;
            _historyService = historyService;
            _showCaFiles = showCaFiles;

            ShowFoldersOnly = showFoldersOnly;

            _eventHub?.Register<PackFileContainerSetAsMainEditableEvent>(this, MainEditablePackChanged);
            _eventHub?.Register<PackFileContainerRemovedEvent>(this, PackFileContainerRemoved);
            _eventHub?.Register<PackFileContainerAddedEvent>(
                this,
                PackFileContainerAdded);
            _eventHub?.Register<PackFileContainerFilesUpdatedEvent>(this, Database_PackFilesUpdated);
            _eventHub?.Register<PackFileContainerFilesAddedEvent>(this, Database_PackFilesAdded);
            _eventHub?.Register<PackFileContainerFilesRemovedEvent>(this, x => Database_PackFilesRemoved(x.Container, x.RemovedFiles));
            _eventHub?.Register<PackFileContainerFolderRemovedEvent>(this, x => Database_PackFileFolderRemoved(x.Container, x.Folder));
            _eventHub?.Register<PackFileContainerFolderRenamedEvent>(this, x => Database_PackFileFolderRenamed(x.Container, x.NewNodePath));
            _eventHub?.Register<FolderProjectChangedEvent>(
                this,
                ApplyFolderProjectChangeSet);
            _eventHub?.Register<PackFileContainerSavedEvent>(this, ContainerSaved);
            _eventHub?.Register<ShowCaWemFilesChangedEvent>(
                this,
                OnShowCaWemFilesChanged);

            Filter = new SearchFilter(Files);
            Filter.ShowFoldersOnly = ShowFoldersOnly;

            foreach (var item in _packFileService.GetAllPackfileContainers())
            {
                var loadFile = true;
                if (!showCaFiles)
                    loadFile = !item.IsCaPackFile;

                if (loadFile)
                    ReloadTree(item);
            }
        }

        private void OnShowCaWemFilesChanged(
            ShowCaWemFilesChangedEvent changedEvent)
        {
            if (!_showCaFiles)
                return;

            CaWemRefreshTask = RefreshCaWemFilesAsync();
        }

        private async Task RefreshCaWemFilesAsync()
        {
            IsRefreshingCaWemFiles = true;
            await Task.Yield();
            try
            {
                var containers = _packFileService
                    .GetAllPackfileContainers()
                    .Where(value => value.IsCaPackFile)
                    .ToArray();
                CaWemRefreshValue = 0;
                CaWemRefreshMaximum = containers.Length;
                CaWemRefreshIsIndeterminate = containers.Length == 0;
                for (var index = 0; index < containers.Length; index++)
                {
                    var container = containers[index];
                    CaWemRefreshDetail = container.Name;
                    ReloadTree(container);
                    CaWemRefreshValue = index + 1;
                    await Task.Yield();
                }
            }
            finally
            {
                IsRefreshingCaWemFiles = false;
            }
        }

        partial void OnSelectedItemChanged(TreeNode value)
        {
            if (value != null)
                value.IsSelected = true;
            ContextMenu = _contextMenuBuilder.Build(value);
            NodeSelected?.Invoke(_selectedItem);
        }

        private void Database_PackFileFolderRemoved(PackFileContainer container, string folder)
        {
            if (container is FolderProjectContainer)
            {
                ReloadFolderProjectTreeAndMarkChanged(container);
                return;
            }

            var root = GetPackFileCollectionRootNode(container);
            var nodeToDelete = GetNodeFromPath(root, container, folder, false);

            var parent = nodeToDelete.Parent;
            parent.Children.Remove(nodeToDelete);
            nodeToDelete.RemoveSelf();

            root.UnsavedChanged = true;
        }

        private void Database_PackFileFolderRenamed(PackFileContainer container, string folder)
        {
            if (container is FolderProjectContainer)
            {
                ReloadFolderProjectTreeAndMarkChanged(container);
                return;
            }

            var root = GetPackFileCollectionRootNode(container);
            var node = GetNodeFromPath(root, container, folder, false);

            node.UnsavedChanged = true;
        }

        private void ContainerSaved(PackFileContainerSavedEvent e)
        {
            var root = GetPackFileCollectionRootNode(e.Container);

            root.UnsavedChanged = false;
            root.ForeachNode((node) => node.UnsavedChanged = false);
        }

        private void Database_PackFilesRemoved(PackFileContainer container, List<PackFile> files)
        {
            if (container is FolderProjectContainer)
            {
                ReloadFolderProjectTreeAndMarkChanged(container);
                return;
            }

            var root = GetPackFileCollectionRootNode(container);
            root.UnsavedChanged = true;

            foreach (var file in files)
            {
                var node = GetNodeFromPackFile(container, file, false);
                node.Parent.Children.Remove(node);
            }
        }

        private void Database_PackFilesUpdated(PackFileContainerFilesUpdatedEvent e)
        {
            if (e.Container is FolderProjectContainer)
            {
                ReloadTree(e.Container);
                MarkFolderProjectFilesChanged(
                    e.Container,
                    e.ChangedFiles);
                return;
            }

            foreach (var file in e.ChangedFiles)
            {
                var rootNode = GetPackFileCollectionRootNode(e.Container);
                rootNode.UnsavedChanged = true;
                var node = GetNodeFromPackFile(e.Container, file);
                if (node == null)
                    continue;
                node.Name = file.Name;
                node.UnsavedChanged = true;

                var parent = node.Parent;
                while (parent != rootNode)
                {
                    parent.UnsavedChanged = true;
                    parent = parent.Parent;
                }
            }
        }

        private void Database_PackFilesAdded(
            PackFileContainerFilesAddedEvent e)
        {
            if (e.Container is FolderProjectContainer)
            {
                ReloadTree(e.Container);
                MarkFolderProjectFilesChanged(
                    e.Container,
                    e.AddedFiles);
                return;
            }

            AddFiles(e.Container, e.AddedFiles);
        }

        private void ApplyFolderProjectChangeSet(
            FolderProjectChangedEvent e)
        {
            if (e.ChangeSet.RequiresReload)
            {
                ReloadFolderProjectTreeAndMarkChanged(e.Container);
                return;
            }

            var root = GetPackFileCollectionRootNode(e.Container);
            if (root == null)
                return;

            var directoryChanges = CoalesceRemovedDirectories(
                e.Container,
                e.ChangeSet);
            foreach (var directoryChange in directoryChanges)
                ApplyFolderProjectDirectoryChange(
                    e.Container,
                    root,
                    directoryChange);

            foreach (var change in e.ChangeSet.FileChanges)
            {
                if (IsCoveredByDirectoryChange(
                        change,
                        directoryChanges))
                {
                    continue;
                }

                var path = change.Path.Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar);
                if (change.Kind == FolderProjectFileChangeKind.Removed)
                {
                    var removedNode = FindNodeByPath(root, path);
                    if (removedNode == null)
                        continue;

                    var parent = removedNode.Parent;
                    var selectionRemoved =
                        ReferenceEquals(SelectedItem, removedNode);
                    if (selectionRemoved)
                        removedNode.IsSelected = false;

                    parent?.Children.Remove(removedNode);
                    removedNode.RemoveSelf();
                    var retainedParent = PruneMissingAncestors(
                        parent,
                        root,
                        e.Container);
                    if (selectionRemoved)
                        SelectedItem = retainedParent;
                    while (parent != null)
                    {
                        parent.UnsavedChanged = true;
                        parent = parent.Parent;
                    }
                    continue;
                }

                TreeNode? node;
                if (change.Kind == FolderProjectFileChangeKind.Moved)
                {
                    var previousPath = change.PreviousPath?.Replace(
                        Path.AltDirectorySeparatorChar,
                        Path.DirectorySeparatorChar);
                    node = previousPath == null
                        ? null
                        : FindNodeByPath(root, previousPath);
                    if (node != null)
                    {
                        var oldParent = node.Parent;
                        oldParent?.Children.Remove(node);
                        var directory = Path.GetDirectoryName(path) ?? "";
                        var newParent = GetNodeFromPath(
                            root,
                            e.Container,
                            directory);
                        node.Parent = newParent;
                        node.Name = Path.GetFileName(path);
                        node.Item = change.File;
                        newParent.Children.Add(node);
                    }
                }
                else
                    node = FindNodeByPath(root, path);

                if (node == null)
                    node = AddFileAtPath(e.Container, root, path, change.File);
                else
                {
                    node.Name = change.File.Name;
                    node.Item = change.File;
                }

                node.IsIgnored = e.Container.IsIgnored(path);
                while (node != null)
                {
                    node.UnsavedChanged = true;
                    node = node.Parent;
                }
            }

            Filter.Refresh();
        }

        private static IReadOnlyList<FolderProjectDirectoryChange>
            CoalesceRemovedDirectories(
                FolderProjectContainer container,
                FolderProjectChangeSet changeSet)
        {
            var directoryChanges = changeSet.DirectoryChanges.ToList();
            var removedDirectories = directoryChanges
                .Where(change => change.Kind ==
                    FolderProjectDirectoryChangeKind.Removed)
                .Select(change => change.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directoryExists = new Dictionary<string, bool>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var fileChange in changeSet.FileChanges)
            {
                if (fileChange.Kind != FolderProjectFileChangeKind.Removed)
                    continue;

                var directory = Path.GetDirectoryName(
                    fileChange.Path.Replace(
                        Path.AltDirectorySeparatorChar,
                        Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(directory))
                    continue;

                var candidate = "";
                foreach (var segment in directory.Split(
                             Path.DirectorySeparatorChar,
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    candidate = candidate.Length == 0
                        ? segment
                        : Path.Combine(candidate, segment);
                    if (removedDirectories.Contains(candidate))
                        break;

                    if (!directoryExists.TryGetValue(
                            candidate,
                            out var exists))
                    {
                        exists = Directory.Exists(
                            FolderProjectPathPolicy.ResolveFilePath(
                                container.ProjectRoot,
                                candidate));
                        directoryExists[candidate] = exists;
                    }

                    if (exists)
                        continue;

                    removedDirectories.Add(candidate);
                    directoryChanges.Add(
                        new FolderProjectDirectoryChange(
                            candidate,
                            FolderProjectDirectoryChangeKind.Removed));
                    break;
                }
            }

            return directoryChanges;
        }

        private static TreeNode PruneMissingAncestors(
            TreeNode? node,
            TreeNode root,
            FolderProjectContainer container)
        {
            while (node != null &&
                   node != root &&
                   node.Children.Count == 0 &&
                   !container.EmptyDirectories.Contains(node.GetFullPath()))
            {
                var parent = node.Parent;
                parent?.Children.Remove(node);
                node.Parent = null;
                node = parent;
            }
            return node ?? root;
        }

        private void ApplyFolderProjectDirectoryChange(
            FolderProjectContainer container,
            TreeNode root,
            FolderProjectDirectoryChange change)
        {
            var path = change.Path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
            if (change.Kind == FolderProjectDirectoryChangeKind.Added)
            {
                GetNodeFromPath(root, container, path).UnsavedChanged = true;
                return;
            }

            var sourcePath = change.PreviousPath ?? path;
            var node = FindNodeByPath(root, sourcePath);
            if (node == null)
                return;

            var oldParent = node.Parent;
            if (change.Kind == FolderProjectDirectoryChangeKind.Removed)
            {
                if (IsNodeOrDescendant(node, SelectedItem))
                {
                    SelectedItem.IsSelected = false;
                    SelectedItem = oldParent ?? root;
                }

                oldParent?.Children.Remove(node);
                node.RemoveSelf();
            }
            else
            {
                oldParent?.Children.Remove(node);
                var directory = Path.GetDirectoryName(path) ?? "";
                var newParent = GetNodeFromPath(root, container, directory);
                node.Parent = newParent;
                node.Name = Path.GetFileName(path);
                newParent.Children.Add(node);
            }

            var changedNode = oldParent ?? root;
            while (changedNode != null)
            {
                changedNode.UnsavedChanged = true;
                changedNode = changedNode.Parent;
            }
        }

        private static bool IsNodeOrDescendant(
            TreeNode ancestor,
            TreeNode? node)
        {
            while (node != null)
            {
                if (ReferenceEquals(ancestor, node))
                    return true;
                node = node.Parent;
            }
            return false;
        }

        private static bool IsCoveredByDirectoryChange(
            FolderProjectFileChange fileChange,
            IReadOnlyList<FolderProjectDirectoryChange> directoryChanges)
        {
            foreach (var directoryChange in directoryChanges)
            {
                if (directoryChange.Kind ==
                        FolderProjectDirectoryChangeKind.Removed &&
                    IsPathInside(fileChange.Path, directoryChange.Path))
                {
                    return true;
                }

                if (directoryChange.Kind ==
                        FolderProjectDirectoryChangeKind.Moved &&
                    fileChange.PreviousPath != null &&
                    directoryChange.PreviousPath != null &&
                    IsPathInside(
                        fileChange.PreviousPath,
                        directoryChange.PreviousPath) &&
                    IsPathInside(fileChange.Path, directoryChange.Path))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPathInside(string path, string directory)
        {
            return path.StartsWith(
                directory.TrimEnd('\\', '/') + "\\",
                StringComparison.OrdinalIgnoreCase);
        }

        private static TreeNode AddFileAtPath(
            FolderProjectContainer container,
            TreeNode root,
            string path,
            PackFile file)
        {
            var directory = Path.GetDirectoryName(path) ?? "";
            var parent = GetNodeFromPath(
                root,
                container,
                directory);
            var node = new TreeNode(
                Path.GetFileName(path),
                NodeType.File,
                container,
                parent,
                file);
            parent.Children.Add(node);
            return node;
        }

        private void ReloadFolderProjectTreeAndMarkChanged(
            PackFileContainer container)
        {
            ReloadTree(container);
            var root = GetPackFileCollectionRootNode(container);
            if (root != null)
                root.UnsavedChanged = true;
        }

        private void MarkFolderProjectFilesChanged(
            PackFileContainer container,
            IEnumerable<PackFile> files)
        {
            var root = GetPackFileCollectionRootNode(container);
            if (root == null)
                return;

            foreach (var file in files)
            {
                var relativePath = container.FileList
                    .FirstOrDefault(
                        pair => ReferenceEquals(pair.Value, file))
                    .Key;
                var node = string.IsNullOrWhiteSpace(relativePath)
                    ? null
                    : FindNodeByPath(
                        root,
                        relativePath.Replace(
                            Path.AltDirectorySeparatorChar,
                            Path.DirectorySeparatorChar));
                node ??= root;
                while (node != null)
                {
                    node.UnsavedChanged = true;
                    node = node.Parent;
                }
            }
        }

        [RelayCommand]
        protected virtual void OnClearText()
        {
            Filter.FilterText = "";
        }

        [RelayCommand]
        protected virtual void OnDoubleClick(TreeNode node)
        {
            if (SelectedItem == null)
                return;

            var maxExpandCount = 200;
            if (SelectedItem.NodeType == NodeType.File)
            {
                FileOpen?.Invoke(SelectedItem.Item!);
            }
            else if (SelectedItem.NodeType == NodeType.Directory || SelectedItem.NodeType == NodeType.Root)
            {
                SelectedItem.IsNodeExpanded = !SelectedItem.IsNodeExpanded;

                if (Keyboard.IsKeyDown(Key.LeftCtrl))
                {
                    var numChildren = SelectedItem.GetAllChildFileNodes().Count;
                    if (numChildren < maxExpandCount)
                        SelectedItem.ExpandIfVisible(true);
                }
            }

        }

        private void MainEditablePackChanged(PackFileContainerSetAsMainEditableEvent e)
        {
            foreach (var item in Files)
                item.IsMainEditabelPack = false;

            var newContiner = Files.FirstOrDefault(x => x.FileOwner == e.Container);
            if (newContiner != null)
                newContiner.IsMainEditabelPack = true;
        }

        private void AddFiles(PackFileContainer container, List<PackFile> files)
        {
            var root = GetPackFileCollectionRootNode(container);
            root.UnsavedChanged = true;

            foreach (var item in files)
            {
                if (container.IsCaPackFile && _applicationSettingsService.CurrentSettings.ShowCAWemFiles == false)
                {
                    var isWemFile = item.Name.EndsWith(".wem", StringComparison.InvariantCultureIgnoreCase);
                    if (isWemFile)
                        continue;
                }

                var fullPath = _packFileService.GetFullPath(item, container);
                var numSeperators = fullPath.Count(x => x == Path.DirectorySeparatorChar);

                var directoryEnd = fullPath.LastIndexOf(Path.DirectorySeparatorChar);
                var fileName = fullPath.Substring(directoryEnd + 1);

                // Check if alreayd added - this happens moving files.

                TreeNode newNode;
                if (numSeperators == 0)
                {
                    newNode = new TreeNode(fileName, NodeType.File, container, root, item);
                    root.Children.Add(newNode);
                }
                else
                {
                    var directory = fullPath.Substring(0, directoryEnd);
                    var folder = GetNodeFromPath(root, container, directory);
                    newNode = new TreeNode(fileName, NodeType.File, container, folder, item);

                    // remove any existing files with same name
                    var existingFile = folder.Children.FirstOrDefault(node => node.Name == item.Name);
                    if (existingFile != null)
                    {
                        folder.Children.Remove(existingFile);
                    }

                    folder.Children.Add(newNode);
                }

                newNode.UnsavedChanged = true;
                var parent = newNode.Parent;
                while (parent != root)
                {
                    parent.UnsavedChanged = true;
                    parent = parent.Parent;
                }
            }
        }

        public TreeNode? GetFromPath(TreeNode parent, string path)
        {
            var numSeperators = path.Count(x => x == Path.DirectorySeparatorChar);
            if (path.Length == 0)
                return parent;

            var nodeName = path;
            var remainingStr = "";

            if (numSeperators != 0)
            {
                var currentIndex = path.IndexOf(Path.DirectorySeparatorChar, 0);
                nodeName = path.Substring(0, currentIndex);
                remainingStr = path.Substring(currentIndex + 1);
            }

            foreach (var child in parent.Children)
            {
                if (child.Name == nodeName)
                    return GetFromPath(child, remainingStr);
            }

            return null;
        }

        private static TreeNode? GetNodeFromPath(TreeNode parent, PackFileContainer container, string path, bool createIfMissing = true)
        {
            var numSeperators = path.Count(x => x == Path.DirectorySeparatorChar);
            if (path.Length == 0)
                return parent;

            var nodeName = path;
            var remainingStr = "";

            if (numSeperators != 0)
            {
                var currentIndex = path.IndexOf(Path.DirectorySeparatorChar, 0);
                nodeName = path.Substring(0, currentIndex);
                remainingStr = path.Substring(currentIndex + 1);
            }

            foreach (var child in parent.Children)
            {
                if (child.Name == nodeName)
                    return GetNodeFromPath(child, container, remainingStr);
            }

            if (createIfMissing)
            {
                var newNode = new TreeNode(nodeName, NodeType.Directory, container, parent);
                parent.Children.Add(newNode);
                return GetNodeFromPath(newNode, container, remainingStr);
            }
            return null;
        }

        private TreeNode? GetPackFileCollectionRootNode(PackFileContainer container)
        {
            foreach (var child in Files)
            {
                if (child.FileOwner == container)
                    return child;
            }
            return null;
        }

        private TreeNode? GetNodeFromPackFile(PackFileContainer container, PackFile pf, bool createIfMissing = true)
        {
            var root = GetPackFileCollectionRootNode(container);
            var fullPath = _packFileService.GetFullPath(pf, container);
            var numSeperators = fullPath.Count(x => x == Path.DirectorySeparatorChar);

            if (numSeperators == 0)
            {
                return root.Children.FirstOrDefault(x => x.Item == pf);
            }
            else
            {
                var directoryEnd = fullPath.LastIndexOf(Path.DirectorySeparatorChar);
                var directory = fullPath.Substring(0, directoryEnd);
                var parent = GetNodeFromPath(root, container, directory, createIfMissing);

                return parent.Children.FirstOrDefault(x => x.Item == pf);
            }
        }

        private void ReloadTree(
            PackFileContainer container,
            FolderProjectTreeState? detachedState = null)
        {
            var existingNode = Files.FirstOrDefault(x => x.FileOwner == container);
            var existingIndex = existingNode == null
                ? -1
                : Files.IndexOf(existingNode);
            var state =
                CaptureTreeState(existingNode) ??
                detachedState;
            if (existingNode != null)
                Files.Remove(existingNode);

            var root = new TreeNode(container.Name, NodeType.Root, container, null);
            root.IsMainEditabelPack = _packFileService.GetEditablePack() == container;
            var directoryMap_new = new Dictionary<string, TreeNode>(container.FileList.Count);
            var skipWemFiles = container.IsCaPackFile && _applicationSettingsService.CurrentSettings.ShowCAWemFiles == false;

            List<(string FolderName, string FullFolderPath)> stackFileNames = new(10);
            foreach (var item in container.FileList)
            {
                ReadOnlySpan<char> pathSpan = item.Key;
                var lastTreeNode = root;

                if (skipWemFiles)
                {
                    var isWemFile = pathSpan.EndsWith(".wem", StringComparison.InvariantCultureIgnoreCase);
                    if (isWemFile)
                        continue;
                }

                stackFileNames.Clear();
                var end = pathSpan.Length - 1;
                while (end >= 0)
                {
                    var index = pathSpan.Slice(0, end + 1).LastIndexOf(Path.DirectorySeparatorChar);
                    if (index == -1)
                        break;

                    var subDirStringSpan = pathSpan.Slice(0, index);
                    var subDirString = subDirStringSpan.ToString();

                    if (directoryMap_new.TryGetValue(subDirString, out var lookUpNode))
                    {
                        lastTreeNode = lookUpNode;
                        break;
                    }
                    else
                    {
                        var subFolderIndex = subDirString.LastIndexOf(Path.DirectorySeparatorChar);
                        var fullPath = subDirString;
                        if (subFolderIndex != -1)
                            subDirString = subDirString.Substring(subFolderIndex + 1, subDirString.Length - 1 - subFolderIndex);
                        stackFileNames.Add((subDirString, fullPath));
                    }

                    // Move end position backward to continue search
                    end = index - 1;
                }

                // Pop the stack and build the folder structure
                for (int i = stackFileNames.Count - 1; i >= 0; i--)
                {
                    var currentInstance = stackFileNames[i];
                    var currentNode = new TreeNode(currentInstance.FolderName, NodeType.Directory, container, lastTreeNode);

                    lastTreeNode.Children.Add(currentNode);
                    lastTreeNode = currentNode;

                    directoryMap_new.Add(currentInstance.FullFolderPath, currentNode);
                }

                // Add
                var treeNode = new TreeNode(item.Value.Name, NodeType.File, container, lastTreeNode, item.Value);
                lastTreeNode.Children.Add(treeNode);
            }

            if (container is FolderProjectContainer folderProject)
            {
                foreach (var emptyDirectory in
                         folderProject.EmptyDirectories.OrderBy(
                             path => path.Count(
                                 character =>
                                     character ==
                                     Path.DirectorySeparatorChar)))
                {
                    GetNodeFromPath(
                        root,
                        container,
                        emptyDirectory);
                }

                root.ForeachNode(
                    node =>
                        node.IsIgnored =
                            node.NodeType != NodeType.Root &&
                            folderProject.IsIgnored(
                                node.GetFullPath()));
            }

            if (existingIndex == -1)
                Files.Insert(GetContainerInsertionIndex(container), root);
            else
                Files.Insert(existingIndex, root);

            Filter.Refresh();
            RestoreTreeState(container, root, state);
            if (container is FolderProjectContainer project)
                QueueFolderProjectHistoryStatusRefresh(project, root);
        }

        private void QueueFolderProjectHistoryStatusRefresh(
            FolderProjectContainer project,
            TreeNode root)
        {
            if (_historyService == null)
                return;

            if (_folderProjectHistoryStatusCache.TryGetValue(
                    project.ProjectRoot,
                    out var cachedStatus))
            {
                MarkFolderProjectHistoryChanges(cachedStatus, root);
            }

            var requestId = ++_historyStatusRequestId;
            HistoryStatusRefreshTask = RefreshFolderProjectHistoryStatusAsync(
                project,
                root,
                requestId);
        }

        private async Task RefreshFolderProjectHistoryStatusAsync(
            FolderProjectContainer project,
            TreeNode root,
            int requestId)
        {
            FolderProjectHistoryStatus status;
            try
            {
                status = await Task.Run(
                    () => _historyService!.GetDisplayStatus(
                        project.ProjectRoot));
            }
            catch (FolderProjectHistoryException)
            {
                return;
            }

            if (requestId != _historyStatusRequestId ||
                !ReferenceEquals(
                    GetPackFileCollectionRootNode(project),
                    root))
            {
                return;
            }

            _folderProjectHistoryStatusCache[project.ProjectRoot] = status;
            root.UnsavedChanged = false;
            root.ForeachNode(node => node.UnsavedChanged = false);
            MarkFolderProjectHistoryChanges(status, root);
        }

        private static void MarkFolderProjectHistoryChanges(
            FolderProjectHistoryStatus status,
            TreeNode root)
        {
            foreach (var change in status.UnrecordedChanges)
            {
                var path = change.Path.Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar);
                TreeNode? node = null;
                while (node == null && path.Length != 0)
                {
                    node = FindNodeByPath(root, path);
                    path = GetParentPath(path) ?? "";
                }

                node ??= root;
                while (node != null)
                {
                    node.UnsavedChanged = true;
                    node = node.Parent;
                }
            }
        }

        private int GetContainerInsertionIndex(PackFileContainer container)
        {
            var containers = _packFileService.GetAllPackfileContainers();
            var containerIndex = containers.FindIndex(
                item => ReferenceEquals(item, container));
            if (containerIndex == -1)
                return Files.Count;

            for (var index = containerIndex + 1;
                 index < containers.Count;
                 index++)
            {
                var nextRoot = Files.FirstOrDefault(
                    root => ReferenceEquals(
                        root.FileOwner,
                        containers[index]));
                if (nextRoot != null)
                    return Files.IndexOf(nextRoot);
            }

            return Files.Count;
        }

        private FolderProjectTreeState? CaptureTreeState(
            TreeNode? root)
        {
            if (root == null)
                return null;

            var expandedPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            root.ForeachNode(node =>
            {
                if (node.IsNodeExpanded)
                    expandedPaths.Add(node.GetFullPath());
            });

            var selected = SelectedItem?.FileOwner == root.FileOwner
                ? SelectedItem
                : null;
            return new FolderProjectTreeState(
                expandedPaths,
                selected?.Item,
                selected?.GetFullPath(),
                selected != null);
        }

        private void RestoreTreeState(
            PackFileContainer container,
            TreeNode root,
            FolderProjectTreeState? state)
        {
            if (state == null)
                return;

            root.ForeachNode(node =>
                node.IsNodeExpanded = state.ExpandedPaths.Contains(
                    node.GetFullPath()));

            TreeNode? selected = null;
            if (state.SelectedFile != null)
            {
                root.ForeachNode(node =>
                {
                    if (selected == null &&
                        ReferenceEquals(node.Item, state.SelectedFile))
                    {
                        selected = node;
                    }
                });
            }

            var selectedPath = state.SelectedPath;
            while (selected == null && selectedPath != null)
            {
                selected = FindNodeByPath(root, selectedPath);
                selectedPath = GetParentPath(selectedPath);
            }

            if (state.HadSelection)
                SelectedItem = selected ?? root;
        }

        private static TreeNode? FindNodeByPath(
            TreeNode root,
            string path)
        {
            if (path.Length == 0)
                return root;

            var current = root;
            foreach (var segment in path.Split(
                         Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = current.Children.FirstOrDefault(child =>
                    child.Name.Equals(
                        segment,
                        StringComparison.OrdinalIgnoreCase));
                if (current == null)
                    return null;
            }

            return current;
        }

        private static string? GetParentPath(string path)
        {
            if (path.Length == 0)
                return null;

            var separator = path.LastIndexOf(Path.DirectorySeparatorChar);
            return separator == -1
                ? ""
                : path[..separator];
        }

        private sealed record FolderProjectTreeState(
            HashSet<string> ExpandedPaths,
            PackFile? SelectedFile,
            string? SelectedPath,
            bool HadSelection);

        private void PackFileContainerAdded(PackFileContainerAddedEvent e)
        {
            FolderProjectTreeState? state = null;
            string? projectRoot = null;
            if (e.Container is FolderProjectContainer folderProject)
            {
                projectRoot = NormalizeProjectRoot(
                    folderProject.ProjectRoot);
                if (e.Reason ==
                    PackFileContainerAddedReason.InternalReattach)
                {
                    _detachedFolderProjectStates.TryGetValue(
                        projectRoot,
                        out state);
                }
                else
                {
                    _detachedFolderProjectStates.Remove(projectRoot);
                }
            }

            ReloadTree(e.Container, state);
            if (state != null && projectRoot != null)
                _detachedFolderProjectStates.Remove(projectRoot);
        }

        private void PackFileContainerRemoved(PackFileContainerRemovedEvent e)
        {
            var node = Files.FirstOrDefault(x => x.FileOwner == e.Container);
            if (node == null)
                return;

            if (e.Container is FolderProjectContainer folderProject)
            {
                var state = CaptureTreeState(node);
                if (state != null)
                {
                    _detachedFolderProjectStates[
                        NormalizeProjectRoot(folderProject.ProjectRoot)] =
                            state with { SelectedFile = null };
                }
            }

            Files.Remove(node);
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(projectRoot));
        }

        public void Dispose()
        {
            _historyStatusRequestId++;
            _eventHub?.UnRegister(this);
        }

        public bool AllowDrop(TreeNode node, TreeNode targetNode = null)
        {
            if (node.Item == null) // dragging a folder not supported
                return false;

            if (node.FileOwner != targetNode.FileOwner) // dragging between different packs not supported
                return false;

            if (node.FileOwner.IsCaPackFile) // dragging inside CA pack not supported
                return false;

            if (targetNode.Item != null) // dragging file onto a file not supported
                return false;

            return true;
        }

        public bool Drop(TreeNode node, TreeNode targeNode)
        {
            var container = node.FileOwner;
            var draggedFile = node.Item;
            var dropPath = targeNode.GetFullPath();

            var newFullPath = dropPath + "\\" + draggedFile.Name;
            if (newFullPath == _packFileService.GetFullPath(draggedFile, container))
                return false;

            _packFileService.MoveFile(container, draggedFile, dropPath);

            return true;
        }
    }
}
