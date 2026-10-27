#nullable enable
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System.Reflection;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxInteractiveBoundsTests
{
    [Test]
    public async Task BoundsPreparationReturnsImmediatelyAndPublishesOnlyOneCompleteResult()
    {
        var completion = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        using var bounds = new VfxInteractiveBounds(Plan(), _ => { starts++; return completion.Task; });
        Assert.That(starts, Is.EqualTo(1));
        Assert.That(bounds.TryTake(out _), Is.False, "Polling cannot wait for unfinished range simulation.");
        completion.SetResult(DelayedBoundsPreview.Expected);
        Assert.That(await Take(bounds), Is.EqualTo(DelayedBoundsPreview.Expected));
        Assert.That(bounds.TryTake(out _), Is.False, "A completed range has no per-loop successor or duplicate notification.");
        Assert.That(starts, Is.EqualTo(1));
        await bounds.StopAsync();
    }

    [Test]
    public async Task DisposalCancelsWorkAndRejectsItsLateResult()
    {
        var completion = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var bounds = new VfxInteractiveBounds(Plan(), token => { captured = token; return completion.Task; });
        bounds.Dispose();
        Assert.That(captured.IsCancellationRequested, Is.True);
        completion.SetResult(DelayedBoundsPreview.Expected);
        await bounds.StopAsync();
        Assert.That(bounds.TryTake(out _), Is.False, "An old snapshot cannot publish after reload/disposal even if work ignored cancellation.");
    }

    [Test]
    public async Task BoundsAndPlaybackPreparationShareTheExistingSingleWorkerPermit()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = VfxInteractivePrewarm.RunPreparation(_ => release.Task, CancellationToken.None);
        var starts = 0;
        using var queued = new VfxInteractiveBounds(Plan(), _ => { starts++; return Task.FromResult<BoundingBox?>(DelayedBoundsPreview.Expected); });
        Assert.That(starts, Is.Zero);
        Assert.That(queued.TryTake(out _), Is.False);
        release.SetResult(true);
        await occupied;
        Assert.That(await Take(queued), Is.EqualTo(DelayedBoundsPreview.Expected));
        Assert.That(starts, Is.EqualTo(1));
        await queued.StopAsync();
    }

    [Test]
    public async Task FailedBoundsAreObservedWithoutPublishingAPartialValue()
    {
        var expected = new InvalidOperationException("DIAG failed optional bounds");
        using var bounds = new VfxInteractiveBounds(Plan(), _ => Task.FromException<BoundingBox?>(expected));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (bounds.Failure == null && DateTime.UtcNow < deadline) { bounds.TryTake(out _); await Task.Delay(1); }
        Assert.That(bounds.Failure, Is.SameAs(expected));
        Assert.That(bounds.TryTake(out _), Is.False);
        await bounds.StopAsync();
    }

    [Test]
    public async Task BackgroundEstimatePreservesSynchronousBoundsAndIndependentPlayback()
    {
        var plan = Plan();
        Assert.That(VfxInteractivePrewarm.CanPrepare(plan), Is.False, "Nonnegative sources keep the existing simulation prewarm guard.");
        var expected = VfxPreviewBounds.Estimate(plan);
        using var preparation = new VfxInteractiveBounds(plan);
        var actual = new TotalWarVfxSimulation(plan);
        var reference = new TotalWarVfxSimulation(plan);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        foreach (var time in new[] { .1f, .25f, 1f, 2f })
        {
            actual.Seek(time, Matrix.Identity);
            reference.Seek(time, Matrix.Identity);
            Assert.That(actual.Frame(camera), Is.EqualTo(reference.Frame(camera)), "Range estimation must not consume playback random state.");
        }
        Assert.That(await Take(preparation), Is.EqualTo(expected));
        await preparation.StopAsync();
    }

    [Test]
    public async Task EarlyAndLatePlaybackClonesObservePublishedImmutableBounds()
    {
        var type = typeof(EffekseerVfxPreview);
        var resourcesType = type.GetNestedType("PlaybackResources", BindingFlags.NonPublic)!;
        var constructor = resourcesType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var layers = Array.CreateInstance(constructor.GetParameters()[2].ParameterType.GetElementType()!, 0);
        var plan = Plan();
        var resources = constructor.Invoke([null, plan, layers, Array.Empty<int>(), null, plan.Diagnostics, "DIAG-immutable-bounds.xml"]);
        using var preview = new EffekseerVfxPreview(null!);
        type.GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(preview, resources);
        var completion = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var preparation = new VfxInteractiveBounds(plan, _ => completion.Task);
        type.GetField("_interactiveBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(preview, preparation);
        using var early = preview.CreatePlaybackInstance();
        Assert.That(early, Is.Not.Null);
        Assert.That(early!.PreviewBounds, Is.Null);
        completion.SetResult(DelayedBoundsPreview.Expected);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (preview.PreviewBounds == null && DateTime.UtcNow < deadline)
        {
            preview.PrepareInteractiveFrame(Matrix.Identity, null);
            await Task.Delay(1);
        }
        using var late = preview.CreatePlaybackInstance();
        Assert.That(preview.PreviewBounds, Is.EqualTo(DelayedBoundsPreview.Expected));
        Assert.That(early.PreviewBounds, Is.EqualTo(preview.PreviewBounds));
        Assert.That(late!.PreviewBounds, Is.EqualTo(preview.PreviewBounds));
    }

    [Test]
    public void LateBoundsNotifyOnceWithoutRestartingOrFreezingInteractivePlayback()
    {
        var preview = new DelayedBoundsPreview();
        var device = new Mock<IDeviceResolver>();
        using var component = new VfxPreviewComponent(new Factory(preview), device.Object, null!);
        var notifications = 0;
        component.Reloaded += () => notifications++;
        component.Reload(new([], "DIAG-late-bounds.xml", _ => null));
        component.Update(new(TimeSpan.FromSeconds(.1), TimeSpan.FromSeconds(.1)));
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(component.CurrentTime, Is.EqualTo(.1f));

        preview.PublishBounds = true;
        component.Update(new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.1)));
        Assert.Multiple(() =>
        {
            Assert.That(component.PreviewBounds, Is.EqualTo(DelayedBoundsPreview.Expected));
            Assert.That(notifications, Is.EqualTo(2), "Late auto-framing needs one completion notification while IsPreparing stays false.");
            Assert.That(component.CurrentTime, Is.EqualTo(.2f), "Bounds work never owns the playback clock.");
            Assert.That(preview.Reloads, Is.EqualTo(1));
        });
        component.Update(new(TimeSpan.FromSeconds(.3), TimeSpan.FromSeconds(.1)));
        Assert.That(notifications, Is.EqualTo(2), "Completed bounds do not reframe on every update.");
        Assert.That(preview.Seeks, Is.EqualTo(new[] { .1f, .2f, .3f }));
    }

    private sealed class Factory(IVfxPreview preview) : IVfxPreviewFactory
    {
        public IVfxPreview Create(GraphicsDevice device) => preview;
    }

    private static VfxConversion Plan() => VfxSpriteLowOpacityCullingTests.Conversion();

    private static async Task<BoundingBox?> Take(VfxInteractiveBounds preparation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (preparation.TryTake(out var bounds)) return bounds;
            Assert.That(preparation.Failure, Is.Null);
            await Task.Delay(1);
        }
        Assert.Fail("Independent range worker did not complete.");
        return null;
    }

    private sealed class DelayedBoundsPreview : IVfxPreview
    {
        internal static readonly BoundingBox Expected = new(new(-2, 0, -3), new(4, 8, 5));
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; } = [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => 1;
        public int Reloads { get; private set; }
        public bool PublishBounds { get; set; }
        public BoundingBox? PreviewBounds { get; private set; }
        public List<float> Seeks { get; } = [];
        public void Reload(VfxPreviewSnapshot snapshot) { Reloads++; PreviewBounds = null; }
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? history)
        {
            if (PublishBounds) PreviewBounds = Expected;
        }
        public void Advance(float seconds, Matrix attachment) { }
        public void Seek(float seconds, Matrix attachment) => Seeks.Add(seconds);
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
}
