using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class AnimationCoreTimeContractTests
{
    [Test]
    public void Clip_NonIntegerFrameRate_UsesUnifiedTimebase()
    {
        var clip = CreateClip(frameCount: 7, durationSeconds: 0.3f);

        Assert.Multiple(() =>
        {
            Assert.That(clip.Timebase, Is.Not.Null);
            Assert.That(clip.Timebase!.FrameCount, Is.EqualTo(7));
            Assert.That(clip.Timebase.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(300)));
            Assert.That(clip.Timebase.FramesPerSecond, Is.EqualTo(70.0 / 3.0).Within(0.000001));
            Assert.That(clip.Timebase.GetSampleTime(6), Is.LessThan(clip.Timebase.Duration));
        });
    }

    [Test]
    public void Player_AtDuration_UsesTimebaseForLoopAndStopFrames()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = CreateClip(frameCount: 7, durationSeconds: 0.3f);
        player.SetAnimation(clip, skeleton);
        player.Play();

        player.Update(new GameTime(
            TimeSpan.FromSeconds(0.3),
            TimeSpan.FromSeconds(0.3)));

        Assert.Multiple(() =>
        {
            Assert.That(player.FramesPerSecond, Is.EqualTo(70.0 / 3.0).Within(0.000001));
            Assert.That(player.CurrentFrame, Is.Zero);
            Assert.That(player.GetTimeUs(), Is.Zero);
        });

        player.LoopAnimation = false;
        player.Play();
        player.Update(new GameTime(
            TimeSpan.FromSeconds(0.6),
            TimeSpan.FromSeconds(0.3)));

        Assert.Multiple(() =>
        {
            Assert.That(player.CurrentFrame, Is.EqualTo(6));
            Assert.That(player.GetTimeUs(), Is.EqualTo(300_000));
            Assert.That(player.IsPlaying, Is.False);
        });
    }

    [Test]
    public void Player_SetFrameAtNonIntegerRate_RoundTripsFrameIndex()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = CreateClip(frameCount: 7, durationSeconds: 0.3f);
        player.SetAnimation(clip, skeleton);

        player.CurrentFrame = 1;

        Assert.That(player.CurrentFrame, Is.EqualTo(1));
    }

    [Test]
    public void Sampler_NormalizedTime_UsesHalfOpenSamplePosition()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = CreateClip(frameCount: 4, durationSeconds: 1);

        var sampledFrame = AnimationSampler.Sample(0.25f, skeleton, clip);

        Assert.That(
            sampledFrame.BoneTransforms[0].Translation.X,
            Is.EqualTo(1).Within(0.000001f));
    }

    [Test]
    public void Resample_ToSingleFrame_UsesFirstSampleAndRequestedDuration()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = CreateClip(frameCount: 4, durationSeconds: 1);

        var result = global::GameWorld.Core.Animation.AnimationEditor.ReSample(
            skeleton,
            clip,
            newFrameCount: 1,
            playTime: 0.3f);

        Assert.Multiple(() =>
        {
            Assert.That(result.DynamicFrames, Has.Count.EqualTo(1));
            Assert.That(result.DynamicFrames[0].Position[0].X, Is.Zero);
            Assert.That(result.Timebase, Is.Not.Null);
            Assert.That(result.Timebase!.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(300)));
            Assert.That(result.Timebase.FramesPerSecond, Is.EqualTo(10.0 / 3.0).Within(0.000001));
            Assert.That(
                result.Timebase.GetSamplePosition(TimeSpan.FromMilliseconds(150)),
                Is.Zero);
        });
    }

    [Test]
    public void Resample_EmptySource_RemainsEmpty()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = new AnimationClip { PlayTimeInSec = 1 };

        var result = global::GameWorld.Core.Animation.AnimationEditor.ReSample(
            skeleton,
            clip,
            newFrameCount: 4,
            playTime: 0.3f);

        Assert.Multiple(() =>
        {
            Assert.That(result.DynamicFrames, Is.Empty);
            Assert.That(result.PlayTimeInSec, Is.EqualTo(0.3f));
            Assert.That(result.Timebase, Is.Null);
        });
    }

    [Test]
    public void Resample_UsesHalfOpenTargetSampleTimes()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = CreateClip(frameCount: 4, durationSeconds: 1);

        var result = global::GameWorld.Core.Animation.AnimationEditor.ReSample(
            skeleton,
            clip,
            newFrameCount: 4,
            playTime: 1);

        Assert.That(
            result.DynamicFrames.Select(frame => frame.Position[0].X),
            Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void AnimationFile_RoundTrip_PreservesUnifiedTimebase()
    {
        var player = new AnimationPlayer();
        var skeleton = CreateSkeleton(player);
        var clip = CreateClip(frameCount: 7, durationSeconds: 0.3f);

        var animationFile = clip.ConvertToFileFormat(skeleton);
        var bytes = AnimationFile.ConvertToBytes(animationFile);
        var reloadedFile = AnimationFile.Create(new ByteChunk(bytes));
        var reloadedClip = new AnimationClip(reloadedFile, skeleton);

        Assert.Multiple(() =>
        {
            Assert.That(animationFile.Header.FrameRate, Is.EqualTo(70.0 / 3.0).Within(0.000001));
            Assert.That(reloadedFile.Header.FrameRate, Is.EqualTo(animationFile.Header.FrameRate));
            Assert.That(reloadedClip.Timebase, Is.Not.Null);
            Assert.That(reloadedClip.Timebase!.FrameCount, Is.EqualTo(clip.Timebase!.FrameCount));
            Assert.That(reloadedClip.Timebase.Duration, Is.EqualTo(clip.Timebase.Duration));
            Assert.That(reloadedClip.Timebase.FramesPerSecond, Is.EqualTo(clip.Timebase.FramesPerSecond).Within(0.000001));
            Assert.That(
                Enumerable.Range(0, clip.Timebase.FrameCount)
                    .Select(clip.Timebase.GetSampleTime),
                Is.EqualTo(
                    Enumerable.Range(0, reloadedClip.Timebase.FrameCount)
                        .Select(reloadedClip.Timebase.GetSampleTime)));
        });
    }

    private static AnimationClip CreateClip(int frameCount, float durationSeconds)
    {
        var clip = new AnimationClip();
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame
            {
                Position = [new Vector3(frameIndex, 0, 0)],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One],
            });
        }

        clip.PlayTimeInSec = durationSeconds;
        return clip;
    }

    private static GameSkeleton CreateSkeleton(AnimationPlayer player)
    {
        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "test" },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = -1,
                },
            ],
        };

        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));

        var part = new AnimationFile.AnimationPart();
        part.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(0));
        part.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(0));
        part.DynamicFrames.Add(frame);
        animation.AnimationParts.Add(part);

        return GameSkeleton.CreateFromAnimationFile(animation, player);
    }
}
