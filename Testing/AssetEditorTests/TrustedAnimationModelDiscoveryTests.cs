using System.Threading.Channels;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using System.Windows;
using System.Windows.Threading;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class TrustedAnimationModelDiscoveryTests
{
    [OneTimeSetUp]
    public void InitializeLocalization() =>
        new LocalizationManager().LoadLanguage();

    [Test]
    public async Task Discovery_UsesBackgroundThreadAndReturnsEffectiveSourcesIncrementally()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var discoveryThread = callerThread;
        var sharedPath = @"models\shared.rigid_model_v2";
        var ca = CreateContainer("data.pack", TrustedAnimationModelSourceRole.CaPack);
        var reference = CreateContainer(
            "reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var untrusted = new PackFileContainer("ordinary.pack");
        AddModel(ca, sharedPath);
        AddModel(reference, sharedPath);
        AddModel(project, sharedPath);
        AddModel(ca, @"models\ca.wsmodel");
        AddModel(reference, @"models\reference.variantmeshdefinition");
        AddModel(project, @"models\project.rigid_model_v2");
        AddModel(untrusted, @"models\must-not-appear.rigid_model_v2");
        for (var index = 0; index < 260; index++)
            AddModel(ca, $@"models\ca-{index}.rigid_model_v2");

        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetAllPackfileContainers())
            .Callback(() => discoveryThread = Environment.CurrentManagedThreadId)
            .Returns([ca, reference, untrusted, project]);
        var discovery = new TrustedAnimationModelDiscovery(
            packFileService.Object);
        var batches = new List<IReadOnlyList<TrustedAnimationModelCandidate>>();

        await foreach (var batch in discovery.DiscoverAsync(
                           CancellationToken.None))
        {
            batches.Add(batch);
        }

        var results = batches.SelectMany(batch => batch).ToList();
        var shared = results.Single(candidate =>
            string.Equals(
                candidate.Path,
                sharedPath,
                StringComparison.OrdinalIgnoreCase));
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(discoveryThread, Is.Not.EqualTo(callerThread));
            NUnitAssert.That(batches.Count, Is.GreaterThan(2));
            NUnitAssert.That(shared.SourceRole,
                Is.EqualTo(TrustedAnimationModelSourceRole.FolderProject));
            NUnitAssert.That(shared.SourcePack, Is.EqualTo("project"));
            NUnitAssert.That(results, Has.Some.Matches<TrustedAnimationModelCandidate>(
                candidate => candidate.SourceRole ==
                    TrustedAnimationModelSourceRole.ReferencePack));
            NUnitAssert.That(results, Has.Some.Matches<TrustedAnimationModelCandidate>(
                candidate => candidate.SourceRole ==
                    TrustedAnimationModelSourceRole.CaPack));
            NUnitAssert.That(results, Has.None.Matches<TrustedAnimationModelCandidate>(
                candidate => candidate.Path.Contains(
                    "must-not-appear",
                    StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public async Task Discovery_LaterContainerWinsWithinTheSameSourceRole()
    {
        var path = @"models\shared.rigid_model_v2";
        var first = CreateContainer(
            "first-reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);
        var second = CreateContainer(
            "second-reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);
        AddModel(first, path);
        AddModel(second, path);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetAllPackfileContainers())
            .Returns([first, second]);
        var discovery = new TrustedAnimationModelDiscovery(
            packFileService.Object);

        var results = new List<TrustedAnimationModelCandidate>();
        await foreach (var batch in discovery.DiscoverAsync(
                           CancellationToken.None))
        {
            results.AddRange(batch);
        }

        NUnitAssert.That(results.Single().SourcePack,
            Is.EqualTo("second-reference.pack"));
    }

    [Test]
    public void Discovery_CanBeCancelledAfterPartialResults()
    {
        var ca = CreateContainer(
            "data.pack",
            TrustedAnimationModelSourceRole.CaPack);
        for (var index = 0; index < 600; index++)
            AddModel(ca, $@"models\model-{index}.rigid_model_v2");
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetAllPackfileContainers())
            .Returns([ca]);
        var discovery = new TrustedAnimationModelDiscovery(
            packFileService.Object);

        NUnitAssert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            await using var enumerator = discovery
                .DiscoverAsync(cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);
            NUnitAssert.That(await enumerator.MoveNextAsync(), Is.True);
            NUnitAssert.That(enumerator.Current, Is.Not.Empty);
            cancellation.Cancel();
            while (await enumerator.MoveNextAsync())
            {
            }
        });
    }

    [Test]
    public async Task ViewModel_IgnoresStaleResultsAfterRestart()
    {
        var discovery = new ControllableDiscovery();
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            Mock.Of<IPackFileService>(),
            discovery);
        var stale = Candidate("stale.rigid_model_v2");
        var current = Candidate("current.rigid_model_v2");

        var firstScan = viewModel.StartModelDiscoveryAsync();
        await discovery.WaitForInvocationAsync(0);
        var secondScan = viewModel.StartModelDiscoveryAsync();
        await discovery.WaitForInvocationAsync(1);
        await discovery.WriteAsync(0, [stale]);
        discovery.Complete(0);
        await discovery.WriteAsync(1, [current]);
        discovery.Complete(1);
        await Task.WhenAll(firstScan, secondScan);

        NUnitAssert.That(viewModel.ModelCandidates.Select(item => item.Path),
            Is.EqualTo(new[] { current.Path }));
    }

    [Test]
    public async Task ViewModel_SearchesDiscoveredResults()
    {
        var discovery = new ControllableDiscovery();
        var viewModel = new TrustedAnimationPreviewViewModel(
            Mock.Of<ITrustedAnimationPreviewViewport>(),
            Mock.Of<IPackFileService>(),
            discovery);
        var visible = Candidate("visible_character.rigid_model_v2");
        var hidden = Candidate("other_character.wsmodel");

        var scan = viewModel.StartModelDiscoveryAsync();
        await discovery.WaitForInvocationAsync(0);
        await discovery.WriteAsync(0, [visible, hidden]);
        discovery.Complete(0);
        await scan;
        viewModel.ModelSearchText = "visible";

        NUnitAssert.That(
            viewModel.ModelCandidatesView
                .Cast<TrustedAnimationModelCandidate>()
                .Select(item => item.Path),
            Is.EqualTo(new[] { visible.Path }));
    }

    [Test]
    public async Task ViewModel_CloseRejectsLateResultsAndDisposesViewport()
    {
        var discovery = new ControllableDiscovery();
        var viewport = new Mock<ITrustedAnimationPreviewViewport>();
        var viewModel = new TrustedAnimationPreviewViewModel(
            viewport.Object,
            Mock.Of<IPackFileService>(),
            discovery);

        var scan = viewModel.StartModelDiscoveryAsync();
        await discovery.WaitForInvocationAsync(0);
        viewModel.Close();
        await discovery.WriteAsync(0, [Candidate("late.rigid_model_v2")]);
        discovery.Complete(0);
        await scan;

        NUnitAssert.That(viewModel.ModelCandidates, Is.Empty);
        viewport.Verify(candidate => candidate.Dispose(), Times.Once);
    }

    [Test]
    public void View_LoadedScanDoesNotFailWhileResultsAreBound()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var discovery = new ImmediateDiscovery();
                var viewport = new Mock<ITrustedAnimationPreviewViewport>();
                var viewModel = new TrustedAnimationPreviewViewModel(
                    viewport.Object,
                    Mock.Of<IPackFileService>(),
                    discovery);
                var results = new System.Windows.Controls.ListBox
                {
                    DataContext = viewModel,
                    ItemsSource = viewModel.ModelCandidatesView,
                };
                results.SetBinding(
                    System.Windows.Controls.Primitives.Selector
                        .SelectedItemProperty,
                    new System.Windows.Data.Binding(
                        nameof(TrustedAnimationPreviewViewModel
                            .SelectedModelCandidate))
                    {
                        Mode = System.Windows.Data.BindingMode.TwoWay,
                    });
                var window = new Window
                {
                    Width = 1280,
                    Height = 820,
                    Content = results,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        DispatcherPriority.ApplicationIdle);
                    viewModel.StartModelDiscoveryAsync()
                        .GetAwaiter()
                        .GetResult();
                    window.UpdateLayout();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(discovery.InvocationCount,
                            Is.EqualTo(1));
                        NUnitAssert.That(viewModel.ModelCandidates.Count,
                            Is.EqualTo(2));
                        NUnitAssert.That(viewModel.ModelScanStatus,
                            Does.Not.Contain(
                                "CollectionView").IgnoreCase);
                    });
                }
                finally
                {
                    window.Close();
                    viewModel.Close();
                }
            });
    }

    private static PackFileContainer CreateContainer(
        string name,
        TrustedAnimationModelSourceRole role)
    {
        var container = new PackFileContainer(name)
        {
            SystemFilePath = $@"C:\packs\{name}",
        };
        switch (role)
        {
            case TrustedAnimationModelSourceRole.FolderProject:
                container.Role = PackFileContainerRole.ProjectWorkspace;
                break;
            case TrustedAnimationModelSourceRole.ReferencePack:
                container.Role = PackFileContainerRole.Reference;
                break;
            case TrustedAnimationModelSourceRole.CaPack:
                container.IsCaPackFile = true;
                break;
        }
        return container;
    }

    private static void AddModel(
        PackFileContainer container,
        string path) => container.FileList[path.ToLowerInvariant()] =
        PackFile.CreateFromBytes(Path.GetFileName(path), [1]);

    private static TrustedAnimationModelCandidate Candidate(string path) =>
        new(
            PackFile.CreateFromBytes(path, [1]),
            path,
            "reference.pack",
            @"C:\packs\reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);

    private sealed class ControllableDiscovery :
        ITrustedAnimationModelDiscovery
    {
        private readonly List<Channel<IReadOnlyList<TrustedAnimationModelCandidate>>>
            _channels = [];
        private readonly List<TaskCompletionSource> _started = [];

        public IAsyncEnumerable<IReadOnlyList<TrustedAnimationModelCandidate>>
            DiscoverAsync(CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<
                IReadOnlyList<TrustedAnimationModelCandidate>>();
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_channels)
            {
                _channels.Add(channel);
                _started.Add(started);
                started.SetResult();
            }
            return ReadIgnoringCancellation(channel.Reader);
        }

        public Task WaitForInvocationAsync(int index)
        {
            lock (_channels)
            {
                if (_started.Count > index)
                    return _started[index].Task;
            }
            return WaitUntilStartedAsync(index);
        }

        public ValueTask WriteAsync(
            int index,
            IReadOnlyList<TrustedAnimationModelCandidate> batch) =>
            _channels[index].Writer.WriteAsync(batch);

        public void Complete(int index) =>
            _channels[index].Writer.TryComplete();

        private async Task WaitUntilStartedAsync(int index)
        {
            while (true)
            {
                Task? started = null;
                lock (_channels)
                {
                    if (_started.Count > index)
                        started = _started[index].Task;
                }
                if (started != null)
                {
                    await started;
                    return;
                }
                await Task.Yield();
            }
        }

        private static async IAsyncEnumerable<
            IReadOnlyList<TrustedAnimationModelCandidate>>
            ReadIgnoringCancellation(
                ChannelReader<IReadOnlyList<TrustedAnimationModelCandidate>>
                    reader)
        {
            await foreach (var batch in reader.ReadAllAsync())
                yield return batch;
        }
    }

    private sealed class ImmediateDiscovery :
        ITrustedAnimationModelDiscovery
    {
        public int InvocationCount { get; private set; }

        public async IAsyncEnumerable<
            IReadOnlyList<TrustedAnimationModelCandidate>> DiscoverAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken)
        {
            InvocationCount++;
            cancellationToken.ThrowIfCancellationRequested();
            yield return
            [
                Candidate("first.rigid_model_v2"),
                Candidate("second.wsmodel"),
            ];
            await Task.CompletedTask;
        }
    }
}
