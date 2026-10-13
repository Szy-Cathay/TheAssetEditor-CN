using System.Threading;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class StartupResourceTreeTests
{
    [Test]
    public async Task DeferredCaTree_PreservesFilesAndWemPreference_AndInitializesOnce()
    {
        var container = CreateContainer();
        using var tree = CreateTree(container);
        Assert.That(tree.Files, Is.Empty);
        var progress = new List<(long Completed, long Total)>();
        await tree.InitializeCaFilesAsync(reportProgress: (completed, total) => progress.Add((completed, total)));
        var root = tree.Files.Single();
        Assert.That(root.GetAllChildFileNodes(), Has.Count.EqualTo(4100));
        Assert.That(root.GetAllChildFileNodes().All(node => node.IsVisible && node.Parent != null), Is.True);
        Assert.That(progress.Last(), Is.EqualTo((4101L, 4101L)));
        Assert.That(root.IsReadOnlyContainer, Is.True);
        await tree.InitializeCaFilesAsync();
        Assert.That(tree.Files.Single(), Is.SameAs(root));
    }

    [Test]
    public void CancelledTree_DoesNotPublishPartiallyBuiltNodes()
    {
        using var tree = CreateTree(CreateContainer());
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAsync<OperationCanceledException>(() => tree.InitializeCaFilesAsync(
            cancellation.Token, (_, _) => cancellation.Cancel()));
        Assert.That(tree.Files, Is.Empty);
    }

    [Test]
    public async Task DisposedTree_DiscardsPendingBackgroundResult()
    {
        using var tree = CreateTree(CreateContainer());
        await tree.InitializeCaFilesAsync(reportProgress: (_, _) => tree.Dispose());
        Assert.That(tree.Files, Is.Empty);
    }

    private static PackFileContainer CreateContainer()
    {
        var container = new PackFileContainer("CA") { IsCaPackFile = true };
        for (var index = 0; index < 4100; index++)
            container.FileList[$"models\\group_{index % 4}\\file_{index}.txt"] = PackFile.CreateFromBytes($"file_{index}.txt", []);
        container.FileList["audio\\hidden.wem"] = PackFile.CreateFromBytes("hidden.wem", []);
        return container;
    }

    private static PackFileBrowserViewModel CreateTree(PackFileContainer container)
    {
        var service = new Mock<IPackFileService>();
        service.Setup(value => value.GetAllPackfileContainers()).Returns([container]);
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        settings.CurrentSettings.ShowCAWemFiles = false;
        return new PackFileBrowserViewModel(settings, Mock.Of<IContextMenuBuilder>(), service.Object,
            null, true, false, deferCaFiles: true);
    }
}
