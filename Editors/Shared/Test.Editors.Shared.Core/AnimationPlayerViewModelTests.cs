using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.Editors.Shared.Core;

public class AnimationPlayerViewModelTests
{
    [Test]
    public void ToggleAnimationPausePlay_ResumesFromPausedFrame()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        player.SetAnimation(CreateClip(10, 1), skeleton);
        var sceneObject = new SceneObject("main")
        {
            Player = player,
            Skeleton = skeleton,
            Description = "Root",
        };
        var metadataPlayer = new AnimationPlayer();
        var metadataSkeleton = CreateSkeleton(metadataPlayer);
        metadataPlayer.SetAnimation(CreateClip(10, 1), metadataSkeleton);
        sceneObject.MetaDataItems.Add(
            new TestMetaDataInstance(metadataPlayer));
        var viewModel = new AnimationPlayerViewModel();
        viewModel.RegisterAsset(sceneObject);
        viewModel.IsEnabled.Value = true;
        player.CurrentFrame = 5;
        metadataPlayer.CurrentFrame = 7;

        viewModel.ToggleAnimationPausePlay();
        viewModel.ToggleAnimationPausePlay();

        Assert.Multiple(() =>
        {
            Assert.That(player.IsPlaying, Is.True);
            Assert.That(player.CurrentFrame, Is.EqualTo(5));
            Assert.That(metadataPlayer.IsPlaying, Is.True);
            Assert.That(metadataPlayer.CurrentFrame, Is.EqualTo(7));
        });
    }

    [Test]
    public void EnablingPlayer_StartsFromFirstFrame()
    {
        var sceneObject = CreateSceneObject("main", 10, 1);
        var viewModel = new AnimationPlayerViewModel();
        viewModel.RegisterAsset(sceneObject);
        sceneObject.Player.CurrentFrame = 5;

        viewModel.IsEnabled.Value = true;

        Assert.That(sceneObject.Player.CurrentFrame, Is.Zero);
    }

    [Test]
    public void PlaybackPositionSeconds_SeeksRegisteredAndMetadataPlayersWithoutChangingPauseState()
    {
        var first = CreateSceneObject("first", 10, 1);
        var second = CreateSceneObject("second", 20, 2);
        var metadataPlayer = new AnimationPlayer();
        var metadataSkeleton = CreateSkeleton(metadataPlayer);
        metadataPlayer.SetAnimation(CreateClip(10, 1), metadataSkeleton);
        first.MetaDataItems.Add(new TestMetaDataInstance(metadataPlayer));
        var viewModel = new AnimationPlayerViewModel();
        viewModel.RegisterAsset(first);
        viewModel.RegisterAsset(second);
        viewModel.IsEnabled.Value = true;
        viewModel.ToggleAnimationPausePlay();

        viewModel.PlaybackPositionSeconds = 0.4f;

        Assert.Multiple(() =>
        {
            Assert.That(first.Player.GetTimeUs(), Is.EqualTo(400_000));
            Assert.That(second.Player.GetTimeUs(), Is.EqualTo(400_000));
            Assert.That(metadataPlayer.GetTimeUs(), Is.EqualTo(400_000));
            Assert.That(first.Player.IsPlaying, Is.False);
            Assert.That(second.Player.IsPlaying, Is.False);
            Assert.That(metadataPlayer.IsPlaying, Is.False);
        });
    }

    private static SceneObject CreateSceneObject(
        string id,
        int frameCount,
        float seconds)
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        player.SetAnimation(CreateClip(frameCount, seconds), skeleton);
        return new SceneObject(id)
        {
            Player = player,
            Skeleton = skeleton,
            Description = id,
        };
    }

    private static AnimationClip CreateClip(int frameCount, float seconds)
    {
        var clip = new AnimationClip();
        for (var index = 0; index < frameCount; index++)
        {
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame
            {
                Position = [Vector3.Zero],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One],
            });
        }

        clip.PlayTimeInSec = seconds;
        return clip;
    }

    private static GameSkeleton CreateSkeleton(AnimationPlayer player)
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test",
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Name = "root",
                    ParentId = -1,
                },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3(0, 0, 0));
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return new GameSkeleton(file, player);
    }

    private sealed class TestMetaDataInstance(AnimationPlayer player) :
        IMetaDataInstance
    {
        public AnimationPlayer Player { get; } = player;

        public void CleanUp()
        {
        }

        public void Update(float currentTime)
        {
        }
    }
}
