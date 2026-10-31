#nullable enable
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxInteractiveBoundsPublicationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly BoundingBox Expected = new(new(-4, -2, -3), new(7, 8, 9));

    [Test]
    public async Task ReadySimulationPublishesBeforeAccurateBoundsAndLateBoundsNotifyExactlyOnce()
    {
        using var preview = PreparedPreview(out var plan, out var original);
        var completion = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bounds = new VfxInteractiveBounds(plan, _ => completion.Task);
        Set(preview, "_reloadBounds", bounds);
        try
        {
            var finished = preview.ContinueInteractiveReload(new(.1f, Matrix.Identity));
            Assert.That(finished, Is.True, "A ready exact simulation cannot wait for unfinished optional range estimation.");
            Assert.That(preview.PreviewBounds, Is.Null, "The new version must not claim the old version's bounds as its accurate result.");
            Assert.That(preview.ParticleCount, Is.GreaterThan(0));
            Assert.That(Get(preview, "_simulation"), Is.SameAs(original));
            var expected = new TotalWarVfxSimulation(plan); expected.Seek(.1f, Matrix.Identity);
            var camera = VfxSpriteLowOpacityCullingTests.Camera();
            Assert.That(original.ProjectedFrame(camera, false), Is.EqualTo(expected.ProjectedFrame(camera, false)));
            var devices = new Mock<IDeviceResolver>();
            using var component = new VfxPreviewComponent(new Factory(preview), devices.Object, null!);
            Set(component, "_preview", preview); Set(component, "_time", .1f);
            var notices = 0; component.Reloaded += () => notices++;
            component.Update(new(TimeSpan.FromSeconds(.1), TimeSpan.Zero));
            Assert.That(notices, Is.Zero);
            completion.SetResult(Expected);
            await Until(() => { component.Update(new(TimeSpan.FromSeconds(.1), TimeSpan.Zero)); return preview.PreviewBounds == Expected; });
            Assert.That(notices, Is.EqualTo(1));
            component.Update(new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.1)));
            Assert.That(notices, Is.EqualTo(1));
            Assert.That(component.CurrentTime, Is.EqualTo(.2f), "Late bounds cannot restart or own the playback clock.");
        }
        finally { completion.TrySetResult(Expected); await bounds.StopAsync(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NewEditOrCloseCancelsPublishedVersionsBoundsAndRejectsLateCompletion(bool close)
    {
        using var preview = PreparedPreview(out var plan, out _);
        var completion = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        using var bounds = new VfxInteractiveBounds(plan, token => { captured = token; return completion.Task; });
        Set(preview, "_reloadBounds", bounds);
        try
        {
            Assert.That(preview.ContinueInteractiveReload(new(.1f, Matrix.Identity)), Is.True);
            if (close) preview.Dispose();
            else preview.BeginInteractiveReload(new(VfxLibraryVisibilityTests.Source(""), "DIAG-bounds-publication.xml", _ => [1]));
            Assert.That(captured.IsCancellationRequested, Is.True);
            completion.SetResult(Expected);
            await bounds.StopAsync();
            Assert.That(bounds.TryTake(out _), Is.False);
            Assert.That(Get(preview, "_interactiveBounds"), Is.Null);
            Assert.That(preview.PreviewBounds, Is.Null, "A canceled version cannot publish bounds after a newer edit or close.");
        }
        finally { completion.TrySetResult(Expected); await bounds.StopAsync(); }
    }

    [Test]
    public async Task ReloadPrewarmIsOneShotAndDoesNotNeedBoundsOwnedByAnotherWorker()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("emitter").Single().Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -.5f), new XAttribute("max", -.5f)));
        var plan = TotalWarVfxConverter.Convert(new(document.Write(), "DIAG-negative.xml", _ => [1]));
        Assert.That(VfxInteractivePrewarm.CanPrepare(plan), Is.True);
        var release = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bounds = new VfxInteractiveBounds(plan, _ => release.Task);
        using var prewarm = VfxInteractivePrewarm.ForReload(plan, false, false);
        var key = new VfxInteractivePrewarmKey(Matrix.Identity, Matrix.Identity, 0, null);
        VfxPreparedPlayback? prepared = null;
        try
        {
            await Until(() => prewarm.TryTake(key, out prepared));
            Assert.That(release.Task.IsCompleted, Is.False);
            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared!.Bounds, Is.Null, "Bounds are pending, not an old or approximate substitute.");
            Assert.That(prepared.Simulation.ParticleCount, Is.GreaterThan(0));
            Assert.That(Get(prewarm, "_pending"), Is.Null, "A one-shot reload must not immediately spend CPU on a discarded successor.");
        }
        finally { release.TrySetResult(Expected); await bounds.StopAsync(); await prewarm.StopAsync(); }
    }

    [Test]
    public void ARangeLessPreparedRestartCannotEraseAlreadyPublishedAccurateBounds()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("emitter").Single().Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -.5f), new XAttribute("max", -.5f)));
        var plan = TotalWarVfxConverter.Convert(new(document.Write(), "DIAG-restart.xml", _ => [1]));
        var simulation = new TotalWarVfxSimulation(plan); simulation.Seek(0, Matrix.Identity);
        using var preview = new EffekseerVfxPreview(null!);
        Set(preview, "_conversion", plan); Set(preview, "_simulation", simulation); Set(preview, "_previewBounds", Expected);
        using var prewarm = new VfxInteractivePrewarm(plan, (key, _, _, _) => Task.FromResult(new VfxPreparedPlayback(key, simulation, null)));
        Set(preview, "_interactivePrewarm", prewarm); Set(preview, "_interactiveRestartPending", true);
        preview.PrepareInteractiveFrame(Matrix.Identity, null);
        Assert.That(preview.PreviewBounds, Is.EqualTo(Expected));
    }

    [Test]
    public async Task OrdinaryFirstPreparationPublishesBeforeOptionalBoundsAndLateResultSurvivesRestart()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("emitter").Single().Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -.5f), new XAttribute("max", -.5f)));
        var plan = TotalWarVfxConverter.Convert(new(document.Write(), "DIAG-default-prewarm.xml", _ => [1]));
        var boundsRelease = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken boundsToken = default;
        using var bounds = new VfxInteractiveBounds(plan, token => { boundsToken = token; return boundsRelease.Task; });
        using var initial = new VfxInteractivePrewarm(plan);
        using var preview = new EffekseerVfxPreview(null!);
        Set(preview, "_conversion", plan); Set(preview, "_interactivePrewarm", initial); Set(preview, "_interactiveBounds", bounds);
        try
        {
            await Until(() => { preview.PrepareInteractiveFrame(Matrix.Identity, null); return !preview.IsPreparing; });
            Assert.That(preview.ParticleCount, Is.GreaterThan(0), "The exact negative prewarm must be visible without optional range work.");
            Assert.That(preview.PreviewBounds, Is.Null);
            Assert.That(boundsRelease.Task.IsCompleted, Is.False);
            var first = (TotalWarVfxSimulation)Get(preview, "_simulation")!;
            var reference = new TotalWarVfxSimulation(plan); _ = reference.CycleDuration; reference.Seek(0, Matrix.Identity);
            var camera = VfxSpriteLowOpacityCullingTests.Camera();
            Assert.That(first.ProjectedFrame(camera, false), Is.EqualTo(reference.ProjectedFrame(camera, false)));
            var replacementReady = false;
            await VfxInteractivePrewarm.RunPreparation(_ => { replacementReady = true; return Task.FromResult(true); }, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(replacementReady, Is.True, "The first preview must release its global playback permit while bounds are pending.");
            boundsRelease.SetResult(Expected);
            await Until(() => { preview.PrepareInteractiveFrame(Matrix.Identity, null); return preview.PreviewBounds == Expected; });
            Assert.That(Get(preview, "_simulation"), Is.SameAs(first), "Late bounds must not replace or rewind the active simulation.");
            Set(preview, "_interactiveRestartPending", true);
            await Until(() => { preview.PrepareInteractiveFrame(Matrix.Identity, null); return !preview.IsPreparing; });
            Assert.That(Get(preview, "_simulation"), Is.Not.SameAs(first), "A natural rewind must still adopt its complete independently prepared successor.");
            Assert.That(preview.PreviewBounds, Is.EqualTo(Expected), "The range-less successor cannot erase accurate bounds published later.");
            Assert.That(boundsToken.IsCancellationRequested, Is.True, "The completed optional worker is released through the existing publication path.");
        }
        finally
        {
            await initial.StopAsync();
            boundsRelease.TrySetResult(Expected); await bounds.StopAsync();
        }
    }

    [Test]
    public async Task FailedInitialOptionalBoundsDoNotInvalidateAnExactPreparedPlayback()
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("emitter").Single().Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -.5f), new XAttribute("max", -.5f)));
        var plan = TotalWarVfxConverter.Convert(new(document.Write(), "DIAG-default-prewarm-failure.xml", _ => [1]));
        var release = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bounds = new VfxInteractiveBounds(plan, _ => release.Task);
        using var initial = new VfxInteractivePrewarm(plan);
        using var preview = new EffekseerVfxPreview(null!);
        Set(preview, "_conversion", plan); Set(preview, "_interactivePrewarm", initial); Set(preview, "_interactiveBounds", bounds);
        try
        {
            await Until(() => { preview.PrepareInteractiveFrame(Matrix.Identity, null); return !preview.IsPreparing; });
            var simulation = (TotalWarVfxSimulation)Get(preview, "_simulation")!;
            release.SetException(new InvalidOperationException("DIAG independent optional bounds failure"));
            await Until(() => { preview.PrepareInteractiveFrame(Matrix.Identity, null); return Get(preview, "_interactiveBounds") == null; });
            Assert.That(Get(preview, "_simulation"), Is.SameAs(simulation));
            Assert.That(preview.IsPreparing, Is.False);
            Assert.That(preview.ParticleCount, Is.GreaterThan(0));
            Assert.That(initial.Failure, Is.Null, "Optional bounds failure cannot poison the complete playback worker.");
        }
        finally
        {
            await initial.StopAsync();
            release.TrySetResult(Expected); await bounds.StopAsync();
        }
    }
    private static EffekseerVfxPreview PreparedPreview(out VfxConversion plan, out TotalWarVfxSimulation simulation)
    {
        // DIAG CPU publication fence: native upload is already complete; keep a real exact simulation.
        plan = VfxSpriteLowOpacityCullingTests.Conversion();
        simulation = new(plan); simulation.Seek(.1f, Matrix.Identity);
        var preview = new EffekseerVfxPreview(null!);
        Set(preview, "_conversion", plan); Set(preview, "_simulation", simulation);
        Set(preview, "_reloadSnapshot", new VfxPreviewSnapshot([1], "DIAG-bounds-publication.xml", _ => [1]));
        Set(preview, "_preparedReload", new VfxPreparedConversion(plan, null, []));
        Set(preview, "_reloadSimulation", simulation); Set(preview, "_reloadUploaded", true); Set(preview, "_reloadRetainsPlayback", true);
        Set(preview, "_previewBounds", new BoundingBox(new(-99), new(99)));
        return preview;
    }

    private static object? Get(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner);
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    private static async Task Until(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!complete())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("The deterministic CPU preparation gate did not complete.");
            await Task.Delay(1);
        }
    }

    private sealed class Factory(IVfxPreview preview) : IVfxPreviewFactory { public IVfxPreview Create(GraphicsDevice device) => preview; }
}
