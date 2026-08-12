using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Animation;

public class AnimationPlayerPlaybackEventTests
{
    [Test]
    public void Seek_RaisesPositionChangedWithoutCompletingPlayback()
    {
        var player = CreatePlayer();
        var positionChanges = 0;
        var completions = 0;
        player.OnPlaybackPositionChanged += () => positionChanges++;
        player.OnPlaybackCompleted += () => completions++;

        player.SeekToTimeSeconds(1);

        Assert.Multiple(() =>
        {
            Assert.That(positionChanges, Is.EqualTo(1));
            Assert.That(completions, Is.Zero);
        });
    }

    [Test]
    public void NaturalPlaybackEnd_RaisesCompletedAfterRefreshingFinalFrame()
    {
        var player = CreatePlayer();
        AnimationFrame completedFrame = null;
        player.OnPlaybackCompleted += () => completedFrame = player.GetCurrentAnimationFrame();

        player.Update(new GameTime(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1001)));

        Assert.Multiple(() =>
        {
            Assert.That(player.IsPlaying, Is.False);
            Assert.That(player.CurrentFrame, Is.EqualTo(player.FrameCount() - 1));
            Assert.That(completedFrame, Is.Not.Null);
        });
    }

    [Test]
    public void NaturalPlaybackEnd_WhenAnimationIsCleared_DoesNotRaiseCompleted()
    {
        var player = CreatePlayer();
        var completions = 0;
        player.OnFrameChanged += _ => player.SetAnimation(null, null);
        player.OnPlaybackCompleted += () => completions++;

        player.Update(new GameTime(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1001)));

        Assert.That(completions, Is.Zero);
    }

    private static AnimationPlayer CreatePlayer()
    {
        var player = new AnimationPlayer
        {
            IsEnabled = true,
            LoopAnimation = false
        };
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test_skeleton"
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Name = "root",
                    ParentId = -1
                }
            ]
        };
        var skeletonFrame = new AnimationFile.Frame();
        skeletonFrame.Transforms.Add(new RmvVector3(0, 0, 0));
        skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var skeletonPart = new AnimationFile.AnimationPart();
        skeletonPart.DynamicFrames.Add(skeletonFrame);
        skeletonFile.AnimationParts.Add(skeletonPart);
        var skeleton = new GameSkeleton(skeletonFile, player);
        var clip = new AnimationClip { PlayTimeInSec = 1 };
        clip.DynamicFrames.Add(new AnimationClip.KeyFrame
        {
            Position = [Vector3.Zero],
            Rotation = [Quaternion.Identity],
            Scale = [Vector3.One]
        });
        clip.DynamicFrames.Add(new AnimationClip.KeyFrame
        {
            Position = [Vector3.One],
            Rotation = [Quaternion.Identity],
            Scale = [Vector3.One]
        });
        player.SetAnimation(clip, skeleton);
        player.Play();
        return player;
    }
}
