using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed class AnimationWorkbenchBlendController : INotifyPropertyChanged
{
    private readonly AnimationWorkbenchDocument _document;
    private int _animationAOutFrame;
    private int _animationBInFrame;
    private double _overlapSeconds;
    private double _outputFramesPerSecond;
    private AnimationWorkbenchBlendCurve _curve;
    private bool _alignHorizontalPosition = true;
    private bool _alignYaw = true;
    private bool _preserveSourceHeightChanges = true;
    private AnimationWorkbenchBlendResult? _lastResult;
    private long _ownedPreviewVersion;

    public AnimationWorkbenchBlendController(
        AnimationWorkbenchDocument document)
    {
        _document = document ?? throw new ArgumentNullException(
            nameof(document));
        var state = document.GetState();
        _animationAOutFrame = Math.Max(
            0,
            (state.AnimationA?.FrameCount ?? 1) - 1);
        _animationBInFrame = 0;
        _outputFramesPerSecond = GetFramesPerSecond(state.AnimationA) ?? 20;
        _overlapSeconds = Math.Min(0.2, MaximumOverlapSeconds);
        _curve = AnimationWorkbenchBlendCurve.Smooth;
        RefreshPreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public int MaximumAnimationAOutFrame => Math.Max(
        0,
        (_document.GetState().AnimationA?.FrameCount ?? 1) - 1);

    public int MaximumAnimationBInFrame => Math.Max(
        0,
        (_document.GetState().AnimationB?.FrameCount ?? 1) - 1);

    public double MaximumOverlapSeconds
    {
        get
        {
            var state = _document.GetState();
            var animationAFps = GetFramesPerSecond(state.AnimationA);
            var animationBFps = GetFramesPerSecond(state.AnimationB);
            var animationADuration = animationAFps.HasValue
                ? Math.Min(
                    state.AnimationA!.Duration.TotalSeconds,
                    (_animationAOutFrame + 1) / animationAFps.Value)
                : 0;
            var animationBDuration = animationBFps.HasValue
                ? Math.Max(
                    0,
                    state.AnimationB!.Duration.TotalSeconds -
                    _animationBInFrame / animationBFps.Value)
                : 0;
            var animationBOutputFrames = Math.Max(
                1,
                (int)Math.Round(
                    animationBDuration * _outputFramesPerSecond,
                    MidpointRounding.AwayFromZero));
            if (animationBOutputFrames > 1)
            {
                animationBDuration = Math.Max(
                    0,
                    animationBDuration - 1 / _outputFramesPerSecond);
            }
            return Math.Min(animationADuration, animationBDuration);
        }
    }

    public int AnimationAOutFrame
    {
        get => _animationAOutFrame;
        set => SetAndRefresh(ref _animationAOutFrame, value);
    }

    public int AnimationBInFrame
    {
        get => _animationBInFrame;
        set => SetAndRefresh(ref _animationBInFrame, value);
    }

    public double OverlapSeconds
    {
        get => _overlapSeconds;
        set => SetAndRefresh(ref _overlapSeconds, value);
    }

    public double OutputFramesPerSecond
    {
        get => _outputFramesPerSecond;
        set => SetAndRefresh(ref _outputFramesPerSecond, value);
    }

    public AnimationWorkbenchBlendCurve Curve
    {
        get => _curve;
        set => SetAndRefresh(ref _curve, value);
    }

    public bool AlignHorizontalPosition
    {
        get => _alignHorizontalPosition;
        set => SetAndRefresh(ref _alignHorizontalPosition, value);
    }

    public bool AlignYaw
    {
        get => _alignYaw;
        set => SetAndRefresh(ref _alignYaw, value);
    }

    public bool PreserveSourceHeightChanges
    {
        get => _preserveSourceHeightChanges;
        set => SetAndRefresh(ref _preserveSourceHeightChanges, value);
    }

    public AnimationWorkbenchBlendResult? LastResult => _lastResult;

    public AnimationWorkbenchBlendImpact? Impact => _lastResult?.Impact;

    public IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics =>
        _lastResult?.Diagnostics ?? Array.Empty<AnimationWorkbenchDiagnostic>();

    public bool HasActivePreview =>
        _document.IsClosed == false &&
        _ownedPreviewVersion != 0 &&
        _document.ActiveBlendPreviewVersion == _ownedPreviewVersion;

    public bool CanCommit => _lastResult?.Succeeded == true && HasActivePreview;

    public AnimationWorkbenchBlendResult RefreshPreview()
    {
        _document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        _lastResult = _document.PreviewBlend(new AnimationWorkbenchBlendRequest(
            _animationAOutFrame,
            _animationBInFrame,
            TimeSpan.FromSeconds(_overlapSeconds),
            _outputFramesPerSecond,
            _curve,
            new AnimationWorkbenchRootMotionOptions(
                _alignHorizontalPosition,
                _alignYaw,
                _preserveSourceHeightChanges)));
        _ownedPreviewVersion = _lastResult.Succeeded &&
            _lastResult.State.HasActiveBlendPreview
            ? _document.ActiveBlendPreviewVersion
            : 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchBlendResult CommitPreview()
    {
        if (HasActivePreview == false)
            return CreateMissingPreviewResult();
        _lastResult = _document.CommitBlendPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchBlendResult CancelPreview()
    {
        if (HasActivePreview == false)
            return CreateMissingPreviewResult();
        _lastResult = _document.CancelBlendPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchBlendResult? ReleasePreview()
    {
        if (_document.IsClosed)
            return null;
        if (HasActivePreview == false)
            return null;
        _lastResult = _document.CancelBlendPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
        return _lastResult;
    }

    private AnimationWorkbenchBlendResult CreateMissingPreviewResult()
    {
        _ownedPreviewVersion = 0;
        _lastResult = new AnimationWorkbenchBlendResult(
            false,
            _document.GetState(),
            null,
            [new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.BlendPreviewMissing,
                AnimationWorkbenchDiagnosticSeverity.Error)]);
        RaiseStateChanged();
        return _lastResult;
    }

    private static double? GetFramesPerSecond(
        AnimationWorkbenchSourceSummary? source)
    {
        if (source == null ||
            source.FrameCount <= 0 ||
            source.Duration <= TimeSpan.Zero)
        {
            return null;
        }
        return source.FrameCount / source.Duration.TotalSeconds;
    }

    private void SetAndRefresh<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
        RefreshPreview();
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(LastResult));
        OnPropertyChanged(nameof(Impact));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(HasActivePreview));
        OnPropertyChanged(nameof(CanCommit));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
