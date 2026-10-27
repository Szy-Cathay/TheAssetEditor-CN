using GameWorld.Core.WpfWindow;

namespace GameWorld.Core.Test.WpfWindow;

public class D3D11FrameDiagnosticsTests
{
    [Test]
    public void MissingOrRelativeOutputAndOutOfBoundsDurationDoNotEnableDiagnostics()
    {
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(null, null), Is.Null);
        Assert.That(D3D11FrameDiagnosticOptions.TryParse("relative-artifacts", "25"), Is.Null);
        var output = Path.Combine(Path.GetTempPath(), "aecn-frame-diagnostic-tests");
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(output, "19.9"), Is.Null);
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(output, "30.1"), Is.Null);
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(output, "invalid"), Is.Null);
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(output, null)!.Duration, Is.EqualTo(TimeSpan.FromSeconds(25)));
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(output, "20")!.Duration, Is.EqualTo(TimeSpan.FromSeconds(20)));
        Assert.That(D3D11FrameDiagnosticOptions.TryParse(output, "30")!.Duration, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void OrdinaryHostEligibilityStopsBeforeConfigurationOrCollectorCreation()
    {
        Assert.That(D3D11HostFrameDiagnostics.TryCreate(false, null), Is.Null);
    }

    [Test]
    public void DisabledFactoryAndSteadyRecordingDoNotAllocatePerCallback()
    {
        var collector = new D3D11FrameTimingCollector(2048);
        var sample = new D3D11FrameTiming { RenderCalls = 1, PresentCalls = 1 };
        for (var index = 0; index < 32; index++)
        {
            D3D11HostFrameDiagnostics.TryCreate(false, null);
            collector.Record(sample);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1024; index++)
        {
            D3D11HostFrameDiagnostics.TryCreate(false, null);
            collector.Record(sample);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(collector.Complete().Callbacks, Is.EqualTo(1056));
    }

    [Test]
    public void BoundedBufferCountsDroppedCallbacksWithoutLosingAggregateWork()
    {
        var collector = new D3D11FrameTimingCollector(2);
        collector.Record(new() { CallbackTimestamp = 100, RenderCalls = 1, PresentAttempts = 1, PresentCalls = 1, RenderTicks = 20, QueryWaitTicks = 30, AllocatedBytes = 400 });
        collector.Record(new() { CallbackTimestamp = 200, HiddenSkipped = true });
        collector.Record(new() { CallbackTimestamp = 300, PresentAttempts = 1, PresentCalls = 1, QueryWaitTicks = 40, ResourceRebuilds = 1 });

        var report = collector.Complete();
        Assert.Multiple(() =>
        {
            Assert.That(report.Samples, Has.Length.EqualTo(2));
            Assert.That(report.Callbacks, Is.EqualTo(3));
            Assert.That(report.DroppedSamples, Is.EqualTo(1));
            Assert.That(report.RenderCalls, Is.EqualTo(1));
            Assert.That(report.PresentCalls, Is.EqualTo(2));
            Assert.That(report.HiddenCallbacks, Is.EqualTo(1));
            Assert.That(report.ResourceRebuilds, Is.EqualTo(1));
            Assert.That(report.QueryWaitTicks, Is.EqualTo(70));
            Assert.That(report.AllocatedBytes, Is.EqualTo(400));
        });
    }

    [Test]
    public void CompletionIsAnImmutableOneTimeSnapshotAndStopsFurtherRecording()
    {
        var collector = new D3D11FrameTimingCollector(4);
        collector.Record(new() { CallbackTimestamp = 100, IsVisible = true, Width = 1920, Height = 1080, CompositionTicks = 42, Gc0 = 1 });
        var first = collector.Complete();
        collector.Record(new() { CallbackTimestamp = 200, RenderCalls = 99 });
        var second = collector.Complete();
        Assert.That(second, Is.SameAs(first));
        Assert.That(first.Callbacks, Is.EqualTo(1));
        Assert.That(first.Samples.Single().Width, Is.EqualTo(1920));
        Assert.That(first.Samples.Single().CompositionTicks, Is.EqualTo(42));
        Assert.That(first.Samples.Single().Gc0, Is.EqualTo(1));
    }

    [Test]
    public void RawCallbacksDistinguishDuplicatePresentationFromRenderAndHiddenSkip()
    {
        var collector = new D3D11FrameTimingCollector(4);
        collector.Record(new() { CompositionTicks = 100, IsVisible = true, RenderCalls = 1, PresentAttempts = 1, PresentCalls = 1 });
        collector.Record(new() { CompositionTicks = 100, IsVisible = true, PresentAttempts = 1, PresentCalls = 1 });
        collector.Record(new() { CompositionTicks = 200, HiddenSkipped = true });
        var result = collector.Complete();
        Assert.That(result.Callbacks, Is.EqualTo(3));
        Assert.That(result.RenderCalls, Is.EqualTo(1));
        Assert.That(result.PresentCalls, Is.EqualTo(2));
        Assert.That(result.HiddenCallbacks, Is.EqualTo(1));
        Assert.That(result.Samples[0].CompositionTicks, Is.EqualTo(result.Samples[1].CompositionTicks));

        var interval = new D3D11FrameInterval();
        var first = default(D3D11FrameTiming);
        interval.Begin(ref first, 100, 1, 1000, 2, 0, 0);
        var next = default(D3D11FrameTiming);
        interval.Begin(ref next, 200, 1, 1600, 3, 0, 0);
        Assert.That(first.HasPreviousCallback, Is.False);
        Assert.That(next.CallbackIntervalTicks, Is.EqualTo(100));
        Assert.That(next.IntervalAllocatedBytes, Is.EqualTo(600), "Include UI work between rendering callbacks, not just the next callback's body.");
        Assert.That(next.IntervalGc0, Is.EqualTo(1));
        var otherThread = default(D3D11FrameTiming);
        interval.Begin(ref otherThread, 300, 2, 9000, 3, 0, 0);
        Assert.That(otherThread.HasPreviousCallback, Is.False, "Per-thread allocation counters must not be subtracted across threads.");
    }
}
