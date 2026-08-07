using System.Collections.ObjectModel;
using System.ComponentModel;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ValueConverters;

namespace AssetEditorTests;

public class PackFileSearchFilterTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void SortingChildrenRepeatedly_DoesNotAccumulateSortRules()
    {
        var converter = new SortedCollectionViewSource
        {
            Property0 = nameof(TreeNode.NodeType),
            Property1 = nameof(TreeNode.Name),
        };
        var children = new ObservableCollection<TreeNode>();

        var firstView = (ICollectionView)converter.Convert(
            children,
            typeof(ICollectionView),
            null!,
            System.Globalization.CultureInfo.InvariantCulture);
        var secondView = (ICollectionView)converter.Convert(
            children,
            typeof(ICollectionView),
            null!,
            System.Globalization.CultureInfo.InvariantCulture);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(secondView, Is.SameAs(firstView));
            NUnitAssert.That(secondView.SortDescriptions, Has.Count.EqualTo(2));
            NUnitAssert.That(
                secondView.SortDescriptions[0].PropertyName,
                Is.EqualTo(nameof(TreeNode.NodeType)));
            NUnitAssert.That(
                secondView.SortDescriptions[1].PropertyName,
                Is.EqualTo(nameof(TreeNode.Name)));
        });
    }

    [Test]
    public void SharedPackBrowser_UsesCurrentSearchInputStyle()
    {
        var root = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Shared",
            "SharedUI",
            "BaseDialogs",
            "PackFileTree",
            "PackFileBrowserView.xaml"));
        var browserWindow = File.ReadAllText(Path.Combine(
            root,
            "Shared",
            "SharedUI",
            "BaseDialogs",
            "StandardDialog",
            "PackFile",
            "PackFileBrowserWindow.xaml.cs"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(source, Does.Not.Contain("HintedTextBox"));
            NUnitAssert.That(
                source,
                Does.Contain("Shared.PackFileBrowser.SearchHint"));
            NUnitAssert.That(
                browserWindow,
                Does.Not.Contain("AutoExapandResultsAfterLimitedCount = 50"));
        });
    }

    [Test]
    public void PackBrowserDialog_DoesNotWrapTheBrowserInADuplicatePanel()
    {
        var root = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Shared",
            "SharedUI",
            "BaseDialogs",
            "StandardDialog",
            "PackFile",
            "PackFileBrowserWindow.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("ShowTitle=\"False\""));
            NUnitAssert.That(source, Does.Not.Contain("AeSurface.Panel"));
        });
    }

    [Test]
    public void ClearingSearch_RestoresExpansionStateFromBeforeSearch()
    {
        var owner = new PackFileContainer("test.pack");
        var root = new TreeNode("test.pack", NodeType.Root, owner, null)
        {
            IsNodeExpanded = true,
        };
        var matchingFolder = new TreeNode(
            "matching",
            NodeType.Directory,
            owner,
            root)
        {
            IsNodeExpanded = false,
        };
        var previouslyExpandedFolder = new TreeNode(
            "expanded",
            NodeType.Directory,
            owner,
            root)
        {
            IsNodeExpanded = true,
        };
        matchingFolder.Children.Add(new TreeNode(
            "match.anim",
            NodeType.File,
            owner,
            matchingFolder));
        previouslyExpandedFolder.Children.Add(new TreeNode(
            "other.anim",
            NodeType.File,
            owner,
            previouslyExpandedFolder));
        root.Children.Add(matchingFolder);
        root.Children.Add(previouslyExpandedFolder);
        var filter = new SearchFilter(new ObservableCollection<TreeNode>
        {
            root,
        });

        filter.FilterText = "match";
        NUnitAssert.That(matchingFolder.IsNodeExpanded, Is.True);

        filter.FilterText = string.Empty;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.IsNodeExpanded, Is.True);
            NUnitAssert.That(matchingFolder.IsNodeExpanded, Is.False);
            NUnitAssert.That(previouslyExpandedFolder.IsNodeExpanded, Is.True);
        });
    }

    [Test]
    public void ChangingSearch_DoesNotAccumulateExpandedResultPaths()
    {
        var owner = new PackFileContainer("test.pack");
        var root = new TreeNode("test.pack", NodeType.Root, owner, null);
        var firstFolder = AddFile(root, owner, "first", "one.anim");
        var secondFolder = AddFile(root, owner, "second", "two.anim");
        var filter = new SearchFilter(new ObservableCollection<TreeNode>
        {
            root,
        });

        filter.FilterText = "one";
        NUnitAssert.That(firstFolder.IsNodeExpanded, Is.True);

        filter.FilterText = "two";

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(firstFolder.IsNodeExpanded, Is.False);
            NUnitAssert.That(secondFolder.IsNodeExpanded, Is.True);
        });
    }

    [Test]
    public void ValidatingSearchText_DoesNotRunTheTreeFilterAgain()
    {
        var owner = new PackFileContainer("test.pack");
        var root = new TreeNode("test.pack", NodeType.Root, owner, null);
        var matchingFolder = AddFile(root, owner, "matching", "match.anim");
        var filter = new SearchFilter(new ObservableCollection<TreeNode>
        {
            root,
        });

        filter.FilterText = "match";
        matchingFolder.IsNodeExpanded = false;

        _ = filter[nameof(SearchFilter.FilterText)];

        NUnitAssert.That(matchingFolder.IsNodeExpanded, Is.False);
    }

    private static TreeNode AddFile(
        TreeNode root,
        PackFileContainer owner,
        string folderName,
        string fileName)
    {
        var folder = new TreeNode(
            folderName,
            NodeType.Directory,
            owner,
            root);
        folder.Children.Add(new TreeNode(
            fileName,
            NodeType.File,
            owner,
            folder));
        root.Children.Add(folder);
        return folder;
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }
}
