using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances;

// The existing marker owns editing/selection. The particle child owns only transient preview resources.
public sealed class EffectMetaInstance : IMetaDataInstance, IMetaDataPlaybackInstance, IMetaDataMarkerPreview
{
    private readonly DrawableMetaInstance _marker;
    private readonly VfxPreviewSceneNode _particles;
    private readonly float _start;
    private readonly bool _instantTrigger;
    private readonly bool _persistentOwner;
    private readonly AnimationPlayer? _ownerPlayer;
    private readonly float? _rangeEmissionEnd;
    private AnimationPlaybackTime? _previousPlayback;
    private double _cycleOriginSeconds;
    private double _playbackOriginSeconds;
    private readonly List<double> _cycleStarts = [];
    private bool _showForEntireAnimation;
    public event Action<EffectMetaInstance>? DiagnosticsChanged;
    public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => _particles.Diagnostics;
    public bool DependsOn(object file) => _particles.DependsOn(file);
    public ParsedMetadataAttribute Source => _marker.Source;
    public AnimationPlayer Player => _marker.Player;
    public bool IsEnabled { get => _marker.IsEnabled; set => _marker.IsEnabled = value; }
    public bool IsSelected { get => _marker.IsSelected; set => _marker.IsSelected = value; }
    public bool ShowForEntireAnimation
    {
        get => _showForEntireAnimation;
        set { _showForEntireAnimation = value; ApplyMarkerVisibility(); }
    }
    public bool IsHitTestVisible => _marker.IsHitTestVisible;
    public bool IsHovered { get => _marker.IsHovered; set => _marker.IsHovered = value; }
    public float HitTestRadius => _marker.HitTestRadius;
    public IReadOnlyList<MetaDataMarkerHitTarget> HitTargets => _marker.HitTargets;
    public Vector3 FocusPosition => _marker.FocusPosition;
    public Matrix ReferenceWorldTransform => _marker.ReferenceWorldTransform;
    public Matrix WorldTransform => _marker.WorldTransform;
    public int? HighlightedBoneIndex => _marker.HighlightedBoneIndex;

    public EffectMetaInstance(DrawableMetaInstance marker, VfxPreviewSceneNode particles, MetaDataTimeRange? range,
        bool persistentOwner = false, AnimationPlayer? ownerPlayer = null)
    {
        _marker = marker; _particles = particles; _start = range?.StartTime ?? 0;
        _instantTrigger = range is { } time && !time.IsWholeAnimationRange && float.IsFinite(time.StartTime)
            && float.IsFinite(time.EndTime) && Math.Abs(time.EndTime - time.StartTime) <= 0.000001f;
        _persistentOwner = persistentOwner;
        _ownerPlayer = ownerPlayer;
        if (range is { IsWholeAnimationRange: false } interval && !_instantTrigger
            && float.IsFinite(interval.EndTime) && interval.EndTime > _start)
            _rangeEmissionEnd = interval.EndTime - _start;
        particles.DiagnosticsChanged += OnDiagnosticsChanged;
    }
    private void OnDiagnosticsChanged() => DiagnosticsChanged?.Invoke(this);
    public void Update(float currentTime)
    {
        var revision = _previousPlayback?.Revision ?? 0;
        if (_previousPlayback is { } previous && currentTime < previous.ClipTimeSeconds) revision++;
        Update(new AnimationPlaybackTime(currentTime, currentTime, revision, 0, false, false));
    }
    public void Update(AnimationPlaybackTime playback)
    {
        var previous = _previousPlayback;
        var reset = previous is { } old && (old.Revision != playback.Revision || old.IsStopped && !playback.IsStopped);
        if (previous == null || reset)
        {
            _playbackOriginSeconds = _cycleOriginSeconds = playback.ElapsedSeconds - playback.ClipTimeSeconds;
            _cycleStarts.Clear();
            _cycleStarts.Add(_cycleOriginSeconds);
        }
        if (reset || playback.IsStopped && previous is not { IsStopped: true })
            _particles.ResetPlayback();
        var continuousOwner = _persistentOwner && _instantTrigger && _particles.HasInfiniteEmitters;
        if (!reset && previous is { } prior && prior.CycleIndex != playback.CycleIndex && !continuousOwner)
        {
            var clipEnd = (float)(_ownerPlayer?.Duration.TotalSeconds ?? prior.ClipTimeSeconds);
            _particles.RetireCurrentPlayback(Math.Max(0, clipEnd - _start));
        }
        if (previous is { } oldCycle && oldCycle.CycleIndex != playback.CycleIndex)
        {
            _cycleOriginSeconds = playback.ElapsedSeconds - playback.ClipTimeSeconds;
            _cycleStarts.Add(_cycleOriginSeconds);
        }
        if (!reset && previous is { } last && !playback.IsStopped)
            _particles.AdvancePlaybackTails((float)Math.Max(0, playback.ElapsedSeconds - last.ElapsedSeconds));
        _previousPlayback = playback;
        var currentTime = playback.ClipTimeSeconds;
        var duration = (float)(_ownerPlayer?.Duration.TotalSeconds ?? 0);
        var reachedStart = continuousOwner
            ? playback.ElapsedSeconds - _playbackOriginSeconds >= _start - .000001
            : currentTime >= _start - .000001f;
        _particles.CurrentPlaybackEnabled = !playback.IsStopped && (reachedStart || _showForEntireAnimation);
        _particles.EmissionEndTimeSeconds = _rangeEmissionEnd
            ?? (!continuousOwner && duration > 0 ? Math.Max(0, duration - _start) : null);
        _particles.TimeSeconds = Math.Max(0, (float)(playback.ElapsedSeconds
            - (continuousOwner ? _playbackOriginSeconds : _cycleOriginSeconds)) - _start);
        _particles.AttachmentSampleTimeSeconds = currentTime - _start;
        _particles.AttachmentHistoryTime = continuousOwner && duration > 0
            ? past => SampleContinuousClipTime(past, duration)
            : null;
        ApplyMarkerVisibility();
        _marker.Update(currentTime);
    }
    private void ApplyMarkerVisibility()
    {
        // META End stops emission; existing particles and triggered events retire themselves.
        _marker.ShowForEntireAnimation = _showForEntireAnimation || _particles.HasPlaybackContent;
    }
    private float SampleContinuousClipTime(float age, float duration)
    {
        var elapsed = _playbackOriginSeconds + age + _start;
        var index = _cycleStarts.BinarySearch(elapsed);
        if (index < 0) index = ~index - 1;
        return (float)Math.Clamp(elapsed - _cycleStarts[Math.Max(0, index)], 0, duration) - _start;
    }
    public void CleanUp()
    {
        _particles.DiagnosticsChanged -= OnDiagnosticsChanged;
        _particles.Dispose();
        _marker.CleanUp();
        DiagnosticsChanged = null;
    }
}
