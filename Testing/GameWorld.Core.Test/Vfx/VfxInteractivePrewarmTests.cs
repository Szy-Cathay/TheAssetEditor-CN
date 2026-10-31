#nullable enable
using System.Xml.Linq;
using System.Collections;
using System.Reflection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxInteractivePrewarmTests
{
    [TestCase(-25)]
    [TestCase(-100)]
    public async Task IndependentPreparedSimulationPreservesTheFullNegativePrewarmAndFutureRandomSequence(int start)
    {
        var bytes = Source(start);
        var plan = Convert(bytes);
        Assert.That(VfxInteractivePrewarm.CanPrepare(plan), Is.True);
        var key = new VfxInteractivePrewarmKey(Matrix.Identity, Matrix.Identity, 0, null);
        using var preparation = new VfxInteractivePrewarm(plan);
        preparation.Request(key);
        VfxPreparedPlayback? prepared = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!preparation.TryTake(key, out prepared) && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.That(prepared, Is.Not.Null);
        var reference = new TotalWarVfxSimulation(plan);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        foreach (var time in new[] { 0f, .1f, 1f, 2f })
        {
            reference.Seek(time, Matrix.Identity);
            prepared!.Simulation.Seek(time, Matrix.Identity);
            Assert.That(prepared.Simulation.Frame(camera), Is.EqualTo(reference.Frame(camera)), $"Unchanged prewarm/random stream at {time}s.");
            Assert.That(prepared.Simulation.ParticleCount, Is.EqualTo(reference.ParticleCount));
            Assert.That(prepared.Simulation.ParticleCount, Is.GreaterThan(0));
            Assert.That(RandomRecords(prepared.Simulation), Is.EqualTo(RandomRecords(reference)), "Compare unused random channels as well as visible constant curves.");
        }
        Assert.That(plan.Emitters.Single().Start, Is.EqualTo(new VfxRange(start, start)));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        await preparation.StopAsync();
    }

    [Test]
    public async Task ExactInitialPlaybackReleasesItsPermitWithoutWaitingForOccupiedOptionalBounds()
    {
        var plan = Convert(Source(-.5f, life: 2));
        var release = new TaskCompletionSource<BoundingBox?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var occupiedBounds = new VfxInteractiveBounds(plan, _ => release.Task);
        using var preparation = new VfxInteractivePrewarm(plan);
        var key = new VfxInteractivePrewarmKey(Matrix.Identity, Matrix.Identity, 0, null);
        try
        {
            preparation.Request(key);
            VfxPreparedPlayback? prepared = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!preparation.TryTake(key, out prepared) && DateTime.UtcNow < deadline) await Task.Delay(1);
            Assert.That(prepared, Is.Not.Null, "An occupied optional bounds worker must not prevent the exact zero-time simulation from being published.");
            Assert.That(prepared!.Bounds, Is.Null, "Pending optional bounds are not an accurate or inherited result.");
            var reference = new TotalWarVfxSimulation(plan);
            _ = reference.CycleDuration;
            reference.Seek(0, Matrix.Identity);
            Assert.That(ParticleRecords(prepared.Simulation), Is.EqualTo(ParticleRecords(reference)));
            Assert.That(RandomRecords(prepared.Simulation), Is.EqualTo(RandomRecords(reference)));
            var entered = false;
            await VfxInteractivePrewarm.RunPreparation(_ => { entered = true; return Task.FromResult(true); }, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(entered, Is.True, "Exact playback readiness must release the global permit for the next preview.");
            Assert.That(release.Task.IsCompleted, Is.False, "Playback readiness must not finish or cancel unrelated bounds.");
        }
        finally
        {
            await preparation.StopAsync();
            release.TrySetResult(new BoundingBox(Vector3.Zero, Vector3.One));
            await occupiedBounds.StopAsync();
        }
    }
    [Test]
    public async Task IndependentBoundsRetainEveryOriginalSynchronousEstimateStage()
    {
        var plan = Convert(Source(-25));
        var expected = VfxPreviewBounds.Estimate(plan);
        var key = new VfxInteractivePrewarmKey(Matrix.Identity, Matrix.Identity, 0, null);
        using var preparation = new VfxInteractivePrewarm(plan);
        using var bounds = new VfxInteractiveBounds(plan);
        var prepared = await Take(preparation, key);
        Assert.That(prepared.Bounds, Is.Null, "The playback worker cannot claim an independently computed result.");
        BoundingBox? actual = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var ready = false;
        while (!(ready = bounds.TryTake(out actual)) && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.That(ready, Is.True);
        Assert.That(actual, Is.Not.Null.And.EqualTo(expected));
        await preparation.StopAsync();
        await bounds.StopAsync();
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task NonidentityRootPreservesTheComponentsInitialAndRestartInitializationOrder(int transform)
    {
        var plan = Convert(Source(-.5f, life: 2));
        var root = (transform switch
        {
            1 => Matrix.CreateRotationY(MathHelper.PiOver4),
            2 => Matrix.CreateScale(2),
            _ => Matrix.Identity,
        }) * Matrix.CreateTranslation(10, 0, 0);
        var key = new VfxInteractivePrewarmKey(root, root, 0, null);
        using var preparation = new VfxInteractivePrewarm(plan);
        var reference = new TotalWarVfxSimulation(plan);
        // The actual component reads the duration before its first attachment/history seek.
        var duration = reference.CycleDuration;
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var prepared = await Take(preparation, key);
            foreach (var time in new[] { 0f, .1f, 1f })
            {
                reference.SeekWithCurrentAttachment(time, root, _ => root);
                prepared.Simulation.SeekWithCurrentAttachment(time, root, _ => root);
                Assert.That(prepared.Simulation.Frame(camera), Is.EqualTo(reference.Frame(camera)), $"Cycle {cycle}, {time}s frame.");
                Assert.That(ParticleRecords(prepared.Simulation), Is.EqualTo(ParticleRecords(reference)), $"Cycle {cycle}, {time}s birth/position/random state.");
            }
            reference.SeekWithCurrentAttachment(duration, root, _ => root);
            // A natural loop resets inside Seek after the attachment/history has been installed.
        }
        await preparation.StopAsync();
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ReloadPrewarmThenOwnerReplayPreservesDirectSeekWithNonidentityAndMovingHistory(bool initializeDuration, bool movingHistory)
    {
        // DIAG: the worker receives one captured time-zero root; the owner retains the actual moving history.
        var plan = Convert(Source(-.5f, life: 2));
        Assert.That(VfxInteractivePrewarm.CanPrepare(plan), Is.True);
        var origin = Matrix.CreateRotationY(.4f) * Matrix.CreateTranslation(7, 1, -3);
        Matrix History(float time) => movingHistory
            ? Matrix.CreateRotationY(time * .6f) * origin * Matrix.CreateTranslation(time * 3, time * .25f, -time)
            : origin;
        const float phase = .717f;
        var attachment = History(phase);
        var key = new VfxInteractivePrewarmKey(History(0), attachment, attachment.Translation.Y, .93f);
        using var preparation = VfxInteractivePrewarm.ForReload(plan, initializeDuration, false);
        var prepared = await Take(preparation, key);
        var reference = new TotalWarVfxSimulation(plan) { EmissionEndTimeSeconds = key.EmissionEnd };
        if (initializeDuration) _ = reference.CycleDuration;
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var calls = 0;
        while (!prepared.Simulation.PrepareInteractiveSeek(phase, attachment, History, useCurrentAttachmentForFrame: true, budget: TimeSpan.Zero))
            if (++calls > 10000) Assert.Fail("The owner replay did not finish its exact original ticks.");
        reference.SeekWithCurrentAttachment(phase, attachment, History);
        Assert.That(calls, Is.GreaterThan(10));
        Assert.That(prepared.Simulation.ParticleCount, Is.GreaterThan(0));
        foreach (var time in new[] { phase, 1.137f, .137f, .917f, 1.317f })
        {
            var root = History(time);
            reference.SeekWithCurrentAttachment(time, root, History);
            prepared.Simulation.SeekWithCurrentAttachment(time, root, History);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Simulation.ProjectedFrame(camera, false), Is.EqualTo(reference.ProjectedFrame(camera, false)), $"Complete projection at {time}s.");
                Assert.That(ParticleRecords(prepared.Simulation), Is.EqualTo(ParticleRecords(reference)), $"Complete particle state at {time}s.");
                Assert.That(RandomRecords(prepared.Simulation), Is.EqualTo(RandomRecords(reference)), $"Unused random channels at {time}s.");
            });
        }
        await preparation.StopAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SharedRestartPrewarmKeepsCachedBoundsAndDirectSeekOrder(bool movingHistory)
    {
        var plan = Convert(Source(-.5f, life: 2));
        var origin = Matrix.CreateRotationY(.7f) * Matrix.CreateTranslation(-5, 2, 4);
        Matrix History(float time) => movingHistory ? origin * Matrix.CreateTranslation(time * 2, 0, -time * 3) : origin;
        var cachedBounds = new BoundingBox(new(-7, -8, -9), new(7, 8, 9));
        var key = new VfxInteractivePrewarmKey(History(0), History(.717f), 2, null);
        using var preparation = VfxInteractivePrewarm.AfterPlayback(plan, cachedBounds, false);
        var prepared = await Take(preparation, key);
        Assert.That(prepared.Bounds, Is.EqualTo(cachedBounds), "An inherited playback must reuse its published bounds instead of estimating them a second time.");
        var reference = new TotalWarVfxSimulation(plan);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        foreach (var time in new[] { .717f, .137f, 1.137f })
        {
            reference.SeekWithCurrentAttachment(time, History(time), History);
            prepared.Simulation.SeekWithCurrentAttachment(time, History(time), History);
            Assert.That(prepared.Simulation.ProjectedFrame(camera, false), Is.EqualTo(reference.ProjectedFrame(camera, false)));
            Assert.That(ParticleRecords(prepared.Simulation), Is.EqualTo(ParticleRecords(reference)));
            Assert.That(RandomRecords(prepared.Simulation), Is.EqualTo(RandomRecords(reference)));
        }
        await preparation.StopAsync();
    }

    [TestCase("root")]
    [TestCase("ground")]
    [TestCase("end")]
    public async Task ChangingAQueuedKeyDoesNotTurnTheUntransferredFirstPreparationIntoARestart(string changed)
    {
        var plan = Convert(Source(-.5f, life: 2));
        Task<VfxPreparedPlayback> Block(VfxInteractivePrewarmKey key, bool ready, BoundingBox? bounds, CancellationToken token)
        {
            var completion = new TaskCompletionSource<VfxPreparedPlayback>();
            token.Register(() => completion.TrySetCanceled(token));
            return completion.Task;
        }
        using var blocker = new VfxInteractivePrewarm(plan, Block);
        using var preparation = new VfxInteractivePrewarm(plan);
        var root = Matrix.CreateTranslation(10, 0, 0);
        var firstKey = new VfxInteractivePrewarmKey(root, root, 0, null);
        var key = changed switch
        {
            "root" => firstKey with { RootAtZero = Matrix.CreateTranslation(20, 0, 0), Attachment = Matrix.CreateTranslation(20, 0, 0) },
            "ground" => firstKey with { GroundHeight = 2 },
            _ => firstKey with { EmissionEnd = .25f },
        };
        blocker.Request(firstKey);
        preparation.Request(firstKey);
        preparation.Request(key);
        await blocker.StopAsync();
        var prepared = await Take(preparation, key);
        Assert.That(prepared.Key, Is.EqualTo(key));
        var reference = new TotalWarVfxSimulation(plan) { EmissionEndTimeSeconds = key.EmissionEnd };
        _ = reference.CycleDuration;
        foreach (var time in new[] { 0f, 1f })
        {
            reference.SeekWithCurrentAttachment(time, key.Attachment, _ => key.RootAtZero);
            prepared.Simulation.SeekWithCurrentAttachment(time, key.Attachment, _ => key.RootAtZero);
            Assert.That(ParticleRecords(prepared.Simulation), Is.EqualTo(ParticleRecords(reference)), $"Latest {changed} key retains the real initial owner order.");
        }
        await preparation.StopAsync();
    }

    [Test]
    public void UnsupportedMotionEventsModelsAndNonnegativeSourcesKeepTheSynchronousRoute()
    {
        Assert.That(VfxInteractivePrewarm.CanPrepare(Convert(Source(0))), Is.False);
        var trail = Convert(Source(-25, true));
        Assert.That(trail.Emitters.Single().IsMotionTrail, Is.True);
        Assert.That(VfxInteractivePrewarm.CanPrepare(trail), Is.False);
        var events = Convert(Source(-25)); events.Emitters.Single().IsEventTemplate = true;
        Assert.That(VfxInteractivePrewarm.CanPrepare(events), Is.False);
        var model = Convert(Source(-25)); model.Models.Add(new([], []));
        Assert.That(VfxInteractivePrewarm.CanPrepare(model), Is.False);
    }

    [Test]
    public async Task ChangedRootRejectsTheLateWorkerAndTransfersOnlyOneIndependentSuccessor()
    {
        var plan = Convert(Source(-25));
        var calls = new List<(VfxInteractivePrewarmKey Key, bool BoundsReady, CancellationToken Token, TaskCompletionSource<VfxPreparedPlayback> Completion)>();
        Task<VfxPreparedPlayback> Start(VfxInteractivePrewarmKey key, bool boundsReady, BoundingBox? bounds, CancellationToken token)
        {
            var completion = new TaskCompletionSource<VfxPreparedPlayback>();
            token.Register(() => completion.TrySetCanceled(token));
            calls.Add((key, boundsReady, token, completion)); return completion.Task;
        }
        using var helper = new VfxInteractivePrewarm(plan, Start);
        var a = new VfxInteractivePrewarmKey(Matrix.Identity, Matrix.Identity, 0, null);
        var b = a with { RootAtZero = Matrix.CreateTranslation(10, 0, 0), Attachment = Matrix.CreateTranslation(10, 0, 0) };
        helper.Request(a); helper.Request(b);
        Assert.That(calls[0].Token.IsCancellationRequested, Is.True);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (calls.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.That(calls, Has.Count.EqualTo(2), "The canceled worker must release its exclusive slot before its replacement starts.");
        var old = new TotalWarVfxSimulation(plan);
        Assert.That(calls[0].Completion.TrySetResult(new(a, old, null)), Is.False);
        Assert.That(helper.TryTake(b, out _), Is.False);
        Assert.That(calls, Has.Count.EqualTo(2));
        var current = new TotalWarVfxSimulation(plan);
        calls[1].Completion.SetResult(new(b, current, new BoundingBox(Vector3.Zero, Vector3.One)));
        VfxPreparedPlayback? taken = null;
        while (!helper.TryTake(b, out taken) && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.That(taken, Is.Not.Null);
        Assert.That(taken!.Simulation, Is.SameAs(current).And.Not.SameAs(old));
        Assert.That(calls, Has.Count.EqualTo(3), "Prepare only one successor after ownership transfer.");
        Assert.That(calls[2].BoundsReady, Is.True, "Do not repeat the 17-stage bounds estimate for the next cycle.");
        var stopped = helper.StopAsync();
        Assert.That(calls[2].Token.IsCancellationRequested, Is.True);
        Assert.That(helper.TryTake(b, out _), Is.False);
        Assert.That(calls, Has.Count.EqualTo(3));
        await stopped;
    }

    [Test]
    public async Task ResourcesShareOneWorkerAndDisposalCancelsItsQueuedOrRunningWork()
    {
        var plan = Convert(Source(-25)); var entered = 0;
        Task<VfxPreparedPlayback> Start(VfxInteractivePrewarmKey key, bool ready, BoundingBox? bounds, CancellationToken token)
        {
            Interlocked.Increment(ref entered);
            var completion = new TaskCompletionSource<VfxPreparedPlayback>();
            token.Register(() => completion.TrySetCanceled(token)); return completion.Task;
        }
        using var first = new VfxInteractivePrewarm(plan, Start);
        using var second = new VfxInteractivePrewarm(plan, Start);
        var key = new VfxInteractivePrewarmKey(Matrix.Identity, Matrix.Identity, 0, null);
        first.Request(key); second.Request(key);
        Assert.That(entered, Is.EqualTo(1));
        first.Dispose();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref entered) != 2 && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.That(entered, Is.EqualTo(2));
        await first.StopAsync();
        await second.StopAsync();
        second.Dispose();
        Assert.That(second.TryTake(key, out _), Is.False);
    }

    [Test]
    public void CancellationIsObservedBetweenTheOriginalTicksWithoutPublishingAPartialPrewarm()
    {
        var plan = Convert(Source(-100)); var simulation = new TotalWarVfxSimulation(plan);
        using var cancellation = new CancellationTokenSource(); var samples = 0;
        Matrix Frozen(float time)
        {
            if (++samples == 2) cancellation.Cancel();
            return Matrix.Identity;
        }
        Assert.That(() => simulation.SeekForPreparation(0, Matrix.Identity, cancellation.Token, Frozen), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(samples, Is.EqualTo(2));
        Assert.That((double)typeof(TotalWarVfxSimulation).GetField("_simulatedTime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(simulation)!, Is.LessThan(0));
        var ordinary = new TotalWarVfxSimulation(plan); ordinary.Seek(0, Matrix.Identity);
        Assert.That(ordinary.ParticleCount, Is.GreaterThan(0));
    }

    [Test]
    public void PendingPreparationFreezesTheInteractiveClockAndReadyFrameDoesNotCatchUpWallTime()
    {
        var preview = new DeferredPreview();
        var devices = new Mock<IDeviceResolver>();
        var component = new VfxPreviewComponent(new Factory(preview), devices.Object, null!);
        component.Reload(new(Source(-25), "diagnostic.xml", _ => [1]));
        component.Update(new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)));
        Assert.That(preview.Enabled, Is.True);
        Assert.That(component.CurrentTime, Is.Zero);
        Assert.That(preview.Seeks, Is.Empty);
        preview.FinishOnPrepare = true;
        component.Update(new(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(20)));
        Assert.That(component.CurrentTime, Is.Zero);
        Assert.That(preview.Seeks, Is.EqualTo(new[] { 0f }));
        component.Update(new(TimeSpan.FromSeconds(40.1), TimeSpan.FromSeconds(.1)));
        Assert.That(component.CurrentTime, Is.EqualTo(.1f));
        component.Dispose();
    }

    [Test]
    public void InitialPendingMovementInitializesHistoryBeforeCapturingAKeyAndPreservesItsBirthSide()
    {
        var preview = new HistoryDeferredPreview();
        var devices = new Mock<IDeviceResolver>();
        using var component = new VfxPreviewComponent(new Factory(preview), devices.Object, null!);
        var target = Matrix.CreateTranslation(10, 0, 0);
        var samplesAtZero = new List<Matrix> { target };
        var hasPrepared = false;
        component.Attachment = () => target;
        component.AttachmentAtTime = _ => samplesAtZero[0];
        component.PreparingFrame += (time, explicitSeek) =>
        {
            Assert.That(time, Is.Zero);
            Assert.That(explicitSeek, Is.False, "Waiting must not cancel an active transform as an explicit seek.");
            // The editor resets on first preparation, then retains both sides of a same-time teleport.
            if (!hasPrepared) { samplesAtZero.Clear(); samplesAtZero.Add(target); hasPrepared = true; }
            else if (samplesAtZero[^1] != target) samplesAtZero.Add(target);
        };
        component.Reload(new(Source(-.5f, life: 2), "diagnostic.xml", _ => [1]));
        component.Update(new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)));
        Assert.That(hasPrepared, Is.True, "The first worker key must see initialized attachment history.");
        var oldKey = preview.RequestedKey;
        target = Matrix.CreateTranslation(20, 0, 0);
        component.Update(new(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(20)));
        Assert.That(preview.RequestedKey, Is.Not.EqualTo(oldKey));
        preview.ReadyKey = oldKey;
        component.Update(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(20)));
        Assert.That(preview.IsPreparing, Is.True, "An old root/current-attachment key must not be adopted.");
        Assert.That(preview.SeekHistoryAtZero, Is.Null);
        preview.ReadyKey = preview.RequestedKey;
        component.Update(new(TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(20)));
        Assert.That(preview.IsPreparing, Is.False);
        Assert.That(samplesAtZero, Is.EqualTo(new[] { Matrix.CreateTranslation(10, 0, 0), target }));
        Assert.That(preview.SeekHistoryAtZero, Is.EqualTo(preview.ReadyKey!.Value.RootAtZero));
        Assert.That(component.CurrentTime, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RestartWaitingDoesNotAddHistoryPreparationOrRepeatAnExplicitSeek(bool explicitSeek)
    {
        var root = Matrix.CreateTranslation(10, 0, 0);
        var preview = new HistoryDeferredPreview { ReadyKey = new(root, root, 0, null), DeferOnRewind = true };
        var devices = new Mock<IDeviceResolver>();
        using var component = new VfxPreviewComponent(new Factory(preview), devices.Object, null!)
        {
            Attachment = () => root,
            AttachmentAtTime = _ => root,
        };
        var events = new List<bool>();
        component.PreparingFrame += (_, seek) => events.Add(seek);
        component.Reload(new(Source(-.5f, life: 2), "diagnostic.xml", _ => [1]));
        component.Update(new(TimeSpan.Zero, TimeSpan.Zero));
        component.Update(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        events.Clear();
        if (explicitSeek)
        {
            component.Seek(0);
            component.Update(new(TimeSpan.FromSeconds(1), TimeSpan.Zero));
            Assert.That(events, Is.EqualTo(new[] { true }));
        }
        else
        {
            component.Update(new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(7)));
            Assert.That(events, Is.EqualTo(new[] { false, false }), "Original authored-end frame then natural-loop reset.");
        }
        Assert.That(preview.IsPreparing, Is.True);
        events.Clear();
        component.Update(new(TimeSpan.FromSeconds(28), TimeSpan.FromSeconds(20)));
        Assert.That(events, Is.Empty, "Only initial preparation may initialize history while waiting.");
        preview.ReadyKey = preview.RequestedKey;
        component.Update(new(TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(20)));
        Assert.That(events, Is.EqualTo(new[] { false }));
        Assert.That(component.CurrentTime, Is.Zero);
    }

    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "diagnostic-prewarm.xml", _ => [1]));
    private static async Task<VfxPreparedPlayback> Take(VfxInteractivePrewarm preparation, VfxInteractivePrewarmKey key)
    {
        VfxPreparedPlayback? prepared = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!preparation.TryTake(key, out prepared) && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.That(prepared, Is.Not.Null);
        return prepared!;
    }
    private static object[] ParticleRecords(TotalWarVfxSimulation simulation)
        => ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(simulation)!)
            .Cast<object>().Select(particle => (object)particle.GetType().GetFields()
                .Where(field => field.FieldType.IsValueType).Select(field => field.GetValue(particle)).ToArray()).ToArray();
    private static object[] RandomRecords(TotalWarVfxSimulation simulation)
        => ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(simulation)!)
            .Cast<object>().Select(particle => (object)new object?[]
            {
                particle.GetType().GetField("RandomScale")!.GetValue(particle), particle.GetType().GetField("RandomColour")!.GetValue(particle),
                particle.GetType().GetField("RandomAlpha")!.GetValue(particle), particle.GetType().GetField("Life")!.GetValue(particle),
                particle.GetType().GetField("Birth")!.GetValue(particle),
            }).ToArray();
    private static byte[] Source(float start, bool trail = false, float life = 1)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var effect = document.Root.Element("vfx")!; var emitter = effect.Element("emitters")!.Element("emitter")!;
        emitter.Elements("property").Single(property => (string?)property.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", life);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", start), new XAttribute("max", start)));
        if (trail) effect.Add(new XElement("effect_render_stacks", new XElement("effect_render_stack", new XAttribute("id", "visible"),
            new XElement("property", new XAttribute("name", "enable_trail_mode"), new XAttribute("value", "true")))));
        return document.Write();
    }

    private sealed class Factory(IVfxPreview preview) : IVfxPreviewFactory { public IVfxPreview Create(GraphicsDevice device) => preview; }
    private sealed class HistoryDeferredPreview : IVfxPreview
    {
        public bool IsPreparing { get; private set; } = true;
        public VfxInteractivePrewarmKey? RequestedKey, ReadyKey;
        public Matrix? SeekHistoryAtZero;
        public bool DeferOnRewind;
        private float _seekTime;
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => 0;
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? history)
        {
            RequestedKey = new(history!(0), attachment, attachment.Translation.Y, null);
            if (ReadyKey == RequestedKey) IsPreparing = false;
        }
        public void Reload(VfxPreviewSnapshot snapshot) { }
        public void Advance(float seconds, Matrix attachment) { }
        public void Seek(float seconds, Matrix attachment) { }
        public void Seek(float seconds, Matrix attachment, Func<float, Matrix> history, bool useCurrentAttachmentForFrame)
        {
            SeekHistoryAtZero = history(0);
            if (DeferOnRewind && seconds < _seekTime) { IsPreparing = true; ReadyKey = null; }
            _seekTime = seconds;
        }
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
    private sealed class DeferredPreview : IVfxPreview
    {
        public bool Enabled, FinishOnPrepare;
        public bool IsPreparing { get; private set; } = true;
        public List<float> Seeks { get; } = [];
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => 0;
        public void EnableInteractivePrewarm() => Enabled = true;
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? history) { if (FinishOnPrepare) IsPreparing = false; }
        public void Reload(VfxPreviewSnapshot snapshot) { }
        public void Advance(float seconds, Matrix attachment) { }
        public void Seek(float seconds, Matrix attachment) => Seeks.Add(seconds);
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
}
