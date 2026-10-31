#nullable enable
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxInteractiveUploadTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Phase = .2f;
    private static readonly Vector3 Eye = new(0, 0, 10);
    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(Eye, Vector3.Zero, Vector3.Up),
        Matrix.CreateOrthographic(2, 2, .1f, 100), Eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(false)]
    [TestCase(true)]
    public void ColdUploadPublishesNoPartialHandleAndMatchesCompleteSyncPacketsAndPixels(bool includeUnusedResourceStages) => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        var snapshot = Snapshot(.6f, original);
        var upload = AttachUpload(parent, device, snapshot, includeUnusedResourceStages);
        var stages = new List<int>();
        EffekseerHandle? pendingHandle = null;
        while (upload.Cursor.MoveNext())
        {
            stages.Add(upload.Cursor.Current);
            pendingHandle ??= CursorHandle(upload.Cursor);
            Assert.Multiple(() =>
            {
                Assert.That(Handle(parent), Is.Null);
                Assert.That(parent.EmitterCount, Is.Zero);
                if (upload.Cursor.Current < 5)
                    Assert.That(Handle(upload.Candidate), Is.Null, "A resource step cannot expose an incompletely configured candidate handle.");
                else Assert.That(Handle(upload.Candidate), Is.SameAs(pendingHandle));
                Assert.That(pendingHandle, Is.Not.Null);
                Assert.That(pendingHandle!.IsClosed, Is.False);
            });
            Assert.That(Render(device, parent).All(pixel => pixel.X == 0 && pixel.Y == 0 && pixel.Z == 0), Is.True);
        }
        Assert.That(stages, Does.Contain(0)); Assert.That(stages, Does.Contain(1)); Assert.That(stages, Does.Contain(5));
        if (includeUnusedResourceStages)
            foreach (var stage in new[] { 2, 3, 4 }) Assert.That(stages, Does.Contain(stage));
        Assert.That(Handle(upload.Candidate), Is.SameAs(pendingHandle));
        Assert.That(Handle(parent), Is.Null, "Even a fully configured private candidate is not public until its owner adopts it.");
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.True);
        Assert.That(Handle(parent), Is.SameAs(pendingHandle));
        Assert.That(Read(parent, "_reloadUploadPreview"), Is.Null);
        Assert.That(Read(parent, "_reloadUpload"), Is.Null);
        Assert.That(Read(upload.Candidate, "_disposed"), Is.True);
        using var control = new EffekseerVfxPreview(device);
        control.Reload(snapshot); control.Seek(Phase, Matrix.Identity);
        AssertPackets(control, parent);
        var pixels = Render(device, parent);
        Assert.That(pixels.Count(pixel => pixel.X + pixel.Y + pixel.Z > .01f), Is.GreaterThan(10));
        AssertPixels(Render(device, control), pixels);
        parent.Dispose();
        Assert.That(pendingHandle!.IsClosed, Is.True);
    });

    [Test]
    public void ExistingParentAndSharedTailKeepTheirFullPictureAtEveryUploadStepUntilAdoption() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent)!;
        using var tail = (EffekseerVfxPreview)parent.CreatePlaybackInstance()!;
        tail.Seek(Phase, Matrix.Identity);
        var originalPixels = Render(device, parent);
        var originalPackets = Packet(parent);
        var snapshot = Snapshot(.35f, original);
        var upload = AttachUpload(parent, device, snapshot, true);
        while (upload.Cursor.MoveNext())
        {
            Assert.That(Handle(parent), Is.SameAs(oldHandle));
            Assert.That(oldHandle.IsClosed, Is.False);
            Assert.That(Packet(parent), Is.EqualTo(originalPackets));
            AssertPixels(originalPixels, Render(device, parent));
            AssertPixels(originalPixels, Render(device, tail));
        }
        var newHandle = Handle(upload.Candidate)!;
        Assert.That(Handle(parent), Is.SameAs(oldHandle));
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.True);
        Assert.That(Handle(parent), Is.SameAs(newHandle));
        Assert.That(Handle(parent), Is.Not.SameAs(oldHandle));
        Assert.That(oldHandle.IsClosed, Is.False, "The live META tail keeps the old resource owner alive after adoption.");
        AssertPixels(originalPixels, Render(device, tail));
        using var control = new EffekseerVfxPreview(device);
        control.Reload(snapshot); control.Seek(Phase, Matrix.Identity);
        AssertPackets(control, parent);
        var changed = Render(device, parent);
        AssertPixels(Render(device, control), changed);
        Assert.That(changed.SequenceEqual(originalPixels), Is.False, "The adopted edit must change the actual picture.");
        parent.Dispose();
        Assert.That(newHandle.IsClosed, Is.True);
        Assert.That(oldHandle.IsClosed, Is.False);
        AssertPixels(originalPixels, Render(device, tail));
        tail.Dispose();
        Assert.That(oldHandle.IsClosed, Is.True);
    });

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void CancellingEachIncompleteStageClosesOnlyTheCandidateAndKeepsTheOriginalPlayback(int stage) => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent)!; var before = Render(device, parent);
        var upload = AttachUpload(parent, device, Snapshot(.5f, original), true);
        AdvanceThrough(upload.Cursor, stage);
        var candidateHandle = CursorHandle(upload.Cursor)!;
        Assert.That(candidateHandle.IsClosed, Is.False);
        Call(parent, "CancelInteractiveReload");
        Assert.Multiple(() =>
        {
            Assert.That(candidateHandle.IsClosed, Is.True);
            Assert.That(Read(upload.Candidate, "_disposed"), Is.True);
            Assert.That(Read(parent, "_reloadUpload"), Is.Null);
            Assert.That(Read(parent, "_reloadUploadPreview"), Is.Null);
            Assert.That(parent.HasPendingInteractiveReload, Is.False);
            Assert.That(Handle(parent), Is.SameAs(oldHandle));
            Assert.That(oldHandle.IsClosed, Is.False);
        });
        AssertPixels(before, Render(device, parent));
        parent.Dispose(); Assert.That(oldHandle.IsClosed, Is.True);
    });

    [Test]
    public void DisposingTheOwnerDuringUploadClosesTheCandidateButPreservesAnIndependentTail() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        using var tail = (EffekseerVfxPreview)parent.CreatePlaybackInstance()!;
        tail.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent)!; var before = Render(device, tail);
        var upload = AttachUpload(parent, device, Snapshot(.5f, original), true);
        AdvanceThrough(upload.Cursor, 3);
        var candidateHandle = CursorHandle(upload.Cursor)!;
        parent.Dispose(); parent.Dispose();
        Assert.That(candidateHandle.IsClosed, Is.True);
        Assert.That(Read(upload.Candidate, "_disposed"), Is.True);
        Assert.That(Handle(parent), Is.Null);
        Assert.That(oldHandle.IsClosed, Is.False);
        AssertPixels(before, Render(device, tail));
        tail.Dispose(); Assert.That(oldHandle.IsClosed, Is.True);
    });

    [Test]
    public void FilteredSecondTextureKeepsTheAdoptedSimulationValidAcrossSeparateUploadTurns() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var before = Render(device, parent);
        var document = VfxDocument.Read(Snapshot(.6f, original).GameBytes);
        var effect = document.Root.Element("vfx")!;
        var emitter = new XElement(effect.Element("emitters")!.Element("emitter")!);
        emitter.SetAttributeValue("id", "failed-upload-DIAG"); effect.Element("emitters")!.Add(emitter);
        var chain = new XElement(effect.Element("behaviour_chains")!.Element("behaviour_chain")!);
        chain.SetAttributeValue("id", "failed-upload-DIAG");
        chain.Descendants("texture_property").Single().SetAttributeValue("path", "DIAG-invalid.dds");
        effect.Element("behaviour_chains")!.Add(chain);
        var snapshot = new VfxPreviewSnapshot(document.Write(), "vfx/failed-upload-DIAG.xml",
            path => path == "DIAG-invalid.dds" ? [1, 2, 3] : original);
        var upload = AttachUpload(parent, device, snapshot, true);
        var observedFilteredStage = false;
        while (upload.Cursor.MoveNext())
        {
            if (((VfxConversion)Read(upload.Candidate, "_conversion")!).Emitters.Count == 1
                && upload.Cursor.Current is 3 or 4) observedFilteredStage = true;
            AssertPixels(before, Render(device, parent));
        }
        Assert.That(observedFilteredStage, Is.True, "Filtering must occur in an earlier yielded turn than final adoption.");
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.False,
            "A changed emitter projection needs a fresh exact prepared simulation even when filtering happened in a previous upload turn.");
        Assert.DoesNotThrow(() => Render(device, parent), "Immediately after adoption, no stale particle may address the removed second layer.");
        for (var turn = 0; turn < 1000 && parent.HasPendingInteractiveReload; turn++)
        {
            parent.ContinueInteractiveReload(new(Phase, Matrix.Identity));
            Thread.Sleep(1);
        }
        Assert.That(parent.HasPendingInteractiveReload, Is.False);
        Assert.That(parent.EmitterCount, Is.EqualTo(1));
        Assert.That(parent.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.LayerTextureUnavailable"), Is.True);
        using var control = new EffekseerVfxPreview(device);
        control.Reload(snapshot); control.Seek(Phase, Matrix.Identity);
        AssertPackets(control, parent);
        AssertPixels(Render(device, control), Render(device, parent));
    });

    [Test]
    public void FatalCandidateConfigurationCannotBeAdoptedOrReleaseTheOldSharedHandle() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        using var tail = (EffekseerVfxPreview)parent.CreatePlaybackInstance()!;
        tail.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent)!; var before = Render(device, tail);
        var upload = AttachUpload(parent, device, Snapshot(.5f, original), false);
        AdvanceThrough(upload.Cursor, 1);
        var candidateHandle = CursorHandle(upload.Cursor)!;
        // DIAG: force the actual native configuration validator to fail, after a valid texture upload.
        var conversion = (VfxConversion)Read(upload.Candidate, "_conversion")!;
        var invalid = conversion.Emitters[0].Render; invalid.TextureIndex = int.MaxValue;
        conversion.Emitters[0].Render = invalid;
        Assert.Throws<InvalidOperationException>(() => parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)));
        Assert.Multiple(() =>
        {
            Assert.That(candidateHandle.IsClosed, Is.True);
            Assert.That(Read(parent, "_reloadUploadPreview"), Is.Null);
            Assert.That(Read(parent, "_reloadUpload"), Is.Null);
            Assert.That(Handle(parent), Is.SameAs(oldHandle));
            Assert.That(oldHandle.IsClosed, Is.False);
            Assert.That(parent.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.True);
        });
        AssertPixels(before, Render(device, tail));
        parent.Dispose(); Assert.That(oldHandle.IsClosed, Is.False);
        tail.Dispose(); Assert.That(oldHandle.IsClosed, Is.True);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void OwnerDispatcherFinishesPrivateUploadWithoutPublishingUntilTheNextCompleteFrame(bool existingPlayback) => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        if (existingPlayback) { parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity); }
        var previousHandle = Handle(parent);
        var previousPixels = Render(device, parent);
        var previousPackets = existingPlayback ? Packet(parent) : [];
        var snapshot = Snapshot(.4f, original);
        var upload = AttachDispatchedUpload(parent, device, snapshot);
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.False);
        var pendingHandle = CursorHandle(upload.Inner)!;
        Assert.That(pendingHandle, Is.Not.Null);
        PumpDispatcherUntil(() => upload.Cursor.Completed);
        Assert.Multiple(() =>
        {
            Assert.That(upload.Cursor.Completed, Is.True, "The owner Dispatcher must finish private GPU preparation without another frame Continue call.");
            Assert.That(Handle(parent), Is.SameAs(previousHandle), "Idle owner turns cannot publish or replace the active playback.");
            Assert.That(parent.HasPendingInteractiveReload, Is.True);
            Assert.That(Handle(upload.Candidate), Is.SameAs(pendingHandle));
            Assert.That(pendingHandle.IsClosed, Is.False);
        });
        AssertPixels(previousPixels, Render(device, parent));
        if (existingPlayback) Assert.That(Packet(parent), Is.EqualTo(previousPackets));
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.True);
        using var control = new EffekseerVfxPreview(device);
        control.Reload(snapshot); control.Seek(Phase, Matrix.Identity);
        AssertPackets(control, parent);
        AssertPixels(Render(device, control), Render(device, parent));
    });

    [Test]
    public void CancelledOwnerUploadCallbackCannotAdvanceOrResurrectItsPrivateCandidate() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent); var previousPixels = Render(device, parent);
        var upload = AttachDispatchedUpload(parent, device, Snapshot(.4f, original));
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.False);
        var pendingHandle = CursorHandle(upload.Inner)!;
        var moves = upload.Cursor.Moves;
        Call(parent, "CancelInteractiveReload");
        DrainOwnerDispatcher();
        Assert.Multiple(() =>
        {
            Assert.That(upload.Cursor.Moves, Is.EqualTo(moves), "A queued late callback must verify the exact iterator before touching it.");
            Assert.That(upload.Cursor.Disposed, Is.True);
            Assert.That(pendingHandle.IsClosed, Is.True);
            Assert.That(Read(upload.Candidate, "_disposed"), Is.True);
            Assert.That(Handle(parent), Is.SameAs(oldHandle));
            Assert.That(parent.HasPendingInteractiveReload, Is.False);
        });
        AssertPixels(previousPixels, Render(device, parent));
    });

    [Test]
    public void OwnerUploadFailureIsRetainedUntilTheNextFrameAndNeverEscapesIntoTheDispatcher() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent); var previousPixels = Render(device, parent);
        var upload = AttachDispatchedUpload(parent, device, Snapshot(.4f, original));
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.False);
        var pendingHandle = CursorHandle(upload.Inner)!;
        var conversion = (VfxConversion)Read(upload.Candidate, "_conversion")!;
        var invalid = conversion.Emitters[0].Render; invalid.TextureIndex = int.MaxValue;
        conversion.Emitters[0].Render = invalid;
        Assert.DoesNotThrow(() => PumpDispatcherUntil(() => Read(upload.Candidate, "_convertedUploadFailure") != null));
        Assert.That(Read(upload.Candidate, "_convertedUploadFailure"), Is.Not.Null,
            "The actual native configuration failure must be captured even without another frame Continue.");
        Assert.That(Handle(parent), Is.SameAs(oldHandle));
        AssertPixels(previousPixels, Render(device, parent));
        Assert.Throws<InvalidOperationException>(() => parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)));
        Assert.Multiple(() =>
        {
            Assert.That(pendingHandle.IsClosed, Is.True);
            Assert.That(Read(upload.Candidate, "_disposed"), Is.True);
            Assert.That(Read(parent, "_reloadUploadPreview"), Is.Null);
            Assert.That(Handle(parent), Is.SameAs(oldHandle));
            Assert.That(parent.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.True);
        });
    });

    [TestCase(false)]
    [TestCase(true)]
    public void SharedNodeOrRejectedDispatcherKeepsTheExistingExplicitFrameUploadPump(bool rejectedDispatcher) => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var snapshot = Snapshot(.4f, original);
        var upload = AttachDispatchedUpload(parent, device, snapshot, rejectedDispatcher);
        if (rejectedDispatcher)
            Set(parent, "_scheduleReloadOwnerWork", new Action<Action>(_ => throw new InvalidOperationException("DIAG-owner-dispatch-rejected")));
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.False);
        var moves = upload.Cursor.Moves;
        DrainOwnerDispatcher();
        Assert.That(upload.Cursor.Moves, Is.EqualTo(moves),
            "Shared nodes retain their aggregate frame budget, and a rejected optional idle scheduler falls back to the frame pump.");
        for (var turn = 0; turn < 64 && parent.HasPendingInteractiveReload; turn++)
            parent.ContinueInteractiveReload(new(Phase, Matrix.Identity));
        Assert.That(parent.HasPendingInteractiveReload, Is.False);
        using var control = new EffekseerVfxPreview(device);
        control.Reload(snapshot); control.Seek(Phase, Matrix.Identity);
        AssertPackets(control, parent);
        AssertPixels(Render(device, control), Render(device, parent));
    });

    [Test]
    public void OwnerUploadStopsAfterAMissedFrameHeartbeatAndResumesOnlyWhenItsOwnerContinues() => WithGraphics((device, original) =>
    {
        using var parent = new EffekseerVfxPreview(device);
        parent.Reload(Snapshot(1, original)); parent.Seek(Phase, Matrix.Identity);
        var oldHandle = Handle(parent); var previousPixels = Render(device, parent);
        var snapshot = Snapshot(.4f, original);
        var upload = AttachDispatchedUpload(parent, device, snapshot, controlledEligibility: false);
        Assert.That(parent.ContinueInteractiveReload(new(Phase, Matrix.Identity)), Is.False);
        var moves = upload.Cursor.Moves;
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
            new Action(() => Thread.Sleep(30)));
        DrainOwnerDispatcher();
        Assert.That(upload.Cursor.Moves, Is.EqualTo(moves), "Idle upload must stop when its owner no longer pumps visible frames.");
        Assert.That(Handle(parent), Is.SameAs(oldHandle));
        AssertPixels(previousPixels, Render(device, parent));
        for (var turn = 0; turn < 64 && parent.HasPendingInteractiveReload; turn++)
        {
            parent.ContinueInteractiveReload(new(Phase, Matrix.Identity));
            DrainOwnerDispatcher();
        }
        Assert.That(parent.HasPendingInteractiveReload, Is.False, "Normal owner frames must restore and finish the exact upload after inactivity.");
        Assert.That(upload.Cursor.Completed, Is.True);
        using var control = new EffekseerVfxPreview(device);
        control.Reload(snapshot); control.Seek(Phase, Matrix.Identity);
        AssertPackets(control, parent);
        AssertPixels(Render(device, control), Render(device, parent));
    });

    private static (EffekseerVfxPreview Candidate, IEnumerator<int> Inner, SlowUploadCursor Cursor) AttachDispatchedUpload(
        EffekseerVfxPreview parent, GraphicsDevice device, VfxPreviewSnapshot snapshot, bool enableIdle = true, bool controlledEligibility = true)
    {
        _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        if (enableIdle) parent.EnableInteractivePrewarm();
        parent.BeginInteractiveReload(snapshot);
        // DIAG: the real Begin captures its owner scheduler; its replaced conversion must not leave unrelated work queued.
        ((IDisposable)Read(parent, "_reloadConversion")!).Dispose();
        Set(parent, "_reloadConversion", null);
        var upload = AttachUpload(parent, device, snapshot, false);
        Action? renewEligibility = controlledEligibility
            ? () => Set(parent, "_reloadUploadOwnerTimestamp", System.Diagnostics.Stopwatch.GetTimestamp()) : null;
        if (renewEligibility is { } heartbeat && Read(parent, "_scheduleReloadOwnerWork") is Action<Action> schedule)
        {
            // DIAG: freeze the eligibility dimension while testing GPU progress and publication independently of cold JIT/driver timing.
            // This never calls Continue/Seek or advances any particle, material or attachment clock.
            Set(parent, "_scheduleReloadOwnerWork", new Action<Action>(action => schedule(() => { heartbeat(); action(); })));
        }
        var slow = new SlowUploadCursor(upload.Cursor, renewEligibility);
        Set(parent, "_reloadUpload", slow);
        return (upload.Candidate, upload.Cursor, slow);
    }

    private sealed class SlowUploadCursor(IEnumerator<int> inner, Action? renewEligibility = null) : IEnumerator<int>
    {
        private readonly int _owner = Environment.CurrentManagedThreadId;
        public int Current => inner.Current;
        object System.Collections.IEnumerator.Current => Current;
        public int Moves { get; private set; }
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }
        public bool MoveNext()
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_owner), "Native resources stay on their device owner.");
            ObjectDisposedException.ThrowIf(Disposed, this);
            Moves++;
            var next = inner.MoveNext();
            Completed = !next;
            // DIAG: one indivisible resource step consumes a complete upload turn deterministically.
            Thread.Sleep(3);
            renewEligibility?.Invoke();
            return next;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { Disposed = true; inner.Dispose(); }
    }

    private static void PumpDispatcherUntil(Func<bool> complete)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) =>
        {
            if (complete() || System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2)) frame.Continue = false;
        };
        try { timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static void DrainOwnerDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static (EffekseerVfxPreview Candidate, IEnumerator<int> Cursor) AttachUpload(EffekseerVfxPreview parent,
        GraphicsDevice device, VfxPreviewSnapshot snapshot, bool unusedResourceStages)
    {
        // DIAG: deterministic upload/resource-lifetime control. The only sampled texture is the untouched official DDS.
        var conversion = TotalWarVfxConverter.Convert(snapshot, false);
        if (unusedResourceStages)
        {
            // Unreferenced, minimal geometry/bindings exercise model and material upload without changing the effect picture.
            var model = new VfxPreviewModel([
                new() { Position = new(-1, -1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(0, 1) },
                new() { Position = new(1, -1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(1, 1) },
                new() { Position = new(0, 1, 0), Normal = Vector3.Backward, Tangent = Vector3.Right, Binormal = Vector3.Up, UV = new(.5f, 0) },
            ], [0, 1, 2]);
            conversion.Models.Add(model); conversion.Models.Add(model);
            conversion.MeshMaterials.Add(new([0, 0, 0, 0], new float[20]));
        }
        var resources = VfxPreparedResources.Prepare(conversion);
        var identity = VfxPlaybackResourceIdentity.Create(conversion);
        var candidate = new EffekseerVfxPreview(device);
        Set(candidate, "_applyingInteractiveReload", true);
        var cursor = (IEnumerator<int>)Call(candidate, "BeginConvertedUpload", snapshot, conversion, identity, resources, false)!;
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(Phase, Matrix.Identity);
        Set(parent, "_reloadSnapshot", snapshot);
        Set(parent, "_preparedReload", new VfxPreparedConversion(conversion, identity, [], resources));
        Set(parent, "_reloadSimulation", simulation);
        Set(parent, "_reloadFrame", new VfxInteractiveReloadFrame(Phase, Matrix.Identity));
        Set(parent, "_reloadBoundsReady", true);
        Set(parent, "_reloadRetainsPlayback", Handle(parent) != null);
        Set(parent, "_reloadUploadPreview", candidate);
        Set(parent, "_reloadUpload", cursor);
        Set(parent, "_reloadUploadEmitters", conversion.Emitters.ToArray());
        return (candidate, cursor);
    }

    private static void AdvanceThrough(IEnumerator<int> cursor, int stage)
    {
        while (cursor.MoveNext()) if (cursor.Current == stage) return;
        Assert.Fail("The diagnostic conversion did not reach upload stage " + stage);
    }

    private static EffekseerHandle? CursorHandle(IEnumerator<int> cursor) => cursor.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(field => field.FieldType == typeof(EffekseerHandle)).Select(field => field.GetValue(cursor)).OfType<EffekseerHandle>().SingleOrDefault();

    private static VfxPreviewSnapshot Snapshot(float scale, byte[] original)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        document.Root.Descendants("property").Single(property => (string?)property.Attribute("name") == "ParticleScale").Value =
            string.Join('|', new[] { "x", "y", "z" }.Select(channel => VfxLibraryVisibilityTests.Constant(channel, scale).Split(':')[0]))
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
        return new(document.Write(), "vfx/upload-lifetime-DIAG.xml", _ => original);
    }

    private static EffekseerParticle[] Packet(EffekseerVfxPreview preview)
    {
        var simulation = (TotalWarVfxSimulation)Read(preview, "_simulation")!;
        var frame = simulation.ProjectedFrame(Camera);
        var count = (int)Call(preview, "PrepareSubmission", (object)frame)!;
        return ((EffekseerParticle[])Read(preview, "_submissionParticles")!).Take(count).ToArray();
    }
    private static void AssertPackets(EffekseerVfxPreview expected, EffekseerVfxPreview actual) =>
        Assert.That(Packet(actual), Is.EqualTo(Packet(expected)), "Every submitted native field and native layer mapping must match the synchronous upload at the same phase.");
    private static Vector4[] Render(GraphicsDevice device, EffekseerVfxPreview preview)
    {
        Assert.That(preview.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
        using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        try { preview.Draw(Camera); }
        finally { device.SetRenderTarget(null); }
        var pixels = new Vector4[64 * 64]; target.GetData(pixels); return pixels;
    }
    private static void AssertPixels(Vector4[] expected, Vector4[] actual) => Assert.That(actual, Is.EqualTo(expected),
        "Complete float-target pixels must stay identical while the old owner is retained and after an equivalent complete upload.");
    private static EffekseerHandle? Handle(EffekseerVfxPreview preview) => (EffekseerHandle?)Read(preview, "_handle");
    private static object? Read(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] arguments)
    {
        try { return target.GetType().GetMethod(name, Private)!.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }

    private static void WithGraphics(Action<GraphicsDevice, byte[]> test)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Explicit native offscreen rendering opt-in is required.");
        var assembly = typeof(VfxInteractiveUploadTests).Assembly;
        using var input = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("OfficialSiegeSmokeDiffuse.dds", StringComparison.Ordinal)))!;
        using var copy = new MemoryStream(); input.CopyTo(copy); var original = copy.ToArray();
        const string expected = "18346cc4e31f527c4abe1d92c287cd50591aa3e154e67b5815b44497e9aa4a70";
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo(expected));
        var graphics = new GraphicsDeviceServiceMock();
        try { test(graphics.GraphicsDevice, original); }
        finally { graphics.Release(); }
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo(expected));
    }
}
