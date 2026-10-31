#nullable enable
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace GameWorld.Core.Test.Vfx;

public class VfxInteractiveReloadHostTests
{
    [Test]
    public void LegacyComponentRetainsItsSynchronousDefault()
    {
        var preview = new DeferredPreview();
        using var component = Component(preview);
        component.Reload(Snapshot(1));
        Update(component, 1);
        Assert.Multiple(() =>
        {
            Assert.That(preview.SynchronousReloads, Is.EqualTo(1));
            Assert.That(preview.BegunSnapshots, Is.Empty);
            Assert.That(component.CurrentTime, Is.EqualTo(1));
            Assert.That(preview.Seeks, Is.EqualTo(new[] { 1f }));
        });
    }

    [Test]
    public void FirstInteractiveLoadWaitsWithoutAdvancingAndInitializesHistoryBeforePreparation()
    {
        var preview = new DeferredPreview();
        using var component = Component(preview, interactive: true);
        var attachment = Matrix.CreateTranslation(7, 0, 3);
        var firstHistory = Matrix.Identity;
        var scopeResets = 0;
        component.Attachment = () => attachment;
        component.AttachmentAtTime = _ => firstHistory;
        component.SnapshotReloaded += () => { scopeResets++; firstHistory = attachment; };
        component.Reload(Snapshot(1));
        Update(component, 20);
        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.Zero);
            Assert.That(preview.Seeks, Is.Empty);
            Assert.That(preview.PreparedHistories.Single(), Is.EqualTo(attachment), "The initial scope reset must precede the first preparation history sample.");
            Assert.That(preview.SynchronousReloads, Is.Zero);
        });
        preview.Ready = true;
        Update(component, 20);
        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.Zero, "Preparation latency is not animation elapsed time.");
            Assert.That(scopeResets, Is.EqualTo(1), "Do not reset history again after the prepared simulation already consumed it.");
            Assert.That(preview.ActiveSnapshot, Is.EqualTo(1));
        });
    }

    [Test]
    public void ContentPreparationKeepsOldPlaybackMovingAndOnlyCommitsTheLatestSnapshot()
    {
        var preview = new DeferredPreview { Ready = true };
        using var component = Component(preview, interactive: true);
        component.Reload(Snapshot(1)); Update(component, 0); Update(component, 1);
        var resets = 0;
        component.SnapshotReloaded += () => resets++;
        preview.Ready = false;
        component.Reload(Snapshot(2), preservePlaybackState: true); Update(component, .25f);
        Assert.That(component.CurrentTime, Is.EqualTo(1.25f));
        Assert.That(preview.LastSeekSnapshot, Is.EqualTo(1));
        component.Reload(Snapshot(3), preservePlaybackState: true); Update(component, .25f);
        Assert.That(component.CurrentTime, Is.EqualTo(1.5f));
        Assert.That(preview.ActiveSnapshot, Is.EqualTo(1));
        preview.Ready = true; Update(component, .25f);
        Assert.Multiple(() =>
        {
            Assert.That(preview.BegunSnapshots, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(preview.CommittedSnapshots, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(component.CurrentTime, Is.EqualTo(1.5f));
            Assert.That(component.IsPlaying, Is.True);
            Assert.That(resets, Is.Zero);
        });
    }

    [Test]
    public void StopDisposesAnUnfinishedReloadAndDoesNotCommitItsLateCompletion()
    {
        var preview = new DeferredPreview();
        using var component = Component(preview, interactive: true);
        component.Reload(Snapshot(1)); Update(component, 0);
        component.Stop();
        preview.Ready = true; Update(component, 10);
        Assert.Multiple(() =>
        {
            Assert.That(preview.Disposed, Is.True);
            Assert.That(preview.HasPendingInteractiveReload, Is.False);
            Assert.That(preview.CommittedSnapshots, Is.Empty);
            Assert.That(component.CurrentTime, Is.Zero);
            Assert.That(component.IsLoaded, Is.False);
        });
    }

    [Test]
    public void LegacySceneNodeRetainsItsSynchronousDefault()
    {
        var preview = new DeferredPreview();
        using var node = Node(preview, out _, out _);
        node.TimeSeconds = .4f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        Assert.That(preview.SynchronousReloads, Is.EqualTo(1));
        Assert.That(preview.BegunSnapshots, Is.Empty);
        Assert.That(preview.Seeks.Single(), Is.EqualTo(.4f));
    }

    [Test]
    public void SceneContentReloadKeepsTheExistingEffectVisibleAndAdvancingWhileItPrepares()
    {
        var preview = new DeferredPreview { Ready = true };
        using var node = Node(preview, out var live, out var file);
        node.EnableInteractiveReload = true;
        node.TimeSeconds = .4f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        preview.Ready = false;
        live.Publish(this, file, Snapshot(2));
        node.TimeSeconds = .7f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True, "A valid previous effect must remain drawable while its replacement is prepared.");
        Assert.Multiple(() =>
        {
            Assert.That(preview.Seeks.Last(), Is.EqualTo(.7f));
            Assert.That(preview.LastSeekSnapshot, Is.EqualTo(1));
            Assert.That(preview.ActiveSnapshot, Is.EqualTo(1));
        });
        preview.Ready = true;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        Assert.That(preview.ActiveSnapshot, Is.EqualTo(2));
    }

    [Test]
    public void SharedRestartDoesNotSynchronouslySeekAnUnpreparedPlaybackClone()
    {
        var preview = new DeferredPreview { Ready = true };
        using var node = Node(preview, out _, out _);
        node.EnableInteractiveReload = true;
        node.TimeSeconds = .4f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        node.ResetPlayback();
        var clone = preview.Clones.Single();
        Assert.That(clone.Restarts, Is.EqualTo(1));
        node.TimeSeconds = .1f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.False);
        Assert.That(clone.Seeks, Is.Empty);
        clone.Ready = true;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        Assert.That(clone.Seeks.Single(), Is.EqualTo(.1f));
    }

    [Test]
    public void AStillPreparingRestartMovedToTailIsPumpedBeforeSeekAndDrainQueries()
    {
        var preview = new DeferredPreview { Ready = true };
        using var node = Node(preview, out _, out _);
        node.EnableInteractiveReload = true;
        node.TimeSeconds = .4f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        node.ResetPlayback();
        var pending = preview.Clones.Single();
        node.TimeSeconds = .2f;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.False);
        node.RetireCurrentPlayback(.3f);
        node.AdvancePlaybackTails(.1f);
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.False);
        Assert.That(pending.Seeks, Is.Empty);
        Assert.That(pending.Continues, Is.GreaterThanOrEqualTo(2));
        pending.Ready = true;
        Assert.That(node.PrepareFrame(Matrix.Identity), Is.True);
        Assert.That(pending.Seeks.Last(), Is.EqualTo(.3f));
    }

    private static VfxPreviewComponent Component(DeferredPreview preview, bool interactive = false)
        => new(new Factory(preview), Mock.Of<IDeviceResolver>(), null!) { EnableInteractiveReload = interactive, Loop = false };

    private static VfxPreviewSceneNode Node(DeferredPreview preview, out VfxPreviewDocumentStore live, out PackFile file)
    {
        file = PackFile.CreateFromBytes("sample.xml", [1]);
        var root = file;
        var files = new Mock<IPackFileService>();
        files.Setup(service => service.GetFullPath(root, It.IsAny<PackFileContainer?>())).Returns("vfx/sample.xml");
        files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
        live = new();
        return new(new Factory(preview), Mock.Of<IDeviceResolver>(), new(files.Object, file, live));
    }

    private static VfxPreviewSnapshot Snapshot(byte value) => new([value], "vfx/sample.xml", _ => null);
    private static void Update(VfxPreviewComponent component, float seconds) => component.Update(new(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));
    private sealed class Factory(DeferredPreview preview) : IVfxPreviewFactory { public IVfxPreview Create(GraphicsDevice device) => preview; }

    private sealed class DeferredPreview : IVfxPreview
    {
        private VfxPreviewSnapshot? _pending;
        private bool _restart, _canSeek;
        private float? _emissionEnd;
        public bool Ready, Disposed;
        public int ActiveSnapshot, LastSeekSnapshot, SynchronousReloads, Continues, Restarts;
        public List<int> BegunSnapshots { get; } = [];
        public List<int> CommittedSnapshots { get; } = [];
        public List<float> Seeks { get; } = [];
        public List<Matrix?> PreparedHistories { get; } = [];
        public List<DeferredPreview> Clones { get; } = [];
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => ActiveSnapshot == 0 ? 0 : 1;
        public int TotalEmitterCount => EmitterCount;
        public int ParticleCount => EmitterCount;
        public float CycleDuration => 100;
        public bool HasPendingInteractiveReload => _pending != null || _restart;
        public bool IsDrained => _canSeek ? false : throw new InvalidOperationException("An unfinished playback must not be queried as drained.");
        public float? EmissionEndTimeSeconds { get => _emissionEnd; set => _emissionEnd = value; }
        public void Reload(VfxPreviewSnapshot snapshot)
        {
            SynchronousReloads++;
            ActiveSnapshot = snapshot.GameBytes[0];
            _canSeek = true;
        }
        public bool BeginInteractiveReload(VfxPreviewSnapshot snapshot)
        {
            _pending = snapshot; _restart = false;
            BegunSnapshots.Add(snapshot.GameBytes[0]);
            return false;
        }
        public bool BeginInteractiveRestart() { _restart = true; _canSeek = false; Restarts++; return false; }
        public bool ContinueInteractiveReload(VfxInteractiveReloadFrame frame)
        {
            Continues++;
            PreparedHistories.Add(frame.History?.Invoke(0));
            if (!Ready) return false;
            if (_pending != null)
            {
                ActiveSnapshot = _pending.GameBytes[0];
                CommittedSnapshots.Add(ActiveSnapshot);
            }
            _pending = null; _restart = false; _canSeek = true;
            return true;
        }
        public IVfxPreview CreatePlaybackInstance()
        {
            var clone = new DeferredPreview { ActiveSnapshot = ActiveSnapshot };
            Clones.Add(clone);
            return clone;
        }
        public void Advance(float seconds, Matrix attachment) => Seek(seconds, attachment);
        public void Seek(float seconds, Matrix attachment)
        {
            if (!_canSeek) throw new InvalidOperationException("Do not synchronously seek an unfinished playback.");
            Seeks.Add(seconds); LastSeekSnapshot = ActiveSnapshot;
        }
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() { Disposed = true; _pending = null; _restart = false; }
    }
}
