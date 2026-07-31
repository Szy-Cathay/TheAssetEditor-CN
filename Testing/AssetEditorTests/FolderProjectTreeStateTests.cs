using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;

namespace AssetEditorTests;

public class FolderProjectTreeStateTests
{
    [Test]
    public void LoadedFolderProject_MarksGitChangesAndAncestorsChanged()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\child\changed.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(service => service.GetStatus(projectRoot.Path))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "main",
                    new string('1', 40),
                    false,
                    FolderProjectRepositoryOperationState.None,
                    [
                        new FolderProjectWorkingChange(
                            "folder/child/changed.bin",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));

        using var harness = CreateViewModelWithVersionControl(
            versionControl.Object,
            project);

        var root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.UnsavedChanged, Is.True);
            NUnitAssert.That(
                FindNode(root, "folder").UnsavedChanged,
                Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child").UnsavedChanged,
                Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child\changed.bin").UnsavedChanged,
                Is.True);
        });
    }

    [Test]
    public void CleanInternalReattach_ClearsFolderProjectChangeMarkers()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\changed.bin", [1]);
        using var original = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        var status = new FolderProjectRepositoryStatus(
            true,
            "main",
            new string('1', 40),
            false,
            FolderProjectRepositoryOperationState.None,
            [
                new FolderProjectWorkingChange(
                    "folder/changed.bin",
                    FolderProjectWorkingChangeKind.Modified |
                    FolderProjectWorkingChangeKind.Unstaged),
            ]);
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(service => service.GetStatus(projectRoot.Path))
            .Returns(() => status);
        using var harness = CreateViewModelWithVersionControl(
            versionControl.Object,
            original);
        NUnitAssert.That(
            FindNode(
                harness.ViewModel.Files.Single(),
                @"folder\changed.bin").UnsavedChanged,
            Is.True);

        harness.EventHub.Publish(new PackFileContainerRemovedEvent(original));
        status = status with { Changes = [] };
        using var reattached = FolderProjectContainer.Open(projectRoot.Path);
        harness.EventHub.Publish(new PackFileContainerAddedEvent(
            reattached,
            PackFileContainerAddedReason.InternalReattach));

        var root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.UnsavedChanged, Is.False);
            NUnitAssert.That(
                FindNode(root, "folder").UnsavedChanged,
                Is.False);
            NUnitAssert.That(
                FindNode(root, @"folder\changed.bin").UnsavedChanged,
                Is.False);
        });
    }

    [Test]
    public void UpdatedFolderProjectFile_MarksFileAndAncestorsChanged()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\child\changed.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(project);
        var changedFile = project.FileList.Values.Single();

        harness.EventHub.Publish(new PackFileContainerFilesUpdatedEvent(
            project,
            [changedFile]));

        var root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.UnsavedChanged, Is.True);
            NUnitAssert.That(
                FindNode(root, "folder").UnsavedChanged,
                Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child").UnsavedChanged,
                Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child\changed.bin").UnsavedChanged,
                Is.True);
        });
    }

    [Test]
    public void AddedAndUpdatedEvents_RestoreExpansionSelectionAndFilter()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\child\selected.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(project);
        var root = harness.ViewModel.Files.Single();
        var folder = FindNode(root, "folder");
        var child = FindNode(root, @"folder\child");
        var selected = FindNode(root, @"folder\child\selected.bin");
        root.IsNodeExpanded = true;
        folder.IsNodeExpanded = true;
        child.IsNodeExpanded = true;
        harness.ViewModel.SelectedItem = selected;
        harness.ViewModel.Filter.FilterText = "selected";

        projectRoot.Write(@"folder\child\added.bin", [2]);
        project.RefreshFromDisk();
        var added = project.FileList.Values.Single(
            file => file.Name == "added.bin");
        harness.EventHub.Publish(new PackFileContainerFilesAddedEvent(
            project,
            [added]));
        harness.EventHub.Publish(new PackFileContainerFilesUpdatedEvent(
            project,
            [project.FileList.Values.Single(
                file => file.Name == "selected.bin")]));

        root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.IsNodeExpanded, Is.True);
            NUnitAssert.That(FindNode(root, "folder").IsNodeExpanded, Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child").IsNodeExpanded,
                Is.True);
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.GetFullPath(),
                Is.EqualTo(@"folder\child\selected.bin")
                    .IgnoreCase);
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.IsSelected,
                Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child\added.bin").IsVisible,
                Is.False);
        });
    }

    [Test]
    public void ActiveFilter_RebuildKeepsCollapsedNodesAndHidesNewNonMatch()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\match.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(project);
        harness.ViewModel.Filter.FilterText = "match";
        var root = harness.ViewModel.Files.Single();
        root.IsNodeExpanded = false;
        FindNode(root, "folder").IsNodeExpanded = false;

        projectRoot.Write(@"folder\new.bin", [2]);
        project.RefreshFromDisk();
        harness.EventHub.Publish(new PackFileContainerFilesAddedEvent(
            project,
            [project.FileList.Values.Single(file => file.Name == "new.bin")]));

        root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.IsNodeExpanded, Is.False);
            NUnitAssert.That(FindNode(root, "folder").IsNodeExpanded, Is.False);
            NUnitAssert.That(
                FindNode(root, @"folder\new.bin").IsVisible,
                Is.False);
        });
    }

    [Test]
    public void RootRemovalClearingSelection_StillRestoresSelectedNode()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\selected.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(project);
        var root = harness.ViewModel.Files.Single();
        harness.ViewModel.SelectedItem = FindNode(root, @"folder\selected.bin");
        harness.ViewModel.Files.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action ==
                System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                harness.ViewModel.SelectedItem = null!;
            }
        };

        harness.EventHub.Publish(new PackFileContainerFilesUpdatedEvent(
            project,
            [project.FileList.Values.Single()]));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.GetFullPath(),
                Is.EqualTo(@"folder\selected.bin").IgnoreCase);
            NUnitAssert.That(harness.ViewModel.SelectedItem.IsSelected, Is.True);
        });
    }

    [Test]
    public void RemovedAndFolderRemovedEvents_FallBackToNearestParent()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\child\selected.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(project);
        var root = harness.ViewModel.Files.Single();
        var selected = FindNode(root, @"folder\child\selected.bin");
        harness.ViewModel.SelectedItem = selected;

        var selectedFile = selected.Item!;
        File.Delete(Path.Combine(
            projectRoot.Path,
            "folder",
            "child",
            "selected.bin"));
        project.RefreshFromDisk();
        harness.EventHub.Publish(new PackFileContainerFilesRemovedEvent(
            project,
            [selectedFile]));

        NUnitAssert.That(
            harness.ViewModel.SelectedItem.GetFullPath(),
            Is.EqualTo(@"folder\child").IgnoreCase);

        project.DeleteFolderFromDisk(@"folder\child");
        harness.EventHub.Publish(new PackFileContainerFolderRemovedEvent(
            project,
            @"folder\child"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.GetFullPath(),
                Is.EqualTo("folder").IgnoreCase);
            NUnitAssert.That(harness.ViewModel.SelectedItem.IsSelected, Is.True);
        });
    }

    [Test]
    public void MoveEvents_KeepTheFallbackSelectionAcrossRemovalAndAddition()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\moving.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(project);
        var root = harness.ViewModel.Files.Single();
        var selected = FindNode(root, @"folder\moving.bin");
        harness.ViewModel.SelectedItem = selected;
        var movingFile = selected.Item!;

        projectRoot.Write(@"other\moving.bin", [1]);
        File.Delete(Path.Combine(projectRoot.Path, "folder", "moving.bin"));
        project.RefreshFromDisk();
        var movedFile = project.FileList.Values.Single(
            file => file.Name == "moving.bin");
        harness.EventHub.Publish(new PackFileContainerFilesRemovedEvent(
            project,
            [movingFile]));
        harness.EventHub.Publish(new PackFileContainerFilesAddedEvent(
            project,
            [movedFile]));

        NUnitAssert.That(
            harness.ViewModel.SelectedItem.GetFullPath(),
            Is.EqualTo("folder").IgnoreCase);
    }

    [Test]
    public void FolderRenamedEvent_PreservesPackOrderAndRestoresCaseInsensitivePath()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"Folder\Selected.bin", [1]);
        using var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        var otherPack = new PackFileContainer("另一个 Pack");
        otherPack.FileList["other.bin"] =
            PackFile.CreateFromBytes("other.bin", [1]);
        using var harness = CreateViewModel(project, otherPack);
        var root = harness.ViewModel.Files[0];
        var selected = FindNode(root, @"folder\selected.bin");
        harness.ViewModel.SelectedItem = selected;

        var renamedPath = project.RenameDirectoryOnDisk("FOLDER", "Renamed");
        harness.EventHub.Publish(new PackFileContainerFolderRenamedEvent(
            project,
            renamedPath));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(harness.ViewModel.Files[0].FileOwner, Is.SameAs(project));
            NUnitAssert.That(
                harness.ViewModel.Files[1].FileOwner,
                Is.SameAs(otherPack));
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.GetFullPath(),
                Is.EqualTo(@"RENAMED\SELECTED.BIN")
                    .IgnoreCase);
        });
    }

    [Test]
    public void InternalReattach_RestoresTreeStateAcrossContainerInstancesByPath()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\child\selected.bin", [1]);
        using var original = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(original);
        harness.ViewModel.Filter.AutoExapandResultsAfterLimitedCount = -1;
        var root = harness.ViewModel.Files.Single();
        root.IsNodeExpanded = true;
        FindNode(root, "folder").IsNodeExpanded = true;
        FindNode(root, @"folder\child").IsNodeExpanded = true;
        var selected = FindNode(root, @"folder\child\selected.bin");
        var oldFile = selected.Item;
        harness.ViewModel.SelectedItem = selected;

        harness.EventHub.Publish(new PackFileContainerRemovedEvent(original));
        harness.ViewModel.SelectedItem = null!;
        using var reattached = FolderProjectContainer.Open(projectRoot.Path);
        harness.EventHub.Publish(new PackFileContainerAddedEvent(
            reattached,
            PackFileContainerAddedReason.InternalReattach));

        root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.FileOwner, Is.SameAs(reattached));
            NUnitAssert.That(root.IsNodeExpanded, Is.True);
            NUnitAssert.That(
                FindNode(root, "folder").IsNodeExpanded,
                Is.True);
            NUnitAssert.That(
                FindNode(root, @"folder\child").IsNodeExpanded,
                Is.True);
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.GetFullPath(),
                Is.EqualTo(@"folder\child\selected.bin")
                    .IgnoreCase);
            NUnitAssert.That(
                harness.ViewModel.SelectedItem.Item,
                Is.Not.SameAs(oldFile));
        });
    }

    [Test]
    public void UserOpenAfterRemoval_DoesNotRestoreDetachedTreeState()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write(@"folder\selected.bin", [1]);
        using var original = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        using var harness = CreateViewModel(original);
        harness.ViewModel.Filter.AutoExapandResultsAfterLimitedCount = -1;
        var root = harness.ViewModel.Files.Single();
        root.IsNodeExpanded = true;
        FindNode(root, "folder").IsNodeExpanded = true;
        harness.ViewModel.SelectedItem =
            FindNode(root, @"folder\selected.bin");

        harness.EventHub.Publish(new PackFileContainerRemovedEvent(original));
        harness.ViewModel.SelectedItem = null!;
        using var reopened = FolderProjectContainer.Open(projectRoot.Path);
        harness.EventHub.Publish(new PackFileContainerAddedEvent(reopened));

        root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.IsNodeExpanded, Is.False);
            NUnitAssert.That(
                FindNode(root, "folder").IsNodeExpanded,
                Is.False);
            NUnitAssert.That(harness.ViewModel.SelectedItem, Is.Null);
        });
    }

    [Test]
    public void InternalReattach_RestoresRootPositionAcrossContainerInstances()
    {
        using var projectRoot = new TemporaryDirectory();
        projectRoot.Write("project.bin", [1]);
        var first = new PackFileContainer("前");
        using var original = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        var last = new PackFileContainer("后");
        var containers = new List<PackFileContainer>
        {
            first,
            original,
            last,
        };
        using var harness = CreateViewModelWithContainerList(containers);

        harness.EventHub.Publish(new PackFileContainerRemovedEvent(original));
        using var reattached = FolderProjectContainer.Open(projectRoot.Path);
        containers[1] = reattached;
        harness.EventHub.Publish(new PackFileContainerAddedEvent(
            reattached,
            PackFileContainerAddedReason.InternalReattach));

        NUnitAssert.That(
            harness.ViewModel.Files.Select(node => node.FileOwner),
            Is.EqualTo(new PackFileContainer[]
            {
                first,
                reattached,
                last,
            }));
    }

    [Test]
    public void InternalReattachForDifferentRoot_DoesNotRestoreCapturedState()
    {
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        firstRoot.Write(@"folder\selected.bin", [1]);
        secondRoot.Write(@"folder\selected.bin", [2]);
        using var original = FolderProjectContainer.Create(
            firstRoot.Path,
            new FolderProjectSettings { Name = "第一个" });
        using var harness = CreateViewModel(original);
        harness.ViewModel.Filter.AutoExapandResultsAfterLimitedCount = -1;
        var root = harness.ViewModel.Files.Single();
        root.IsNodeExpanded = true;
        FindNode(root, "folder").IsNodeExpanded = true;
        harness.ViewModel.SelectedItem =
            FindNode(root, @"folder\selected.bin");

        harness.EventHub.Publish(new PackFileContainerRemovedEvent(original));
        harness.ViewModel.SelectedItem = null!;
        using var other = FolderProjectContainer.Create(
            secondRoot.Path,
            new FolderProjectSettings { Name = "第二个" });
        harness.EventHub.Publish(new PackFileContainerAddedEvent(
            other,
            PackFileContainerAddedReason.InternalReattach));

        root = harness.ViewModel.Files.Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.FileOwner, Is.SameAs(other));
            NUnitAssert.That(root.IsNodeExpanded, Is.False);
            NUnitAssert.That(
                FindNode(root, "folder").IsNodeExpanded,
                Is.False);
            NUnitAssert.That(harness.ViewModel.SelectedItem, Is.Null);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TreeNodeSelection_BindsToTreeViewItem()
    {
        var pack = new PackFileContainer("Pack");
        var node = new TreeNode("node.bin", NodeType.File, pack, null);
        var item = new TreeViewItem { DataContext = node };
        BindingOperations.SetBinding(
            item,
            TreeViewItem.IsSelectedProperty,
            new Binding(nameof(TreeNode.IsSelected))
            {
                Mode = BindingMode.TwoWay,
            });

        node.IsSelected = true;

        NUnitAssert.That(item.IsSelected, Is.True);
    }

    private static TreeNode FindNode(TreeNode root, string path)
    {
        return GetAllNodes(root)
            .Single(node => node.GetFullPath().Equals(
                path,
                StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<TreeNode> GetAllNodes(TreeNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in GetAllNodes(child))
                yield return descendant;
        }
    }

    private static TreeHarness CreateViewModel(
        params PackFileContainer[] containers)
    {
        return CreateViewModelWithContainerList(
            containers.ToList(),
            null);
    }

    private static TreeHarness CreateViewModelWithVersionControl(
        IFolderProjectVersionControlService versionControl,
        params PackFileContainer[] containers)
    {
        return CreateViewModelWithContainerList(
            containers.ToList(),
            versionControl);
    }

    private static TreeHarness CreateViewModelWithContainerList(
        List<PackFileContainer> containers,
        IFolderProjectVersionControlService? versionControl = null)
    {
        var service = new Mock<IPackFileService>();
        service.Setup(x => x.GetAllPackfileContainers())
            .Returns(containers);
        service.Setup(x => x.GetEditablePack()).Returns(containers[0]);
        service.Setup(x => x.GetFullPath(
                It.IsAny<PackFile>(),
                It.IsAny<PackFileContainer>()))
            .Returns(
                (PackFile file, PackFileContainer container) =>
                    container.FileList.Single(
                        pair => ReferenceEquals(pair.Value, file)).Key);
        var contextMenu = new Mock<IContextMenuBuilder>();
        contextMenu.Setup(x => x.Build(It.IsAny<TreeNode?>()))
            .Returns(new ObservableCollection<ContextMenuItem2>());
        var eventHub = new TestEventHub();
        return new TreeHarness(
            eventHub,
            new PackFileBrowserViewModel(
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                contextMenu.Object,
                service.Object,
                eventHub,
                true,
                false,
                versionControl));
    }

    private sealed record TreeHarness(
        TestEventHub EventHub,
        PackFileBrowserViewModel ViewModel) : IDisposable
    {
        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-tree-state-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Write(string relativePath, byte[] bytes)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
