using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Vfx;

public sealed class VfxPreviewComponent(IVfxPreviewFactory factory, IDeviceResolver devices, RenderEngineComponent renderer)
    : BaseComponent, IRenderItem, IDisposable
{
    private IVfxPreview? _preview;
    private VfxPreviewSnapshot? _pending;
    private VfxPreviewSnapshot? _loading;
    private bool _loadingPreservesPlaybackState;
    private bool _loadingScopeInitialized;
    private bool _pendingPreservesPlaybackState;
    private float? _pendingSeek;
    private bool _pendingSeekIsRefresh;
    private string? _loadedSourcePath;
    private string? _loadedQuality;
    private VfxPreviewRootSelection? _loadedRootSelection;
    private VfxPreviewLayerIdentity? _identification;
    private bool _disposed;
    private float _time;
    private float _continuousPreviewEnd;
    private bool _attachmentMoved;
    private bool _hasPreviousAttachment;
    private Matrix _previousAttachment;
    private bool _isPlaying = true;
    private bool _isScrubbing;
    private bool _loop = true;
    private (float Time, float Duration, bool Loaded, bool Playing, bool Scrubbing, bool Loop)? _playbackState;
    public Func<Matrix> Attachment { get; set; } = () => Matrix.Identity;
    public Func<float, Matrix>? AttachmentAtTime { get; set; }
    public Func<float, float?>? NextAttachmentSample { get; set; }
    public Func<bool>? IsAttachmentHistoryConstant { get; set; }
    public float CurrentTime => _time;
    public bool EnableContinuousTrailPreview { get; set; }
    public bool EnableInteractiveReload { get; set; }
    public bool IsContinuousTrailPreview => EnableContinuousTrailPreview && Loop && _attachmentMoved
        && IsLoaded && _preview?.HasMotionTrailEmitters == true;
    public float Duration => IsContinuousTrailPreview ? Math.Max(_preview!.CycleDuration, _continuousPreviewEnd)
        : _preview?.CycleDuration ?? 0;
    public bool IsLoaded => _preview?.EmitterCount > 0;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { if (_disposed || _isPlaying == value) return; _isPlaying = value; PublishPlaybackChanged(); }
    }
    public bool IsScrubbing
    {
        get => _isScrubbing;
        set { if (_disposed || _isScrubbing == value) return; _isScrubbing = value; PublishPlaybackChanged(); }
    }
    public bool Loop
    {
        get => _loop;
        set { if (_disposed || _loop == value) return; _loop = value; PublishPlaybackChanged(); }
    }
    public bool IncludeInPhotoCapture => false;
    public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; private set; } = [];
    public VfxPreviewEditorInfo EditorInfo => _preview?.EditorInfo ?? VfxPreviewEditorInfo.Empty;
    public int EmitterCount => _preview?.EmitterCount ?? 0;
    public int TotalEmitterCount => _preview?.TotalEmitterCount ?? 0;
    public int DisabledEmitterCount => _preview?.DisabledEmitterCount ?? 0;
    public BoundingBox? PreviewBounds => _preview?.PreviewBounds;
    public event Action? Reloaded;
    public event Action? SnapshotReloaded;
    public event Action? PlaybackChanged;
    public event Action<float, bool>? PreparingFrame;

    public void Reload(VfxPreviewSnapshot snapshot) => Reload(snapshot, preservePlaybackState: false);

    public void Reload(VfxPreviewSnapshot snapshot, bool preservePlaybackState)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var preserve = preservePlaybackState && _preview != null
            && (_pending == null || _pendingPreservesPlaybackState)
            && string.Equals(_loadedSourcePath, snapshot.SourcePath, StringComparison.OrdinalIgnoreCase)
            && _loadedQuality == snapshot.Quality && SameSelection(_loadedRootSelection, snapshot.RootSelection);
        _pending = snapshot;
        _pendingPreservesPlaybackState = preserve;
        if (preserve)
        {
            if (_pendingSeek == null)
            {
                _pendingSeek = _time;
                _pendingSeekIsRefresh = true;
            }
        }
        else
        {
            _pendingSeek = null;
            _pendingSeekIsRefresh = false;
        }
    }

    private static bool SameSelection(VfxPreviewRootSelection? previous, VfxPreviewRootSelection? current)
        => previous == null || current == null ? previous == current
            : previous.EmitterIds.ToHashSet(StringComparer.Ordinal).SetEquals(current.EmitterIds)
                && previous.ReferenceNames.ToHashSet(StringComparer.Ordinal).SetEquals(current.ReferenceNames);

    public override void Update(GameTime gameTime)
    {
        if (_disposed) return;
        try
        {
            if (_pending != null)
            {
                var snapshot = _pending; _pending = null;
                var preserve = _pendingPreservesPlaybackState;
                _pendingPreservesPlaybackState = false;
                _preview ??= factory.Create(devices.Device);
                _preview.EnableInteractivePrewarm();
                _preview.ConfigureInteractiveSeekHistory(AttachmentAtTime == null || IsAttachmentHistoryConstant?.Invoke() == true);
                _loading = null;
                _loadingScopeInitialized = false;
                if (!EnableInteractiveReload || _preview.BeginInteractiveReload(snapshot))
                {
                    if (!EnableInteractiveReload) _preview.Reload(snapshot);
                    CompleteReload(snapshot, preserve);
                }
                else
                {
                    _loading = snapshot;
                    _loadingPreservesPlaybackState = preserve;
                    if (_pendingSeekIsRefresh) { _pendingSeek = null; _pendingSeekIsRefresh = false; }
                    if (!preserve)
                    {
                        ResetPlaybackScope();
                        _loadingScopeInitialized = true;
                        PreparingFrame?.Invoke(_pendingSeek ?? 0, false);
                    }
                }
            }
            if (_preview == null) { PublishPlaybackChanged(); return; }
            if (_loading is { } loading)
            {
                var reloadTarget = _pendingSeek ?? (_loadingPreservesPlaybackState ? _time : 0);
                if (_preview.ContinueInteractiveReload(new(reloadTarget, Attachment(), AttachmentAtTime,
                    InitializeDuration: true, UseCurrentAttachmentForFrame: AttachmentAtTime != null,
                    ContinuousTrail: IsContinuousTrailPreview, NextAttachmentSample: NextAttachmentSample,
                    GroundHeight: Attachment().Translation.Y)))
                {
                    var preserve = _loadingPreservesPlaybackState;
                    _loading = null;
                    CompleteReload(loading, preserve);
                    if (_pendingSeek == null) { _pendingSeek = preserve ? _time : 0; _pendingSeekIsRefresh = true; }
                }
                else if (!IsLoaded) { PublishPlaybackChanged(); return; }
            }
            var wasPreparing = _preview.IsPreparing || _preview.IsSeekingFrame;
            _preview.ConfigureInteractiveSeekHistory(AttachmentAtTime == null || IsAttachmentHistoryConstant?.Invoke() == true);
            ConfigureSeekFrame(Attachment());
            wasPreparing |= _preview.IsPreparing || _preview.IsSeekingFrame;
            if (_preview.IsSeekingFrame && _pendingSeek is { } newerSeek)
            {
                _time = Math.Clamp(newerSeek, 0, Math.Max(0, Duration));
                var refresh = _pendingSeekIsRefresh;
                _pendingSeek = null; _pendingSeekIsRefresh = false;
                PrepareFrame(!refresh);
            }
            var previousBounds = _preview.PreviewBounds;
            if (wasPreparing && !_hasPreviousAttachment) PreparingFrame?.Invoke(_time, false);
            _preview.PrepareInteractiveFrame(Attachment(), AttachmentAtTime);
            if (_preview.IsPreparing || _preview.IsSeekingFrame) { PublishPlaybackChanged(); return; }
            if (wasPreparing || previousBounds != _preview.PreviewBounds)
            {
                Diagnostics = _preview.Diagnostics;
                Reloaded?.Invoke();
            }
            var duration = _preview.CycleDuration;
            var requested = _pendingSeek; _pendingSeek = null;
            var contentRefresh = _pendingSeekIsRefresh; _pendingSeekIsRefresh = false;
            var attachment = Attachment();
            if (requested == null && _hasPreviousAttachment && attachment != _previousAttachment) _attachmentMoved = true;
            var continuous = IsContinuousTrailPreview;
            var clampedToAuthoredEnd = !continuous && _time > duration;
            if (clampedToAuthoredEnd) _time = Math.Max(0, duration);
            var seconds = wasPreparing ? 0 : Math.Max((float)gameTime.ElapsedGameTime.TotalSeconds, 0);
            var advancing = requested == null && IsPlaying && !IsScrubbing && seconds > 0;
            var elapsed = advancing ? continuous ? seconds : Math.Min(seconds, Math.Max(0, duration - _time)) : 0;
            _time = requested is { } target ? Math.Clamp(target, 0, Math.Max(0, Duration)) : _time + elapsed;
            if (continuous) _continuousPreviewEnd = Math.Max(_continuousPreviewEnd, _time);
            // Re-evaluating edited content must not cancel the current attachment gesture.
            PrepareFrame(requested != null && !contentRefresh || clampedToAuthoredEnd);
            // A moving trail keeps one simulation; ordinary effects retain their authored loop boundary.
            if (!continuous && advancing && duration > 0 && _time >= duration)
            {
                if (Loop) { _time = 0; PrepareFrame(false); }
                else _isPlaying = false;
            }
            if (!ReferenceEquals(Diagnostics, _preview.Diagnostics))
            {
                Diagnostics = _preview.Diagnostics;
                Reloaded?.Invoke();
            }
            // Sample after explicit/history seeks so replayed attachments do not look like a new drag.
            _previousAttachment = Attachment();
            _hasPreviousAttachment = true;
            PublishPlaybackChanged();
        }
        catch (Exception exception) { DisableAfterFailure(exception); }
    }

    private void CompleteReload(VfxPreviewSnapshot snapshot, bool preserve)
    {
        _preview!.Identify(_identification);
        _loadedSourcePath = snapshot.SourcePath;
        _loadedQuality = snapshot.Quality;
        _loadedRootSelection = snapshot.RootSelection;
        Diagnostics = _preview.Diagnostics;
        if (!preserve && !_loadingScopeInitialized) ResetPlaybackScope();
        _loadingScopeInitialized = false;
        Reloaded?.Invoke();
    }

    private void ResetPlaybackScope()
    {
        _time = 0;
        _continuousPreviewEnd = 0;
        _attachmentMoved = false;
        _hasPreviousAttachment = false;
        SnapshotReloaded?.Invoke();
    }

    public void Seek(float seconds)
    {
        if (_disposed) return;
        if (!float.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        _pendingSeek = Math.Max(0, seconds);
        _pendingSeekIsRefresh = false;
    }

    public void Restart() => Seek(0);

    public void BeginIdentification(VfxPreviewLayerIdentity layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_disposed || _identification == layer) return;
        _identification = layer;
        _preview?.Identify(layer);
    }

    public void EndIdentification()
    {
        if (_disposed || _identification == null) return;
        _identification = null;
        _preview?.Identify(null);
    }

    private void PrepareFrame(bool explicitSeek)
    {
        PreparingFrame?.Invoke(_time, explicitSeek);
        var attachment = Attachment();
        ConfigureSeekFrame(attachment, explicitSeek || IsScrubbing || !IsPlaying);
        if (IsContinuousTrailPreview)
        {
            if (NextAttachmentSample is { } nextSample) _preview?.SeekContinuousTrail(_time, attachment, AttachmentAtTime, nextSample);
            else _preview?.SeekContinuousTrail(_time, attachment, AttachmentAtTime);
        }
        else if (AttachmentAtTime is { } sample) _preview?.Seek(_time, attachment, sample, useCurrentAttachmentForFrame: true);
        else _preview?.Seek(_time, attachment);
    }

    private void ConfigureSeekFrame(Matrix attachment, bool allowForwardReplay = true)
    {
        if (EnableInteractiveReload)
            _preview?.ConfigureInteractiveSeekFrame(AttachmentAtTime == null || IsAttachmentHistoryConstant?.Invoke() == true,
                attachment.Translation.Y, initializeDuration: true, allowForwardReplay: allowForwardReplay);
    }

    private void PublishPlaybackChanged()
    {
        var state = (CurrentTime, Duration, IsLoaded, IsPlaying, IsScrubbing, Loop);
        if (_playbackState is { } previous && previous == state) return;
        _playbackState = state;
        PlaybackChanged?.Invoke();
    }

    public override void Draw(GameTime gameTime)
    {
        if (!_disposed && _preview?.EmitterCount > 0) renderer.AddRenderItem(RenderBuckedId.Normal, this);
    }

    void IRenderItem.Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
    {
        if (_disposed || renderingTechnique == RenderingTechnique.Emissive) return;
        try { _preview?.Draw(parameters with { VfxGroundHeight = Attachment().Translation.Y }); }
        catch (Exception exception) { DisableAfterFailure(exception); }
    }

    private void DisableAfterFailure(Exception exception)
    {
        Logging.Create<VfxPreviewComponent>().Error(exception, "VFX scene preview disabled");
        Stop();
        Diagnostics = [new("", "", "", "Vfx.Preview.RuntimeUnavailable")];
        Reloaded?.Invoke();
    }

    public void Stop()
    {
        _pending = null;
        _loading = null;
        _loadingPreservesPlaybackState = false;
        _loadingScopeInitialized = false;
        _pendingPreservesPlaybackState = false;
        _pendingSeek = null;
        _pendingSeekIsRefresh = false;
        _loadedSourcePath = null;
        _loadedQuality = null;
        _loadedRootSelection = null;
        _identification = null;
        _preview?.Dispose();
        _preview = null;
        _time = 0;
        _continuousPreviewEnd = 0;
        _attachmentMoved = false;
        _hasPreviousAttachment = false;
        _isPlaying = false;
        _isScrubbing = false;
        Diagnostics = [];
        PublishPlaybackChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        Reloaded = null;
        SnapshotReloaded = null;
        PlaybackChanged = null;
        PreparingFrame = null;
        AttachmentAtTime = null;
        IsAttachmentHistoryConstant = null;
    }
}
