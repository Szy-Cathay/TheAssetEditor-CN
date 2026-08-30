using System.IO;
using GameWorld.Core.Animation;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchPreviewKind
{
    AnimationA,
    AnimationB,
    Result,
}

public enum AnimationWorkbenchSourceSlot
{
    AnimationA,
    AnimationB,
}

public enum AnimationWorkbenchDiagnosticSeverity
{
    Warning,
    Error,
}

public enum AnimationWorkbenchDiagnosticCode
{
    AnimationAMissing,
    SourceSkeletonBoneCountMismatch,
    TargetGameMissing,
    TargetGameUnsupported,
    TargetSkeletonMissing,
    SourceFormatUnknown,
    SourceFormatUnsupported,
    SourceVersionEightReadOnly,
    SourceMultiplePartsReadOnly,
    SourceStaticFrameReadOnly,
    TargetGameSaveUnsupported,
    ResultMissing,
    ResultTargetSkeletonBoneCountMismatch,
    CandidateSerializationFailed,
    CandidateRoundTripMismatch,
    DestinationAlreadyExists,
    DestinationInvalid,
    DestinationWriteFailed,
    PoseFrameIndexInvalid,
    PoseLastFrameDeleteRejected,
    PoseBoneMissing,
    PoseClipboardIncomplete,
    PoseTransformInvalid,
    PosePreviewAlreadyActive,
    PosePreviewMissing,
    PoseUndoUnavailable,
    PoseRedoUnavailable,
}

public sealed record AnimationWorkbenchSourceFormat(
    uint Version,
    int PartCount,
    bool HasStaticFrame = false)
{
    public static AnimationWorkbenchSourceFormat FromFile(
        AnimationFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new AnimationWorkbenchSourceFormat(
            file.Header.Version,
            file.AnimationParts.Count,
            file.AnimationParts.Any(
                part => part.StaticFrame != null));
    }
}

public sealed record AnimationWorkbenchSourceInput(
    string Name,
    AnimationClip Animation,
    GameSkeleton Skeleton,
    AnimationWorkbenchSourceFormat? Format = null);

public sealed record AnimationWorkbenchLoadRequest(
    AnimationWorkbenchSourceInput? AnimationA,
    AnimationWorkbenchSourceInput? AnimationB,
    GameTypeEnum? TargetGame,
    GameSkeleton? TargetSkeleton);

public sealed record AnimationWorkbenchSourceSummary(
    string Name,
    int FrameCount,
    TimeSpan Duration,
    string SkeletonName,
    int BoneCount);

public sealed record AnimationWorkbenchSkeletonSummary(
    string Name,
    int BoneCount);

public sealed record AnimationWorkbenchDiagnostic(
    AnimationWorkbenchDiagnosticCode Code,
    AnimationWorkbenchDiagnosticSeverity Severity,
    AnimationWorkbenchSourceSlot? Source = null,
    int? ExpectedValue = null,
    int? ActualValue = null,
    string? BoneName = null)
{
    public string ReasonKey => $"AnimationWorkbench.Diagnostic.{Code}";
}

public sealed record AnimationWorkbenchSaveResult(
    bool Succeeded,
    AnimationWorkbenchDocumentState State,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public interface IAnimationWorkbenchPreviewHost : IDisposable
{
    /// <summary>
    /// Creates a preview session that owns its player, scene nodes, and event
    /// subscriptions. Disposing the session must release all of them.
    /// </summary>
    IDisposable Show(
        AnimationWorkbenchPreviewSnapshot preview,
        CancellationToken cancellationToken);
}

public sealed class AnimationWorkbenchPreviewSnapshot
{
    internal AnimationWorkbenchPreviewSnapshot(
        AnimationWorkbenchPreviewKind kind,
        string name,
        AnimationClip animation,
        GameSkeleton skeleton)
    {
        Kind = kind;
        Name = name;
        Animation = animation;
        Skeleton = skeleton;
    }

    public AnimationWorkbenchPreviewKind Kind { get; }

    public string Name { get; }

    public AnimationClip Animation { get; }

    public GameSkeleton Skeleton { get; }
}

public sealed record AnimationWorkbenchDocumentState(
    AnimationWorkbenchSourceSummary? AnimationA,
    AnimationWorkbenchSourceSummary? AnimationB,
    AnimationWorkbenchSourceSummary? Result,
    GameTypeEnum? TargetGame,
    AnimationWorkbenchSkeletonSummary? TargetSkeleton,
    AnimationWorkbenchPreviewKind? SelectedPreview,
    AnimationWorkbenchPreviewSnapshot? CurrentPreview,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics,
    bool IsDirty,
    string? ProjectResourcePath,
    bool IsClosed,
    bool CanUndo,
    bool CanRedo,
    bool HasActivePosePreview);

public sealed partial class AnimationWorkbenchDocument : IDisposable
{
    private readonly IAnimationWorkbenchPreviewHost _previewHost;
    private SourceSnapshot? _animationA;
    private SourceSnapshot? _animationB;
    private SourceSnapshot? _result;
    private GameTypeEnum? _targetGame;
    private GameSkeleton? _targetSkeleton;
    private AnimationWorkbenchPreviewKind? _selectedPreview;
    private CancellationTokenSource? _previewCancellationSource;
    private IDisposable? _previewSession;
    private bool _isDirty;
    private string? _projectResourcePath;
    private bool _isClosed;

    public AnimationWorkbenchDocument(
        IAnimationWorkbenchPreviewHost? previewHost = null)
    {
        _previewHost = previewHost ?? new EmptyPreviewHost();
    }

    public AnimationWorkbenchDocumentState Load(
        AnimationWorkbenchLoadRequest request)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(request);

        var animationA = SourceSnapshot.Create(request.AnimationA);
        var animationB = SourceSnapshot.Create(request.AnimationB);
        var result = animationA?.Clone();
        var targetSkeleton = request.TargetSkeleton?.Clone(
            new AnimationPlayer());
        AnimationWorkbenchPreviewKind? selectedPreview = animationA == null
            ? null
            : AnimationWorkbenchPreviewKind.AnimationA;

        ReleasePreview();
        _animationA = animationA;
        _animationB = animationB;
        _result = result;
        _targetGame = request.TargetGame;
        _targetSkeleton = targetSkeleton;
        _selectedPreview = selectedPreview;
        _projectResourcePath = null;
        ResetPoseEditing(result != null);

        ShowCurrentPreview();

        return CreateState();
    }

    public AnimationWorkbenchDocumentState SelectPreview(
        AnimationWorkbenchPreviewKind kind)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (GetSource(kind) != null)
        {
            ReleasePreview();
            _selectedPreview = kind;
            ShowCurrentPreview();
        }

        return CreateState();
    }

    public AnimationWorkbenchSaveResult SaveAsNewProjectResource(
        IPackFileService packFileService,
        FolderProjectContainer project,
        string resourcePath)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ArgumentNullException.ThrowIfNull(packFileService);
        ArgumentNullException.ThrowIfNull(project);

        var candidate = PrepareSaveCandidate();
        if (!candidate.Succeeded)
            return CreateSaveResult(false, candidate.Diagnostics);

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
                        PackFile.CreateFromBytes(
                            fileName,
                            candidate.Bytes!)),
                ],
                overwriteExisting: false);
        }
        catch (FolderProjectFileConflictException)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationAlreadyExists);
        }
        catch (Exception)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationWriteFailed);
        }

        _projectResourcePath = normalizedPath;
        MarkPoseHistorySaved();
        return CreateSaveResult(
            succeeded: true,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    public AnimationWorkbenchSaveResult ExportDiskCopy(
        string destinationPath)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);

        var candidate = PrepareSaveCandidate();
        if (!candidate.Succeeded)
            return CreateSaveResult(false, candidate.Diagnostics);

        try
        {
            AnimationWorkbenchDiskCopyWriter.Write(
                destinationPath,
                candidate.Bytes!);
        }
        catch (AnimationWorkbenchDestinationExistsException)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationAlreadyExists);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  NotSupportedException or
                  PathTooLongException)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationInvalid);
        }
        catch (Exception)
        {
            return CreateSaveFailure(
                AnimationWorkbenchDiagnosticCode.DestinationWriteFailed);
        }

        return CreateSaveResult(
            succeeded: true,
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    public AnimationWorkbenchDocumentState Close()
    {
        if (_isClosed)
            return CreateState();

        ReleasePreview();
        _previewHost.Dispose();
        _animationA = null;
        _animationB = null;
        _result = null;
        _targetGame = null;
        _targetSkeleton = null;
        _selectedPreview = null;
        _projectResourcePath = null;
        ResetPoseEditing(hasResult: false);
        _isClosed = true;

        return CreateState();
    }

    public void Dispose() => Close();

    private AnimationWorkbenchDocumentState CreateState()
    {
        return new AnimationWorkbenchDocumentState(
            _animationA?.CreateSummary(),
            _animationB?.CreateSummary(),
            _result?.CreateSummary(),
            _targetGame,
            _targetSkeleton == null
                ? null
                : new AnimationWorkbenchSkeletonSummary(
                    _targetSkeleton.SkeletonName,
                    _targetSkeleton.BoneCount),
            _selectedPreview,
            CreatePreview(_selectedPreview),
            CreateDiagnostics(),
            _isDirty,
            _projectResourcePath,
            _isClosed,
            _undoPoseEdits.Count != 0,
            _redoPoseEdits.Count != 0,
            _posePreviewResult != null);
    }

    private AnimationWorkbenchCandidateBuildResult PrepareSaveCandidate()
    {
        var diagnostics = new List<AnimationWorkbenchDiagnostic>();
        if (_targetGame != GameTypeEnum.Warhammer3)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetGameSaveUnsupported));
        }

        if (_posePreviewResult != null)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                AnimationWorkbenchDiagnosticCode.PosePreviewAlreadyActive));
        }

        if (_result == null)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                AnimationWorkbenchDiagnosticCode.ResultMissing));
        }

        if (_targetSkeleton == null)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing));
        }

        AddSaveSourceDiagnostics(
            diagnostics,
            _animationA,
            AnimationWorkbenchSourceSlot.AnimationA);
        if (_animationB != null)
        {
            AddSaveSourceDiagnostics(
                diagnostics,
                _animationB,
                AnimationWorkbenchSourceSlot.AnimationB);
        }

        if (diagnostics.Count != 0)
            return AnimationWorkbenchCandidateBuildResult.Failure(diagnostics);

        return AnimationWorkbenchCandidateBuilder.Build(
            _result!.Animation,
            _targetSkeleton!);
    }

    private static void AddSaveSourceDiagnostics(
        ICollection<AnimationWorkbenchDiagnostic> diagnostics,
        SourceSnapshot? source,
        AnimationWorkbenchSourceSlot slot)
    {
        if (source == null)
            return;

        if (source.AnimationBoneCount != source.SkeletonBoneCount)
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.SourceSkeletonBoneCountMismatch,
                AnimationWorkbenchDiagnosticSeverity.Error,
                slot,
                source.SkeletonBoneCount,
                source.AnimationBoneCount));
        }

        if (source.Format == null)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                AnimationWorkbenchDiagnosticCode.SourceFormatUnknown,
                slot));
            return;
        }

        var capabilities = AnimationFormatCapabilities.Evaluate(
            source.Format.Version,
            source.Format.PartCount);
        foreach (var reason in capabilities.BlockingReasons)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                reason switch
                {
                    AnimationFormatBlockReason.UnsupportedVersion =>
                        AnimationWorkbenchDiagnosticCode.SourceFormatUnsupported,
                    AnimationFormatBlockReason.VersionEightIsReadOnly =>
                        AnimationWorkbenchDiagnosticCode.SourceVersionEightReadOnly,
                    AnimationFormatBlockReason.MultiplePartsAreReadOnly =>
                        AnimationWorkbenchDiagnosticCode.SourceMultiplePartsReadOnly,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(reason),
                        reason,
                        null),
                },
                slot));
        }

        if (source.Format.HasStaticFrame)
        {
            diagnostics.Add(CreateSaveDiagnostic(
                AnimationWorkbenchDiagnosticCode.SourceStaticFrameReadOnly,
                slot));
        }
    }

    private AnimationWorkbenchSaveResult CreateSaveFailure(
        AnimationWorkbenchDiagnosticCode code) => CreateSaveResult(
            succeeded: false,
            [CreateSaveDiagnostic(code)]);

    private AnimationWorkbenchSaveResult CreateSaveResult(
        bool succeeded,
        IReadOnlyList<AnimationWorkbenchDiagnostic> diagnostics) => new(
            succeeded,
            CreateState(),
            diagnostics.ToArray());

    private static AnimationWorkbenchDiagnostic CreateSaveDiagnostic(
        AnimationWorkbenchDiagnosticCode code,
        AnimationWorkbenchSourceSlot? source = null) => new(
            code,
            AnimationWorkbenchDiagnosticSeverity.Error,
            source);

    private IReadOnlyList<AnimationWorkbenchDiagnostic> CreateDiagnostics()
    {
        if (_isClosed)
            return Array.Empty<AnimationWorkbenchDiagnostic>();

        var diagnostics = new List<AnimationWorkbenchDiagnostic>();
        if (_animationA == null)
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.AnimationAMissing,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }
        else
        {
            AddSourceDiagnostics(
                diagnostics,
                _animationA,
                AnimationWorkbenchSourceSlot.AnimationA);
        }

        if (_animationB != null)
        {
            AddSourceDiagnostics(
                diagnostics,
                _animationB,
                AnimationWorkbenchSourceSlot.AnimationB);
        }

        if (_targetGame == null)
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetGameMissing,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }
        else if (_targetGame != GameTypeEnum.Warhammer3 &&
                 _targetGame != GameTypeEnum.ThreeKingdoms)
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetGameUnsupported,
                AnimationWorkbenchDiagnosticSeverity.Error));
        }

        if (_targetSkeleton == null)
        {
            diagnostics.Add(new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.TargetSkeletonMissing,
                AnimationWorkbenchDiagnosticSeverity.Warning));
        }

        return diagnostics;
    }

    private static void AddSourceDiagnostics(
        ICollection<AnimationWorkbenchDiagnostic> diagnostics,
        SourceSnapshot source,
        AnimationWorkbenchSourceSlot slot)
    {
        if (source.AnimationBoneCount == source.SkeletonBoneCount)
            return;

        diagnostics.Add(new AnimationWorkbenchDiagnostic(
            AnimationWorkbenchDiagnosticCode.SourceSkeletonBoneCountMismatch,
            AnimationWorkbenchDiagnosticSeverity.Error,
            slot,
            source.SkeletonBoneCount,
            source.AnimationBoneCount));
    }

    private AnimationWorkbenchPreviewSnapshot? CreatePreview(
        AnimationWorkbenchPreviewKind? kind)
    {
        var source = kind == null ? null : GetSource(kind.Value);

        return source?.CreatePreview(kind!.Value);
    }

    private SourceSnapshot? GetSource(AnimationWorkbenchPreviewKind kind) =>
        kind switch
        {
            AnimationWorkbenchPreviewKind.AnimationA => _animationA,
            AnimationWorkbenchPreviewKind.AnimationB => _animationB,
            AnimationWorkbenchPreviewKind.Result =>
                _posePreviewResult ?? _result,
            _ => null,
        };

    private void ShowCurrentPreview()
    {
        var preview = CreatePreview(_selectedPreview);
        if (preview == null)
            return;

        var cancellationSource = new CancellationTokenSource();
        try
        {
            var previewSession = _previewHost.Show(
                preview,
                cancellationSource.Token);
            _previewCancellationSource = cancellationSource;
            _previewSession = previewSession;
        }
        catch
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
            throw;
        }
    }

    private void ReleasePreview()
    {
        var cancellationSource = Interlocked.Exchange(
            ref _previewCancellationSource,
            null);
        var previewSession = Interlocked.Exchange(
            ref _previewSession,
            null);

        try
        {
            cancellationSource?.Cancel();
        }
        finally
        {
            try
            {
                previewSession?.Dispose();
            }
            finally
            {
                cancellationSource?.Dispose();
            }
        }
    }

    private sealed class SourceSnapshot(
        string name,
        AnimationClip animation,
        GameSkeleton skeleton,
        AnimationWorkbenchSourceFormat? format)
    {
        public AnimationClip Animation => animation;

        public int AnimationBoneCount => animation.AnimationBoneCount;

        public int SkeletonBoneCount => skeleton.BoneCount;

        public AnimationWorkbenchSourceFormat? Format => format;

        public static SourceSnapshot? Create(
            AnimationWorkbenchSourceInput? input)
        {
            if (input == null)
                return null;

            ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
            ArgumentNullException.ThrowIfNull(input.Animation);
            ArgumentNullException.ThrowIfNull(input.Skeleton);

            return new SourceSnapshot(
                input.Name,
                input.Animation.Clone(),
                input.Skeleton.Clone(new AnimationPlayer()),
                input.Format);
        }

        public SourceSnapshot Clone() => new(
            name,
            animation.Clone(),
            skeleton.Clone(new AnimationPlayer()),
            format);

        public SourceSnapshot WithAnimation(AnimationClip replacement) => new(
            name,
            replacement.Clone(),
            skeleton.Clone(new AnimationPlayer()),
            format);

        public AnimationWorkbenchSourceSummary CreateSummary() => new(
            name,
            animation.DynamicFrames.Count,
            animation.Duration,
            skeleton.SkeletonName,
            skeleton.BoneCount);

        public AnimationWorkbenchPreviewSnapshot CreatePreview(
            AnimationWorkbenchPreviewKind kind) => new(
                kind,
                name,
                animation.Clone(),
                skeleton.Clone(new AnimationPlayer()));
    }

    private sealed class EmptyPreviewHost : IAnimationWorkbenchPreviewHost
    {
        public IDisposable Show(
            AnimationWorkbenchPreviewSnapshot preview,
            CancellationToken cancellationToken)
        {
            return EmptyPreviewSession.Instance;
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyPreviewSession : IDisposable
    {
        public static EmptyPreviewSession Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
