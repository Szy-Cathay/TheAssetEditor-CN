#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxResourceDispatchTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void RealPreviewDispatchesItsRecursiveResourceCapturesWithoutViewportPumps()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var owner = Environment.CurrentManagedThreadId;
        var source = VfxLibraryVisibilityTests.Source("");
        var composition = VfxDocument.CreateComposition();
        composition.AddReference("child", VfxDocument.Read(source)).Apply();
        composition.AddReference("child", VfxDocument.Read(source)).Apply();
        var captures = new List<string>();
        var reads = new ConcurrentQueue<string>();
        var snapshot = new VfxPreviewSnapshot(composition.Write(), "vfx/production-dispatch-DIAG.xml",
            _ => throw new AssertionException("Legacy owner read used."))
        {
            CaptureResourceRead = path =>
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
                captures.Add(path);
                return () =>
                {
                    Assert.That(Environment.CurrentManagedThreadId, Is.Not.EqualTo(owner));
                    reads.Enqueue(path);
                    return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? source : [1, 2, 3];
                };
            },
        };
        // Conversion needs no device, Window, Application or scene/frame preparation.
        using var preview = new EffekseerVfxPreview(null!);
        Assert.That(preview.BeginInteractiveReload(snapshot), Is.False);
        var conversion = (VfxInteractiveConversion)typeof(EffekseerVfxPreview)
            .GetField("_reloadConversion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        var pending = (Task<VfxPreparedConversion>)Pending(conversion);
        var frame = new DispatcherFrame();
        var started = Stopwatch.GetTimestamp();
        var poll = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) =>
        {
            if (pending.IsCompleted || Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5)) frame.Continue = false;
        };
        poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { poll.Stop(); }
        Assert.That(pending.IsCompleted, Is.True, "The real preview must dispatch dependency captures without ContinueInteractiveReload or TryTake.");
        var prepared = pending.GetAwaiter().GetResult();
        Assert.That(prepared.Conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(captures.Select(path => path.Replace('\\', '/')), Is.EquivalentTo(new[] { "vfx/child.xml", "vfx/texture.dds" }));
        Assert.That(reads, Is.EquivalentTo(captures));
        Assert.That(preview.HasPendingInteractiveReload, Is.True, "CPU resources alone must not publish a partially uploaded playback.");
    }

    [Test]
    public void RecursiveResourceDemandFinishesWithoutWaitingForViewportFrames()
    {
        var owner = Environment.CurrentManagedThreadId;
        var source = VfxLibraryVisibilityTests.Source("");
        var composition = VfxDocument.CreateComposition();
        composition.AddReference("child", VfxDocument.Read(source)).Apply();
        composition.AddReference("child", VfxDocument.Read(source)).Apply();
        var requests = new List<string>();
        var queue = new OwnerQueue();
        var snapshot = new VfxPreviewSnapshot(composition.Write(), "vfx/dispatch-DIAG.xml", path =>
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
            requests.Add(path);
            return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? source : [1, 2, 3];
        });
        using var helper = new VfxInteractiveConversion(snapshot, false, scheduleOwnerWork: queue.Post);
        FinishOwnerWork(helper, queue);
        Assert.That(helper.TryTake(out var prepared), Is.True);
        Assert.That(prepared!.Conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(requests.Select(path => path.Replace('\\', '/')), Is.EquivalentTo(new[] { "vfx/child.xml", "vfx/texture.dds" }));
    }

    [Test]
    public void OwnerCallbacksCoalesceQueuedCapturesAndReadEachDependencyOnlyOnce()
    {
        var queue = new OwnerQueue();
        var captures = new List<string>();
        var owner = Environment.CurrentManagedThreadId;
        var snapshot = new VfxPreviewSnapshot(VfxLibraryVisibilityTests.Source(""), "vfx/coalesce-DIAG.xml", _ => throw new AssertionException("Legacy read used."))
        {
            CaptureResourceRead = path =>
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
                captures.Add(path);
                return () =>
                {
                    Assert.That(Environment.CurrentManagedThreadId, Is.Not.EqualTo(owner));
                    return new byte[] { 1, 2, 3 };
                };
            },
        };
        var paths = new[] { "unused/a-DIAG", "unused/b-DIAG", @"vfx\texture.dds" };
        using var helper = new VfxInteractiveConversion(snapshot, false, paths, scheduleOwnerWork: queue.Post);
        Assert.That(queue.Count, Is.EqualTo(1), "One pending owner callback must cover all pre-captured dependency demands.");
        FinishOwnerWork(helper, queue);
        Assert.That(captures, Is.EquivalentTo(paths));
        Assert.That(queue.MaximumCount, Is.EqualTo(1), "A worker request arriving during a pump must merge with that callback.");
    }

    [Test]
    public void ScheduledCaptureBudgetYieldsAfterOneSlowAtomicRead()
    {
        var queue = new OwnerQueue();
        var reads = new List<string>();
        var snapshot = new VfxPreviewSnapshot(VfxLibraryVisibilityTests.Source(""), "vfx/budget-dispatch-DIAG.xml", path =>
        {
            reads.Add(path);
            Thread.Sleep(5);
            return [1, 2, 3];
        });
        using var helper = new VfxInteractiveConversion(snapshot, false,
            ["unused/a-DIAG", "unused/b-DIAG", @"vfx\texture.dds"], scheduleOwnerWork: queue.Post);
        Assert.That(queue.RunOne(), Is.True);
        Assert.That(reads, Is.EqualTo(new[] { "unused/a-DIAG" }), "Do not drain more resource work after the original two millisecond budget expires.");
        Assert.That(queue.Count, Is.EqualTo(1), "Remaining captures need a later owner turn, without a busy wait or frame dependency.");
        FinishOwnerWork(helper, queue);
        Assert.That(reads, Has.Count.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ScheduledMissingAndThrowingCapturesKeepOriginalDiagnostics(bool throwing)
    {
        var queue = new OwnerQueue();
        byte[]? Read(string _) => throwing ? throw new InvalidDataException("DIAG dispatch read failed") : null;
        var snapshot = new VfxPreviewSnapshot(VfxLibraryVisibilityTests.Source(""), "vfx/failure-dispatch-DIAG.xml", Read);
        var expected = TotalWarVfxConverter.Convert(snapshot);
        using var helper = new VfxInteractiveConversion(snapshot, false, scheduleOwnerWork: queue.Post);
        FinishOwnerWork(helper, queue);
        Assert.That(helper.TryTake(out var prepared), Is.True);
        Assert.That(prepared!.Conversion.Diagnostics, Is.EqualTo(expected.Diagnostics));
        Assert.That(prepared.Conversion.Emitters, Is.Empty);
    }

    [Test]
    public void DisposedScheduledCallbacksNeverReadOrPublishResources()
    {
        var queue = new OwnerQueue();
        var reads = 0;
        var helper = new VfxInteractiveConversion(new(VfxLibraryVisibilityTests.Source(""), "vfx/cancel-dispatch-DIAG.xml",
            _ => { reads++; return [1]; }), false, [@"vfx\texture.dds"], scheduleOwnerWork: queue.Post);
        try
        {
            Assert.That(queue.Count, Is.EqualTo(1));
            helper.Dispose();
            while (queue.RunOne()) { }
            WaitUntil(() => Pending(helper).IsCompleted);
            Assert.That(reads, Is.Zero);
            Assert.That(helper.TryTake(out _), Is.False);
            Assert.That(Pending(helper).IsCanceled, Is.True);
        }
        finally { helper.Dispose(); }
    }

    [Test]
    public void UnavailableOwnerSchedulerFallsBackToExplicitPumps()
    {
        var posts = 0;
        var reads = 0;
        using var helper = new VfxInteractiveConversion(new(VfxLibraryVisibilityTests.Source(""), "vfx/unavailable-dispatch-DIAG.xml",
            _ => { reads++; return [1]; }), false, [@"vfx\texture.dds"], scheduleOwnerWork: _ =>
            {
                posts++;
                throw new InvalidOperationException("DIAG owner no longer accepts dispatches.");
            });
        WaitUntil(() => helper.TryTake(out _));
        Assert.That(reads, Is.EqualTo(1));
        Assert.That(posts, Is.EqualTo(1));
    }

    private static Task Pending(VfxInteractiveConversion helper) => (Task)typeof(VfxInteractiveConversion)
        .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helper)!;

    private static void FinishOwnerWork(VfxInteractiveConversion helper, OwnerQueue queue)
        => WaitUntil(() => { queue.RunOne(); return Pending(helper).IsCompleted; });

    private static void WaitUntil(Func<bool> condition)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
        {
            if (condition()) return;
            Thread.Sleep(1);
        }
        Assert.Fail("Owner-dispatched conversion must complete without a viewport preparation/frame pump.");
    }

    private sealed class OwnerQueue
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private int _maximumCount;
        internal int Count => _queue.Count;
        internal int MaximumCount => Volatile.Read(ref _maximumCount);
        internal void Post(Action action)
        {
            _queue.Enqueue(action);
            Interlocked.Exchange(ref _maximumCount, Math.Max(Count, MaximumCount));
        }
        internal bool RunOne()
        {
            if (!_queue.TryDequeue(out var action)) return false;
            action();
            return true;
        }
    }
}
