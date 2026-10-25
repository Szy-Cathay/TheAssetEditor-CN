using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances;

// The existing marker owns editing/selection. The particle child owns only transient preview resources.
public sealed class EffectMetaInstance : IMetaDataInstance, IMetaDataMarkerPreview
{
    private readonly DrawableMetaInstance _marker;
    private readonly VfxPreviewSceneNode _particles;
    private readonly float _start;
    private readonly bool _instantTrigger;
    private float _currentTime;
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

    public EffectMetaInstance(DrawableMetaInstance marker, VfxPreviewSceneNode particles, MetaDataTimeRange? range)
    {
        _marker = marker; _particles = particles; _start = range?.StartTime ?? 0;
        _instantTrigger = range is { } time && !time.IsWholeAnimationRange && float.IsFinite(time.StartTime)
            && float.IsFinite(time.EndTime) && Math.Abs(time.EndTime - time.StartTime) <= 0.000001f;
        particles.DiagnosticsChanged += OnDiagnosticsChanged;
    }
    private void OnDiagnosticsChanged() => DiagnosticsChanged?.Invoke(this);
    public void Update(float currentTime)
    {
        _currentTime = currentTime;
        ApplyMarkerVisibility();
        _marker.Update(currentTime);
        _particles.TimeSeconds = Math.Max(0, currentTime - _start);
    }
    private void ApplyMarkerVisibility()
    {
        // A point event starts the VFX; it must not clip the effect to one animation instant.
        var elapsed = _currentTime - _start;
        var tail = _instantTrigger && elapsed >= -0.000001f
            && elapsed <= (_particles.Duration ?? float.PositiveInfinity) + 0.000001f;
        _marker.ShowForEntireAnimation = _showForEntireAnimation || tail;
    }
    public void CleanUp()
    {
        _particles.DiagnosticsChanged -= OnDiagnosticsChanged;
        _particles.Dispose();
        _marker.CleanUp();
        DiagnosticsChanged = null;
    }
}
