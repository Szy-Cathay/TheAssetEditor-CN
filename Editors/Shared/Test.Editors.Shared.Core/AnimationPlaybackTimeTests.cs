using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.Editors.Shared.Core;

public class AnimationPlaybackTimeTests
{
    [Test]
    public void ActualViewModelLoop_KeepsElapsedAndRevisionAndStartsANewCycle()
    {
        var (scene, model) = Create();
        var revision = scene.Player.PlaybackTime.Revision;
        Tick(scene.Player, .75); Tick(scene.Player, .5);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Player.LoopAnimation, Is.False, "The real view model owns the restart.");
            Assert.That(scene.Player.PlaybackTime.ClipTimeSeconds, Is.Zero);
            Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.EqualTo(1.25).Within(.000001));
            Assert.That(scene.Player.PlaybackTime.CycleIndex, Is.EqualTo(1));
            Assert.That(scene.Player.PlaybackTime.Revision, Is.EqualTo(revision));
            Assert.That(model.IsPlaying.Value, Is.True);
        });
    }

    [Test]
    public void NonLoopingEnd_HoldsModelPoseWhileTheOptedInClockContinues()
    {
        var (scene, model) = Create(); model.LoopAnimation.Value = false;
        Tick(scene.Player, 1);
        var finalPose = scene.Player.GetCurrentAnimationFrame().BoneTransforms[0].WorldTransform;
        var revision = scene.Player.PlaybackTime.Revision;
        Tick(scene.Player, .5);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Player.IsPlaying, Is.False);
            Assert.That(scene.Player.PlaybackTime.ClipTimeSeconds, Is.EqualTo(1));
            Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.EqualTo(1.5).Within(.000001));
            Assert.That(scene.Player.PlaybackTime.Revision, Is.EqualTo(revision));
            Assert.That(scene.Player.PlaybackTime.CycleIndex, Is.Zero);
            Assert.That(scene.Player.GetCurrentAnimationFrame().BoneTransforms[0].WorldTransform, Is.EqualTo(finalPose));
            Assert.That(model.IsPlaying.Value, Is.True, "The existing play/pause button can pause a draining tail.");
        });
    }

    [Test]
    public void ActualViewModelLoop_DeduplicatesMetadataSharingTheRootPlayer()
    {
        var (scene, _) = Create();
        scene.MetaDataItems.Add(new SharedPlayerMetadata(scene.Player));
        Tick(scene.Player, 1);
        Assert.That(scene.Player.PlaybackTime.CycleIndex, Is.EqualTo(1), "A shared player is one transport, even when metadata exposes it too.");
    }

    [Test]
    public void UserPauseAndResume_AfterTheClipEndFreezeAndResumeTheTailClock()
    {
        var (scene, model) = Create(); model.LoopAnimation.Value = false;
        Tick(scene.Player, 1); Tick(scene.Player, .5);
        var revision = scene.Player.PlaybackTime.Revision;
        model.ToggleAnimationPausePlay(); Tick(scene.Player, 1);
        Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.EqualTo(1.5).Within(.000001));
        Assert.That(scene.Player.PlaybackTime.IsRunning, Is.False);
        model.ToggleAnimationPausePlay(); Tick(scene.Player, .2);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.EqualTo(1.7).Within(.000001));
            Assert.That(scene.Player.PlaybackTime.Revision, Is.EqualTo(revision));
            Assert.That(scene.Player.PlaybackTime.IsRunning, Is.True);
        });
    }

    [Test]
    public void ExplicitSeek_AfterALoopStartsANewRevisionAtTheRequestedClipTime()
    {
        var (scene, model) = Create(); Tick(scene.Player, 1.25);
        var before = scene.Player.PlaybackTime;
        model.PlaybackPositionSeconds = .25f;
        var after = scene.Player.PlaybackTime;
        Assert.Multiple(() =>
        {
            Assert.That(after.Revision, Is.GreaterThan(before.Revision));
            Assert.That(after.CycleIndex, Is.Zero);
            Assert.That(after.ElapsedSeconds, Is.EqualTo(.25).Within(.000001));
            Assert.That(after.ClipTimeSeconds, Is.EqualTo(.25));
            Assert.That(after.IsRunning, Is.True);
        });
        model.PlaybackPositionSeconds = .25f;
        Assert.That(scene.Player.PlaybackTime.Revision, Is.GreaterThan(after.Revision), "An explicit same-time seek is still a replay request.");
    }

    [Test]
    public void RealViewModelStopAndStart_AreDifferentFromANaturalLoop()
    {
        var (scene, model) = Create(); Tick(scene.Player, .6);
        var before = scene.Player.PlaybackTime;
        model.IsEnabled.Value = false; Tick(scene.Player, .4);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Player.PlaybackTime.Revision, Is.GreaterThan(before.Revision));
            Assert.That(scene.Player.PlaybackTime.IsStopped, Is.True);
            Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.Zero);
        });
        var stopped = scene.Player.PlaybackTime.Revision;
        model.IsEnabled.Value = true;
        Assert.Multiple(() =>
        {
            Assert.That(scene.Player.PlaybackTime.Revision, Is.GreaterThan(stopped));
            Assert.That(scene.Player.PlaybackTime.ClipTimeSeconds, Is.Zero);
            Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.Zero);
            Assert.That(scene.Player.PlaybackTime.CycleIndex, Is.Zero);
        });
    }

    [Test]
    public void SceneObject_OnlySendsExtendedTimeToOptedInMetadata()
    {
        var (scene, model) = Create(); model.LoopAnimation.Value = false;
        var aware = new PlaybackMetadata(); var ordinary = new OrdinaryMetadata();
        scene.MetaDataItems.Add(aware); scene.MetaDataItems.Add(ordinary);
        Tick(scene.Player, 1); Tick(scene.Player, .5); scene.Update(new GameTime());
        Assert.Multiple(() =>
        {
            Assert.That(ordinary.Time, Is.EqualTo(1));
            Assert.That(aware.Time.ElapsedSeconds, Is.EqualTo(1.5).Within(.000001));
            Assert.That(aware.LegacyCalls, Is.Zero);
        });
    }

    [Test]
    public void OrdinaryPlayer_WithoutTailOptInKeepsItsExistingEndBehaviour()
    {
        var (scene, model) = Create(false); model.LoopAnimation.Value = false;
        Tick(scene.Player, 1); Tick(scene.Player, .5);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Player.CurrentTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(scene.Player.PlaybackTime.ElapsedSeconds, Is.EqualTo(1).Within(.000001));
            Assert.That(scene.Player.PlaybackTime.IsRunning, Is.False);
            Assert.That(model.IsPlaying.Value, Is.False);
        });
    }

    private static void Tick(AnimationPlayer player, double seconds) => player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));

    private static (SceneObject Scene, AnimationPlayerViewModel Model) Create(bool tail = true)
    {
        var player = new AnimationPlayer { ContinuePlaybackTimeAfterAnimationEnd = tail };
        var file = new AnimationFile { Header = new() { SkeletonName = "clock" }, Bones = [new() { Name = "root", ParentId = -1 }] };
        var frame = new AnimationFile.Frame(); frame.Transforms.Add(new RmvVector3(0, 0, 0)); frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart(); part.DynamicFrames.Add(frame); file.AnimationParts.Add(part);
        var skeleton = new GameSkeleton(file, player);
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        for (var i = 0; i < 10; i++) clip.DynamicFrames.Add(new() { Position = [new(i, 0, 0)], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
        player.SetAnimation(clip, skeleton);
        var scene = new SceneObject("clock") { Player = player, Skeleton = skeleton, Description = "clock", ParentNode = new GroupNode() };
        var model = new AnimationPlayerViewModel(); model.RegisterAsset(scene); model.IsEnabled.Value = true;
        return (scene, model);
    }

    private sealed class OrdinaryMetadata : IMetaDataInstance
    {
        public AnimationPlayer Player { get; } = new();
        public float Time;
        public void Update(float currentTime) => Time = currentTime;
        public void CleanUp() { }
    }

    private sealed class PlaybackMetadata : IMetaDataInstance, IMetaDataPlaybackInstance
    {
        public AnimationPlayer Player { get; } = new();
        public AnimationPlaybackTime Time;
        public int LegacyCalls;
        public void Update(float currentTime) => LegacyCalls++;
        public void Update(AnimationPlaybackTime playbackTime) => Time = playbackTime;
        public void CleanUp() { }
    }

    private sealed class SharedPlayerMetadata(AnimationPlayer player) : IMetaDataInstance
    {
        public AnimationPlayer Player => player;
        public void Update(float currentTime) { }
        public void CleanUp() { }
    }
}
