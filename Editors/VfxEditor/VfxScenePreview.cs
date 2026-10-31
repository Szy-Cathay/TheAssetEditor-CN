using GameWorld.Core.Components;
using GameWorld.Core.Vfx;
using Shared.Core.Services;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.WpfWindow;

namespace Editors.VfxEditor;

public sealed class VfxScenePreview(IWpfGame game, IComponentInserter components, View3DCoreComponentSet core, VfxPreviewComponent preview,
    VfxEmitterInteractionComponent? interaction = null)
    : IDisposable
{
    private bool _started;
    private bool _disposed;
    private bool _framed;
    private ArcBallCamera? _camera;
    private (float Zoom, float Yaw, float Pitch, Vector3 Target, ProjectionType Projection)? _initialCamera;
    public IWpfGame Game { get; } = game;
    public VfxPreviewComponent Preview { get; } = preview;
    public bool IsEffectSelected => interaction?.IsSelected == true;
    public bool HasActiveInteraction => interaction?.HasActiveInteraction == true;
    public event Action<Matrix, Matrix>? TransformCommitted
    {
        add { if (interaction != null) interaction.TransformCommitted += value; }
        remove { if (interaction != null) interaction.TransformCommitted -= value; }
    }
    public bool CancelActiveInteraction() => interaction?.CancelActiveInteraction() == true;
    public void SetTransform(Matrix matrix) => interaction?.RestoreTransform(matrix);
    public void SelectEffect() => interaction?.Select();
    public void ResetTransform() => interaction?.ResetTransform();
    public void SetGizmoMode(GizmoMode mode) => interaction?.SetGizmoMode(mode);
    public void Start()
    {
        if (_disposed || _started) return;
        if (Game is D3D11Host host)
        {
            host.SuspendWhenHidden = true;
            host.UseAsynchronousPresentation = true;
            host.RenderingPreparation = VfxRuntimePreparation.PrepareAsync(typeof(VfxEmitterInteractionComponent),
                typeof(VfxScenePreview), typeof(VfxAttachmentHistory));
        }
        _camera = core.Components.OfType<ArcBallCamera>().SingleOrDefault();
        if (_camera != null)
        {
            _initialCamera = (_camera.Zoom, _camera.Yaw, _camera.Pitch, _camera.LookAt, _camera.CurrentProjectionType);
            _framed = _camera.Zoom != 10 || _camera.LookAt != Vector3.Zero
                || _camera.Yaw != MathHelper.ToRadians(26) || _camera.Pitch != MathHelper.ToRadians(-24)
                || _camera.CurrentProjectionType != ProjectionType.Perspective;
        }
        Preview.EnableContinuousTrailPreview = true;
        Preview.EnableInteractiveReload = true;
        Preview.Reloaded += FrameInitialContent;
        if (interaction != null) components.Execute(core, interaction, Preview);
        else components.Execute(core, Preview);
        _started = true;
    }
    public void Stop()
    {
        if (_disposed) return;
        interaction?.CancelTransform();
        Preview.Stop();
    }
    public void FrameContent()
    {
        if (_camera == null || Preview.PreviewBounds is not { } bounds) return;
        if (interaction != null) bounds = BoundingBox.CreateFromPoints(bounds.GetCorners().Select(corner => Vector3.Transform(corner, interaction.Target.World)));
        _camera.FrameBounds(bounds);
        _framed = true;
    }
    private void FrameInitialContent()
    {
        if (_framed || _camera == null || _initialCamera is not { } initial || Preview.EmitterCount == 0 || Preview.PreviewBounds is not { } bounds) return;
        if (interaction != null) bounds = BoundingBox.CreateFromPoints(bounds.GetCorners().Select(corner => Vector3.Transform(corner, interaction.Target.World)));
        // Preserve any explicit or user-adjusted view, including navigation while loading.
        _framed = true;
        if (_camera.Zoom == initial.Zoom && _camera.Yaw == initial.Yaw && _camera.Pitch == initial.Pitch && _camera.LookAt == initial.Target
            && _camera.CurrentProjectionType == initial.Projection)
            _camera.FrameBounds(bounds);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Preview.Reloaded -= FrameInitialContent;
        interaction?.Dispose();
        Preview.Dispose();
    }
}
