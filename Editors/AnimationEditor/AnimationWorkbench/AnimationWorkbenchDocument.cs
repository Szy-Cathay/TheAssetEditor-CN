using GameWorld.Core.Animation;
using Shared.Core.Settings;

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
    Information,
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
}

public sealed record AnimationWorkbenchSourceInput(
    string Name,
    AnimationClip Animation,
    GameSkeleton Skeleton);

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
    int? ActualValue = null);

public interface IAnimationWorkbenchPreviewHost : IDisposable
{
    void Show(
        AnimationWorkbenchPreviewSnapshot preview,
        CancellationToken cancellationToken);

    void Clear();
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
    bool IsClosed);

public sealed class AnimationWorkbenchDocument : IDisposable
{
    private readonly IAnimationWorkbenchPreviewHost _previewHost;
    private SourceSnapshot? _animationA;
    private SourceSnapshot? _animationB;
    private SourceSnapshot? _result;
    private GameTypeEnum? _targetGame;
    private GameSkeleton? _targetSkeleton;
    private AnimationWorkbenchPreviewKind? _selectedPreview;
    private CancellationTokenSource? _previewCancellationSource;
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
            _isClosed);
    }

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
            AnimationWorkbenchPreviewKind.Result => _result,
            _ => null,
        };

    private void ShowCurrentPreview()
    {
        var preview = CreatePreview(_selectedPreview);
        if (preview == null)
            return;

        _previewCancellationSource = new CancellationTokenSource();
        _previewHost.Show(
            preview,
            _previewCancellationSource.Token);
    }

    private void ReleasePreview()
    {
        var cancellationSource = Interlocked.Exchange(
            ref _previewCancellationSource,
            null);
        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
        _previewHost.Clear();
    }

    private sealed class SourceSnapshot(
        string name,
        AnimationClip animation,
        GameSkeleton skeleton)
    {
        public int AnimationBoneCount => animation.AnimationBoneCount;

        public int SkeletonBoneCount => skeleton.BoneCount;

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
                input.Skeleton.Clone(new AnimationPlayer()));
        }

        public SourceSnapshot Clone() => new(
            name,
            animation.Clone(),
            skeleton.Clone(new AnimationPlayer()));

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
        public void Show(
            AnimationWorkbenchPreviewSnapshot preview,
            CancellationToken cancellationToken)
        {
        }

        public void Clear()
        {
        }

        public void Dispose()
        {
        }
    }
}
