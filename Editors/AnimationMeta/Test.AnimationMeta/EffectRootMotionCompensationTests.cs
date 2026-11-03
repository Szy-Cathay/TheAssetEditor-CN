using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta;

public class EffectRootMotionCompensationTests
{
    [Test]
    public void NativeSourceFrameRateAndClone_AvoidTheFrameCountOverDurationDrift()
    {
        var skeleton = Skeleton();
        var clip = Clip(skeleton, .1f, 20, [Vector3.Zero, new(10, 0, 0), new(20, 0, 0)]);
        var copy = clip.Clone();
        Assert.Multiple(() =>
        {
            Assert.That((copy.SourceFrameRate, copy.SourceDuration, copy.SourceFrameCount), Is.EqualTo(((float?)20f, clip.Duration, 3)));
            Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, copy, .025f, true, out var correction), Is.True);
            AssertVector(correction.Translation, new(-5, 0, 0));
        });
        copy.Duration *= 2;
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, copy, .05f, true, out var retimed), Is.True);
        AssertVector(retimed.Translation, new(-5, 0, 0));
    }

    [Test]
    public void NativePartialLastInterval_ReachesTheFinalKeyAtTheSourceDuration()
    {
        var skeleton = Skeleton();
        var clip = Clip(skeleton, .075f, 20, [Vector3.Zero, new(10, 0, 0), new(20, 0, 0)]);
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, .0625f, true, out var halfway), Is.True);
        AssertVector(halfway.Translation, new(-15, 0, 0));
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, .075f, true, out var end), Is.True);
        AssertVector(end.Translation, new(-20, 0, 0));
    }

    [TestCase(-.01f, 2f)]
    [TestCase(-.1f, -20f)]
    [TestCase(1f, -20f)]
    public void NativeFrameBounds_PreserveTruncationAndUnsignedLastFrameSelection(float start, float expectedX)
    {
        var skeleton = Skeleton();
        var clip = Clip(skeleton, .1f, 20, [Vector3.Zero, new(10, 0, 0), new(20, 0, 0)]);
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, start, true, out var result), Is.True);
        AssertVector(result.Translation, new(expectedX, 0, 0));
    }

    [TestCase(0, 180, -90)]
    [TestCase(170, -170, -180)]
    public void HeadingInterpolatesQuantizedKeysAcrossTheWrappedShortestArcAndPositiveHalfTurnTie(float first, float second, float expected)
    {
        var skeleton = Skeleton();
        var rotations = new[] { Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(first), 0, 0), Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(second), 0, 0) };
        var clip = Clip(skeleton, .05f, 20, [Vector3.Zero, Vector3.Zero], rotations);
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, .025f, true, out var result), Is.True);
        AssertVector(result.Right, Matrix.CreateRotationY(MathHelper.ToRadians(expected)).Right, .0002f);
        AssertVector(result.Backward, Matrix.CreateRotationY(MathHelper.ToRadians(expected)).Backward, .0002f);
    }

    [Test]
    public void RootReferenceChangesHeadingWithoutSubtractingTheBindPosition()
    {
        var reference = Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0);
        var skeleton = Skeleton(new(0, 0, -.85f), reference);
        var clip = Clip(skeleton, .05f, 20, [new(3, 2, 4), new(3, 2, 4)], [reference, reference]);
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, .01f, true, out var result), Is.True);
        AssertVector(result.Translation, new(-3, -2, -4));
        AssertVector(result.Right, Vector3.Right);
    }

    [Test]
    public void RootCompensationRunsBetweenTheAuthoredOffsetAndTheActorTransform()
    {
        var skeleton = Skeleton();
        var yaw = Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0, 0);
        var clip = Clip(skeleton, .05f, 20, [new(3, 2, 4), new(3, 2, 4)], [yaw, yaw]);
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, .01f, true, out var correction), Is.True);
        var world = Matrix.CreateTranslation(1, 0, 0) * correction
            * Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(10, 0, 0);
        AssertVector(world.Translation, new(8, -2, -4), .0002f);
    }

    [Test]
    public void TheDefaultNativeRootIndexIsZeroEvenWhenAnotherBoneHasNoParent()
    {
        var skeleton = Skeleton(multipleRoots: true);
        var clip = Clip(skeleton, .05f, 20, [new(3, 0, 0), new(3, 0, 0)]);
        Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, .01f, true, out var result), Is.True);
        AssertVector(result.Translation, new(-3, 0, 0));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AnimrootFlagSelectsTheNativeForwardHeadingBeforeTheVerticalBlend(bool forwardOnly)
    {
        var rotation = new Matrix(1, 0, 0, 0, 1, 1, 0, 0, 0, .6f, 1, 0, 0, 0, 0, 1);
        var actual = EffectRootMotionCompensation.Heading(rotation, forwardOnly);
        // DIAG matrix: forward=0, alternate=+16384 minus32768; blend step=-3379.
        Assert.That(actual, Is.EqualTo(forwardOnly ? (ushort)0 : unchecked((ushort)-3379)));
    }

    [Test]
    public void NoAnimationOrDisabledCompensation_ReturnsIdentityWithoutReadingAnyFrames()
    {
        var skeleton = Skeleton();
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        var valid = Clip(skeleton, .05f, 20, [new(3, 0, 0), new(3, 0, 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, null, 0, true, out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(Matrix.Identity));
            Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, clip, 0, true, out var empty), Is.False);
            Assert.That(empty, Is.EqualTo(Matrix.Identity));
            Assert.That(EffectRootMotionCompensation.TryCreate(null, clip, 0, false, out var disabled), Is.False);
            Assert.That(disabled, Is.EqualTo(Matrix.Identity));
            Assert.That(EffectRootMotionCompensation.TryCreate(skeleton, valid, 0, false, out var disabledValid), Is.False);
            Assert.That(disabledValid, Is.EqualTo(Matrix.Identity));
        });
    }

    private static GameSkeleton Skeleton(Vector3 position = default, Quaternion? rotation = null, bool multipleRoots = false)
    {
        var q = rotation ?? Quaternion.Identity;
        var file = new AnimationFile { Header = new() { SkeletonName = "diagnostic" }, Bones = [new() { Name = "animroot", Id = 0, ParentId = -1 }] };
        var part = new AnimationFile.AnimationPart();
        part.DynamicFrames.Add(new() { Transforms = [new(position)], Quaternion = [new(q.X, q.Y, q.Z, q.W)] });
        if (multipleRoots)
        {
            file.Bones = [file.Bones[0], new() { Name = "another_root", Id = 1, ParentId = -1 }];
            part.DynamicFrames[0].Transforms.Add(new(new Vector3(900, 0, 0)));
            part.DynamicFrames[0].Quaternion.Add(new(0, 0, 0, 1));
        }
        file.AnimationParts.Add(part);
        return new(file, new AnimationPlayer());
    }

    private static AnimationClip Clip(GameSkeleton skeleton, float duration, float fps, Vector3[] positions, Quaternion[]? rotations = null)
    {
        var file = new AnimationFile { Header = new() { SkeletonName = "diagnostic", FrameRate = fps, AnimationTotalPlayTimeInSec = duration },
            Bones = [new() { Name = "animroot", Id = 0, ParentId = -1 }] };
        var part = new AnimationFile.AnimationPart { TranslationMappings = [new(0)], RotationMappings = [new(0)] };
        for (var index = 0; index < positions.Length; index++)
        {
            var q = rotations?[index] ?? Quaternion.Identity;
            part.DynamicFrames.Add(new() { Transforms = [new(positions[index])], Quaternion = [new(q.X, q.Y, q.Z, q.W)] });
        }
        file.AnimationParts.Add(part);
        return new(file, skeleton);
    }

    private static void AssertVector(Vector3 actual, Vector3 expected, float tolerance = .00005f) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance), $"Expected {expected}, got {actual}.");
}
