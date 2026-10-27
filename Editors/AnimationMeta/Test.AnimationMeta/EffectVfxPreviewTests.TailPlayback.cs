using System.Text;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
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

public partial class EffectVfxPreviewTests
{
    [Test]
    public void TailPlayback_MetaEndStopsEmissionWithoutHidingTheExistingParticles()
    {
        using var control = new TailPlaybackControl(.25f, .75f);
        control.Update(new(.5f, .5, 1, 0, true, false));
        var original = control.Factory.Created.Single();
        control.Update(new(1, 1, 1, 0, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(control.Marker.IsVisible, Is.True);
            Assert.That(original.EmissionEndTimeSeconds, Is.EqualTo(.5f));
            Assert.That(original.Time, Is.EqualTo(.75f));
            Assert.That(original.Disposed, Is.False);
            Assert.That(control.Effect.ShowForEntireAnimation, Is.False);
            Assert.That(control.Source.StartTime, Is.EqualTo(.25f));
            Assert.That(control.Source.EndTime, Is.EqualTo(.75f));
        });
        original.Drained = true;
        control.Update(new(2, 2, 1, 0, true, false));
        Assert.That(control.Marker.IsVisible, Is.False, "Retirement follows actual runtime drain, not META End or CycleDuration.");
    }

    [Test]
    public void TailPlayback_NaturalLoopKeepsOldParticlesAndStartsAnIndependentNewRun()
    {
        using var control = new TailPlaybackControl(0, 0);
        control.Update(new(.9f, .9, 1, 0, true, false));
        var first = control.Factory.Created.Single();
        control.Update(new(0, 1.02, 1, 1, true, false));
        var second = control.Factory.Created.Last();
        Assert.Multiple(() =>
        {
            Assert.That(control.Factory.Created, Has.Count.EqualTo(2));
            Assert.That(first.Disposed, Is.False);
            Assert.That(first.Time, Is.EqualTo(1.02f).Within(.000001f));
            Assert.That(second.Time, Is.Zero);
            Assert.That(control.Node.ParticleCount, Is.EqualTo(2));
        });
        control.Update(new(.2f, 1.22, 1, 1, true, false));
        Assert.That(first.Time, Is.EqualTo(1.22f).Within(.000001f));
        Assert.That(second.Time, Is.EqualTo(.2f).Within(.000001f));
        first.Drained = true;
        control.Update(new(.3f, 1.32, 1, 1, true, false));
        Assert.That(first.Disposed, Is.True);
        Assert.That(second.Disposed, Is.False);
    }

    [Test]
    public void TailPlayback_PauseFreezesAndUserSeekOrStopResetsAllRuns()
    {
        using var control = new TailPlaybackControl(0, 0);
        control.Update(new(.9f, .9, 1, 0, true, false));
        control.Update(new(0, 1.02, 1, 1, true, false));
        var oldRuns = control.Factory.Created.ToArray();
        control.Update(new(0, 1.02, 1, 1, false, false));
        Assert.That(oldRuns[0].Time, Is.EqualTo(1.02f).Within(.000001f));
        control.Update(new(.4f, .4, 2, 0, false, false));
        Assert.That(oldRuns.All(run => run.Disposed), Is.True);
        Assert.That(control.Factory.Created.Last().Time, Is.EqualTo(.4f));
        control.Update(new(.4f, 0, 3, 0, false, true));
        Assert.That(control.Factory.Created.All(run => run.Disposed), Is.True);
        Assert.That(control.Marker.IsVisible, Is.False);
        control.Update(new(.4f, .4, 3, 0, true, false));
        Assert.That(control.Factory.Created.Last().Time, Is.EqualTo(.4f), "Play after Stop starts from the real clip time.");
    }

    [Test]
    public void TailPlayback_SourceEditAtLoopBoundaryStillReloadsAndDiscardsOutdatedTails()
    {
        using var control = new TailPlaybackControl(0, 0);
        control.Factory.ShareResources = true;
        control.Update(new(.9f, .9, 1, 0, true, false));
        var first = control.Factory.Created.Single();
        control.LiveDocuments.Publish(new object(), control.File,
            new(Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>"), @"vfx\tail-control.xml", _ => null));
        control.Update(new(0, 1.02, 1, 1, true, false));
        Assert.That(first.Disposed, Is.True, "The edited source invalidates all old cohorts.");
        Assert.That(control.Factory.Created.Last().ReloadCount, Is.EqualTo(1), "Sharing must not swallow an outstanding source reload.");
    }

    [Test]
    public void TailPlayback_EmptyPendingTailDoesNotPreventTheCurrentRunFromOpeningASceneScope()
    {
        using var control = new TailPlaybackControl(0, 0);
        control.Update(new(.9f, .9, 1, 0, true, false));
        control.Update(new(0, 1.02, 1, 1, true, false));
        var first = control.Factory.Created[0];
        var second = control.Factory.Created[1];
        using var scene = new TailSceneScope();
        second.Scene = scene;
        var opened = typeof(VfxPreviewSceneNode).GetMethod("BeginScene",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control.Node, [null]);
        Assert.That(opened, Is.SameAs(scene));
        Assert.That(first.SceneRequests, Is.EqualTo(1));
        Assert.That(second.SceneRequests, Is.EqualTo(1));
    }

    [Test]
    public void TailPlayback_NewCycleReusesLoadedResourcesWithoutReloadingOrSharingSimulation()
    {
        var player = new AnimationPlayer();
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        for (var index = 0; index < 2; ++index)
            clip.DynamicFrames.Add(new() { Position = [Vector3.Zero], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
        player.SetAnimation(clip, Skeleton(player));
        using var control = new TailPlaybackControl(.25f, .75f, player: player);
        control.Factory.ShareResources = true;
        control.Update(new(.9f, .9, 1, 0, true, false));
        var first = control.Factory.Created.Single();
        control.Update(new(0, 1.02, 1, 1, true, false));
        var second = control.Factory.Created.Last();
        control.Update(new(.3f, 1.32, 1, 1, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(control.Factory.Created, Has.Count.EqualTo(2));
            Assert.That(first.ReloadCount, Is.EqualTo(1));
            Assert.That(second.ReloadCount, Is.Zero, "The next playback already owns the uploaded immutable resources.");
            Assert.That(first.EmissionEndTimeSeconds, Is.EqualTo(.5f), "Loop retirement must not widen the original META stop boundary.");
            Assert.That(first.Time, Is.EqualTo(1.07f).Within(.000001f));
            Assert.That(second.Time, Is.EqualTo(.05f).Within(.000001f));
            Assert.That(first.Disposed, Is.False);
        });
        control.Update(new(.4f, .4, 2, 0, false, false));
        var sought = control.Factory.Created.Last();
        Assert.That(first.Disposed && second.Disposed, Is.True, "Seeking clears old simulations.");
        Assert.That(sought.ReloadCount, Is.Zero, "Seeking reuses the same unchanged resources.");
        Assert.That(sought.Time, Is.EqualTo(.15f).Within(.000001f));
    }

    [Test]
    public void TailPlayback_PersistentHistoryUsesActualLoopOriginsIncludingDiscardedOvershoot()
    {
        var player = new AnimationPlayer();
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        for (var index = 0; index < 2; ++index)
            clip.DynamicFrames.Add(new() { Position = [Vector3.Zero], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
        player.SetAnimation(clip, Skeleton(player));
        using var control = new TailPlaybackControl(0, 0, true, player);
        control.Factory.Infinite = true;
        control.Node.LocalAttachmentAtTime = seconds => Matrix.CreateTranslation(seconds * 10, 0, 0);
        control.Update(new(.9f, .9, 1, 0, true, false), Matrix.CreateTranslation(9, 0, 0));
        control.Update(new(0, 1.2, 1, 1, true, false));
        var preview = control.Factory.Created.Single();
        Assert.Multiple(() =>
        {
            Assert.That(preview.History!(1.2f).Translation.X, Is.EqualTo(0).Within(.00001f));
            Assert.That(preview.History(.9f).Translation.X, Is.EqualTo(9).Within(.00001f));
            Assert.That(preview.History(1.1f).Translation.X, Is.EqualTo(10).Within(.00001f));
        });
        control.Update(new(0, 2.4, 1, 2, true, false));
        Assert.That(control.Factory.Created, Has.Count.EqualTo(1), "A persistent infinite owner does not restart its simulation.");
        Assert.That(preview.History!(1.2f).Translation.X, Is.EqualTo(0).Within(.00001f));
        Assert.That(preview.History(2.4f).Translation.X, Is.EqualTo(0).Within(.00001f));
    }

    [Test]
    public void TailPlayback_EndOfNonLoopingClipContinuesVfxTimeAndPreservesBirthAttachments()
    {
        using var control = new TailPlaybackControl(0, 0);
        control.Node.LocalAttachmentAtTime = seconds => Matrix.CreateTranslation(seconds * 10, 0, 0);
        control.Update(new(1, 1, 1, 0, true, false), Matrix.CreateTranslation(10, 0, 0));
        control.Update(new(1, 2.5, 1, 0, true, false), Matrix.CreateTranslation(10, 0, 0));
        var preview = control.Factory.Created.Single();
        Assert.That(preview.Time, Is.EqualTo(2.5f));
        Assert.That(preview.History!(.4f).Translation.X, Is.EqualTo(4).Within(.000001f),
            "Birth history uses the held clip pose to recover the root, not the later particle age.");
    }

    private sealed class TailPlaybackControl : IDisposable
    {
        public TailPreviewFactory Factory { get; } = new();
        public VfxPreviewDocumentStore LiveDocuments { get; } = new();
        public PackFile File { get; }
        public GroupNode Root { get; } = new("tail-playback");
        public ISceneNode Marker => Root.Children.Single();
        public VfxPreviewSceneNode Node => (VfxPreviewSceneNode)Marker.Children.Single();
        public Effect_v11 Source { get; }
        public EffectMetaInstance Effect { get; }
        public TailPlaybackControl(float start, float end, bool persistent = false, AnimationPlayer? player = null)
        {
            Source = new() { Name = "EFFECT", Version = 11, VfxName = "tail-control", Scale = 1, StartTime = start, EndTime = end, NodeIndex = -1 };
            var file = PackFile.CreateFromBytes("tail-control.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>"));
            File = file;
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.FindFile(@"vfx\tail-control.xml", null)).Returns(file);
            files.Setup(service => service.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\tail-control.xml");
            files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
            var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, Factory, Mock.Of<IDeviceResolver>(), LiveDocuments);
            var document = new ParsedMetadataFile { Version = 2, Attributes = [Source] };
            Effect = (EffectMetaInstance)builder.Build(persistent ? document : null, persistent ? null : document, null,
                Root, null!, player ?? new AnimationPlayer(), null).Instances.Single();
        }
        public void Update(AnimationPlaybackTime time, Matrix? parent = null)
        {
            Effect.Update(time);
            if (Marker.IsVisible) Node.PrepareFrame(parent ?? Marker.ModelMatrix);
        }
        public void Dispose() => Effect.CleanUp();
    }
    private sealed class TailPreviewFactory : IVfxPreviewFactory
    {
        public List<TailPreview> Created { get; } = [];
        public bool Infinite { get; set; }
        public bool ShareResources { get; set; }
        public IVfxPreview Create(GraphicsDevice device)
        {
            var preview = new TailPreview { Infinite = Infinite };
            if (ShareResources) preview.Fork = () => Create(device);
            Created.Add(preview);
            return preview;
        }
    }
    private sealed class TailPreview : IVfxPreview
    {
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => Drained ? 0 : 1;
        public bool Infinite { get; set; }
        public bool HasInfiniteEmitters => Infinite;
        public float CycleDuration => .1f;
        public bool IsDrained => Drained;
        public bool Drained { get; set; }
        public float? EmissionEndTimeSeconds { get; set; }
        public Func<IVfxPreview>? Fork { get; set; }
        public IVfxPreview? CreatePlaybackInstance() => Fork?.Invoke();
        public int ReloadCount { get; private set; }
        public IDisposable? Scene { get; set; }
        public int SceneRequests { get; private set; }
        public IDisposable? BeginScene(CommonShaderParameters parameters) { SceneRequests++; return Scene; }
        public bool Disposed { get; private set; }
        public float Time { get; private set; }
        public Func<float, Matrix>? History { get; private set; }
        public void Reload(VfxPreviewSnapshot snapshot) => ReloadCount++;
        public void Advance(float seconds, Matrix attachment) => Seek(Time + seconds, attachment);
        public void Seek(float seconds, Matrix attachment) => Time = seconds;
        public void Seek(float seconds, Matrix attachment, Func<float, Matrix> history) { Seek(seconds, attachment); History = history; }
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() => Disposed = true;
    }
    private sealed class TailSceneScope : IDisposable
    {
        public void Dispose() { }
    }
}
