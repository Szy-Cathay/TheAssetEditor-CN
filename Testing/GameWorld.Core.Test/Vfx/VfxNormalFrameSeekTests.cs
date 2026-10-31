#nullable enable
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxNormalFrameSeekTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 30), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 2);

    private static IEnumerable<TestCaseData> FrameCases()
    {
        foreach (var model in new[] { false, true })
        foreach (var events in new[] { false, true })
        foreach (var constant in new[] { false, true })
        foreach (var current in new[] { false, true })
        foreach (var durationFirst in new[] { false, true })
            yield return new(model, events, constant, current, durationFirst);
    }

    [TestCaseSource(nameof(FrameCases))]
    public void PublicLargeForwardReverseAndFutureSeeksKeepAllParticleAndRandomValues(bool model, bool events,
        bool constant, bool current, bool durationFirst)
    {
        var owner = Environment.CurrentManagedThreadId;
        var conversion = Conversion(model, events);
        var fixedRoot = Matrix.CreateScale(1.25f) * (events ? Matrix.Identity : Matrix.CreateRotationY(.3f)) * Matrix.CreateTranslation(2, 4, -1);
        Matrix History(float time)
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner), "Live history must never enter a worker.");
            return constant ? fixedRoot : events ? Matrix.CreateScale(1.25f) * Matrix.CreateTranslation(2 + time * 2, 4, -1 + time * .3f)
                : Matrix.CreateRotationY(time * .4f) * Matrix.CreateTranslation(time, 4 + time * .1f, -time * .3f);
        }
        var actual = Initialize(conversion, History(.27f), History, current, durationFirst, constant);
        var expected = Initialize(conversion, History(.27f), History, current, durationFirst, constant);
        using var preview = Preview(conversion, actual, constant, durationFirst);
        foreach (var time in new[] { 2.517f, 1.117f, 1.117f, 1.937f, 3.137f })
        {
            var root = History(time);
            var prior = Simulation(preview); var priorTime = prior.Time;
            var previousPicture = prior.ProjectedFrame(Camera, false);
            Apply(preview, time, root, History, current);
            if (Math.Abs(time - priorTime) > .03f)
            {
                Assert.That(preview.IsSeekingFrame, Is.True);
                Assert.That(Simulation(preview), Is.SameAs(prior), "The active complete simulation cannot be transferred to a worker or partially replayed.");
                Assert.That(prior.Time, Is.EqualTo(priorTime));
                Assert.That(prior.ProjectedFrame(Camera, false), Is.EqualTo(previousPicture));
            }
            Finish(preview, root, History);
            if (current) expected.SeekWithCurrentAttachment(time, root, History); else expected.Seek(time, root, History);
            AssertSame(expected, Simulation(preview));
        }
    }

    [Test]
    public void SamePhaseCameraRefreshAndThreeTickPlaybackKeepTheExistingSimulation()
    {
        var conversion = Conversion(false, false);
        var simulation = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        using var preview = Preview(conversion, simulation, true, true);
        for (var frame = 0; frame < 50; frame++)
        {
            preview.Seek(simulation.Time, Matrix.Identity);
            Assert.That(preview.IsSeekingFrame, Is.False);
            Assert.That(Simulation(preview), Is.SameAs(simulation));
        }
        var target = simulation.Time + 3f / 120;
        preview.Seek(target, Matrix.Identity);
        Assert.That(preview.IsSeekingFrame, Is.False);
        Assert.That(Simulation(preview), Is.SameAs(simulation));
        Assert.That(simulation.Time, Is.EqualTo(target));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void OrdinarySlowForwardPlaybackKeepsTheExistingSimulationAndExactFuturePackets(bool model, bool events)
    {
        var conversion = Conversion(model, events);
        var actual = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        var expected = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        using var preview = Preview(conversion, actual, true, true);
        preview.ConfigureInteractiveSeekFrame(true, 2, true, allowForwardReplay: false);
        foreach (var time in new[] { .337f, .717f, 1.117f, 1.737f })
        {
            preview.Seek(time, Matrix.Identity); expected.Seek(time, Matrix.Identity);
            Assert.That(preview.IsSeekingFrame, Is.False, "Slow normal playback must not enter whole-frame replay or freeze its clock.");
            Assert.That(Simulation(preview), Is.SameAs(actual));
            AssertSame(expected, actual);
        }
        preview.ConfigureInteractiveSeekFrame(true, 2, true);
        preview.Seek(2.517f, Matrix.Identity);
        Assert.That(preview.IsSeekingFrame, Is.True, "An explicit forward target still uses private preparation.");
        Finish(preview, Matrix.Identity, null); expected.Seek(2.517f, Matrix.Identity);
        AssertSame(expected, Simulation(preview));
        preview.ConfigureInteractiveSeekFrame(true, 2, true, allowForwardReplay: false);
        preview.Seek(.717f, Matrix.Identity);
        Assert.That(preview.IsSeekingFrame, Is.True, "Disabling ordinary forward replay must not make reverse replay synchronous.");
        Finish(preview, Matrix.Identity, null); expected.Seek(.717f, Matrix.Identity);
        AssertSame(expected, Simulation(preview));
        if (!events) return;
        preview.ConfigureInteractiveSeekFrame(true, 3, true, allowForwardReplay: false);
        preview.Seek(.917f, Matrix.Identity);
        Assert.That(preview.IsSeekingFrame, Is.True, "A new receiving ground must still prepare its exact event simulation privately.");
        Finish(preview, Matrix.Identity, null);
        var newGround = Camera with { VfxGroundHeight = 3 };
        _ = expected.ProjectedFrame(newGround, false);
        expected.Seek(.917f, Matrix.Identity);
        AssertSame(expected, Simulation(preview), newGround);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FirstPublicLargeSeekPreparesPrivatelyAndNeverClampsToCycleDuration(bool durationFirst)
    {
        var conversion = Conversion(false, false);
        var actual = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 3.7f };
        var expected = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 3.7f };
        if (durationFirst) { _ = actual.CycleDuration; _ = expected.CycleDuration; }
        using var preview = Preview(conversion, actual, true, durationFirst);
        var initialized = actual.IsInitialized;
        preview.Seek(2.5f, Matrix.Identity);
        Assert.That(preview.IsSeekingFrame, Is.True);
        Assert.That(Simulation(preview), Is.SameAs(actual));
        Assert.That(actual.Time, Is.Zero);
        Assert.That(actual.IsInitialized, Is.EqualTo(initialized), "Even Reset/Tick(0) for a constant origin must remain off the owner callback.");
        Finish(preview, Matrix.Identity, null);
        expected.Seek(2.5f, Matrix.Identity);
        AssertSame(expected, Simulation(preview));
        var afterCycle = expected.CycleDuration + 1;
        preview.Seek(afterCycle, Matrix.Identity); Finish(preview, Matrix.Identity, null);
        expected.Seek(afterCycle, Matrix.Identity);
        AssertSame(expected, Simulation(preview));
        Assert.That(Simulation(preview).Time, Is.GreaterThan(Simulation(preview).CycleDuration));
    }

    [Test]
    public void AQueuedWorkerFinishesOneCompleteTargetAndKeepsOnlyTheNewestSliderTarget()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = VfxInteractivePrewarm.RunPreparation(_ => release.Task, CancellationToken.None);
        var conversion = Conversion(false, false);
        var actual = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        var expected = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        using var preview = Preview(conversion, actual, true, true);
        try
        {
            preview.Seek(2.5f, Matrix.Identity);
            var worker = Read(preview, "_normalSeekWorker");
            preview.Seek(1.1f, Matrix.Identity); preview.Seek(3.3f, Matrix.Identity);
            Assert.That(Read(preview, "_normalSeekWorker"), Is.SameAs(worker), "Continuous input cannot cancel and starve the current complete version.");
            Assert.That(Simulation(preview), Is.SameAs(actual));
            release.SetResult(true); occupied.GetAwaiter().GetResult();
            Wait(() =>
            {
                preview.PrepareInteractiveFrame(Matrix.Identity, null);
                return Simulation(preview).Time == 2.5f;
            });
            Assert.That(preview.IsSeekingFrame, Is.True, "The last 3.3s request still needs preparation; 1.1s was superseded.");
            expected.Seek(2.5f, Matrix.Identity);
            AssertSame(expected, Simulation(preview));
            Finish(preview, Matrix.Identity, null);
            expected.Seek(3.3f, Matrix.Identity);
            AssertSame(expected, Simulation(preview));
        }
        finally { release.TrySetResult(true); occupied.GetAwaiter().GetResult(); }
    }

    [TestCase("ground")]
    [TestCase("end")]
    [TestCase("history")]
    [TestCase("attachment")]
    [TestCase("dispose")]
    [TestCase("reload")]
    [TestCase("restart")]
    public void ChangedContextCancelsAnUnpublishedTargetWithoutReplacingTheCompletePicture(string change)
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = VfxInteractivePrewarm.RunPreparation(_ => release.Task, CancellationToken.None);
        var conversion = Conversion(false, false);
        var simulation = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        using var preview = Preview(conversion, simulation, true, true);
        try
        {
            preview.Seek(2.5f, Matrix.Identity);
            Assert.That(preview.IsSeekingFrame, Is.True);
            switch (change)
            {
                case "ground": preview.ConfigureInteractiveSeekFrame(true, 3, true); break;
                case "end": preview.EmissionEndTimeSeconds = .1f; break;
                case "history": preview.ConfigureInteractiveSeekFrame(false, 2, true); break;
                case "attachment": preview.PrepareInteractiveFrame(Matrix.CreateTranslation(3, 0, 0), null); break;
                case "dispose": preview.Dispose(); break;
                case "reload": preview.BeginInteractiveReload(new([1], "DIAG-new-scope.xml", _ => null)); break;
                case "restart": preview.BeginInteractiveRestart(); break;
            }
            Assert.That(preview.IsSeekingFrame, Is.False);
            if (change != "dispose") Assert.That(Simulation(preview), Is.SameAs(simulation));
            release.SetResult(true); occupied.GetAwaiter().GetResult();
            if (change != "dispose")
            {
                preview.PrepareInteractiveFrame(Matrix.Identity, null);
                Assert.That(Simulation(preview), Is.SameAs(simulation));
            }
        }
        finally { release.TrySetResult(true); occupied.GetAwaiter().GetResult(); }
    }

    [Test]
    public void ChangedCutoffHistoryRetainsTheOriginalOwnerRouteAndItsBaselinePackets()
    {
        var conversion = Conversion(false, false);
        var actual = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        var expected = Initialize(conversion, Matrix.Identity, _ => Matrix.Identity, false, true, true);
        using var preview = Preview(conversion, actual, true, true);
        preview.EmissionEndTimeSeconds = .1f; expected.EmissionEndTimeSeconds = .1f;
        preview.Seek(2.5f, Matrix.Identity); expected.Seek(2.5f, Matrix.Identity);
        Assert.That(preview.IsSeekingFrame, Is.False, "Changed historical cutoffs are deliberately not admitted to fresh asynchronous replay.");
        Assert.That(Simulation(preview), Is.SameAs(actual));
        AssertSame(expected, actual);
    }

    [Test]
    public void ContinuousTrailChordsAndFutureRandomValuesRetainTheirOriginalPath()
    {
        var conversion = Conversion(false, false, trail: true);
        var actual = new TotalWarVfxSimulation(conversion); var expected = new TotalWarVfxSimulation(conversion);
        using var preview = Preview(conversion, actual, false, false);
        var actualQueries = new List<float>(); var expectedQueries = new List<float>();
        Matrix History(float time) => time < .3f ? Matrix.CreateTranslation(time * 8, 0, 0) : Matrix.CreateTranslation(2.4f, 0, (time - .3f) * 9);
        float? Next(float time, List<float> queries)
        {
            queries.Add(time);
            foreach (var turn in new[] { .137f, .3f, .517f, .713f }) if (turn > time) return turn;
            return null;
        }
        foreach (var time in new[] { .317f, .817f, 1.137f })
        {
            preview.SeekContinuousTrail(time, History(time), History, value => Next(value, actualQueries));
            expected.SeekContinuousTrail(time, History(time), History, value => Next(value, expectedQueries));
            Assert.That(preview.IsSeekingFrame, Is.False);
            Assert.That(Simulation(preview), Is.SameAs(actual));
            Assert.That(actualQueries, Is.EqualTo(expectedQueries));
            AssertSame(expected, actual);
        }
    }

    [Test]
    public void ComponentFreezesWaitingTimeAcceptsTheNewestSeekAndDoesNotCatchUpWallClock()
    {
        var runtime = new WaitingPreview();
        using var component = new VfxPreviewComponent(new Factory(runtime), Mock.Of<IDeviceResolver>(), null!)
            { EnableInteractiveReload = true, Loop = false };
        component.Reload(new([1], "DIAG-clock.xml", _ => null)); component.Update(new GameTime());
        component.Seek(2.5f); Update(component, .016f);
        Assert.That(runtime.IsSeekingFrame, Is.True);
        Update(component, 20); Assert.That(component.CurrentTime, Is.EqualTo(2.5f));
        component.Seek(1.2f); Update(component, 20);
        Assert.That(component.CurrentTime, Is.EqualTo(1.2f));
        Assert.That(runtime.Latest, Is.EqualTo(1.2f));
        runtime.Ready = true; Update(component, 20);
        Assert.That(component.CurrentTime, Is.EqualTo(1.2f), "Waiting and the completion frame add zero animation elapsed time.");
        Assert.That(runtime.IsSeekingFrame, Is.False);
        Update(component, .016f);
        Assert.That(component.CurrentTime, Is.EqualTo(1.216f).Within(.000001f));
    }

    [Test]
    public void ComponentSlowPlaybackDoesNotFreezeAndUsesTheOriginalIncrementalParticleStream()
    {
        var conversion = Conversion(true, false);
        var actual = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 3.7f };
        var expected = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 3.7f };
        foreach (var simulation in new[] { actual, expected })
        {
            simulation.PrepareInitialGround(0, Matrix.Identity, null, false, false, null);
            _ = simulation.CycleDuration;
            simulation.Seek(0, Matrix.Identity);
        }
        using var preview = Preview(conversion, actual, true, true);
        var runtime = new SimulationPreview(preview);
        using var component = new VfxPreviewComponent(new Factory(runtime), Mock.Of<IDeviceResolver>(), null!)
            { EnableInteractiveReload = true, Loop = false };
        component.Reload(new([1], "DIAG-slow-playback.xml", _ => null)); Update(component, 0);
        var elapsed = 0f;
        foreach (var delta in new[] { .033f, .067f, .04f, .101f, .029f, .083f })
        {
            // GameTime receives a TimeSpan, so retain its original 100ns quantization in the oracle.
            elapsed += (float)TimeSpan.FromSeconds(delta).TotalSeconds;
            Update(component, delta); expected.Seek(elapsed, Matrix.Identity);
            Assert.That(component.CurrentTime, Is.EqualTo(elapsed).Within(.000001f));
            Assert.That(preview.IsSeekingFrame, Is.False);
            Assert.That(Simulation(preview), Is.SameAs(actual));
            AssertSame(expected, actual);
        }
        component.Seek(1.5f); Update(component, .1f);
        Assert.That(preview.IsSeekingFrame, Is.True, "Playing does not turn a user slider seek into ordinary advancement.");
    }

    [Test]
    [Category("NativeOffscreen")]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void PublicLargeSeekKeepsTheOldNativeHandleAndPixelsUntilACompleteSamePhaseFrameIsReady()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var assembly = typeof(VfxNormalFrameSeekTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("OfficialSiegeSmokeDiffuse.dds", StringComparison.Ordinal)))!;
        using var copy = new MemoryStream(); input.CopyTo(copy); var original = copy.ToArray();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo("18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70"));
        var graphics = new global::Test.TestingUtility.Shared.GraphicsDeviceServiceMock();
        try
        {
            // DIAG scheduling control with the untouched, hash-checked official DDS; not complete-effect/native acceptance.
            var snapshot = new VfxPreviewSnapshot(Shared.GameFormats.Vfx.VfxDocument.Read(VfxLibraryVisibilityTests.Source("")).Write(),
                "DIAG-normal-seek-offscreen.xml", _ => original);
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            using var control = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(snapshot); control.Reload(snapshot);
            preview.Seek(.27f, Matrix.Identity); control.Seek(.27f, Matrix.Identity);
            preview.EnableInteractivePrewarm(); preview.ConfigureInteractiveSeekFrame(true, 2);
            var oldSimulation = Simulation(preview); var oldHandle = Read(preview, "_handle");
            var oldPixels = Render(graphics.GraphicsDevice, preview);
            preview.Seek(2.5f, Matrix.Identity);
            Assert.That(preview.IsSeekingFrame, Is.True);
            Assert.That(Simulation(preview), Is.SameAs(oldSimulation));
            Wait(() =>
            {
                Assert.That(Render(graphics.GraphicsDevice, preview), Is.EqualTo(oldPixels));
                preview.PrepareInteractiveFrame(Matrix.Identity, null);
                return !preview.IsSeekingFrame;
            });
            Assert.That(Read(preview, "_handle"), Is.SameAs(oldHandle), "Preparing a frame must never rebuild or replace native resources.");
            control.Seek(2.5f, Matrix.Identity);
            Assert.That(Render(graphics.GraphicsDevice, preview), Is.EqualTo(Render(graphics.GraphicsDevice, control)));
            Assert.That(NativePacket(preview), Is.EqualTo(NativePacket(control)));
            preview.Seek(1.1f, Matrix.Identity); Finish(preview, Matrix.Identity, null);
            control.Seek(1.1f, Matrix.Identity);
            Assert.That(Render(graphics.GraphicsDevice, preview), Is.EqualTo(Render(graphics.GraphicsDevice, control)));
            Assert.That(NativePacket(preview), Is.EqualTo(NativePacket(control)));
        }
        finally { graphics.Release(); }
    }

    [Test]
    [Category("NativeOffscreen")]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void PendingChangedGroundDrawCannotReplayOrReplaceTheCompleteTerrainEventSimulation()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var assembly = typeof(VfxNormalFrameSeekTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("OfficialSiegeSmokeDiffuse.dds", StringComparison.Ordinal)))!;
        using var copy = new MemoryStream(); input.CopyTo(copy); var original = copy.ToArray();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo("18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70"));
        var graphics = new global::Test.TestingUtility.Shared.GraphicsDeviceServiceMock();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? occupied = null;
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            using var control = new EffekseerVfxPreview(graphics.GraphicsDevice);
            foreach (var runtime in new[] { preview, control })
            {
                var conversion = Conversion(false, true);
                for (var index = 0; index < conversion.Textures.Count; index++)
                    if (conversion.TextureUsages[index] is VfxTextureUsage.Diffuse or VfxTextureUsage.OpaqueDiffuse)
                        conversion.Textures[index] = original;
                var snapshot = new VfxPreviewSnapshot([1], "DIAG-normal-ground-event.xml", _ => original);
                Set(runtime, "_applyingInteractiveReload", true);
                typeof(EffekseerVfxPreview).GetMethod("ReloadConverted", Private)!.Invoke(runtime,
                    [snapshot, conversion, VfxPlaybackResourceIdentity.Create(conversion), VfxPreparedResources.Prepare(conversion), false]);
                Set(runtime, "_applyingInteractiveReload", false);
                runtime.EmissionEndTimeSeconds = 3.7f;
            }
            var root = Matrix.CreateScale(1.25f) * Matrix.CreateTranslation(2, 4, -1);
            Func<float, Matrix> history = _ => root;
            foreach (var runtime in new[] { preview, control })
            {
                Simulation(runtime).PrepareInitialGround(2, root, history, true, false, null);
                runtime.Seek(.27f, root, history, true);
            }
            var eye = new Vector3(2, 8, 15);
            var oldCamera = Camera with { View = Matrix.CreateLookAt(eye, new(2, 2, -1), Vector3.Up),
                Projection = Matrix.CreateOrthographic(20, 20, .1f, 100), CameraPosition = eye, VfxGroundHeight = 2 };
            var newCamera = oldCamera with { VfxGroundHeight = 3 };
            var oldPixels = Render(graphics.GraphicsDevice, preview, oldCamera);
            Assert.That(oldPixels.Any(pixel => pixel.X + pixel.Y + pixel.Z > .001f), Is.True);
            preview.EnableInteractivePrewarm(); preview.ConfigureInteractiveSeekFrame(true, 2, true);
            occupied = VfxInteractivePrewarm.RunPreparation(_ => release.Task, CancellationToken.None);
            preview.ConfigureInteractiveSeekFrame(true, 3, true);
            var oldSimulation = Simulation(preview); var oldOrigin = oldSimulation.ReplayOrigin;
            preview.Seek(2.5f, root, history, true);
            Assert.That(preview.IsSeekingFrame, Is.True);
            for (var frame = 0; frame < 3; frame++)
            {
                Assert.That(Render(graphics.GraphicsDevice, preview, newCamera), Is.EqualTo(oldPixels));
                Assert.That(Simulation(preview), Is.SameAs(oldSimulation));
                Assert.That(oldSimulation.ReplayOrigin, Is.SameAs(oldOrigin), "Draw cannot secretly Reset the old graph while a private ground frame is pending.");
                Assert.That(oldSimulation.CurrentGroundHeight, Is.EqualTo(2));
                Assert.That(oldSimulation.Time, Is.EqualTo(.27f));
            }
            release.SetResult(true); occupied.GetAwaiter().GetResult();
            Finish(preview, root, history);
            control.Seek(2.5f, root, history, true);
            Assert.That(Render(graphics.GraphicsDevice, preview, newCamera), Is.EqualTo(Render(graphics.GraphicsDevice, control, newCamera)));
            Assert.That(Simulation(preview).CurrentGroundHeight, Is.EqualTo(3));
        }
        finally { release.TrySetResult(true); occupied?.GetAwaiter().GetResult(); graphics.Release(); }
    }

    private static VfxConversion Conversion(bool model, bool events, bool trail = false) => events
        ? (VfxConversion)typeof(VfxInteractiveGroundPreparationTests).GetMethod("Conversion", Private)!.Invoke(null, [model, trail])!
        : (VfxConversion)typeof(VfxInteractiveSeekPreparationTests).GetMethod("Conversion", Private)!.Invoke(null, [model, false, trail, !trail])!;

    private static TotalWarVfxSimulation Initialize(VfxConversion conversion, Matrix attachment, Func<float, Matrix> history,
        bool current, bool durationFirst, bool checkpoint)
    {
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 3.7f };
        if (checkpoint) simulation.EnableSeekCheckpoints();
        simulation.PrepareInitialGround(2, attachment, history, current, false, null);
        if (durationFirst) _ = simulation.CycleDuration;
        if (current) simulation.SeekWithCurrentAttachment(.27f, attachment, history); else simulation.Seek(.27f, attachment, history);
        return simulation;
    }

    private static EffekseerVfxPreview Preview(VfxConversion conversion, TotalWarVfxSimulation simulation, bool constant, bool durationFirst)
    {
        var preview = new EffekseerVfxPreview(null!);
        Set(preview, "_conversion", conversion); Set(preview, "_simulation", simulation);
        preview.EmissionEndTimeSeconds = simulation.EmissionEndTimeSeconds;
        preview.EnableInteractivePrewarm(); preview.ConfigureInteractiveSeekFrame(constant, 2, durationFirst);
        return preview;
    }
    private static void Apply(EffekseerVfxPreview preview, float time, Matrix attachment, Func<float, Matrix> history, bool current)
        => preview.Seek(time, attachment, history, current);
    private static void Finish(EffekseerVfxPreview preview, Matrix attachment, Func<float, Matrix>? history)
        => Wait(() => { preview.PrepareInteractiveFrame(attachment, history); return !preview.IsSeekingFrame && !preview.IsPreparing; });
    private static void Wait(Func<bool> complete)
    {
        var started = Stopwatch.GetTimestamp();
        var ready = complete();
        while (!ready && Stopwatch.GetElapsedTime(started).TotalSeconds < 10) { Thread.Sleep(1); ready = complete(); }
        Assert.That(ready, Is.True, "Bounded deterministic frame preparation did not finish.");
    }
    private static void AssertSame(TotalWarVfxSimulation expected, TotalWarVfxSimulation actual, CommonShaderParameters? camera = null)
    {
        Assert.That(actual.Time, Is.EqualTo(expected.Time));
        Assert.That(actual.ProjectedFrame(camera ?? Camera, false), Is.EqualTo(expected.ProjectedFrame(camera ?? Camera, false)));
        Assert.That(ParticleValues(actual), Is.EqualTo(ParticleValues(expected)), "Retain hidden random channels, births, velocity, scale and spin state.");
        Assert.That(Read(actual, "_simulatedTime"), Is.EqualTo(Read(expected, "_simulatedTime")));
        Assert.That(actual.IsDrained, Is.EqualTo(expected.IsDrained));
    }
    private static Vector4[] Render(GraphicsDevice device, EffekseerVfxPreview preview, CommonShaderParameters? explicitCamera = null)
    {
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        var eye = new Vector3(0, 0, 10);
        var camera = explicitCamera ?? (Camera with { View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Projection = Matrix.CreateOrthographic(4, 4, .1f, 100), CameraPosition = eye });
        try { preview.Draw(camera); } finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[64 * 64]; target.GetData(pixels); return pixels;
    }
    private static EffekseerParticle[] NativePacket(EffekseerVfxPreview preview)
    {
        var frame = Simulation(preview).ProjectedFrame(Camera, false);
        var count = (int)typeof(EffekseerVfxPreview).GetMethod("PrepareSubmission", Private)!.Invoke(preview, [frame])!;
        return ((EffekseerParticle[])Read(preview, "_submissionParticles")!).Take(count).ToArray();
    }
    private static object[] ParticleValues(TotalWarVfxSimulation simulation) => ((IEnumerable)Read(simulation, "_particles")!).Cast<object>()
        .Select(particle => (object)particle.GetType().GetFields().Where(field => field.FieldType.IsValueType)
            .OrderBy(field => field.Name).Select(field => field.GetValue(particle)).ToArray()).ToArray();
    private static TotalWarVfxSimulation Simulation(EffekseerVfxPreview preview) => (TotalWarVfxSimulation)Read(preview, "_simulation")!;
    private static object? Read(object owner, string field) => owner.GetType().GetField(field, Private)!.GetValue(owner);
    private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, Private)!.SetValue(owner, value);
    private static void Update(VfxPreviewComponent component, float seconds) => component.Update(new(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));
    private sealed class Factory(IVfxPreview preview) : IVfxPreviewFactory { public IVfxPreview Create(GraphicsDevice device) => preview; }
    private sealed class SimulationPreview(EffekseerVfxPreview preview) : IVfxPreview
    {
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => preview.ParticleCount;
        public float CycleDuration => 10;
        public bool IsSeekingFrame => preview.IsSeekingFrame;
        public void ConfigureInteractiveSeekHistory(bool isConstant) => preview.ConfigureInteractiveSeekHistory(isConstant);
        public void ConfigureInteractiveSeekFrame(bool isHistoryConstant, float? groundHeight, bool initializeDuration = false, bool allowForwardReplay = true)
            => preview.ConfigureInteractiveSeekFrame(isHistoryConstant, groundHeight, initializeDuration, allowForwardReplay);
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? history) => preview.PrepareInteractiveFrame(attachment, history);
        public void Reload(VfxPreviewSnapshot snapshot) { }
        public void Advance(float seconds, Matrix attachment) => preview.Advance(seconds, attachment);
        public void Seek(float seconds, Matrix attachment) => preview.Seek(seconds, attachment);
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
    private sealed class WaitingPreview : IVfxPreview
    {
        private float _time;
        public bool Ready;
        public float Latest;
        public bool IsSeekingFrame { get; private set; }
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => 1;
        public float CycleDuration => 10;
        public void Reload(VfxPreviewSnapshot snapshot) { _time = 0; IsSeekingFrame = false; }
        public void Advance(float seconds, Matrix attachment) => Seek(_time + seconds, attachment);
        public void Seek(float seconds, Matrix attachment)
        {
            Latest = seconds;
            if (Math.Abs(seconds - _time) > 3f / 120) IsSeekingFrame = true;
            else _time = seconds;
        }
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? history)
        {
            if (Ready && IsSeekingFrame) { _time = Latest; IsSeekingFrame = false; }
        }
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
}
