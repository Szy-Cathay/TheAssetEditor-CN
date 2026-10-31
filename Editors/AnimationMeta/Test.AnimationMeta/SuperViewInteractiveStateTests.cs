using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public sealed class SuperViewInteractiveStateTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PendingXmlReload_SurvivesPlaybackResetAndRetirement(bool retire)
    {
        using var control = new InteractiveControl();
        control.Update(.5f);
        var original = control.Factory.Created.Single();
        original.Ready = false;
        control.Live.Publish(this, control.File, Snapshot(2));
        control.Update(.7f);
        Assert.That(original.HasPendingInteractiveReload, Is.True);
        if (retire) control.Node.RetireCurrentPlayback(.7f);
        else control.Node.ResetPlayback();
        control.Node.TimeSeconds = .1f;
        control.Node.PrepareFrame(Matrix.Identity);
        var next = control.Factory.Created.Last();
        Assert.That(next.BegunSnapshots, Does.Contain(2), "A timeline operation must not lose the most recent unsaved XML revision.");
        foreach (var preview in control.Factory.Created) preview.Ready = true;
        Assert.That(control.Node.PrepareFrame(Matrix.Identity), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(next.ActiveSnapshot, Is.EqualTo(2));
            Assert.That(next.HasPendingInteractiveReload, Is.False);
            Assert.That(next.Seeks.Last(), Is.EqualTo(.1f));
            Assert.That(next.CommittedSnapshots.Last(), Is.EqualTo(2));
        });
    }

    [Test]
    public void MetaTransformRefresh_KeepsTheCompletedEffectDrawableWhileItsRestartPrepares()
    {
        using var control = new InteractiveControl();
        control.Update(.5f);
        var preview = control.Factory.Created.Single();
        var marker = control.Root.Children.Single();
        preview.Ready = false;
        control.Source.Position = new(4, 1, -2);
        control.Source.Scale = 2;
        Assert.That(control.Effect.TryRefreshTransform(), Is.True);
        Assert.That(control.Update(.5f), Is.True, "The completed old simulation must remain drawable during exact replacement preparation.");
        ((IRenderItem)control.Node).Draw(null!, new(Matrix.Identity, Matrix.Identity, Vector3.Zero, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []), RenderingTechnique.Normal);
        Assert.Multiple(() =>
        {
            Assert.That(control.Factory.Created, Has.Count.EqualTo(1), "The in-place restart must retain its loaded resources and completed simulation.");
            Assert.That(control.Root.Children.Single(), Is.SameAs(marker));
            Assert.That(preview.Disposed, Is.False);
            Assert.That(preview.Restarts, Is.EqualTo(1));
            Assert.That(preview.HasPendingInteractiveReload, Is.True);
            Assert.That(preview.DrawnSnapshots.Last(), Is.EqualTo(1));
            Assert.That(preview.Time, Is.EqualTo(.5f));
        });
        preview.Ready = true;
        Assert.That(control.Update(.5f), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(preview.HasPendingInteractiveReload, Is.False);
            Assert.That(preview.Time, Is.EqualTo(.5f));
            Assert.That(preview.History!(0), Is.EqualTo(Matrix.CreateScale(2) * Matrix.CreateTranslation(control.Source.Position)));
            Assert.That(control.Factory.Created, Has.Count.EqualTo(1));
        });
    }

    private static VfxPreviewSnapshot Snapshot(byte revision) => new([revision], @"vfx\interactive_state.xml", _ => null);

    private sealed class InteractiveControl : IDisposable
    {
        public DeferredFactory Factory { get; } = new();
        public VfxPreviewDocumentStore Live { get; } = new();
        public PackFile File { get; } = PackFile.CreateFromBytes("interactive_state.xml", [1]);
        public GroupNode Root { get; } = new("interactive-state-diagnostic");
        public Effect_v11 Source { get; } = new() { Name = "EFFECT", Version = 11, VfxName = "interactive_state", EndTime = 3, Scale = 1, NodeIndex = -1 };
        public EffectMetaInstance Effect { get; }
        public VfxPreviewSceneNode Node => (VfxPreviewSceneNode)Root.Children.Single().Children.Single();
        public InteractiveControl()
        {
            // DIAG: opaque revision bytes exercise scheduling only; no game rendering is claimed.
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.FindFile(@"vfx\interactive_state.xml", null)).Returns(File);
            files.Setup(service => service.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns(@"vfx\interactive_state.xml");
            files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
            var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, Factory, Mock.Of<IDeviceResolver>(), Live)
            { EnableInteractiveVfxPreview = true };
            Effect = (EffectMetaInstance)builder.Build(null, new() { Version = 2, Attributes = [Source] }, null,
                Root, null!, new(), null).Instances.Single();
        }
        public bool Update(float time)
        {
            Effect.Update(time);
            return Node.PrepareFrame(Node.Parent.ModelMatrix);
        }
        public void Dispose() => Effect.CleanUp();
    }

    private sealed class DeferredFactory : IVfxPreviewFactory
    {
        public List<DeferredPreview> Created { get; } = [];
        public IVfxPreview Create(GraphicsDevice device) => Create(ready: true);
        public DeferredPreview Create(bool ready)
        {
            var preview = new DeferredPreview(this) { Ready = ready };
            Created.Add(preview); return preview;
        }
    }

    private sealed class DeferredPreview(DeferredFactory factory) : IVfxPreview
    {
        private VfxPreviewSnapshot? _pending;
        private bool _restart, _canSeek;
        public bool Ready, Disposed;
        public int ActiveSnapshot, Restarts;
        public float Time;
        public Func<float, Matrix>? History;
        public List<int> BegunSnapshots { get; } = [];
        public List<int> CommittedSnapshots { get; } = [];
        public List<int> DrawnSnapshots { get; } = [];
        public List<float> Seeks { get; } = [];
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => ActiveSnapshot == 0 ? 0 : 1;
        public int TotalEmitterCount => EmitterCount;
        public int ParticleCount => EmitterCount;
        public bool HasPendingInteractiveReload => _pending != null || _restart;
        public bool IsDrained => _canSeek ? false : throw new InvalidOperationException("Unprepared playback has no drain result.");
        public float? EmissionEndTimeSeconds { get; set; }
        public void Reload(VfxPreviewSnapshot snapshot) { ActiveSnapshot = snapshot.GameBytes[0]; _canSeek = true; }
        public bool BeginInteractiveReload(VfxPreviewSnapshot snapshot)
        {
            _pending = snapshot; _restart = false; BegunSnapshots.Add(snapshot.GameBytes[0]); return false;
        }
        public bool BeginInteractiveRestart() { _restart = true; Restarts++; return false; }
        public bool ContinueInteractiveReload(VfxInteractiveReloadFrame frame)
        {
            if (!Ready) return false;
            if (_pending != null) { ActiveSnapshot = _pending.GameBytes[0]; CommittedSnapshots.Add(ActiveSnapshot); }
            _pending = null; _restart = false; _canSeek = true; return true;
        }
        public IVfxPreview CreatePlaybackInstance()
        {
            var clone = factory.Create(ready: false); clone.ActiveSnapshot = ActiveSnapshot; return clone;
        }
        public void Advance(float seconds, Matrix attachment) => Seek(seconds, attachment);
        public void Seek(float seconds, Matrix attachment)
        {
            if (!_canSeek) throw new InvalidOperationException("Do not synchronously seek an unprepared clone.");
            Time = seconds; Seeks.Add(seconds);
        }
        public void Seek(float seconds, Matrix attachment, Func<float, Matrix> history) { Seek(seconds, attachment); History = history; }
        public void Draw(CommonShaderParameters parameters) { if (!_canSeek) throw new InvalidOperationException("Unprepared draw."); DrawnSnapshots.Add(ActiveSnapshot); }
        public void Dispose() { Disposed = true; _pending = null; _restart = false; }
    }
}
