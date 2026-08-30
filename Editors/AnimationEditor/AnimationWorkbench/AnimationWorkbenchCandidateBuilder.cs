using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.GameFormats.Animation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

internal sealed record AnimationWorkbenchCandidateBuildResult(
    bool Succeeded,
    byte[]? Bytes,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics)
{
    public static AnimationWorkbenchCandidateBuildResult Success(
        byte[] bytes) => new(
            true,
            bytes,
            Array.Empty<AnimationWorkbenchDiagnostic>());

    public static AnimationWorkbenchCandidateBuildResult Failure(
        IReadOnlyList<AnimationWorkbenchDiagnostic> diagnostics) => new(
            false,
            null,
            diagnostics.ToArray());

    public static AnimationWorkbenchCandidateBuildResult Failure(
        AnimationWorkbenchDiagnosticCode code) => Failure(
            [
                new AnimationWorkbenchDiagnostic(
                    code,
                    AnimationWorkbenchDiagnosticSeverity.Error),
            ]);
}

internal static class AnimationWorkbenchCandidateBuilder
{
    private const float VectorTolerance = 0.0001f;
    private const float QuaternionDotTolerance = 0.9999f;
    private static readonly TimeSpan s_durationTolerance =
        TimeSpan.FromTicks(100);

    public static AnimationWorkbenchCandidateBuildResult Build(
        AnimationClip result,
        GameSkeleton targetSkeleton)
    {
        if (!HasCompleteFrames(result, targetSkeleton.BoneCount))
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode
                    .ResultTargetSkeletonBoneCountMismatch);
        }

        AnimationFile candidate;
        byte[] bytes;
        AnimationFile roundTripFile;
        try
        {
            candidate = result.ConvertToFileFormat(targetSkeleton);
            bytes = AnimationFile.ConvertToBytes(candidate);
            roundTripFile = AnimationFile.Create(new ByteChunk(bytes));
        }
        catch (Exception)
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode.CandidateSerializationFailed);
        }

        if (!HasEquivalentFileStructure(candidate, roundTripFile))
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode.CandidateRoundTripMismatch);
        }

        AnimationClip roundTripClip;
        try
        {
            roundTripClip = new AnimationClip(
                roundTripFile,
                targetSkeleton);
        }
        catch (Exception)
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode.CandidateRoundTripMismatch);
        }

        if (!HasEquivalentPose(result, roundTripClip))
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode.CandidateRoundTripMismatch);
        }

        return AnimationWorkbenchCandidateBuildResult.Success(bytes);
    }

    private static bool HasCompleteFrames(
        AnimationClip animation,
        int targetBoneCount)
    {
        if (animation.DynamicFrames.Count == 0 ||
            animation.Duration <= TimeSpan.Zero)
        {
            return false;
        }

        foreach (var frame in animation.DynamicFrames)
        {
            try
            {
                if (frame.GetBoneCountFromFrame() != targetBoneCount)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasEquivalentFileStructure(
        AnimationFile expected,
        AnimationFile actual)
    {
        if (expected.Header.Version != 7 ||
            actual.Header.Version != expected.Header.Version ||
            actual.Header.SkeletonName != expected.Header.SkeletonName ||
            MathF.Abs(actual.Header.FrameRate - expected.Header.FrameRate) >
                VectorTolerance ||
            MathF.Abs(
                actual.Header.AnimationTotalPlayTimeInSec -
                expected.Header.AnimationTotalPlayTimeInSec) >
                VectorTolerance ||
            !actual.Header.FlagVariables.SequenceEqual(
                expected.Header.FlagVariables) ||
            expected.AnimationParts.Count != 1 ||
            actual.AnimationParts.Count != expected.AnimationParts.Count ||
            actual.Bones.Length != expected.Bones.Length)
        {
            return false;
        }

        for (var boneIndex = 0; boneIndex < expected.Bones.Length; boneIndex++)
        {
            var expectedBone = expected.Bones[boneIndex];
            var actualBone = actual.Bones[boneIndex];
            if (actualBone.Id != expectedBone.Id ||
                actualBone.Name != expectedBone.Name ||
                actualBone.ParentId != expectedBone.ParentId)
            {
                return false;
            }
        }

        var expectedPart = expected.AnimationParts[0];
        var actualPart = actual.AnimationParts[0];
        if (!HasEquivalentStaticFrame(
                expectedPart.StaticFrame,
                actualPart.StaticFrame) ||
            actualPart.DynamicFrames.Count !=
                expectedPart.DynamicFrames.Count ||
            !HasEquivalentMappings(
                expectedPart.TranslationMappings,
                actualPart.TranslationMappings) ||
            !HasEquivalentMappings(
                expectedPart.RotationMappings,
                actualPart.RotationMappings))
        {
            return false;
        }

        return true;
    }

    private static bool HasEquivalentStaticFrame(
        AnimationFile.Frame? expected,
        AnimationFile.Frame? actual)
    {
        if (expected == null || actual == null)
            return expected == null && actual == null;

        return expected.Transforms.Count == actual.Transforms.Count &&
               expected.Quaternion.Count == actual.Quaternion.Count;
    }

    private static bool HasEquivalentMappings(
        IReadOnlyList<AnimationFile.AnimationBoneMapping> expected,
        IReadOnlyList<AnimationFile.AnimationBoneMapping> actual)
    {
        if (expected.Count != actual.Count)
            return false;

        for (var index = 0; index < expected.Count; index++)
        {
            if (expected[index].FileWriteValue !=
                actual[index].FileWriteValue)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasEquivalentPose(
        AnimationClip expected,
        AnimationClip actual)
    {
        if ((expected.Duration - actual.Duration).Duration() >
                s_durationTolerance ||
            expected.DynamicFrames.Count != actual.DynamicFrames.Count)
        {
            return false;
        }

        for (var frameIndex = 0;
             frameIndex < expected.DynamicFrames.Count;
             frameIndex++)
        {
            var expectedFrame = expected.DynamicFrames[frameIndex];
            var actualFrame = actual.DynamicFrames[frameIndex];
            if (expectedFrame.Position.Count != actualFrame.Position.Count ||
                expectedFrame.Rotation.Count != actualFrame.Rotation.Count ||
                expectedFrame.Scale.Count != actualFrame.Scale.Count)
            {
                return false;
            }

            for (var boneIndex = 0;
                 boneIndex < expectedFrame.Position.Count;
                 boneIndex++)
            {
                if (!NearlyEqual(
                        expectedFrame.Position[boneIndex],
                        actualFrame.Position[boneIndex]) ||
                    !NearlyEqual(
                        expectedFrame.Scale[boneIndex],
                        actualFrame.Scale[boneIndex]) ||
                    !NearlyEqual(
                        expectedFrame.Rotation[boneIndex],
                        actualFrame.Rotation[boneIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool NearlyEqual(Vector3 expected, Vector3 actual)
    {
        return IsFinite(expected.X) &&
               IsFinite(expected.Y) &&
               IsFinite(expected.Z) &&
               IsFinite(actual.X) &&
               IsFinite(actual.Y) &&
               IsFinite(actual.Z) &&
               MathF.Abs(expected.X - actual.X) <= VectorTolerance &&
               MathF.Abs(expected.Y - actual.Y) <= VectorTolerance &&
               MathF.Abs(expected.Z - actual.Z) <= VectorTolerance;
    }

    private static bool NearlyEqual(
        Quaternion expected,
        Quaternion actual)
    {
        if (!IsFinite(expected.X) ||
            !IsFinite(expected.Y) ||
            !IsFinite(expected.Z) ||
            !IsFinite(expected.W) ||
            !IsFinite(actual.X) ||
            !IsFinite(actual.Y) ||
            !IsFinite(actual.Z) ||
            !IsFinite(actual.W) ||
            expected.LengthSquared() <= float.Epsilon ||
            actual.LengthSquared() <= float.Epsilon)
        {
            return false;
        }

        expected.Normalize();
        actual.Normalize();
        return MathF.Abs(Quaternion.Dot(expected, actual)) >=
               QuaternionDotTolerance;
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
