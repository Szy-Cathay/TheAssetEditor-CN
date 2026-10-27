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
    public void PersistentNonzeroPoint_InfiniteRuntimeWaitsForFirstTriggerThenSurvivesLoopStarts()
    {
        using var control = new NonzeroPointControl(true, true);
        control.Update(new(.5f, .5, 1, 0, true, false));
        Assert.That(control.Created, Is.Empty, "Capability discovery must not start the effect before its first trigger.");
        Assert.That(control.Node.CurrentPlaybackEnabled, Is.False);
        control.Update(new(1.5f, 1.5, 1, 0, true, false));
        var original = control.Created.Single();
        control.Update(new(0, 3.2, 1, 1, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(control.Marker.IsVisible, Is.True);
            Assert.That(control.Node.CurrentPlaybackEnabled, Is.True);
            Assert.That(control.Created, Has.Count.EqualTo(1));
            Assert.That(control.Created.Single(), Is.SameAs(original));
            Assert.That(original.ReloadCalls, Is.EqualTo(1));
            Assert.That(original.Disposed, Is.False);
            Assert.That(original.Time, Is.EqualTo(2.2f).Within(.000001f));
            Assert.That(original.EmissionEndTimeSeconds, Is.Null);
        });
        control.Update(new(.4f, 3.6, 1, 1, false, false));
        control.Update(new(.4f, 3.6, 1, 1, false, false));
        Assert.That(original.Time, Is.EqualTo(2.6f).Within(.000001f));
        control.Update(new(0, 6.4, 1, 2, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(control.Node.CurrentPlaybackEnabled, Is.True);
            Assert.That(control.Created, Has.Count.EqualTo(1));
            Assert.That(original.ReloadCalls, Is.EqualTo(1));
            Assert.That(original.Time, Is.EqualTo(5.4f).Within(.000001f));
            Assert.That(control.Source.StartTime, Is.EqualTo(1));
            Assert.That(control.Source.EndTime, Is.EqualTo(1));
            Assert.That(control.Effect.ShowForEntireAnimation, Is.False);
        });
    }

    [Test]
    public void PersistentNonzeroPoint_SeekAndStopRequireANewFirstTrigger()
    {
        using var control = new NonzeroPointControl(true, true);
        control.Update(new(1.5f, 1.5, 1, 0, true, false));
        var first = control.Created.Single();
        control.Update(new(0, 3.2, 1, 1, true, false));
        control.Update(new(.25f, .25, 2, 0, false, false));
        control.Update(new(.75f, .75, 2, 0, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(first.Disposed, Is.True);
            Assert.That(control.Created, Has.Count.EqualTo(1));
            Assert.That(control.Node.CurrentPlaybackEnabled, Is.False);
            Assert.That(control.Marker.IsVisible, Is.False);
        });
        control.Update(new(1.2f, 1.2, 2, 0, true, false));
        var second = control.Created.Last();
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Time, Is.EqualTo(.2f).Within(.000001f));
        control.Update(new(1.2f, 0, 3, 0, false, true));
        control.Update(new(.2f, .2, 3, 0, true, false));
        control.Update(new(.9f, .9, 3, 0, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(second.Disposed, Is.True);
            Assert.That(control.Created, Has.Count.EqualTo(2));
            Assert.That(control.Node.CurrentPlaybackEnabled, Is.False);
            Assert.That(control.Marker.IsVisible, Is.False);
        });
        control.Update(new(1.1f, 1.1, 3, 0, true, false));
        Assert.That(control.Created, Has.Count.EqualTo(3));
        Assert.That(control.Created.Last().ReloadCalls, Is.EqualTo(1));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void NonzeroPoint_FinitePersistentAndAnimationOwnersStillWaitInEachNewCycle(bool persistent, bool infinite)
    {
        using var control = new NonzeroPointControl(persistent, infinite);
        control.Update(new(1.5f, 1.5, 1, 0, true, false));
        var first = control.Created.Single();
        control.Update(new(0, 3.2, 1, 1, true, false));
        control.Update(new(.9f, 4.1, 1, 1, true, false));
        Assert.Multiple(() =>
        {
            Assert.That(control.Node.CurrentPlaybackEnabled, Is.False);
            Assert.That(control.Created, Has.Count.EqualTo(1), "The prior tail may remain; this cycle's new runtime must still wait for start=1.");
            Assert.That(first.Disposed, Is.False);
            Assert.That(first.ReloadCalls, Is.EqualTo(1));
            Assert.That(first.EmissionEndTimeSeconds, Is.EqualTo(2));
        });
        control.Update(new(1.1f, 4.3, 1, 1, true, false));
        Assert.That(control.Node.CurrentPlaybackEnabled, Is.True);
        Assert.That(control.Created, Has.Count.EqualTo(2));
    }

    private sealed class NonzeroPointControl : IVfxPreviewFactory, IDisposable
    {
        private readonly bool _infinite;
        private readonly GroupNode _root = new("nonzero-persistent-point");
        public List<NonzeroPointPreview> Created { get; } = [];
        public Effect_v11 Source { get; }
        public EffectMetaInstance Effect { get; }
        public ISceneNode Marker => _root.Children.Single();
        public VfxPreviewSceneNode Node => (VfxPreviewSceneNode)Marker.Children.Single();
        public NonzeroPointControl(bool persistent, bool infinite)
        {
            _infinite = infinite;
            var player = new AnimationPlayer();
            var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(3) };
            for (var i = 0; i < 2; i++) clip.DynamicFrames.Add(new() { Position = [Vector3.Zero], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
            player.SetAnimation(clip, Skeleton(player));
            Source = new() { Name = "EFFECT", Version = 11, VfxName = "nonzero-point-control", Scale = 1, StartTime = 1, EndTime = 1, NodeIndex = -1 };
            var file = PackFile.CreateFromBytes("nonzero-point-control.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>"));
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.FindFile(@"vfx\nonzero-point-control.xml", null)).Returns(file);
            files.Setup(service => service.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\nonzero-point-control.xml");
            files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
            var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, this, Mock.Of<IDeviceResolver>(), new());
            var document = new ParsedMetadataFile { Version = 2, Attributes = [Source] };
            Effect = (EffectMetaInstance)builder.Build(persistent ? document : null, persistent ? null : document, null, _root, null!, player, null).Instances.Single();
        }
        public IVfxPreview Create(GraphicsDevice device)
        {
            var preview = new NonzeroPointPreview(_infinite); Created.Add(preview); return preview;
        }
        public void Update(AnimationPlaybackTime time)
        {
            Effect.Update(time);
            if (Marker.IsVisible) Node.PrepareFrame(Marker.ModelMatrix);
        }
        public void Dispose() => Effect.CleanUp();
    }

    private sealed class NonzeroPointPreview(bool infinite) : IVfxPreview
    {
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => Disposed ? 0 : 1;
        public bool HasInfiniteEmitters => infinite;
        public bool IsDrained => false;
        public float CycleDuration => .1f;
        public float? EmissionEndTimeSeconds { get; set; }
        public int ReloadCalls { get; private set; }
        public bool Disposed { get; private set; }
        public float Time { get; private set; }
        public void Reload(VfxPreviewSnapshot snapshot) => ReloadCalls++;
        public void Advance(float seconds, Matrix attachment) => Seek(Time + seconds, attachment);
        public void Seek(float seconds, Matrix attachment) => Time = seconds;
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() => Disposed = true;
    }
}
