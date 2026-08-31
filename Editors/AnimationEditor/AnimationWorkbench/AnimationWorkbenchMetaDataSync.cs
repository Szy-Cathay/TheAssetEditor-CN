using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchMetaDataKind
{
    AnimationA,
    AnimationB,
    Result,
}

public enum AnimationWorkbenchMetaDataProblemCode
{
    SynchronizationDisabled,
    UnknownPayloadUnmapped,
    SourceOutsideResult,
    InvalidTimeRange,
    Conflict,
    BoneUnmapped,
}

public sealed record AnimationWorkbenchMetaDataProblem(
    AnimationWorkbenchMetaDataProblemCode Code,
    AnimationWorkbenchDiagnosticSeverity Severity,
    AnimationWorkbenchSourceSlot? Source,
    int? SourceAttributeIndex,
    int? ResultAttributeIndex,
    float? SourceStartTime,
    float? SourceEndTime,
    float? ResultStartTime = null,
    float? ResultEndTime = null)
{
    public string ReasonKey => $"AnimationWorkbench.MetaData.Problem.{Code}";

    public bool HasNavigationLocation =>
        SourceAttributeIndex.HasValue ||
        ResultAttributeIndex.HasValue ||
        SourceStartTime.HasValue ||
        ResultStartTime.HasValue;
}

public sealed record AnimationWorkbenchMetaDataNavigationLocation(
    AnimationWorkbenchSourceSlot? Source,
    int? SourceAttributeIndex,
    int? ResultAttributeIndex,
    float? SourceTimeSeconds,
    float? ResultTimeSeconds);

public sealed record AnimationWorkbenchMetaDataNavigationResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    AnimationWorkbenchMetaDataNavigationLocation? Location);

public sealed class AnimationWorkbenchMetaDataSourceInput
{
    private readonly byte[] _bytes;

    public AnimationWorkbenchMetaDataSourceInput(string name, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(bytes);
        Name = name;
        _bytes = bytes.ToArray();
    }

    public string Name { get; }

    internal byte[] CopyBytes() => _bytes.ToArray();
}

public sealed class AnimationWorkbenchMetaDataSnapshot
{
    private readonly byte[] _bytes;

    internal AnimationWorkbenchMetaDataSnapshot(string name, byte[] bytes)
    {
        Name = name;
        _bytes = bytes.ToArray();
    }

    public string Name { get; }

    public ReadOnlyMemory<byte> Bytes => new(_bytes.ToArray());
}

public sealed partial class AnimationWorkbenchDocument
{
    private static readonly HashSet<string> s_metaDataBoneIndexProperties =
    [
        "BoneId",
        "NodeIndex",
        "BoneIndex",
        "GenericBoneIndex",
    ];

    private readonly MetaDataFileParser _metaDataParser = new(
        new MetaDataDatabase());
    private MetaDataDocumentSnapshot? _animationAMetaData;
    private MetaDataDocumentSnapshot? _animationBMetaData;
    private MetaDataDocumentSnapshot? _resultMetaData;
    private MetaDataDocumentSnapshot? _savedResultMetaData;
    private MetaDataDocumentSnapshot? _retargetedAnimationAMetaData;
    private MetaDataDocumentSnapshot? _retargetedAnimationBMetaData;
    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        _retargetedAnimationAMetaDataProblems = [];
    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        _retargetedAnimationBMetaDataProblems = [];
    private MetaDataDocumentSnapshot? _timelinePreviewMetaData;
    private MetaDataDocumentSnapshot? _blendPreviewMetaData;
    private MetaDataDocumentSnapshot? _layerPreviewMetaData;
    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        _metaDataProblems = [];
    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        _timelinePreviewMetaDataProblems = [];
    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        _blendPreviewMetaDataProblems = [];
    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        _layerPreviewMetaDataProblems = [];
    private bool _isMetaDataSynchronizationEnabled;
    private string? _projectMetaDataResourcePath;

    public AnimationWorkbenchMetaDataSnapshot? GetMetaDataSnapshot(
        AnimationWorkbenchMetaDataKind kind)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        var snapshot = kind switch
        {
            AnimationWorkbenchMetaDataKind.AnimationA => _animationAMetaData,
            AnimationWorkbenchMetaDataKind.AnimationB => _animationBMetaData,
            AnimationWorkbenchMetaDataKind.Result
                when _isMetaDataSynchronizationEnabled =>
                _blendPreviewMetaData ??
                _layerPreviewMetaData ??
                _timelinePreviewMetaData ??
                _resultMetaData,
            _ => null,
        };
        return snapshot?.CreatePublicSnapshot();
    }

    public AnimationWorkbenchSaveResult SaveMetaDataAsNewProjectResource(
        IPackFileService packFileService,
        FolderProjectContainer project,
        string resourcePath)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(packFileService);
        ArgumentNullException.ThrowIfNull(project);
        if (!_isMetaDataSynchronizationEnabled)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.MetaDataSynchronizationDisabled);
        }
        if (_resultMetaData == null)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.MetaDataResultMissing);
        }

        byte[] bytes;
        try
        {
            bytes = _resultMetaData.CopyBytes();
            var parsed = _metaDataParser.ParseFile(bytes);
            var roundTrip = _metaDataParser.GenerateBytes(
                parsed.Version,
                parsed);
            if (!bytes.AsSpan().SequenceEqual(roundTrip))
            {
                return CreateSaveFailure(
                    AnimationWorkbenchDiagnosticCode
                        .MetaDataCandidateRoundTripMismatch);
            }
        }
        catch
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode
                    .MetaDataCandidateSerializationFailed);
        }

        string normalizedPath;
        try
        {
            normalizedPath = FolderProjectPathPolicy.EnsureResourcePath(
                resourcePath);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationInvalid);
        }

        var directory = Path.GetDirectoryName(normalizedPath) ?? "";
        var fileName = Path.GetFileName(normalizedPath);
        try
        {
            packFileService.AddFilesToPack(
                project,
                [
                    new NewPackFileEntry(
                        directory,
                        PackFile.CreateFromBytes(fileName, bytes)),
                ],
                overwriteExisting: false);
        }
        catch (FolderProjectFileConflictException)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationAlreadyExists);
        }
        catch
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationWriteFailed);
        }

        _projectMetaDataResourcePath = normalizedPath;
        _savedResultMetaData = _resultMetaData.Clone();
        UpdateDirtyState();
        return CreateSaveResult(
            succeeded: true,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    public AnimationWorkbenchDocumentState SetMetaDataSynchronizationEnabled(
        bool enabled)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        _isMetaDataSynchronizationEnabled = enabled;
        UpdateDirtyState();
        return CreateState();
    }

    public AnimationWorkbenchMetaDataNavigationResult NavigateToMetaDataProblem(
        AnimationWorkbenchMetaDataProblem problem)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(problem);
        if (!problem.HasNavigationLocation ||
            !GetCurrentMetaDataProblems().Contains(problem))
        {
            return new AnimationWorkbenchMetaDataNavigationResult(
                false,
                CreateState(),
                null);
        }

        return new AnimationWorkbenchMetaDataNavigationResult(
            true,
            CreateState(),
            new AnimationWorkbenchMetaDataNavigationLocation(
                problem.Source,
                problem.SourceAttributeIndex,
                problem.ResultAttributeIndex,
                problem.SourceStartTime,
                problem.ResultStartTime));
    }

    private void LoadMetaData(AnimationWorkbenchLoadRequest request)
    {
        _animationAMetaData = MetaDataDocumentSnapshot.Create(
            request.AnimationAMetaData,
            _metaDataParser);
        _animationBMetaData = MetaDataDocumentSnapshot.Create(
            request.AnimationBMetaData,
            _metaDataParser);
        _resultMetaData = _animationAMetaData?.CloneAs("result.anm.meta");
        _isMetaDataSynchronizationEnabled = request.SynchronizeMetaData;
        _savedResultMetaData = null;
        _projectMetaDataResourcePath = null;
        _retargetedAnimationAMetaData = null;
        _retargetedAnimationBMetaData = null;
        _retargetedAnimationAMetaDataProblems = [];
        _retargetedAnimationBMetaDataProblems = [];
        _timelinePreviewMetaData = null;
        _blendPreviewMetaData = null;
        _layerPreviewMetaData = null;
        _metaDataProblems = [];
        _timelinePreviewMetaDataProblems = [];
        _blendPreviewMetaDataProblems = [];
        _layerPreviewMetaDataProblems = [];
    }

    private void ClearMetaData()
    {
        _animationAMetaData = null;
        _animationBMetaData = null;
        _resultMetaData = null;
        _savedResultMetaData = null;
        _projectMetaDataResourcePath = null;
        _retargetedAnimationAMetaData = null;
        _retargetedAnimationBMetaData = null;
        _retargetedAnimationAMetaDataProblems = [];
        _retargetedAnimationBMetaDataProblems = [];
        _timelinePreviewMetaData = null;
        _blendPreviewMetaData = null;
        _layerPreviewMetaData = null;
        _metaDataProblems = [];
        _timelinePreviewMetaDataProblems = [];
        _blendPreviewMetaDataProblems = [];
        _layerPreviewMetaDataProblems = [];
        _isMetaDataSynchronizationEnabled = false;
    }

    private void ResetMetaDataHistory()
    {
        _savedResultMetaData = null;
        _timelinePreviewMetaData = null;
        _blendPreviewMetaData = null;
        _layerPreviewMetaData = null;
        _metaDataProblems = [];
        _timelinePreviewMetaDataProblems = [];
        _blendPreviewMetaDataProblems = [];
        _layerPreviewMetaDataProblems = [];
    }

    private bool IsMetaDataDirty() =>
        _isMetaDataSynchronizationEnabled &&
        _resultMetaData != null &&
        (_savedResultMetaData == null ||
         !_resultMetaData.BytesEqual(_savedResultMetaData));

    private void BeginMetaDataTimelinePreview()
    {
        if (!_isMetaDataSynchronizationEnabled)
        {
            _timelinePreviewMetaData = null;
            _timelinePreviewMetaDataProblems = [];
            return;
        }
        _timelinePreviewMetaData = _resultMetaData?.Clone();
        _timelinePreviewMetaDataProblems = _metaDataProblems.ToArray();
    }

    private void PreviewMetaDataTrim(
        GameWorld.Core.Animation.AnimationClip animation,
        AnimationWorkbenchFrameRange range,
        TimeSpan outputDuration)
    {
        if (!_isMetaDataSynchronizationEnabled ||
            _resultMetaData == null ||
            animation.DynamicFrames.Count == 0)
            return;

        var startSeconds = animation.Duration.TotalSeconds *
            range.StartFrame / animation.DynamicFrames.Count;
        var endSeconds = animation.Duration.TotalSeconds *
            range.EndFrameExclusive / animation.DynamicFrames.Count;
        var outputFramesPerSecond = range.Length /
            outputDuration.TotalSeconds;
        var parsed = _resultMetaData.Parse(_metaDataParser);
        var problems = new List<AnimationWorkbenchMetaDataProblem>();
        for (var index = 0; index < parsed.Attributes.Count; index++)
        {
            var attribute = parsed.Attributes[index];
            if (attribute is ParsedUnknownMetadataAttribute)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode
                        .UnknownPayloadUnmapped,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    index,
                    index,
                    null,
                    null));
                continue;
            }

            if (!TryGetTimeRange(attribute, out var startTime, out var endTime) ||
                IsWholeAnimationRange(attribute))
            {
                continue;
            }

            var resultStartTime = startTime;
            var resultEndTime = endTime;
            if (startTime < 0 || endTime < startTime)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode.InvalidTimeRange,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    index,
                    index,
                    startTime,
                    endTime));
            }
            else if (IsOutsideSourceRange(
                         startTime,
                         endTime,
                         startSeconds,
                         endSeconds))
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    index,
                    index,
                    startTime,
                    endTime));
            }
            else
            {
                resultStartTime = QuantizeMetaDataTime(
                    Math.Max(startTime, startSeconds) - startSeconds,
                    outputFramesPerSecond,
                    roundUp: false,
                    outputDuration.TotalSeconds);
                resultEndTime = QuantizeMetaDataTime(
                    Math.Min(endTime, endSeconds) - startSeconds,
                    outputFramesPerSecond,
                    roundUp: true,
                    outputDuration.TotalSeconds);
                SetTimeRange(attribute, resultStartTime, resultEndTime);
            }
        }

        AppendInheritedTimelineProblems(
            problems,
            _metaDataProblems,
            parsed.Attributes);

        _timelinePreviewMetaData = MetaDataDocumentSnapshot.CreateResult(
            _resultMetaData.Name,
            parsed.Version,
            parsed.Attributes,
            _metaDataParser);
        _timelinePreviewMetaDataProblems = problems.ToArray();
    }

    private void PreviewMetaDataStretch(
        GameWorld.Core.Animation.AnimationClip animation,
        AnimationWorkbenchFrameRange range,
        int targetFrameCount,
        TimeSpan outputDuration)
    {
        if (!_isMetaDataSynchronizationEnabled ||
            _resultMetaData == null ||
            animation.DynamicFrames.Count == 0)
            return;

        var framesPerSecond = animation.DynamicFrames.Count /
            animation.Duration.TotalSeconds;
        var rangeStartSeconds = range.StartFrame / framesPerSecond;
        var rangeEndSeconds = range.EndFrameExclusive / framesPerSecond;
        var targetDurationSeconds = targetFrameCount / framesPerSecond;
        var rangeScale = targetDurationSeconds /
            (rangeEndSeconds - rangeStartSeconds);
        var trailingOffsetSeconds = targetDurationSeconds -
            (rangeEndSeconds - rangeStartSeconds);
        var parsed = _resultMetaData.Parse(_metaDataParser);
        var problems = new List<AnimationWorkbenchMetaDataProblem>();
        for (var index = 0; index < parsed.Attributes.Count; index++)
        {
            var attribute = parsed.Attributes[index];
            if (attribute is ParsedUnknownMetadataAttribute)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode
                        .UnknownPayloadUnmapped,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    index,
                    index,
                    null,
                    null));
                continue;
            }

            if (!TryGetTimeRange(attribute, out var startTime, out var endTime) ||
                IsWholeAnimationRange(attribute))
            {
                continue;
            }

            if (startTime < 0 || endTime < startTime)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode.InvalidTimeRange,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    index,
                    index,
                    startTime,
                    endTime));
                continue;
            }

            if (endTime > animation.Duration.TotalSeconds)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode.SourceOutsideResult,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    index,
                    index,
                    startTime,
                    endTime));
                continue;
            }

            var resultStartTime = QuantizeMetaDataTime(
                MapStretchTime(
                    startTime,
                    rangeStartSeconds,
                    rangeEndSeconds,
                    rangeScale,
                    trailingOffsetSeconds),
                framesPerSecond,
                roundUp: false,
                outputDuration.TotalSeconds);
            var resultEndTime = QuantizeMetaDataTime(
                MapStretchTime(
                    endTime,
                    rangeStartSeconds,
                    rangeEndSeconds,
                    rangeScale,
                    trailingOffsetSeconds),
                framesPerSecond,
                roundUp: true,
                outputDuration.TotalSeconds);
            SetTimeRange(attribute, resultStartTime, resultEndTime);
        }

        AppendInheritedTimelineProblems(
            problems,
            _metaDataProblems,
            parsed.Attributes);

        _timelinePreviewMetaData = MetaDataDocumentSnapshot.CreateResult(
            _resultMetaData.Name,
            parsed.Version,
            parsed.Attributes,
            _metaDataParser);
        _timelinePreviewMetaDataProblems = problems.ToArray();
    }

    private static double MapStretchTime(
        double sourceTime,
        double rangeStartSeconds,
        double rangeEndSeconds,
        double rangeScale,
        double trailingOffsetSeconds)
    {
        if (sourceTime < rangeStartSeconds)
            return sourceTime;
        if (sourceTime < rangeEndSeconds)
        {
            return rangeStartSeconds +
                (sourceTime - rangeStartSeconds) * rangeScale;
        }
        return sourceTime + trailingOffsetSeconds;
    }

    private static void AppendInheritedTimelineProblems(
        ICollection<AnimationWorkbenchMetaDataProblem> output,
        IReadOnlyList<AnimationWorkbenchMetaDataProblem> inherited,
        IReadOnlyList<ParsedMetadataAttribute> attributes)
    {
        foreach (var problem in inherited.Where(problem =>
                     problem.Code is
                         AnimationWorkbenchMetaDataProblemCode.BoneUnmapped or
                         AnimationWorkbenchMetaDataProblemCode.Conflict))
        {
            if (problem.ResultAttributeIndex is not int resultIndex ||
                resultIndex < 0 ||
                resultIndex >= attributes.Count ||
                !TryGetTimeRange(
                    attributes[resultIndex],
                    out var startTime,
                    out var endTime))
            {
                output.Add(problem);
                continue;
            }

            output.Add(problem with
            {
                ResultStartTime = startTime,
                ResultEndTime = endTime,
            });
        }
    }

    private MetaDataPreviewCommit CaptureTimelinePreviewMetaData() => new(
        _timelinePreviewMetaData?.Clone(),
        _timelinePreviewMetaDataProblems.ToArray());

    private void ClearMetaDataTimelinePreview()
    {
        _timelinePreviewMetaData = null;
        _timelinePreviewMetaDataProblems = [];
    }

    private IReadOnlyList<AnimationWorkbenchMetaDataProblem>
        GetCurrentMetaDataProblems()
    {
        if (!_isMetaDataSynchronizationEnabled)
        {
            return
            [
                new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode
                        .SynchronizationDisabled,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    null,
                    null,
                    null,
                    null,
                    null),
            ];
        }

        if (_timelinePreviewMetaData != null)
            return _timelinePreviewMetaDataProblems;
        if (_layerPreviewMetaData != null)
            return _layerPreviewMetaDataProblems;
        return _blendPreviewMetaData != null
            ? _blendPreviewMetaDataProblems
            : _metaDataProblems;
    }

    private void PreviewBlendMetaData(
        AnimationWorkbenchBlendRequest request,
        AnimationWorkbenchBlendImpact impact)
    {
        if (!_isMetaDataSynchronizationEnabled)
            return;

        var transitionStartSeconds =
            (impact.AnimationAOutputFrameCount - impact.OverlapFrameCount) /
            impact.OutputFramesPerSecond;
        var animationA = (_retargetedAnimationA ?? _animationA)?.Animation;
        var animationB = (_retargetedAnimationB ?? _animationB)?.Animation;
        if (animationA == null || animationB == null)
            return;

        var sourceAEndSeconds = animationA.Duration.TotalSeconds *
            (request.AnimationAOutFrame + 1) /
            animationA.DynamicFrames.Count;
        var sourceBStartSeconds = animationB.Duration.TotalSeconds *
            request.AnimationBInFrame /
            animationB.DynamicFrames.Count;
        var mapped = new List<MappedMetaDataAttribute>();
        var problems = new List<AnimationWorkbenchMetaDataProblem>();
        AppendMappedMetaData(
            mapped,
            problems,
            _retargetedAnimationAMetaData ?? _animationAMetaData,
            AnimationWorkbenchSourceSlot.AnimationA,
            0,
            sourceAEndSeconds,
            0,
            impact.OutputFramesPerSecond,
            impact.OutputDuration.TotalSeconds);
        AppendMappedMetaData(
            mapped,
            problems,
            _retargetedAnimationBMetaData ?? _animationBMetaData,
            AnimationWorkbenchSourceSlot.AnimationB,
            sourceBStartSeconds,
            animationB.Duration.TotalSeconds,
            transitionStartSeconds,
            impact.OutputFramesPerSecond,
            impact.OutputDuration.TotalSeconds);
        AppendRetargetMetaDataProblems(
            mapped,
            problems,
            _retargetedAnimationAMetaDataProblems);
        AppendRetargetMetaDataProblems(
            mapped,
            problems,
            _retargetedAnimationBMetaDataProblems);

        AddConflictProblems(mapped, problems);
        var attributes = mapped.Select(item => item.Attribute).ToList();
        var version = (_retargetedAnimationAMetaData ?? _animationAMetaData)
                ?.Version(_metaDataParser) ??
            (_retargetedAnimationBMetaData ?? _animationBMetaData)
                ?.Version(_metaDataParser) ?? 2;
        _blendPreviewMetaData = MetaDataDocumentSnapshot.CreateResult(
            "result.anm.meta",
            version,
            attributes,
            _metaDataParser);
        _blendPreviewMetaDataProblems = problems.ToArray();
    }

    private void AppendMappedMetaData(
        ICollection<MappedMetaDataAttribute> output,
        ICollection<AnimationWorkbenchMetaDataProblem> problems,
        MetaDataDocumentSnapshot? source,
        AnimationWorkbenchSourceSlot slot,
        double sourceStartSeconds,
        double sourceEndSeconds,
        double outputStartSeconds,
        double outputFramesPerSecond,
        double outputDurationSeconds,
        double timeScale = 1)
    {
        if (source == null)
            return;

        var parsed = source.Parse(_metaDataParser);
        for (var sourceIndex = 0;
             sourceIndex < parsed.Attributes.Count;
             sourceIndex++)
        {
            var attribute = parsed.Attributes[sourceIndex];
            var resultIndex = output.Count;
            if (attribute is ParsedUnknownMetadataAttribute)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode
                        .UnknownPayloadUnmapped,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    slot,
                    sourceIndex,
                    resultIndex,
                    null,
                    null));
            }
            else if (TryGetTimeRange(
                         attribute,
                         out var startTime,
                         out var endTime))
            {
                var sourceStartTime = startTime;
                var sourceEndTime = endTime;
                if (IsWholeAnimationRange(attribute))
                {
                    output.Add(new MappedMetaDataAttribute(
                        attribute,
                        slot,
                        sourceIndex,
                        resultIndex,
                        sourceStartTime,
                        sourceEndTime,
                        startTime,
                        endTime));
                    continue;
                }
                if (startTime < 0 || endTime < startTime)
                {
                    problems.Add(new AnimationWorkbenchMetaDataProblem(
                        AnimationWorkbenchMetaDataProblemCode.InvalidTimeRange,
                        AnimationWorkbenchDiagnosticSeverity.Warning,
                        slot,
                        sourceIndex,
                        resultIndex,
                        startTime,
                        endTime));
                }
                else if (IsOutsideSourceRange(
                             startTime,
                             endTime,
                             sourceStartSeconds,
                             sourceEndSeconds))
                {
                    problems.Add(new AnimationWorkbenchMetaDataProblem(
                        AnimationWorkbenchMetaDataProblemCode
                            .SourceOutsideResult,
                        AnimationWorkbenchDiagnosticSeverity.Warning,
                        slot,
                        sourceIndex,
                        resultIndex,
                        startTime,
                        endTime));
                }
                else
                {
                    var clippedStart = Math.Max(startTime, sourceStartSeconds);
                    var clippedEnd = Math.Min(endTime, sourceEndSeconds);
                    var mappedStart = QuantizeMetaDataTime(
                        (clippedStart - sourceStartSeconds) * timeScale +
                            outputStartSeconds,
                        outputFramesPerSecond,
                        roundUp: false,
                        outputDurationSeconds);
                    var mappedEnd = QuantizeMetaDataTime(
                        (clippedEnd - sourceStartSeconds) * timeScale +
                            outputStartSeconds,
                        outputFramesPerSecond,
                        roundUp: true,
                        outputDurationSeconds);
                    SetTimeRange(attribute, mappedStart, mappedEnd);
                    startTime = mappedStart;
                    endTime = mappedEnd;
                }

                output.Add(new MappedMetaDataAttribute(
                    attribute,
                    slot,
                    sourceIndex,
                    resultIndex,
                    sourceStartTime,
                    sourceEndTime,
                    startTime,
                    endTime));
                continue;
            }

            output.Add(new MappedMetaDataAttribute(
                attribute,
                slot,
                sourceIndex,
                resultIndex,
                null,
                null,
                null,
                null));
        }
    }

    private static float QuantizeMetaDataTime(
        double seconds,
        double framesPerSecond,
        bool roundUp,
        double outputDurationSeconds)
    {
        var framePosition = seconds * framesPerSecond;
        var frame = roundUp
            ? Math.Ceiling(framePosition - 0.000001)
            : Math.Floor(framePosition + 0.000001);
        return (float)Math.Clamp(
            frame / framesPerSecond,
            0,
            outputDurationSeconds);
    }

    private static bool IsOutsideSourceRange(
        double startTime,
        double endTime,
        double sourceStartTime,
        double sourceEndTime)
    {
        const double tolerance = 0.000001;
        var isInstant = Math.Abs(endTime - startTime) <= tolerance;
        return isInstant
            ? startTime < sourceStartTime - tolerance ||
                startTime >= sourceEndTime - tolerance
            : endTime <= sourceStartTime + tolerance ||
                startTime >= sourceEndTime - tolerance;
    }

    private static bool TryGetTimeRange(
        ParsedMetadataAttribute attribute,
        out float startTime,
        out float endTime)
    {
        if (ParsedMetadataTimeRange.TryCreate(
                attribute,
                out var timeRange))
        {
            startTime = timeRange.StartTime;
            endTime = timeRange.EndTime;
            return true;
        }
        startTime = 0;
        endTime = 0;
        return false;
    }

    private static bool IsWholeAnimationRange(
        ParsedMetadataAttribute attribute) =>
        ParsedMetadataTimeRange.TryCreate(attribute, out var timeRange) &&
        timeRange.IsWholeAnimationRange;

    private static void SetTimeRange(
        ParsedMetadataAttribute attribute,
        float startTime,
        float endTime)
    {
        ParsedMetadataTimeRange.Set(attribute, startTime, endTime);
    }

    private static void AddConflictProblems(
        IReadOnlyList<MappedMetaDataAttribute> attributes,
        ICollection<AnimationWorkbenchMetaDataProblem> problems)
    {
        var conflicted = new HashSet<int>();
        for (var firstIndex = 0;
             firstIndex < attributes.Count;
             firstIndex++)
        {
            var first = attributes[firstIndex];
            if (!first.StartTime.HasValue || !first.EndTime.HasValue)
                continue;
            for (var secondIndex = firstIndex + 1;
                 secondIndex < attributes.Count;
                 secondIndex++)
            {
                var second = attributes[secondIndex];
                if (!second.StartTime.HasValue || !second.EndTime.HasValue ||
                    first.Source == second.Source ||
                    first.Attribute.Name != second.Attribute.Name ||
                    first.Attribute.Version != second.Attribute.Version ||
                    !RangesOverlap(first, second))
                {
                    continue;
                }

                AddConflict(first, conflicted, problems);
                AddConflict(second, conflicted, problems);
            }
        }
    }

    private static bool RangesOverlap(
        MappedMetaDataAttribute first,
        MappedMetaDataAttribute second)
    {
        if (first.StartTime == first.EndTime ||
            second.StartTime == second.EndTime)
        {
            return first.StartTime <= second.EndTime &&
                second.StartTime <= first.EndTime;
        }
        return first.StartTime < second.EndTime &&
            second.StartTime < first.EndTime;
    }

    private static void AddConflict(
        MappedMetaDataAttribute item,
        ISet<int> conflicted,
        ICollection<AnimationWorkbenchMetaDataProblem> problems)
    {
        if (!conflicted.Add(item.ResultIndex))
            return;
        problems.Add(new AnimationWorkbenchMetaDataProblem(
            AnimationWorkbenchMetaDataProblemCode.Conflict,
            AnimationWorkbenchDiagnosticSeverity.Warning,
            item.Source,
            item.SourceIndex,
            item.ResultIndex,
            item.SourceStartTime,
            item.SourceEndTime,
            item.ResultStartTime,
            item.ResultEndTime));
    }

    private MetaDataPreviewCommit CaptureBlendPreviewMetaData() => new(
        _blendPreviewMetaData?.Clone(),
        _blendPreviewMetaDataProblems.ToArray());

    private void ClearBlendMetaDataPreview()
    {
        _blendPreviewMetaData = null;
        _blendPreviewMetaDataProblems = [];
    }

    private void PreviewLayerMetaData(AnimationWorkbenchLayerImpact impact)
    {
        if (!_isMetaDataSynchronizationEnabled)
            return;

        var animationA = (_retargetedAnimationA ?? _animationA)?.Animation;
        var animationB = (_retargetedAnimationB ?? _animationB)?.Animation;
        if (animationA == null || animationB == null ||
            impact.OutputDuration <= TimeSpan.Zero)
        {
            return;
        }

        var outputDurationSeconds = impact.OutputDuration.TotalSeconds;
        var outputFramesPerSecond = impact.OutputFrameCount /
            outputDurationSeconds;
        var mapped = new List<MappedMetaDataAttribute>();
        var problems = new List<AnimationWorkbenchMetaDataProblem>();
        AppendMappedMetaData(
            mapped,
            problems,
            _retargetedAnimationAMetaData ?? _animationAMetaData,
            AnimationWorkbenchSourceSlot.AnimationA,
            0,
            animationA.Duration.TotalSeconds,
            0,
            outputFramesPerSecond,
            outputDurationSeconds,
            outputDurationSeconds / animationA.Duration.TotalSeconds);
        AppendMappedMetaData(
            mapped,
            problems,
            _retargetedAnimationBMetaData ?? _animationBMetaData,
            AnimationWorkbenchSourceSlot.AnimationB,
            0,
            animationB.Duration.TotalSeconds,
            0,
            outputFramesPerSecond,
            outputDurationSeconds,
            outputDurationSeconds / animationB.Duration.TotalSeconds);
        AppendRetargetMetaDataProblems(
            mapped,
            problems,
            _retargetedAnimationAMetaDataProblems);
        AppendRetargetMetaDataProblems(
            mapped,
            problems,
            _retargetedAnimationBMetaDataProblems);
        AddConflictProblems(mapped, problems);

        var version = (_retargetedAnimationAMetaData ?? _animationAMetaData)
                ?.Version(_metaDataParser) ??
            (_retargetedAnimationBMetaData ?? _animationBMetaData)
                ?.Version(_metaDataParser) ?? 2;
        _layerPreviewMetaData = MetaDataDocumentSnapshot.CreateResult(
            "result.anm.meta",
            version,
            mapped.Select(item => item.Attribute).ToList(),
            _metaDataParser);
        _layerPreviewMetaDataProblems = problems.ToArray();
    }

    private MetaDataPreviewCommit CaptureLayerPreviewMetaData() => new(
        _layerPreviewMetaData?.Clone(),
        _layerPreviewMetaDataProblems.ToArray());

    private void ClearLayerMetaDataPreview()
    {
        _layerPreviewMetaData = null;
        _layerPreviewMetaDataProblems = [];
    }

    private static void AppendRetargetMetaDataProblems(
        IReadOnlyList<MappedMetaDataAttribute> mapped,
        ICollection<AnimationWorkbenchMetaDataProblem> problems,
        IReadOnlyList<AnimationWorkbenchMetaDataProblem> retargetProblems)
    {
        foreach (var problem in retargetProblems.Where(problem =>
                     problem.Code ==
                         AnimationWorkbenchMetaDataProblemCode.BoneUnmapped))
        {
            var item = mapped.FirstOrDefault(item =>
                item.Source == problem.Source &&
                item.SourceIndex == problem.SourceAttributeIndex);
            problems.Add(item == null
                ? problem with
                {
                    ResultAttributeIndex = null,
                    ResultStartTime = null,
                    ResultEndTime = null,
                }
                : problem with
                {
                    ResultAttributeIndex = item.ResultIndex,
                    ResultStartTime = item.ResultStartTime,
                    ResultEndTime = item.ResultEndTime,
                });
        }
    }

    private MetaDataPreviewCommit RetargetMetaData(
        AnimationWorkbenchSourceSlot source,
        IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> mappings)
    {
        var sourceMetaData = source == AnimationWorkbenchSourceSlot.AnimationA
            ? _animationAMetaData
            : _animationBMetaData;
        if (!_isMetaDataSynchronizationEnabled || sourceMetaData == null)
            return new MetaDataPreviewCommit(null, []);

        var targetIndexBySourceIndex = mappings
            .Where(mapping => mapping.SourceBoneIndex.HasValue)
            .GroupBy(mapping => mapping.SourceBoneIndex!.Value)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single().TargetBoneIndex);
        var parsed = sourceMetaData.Parse(_metaDataParser);
        var problems = new List<AnimationWorkbenchMetaDataProblem>();
        for (var index = 0; index < parsed.Attributes.Count; index++)
        {
            var attribute = parsed.Attributes[index];
            if (attribute is ParsedUnknownMetadataAttribute)
            {
                problems.Add(new AnimationWorkbenchMetaDataProblem(
                    AnimationWorkbenchMetaDataProblemCode
                        .UnknownPayloadUnmapped,
                    AnimationWorkbenchDiagnosticSeverity.Warning,
                    source,
                    index,
                    index,
                    null,
                    null));
                continue;
            }

            var hasUnmappedBone = false;
            foreach (var property in attribute.GetType().GetProperties()
                         .Where(property =>
                             property.PropertyType == typeof(int) &&
                             property.CanRead &&
                             property.CanWrite &&
                             s_metaDataBoneIndexProperties.Contains(
                                 property.Name) &&
                             property.GetCustomAttributes(
                                 typeof(MetaDataTagAttribute),
                                 true).Length != 0))
            {
                var sourceBoneIndex = (int)property.GetValue(attribute)!;
                if (sourceBoneIndex < 0)
                    continue;
                if (targetIndexBySourceIndex.TryGetValue(
                        sourceBoneIndex,
                        out var targetBoneIndex))
                {
                    property.SetValue(attribute, targetBoneIndex);
                }
                else
                {
                    hasUnmappedBone = true;
                }
            }

            if (!hasUnmappedBone)
                continue;
            TryGetTimeRange(attribute, out var startTime, out var endTime);
            problems.Add(new AnimationWorkbenchMetaDataProblem(
                AnimationWorkbenchMetaDataProblemCode.BoneUnmapped,
                AnimationWorkbenchDiagnosticSeverity.Warning,
                source,
                index,
                index,
                startTime,
                endTime,
                startTime,
                endTime));
        }

        return new MetaDataPreviewCommit(
            MetaDataDocumentSnapshot.CreateResult(
                sourceMetaData.Name,
                parsed.Version,
                parsed.Attributes,
                _metaDataParser),
            problems.ToArray());
    }

    private void CommitRetargetMetaData(
        AnimationWorkbenchSourceSlot source,
        MetaDataPreviewCommit commit)
    {
        if (commit.Snapshot == null)
            return;

        if (source == AnimationWorkbenchSourceSlot.AnimationA)
        {
            _retargetedAnimationAMetaData = commit.Snapshot.Clone();
            _retargetedAnimationAMetaDataProblems =
                commit.Problems.ToArray();
            _resultMetaData = commit.Snapshot.CloneAs("result.anm.meta");
            _metaDataProblems = commit.Problems;
        }
        else
        {
            _retargetedAnimationBMetaData = commit.Snapshot.Clone();
            _retargetedAnimationBMetaDataProblems =
                commit.Problems.ToArray();
            _metaDataProblems = _metaDataProblems
                .Where(problem =>
                    problem.Source != AnimationWorkbenchSourceSlot.AnimationB)
                .Concat(commit.Problems.Select(problem => problem with
                {
                    ResultAttributeIndex = null,
                    ResultStartTime = null,
                    ResultEndTime = null,
                }))
                .ToArray();
        }
        UpdateDirtyState();
    }

    private sealed record MappedMetaDataAttribute(
        ParsedMetadataAttribute Attribute,
        AnimationWorkbenchSourceSlot? Source,
        int SourceIndex,
        int ResultIndex,
        float? SourceStartTime,
        float? SourceEndTime,
        float? ResultStartTime,
        float? ResultEndTime)
    {
        public float? StartTime => ResultStartTime ?? SourceStartTime;

        public float? EndTime => ResultEndTime ?? SourceEndTime;
    }

    private sealed record MetaDataPreviewCommit(
        MetaDataDocumentSnapshot? Snapshot,
        IReadOnlyList<AnimationWorkbenchMetaDataProblem> Problems);

    private sealed class MetaDataDocumentSnapshot(
        string name,
        byte[] bytes)
    {
        private readonly byte[] _bytes = bytes.ToArray();

        public string Name => name;

        public static MetaDataDocumentSnapshot? Create(
            AnimationWorkbenchMetaDataSourceInput? input,
            MetaDataFileParser parser)
        {
            if (input == null)
                return null;
            var bytes = input.CopyBytes();
            parser.ParseFile(bytes);
            return new MetaDataDocumentSnapshot(input.Name, bytes);
        }

        public MetaDataDocumentSnapshot Clone() => new(name, _bytes);

        public MetaDataDocumentSnapshot CloneAs(string replacementName) =>
            new(replacementName, _bytes);

        public ParsedMetadataFile Parse(MetaDataFileParser parser) =>
            parser.ParseFile(_bytes);

        public int Version(MetaDataFileParser parser) =>
            Parse(parser).Version;

        public static MetaDataDocumentSnapshot CreateResult(
            string name,
            int version,
            List<ParsedMetadataAttribute> attributes,
            MetaDataFileParser parser) => new(
            name,
            parser.GenerateBytes(
                version,
                new ParsedMetadataFile
                {
                    Version = version,
                    Attributes = attributes,
                }));

        public bool BytesEqual(MetaDataDocumentSnapshot other) =>
            _bytes.AsSpan().SequenceEqual(other._bytes);

        public byte[] CopyBytes() => _bytes.ToArray();

        public AnimationWorkbenchMetaDataSnapshot CreatePublicSnapshot() =>
            new(name, _bytes);

    }
}
