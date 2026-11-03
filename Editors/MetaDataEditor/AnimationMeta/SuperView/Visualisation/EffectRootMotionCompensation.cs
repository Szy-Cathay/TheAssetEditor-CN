using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation;

internal static class EffectRootMotionCompensation
{
    private const float AngleToRadians = 0.0000958738019107841f;
    private const float RadiansToAngle = 10430.3779296875f;

    // The ordinary native loader passes empty root names and retains its default bone index zero.
    internal static bool TryCreate(GameSkeleton? skeleton, AnimationClip? clip, float startTime, bool enabled, out Matrix correction)
    {
        correction = Matrix.Identity;
        if (!enabled || clip == null || clip.DynamicFrames.Count == 0
            || clip.Duration <= TimeSpan.Zero || !float.IsFinite(startTime))
            return false;

        var referenceRotation = skeleton is { BoneCount: > 0 } ? skeleton.Rotation[0] : clip.SourceRootReferenceRotation;
        if (!referenceRotation.HasValue) return false;
        var rootName = skeleton is { BoneCount: > 0 } ? skeleton.BoneNames[0] : clip.SourceRootName;

        var frameCount = clip.DynamicFrames.Count;
        var nativeTiming = clip.SourceFrameRate is > 0 && clip.SourceFrameCount == frameCount && clip.SourceDuration > TimeSpan.Zero;
        var fps = nativeTiming
            ? clip.SourceFrameRate!.Value * (float)(clip.SourceDuration.TotalSeconds / clip.Duration.TotalSeconds)
            : (float)clip.Timebase!.FramesPerSecond;
        var sample = startTime * fps;
        if (!float.IsFinite(sample) || sample < int.MinValue || sample >= int.MaxValue)
            return false;
        var current = (int)sample;
        var next = current + 1;
        var fraction = sample - current;
        if ((uint)current >= (uint)(frameCount - 1))
        {
            current = next = frameCount - 1;
            fraction = 0;
        }
        else if (nativeTiming && next == frameCount - 1)
        {
            var lastInterval = (float)clip.Duration.TotalSeconds * fps - current;
            if (lastInterval > 0) fraction /= lastInterval;
        }

        var first = clip.DynamicFrames[current];
        var second = clip.DynamicFrames[next];
        if (first.Position.Count == 0 || first.Rotation.Count == 0 || second.Position.Count == 0 || second.Rotation.Count == 0)
            return false;

        // Cache each key's raw root heading before interpolation, including native 16-bit rounding.
        var reference = Matrix.Transpose(Matrix.CreateFromQuaternion(referenceRotation.Value));
        var forwardOnly = string.Equals(rootName, "animroot", StringComparison.Ordinal);
        var firstHeading = Heading(reference * Matrix.CreateFromQuaternion(first.Rotation[0]), forwardOnly);
        var secondHeading = Heading(reference * Matrix.CreateFromQuaternion(second.Rotation[0]), forwardOnly);
        var heading = unchecked((ushort)(firstHeading + NativeRound(SignedAngle(unchecked((ushort)(secondHeading - firstHeading))) * fraction)));
        var position = Vector3.Lerp(first.Position[0], second.Position[0], fraction);
        correction = Matrix.CreateTranslation(-position) * Matrix.CreateRotationY(-SignedAngle(heading) * AngleToRadians);
        return true;
    }

    // Native 0x180bdcc blends the forward and alternate heading near a vertical root.
    internal static ushort Heading(Matrix rotation, bool forwardOnly = false)
    {
        var forward = NativeRound(MathF.Atan2(rotation.M31, rotation.M33) * RadiansToAngle);
        if (forwardOnly) return unchecked((ushort)forward);
        var alternate = NativeRound(MathF.Atan2(rotation.M21, rotation.M23) * RadiansToAngle);
        if (rotation.M32 >= 0) alternate -= 32768;
        var weight = Math.Clamp((MathF.Abs(rotation.M32) - 0.4999999701976776f) * 2.062673330307007f, 0, 1);
        return unchecked((ushort)(forward + NativeRound(SignedAngle(unchecked((ushort)(alternate - forward))) * weight)));
    }

    private static int NativeRound(float value) => (int)(value > 0 ? value + .5f : value - .5f);
    private static int SignedAngle(ushort value) => value > 32768 ? value - 65536 : value;
}
