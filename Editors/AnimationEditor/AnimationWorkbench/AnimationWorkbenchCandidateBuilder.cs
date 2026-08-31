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
        TimeSpan.FromMilliseconds(1);

    public static AnimationWorkbenchCandidateBuildResult Build(
        AnimationClip result,
        GameSkeleton targetSkeleton,
        AnimationWorkbenchSourceFormat sourceFormat)
    {
        if (!HasCompleteFrames(result, targetSkeleton.BoneCount))
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode
                    .ResultTargetSkeletonBoneCountMismatch);
        }

        AnimationFile candidate;
        var outputVersion = sourceFormat.Version == 8 ? 8u : 7u;
        try
        {
            candidate = result.ConvertToFileFormat(
                targetSkeleton,
                outputVersion,
                sourceFormat.UnknownValueV8,
                sourceFormat.FlagVariables);
        }
        catch (Exception)
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode.CandidateSerializationFailed);
        }

        if (!MatchesTargetStructure(
                candidate,
                result,
                targetSkeleton,
                outputVersion))
        {
            return AnimationWorkbenchCandidateBuildResult.Failure(
                AnimationWorkbenchDiagnosticCode.CandidateRoundTripMismatch);
        }

        byte[] bytes;
        AnimationFile roundTripFile;
        try
        {
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
            if (frame.Position.Count != targetBoneCount ||
                frame.Rotation.Count != targetBoneCount ||
                frame.Scale.Count != targetBoneCount)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTargetStructure(
        AnimationFile candidate,
        AnimationClip expectedAnimation,
        GameSkeleton targetSkeleton,
        uint expectedVersion)
    {
        if (candidate.Header.Version != expectedVersion ||
            candidate.Header.SkeletonName != targetSkeleton.SkeletonName ||
            candidate.Bones == null ||
            candidate.Bones.Length != targetSkeleton.BoneCount ||
            candidate.AnimationParts.Count != 1)
        {
            return false;
        }

        var part = candidate.AnimationParts[0];
        var dynamicTranslationCount = part.TranslationMappings.Count(
            mapping => mapping.MappingType ==
                AnimationFile.AnimationBoneMappingType.Dynamic);
        var dynamicRotationCount = part.RotationMappings.Count(
            mapping => mapping.MappingType ==
                AnimationFile.AnimationBoneMappingType.Dynamic);
        var expectedDynamicFrameCount =
            dynamicTranslationCount != 0 || dynamicRotationCount != 0
                ? expectedAnimation.DynamicFrames.Count
                : 0;
        if (part.DynamicFrames.Count != expectedDynamicFrameCount ||
            part.TranslationMappings.Count != targetSkeleton.BoneCount ||
            part.RotationMappings.Count != targetSkeleton.BoneCount)
        {
            return false;
        }

        for (var boneIndex = 0;
             boneIndex < targetSkeleton.BoneCount;
             boneIndex++)
        {
            var bone = candidate.Bones[boneIndex];
            var translationMapping = part.TranslationMappings[boneIndex];
            var rotationMapping = part.RotationMappings[boneIndex];
            if (bone.Id != boneIndex ||
                bone.Name != targetSkeleton.BoneNames[boneIndex] ||
                bone.ParentId !=
                    targetSkeleton.GetParentBoneIndex(boneIndex) ||
                !IsSupportedMapping(translationMapping, expectedVersion, boneIndex) ||
                !IsSupportedMapping(rotationMapping, expectedVersion, boneIndex))
            {
                return false;
            }
        }

        var staticTranslationCount = part.TranslationMappings.Count(
            mapping => mapping.MappingType ==
                AnimationFile.AnimationBoneMappingType.Static);
        var staticRotationCount = part.RotationMappings.Count(
            mapping => mapping.MappingType ==
                AnimationFile.AnimationBoneMappingType.Static);
        if (expectedVersion == 7 && part.StaticFrame != null)
            return false;
        if (expectedVersion == 8 &&
            ((staticTranslationCount != 0 || staticRotationCount != 0) !=
             (part.StaticFrame != null)))
        {
            return false;
        }
        if (part.StaticFrame != null &&
            (part.StaticFrame.Transforms.Count != staticTranslationCount ||
             part.StaticFrame.Quaternion.Count != staticRotationCount))
        {
            return false;
        }

        return part.DynamicFrames.All(
            frame =>
                frame.Transforms.Count == dynamicTranslationCount &&
                frame.Quaternion.Count == dynamicRotationCount);
    }

    private static bool IsSupportedMapping(
        AnimationFile.AnimationBoneMapping mapping,
        uint expectedVersion,
        int boneIndex)
    {
        if (expectedVersion == 7)
        {
            return mapping.MappingType ==
                       AnimationFile.AnimationBoneMappingType.Dynamic &&
                   mapping.Id == boneIndex;
        }

        return mapping.MappingType !=
            AnimationFile.AnimationBoneMappingType.None;
    }

    private static bool HasEquivalentFileStructure(
        AnimationFile expected,
        AnimationFile actual)
    {
        if (actual.Header.Version != expected.Header.Version ||
            actual.Header.SkeletonName != expected.Header.SkeletonName ||
            MathF.Abs(actual.Header.FrameRate - expected.Header.FrameRate) >
                VectorTolerance ||
            MathF.Abs(
                actual.Header.AnimationTotalPlayTimeInSec -
                expected.Header.AnimationTotalPlayTimeInSec) >
                VectorTolerance ||
            !actual.Header.FlagVariables.SequenceEqual(
                expected.Header.FlagVariables) ||
            actual.Header.UnknownValue_v8 != expected.Header.UnknownValue_v8 ||
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
