using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchBlendCurve
{
    Smooth,
    Linear,
    EaseInOut,
}

public sealed record AnimationWorkbenchRootMotionOptions(
    bool AlignHorizontalPosition = true,
    bool AlignYaw = true,
    bool PreserveSourceHeightChanges = true)
{
    public static AnimationWorkbenchRootMotionOptions Default { get; } = new();
}

public sealed record AnimationWorkbenchBlendRequest(
    int AnimationAOutFrame,
    int AnimationBInFrame,
    TimeSpan OverlapDuration,
    double OutputFramesPerSecond,
    AnimationWorkbenchBlendCurve Curve,
    AnimationWorkbenchRootMotionOptions RootMotion);

public sealed record AnimationWorkbenchBlendImpact(
    double AnimationAFramesPerSecond,
    double AnimationBFramesPerSecond,
    double OutputFramesPerSecond,
    bool AnimationAWasResampled,
    bool AnimationBWasResampled,
    int AnimationAOutputFrameCount,
    int AnimationBOutputFrameCount,
    int OverlapFrameCount,
    int OutputFrameCount,
    TimeSpan RequestedOverlapDuration,
    TimeSpan QuantizedOverlapDuration,
    TimeSpan OutputDuration,
    float LoopSeamPositionDelta,
    float LoopSeamRotationDegrees,
    float LoopSeamScaleDelta,
    bool HasLoopSeamDiscontinuity);

public sealed record AnimationWorkbenchBlendResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    AnimationWorkbenchBlendImpact? Impact,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed partial class AnimationWorkbenchDocument
{
    private const float LoopPositionTolerance = 0.001f;
    private const float LoopRotationToleranceDegrees = 0.5f;
    private const float LoopScaleTolerance = 0.001f;

    private SourceSnapshot? _blendPreviewResult;
    private AnimationWorkbenchBlendImpact? _blendPreviewImpact;
    private long _blendPreviewVersion;
    private IReadOnlyList<AnimationWorkbenchDiagnostic> _blendPreviewDiagnostics =
        Array.Empty<AnimationWorkbenchDiagnostic>();

    internal long ActiveBlendPreviewVersion =>
        _blendPreviewResult == null ? 0 : _blendPreviewVersion;

    public AnimationWorkbenchBlendResult PreviewBlend(
        AnimationWorkbenchBlendRequest request)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RootMotion);

        if (_posePreviewResult != null || _timelinePreviewResult != null)
        {
            return CreateBlendFailure(
                _posePreviewResult != null
                    ? AnimationWorkbenchDiagnosticCode.PosePreviewAlreadyActive
                    : AnimationWorkbenchDiagnosticCode
                        .TimelinePreviewAlreadyActive);
        }

        var build = BuildBlend(request);
        if (build.Animation == null || build.Impact == null)
        {
            if (_blendPreviewResult != null)
            {
                ClearBlendPreview();
                RefreshSelectedResultPreview();
            }
            return new AnimationWorkbenchBlendResult(
                false,
                CreateState(),
                null,
                build.Diagnostics);
        }

        _blendPreviewResult = _result!.WithPreviewAnimation(build.Animation);
        _blendPreviewImpact = build.Impact;
        _blendPreviewDiagnostics = build.Diagnostics;
        _blendPreviewVersion = _blendPreviewVersion == long.MaxValue
            ? 1
            : _blendPreviewVersion + 1;
        RefreshSelectedResultPreview();
        return CreateBlendSuccess();
    }

    public AnimationWorkbenchBlendResult CommitBlendPreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_blendPreviewResult == null || _blendPreviewImpact == null)
        {
            return CreateBlendFailure(
                AnimationWorkbenchDiagnosticCode.BlendPreviewMissing);
        }

        var next = _blendPreviewResult.Animation.Clone();
        var impact = _blendPreviewImpact;
        var diagnostics = _blendPreviewDiagnostics.ToArray();
        var transitionStart = Math.Max(
            0,
            impact.AnimationAOutputFrameCount - impact.OverlapFrameCount);
        var transitionEnd = Math.Min(
            next.DynamicFrames.Count - 1,
            transitionStart + Math.Max(0, impact.OverlapFrameCount - 1));
        ClearBlendPreview();
        CommitResultAnimation(
            next,
            [0, transitionStart, transitionEnd, next.DynamicFrames.Count - 1]);
        return new AnimationWorkbenchBlendResult(
            true,
            CreateState(),
            impact,
            diagnostics);
    }

    public AnimationWorkbenchBlendResult CancelBlendPreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_blendPreviewResult == null)
        {
            return CreateBlendFailure(
                AnimationWorkbenchDiagnosticCode.BlendPreviewMissing);
        }

        ClearBlendPreview();
        RefreshSelectedResultPreview();
        return new AnimationWorkbenchBlendResult(
            true,
            CreateState(),
            null,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    private BlendBuildResult BuildBlend(AnimationWorkbenchBlendRequest request)
    {
        var diagnostics = new List<AnimationWorkbenchDiagnostic>();
        if (_animationA == null)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.AnimationAMissing));
        }

        if (_animationB == null)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendAnimationBMissing));
        }

        if (_targetSkeleton == null)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing));
        }

        if (TryValidateBlendSource(
                _animationA,
                AnimationWorkbenchSourceSlot.AnimationA,
                out var sourceDiagnostic) == false)
        {
            return BlendBuildResult.Failure(sourceDiagnostic!);
        }

        if (TryValidateBlendSource(
                _animationB,
                AnimationWorkbenchSourceSlot.AnimationB,
                out sourceDiagnostic) == false)
        {
            return BlendBuildResult.Failure(sourceDiagnostic!);
        }

        if (SkeletonsMatch(_animationA.Skeleton, _targetSkeleton) == false)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendSkeletonMismatch,
                AnimationWorkbenchDiagnosticSeverity.Error,
                AnimationWorkbenchSourceSlot.AnimationA));
        }

        if (SkeletonsMatch(_animationB.Skeleton, _targetSkeleton) == false)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendSkeletonMismatch,
                AnimationWorkbenchDiagnosticSeverity.Error,
                AnimationWorkbenchSourceSlot.AnimationB));
        }

        var animationA = _animationA.Animation;
        var animationB = _animationB.Animation;
        if (request.AnimationAOutFrame < 0 ||
            request.AnimationAOutFrame >= animationA.DynamicFrames.Count)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendOutPointInvalid,
                expectedValue: animationA.DynamicFrames.Count,
                actualValue: request.AnimationAOutFrame));
        }

        if (request.AnimationBInFrame < 0 ||
            request.AnimationBInFrame >= animationB.DynamicFrames.Count)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendInPointInvalid,
                expectedValue: animationB.DynamicFrames.Count,
                actualValue: request.AnimationBInFrame));
        }

        if (request.OverlapDuration < TimeSpan.Zero)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendOverlapInvalid));
        }

        if (double.IsFinite(request.OutputFramesPerSecond) == false ||
            request.OutputFramesPerSecond <= 0 ||
            request.OutputFramesPerSecond > TimeSpan.TicksPerSecond)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .BlendOutputFrameRateInvalid));
        }

        var timebaseA = animationA.Timebase!;
        var timebaseB = animationB.Timebase!;
        var durationA = GetRangeBoundaryTime(
            timebaseA,
            request.AnimationAOutFrame + 1);
        var startB = timebaseB.GetSampleTime(request.AnimationBInFrame);
        var durationB = timebaseB.Duration - startB;
        if (request.OverlapDuration > durationA ||
            request.OverlapDuration > durationB)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .BlendOverlapExceedsAvailable));
        }

        if (TryQuantizeFrameCount(
                durationA,
                request.OutputFramesPerSecond,
                out var outputFramesA) == false ||
            TryQuantizeFrameCount(
                durationB,
                request.OutputFramesPerSecond,
                out var outputFramesB) == false)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendOutputTooLarge));
        }

        var overlapFrames = 0;
        if (request.OverlapDuration > TimeSpan.Zero)
        {
            if (TryQuantizeFrameCount(
                    request.OverlapDuration,
                    request.OutputFramesPerSecond,
                    out overlapFrames) == false)
            {
                return BlendBuildResult.Failure(CreateBlendDiagnostic(
                    AnimationWorkbenchDiagnosticCode.BlendOutputTooLarge));
            }
            overlapFrames = Math.Max(1, overlapFrames);
        }

        if (overlapFrames > outputFramesA ||
            overlapFrames > outputFramesB)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .BlendOverlapExceedsAvailable));
        }

        if (overlapFrames == outputFramesB)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .BlendOverlapConsumesAnimationB));
        }

        int outputFrameCount;
        try
        {
            outputFrameCount = checked(
                outputFramesA + outputFramesB - overlapFrames);
        }
        catch (OverflowException)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendOutputTooLarge));
        }

        if (outputFrameCount <= 0 ||
            outputFrameCount > MaximumTimelineFrameCount)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendOutputTooLarge));
        }

        AnimationTimebase outputTimebase;
        try
        {
            outputTimebase = AnimationTimebase.FromFramesPerSecond(
                outputFrameCount,
                request.OutputFramesPerSecond);
        }
        catch (ArgumentOutOfRangeException)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .BlendOutputFrameRateInvalid));
        }

        var framesA = SampleSegment(
            animationA,
            TimeSpan.Zero,
            request.AnimationAOutFrame,
            outputFramesA,
            request.OutputFramesPerSecond,
            _targetSkeleton.BoneCount);
        var framesB = SampleSegment(
            animationB,
            startB,
            animationB.DynamicFrames.Count - 1,
            outputFramesB,
            request.OutputFramesPerSecond,
            _targetSkeleton.BoneCount);

        var rootIndex = FindRootBoneIndex(_targetSkeleton);
        if ((request.RootMotion.AlignHorizontalPosition ||
             request.RootMotion.AlignYaw ||
             request.RootMotion.PreserveSourceHeightChanges) &&
            rootIndex < 0)
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendRootBoneMissing));
        }

        if (rootIndex >= 0)
        {
            var anchorIndex = overlapFrames == 0
                ? framesA.Count - 1
                : framesA.Count - overlapFrames;
            AlignRootMotion(
                framesB,
                rootIndex,
                framesA[anchorIndex],
                request.RootMotion);
        }

        var output = new AnimationClip
        {
            Duration = outputTimebase.Duration,
        };
        var transitionStart = framesA.Count - overlapFrames;
        for (var frameIndex = 0;
             frameIndex < transitionStart;
             frameIndex++)
        {
            output.DynamicFrames.Add(framesA[frameIndex].Clone());
        }

        for (var overlapIndex = 0;
             overlapIndex < overlapFrames;
             overlapIndex++)
        {
            var amount = EvaluateCurve(
                request.Curve,
                overlapIndex / (float)overlapFrames);
            output.DynamicFrames.Add(InterpolateBlendFrame(
                framesA[transitionStart + overlapIndex],
                framesB[overlapIndex],
                amount,
                _targetSkeleton.BoneCount));
        }

        for (var frameIndex = overlapFrames;
             frameIndex < framesB.Count;
             frameIndex++)
        {
            output.DynamicFrames.Add(framesB[frameIndex].Clone());
        }

        if (output.DynamicFrames.Any(frame =>
                HasValidFrameTransforms(
                    frame,
                    _targetSkeleton.BoneCount) == false))
        {
            return BlendBuildResult.Failure(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode
                    .BlendResultTransformInvalid));
        }

        if (request.OverlapDuration == TimeSpan.Zero)
        {
            diagnostics.Add(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendZeroOverlap,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }
        else if (request.OverlapDuration.TotalSeconds *
                 request.OutputFramesPerSecond < 1)
        {
            diagnostics.Add(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendOverlapBelowOneFrame,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }

        if (animationA.DynamicFrames.Count == 1 ||
            animationB.DynamicFrames.Count == 1)
        {
            diagnostics.Add(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendSingleFrameSource,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }

        var loopSeam = MeasureLoopSeam(output, _targetSkeleton.BoneCount);
        if (loopSeam.IsDiscontinuous)
        {
            diagnostics.Add(CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendLoopSeamDiscontinuity,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }

        var quantizedOverlap = overlapFrames == 0
            ? TimeSpan.Zero
            : AnimationTimebase.FromFramesPerSecond(
                overlapFrames,
                request.OutputFramesPerSecond).Duration;
        var impact = new AnimationWorkbenchBlendImpact(
            timebaseA.FramesPerSecond,
            timebaseB.FramesPerSecond,
            request.OutputFramesPerSecond,
            RatesDiffer(timebaseA.FramesPerSecond, request.OutputFramesPerSecond),
            RatesDiffer(timebaseB.FramesPerSecond, request.OutputFramesPerSecond),
            outputFramesA,
            outputFramesB,
            overlapFrames,
            outputFrameCount,
            request.OverlapDuration,
            quantizedOverlap,
            outputTimebase.Duration,
            loopSeam.PositionDelta,
            loopSeam.RotationDegrees,
            loopSeam.ScaleDelta,
            loopSeam.IsDiscontinuous);
        return new BlendBuildResult(output, impact, diagnostics.ToArray());
    }

    private static bool TryValidateBlendSource(
        SourceSnapshot source,
        AnimationWorkbenchSourceSlot slot,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        var animation = source.Animation;
        if (animation.DynamicFrames.Count == 0)
        {
            diagnostic = CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendSourceEmpty,
                AnimationWorkbenchDiagnosticSeverity.Error,
                slot);
            return false;
        }

        if (animation.Timebase == null)
        {
            diagnostic = CreateBlendDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendSourceDurationInvalid,
                AnimationWorkbenchDiagnosticSeverity.Error,
                slot);
            return false;
        }

        foreach (var frame in animation.DynamicFrames)
        {
            if (frame.Position.Count != source.SkeletonBoneCount ||
                frame.Rotation.Count != source.SkeletonBoneCount ||
                frame.Scale.Count != source.SkeletonBoneCount)
            {
                diagnostic = CreateBlendDiagnostic(
                    AnimationWorkbenchDiagnosticCode
                        .SourceSkeletonBoneCountMismatch,
                    AnimationWorkbenchDiagnosticSeverity.Error,
                    slot,
                    source.SkeletonBoneCount,
                    frame.Position.Count);
                return false;
            }

            for (var boneIndex = 0;
                 boneIndex < source.SkeletonBoneCount;
                 boneIndex++)
            {
                var rotation = frame.Rotation[boneIndex];
                var rotationLengthSquared = rotation.LengthSquared();
                if (IsFinite(frame.Position[boneIndex]) == false ||
                    IsFinite(frame.Scale[boneIndex]) == false ||
                    IsFinite(rotation) == false ||
                    float.IsFinite(rotationLengthSquared) == false ||
                    rotationLengthSquared < MinimumQuaternionLengthSquared)
                {
                    diagnostic = CreateBlendDiagnostic(
                        AnimationWorkbenchDiagnosticCode
                            .BlendSourceTransformInvalid,
                        AnimationWorkbenchDiagnosticSeverity.Error,
                        slot);
                    return false;
                }
            }
        }

        diagnostic = null;
        return true;
    }

    private static bool SkeletonsMatch(GameSkeleton source, GameSkeleton target)
    {
        const float restPoseTolerance = 0.0001f;
        if (source.BoneCount != target.BoneCount ||
            string.Equals(
                source.SkeletonName,
                target.SkeletonName,
                StringComparison.Ordinal) == false)
        {
            return false;
        }
        for (var boneIndex = 0; boneIndex < source.BoneCount; boneIndex++)
        {
            if (string.Equals(
                    source.BoneNames[boneIndex],
                    target.BoneNames[boneIndex],
                    StringComparison.Ordinal) == false ||
                source.GetParentBoneIndex(boneIndex) !=
                target.GetParentBoneIndex(boneIndex) ||
                VectorsMatch(
                    source.Translation[boneIndex],
                    target.Translation[boneIndex],
                    restPoseTolerance) == false ||
                QuaternionsMatch(
                    source.Rotation[boneIndex],
                    target.Rotation[boneIndex],
                    restPoseTolerance) == false ||
                ScalarsMatch(
                    source.Scale[boneIndex],
                    target.Scale[boneIndex],
                    restPoseTolerance) == false)
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasValidFrameTransforms(
        AnimationClip.KeyFrame frame,
        int boneCount)
    {
        if (frame.Position.Count != boneCount ||
            frame.Rotation.Count != boneCount ||
            frame.Scale.Count != boneCount)
        {
            return false;
        }

        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            var rotation = frame.Rotation[boneIndex];
            var lengthSquared = rotation.LengthSquared();
            if (IsFinite(frame.Position[boneIndex]) == false ||
                IsFinite(frame.Scale[boneIndex]) == false ||
                IsFinite(rotation) == false ||
                float.IsFinite(lengthSquared) == false ||
                lengthSquared < MinimumQuaternionLengthSquared)
            {
                return false;
            }
        }
        return true;
    }

    private static bool VectorsMatch(
        Vector3 first,
        Vector3 second,
        float tolerance) =>
        ScalarsMatch(first.X, second.X, tolerance) &&
        ScalarsMatch(first.Y, second.Y, tolerance) &&
        ScalarsMatch(first.Z, second.Z, tolerance);

    private static bool QuaternionsMatch(
        Quaternion first,
        Quaternion second,
        float tolerance)
    {
        if (IsFinite(first) == false || IsFinite(second) == false)
            return false;
        var firstLengthSquared = first.LengthSquared();
        var secondLengthSquared = second.LengthSquared();
        if (float.IsFinite(firstLengthSquared) == false ||
            float.IsFinite(secondLengthSquared) == false ||
            firstLengthSquared < MinimumQuaternionLengthSquared ||
            secondLengthSquared < MinimumQuaternionLengthSquared)
        {
            return false;
        }

        first.Normalize();
        second.Normalize();
        return 1 - MathF.Abs(Quaternion.Dot(first, second)) <= tolerance;
    }

    private static bool ScalarsMatch(
        float first,
        float second,
        float tolerance) =>
        float.IsFinite(first) &&
        float.IsFinite(second) &&
        MathF.Abs(first - second) <= tolerance;

    private static TimeSpan GetRangeBoundaryTime(
        AnimationTimebase timebase,
        int frameIndexExclusive)
    {
        var ticks = (long)Math.Round(
            (decimal)timebase.Duration.Ticks * frameIndexExclusive /
            timebase.FrameCount,
            MidpointRounding.AwayFromZero);
        return TimeSpan.FromTicks(Math.Clamp(ticks, 1, timebase.Duration.Ticks));
    }

    private static bool TryQuantizeFrameCount(
        TimeSpan duration,
        double framesPerSecond,
        out int frameCount)
    {
        var exactFrameCount = duration.TotalSeconds * framesPerSecond;
        if (double.IsFinite(exactFrameCount) == false ||
            exactFrameCount > MaximumTimelineFrameCount)
        {
            frameCount = 0;
            return false;
        }

        frameCount = Math.Max(
            1,
            (int)Math.Round(
                exactFrameCount,
                MidpointRounding.AwayFromZero));
        return true;
    }

    private static List<AnimationClip.KeyFrame> SampleSegment(
        AnimationClip source,
        TimeSpan start,
        int lastFrameIndex,
        int outputFrameCount,
        double outputFramesPerSecond,
        int boneCount)
    {
        var result = new List<AnimationClip.KeyFrame>(outputFrameCount);
        var lastSample = source.Timebase!.GetSampleTime(lastFrameIndex);
        for (var outputIndex = 0;
             outputIndex < outputFrameCount;
             outputIndex++)
        {
            var offsetTicks = (long)Math.Round(
                outputIndex * (double)TimeSpan.TicksPerSecond /
                outputFramesPerSecond,
                MidpointRounding.AwayFromZero);
            var sampleTime = start + TimeSpan.FromTicks(offsetTicks);
            if (sampleTime > lastSample)
                sampleTime = lastSample;
            result.Add(SampleLocalFrame(source, sampleTime, boneCount));
        }
        return result;
    }

    private static AnimationClip.KeyFrame SampleLocalFrame(
        AnimationClip source,
        TimeSpan time,
        int boneCount)
    {
        var position = source.Timebase!.GetSamplePosition(time);
        var firstIndex = (int)Math.Floor(position);
        var secondIndex = Math.Min(
            firstIndex + 1,
            source.DynamicFrames.Count - 1);
        var amount = (float)(position - firstIndex);
        return InterpolateBlendFrame(
            source.DynamicFrames[firstIndex],
            source.DynamicFrames[secondIndex],
            amount,
            boneCount);
    }

    private static AnimationClip.KeyFrame InterpolateBlendFrame(
        AnimationClip.KeyFrame first,
        AnimationClip.KeyFrame second,
        float amount,
        int boneCount)
    {
        var result = new AnimationClip.KeyFrame();
        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            result.Position.Add(Vector3.Lerp(
                first.Position[boneIndex],
                second.Position[boneIndex],
                amount));
            var rotation = Quaternion.Slerp(
                first.Rotation[boneIndex],
                second.Rotation[boneIndex],
                amount);
            rotation.Normalize();
            result.Rotation.Add(rotation);
            result.Scale.Add(Vector3.Lerp(
                first.Scale[boneIndex],
                second.Scale[boneIndex],
                amount));
        }
        return result;
    }

    private static int FindRootBoneIndex(GameSkeleton skeleton)
    {
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (skeleton.GetParentBoneIndex(boneIndex) ==
                AnimationFile.BoneIndexNoParent)
            {
                return boneIndex;
            }
        }
        return -1;
    }

    private static void AlignRootMotion(
        IReadOnlyList<AnimationClip.KeyFrame> frames,
        int rootIndex,
        AnimationClip.KeyFrame anchorFrame,
        AnimationWorkbenchRootMotionOptions options)
    {
        if (frames.Count == 0)
            return;
        var sourceStartPosition = frames[0].Position[rootIndex];
        var sourceStartRotation = frames[0].Rotation[rootIndex];
        var anchorPosition = anchorFrame.Position[rootIndex];
        var yawOffset = options.AlignYaw
            ? Quaternion.CreateFromAxisAngle(
                Vector3.Up,
                MathHelper.WrapAngle(
                    ExtractYaw(anchorFrame.Rotation[rootIndex]) -
                    ExtractYaw(sourceStartRotation)))
            : Quaternion.Identity;

        foreach (var frame in frames)
        {
            var sourcePosition = frame.Position[rootIndex];
            var alignedPosition = sourcePosition;
            if (options.AlignHorizontalPosition)
            {
                var delta = sourcePosition - sourceStartPosition;
                if (options.AlignYaw)
                    delta = Vector3.Transform(delta, yawOffset);
                alignedPosition.X = anchorPosition.X + delta.X;
                alignedPosition.Z = anchorPosition.Z + delta.Z;
            }
            if (options.PreserveSourceHeightChanges)
            {
                alignedPosition.Y = anchorPosition.Y +
                    sourcePosition.Y - sourceStartPosition.Y;
            }
            frame.Position[rootIndex] = alignedPosition;

            if (options.AlignYaw)
            {
                var rotation = yawOffset * frame.Rotation[rootIndex];
                rotation.Normalize();
                frame.Rotation[rootIndex] = rotation;
            }
        }
    }

    private static float ExtractYaw(Quaternion rotation)
    {
        var forward = Vector3.Transform(Vector3.Forward, rotation);
        if (forward.X * forward.X + forward.Z * forward.Z < 0.0000001f)
            return 0;
        return MathF.Atan2(-forward.X, -forward.Z);
    }

    private static float EvaluateCurve(
        AnimationWorkbenchBlendCurve curve,
        float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return curve switch
        {
            AnimationWorkbenchBlendCurve.Linear => amount,
            AnimationWorkbenchBlendCurve.Smooth =>
                amount * amount * amount *
                (amount * (amount * 6 - 15) + 10),
            AnimationWorkbenchBlendCurve.EaseInOut => amount < 0.5f
                ? 2 * amount * amount
                : 1 - MathF.Pow(-2 * amount + 2, 2) / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(curve)),
        };
    }

    private static LoopSeamMeasurement MeasureLoopSeam(
        AnimationClip animation,
        int boneCount)
    {
        var first = animation.DynamicFrames[0];
        var last = animation.DynamicFrames[^1];
        var positionDelta = 0f;
        var rotationDegrees = 0f;
        var scaleDelta = 0f;
        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            positionDelta = Math.Max(
                positionDelta,
                Vector3.Distance(
                    first.Position[boneIndex],
                    last.Position[boneIndex]));
            scaleDelta = Math.Max(
                scaleDelta,
                Vector3.Distance(
                    first.Scale[boneIndex],
                    last.Scale[boneIndex]));
            var dot = Math.Clamp(
                MathF.Abs(Quaternion.Dot(
                    first.Rotation[boneIndex],
                    last.Rotation[boneIndex])),
                0,
                1);
            rotationDegrees = Math.Max(
                rotationDegrees,
                MathHelper.ToDegrees(2 * MathF.Acos(dot)));
        }
        return new LoopSeamMeasurement(
            positionDelta,
            rotationDegrees,
            scaleDelta,
            positionDelta > LoopPositionTolerance ||
            rotationDegrees > LoopRotationToleranceDegrees ||
            scaleDelta > LoopScaleTolerance);
    }

    private static bool RatesDiffer(double first, double second) =>
        Math.Abs(first - second) > 0.000001;

    private AnimationWorkbenchBlendResult CreateBlendSuccess() => new(
        true,
        CreateState(),
        _blendPreviewImpact,
        _blendPreviewDiagnostics.ToArray());

    private AnimationWorkbenchBlendResult CreateBlendFailure(
        AnimationWorkbenchDiagnosticCode code) => new(
        false,
        CreateState(),
        null,
        [CreateBlendDiagnostic(code)]);

    private static AnimationWorkbenchDiagnostic CreateBlendDiagnostic(
        AnimationWorkbenchDiagnosticCode code,
        AnimationWorkbenchDiagnosticSeverity severity =
            AnimationWorkbenchDiagnosticSeverity.Error,
        AnimationWorkbenchSourceSlot? source = null,
        int? expectedValue = null,
        int? actualValue = null) => new(
        code,
        severity,
        source,
        expectedValue,
        actualValue);

    private void ClearBlendPreview()
    {
        _blendPreviewResult = null;
        _blendPreviewImpact = null;
        _blendPreviewDiagnostics = Array.Empty<AnimationWorkbenchDiagnostic>();
    }

    private sealed record BlendBuildResult(
        AnimationClip? Animation,
        AnimationWorkbenchBlendImpact? Impact,
        IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics)
    {
        public static BlendBuildResult Failure(
            AnimationWorkbenchDiagnostic diagnostic) => new(
            null,
            null,
            [diagnostic]);
    }

    private sealed record LoopSeamMeasurement(
        float PositionDelta,
        float RotationDegrees,
        float ScaleDelta,
        bool IsDiscontinuous);
}
