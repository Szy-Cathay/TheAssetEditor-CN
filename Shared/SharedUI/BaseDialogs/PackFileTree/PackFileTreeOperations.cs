using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    public sealed class PackFileTreeClipboard
    {
        internal IReadOnlyList<PackFileTreeClipboardSelection> Selections
        {
            get;
            private set;
        } = [];

        internal void Set(
            IReadOnlyList<PackFileTreeClipboardSelection> selections) =>
            Selections = selections;
    }

    internal sealed record PackFileTreeClipboardSelection(
        string RootName,
        IReadOnlyList<PackFileTreeClipboardEntry> Entries);

    internal sealed record PackFileTreeClipboardEntry(
        string RelativePath,
        bool IsDirectory,
        byte[]? Data);

    public sealed class PackFileTreeOperations
    {
        private readonly IPackFileService _packFileService;
        private readonly PackFileTreeClipboard _clipboard;
        private readonly Func<bool> _confirmDelete;

        public PackFileTreeOperations(
            IPackFileService packFileService,
            PackFileTreeClipboard clipboard)
            : this(
                packFileService,
                clipboard,
                ShowDeleteConfirmation)
        {
        }

        internal PackFileTreeOperations(
            IPackFileService packFileService,
            PackFileTreeClipboard clipboard,
            Func<bool> confirmDelete)
        {
            _packFileService = packFileService;
            _clipboard = clipboard;
            _confirmDelete = confirmDelete;
        }

        public bool CanCopy(IReadOnlyList<TreeNode> nodes) =>
            Normalize(nodes).Count != 0;

        public void Copy(IReadOnlyList<TreeNode> nodes)
        {
            var selectedNodes = Normalize(nodes);
            if (selectedNodes.Count == 0)
                return;

            using (new WaitCursor())
            {
                _clipboard.Set(selectedNodes
                    .Select(CreateClipboardSelection)
                    .ToList());
            }
        }

        public bool CanPaste(TreeNode? target) =>
            target is { NodeType: not NodeType.File } &&
            _clipboard.Selections.Count != 0 &&
            ReferenceEquals(
                _packFileService.GetEditablePack(),
                target.FileOwner) &&
            !target.FileOwner.IsCaPackFile;

        public void Paste(TreeNode target)
        {
            if (!CanPaste(target))
                return;

            var occupiedNames = target.Children
                .Select(child => child.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            using (new WaitCursor())
            {
                foreach (var selection in _clipboard.Selections)
                {
                    var destinationName = CreateUniqueName(
                        selection.RootName,
                        occupiedNames);
                    occupiedNames.Add(destinationName);
                    PasteSelection(target, selection, destinationName);
                }
            }
        }

        public bool CanDelete(IReadOnlyList<TreeNode> nodes)
        {
            var selectedNodes = Normalize(nodes);
            return selectedNodes.Count != 0 &&
                   selectedNodes.All(node =>
                       !node.FileOwner.IsCaPackFile &&
                       ReferenceEquals(
                           _packFileService.GetEditablePack(),
                           node.FileOwner));
        }

        public void Delete(IReadOnlyList<TreeNode> nodes)
        {
            var selectedNodes = Normalize(nodes);
            if (!CanDelete(selectedNodes))
                return;

            if (!_confirmDelete())
                return;

            using (new WaitCursor())
            {
                foreach (var node in selectedNodes)
                {
                    if (node.NodeType == NodeType.File)
                    {
                        _packFileService.DeleteFile(
                            node.FileOwner,
                            node.Item!);
                    }
                    else
                    {
                        _packFileService.DeleteFolder(
                            node.FileOwner,
                            node.GetFullPath());
                    }
                }
            }
        }

        public bool CanMove(
            IReadOnlyList<TreeNode> nodes,
            TreeNode? target)
        {
            if (target is not { NodeType: not NodeType.File } ||
                target.FileOwner.IsCaPackFile ||
                !ReferenceEquals(
                    _packFileService.GetEditablePack(),
                    target.FileOwner))
            {
                return false;
            }

            var selectedNodes = Normalize(nodes);
            if (selectedNodes.Count == 0 ||
                selectedNodes.Any(node =>
                    !ReferenceEquals(node.FileOwner, target.FileOwner) ||
                    IsNodeOrDescendant(node, target)))
            {
                return false;
            }

            var movingNodes = selectedNodes
                .Where(node => !ReferenceEquals(node.Parent, target))
                .ToList();
            if (movingNodes.Count == 0)
                return false;

            var destinationNames = new HashSet<string>(
                target.Children
                    .Where(child => !movingNodes.Contains(child))
                    .Select(child => child.Name),
                StringComparer.OrdinalIgnoreCase);
            return movingNodes.All(node =>
                destinationNames.Add(node.Name));
        }

        public void Move(
            IReadOnlyList<TreeNode> nodes,
            TreeNode target)
        {
            var selectedNodes = Normalize(nodes)
                .Where(node => !ReferenceEquals(node.Parent, target))
                .ToList();
            if (!CanMove(selectedNodes, target))
                return;

            var destinationPath = target.GetFullPath();
            using (new WaitCursor())
            {
                foreach (var node in selectedNodes)
                {
                    if (node.NodeType == NodeType.File)
                    {
                        _packFileService.MoveFile(
                            node.FileOwner,
                            node.Item!,
                            destinationPath);
                    }
                    else
                    {
                        _packFileService.MoveFolder(
                            node.FileOwner,
                            node.GetFullPath(),
                            destinationPath);
                    }
                }
            }
        }

        public static IReadOnlyList<TreeNode> Normalize(
            IEnumerable<TreeNode> nodes)
        {
            var selected = nodes
                .Where(node => node.NodeType != NodeType.Root)
                .Distinct()
                .ToList();
            var selectedSet = selected.ToHashSet();
            return selected
                .Where(node => !HasSelectedAncestor(node, selectedSet))
                .ToList();
        }

        private static bool HasSelectedAncestor(
            TreeNode node,
            HashSet<TreeNode> selected)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (selected.Contains(parent))
                    return true;
                parent = parent.Parent;
            }
            return false;
        }

        private static bool IsNodeOrDescendant(
            TreeNode ancestor,
            TreeNode node)
        {
            TreeNode? current = node;
            while (current != null)
            {
                if (ReferenceEquals(ancestor, current))
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private static PackFileTreeClipboardSelection
            CreateClipboardSelection(TreeNode node)
        {
            if (node.NodeType == NodeType.File)
            {
                return new PackFileTreeClipboardSelection(
                    node.Name,
                    [
                        new PackFileTreeClipboardEntry(
                            node.Name,
                            false,
                            node.Item!.DataSource.ReadData()),
                    ]);
            }

            var sourcePath = node.GetFullPath();
            var entries = new List<PackFileTreeClipboardEntry>();
            node.ForeachNode(current =>
            {
                var relativePath = node.Name +
                    current.GetFullPath()[sourcePath.Length..];
                entries.Add(new PackFileTreeClipboardEntry(
                    relativePath,
                    current.NodeType == NodeType.Directory,
                    current.Item?.DataSource.ReadData()));
            });
            return new PackFileTreeClipboardSelection(
                node.Name,
                entries);
        }

        private void PasteSelection(
            TreeNode target,
            PackFileTreeClipboardSelection selection,
            string destinationName)
        {
            var targetPath = target.GetFullPath();
            var writes = new List<NewPackFileEntry>();
            foreach (var entry in selection.Entries.Where(
                         item => !item.IsDirectory))
            {
                var relativePath = ReplaceRootName(
                    entry.RelativePath,
                    selection.RootName,
                    destinationName);
                var fullPath = Path.Combine(targetPath, relativePath);
                writes.Add(new NewPackFileEntry(
                    Path.GetDirectoryName(fullPath) ?? "",
                    new PackFile(
                        Path.GetFileName(fullPath),
                        new MemorySource(entry.Data!))));
            }

            if (writes.Count != 0)
            {
                _packFileService.AddFilesToPack(
                    target.FileOwner,
                    writes,
                    overwriteExisting: false);
            }

            foreach (var entry in selection.Entries
                         .Where(item => item.IsDirectory)
                         .OrderBy(item => item.RelativePath.Length))
            {
                var relativePath = ReplaceRootName(
                    entry.RelativePath,
                    selection.RootName,
                    destinationName);
                var fullPath = Path.Combine(targetPath, relativePath);
                if (!target.FileOwner.FileList.Keys.Any(path =>
                        path.StartsWith(
                            fullPath + "\\",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    _packFileService.CreateFolder(
                        target.FileOwner,
                        fullPath);
                }
            }
        }

        private static string ReplaceRootName(
            string relativePath,
            string sourceName,
            string destinationName) =>
            destinationName + relativePath[sourceName.Length..];

        private static string CreateUniqueName(
            string sourceName,
            HashSet<string> occupiedNames)
        {
            if (!occupiedNames.Contains(sourceName))
                return sourceName;

            var extension = Path.GetExtension(sourceName);
            var baseName = Path.GetFileNameWithoutExtension(sourceName);
            if (string.IsNullOrEmpty(extension))
                baseName = sourceName;
            var candidate = $"{baseName}_copy{extension}";
            var suffix = 2;
            while (occupiedNames.Contains(candidate))
            {
                candidate = $"{baseName}_copy_{suffix}{extension}";
                suffix++;
            }
            return candidate;
        }

        private static bool ShowDeleteConfirmation() =>
            UiMessageBoxBridge.Show(
                LocalizationManager.Instance.Get("Msg.DeleteFile"),
                "",
                UiMessageBoxButtonSet.YesNo,
                UiMessageBoxIcon.Question) == UiMessageBoxResult.Yes;
    }
}
