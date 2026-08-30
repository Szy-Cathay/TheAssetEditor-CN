using System.Collections.ObjectModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed record AnimationWorkbenchBoneTransform(
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale);

public sealed class AnimationWorkbenchPoseClipboard
{
    public AnimationWorkbenchPoseClipboard(
        bool isCompletePose,
        IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform>? bones)
    {
        IsCompletePose = isCompletePose;
        Bones = new ReadOnlyDictionary<string, AnimationWorkbenchBoneTransform>(
            bones == null
                ? new Dictionary<string, AnimationWorkbenchBoneTransform>(
                    StringComparer.Ordinal)
                : new Dictionary<string, AnimationWorkbenchBoneTransform>(
                    bones,
                    StringComparer.Ordinal));
    }

    public bool IsCompletePose { get; }

    public IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform> Bones { get; }
}

public sealed record AnimationWorkbenchPoseEditResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    AnimationWorkbenchPoseClipboard? Clipboard,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed partial class AnimationWorkbenchDocument
{
    private const float MinimumQuaternionLengthSquared = 0.000000000001f;
    private const float UnitScaleTolerance = 0.0001f;

    private readonly Stack<DocumentHistoryEntry> _undoEdits = new();
    private readonly Stack<DocumentHistoryEntry> _redoEdits = new();
    private SourceSnapshot? _posePreviewResult;
    private AnimationClip? _posePreviewStart;
    private AnimationClip? _savedResultAnimation;
    private int _posePreviewFrameIndex = -1;

    public AnimationWorkbenchPoseEditResult InsertPoseFrame(
        int insertionIndex,
        int sourceFrameIndex)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (TryGetCommittedEditContext(
                out var animation,
                out var skeleton,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateFrame(
                animation,
                skeleton,
                sourceFrameIndex,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (insertionIndex < 0 || insertionIndex > animation.DynamicFrames.Count)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseFrameIndexInvalid,
                animation.DynamicFrames.Count,
                insertionIndex));
        }

        var next = animation.Clone();
        next.DynamicFrames.Insert(
            insertionIndex,
            animation.DynamicFrames[sourceFrameIndex].Clone());
        next.Duration = ScaleDurationForFrameCount(
            animation.Duration,
            animation.DynamicFrames.Count,
            next.DynamicFrames.Count);
        CommitResultAnimation(next);
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult CopyPoseFrame(
        int frameIndex,
        IReadOnlyCollection<string>? boneNames = null)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (TryGetCommittedEditContext(
                out var animation,
                out var skeleton,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateFrame(
                animation,
                skeleton,
                frameIndex,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        var isCompletePose = boneNames == null;
        var requestedBones = boneNames?.ToArray()
            ?? skeleton.BoneNames.ToArray();
        if (requestedBones.Length == 0)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete));
        }

        if (requestedBones.Distinct(StringComparer.Ordinal).Count() !=
            requestedBones.Length)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete));
        }

        var frame = animation.DynamicFrames[frameIndex];
        var transforms = new Dictionary<string, AnimationWorkbenchBoneTransform>(
            StringComparer.Ordinal);
        foreach (var boneName in requestedBones)
        {
            var boneIndex = skeleton.GetBoneIndexByName(boneName);
            if (boneIndex < 0)
            {
                return CreatePoseFailure(CreatePoseDiagnostic(
                    AnimationWorkbenchDiagnosticCode.PoseBoneMissing,
                    boneName: boneName));
            }

            var transform = new AnimationWorkbenchBoneTransform(
                frame.Position[boneIndex],
                frame.Rotation[boneIndex],
                frame.Scale[boneIndex]);
            if (TryNormalizeTransform(transform, out var normalized) == false)
            {
                return CreatePoseFailure(CreatePoseDiagnostic(
                    AnimationWorkbenchDiagnosticCode.PoseTransformInvalid,
                    boneName: boneName));
            }

            transforms.Add(boneName, normalized);
        }

        return CreatePoseSuccess(new AnimationWorkbenchPoseClipboard(
            isCompletePose,
            transforms));
    }

    public AnimationWorkbenchPoseEditResult DeletePoseFrame(int frameIndex)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (TryGetCommittedEditContext(
                out var animation,
                out var skeleton,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateFrame(
                animation,
                skeleton,
                frameIndex,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (animation.DynamicFrames.Count == 1)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseLastFrameDeleteRejected));
        }

        var next = animation.Clone();
        next.DynamicFrames.RemoveAt(frameIndex);
        next.Duration = ScaleDurationForFrameCount(
            animation.Duration,
            animation.DynamicFrames.Count,
            next.DynamicFrames.Count);
        CommitResultAnimation(next);
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult PastePoseFrame(
        int frameIndex,
        AnimationWorkbenchPoseClipboard? clipboard)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (TryGetCommittedEditContext(
                out var animation,
                out var skeleton,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateFrame(
                animation,
                skeleton,
                frameIndex,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateTransforms(
                skeleton,
                clipboard,
                out var transforms,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        var next = animation.Clone();
        ApplyTransforms(next.DynamicFrames[frameIndex], skeleton, transforms);
        if (AnimationsEqual(animation, next) == false)
            CommitResultAnimation(next);
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult ApplyExactPoseTransforms(
        int frameIndex,
        IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform>? transforms)
    {
        return ApplyCommittedPoseTransforms(frameIndex, transforms);
    }

    public AnimationWorkbenchPoseEditResult BeginPosePreview(int frameIndex)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_posePreviewResult != null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewAlreadyActive));
        }

        if (TryGetEditContext(
                out var animation,
                out var skeleton,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateFrame(
                animation,
                skeleton,
                frameIndex,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        _posePreviewStart = animation.Clone();
        _posePreviewResult = _result!.Clone();
        _posePreviewFrameIndex = frameIndex;
        RefreshSelectedResultPreview();
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult PreviewPoseTransforms(
        IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform>? transforms)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_posePreviewResult == null || _posePreviewStart == null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewMissing));
        }

        if (_targetSkeleton == null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing));
        }

        var clipboard = new AnimationWorkbenchPoseClipboard(
            false,
            transforms);
        if (TryValidateTransforms(
                _targetSkeleton,
                clipboard,
                out var normalized,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        var next = _posePreviewStart.Clone();
        ApplyTransforms(
            next.DynamicFrames[_posePreviewFrameIndex],
            _targetSkeleton,
            normalized);
        _posePreviewResult = _result!.WithAnimation(next);
        RefreshSelectedResultPreview();
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult CommitPosePreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_posePreviewResult == null || _posePreviewStart == null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewMissing));
        }

        var next = _posePreviewResult.Animation.Clone();
        var previous = _posePreviewStart;
        ClearPosePreview();
        if (AnimationsEqual(previous, next))
        {
            RefreshSelectedResultPreview();
            return CreatePoseSuccess();
        }

        CommitResultAnimation(next);
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult CancelPosePreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_posePreviewResult == null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewMissing));
        }

        ClearPosePreview();
        RefreshSelectedResultPreview();
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult Undo()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_posePreviewResult != null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewAlreadyActive));
        }

        if (_undoEdits.Count == 0 || _result == null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseUndoUnavailable));
        }

        var entry = _undoEdits.Pop();
        _redoEdits.Push(entry);
        _result = _result.WithAnimation(entry.Before);
        UpdateDirtyState();
        RefreshSelectedResultPreview();
        return CreatePoseSuccess();
    }

    public AnimationWorkbenchPoseEditResult Redo()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_posePreviewResult != null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewAlreadyActive));
        }

        if (_redoEdits.Count == 0 || _result == null)
        {
            return CreatePoseFailure(CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseRedoUnavailable));
        }

        var entry = _redoEdits.Pop();
        _undoEdits.Push(entry);
        _result = _result.WithAnimation(entry.After);
        UpdateDirtyState();
        RefreshSelectedResultPreview();
        return CreatePoseSuccess();
    }

    private AnimationWorkbenchPoseEditResult ApplyCommittedPoseTransforms(
        int frameIndex,
        IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform>? transforms)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (TryGetCommittedEditContext(
                out var animation,
                out var skeleton,
                out var diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        if (TryValidateFrame(
                animation,
                skeleton,
                frameIndex,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        var clipboard = new AnimationWorkbenchPoseClipboard(
            false,
            transforms);
        if (TryValidateTransforms(
                skeleton,
                clipboard,
                out var normalized,
                out diagnostic) == false)
        {
            return CreatePoseFailure(diagnostic!);
        }

        var next = animation.Clone();
        ApplyTransforms(next.DynamicFrames[frameIndex], skeleton, normalized);
        if (AnimationsEqual(animation, next) == false)
            CommitResultAnimation(next);
        return CreatePoseSuccess();
    }

    private bool TryGetCommittedEditContext(
        out AnimationClip animation,
        out GameSkeleton skeleton,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        if (_posePreviewResult != null)
        {
            animation = null!;
            skeleton = null!;
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewAlreadyActive);
            return false;
        }

        return TryGetEditContext(out animation, out skeleton, out diagnostic);
    }

    private bool TryGetEditContext(
        out AnimationClip animation,
        out GameSkeleton skeleton,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        if (_result == null)
        {
            animation = null!;
            skeleton = null!;
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.ResultMissing);
            return false;
        }

        if (_targetSkeleton == null)
        {
            animation = null!;
            skeleton = null!;
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing);
            return false;
        }

        animation = _result.Animation;
        skeleton = _targetSkeleton;
        if (animation.AnimationBoneCount != skeleton.BoneCount)
        {
            diagnostic = new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.ResultTargetSkeletonBoneCountMismatch,
                AnimationWorkbenchDiagnosticSeverity.Error,
                ExpectedValue: skeleton.BoneCount,
                ActualValue: animation.AnimationBoneCount);
            return false;
        }

        diagnostic = null;
        return true;
    }

    private static bool TryValidateFrame(
        AnimationClip animation,
        GameSkeleton skeleton,
        int frameIndex,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        if (frameIndex < 0 || frameIndex >= animation.DynamicFrames.Count)
        {
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseFrameIndexInvalid,
                animation.DynamicFrames.Count,
                frameIndex);
            return false;
        }

        var frame = animation.DynamicFrames[frameIndex];
        if (frame.Position.Count != skeleton.BoneCount ||
            frame.Rotation.Count != skeleton.BoneCount ||
            frame.Scale.Count != skeleton.BoneCount)
        {
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseTransformInvalid,
                skeleton.BoneCount,
                Math.Min(
                    frame.Position.Count,
                    Math.Min(frame.Rotation.Count, frame.Scale.Count)));
            return false;
        }

        diagnostic = null;
        return true;
    }

    private static bool TryValidateTransforms(
        GameSkeleton skeleton,
        AnimationWorkbenchPoseClipboard? clipboard,
        out IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform> transforms,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        transforms = new Dictionary<string, AnimationWorkbenchBoneTransform>();
        if (clipboard == null || clipboard.Bones.Count == 0)
        {
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete);
            return false;
        }

        if (clipboard.IsCompletePose &&
            clipboard.Bones.Count != skeleton.BoneCount)
        {
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete,
                skeleton.BoneCount,
                clipboard.Bones.Count);
            return false;
        }

        var normalized = new Dictionary<string, AnimationWorkbenchBoneTransform>(
            StringComparer.Ordinal);
        foreach (var pair in clipboard.Bones)
        {
            if (skeleton.GetBoneIndexByName(pair.Key) < 0)
            {
                diagnostic = CreatePoseDiagnostic(
                    AnimationWorkbenchDiagnosticCode.PoseBoneMissing,
                    boneName: pair.Key);
                return false;
            }

            if (TryNormalizeTransform(pair.Value, out var transform) == false)
            {
                diagnostic = CreatePoseDiagnostic(
                    AnimationWorkbenchDiagnosticCode.PoseTransformInvalid,
                    boneName: pair.Key);
                return false;
            }

            normalized.Add(pair.Key, transform);
        }

        if (clipboard.IsCompletePose && skeleton.BoneNames.Any(
                boneName => normalized.ContainsKey(boneName) == false))
        {
            diagnostic = CreatePoseDiagnostic(
                AnimationWorkbenchDiagnosticCode.PoseClipboardIncomplete);
            return false;
        }

        transforms = normalized;
        diagnostic = null;
        return true;
    }

    private static bool TryNormalizeTransform(
        AnimationWorkbenchBoneTransform transform,
        out AnimationWorkbenchBoneTransform normalized)
    {
        normalized = transform;
        if (IsFinite(transform.Position) == false ||
            IsFinite(transform.Rotation) == false ||
            IsFinite(transform.Scale) == false ||
            IsUnitScale(transform.Scale) == false)
        {
            return false;
        }

        var lengthSquared = transform.Rotation.LengthSquared();
        if (float.IsFinite(lengthSquared) == false ||
            lengthSquared < MinimumQuaternionLengthSquared)
        {
            return false;
        }

        var rotation = transform.Rotation;
        rotation.Normalize();
        normalized = transform with
        {
            Rotation = rotation,
            Scale = Vector3.One,
        };
        return true;
    }

    private static bool IsUnitScale(Vector3 scale) =>
        MathF.Abs(scale.X - 1) <= UnitScaleTolerance &&
        MathF.Abs(scale.Y - 1) <= UnitScaleTolerance &&
        MathF.Abs(scale.Z - 1) <= UnitScaleTolerance;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static void ApplyTransforms(
        AnimationClip.KeyFrame frame,
        GameSkeleton skeleton,
        IReadOnlyDictionary<string, AnimationWorkbenchBoneTransform> transforms)
    {
        foreach (var pair in transforms)
        {
            var boneIndex = skeleton.GetBoneIndexByName(pair.Key);
            frame.Position[boneIndex] = pair.Value.Position;
            frame.Rotation[boneIndex] = pair.Value.Rotation;
            frame.Scale[boneIndex] = pair.Value.Scale;
        }
    }

    private void CommitResultAnimation(AnimationClip next)
    {
        var entry = new DocumentHistoryEntry(
            _result!.Animation.Clone(),
            next.Clone());
        _result = _result.WithAnimation(next);
        _undoEdits.Push(entry);
        _redoEdits.Clear();
        UpdateDirtyState();
        RefreshSelectedResultPreview();
    }

    private void ResetDocumentHistory()
    {
        _undoEdits.Clear();
        _redoEdits.Clear();
        ClearPosePreview();
        _savedResultAnimation = null;
        UpdateDirtyState();
    }

    private void MarkDocumentHistorySaved()
    {
        _savedResultAnimation = _result?.Animation.Clone();
        UpdateDirtyState();
    }

    private void UpdateDirtyState() =>
        _isDirty = _result != null &&
            (_savedResultAnimation == null ||
             AnimationsEqual(_result.Animation, _savedResultAnimation) == false);

    private void ClearPosePreview()
    {
        _posePreviewResult = null;
        _posePreviewStart = null;
        _posePreviewFrameIndex = -1;
    }

    private void RefreshSelectedResultPreview()
    {
        if (_selectedPreview != AnimationWorkbenchPreviewKind.Result)
            return;

        ReleasePreview();
        ShowCurrentPreview();
    }

    private AnimationWorkbenchPoseEditResult CreatePoseSuccess(
        AnimationWorkbenchPoseClipboard? clipboard = null) => new(
        true,
        CreateState(),
        clipboard,
        Array.Empty<AnimationWorkbenchDiagnostic>());

    private AnimationWorkbenchPoseEditResult CreatePoseFailure(
        AnimationWorkbenchDiagnostic diagnostic) => new(
        false,
        CreateState(),
        null,
        [diagnostic]);

    private static AnimationWorkbenchDiagnostic CreatePoseDiagnostic(
        AnimationWorkbenchDiagnosticCode code,
        int? expectedValue = null,
        int? actualValue = null,
        string? boneName = null) => new(
        code,
        AnimationWorkbenchDiagnosticSeverity.Error,
        ExpectedValue: expectedValue,
        ActualValue: actualValue,
        BoneName: boneName);

    private static TimeSpan ScaleDurationForFrameCount(
        TimeSpan duration,
        int previousFrameCount,
        int nextFrameCount)
    {
        if (duration <= TimeSpan.Zero || previousFrameCount <= 0)
            return duration;

        var ticks = (long)Math.Round(
            (double)duration.Ticks * nextFrameCount / previousFrameCount);
        return TimeSpan.FromTicks(Math.Max(1, ticks));
    }

    private static bool AnimationsEqual(AnimationClip left, AnimationClip right)
    {
        if (left.Duration != right.Duration ||
            left.DynamicFrames.Count != right.DynamicFrames.Count)
        {
            return false;
        }

        for (var frameIndex = 0;
             frameIndex < left.DynamicFrames.Count;
             frameIndex++)
        {
            var leftFrame = left.DynamicFrames[frameIndex];
            var rightFrame = right.DynamicFrames[frameIndex];
            if (leftFrame.Position.Count != rightFrame.Position.Count ||
                leftFrame.Rotation.Count != rightFrame.Rotation.Count ||
                leftFrame.Scale.Count != rightFrame.Scale.Count)
            {
                return false;
            }

            for (var boneIndex = 0;
                 boneIndex < leftFrame.Position.Count;
                 boneIndex++)
            {
                if (leftFrame.Position[boneIndex] != rightFrame.Position[boneIndex] ||
                    leftFrame.Scale[boneIndex] != rightFrame.Scale[boneIndex] ||
                    Math.Abs(Quaternion.Dot(
                        leftFrame.Rotation[boneIndex],
                        rightFrame.Rotation[boneIndex])) < 0.999999f)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private sealed record DocumentHistoryEntry(
        AnimationClip Before,
        AnimationClip After);
}
