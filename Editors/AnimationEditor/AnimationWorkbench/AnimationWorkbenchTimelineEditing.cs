using System.ComponentModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchTimelineDisplayMode
{
    EditingAnchors,
    AllSamples,
}

public enum AnimationWorkbenchSplitPart
{
    Leading,
    Trailing,
}

public readonly record struct AnimationWorkbenchFrameRange(
    int StartFrame,
    int EndFrameExclusive)
{
    public int Length => EndFrameExclusive - StartFrame;
}

public sealed class AnimationWorkbenchTimelineSnapshot
{
    internal AnimationWorkbenchTimelineSnapshot(
        int frameCount,
        TimeSpan duration,
        IReadOnlyList<int> editingAnchorFrames,
        IReadOnlyList<string> boneNames)
    {
        FrameCount = frameCount;
        Duration = duration;
        EditingAnchorFrames = editingAnchorFrames.ToArray();
        BoneNames = boneNames.ToArray();
    }

    public int FrameCount { get; }

    public TimeSpan Duration { get; }

    public double FramesPerSecond =>
        Duration <= TimeSpan.Zero ? 0 : FrameCount / Duration.TotalSeconds;

    public IReadOnlyList<int> EditingAnchorFrames { get; }

    public IReadOnlyList<string> BoneNames { get; }
}

public sealed record AnimationWorkbenchTimelineEditResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    AnimationWorkbenchTimelineSnapshot Timeline,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed partial class AnimationWorkbenchDocument
{
    private const int MaximumTimelineFrameCount = 1_000_000;

    private SortedSet<int> _editingAnchorFrames = [];
    private SourceSnapshot? _timelinePreviewResult;
    private AnimationClip? _timelinePreviewStart;
    private SortedSet<int>? _timelinePreviewStartAnchors;
    private SortedSet<int>? _timelinePreviewAnchors;

    public AnimationWorkbenchTimelineSnapshot GetTimelineSnapshot()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        var source = _timelinePreviewResult ?? _result;
        var anchors = _timelinePreviewAnchors ?? _editingAnchorFrames;
        return new AnimationWorkbenchTimelineSnapshot(
            source?.Animation.DynamicFrames.Count ?? 0,
            source?.Animation.Duration ?? TimeSpan.Zero,
            NormalizeEditingAnchors(
                anchors,
                source?.Animation.DynamicFrames.Count ?? 0),
            _targetSkeleton?.BoneNames ?? []);
    }

    public AnimationWorkbenchTimelineEditResult BeginTimelinePreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        var activePreview = GetActiveEditPreviewDiagnostic();
        if (activePreview != null)
        {
            return CreateTimelineFailure(activePreview.Value);
        }

        if (TryGetEditContext(
                out var animation,
                out _,
                out var diagnostic) == false)
        {
            return CreateTimelineFailure(diagnostic!);
        }

        _timelinePreviewStart = animation.Clone();
        _timelinePreviewResult = _result!.Clone();
        _timelinePreviewStartAnchors = new SortedSet<int>(
            _editingAnchorFrames);
        _timelinePreviewAnchors = new SortedSet<int>(
            _editingAnchorFrames);
        RefreshSelectedResultPreview();
        return CreateTimelineSuccess();
    }

    public AnimationWorkbenchTimelineEditResult PreviewMoveFrames(
        IReadOnlyCollection<int>? frameIndices,
        int frameDelta)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out _,
                out var anchors,
                out var failure))
        {
            var selected = frameIndices?
                .Distinct()
                .OrderBy(index => index)
                .ToArray() ?? [];
            if (selected.Length == 0 ||
                selected[0] < 0 ||
                selected[^1] >= animation.DynamicFrames.Count ||
                selected[^1] - selected[0] + 1 != selected.Length)
            {
                return CreateTimelineFailure(
                    AnimationWorkbenchDiagnosticCode.TimelineSelectionInvalid);
            }

            var range = new AnimationWorkbenchFrameRange(
                selected[0],
                selected[^1] + 1);
            var targetStart = range.StartFrame + frameDelta;
            if (targetStart < 0 ||
                targetStart + range.Length > animation.DynamicFrames.Count)
            {
                return CreateTimelineFailure(
                    AnimationWorkbenchDiagnosticCode.TimelineMoveInvalid,
                    animation.DynamicFrames.Count,
                    targetStart);
            }

            if (frameDelta == 0)
                return SetTimelinePreview(animation, anchors);

            var order = Enumerable.Range(
                0,
                animation.DynamicFrames.Count).ToList();
            var moved = order.GetRange(range.StartFrame, range.Length);
            order.RemoveRange(range.StartFrame, range.Length);
            order.InsertRange(targetStart, moved);

            var next = animation.Clone();
            next.DynamicFrames = order
                .Select(index => animation.DynamicFrames[index].Clone())
                .ToList();
            var newIndexByOldIndex = order
                .Select((oldIndex, newIndex) => (oldIndex, newIndex))
                .ToDictionary(pair => pair.oldIndex, pair => pair.newIndex);
            var nextAnchors = new SortedSet<int>(anchors.Select(
                anchor => newIndexByOldIndex[anchor]));
            for (var index = targetStart;
                 index < targetStart + range.Length;
                 index++)
            {
                nextAnchors.Add(index);
            }

            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult PreviewTrimRange(
        AnimationWorkbenchFrameRange range)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out _,
                out var anchors,
                out var failure))
        {
            if (TryValidateRange(animation, range, out var diagnostic) == false)
                return CreateTimelineFailure(diagnostic!);

            var next = new AnimationClip
            {
                Duration = ScaleDurationForFrameCount(
                    animation.Duration,
                    animation.DynamicFrames.Count,
                    range.Length),
                DynamicFrames = animation.DynamicFrames
                    .Skip(range.StartFrame)
                    .Take(range.Length)
                    .Select(frame => frame.Clone())
                    .ToList(),
            };
            var nextAnchors = anchors
                .Where(anchor =>
                    anchor >= range.StartFrame &&
                    anchor < range.EndFrameExclusive)
                .Select(anchor => anchor - range.StartFrame)
                .ToArray();
            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult PreviewSplitAt(
        int splitFrame,
        AnimationWorkbenchSplitPart part)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out _,
                out _,
                out var failure) == false)
        {
            return failure!;
        }

        if (splitFrame <= 0 || splitFrame >= animation.DynamicFrames.Count)
        {
            return CreateTimelineFailure(
                AnimationWorkbenchDiagnosticCode.TimelineRangeInvalid,
                animation.DynamicFrames.Count,
                splitFrame);
        }

        return PreviewTrimRange(part switch
        {
            AnimationWorkbenchSplitPart.Leading =>
                new AnimationWorkbenchFrameRange(0, splitFrame),
            AnimationWorkbenchSplitPart.Trailing =>
                new AnimationWorkbenchFrameRange(
                    splitFrame,
                    animation.DynamicFrames.Count),
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        });
    }

    public AnimationWorkbenchTimelineEditResult PreviewReverseRange(
        AnimationWorkbenchFrameRange range)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out _,
                out var anchors,
                out var failure))
        {
            if (TryValidateRange(animation, range, out var diagnostic) == false)
                return CreateTimelineFailure(diagnostic!);

            var next = animation.Clone();
            next.DynamicFrames.Reverse(range.StartFrame, range.Length);
            var nextAnchors = anchors.Select(anchor =>
                anchor >= range.StartFrame &&
                anchor < range.EndFrameExclusive
                    ? range.StartFrame +
                      range.EndFrameExclusive - 1 - anchor
                    : anchor).ToArray();
            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult PreviewLoopRange(
        AnimationWorkbenchFrameRange range,
        int repeatCount)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out _,
                out var anchors,
                out var failure))
        {
            if (TryValidateRange(animation, range, out var diagnostic) == false)
                return CreateTimelineFailure(diagnostic!);
            if (repeatCount < 1)
            {
                return CreateTimelineFailure(
                    AnimationWorkbenchDiagnosticCode.TimelineLoopInvalid,
                    actualValue: repeatCount);
            }

            var replacementFrameCount = (long)range.Length * repeatCount;
            var nextFrameCount = animation.DynamicFrames.Count -
                range.Length + replacementFrameCount;
            if (nextFrameCount > MaximumTimelineFrameCount)
            {
                return CreateTimelineFailure(
                    AnimationWorkbenchDiagnosticCode.TimelineLoopInvalid,
                    MaximumTimelineFrameCount,
                    nextFrameCount > int.MaxValue
                        ? int.MaxValue
                        : (int)nextFrameCount);
            }

            var next = animation.Clone();
            next.DynamicFrames.RemoveRange(range.StartFrame, range.Length);
            var repeatedFrames = Enumerable.Range(0, repeatCount)
                .SelectMany(_ => animation.DynamicFrames
                    .Skip(range.StartFrame)
                    .Take(range.Length))
                .Select(frame => frame.Clone())
                .ToArray();
            next.DynamicFrames.InsertRange(range.StartFrame, repeatedFrames);
            next.Duration = ScaleDurationForFrameCount(
                animation.Duration,
                animation.DynamicFrames.Count,
                next.DynamicFrames.Count);

            var addedFrameCount = range.Length * (repeatCount - 1);
            var nextAnchors = new SortedSet<int>();
            foreach (var anchor in anchors)
            {
                if (anchor < range.StartFrame)
                {
                    nextAnchors.Add(anchor);
                }
                else if (anchor >= range.EndFrameExclusive)
                {
                    nextAnchors.Add(anchor + addedFrameCount);
                }
                else
                {
                    for (var repetition = 0;
                         repetition < repeatCount;
                         repetition++)
                    {
                        nextAnchors.Add(
                            range.StartFrame +
                            repetition * range.Length +
                            anchor - range.StartFrame);
                    }
                }
            }

            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult PreviewStretchRange(
        AnimationWorkbenchFrameRange range,
        int targetFrameCount)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out var skeleton,
                out var anchors,
                out var failure))
        {
            if (TryValidateRange(animation, range, out var diagnostic) == false)
                return CreateTimelineFailure(diagnostic!);
            var nextFrameCount = (long)animation.DynamicFrames.Count -
                range.Length + targetFrameCount;
            if (targetFrameCount < 1 ||
                nextFrameCount > MaximumTimelineFrameCount)
            {
                return CreateTimelineFailure(
                    AnimationWorkbenchDiagnosticCode.TimelineStretchInvalid,
                    MaximumTimelineFrameCount,
                    targetFrameCount);
            }

            var replacement = new List<AnimationClip.KeyFrame>(
                targetFrameCount);
            for (var targetIndex = 0;
                 targetIndex < targetFrameCount;
                 targetIndex++)
            {
                var sourcePosition = (double)targetIndex * range.Length /
                    targetFrameCount;
                var lowerOffset = (int)Math.Floor(sourcePosition);
                var upperOffset = Math.Min(
                    range.Length - 1,
                    lowerOffset + 1);
                replacement.Add(InterpolateFrame(
                    animation.DynamicFrames[range.StartFrame + lowerOffset],
                    animation.DynamicFrames[range.StartFrame + upperOffset],
                    (float)(sourcePosition - lowerOffset),
                    skeleton.BoneCount));
            }

            var next = animation.Clone();
            next.DynamicFrames.RemoveRange(range.StartFrame, range.Length);
            next.DynamicFrames.InsertRange(range.StartFrame, replacement);
            next.Duration = ScaleDurationForFrameCount(
                animation.Duration,
                animation.DynamicFrames.Count,
                next.DynamicFrames.Count);

            var frameCountDelta = targetFrameCount - range.Length;
            var nextAnchors = anchors.Select(anchor =>
            {
                if (anchor < range.StartFrame)
                    return anchor;
                if (anchor >= range.EndFrameExclusive)
                    return anchor + frameCountDelta;
                if (range.Length == 1 || targetFrameCount == 1)
                    return range.StartFrame;
                var mappedOffset = (int)Math.Round(
                    (double)(anchor - range.StartFrame) * targetFrameCount /
                    range.Length,
                    MidpointRounding.AwayFromZero);
                return range.StartFrame + Math.Min(
                    targetFrameCount - 1,
                    mappedOffset);
            }).ToArray();
            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult PreviewInterpolateRange(
        AnimationWorkbenchFrameRange range)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out var skeleton,
                out var anchors,
                out var failure))
        {
            if (TryValidateRange(animation, range, out var diagnostic) == false)
                return CreateTimelineFailure(diagnostic!);
            if (range.Length < 2)
            {
                return CreateTimelineFailure(
                    AnimationWorkbenchDiagnosticCode.TimelineRangeInvalid,
                    expectedValue: 2,
                    actualValue: range.Length);
            }

            var next = animation.Clone();
            var first = animation.DynamicFrames[range.StartFrame];
            var last = animation.DynamicFrames[range.EndFrameExclusive - 1];
            for (var index = 1; index < range.Length - 1; index++)
            {
                next.DynamicFrames[range.StartFrame + index] =
                    InterpolateFrame(
                        first,
                        last,
                        (float)index / (range.Length - 1),
                        skeleton.BoneCount);
            }

            var nextAnchors = new SortedSet<int>(anchors)
            {
                range.StartFrame,
                range.EndFrameExclusive - 1,
            };
            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult PreviewMirrorRange(
        AnimationWorkbenchFrameRange range)
    {
        if (TryGetTimelinePreviewContext(
                out var animation,
                out var skeleton,
                out var anchors,
                out var failure))
        {
            if (TryValidateRange(animation, range, out var diagnostic) == false)
                return CreateTimelineFailure(diagnostic!);

            var mirrorIndexes = CreateMirrorIndexes(skeleton.BoneNames);
            var next = animation.Clone();
            for (var frameIndex = range.StartFrame;
                 frameIndex < range.EndFrameExclusive;
                 frameIndex++)
            {
                var source = animation.DynamicFrames[frameIndex];
                var target = next.DynamicFrames[frameIndex];
                for (var boneIndex = 0;
                     boneIndex < skeleton.BoneCount;
                     boneIndex++)
                {
                    var sourceBoneIndex = mirrorIndexes[boneIndex];
                    if (sourceBoneIndex < 0)
                        continue;
                    target.Position[boneIndex] = MirrorPosition(
                        source.Position[sourceBoneIndex]);
                    target.Rotation[boneIndex] = MirrorRotation(
                        source.Rotation[sourceBoneIndex]);
                    target.Scale[boneIndex] = source.Scale[sourceBoneIndex];
                }
            }

            var nextAnchors = new SortedSet<int>(anchors);
            for (var frameIndex = range.StartFrame;
                 frameIndex < range.EndFrameExclusive;
                 frameIndex++)
            {
                nextAnchors.Add(frameIndex);
            }

            return SetTimelinePreview(next, nextAnchors);
        }

        return failure!;
    }

    public AnimationWorkbenchTimelineEditResult CommitTimelinePreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_timelinePreviewResult == null ||
            _timelinePreviewStart == null ||
            _timelinePreviewAnchors == null)
        {
            return CreateTimelineFailure(
                AnimationWorkbenchDiagnosticCode.TimelinePreviewMissing);
        }

        var next = _timelinePreviewResult.Animation.Clone();
        var previous = _timelinePreviewStart;
        var nextAnchors = _timelinePreviewAnchors.ToArray();
        ClearTimelinePreview();
        if (AnimationsEqual(previous, next))
        {
            SetEditingAnchors(nextAnchors);
            RefreshSelectedResultPreview();
            return CreateTimelineSuccess();
        }

        CommitResultAnimation(next, nextAnchors);
        return CreateTimelineSuccess();
    }

    public AnimationWorkbenchTimelineEditResult CancelTimelinePreview()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_timelinePreviewResult == null)
        {
            return CreateTimelineFailure(
                AnimationWorkbenchDiagnosticCode.TimelinePreviewMissing);
        }

        ClearTimelinePreview();
        RefreshSelectedResultPreview();
        return CreateTimelineSuccess();
    }

    private bool TryGetTimelinePreviewContext(
        out AnimationClip animation,
        out GameSkeleton skeleton,
        out SortedSet<int> anchors,
        out AnimationWorkbenchTimelineEditResult? failure)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (_timelinePreviewStart == null ||
            _timelinePreviewResult == null ||
            _timelinePreviewStartAnchors == null)
        {
            animation = null!;
            skeleton = null!;
            anchors = null!;
            failure = CreateTimelineFailure(
                AnimationWorkbenchDiagnosticCode.TimelinePreviewMissing);
            return false;
        }

        if (_targetSkeleton == null)
        {
            animation = null!;
            skeleton = null!;
            anchors = null!;
            failure = CreateTimelineFailure(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing);
            return false;
        }

        animation = _timelinePreviewStart;
        skeleton = _targetSkeleton;
        anchors = new SortedSet<int>(_timelinePreviewStartAnchors);
        failure = null;
        return true;
    }

    private AnimationWorkbenchTimelineEditResult SetTimelinePreview(
        AnimationClip animation,
        IEnumerable<int> anchors)
    {
        _timelinePreviewResult = _result!.WithPreviewAnimation(animation);
        _timelinePreviewAnchors = new SortedSet<int>(
            NormalizeEditingAnchors(
                anchors,
                animation.DynamicFrames.Count));
        RefreshSelectedResultPreview();
        return CreateTimelineSuccess();
    }

    private AnimationWorkbenchTimelineEditResult CreateTimelineSuccess() =>
        new(
            true,
            CreateState(),
            GetTimelineSnapshot(),
            Array.Empty<AnimationWorkbenchDiagnostic>());

    private AnimationWorkbenchTimelineEditResult CreateTimelineFailure(
        AnimationWorkbenchDiagnosticCode code,
        int? expectedValue = null,
        int? actualValue = null) => CreateTimelineFailure(
            new AnimationWorkbenchDiagnostic(
                code,
                AnimationWorkbenchDiagnosticSeverity.Error,
                ExpectedValue: expectedValue,
                ActualValue: actualValue));

    private AnimationWorkbenchTimelineEditResult CreateTimelineFailure(
        AnimationWorkbenchDiagnostic diagnostic) => new(
            false,
            CreateState(),
            GetTimelineSnapshot(),
            [diagnostic]);

    private static bool TryValidateRange(
        AnimationClip animation,
        AnimationWorkbenchFrameRange range,
        out AnimationWorkbenchDiagnostic? diagnostic)
    {
        if (range.StartFrame < 0 ||
            range.EndFrameExclusive > animation.DynamicFrames.Count ||
            range.StartFrame >= range.EndFrameExclusive)
        {
            diagnostic = new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.TimelineRangeInvalid,
                AnimationWorkbenchDiagnosticSeverity.Error,
                ExpectedValue: animation.DynamicFrames.Count,
                ActualValue: range.EndFrameExclusive);
            return false;
        }

        diagnostic = null;
        return true;
    }

    private static AnimationClip.KeyFrame InterpolateFrame(
        AnimationClip.KeyFrame first,
        AnimationClip.KeyFrame second,
        float amount,
        int boneCount)
    {
        var frame = new AnimationClip.KeyFrame();
        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            frame.Position.Add(Vector3.Lerp(
                first.Position[boneIndex],
                second.Position[boneIndex],
                amount));
            frame.Rotation.Add(Quaternion.Slerp(
                first.Rotation[boneIndex],
                second.Rotation[boneIndex],
                amount));
            frame.Scale.Add(Vector3.Lerp(
                first.Scale[boneIndex],
                second.Scale[boneIndex],
                amount));
        }

        return frame;
    }

    private static int[] CreateMirrorIndexes(
        IReadOnlyList<string> boneNames)
    {
        var indexes = boneNames
            .Select((name, index) => (name, index))
            .ToDictionary(
                pair => pair.name,
                pair => pair.index,
                StringComparer.OrdinalIgnoreCase);
        var result = new int[boneNames.Count];
        for (var boneIndex = 0;
             boneIndex < boneNames.Count;
             boneIndex++)
        {
            var counterpart = GetMirrorCounterpart(boneNames[boneIndex]);
            if (counterpart == null)
            {
                result[boneIndex] = boneIndex;
            }
            else if (indexes.TryGetValue(counterpart, out var counterpartIndex))
            {
                result[boneIndex] = counterpartIndex;
            }
            else
            {
                result[boneIndex] = -1;
            }
        }

        return result;
    }

    private static string? GetMirrorCounterpart(string boneName)
    {
        return TrySwapSideToken(
                boneName,
                "left",
                "right",
                requireEndBoundary: false) ??
            TrySwapSideToken(
                boneName,
                "right",
                "left",
                requireEndBoundary: false) ??
            TrySwapSideToken(
                boneName,
                "l",
                "r",
                requireEndBoundary: true) ??
            TrySwapSideToken(
                boneName,
                "r",
                "l",
                requireEndBoundary: true);
    }

    private static string? TrySwapSideToken(
        string boneName,
        string sourceToken,
        string targetToken,
        bool requireEndBoundary)
    {
        var searchStart = 0;
        while (searchStart < boneName.Length)
        {
            var tokenIndex = boneName.IndexOf(
                sourceToken,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
                return null;

            var tokenEnd = tokenIndex + sourceToken.Length;
            var startsAtBoundary = tokenIndex == 0 ||
                IsBoneNameSeparator(boneName[tokenIndex - 1]);
            var endsAtBoundary = tokenEnd == boneName.Length ||
                IsBoneNameSeparator(boneName[tokenEnd]);
            if (startsAtBoundary &&
                (requireEndBoundary == false || endsAtBoundary))
            {
                return boneName[..tokenIndex] +
                    MatchTokenCase(
                        boneName.AsSpan(tokenIndex, sourceToken.Length),
                        targetToken) +
                    boneName[tokenEnd..];
            }

            searchStart = tokenIndex + sourceToken.Length;
        }

        return null;
    }

    private static string MatchTokenCase(
        ReadOnlySpan<char> sourceToken,
        string targetToken)
    {
        if (sourceToken.ToString().All(char.IsUpper))
            return targetToken.ToUpperInvariant();
        if (char.IsUpper(sourceToken[0]))
        {
            return char.ToUpperInvariant(targetToken[0]) + targetToken[1..];
        }

        return targetToken;
    }

    private static bool IsBoneNameSeparator(char value) =>
        value is '_' or '.' or '-';

    private static Vector3 MirrorPosition(Vector3 value) =>
        new(-value.X, value.Y, value.Z);

    private static Quaternion MirrorRotation(Quaternion value)
    {
        var mirrored = new Quaternion(
            value.X,
            -value.Y,
            -value.Z,
            value.W);
        mirrored.Normalize();
        return mirrored;
    }

    private SortedSet<int> AddEditingAnchor(int frameIndex)
    {
        var anchors = new SortedSet<int>(_editingAnchorFrames);
        anchors.Add(frameIndex);
        return anchors;
    }

    private SortedSet<int> ShiftAnchorsForInsertion(int insertionIndex)
    {
        return new SortedSet<int>(_editingAnchorFrames.Select(anchor =>
            anchor >= insertionIndex ? anchor + 1 : anchor));
    }

    private SortedSet<int> ShiftAnchorsForDeletion(int deletedFrameIndex)
    {
        return new SortedSet<int>(_editingAnchorFrames
            .Where(anchor => anchor != deletedFrameIndex)
            .Select(anchor =>
                anchor > deletedFrameIndex ? anchor - 1 : anchor));
    }

    private void ResetEditingAnchors()
    {
        _editingAnchorFrames.Clear();
        var frameCount = _result?.Animation.DynamicFrames.Count ?? 0;
        if (frameCount == 0)
            return;
        _editingAnchorFrames.Add(0);
        _editingAnchorFrames.Add(frameCount - 1);
    }

    private void SetEditingAnchors(IEnumerable<int> anchors)
    {
        _editingAnchorFrames = new SortedSet<int>(
            NormalizeEditingAnchors(
                anchors,
                _result?.Animation.DynamicFrames.Count ?? 0));
    }

    private static int[] NormalizeEditingAnchors(
        IEnumerable<int> anchors,
        int frameCount)
    {
        if (frameCount <= 0)
            return [];
        var normalized = new SortedSet<int>(anchors.Where(
            anchor => anchor >= 0 && anchor < frameCount))
        {
            0,
            frameCount - 1,
        };
        return normalized.ToArray();
    }

    private void ClearTimelinePreview()
    {
        _timelinePreviewResult = null;
        _timelinePreviewStart = null;
        _timelinePreviewStartAnchors = null;
        _timelinePreviewAnchors = null;
    }
}

public sealed record AnimationWorkbenchTimelineTrackRow(string BoneName);

public sealed class AnimationWorkbenchTimelineController :
    INotifyPropertyChanged
{
    private const double BasePixelsPerFrame = 12;
    private const double MinimumZoom = 0.25;
    private const double MaximumZoom = 8;

    private readonly AnimationWorkbenchDocument _document;
    private readonly SortedSet<int> _selectedFrames = [];
    private AnimationWorkbenchTimelineSnapshot _timeline;
    private AnimationWorkbenchTimelineDisplayMode _displayMode =
        AnimationWorkbenchTimelineDisplayMode.EditingAnchors;
    private double _zoom = 1;
    private double _viewportWidth;
    private double _horizontalOffset;
    private int _focusedFrameIndex = -1;
    private int _selectionAnchorIndex = -1;
    private int[]? _moveSourceFrames;
    private int _moveSourceStart;
    private int _lastMovePreviewDelta = int.MinValue;
    private AnimationWorkbenchTimelineEditResult? _lastMovePreviewResult;
    private int _lastRenderedMovePreviewDelta = int.MinValue;
    private int _pendingMovePreviewDelta;
    private SelectionSnapshot? _previewSelectionStart;
    private readonly Dictionary<long, SelectionSnapshot>
        _selectionByHistoryRevision = [];
    private long _observedHistoryRevision;
    private long _observedDocumentGeneration;
    private IReadOnlyList<AnimationWorkbenchTimelineTrackRow> _tracks = [];
    private IReadOnlyList<int> _visibleFrameIndices = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public AnimationWorkbenchTimelineController(
        AnimationWorkbenchDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _timeline = document.GetTimelineSnapshot();
        var state = document.GetState();
        _observedHistoryRevision = state.HistoryRevision;
        _observedDocumentGeneration = state.DocumentGeneration;
        RememberSelectionForCurrentRevision();
        RefreshTracks();
        RefreshVisibleFrames();
    }

    public AnimationWorkbenchTimelineDisplayMode DisplayMode => _displayMode;

    public double Zoom => _zoom;

    public double PixelsPerFrame => BasePixelsPerFrame * _zoom;

    public double HorizontalOffset => _horizontalOffset;

    public int FocusedFrameIndex => _focusedFrameIndex;

    public AnimationWorkbenchTimelineSnapshot Timeline => _timeline;

    public IReadOnlyList<int> VisibleFrameIndices => _visibleFrameIndices;

    public IReadOnlyList<int> SelectedFrameIndices => _selectedFrames.ToArray();

    public bool IsFrameSelected(int frameIndex) =>
        _selectedFrames.Contains(frameIndex);

    public IReadOnlyList<AnimationWorkbenchTimelineTrackRow> Tracks => _tracks;

    public int SelectionCount => _selectedFrames.Count;

    public AnimationWorkbenchFrameRange? SelectedRange =>
        _selectedFrames.Count == 0
            ? null
            : new AnimationWorkbenchFrameRange(
                _selectedFrames.Min,
                _selectedFrames.Max + 1);

    public bool HasActivePreview =>
        _document.GetState().HasActiveTimelinePreview;

    public bool CanUndo => _document.GetState().CanUndo;

    public bool CanRedo => _document.GetState().CanRedo;

    public void SetViewport(double width, double horizontalOffset)
    {
        if (double.IsFinite(width) == false || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (double.IsFinite(horizontalOffset) == false)
            throw new ArgumentOutOfRangeException(nameof(horizontalOffset));
        _viewportWidth = width;
        _horizontalOffset = ClampHorizontalOffset(horizontalOffset);
        RefreshVisibleFrames();
        NotifyChanged();
    }

    public void SetDisplayMode(
        AnimationWorkbenchTimelineDisplayMode displayMode)
    {
        _displayMode = displayMode;
        RefreshVisibleFrames();
        NotifyChanged();
    }

    public void ZoomAt(double zoom, double viewportX)
    {
        if (double.IsFinite(zoom) == false)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        if (double.IsFinite(viewportX) == false)
            throw new ArgumentOutOfRangeException(nameof(viewportX));
        var framePosition = FramePositionFromViewportX(viewportX);
        _zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        _horizontalOffset = ClampHorizontalOffset(
            framePosition * PixelsPerFrame - viewportX);
        RefreshVisibleFrames();
        NotifyChanged();
    }

    public double FramePositionFromViewportX(double viewportX)
    {
        if (_timeline.FrameCount == 0)
            return 0;
        return Math.Clamp(
            (_horizontalOffset + viewportX) / PixelsPerFrame,
            0,
            _timeline.FrameCount - 1);
    }

    public int SnapFrameFromViewportX(double viewportX)
    {
        if (_timeline.FrameCount == 0)
            return 0;
        return Math.Clamp(
            (int)Math.Floor(
                (_horizontalOffset + viewportX) / PixelsPerFrame),
            0,
            _timeline.FrameCount - 1);
    }

    public void SelectFrame(int frameIndex, bool extendRange, bool toggle)
    {
        ValidateFrameIndex(frameIndex);
        if (extendRange && _selectionAnchorIndex >= 0)
        {
            if (toggle == false)
                _selectedFrames.Clear();
            AddSelectionRange(_selectionAnchorIndex, frameIndex);
        }
        else if (toggle)
        {
            if (_selectedFrames.Remove(frameIndex) == false)
                _selectedFrames.Add(frameIndex);
            _selectionAnchorIndex = frameIndex;
        }
        else
        {
            _selectedFrames.Clear();
            _selectedFrames.Add(frameIndex);
            _selectionAnchorIndex = frameIndex;
        }

        _focusedFrameIndex = frameIndex;
        RememberSelectionForCurrentRevision();
        NotifyChanged();
    }

    public void BoxSelect(
        double startViewportX,
        double endViewportX,
        bool extendSelection)
    {
        var startFrame = SnapFrameFromViewportX(Math.Min(
            startViewportX,
            endViewportX));
        var endFrame = SnapFrameFromViewportX(Math.Max(
            startViewportX,
            endViewportX));
        if (extendSelection == false)
            _selectedFrames.Clear();
        AddSelectionRange(startFrame, endFrame);
        _selectionAnchorIndex = startFrame;
        _focusedFrameIndex = endFrame;
        RememberSelectionForCurrentRevision();
        NotifyChanged();
    }

    public void NavigateSelection(int frameDelta, bool extendRange)
    {
        if (_timeline.FrameCount == 0)
            return;
        var current = _focusedFrameIndex < 0 ? 0 : _focusedFrameIndex;
        var next = Math.Clamp(
            current + frameDelta,
            0,
            _timeline.FrameCount - 1);
        if (extendRange)
        {
            if (_selectionAnchorIndex < 0)
                _selectionAnchorIndex = current;
            _selectedFrames.Clear();
            AddSelectionRange(_selectionAnchorIndex, next);
        }
        else
        {
            _selectedFrames.Clear();
            _selectedFrames.Add(next);
            _selectionAnchorIndex = next;
        }

        _focusedFrameIndex = next;
        RememberSelectionForCurrentRevision();
        NotifyChanged();
    }

    public AnimationWorkbenchTimelineEditResult BeginMoveSelection()
    {
        _previewSelectionStart = CaptureSelection();
        _moveSourceFrames = _selectedFrames.ToArray();
        _moveSourceStart = _moveSourceFrames.Length == 0
            ? -1
            : _moveSourceFrames[0];
        var result = _document.BeginTimelinePreview();
        if (result.Succeeded == false)
            _previewSelectionStart = null;
        _lastMovePreviewDelta = int.MinValue;
        _lastRenderedMovePreviewDelta = int.MinValue;
        _pendingMovePreviewDelta = 0;
        _lastMovePreviewResult = null;
        Refresh();
        return result;
    }

    public AnimationWorkbenchTimelineEditResult PreviewMoveSelectionByPixels(
        double horizontalPixels)
    {
        var frameDelta = (int)Math.Round(
            horizontalPixels / PixelsPerFrame,
            MidpointRounding.AwayFromZero);
        if (_moveSourceFrames is { Length: > 0 })
        {
            frameDelta = Math.Clamp(
                frameDelta,
                -_moveSourceFrames[0],
                _timeline.FrameCount - 1 - _moveSourceFrames[^1]);
        }
        if (frameDelta == _lastMovePreviewDelta &&
            _lastMovePreviewResult != null)
        {
            return _lastMovePreviewResult;
        }
        _pendingMovePreviewDelta = frameDelta;
        _lastMovePreviewDelta = frameDelta;
        UpdateMoveSelection(frameDelta);

        var previewStride = Math.Max(1, _timeline.FrameCount / 40);
        if (_lastMovePreviewResult != null &&
            Math.Abs(frameDelta - _lastRenderedMovePreviewDelta) <
            previewStride)
        {
            NotifyChanged();
            return _lastMovePreviewResult;
        }

        var result = ApplyMovePreview(frameDelta);
        if (result.Succeeded)
        {
            Refresh();
        }

        return result;
    }

    private AnimationWorkbenchTimelineEditResult ApplyMovePreview(
        int frameDelta)
    {
        var result = _document.PreviewMoveFrames(
            _moveSourceFrames,
            frameDelta);
        _lastRenderedMovePreviewDelta = frameDelta;
        _lastMovePreviewResult = result;
        return result;
    }

    private void UpdateMoveSelection(int frameDelta)
    {
        if (_moveSourceFrames == null)
            return;
        _selectedFrames.Clear();
        for (var index = 0; index < _moveSourceFrames.Length; index++)
            _selectedFrames.Add(_moveSourceStart + frameDelta + index);
        _focusedFrameIndex = _selectedFrames.Count == 0
            ? -1
            : _selectedFrames.Max;
    }

    public AnimationWorkbenchTimelineEditResult CommitMoveSelection()
    {
        if (_pendingMovePreviewDelta != _lastRenderedMovePreviewDelta)
        {
            var finalPreview = ApplyMovePreview(_pendingMovePreviewDelta);
            if (finalPreview.Succeeded == false)
                return finalPreview;
        }
        var result = _document.CommitTimelinePreview();
        _moveSourceFrames = null;
        _lastMovePreviewResult = null;
        _lastRenderedMovePreviewDelta = int.MinValue;
        _previewSelectionStart = null;
        Refresh();
        if (result.Succeeded)
            RememberSelectionForCurrentRevision();
        return result;
    }

    public AnimationWorkbenchTimelineEditResult CancelMoveSelection()
    {
        var sourceSelection = _previewSelectionStart;
        var result = _document.CancelTimelinePreview();
        _moveSourceFrames = null;
        _lastMovePreviewResult = null;
        _lastRenderedMovePreviewDelta = int.MinValue;
        _previewSelectionStart = null;
        Refresh();
        if (result.Succeeded && sourceSelection != null)
            RestoreSelection(sourceSelection);
        return result;
    }

    public void Refresh()
    {
        _timeline = _document.GetTimelineSnapshot();
        RefreshTracks();
        var state = _document.GetState();
        if (state.DocumentGeneration != _observedDocumentGeneration)
        {
            _observedDocumentGeneration = state.DocumentGeneration;
            _selectionByHistoryRevision.Clear();
            _selectedFrames.Clear();
            _focusedFrameIndex = -1;
            _selectionAnchorIndex = -1;
            _previewSelectionStart = null;
        }
        var historyRevision = state.HistoryRevision;
        if (historyRevision != _observedHistoryRevision)
        {
            _observedHistoryRevision = historyRevision;
            if (_selectionByHistoryRevision.TryGetValue(
                    historyRevision,
                    out var selection))
            {
                RestoreSelection(selection, notify: false);
            }
        }
        _selectedFrames.RemoveWhere(index =>
            index < 0 || index >= _timeline.FrameCount);
        if (_focusedFrameIndex >= _timeline.FrameCount)
            _focusedFrameIndex = _timeline.FrameCount - 1;
        _horizontalOffset = ClampHorizontalOffset(_horizontalOffset);
        RefreshVisibleFrames();
        NotifyChanged();
    }

    public AnimationWorkbenchTimelineEditResult PreviewTrimSelection() =>
        BeginSelectedRangePreview(
            (document, range) => document.PreviewTrimRange(range),
            (_, timeline) => new AnimationWorkbenchFrameRange(
                0,
                timeline.FrameCount));

    public AnimationWorkbenchTimelineEditResult PreviewReverseSelection() =>
        BeginSelectedRangePreview(
            (document, range) => document.PreviewReverseRange(range),
            (range, _) => range);

    public AnimationWorkbenchTimelineEditResult PreviewLoopSelection(
        int repeatCount) => BeginSelectedRangePreview(
            (document, range) => document.PreviewLoopRange(range, repeatCount),
            (range, _) => new AnimationWorkbenchFrameRange(
                range.StartFrame,
                range.StartFrame + range.Length * repeatCount));

    public AnimationWorkbenchTimelineEditResult PreviewStretchSelection(
        int targetFrameCount) => BeginSelectedRangePreview(
            (document, range) => document.PreviewStretchRange(
                range,
                targetFrameCount),
            (range, _) => new AnimationWorkbenchFrameRange(
                range.StartFrame,
                range.StartFrame + targetFrameCount));

    public AnimationWorkbenchTimelineEditResult PreviewInterpolateSelection() =>
        BeginSelectedRangePreview(
            (document, range) => document.PreviewInterpolateRange(range),
            (range, _) => range);

    public AnimationWorkbenchTimelineEditResult PreviewMirrorSelection() =>
        BeginSelectedRangePreview(
            (document, range) => document.PreviewMirrorRange(range),
            (range, _) => range);

    public AnimationWorkbenchTimelineEditResult PreviewSplit(
        AnimationWorkbenchSplitPart part)
    {
        return BeginPreview(
            document => document.PreviewSplitAt(_focusedFrameIndex, part),
            timeline => new AnimationWorkbenchFrameRange(
                0,
                timeline.FrameCount));
    }

    public AnimationWorkbenchTimelineEditResult CommitPreview()
    {
        var result = _document.CommitTimelinePreview();
        _previewSelectionStart = null;
        Refresh();
        if (result.Succeeded)
            RememberSelectionForCurrentRevision();
        return result;
    }

    public AnimationWorkbenchTimelineEditResult CancelPreview()
    {
        var result = _document.CancelTimelinePreview();
        var sourceSelection = _previewSelectionStart;
        _previewSelectionStart = null;
        Refresh();
        if (result.Succeeded && sourceSelection != null)
            RestoreSelection(sourceSelection);
        return result;
    }

    public AnimationWorkbenchPoseEditResult Undo()
    {
        var result = _document.Undo();
        Refresh();
        return result;
    }

    public AnimationWorkbenchPoseEditResult Redo()
    {
        var result = _document.Redo();
        Refresh();
        return result;
    }

    private void RefreshVisibleFrames()
    {
        if (_timeline.FrameCount == 0 || _viewportWidth <= 0)
        {
            _visibleFrameIndices = [];
            return;
        }

        var firstFrame = Math.Clamp(
            (int)Math.Floor(_horizontalOffset / PixelsPerFrame),
            0,
            _timeline.FrameCount - 1);
        var lastFrame = Math.Clamp(
            (int)Math.Ceiling(
                (_horizontalOffset + _viewportWidth) /
                PixelsPerFrame),
            firstFrame,
            _timeline.FrameCount - 1);
        _visibleFrameIndices = _displayMode ==
            AnimationWorkbenchTimelineDisplayMode.AllSamples
                ? Enumerable.Range(
                    firstFrame,
                    lastFrame - firstFrame + 1).ToArray()
                : _timeline.EditingAnchorFrames.Where(anchor =>
                    anchor >= firstFrame && anchor <= lastFrame).ToArray();
    }

    private void RefreshTracks()
    {
        if (_tracks.Select(track => track.BoneName)
            .SequenceEqual(_timeline.BoneNames))
        {
            return;
        }

        _tracks = _timeline.BoneNames
            .Select(name => new AnimationWorkbenchTimelineTrackRow(name))
            .ToArray();
    }

    private double ClampHorizontalOffset(double offset)
    {
        var contentWidth = Math.Max(
            0,
            _timeline.FrameCount * PixelsPerFrame);
        return Math.Clamp(
            offset,
            0,
            Math.Max(0, contentWidth - _viewportWidth));
    }

    private void AddSelectionRange(int firstFrame, int secondFrame)
    {
        var startFrame = Math.Min(firstFrame, secondFrame);
        var endFrame = Math.Max(firstFrame, secondFrame);
        for (var frameIndex = startFrame;
             frameIndex <= endFrame;
             frameIndex++)
        {
            _selectedFrames.Add(frameIndex);
        }
    }

    private void ValidateFrameIndex(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _timeline.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
    }

    private AnimationWorkbenchTimelineEditResult BeginSelectedRangePreview(
        Func<AnimationWorkbenchDocument,
            AnimationWorkbenchFrameRange,
            AnimationWorkbenchTimelineEditResult> preview,
        Func<AnimationWorkbenchFrameRange,
            AnimationWorkbenchTimelineSnapshot,
            AnimationWorkbenchFrameRange> mapSelection)
    {
        var range = SelectedRange;
        if (range == null || range.Value.Length != _selectedFrames.Count)
            return CreateControllerFailure(
                AnimationWorkbenchDiagnosticCode.TimelineSelectionInvalid);
        return BeginPreview(
            document => preview(document, range.Value),
            timeline => mapSelection(range.Value, timeline));
    }

    private AnimationWorkbenchTimelineEditResult BeginPreview(
        Func<AnimationWorkbenchDocument,
            AnimationWorkbenchTimelineEditResult> preview,
        Func<AnimationWorkbenchTimelineSnapshot,
            AnimationWorkbenchFrameRange>? mapSelection = null)
    {
        var sourceSelection = CaptureSelection();
        var begin = _document.BeginTimelinePreview();
        if (begin.Succeeded == false)
            return begin;
        _previewSelectionStart = sourceSelection;
        var result = preview(_document);
        if (result.Succeeded == false)
        {
            _document.CancelTimelinePreview();
            _previewSelectionStart = null;
        }
        Refresh();
        if (result.Succeeded && mapSelection != null)
            SelectRange(mapSelection(result.Timeline), notify: true);
        else if (result.Succeeded == false)
            RestoreSelection(sourceSelection);
        return result;
    }

    private SelectionSnapshot CaptureSelection() => new(
        _selectedFrames.ToArray(),
        _focusedFrameIndex,
        _selectionAnchorIndex);

    private void RestoreSelection(
        SelectionSnapshot selection,
        bool notify = true)
    {
        _selectedFrames.Clear();
        foreach (var frameIndex in selection.FrameIndices.Where(index =>
                     index >= 0 && index < _timeline.FrameCount))
        {
            _selectedFrames.Add(frameIndex);
        }
        _focusedFrameIndex = Math.Clamp(
            selection.FocusedFrameIndex,
            -1,
            _timeline.FrameCount - 1);
        _selectionAnchorIndex = Math.Clamp(
            selection.SelectionAnchorIndex,
            -1,
            _timeline.FrameCount - 1);
        if (notify)
            NotifyChanged();
    }

    private void SelectRange(
        AnimationWorkbenchFrameRange range,
        bool notify)
    {
        _selectedFrames.Clear();
        var startFrame = Math.Clamp(
            range.StartFrame,
            0,
            Math.Max(0, _timeline.FrameCount - 1));
        var endFrameExclusive = Math.Clamp(
            range.EndFrameExclusive,
            startFrame,
            _timeline.FrameCount);
        for (var frameIndex = startFrame;
             frameIndex < endFrameExclusive;
             frameIndex++)
        {
            _selectedFrames.Add(frameIndex);
        }
        _selectionAnchorIndex = _selectedFrames.Count == 0
            ? -1
            : _selectedFrames.Min;
        _focusedFrameIndex = _selectedFrames.Count == 0
            ? -1
            : _selectedFrames.Max;
        if (notify)
            NotifyChanged();
    }

    private void RememberSelectionForCurrentRevision()
    {
        var state = _document.GetState();
        if (state.HasActiveTimelinePreview || state.HasActivePosePreview)
            return;
        _observedHistoryRevision = state.HistoryRevision;
        _selectionByHistoryRevision[state.HistoryRevision] = CaptureSelection();
    }

    private AnimationWorkbenchTimelineEditResult CreateControllerFailure(
        AnimationWorkbenchDiagnosticCode code) => new(
            false,
            _document.GetState(),
            _document.GetTimelineSnapshot(),
            [
                new AnimationWorkbenchDiagnostic(
                    code,
                    AnimationWorkbenchDiagnosticSeverity.Error),
            ]);

    private void NotifyChanged()
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(null));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record SelectionSnapshot(
        IReadOnlyList<int> FrameIndices,
        int FocusedFrameIndex,
        int SelectionAnchorIndex);
}
