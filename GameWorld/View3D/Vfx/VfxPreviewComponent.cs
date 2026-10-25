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
    private float? _pendingSeek;
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
    public float CurrentTime => _time;
    public bool EnableContinuousTrailPreview { get; set; }
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
    public int EmitterCount => _preview?.EmitterCount ?? 0;
    public int TotalEmitterCount => _preview?.TotalEmitterCount ?? 0;
    public int DisabledEmitterCount => _preview?.DisabledEmitterCount ?? 0;
    public BoundingBox? PreviewBounds => _preview?.PreviewBounds;
    public event Action? Reloaded;
    public event Action? SnapshotReloaded;
    public event Action? PlaybackChanged;
    public event Action<float, bool>? PreparingFrame;

    public void Reload(VfxPreviewSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pending = snapshot;
        _pendingSeek = null;
    }

    public override void Update(GameTime gameTime)
    {
        if (_disposed) return;
        try
        {
            if (_pending != null)
            {
                var snapshot = _pending; _pending = null;
                _preview ??= factory.Create(devices.Device);
                _preview.Reload(snapshot);
                Diagnostics = _preview.Diagnostics;
                _time = 0;
                _continuousPreviewEnd = 0;
                _attachmentMoved = false;
                _hasPreviousAttachment = false;
                SnapshotReloaded?.Invoke();
                Reloaded?.Invoke();
            }
            if (_preview == null) { PublishPlaybackChanged(); return; }
            var duration = _preview.CycleDuration;
            var requested = _pendingSeek; _pendingSeek = null;
            var attachment = Attachment();
            if (requested == null && _hasPreviousAttachment && attachment != _previousAttachment) _attachmentMoved = true;
            var continuous = IsContinuousTrailPreview;
            var clampedToAuthoredEnd = !continuous && _time > duration;
            if (clampedToAuthoredEnd) _time = Math.Max(0, duration);
            var seconds = Math.Max((float)gameTime.ElapsedGameTime.TotalSeconds, 0);
            var advancing = requested == null && IsPlaying && !IsScrubbing && seconds > 0;
            var elapsed = advancing ? continuous ? seconds : Math.Min(seconds, Math.Max(0, duration - _time)) : 0;
            _time = requested is { } target ? Math.Clamp(target, 0, Math.Max(0, Duration)) : _time + elapsed;
            if (continuous) _continuousPreviewEnd = Math.Max(_continuousPreviewEnd, _time);
            PrepareFrame(requested != null || clampedToAuthoredEnd);
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

    public void Seek(float seconds)
    {
        if (_disposed) return;
        if (!float.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        _pendingSeek = Math.Max(0, seconds);
    }

    public void Restart() => Seek(0);

    private void PrepareFrame(bool explicitSeek)
    {
        PreparingFrame?.Invoke(_time, explicitSeek);
        var attachment = Attachment();
        if (IsContinuousTrailPreview) _preview?.SeekContinuousTrail(_time, attachment, AttachmentAtTime);
        else if (AttachmentAtTime is { } sample) _preview?.Seek(_time, attachment, sample, useCurrentAttachmentForFrame: true);
        else _preview?.Seek(_time, attachment);
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
        _pendingSeek = null;
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
    }
}
