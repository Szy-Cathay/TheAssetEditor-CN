using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Editors.VfxEditor;

// VFX-only interaction: no model selection manager, editable mesh or document mutation.
public sealed class VfxEmitterInteractionComponent : BaseComponent, IDisposable
{
    private readonly IMouseComponent _mouse;
    private readonly IKeyboardComponent _keyboard;
    private readonly ArcBallCamera _camera;
    private readonly IDeviceResolver _devices;
    private readonly RenderEngineComponent _renderer;
    private readonly VfxPreviewComponent _preview;
    private Gizmo? _gizmo;
    private BasicEffect? _selectionMarker;
    private bool _isBoxSelecting;
    private bool _selectionBeforeBox;
    private Vector2 _selectionStart;
    private Vector2 _selectionEnd;
    private bool _disposed;
    private bool _hasPrepared;
    private float _previousTime;
    private Matrix _previousAttachment = Matrix.Identity;
    private Matrix _transformBefore = Matrix.Identity;
    private GizmoMode _mode = GizmoMode.Translate;
    public VfxPreviewTransform Target { get; } = new();
    public VfxAttachmentHistory History { get; } = new();
    public bool IsSelected { get; private set; }
    public bool HasActiveInteraction => Target.IsTransformActive || _isBoxSelecting;
    public event Action? SelectionChanged;
    public event Action<Matrix, Matrix>? TransformCommitted;

    public VfxEmitterInteractionComponent(IMouseComponent mouse, IKeyboardComponent keyboard, ArcBallCamera camera,
        IDeviceResolver devices, RenderEngineComponent renderer, VfxPreviewComponent preview)
    {
        _mouse = mouse; _keyboard = keyboard; _camera = camera; _devices = devices; _renderer = renderer; _preview = preview;
        UpdateOrder = (int)ComponentUpdateOrderEnum.Gizmo;
        DrawOrder = (int)ComponentDrawOrderEnum.Gizmo;
        _preview.Attachment = () => Target.World;
        _preview.AttachmentAtTime = History.Sample;
        _preview.NextAttachmentSample = History.NextSampleTime;
        _preview.IsAttachmentHistoryConstant = () => History.IsConstant;
        _preview.PreparingFrame += PrepareAttachment;
        _preview.SnapshotReloaded += ResetHistory;
        _mouse.CaptureInterrupted += CancelTransform;
    }

    public override void Initialize()
    {
        if (_gizmo != null) return;
        _gizmo = new Gizmo(_camera, _mouse, _devices.Device, _renderer) { ActivePivot = PivotType.ObjectCenter, ActiveMode = _mode };
        if (IsSelected) _gizmo.Selection.Add(Target);
        _gizmo.SetKeyboard(_keyboard);
        _gizmo.StartEvent += BeginTransform;
        _gizmo.StopEvent += EndTransform;
        _gizmo.ReplacePreviewFromInitialRequested += ApplyTransform;
        base.Initialize();
    }

    public void Select(bool selected = true)
    {
        if (_disposed || selected == IsSelected) return;
        CancelTransform();
        IsSelected = selected;
        _gizmo?.Selection.Clear();
        if (selected) _gizmo?.Selection.Add(Target);
        SelectionChanged?.Invoke();
    }

    public void SetGizmoMode(GizmoMode mode)
    {
        CancelTransform();
        Select();
        _mode = mode;
        if (_gizmo != null) _gizmo.ActiveMode = mode;
    }

    public void ResetTransform()
    {
        CancelTransform();
        var before = Target.World;
        Target.Set(Matrix.Identity);
        if (before != Target.World) TransformCommitted?.Invoke(before, Target.World);
    }

    public void RestoreTransform(Matrix matrix)
    {
        CancelTransform();
        Target.Set(matrix);
        var time = _preview.CurrentTime;
        // An undo changes the current attachment, not the lifetime or position of existing world particles.
        History.Record(time, matrix);
        _previousTime = time;
        _previousAttachment = matrix;
        _hasPrepared = true;
    }

    public bool CancelActiveInteraction()
    {
        if (!HasActiveInteraction) return false;
        CancelTransform();
        return true;
    }

    public void BeginTransform()
    {
        if (!Target.IsTransformActive) _transformBefore = Target.World;
        Target.Begin();
        _mouse.MouseOwner = this;
    }

    public void ApplyTransform(ModalPreviewReplacement replacement) => Target.Replace(replacement);

    private void EndTransform()
    {
        try
        {
            if (_gizmo?.IsModalCancelled == true) Target.Cancel();
            else
            {
                var active = Target.IsTransformActive;
                var after = Target.World;
                Target.Commit();
                if (active && _transformBefore != after) TransformCommitted?.Invoke(_transformBefore, after);
            }
        }
        finally
        {
            if (_gizmo != null) _gizmo.IsModalCancelled = false;
            ReleaseMouse();
        }
    }

    public void CancelTransform()
    {
        if (_isBoxSelecting) { EndBoxSelection(_selectionBeforeBox); return; }
        try
        {
            if (_gizmo?.IsInModalTransform == true) _gizmo.CancelModalTransform();
            else Target.Cancel();
        }
        finally { ReleaseMouse(); }
    }

    private void ReleaseMouse()
    {
        if (_mouse.MouseOwner != this) return;
        _mouse.MouseOwner = null;
        _mouse.ClearStates();
    }

    public void PrepareAttachment(float time, bool explicitSeek)
    {
        if (!_hasPrepared || (!explicitSeek && time < _previousTime))
        {
            History.Reset(Target.World);
            History.Record(time, Target.World);
        }
        else if (explicitSeek)
        {
            CancelTransform();
            Target.Set(History.SampleCurrent(time));
        }
        else if (Target.World != _previousAttachment)
        {
            // Editing after a rewind starts a new path from that point, without moving old world particles.
            if (_previousTime < History.EndTime) History.Truncate(_previousTime);
            History.Record(time, Target.World);
        }
        else if (_previousTime < History.EndTime)
        {
            Target.Set(History.SampleCurrent(time));
            if (time > History.EndTime) History.Record(time, Target.World);
        }
        else History.Record(time, Target.World);
        _previousTime = time; _previousAttachment = Target.World; _hasPrepared = true;
    }

    private void ResetHistory()
    {
        CancelTransform();
        History.Reset(Target.World);
        _hasPrepared = false;
    }

    public override void Update(GameTime gameTime)
    {
        try { UpdateInteraction(gameTime); }
        catch
        {
            try { CancelTransform(); }
            catch { /* Preserve the original input failure after attempting to restore the gesture. */ }
            throw;
        }
    }

    private void UpdateInteraction(GameTime gameTime)
    {
        if (_disposed || _gizmo == null) return;
        if (!_preview.IsLoaded) { CancelTransform(); Select(false); return; }
        if (_mouse.MouseOwner != null && _mouse.MouseOwner != this)
        {
            if (_isBoxSelecting) CancelTransform();
            return;
        }
        if (_isBoxSelecting) { UpdateBoxSelection(); return; }
        if (_gizmo.JustFinishedModalTransform) { _gizmo.ClearJustFinishedFlag(); return; }
        var alt = _keyboard.IsKeyDownOrReleased(Keys.LeftAlt) || _keyboard.IsKeyDownOrReleased(Keys.RightAlt);
        var control = _keyboard.IsKeyDownOrReleased(Keys.LeftControl) || _keyboard.IsKeyDownOrReleased(Keys.RightControl);
        _gizmo.SnapEnabled = _keyboard.IsKeyDown(Keys.LeftControl) || _keyboard.IsKeyDown(Keys.RightControl);
        if (_gizmo.IsInModalTransform)
        {
            if (_gizmo.ActiveMode == GizmoMode.Rotate && !_gizmo.IsInNumericInput && _keyboard.IsKeyPressed(Keys.R))
                _gizmo.ToggleTrackballRotation();
            _gizmo.Update(gameTime, true);
            return;
        }
        if (alt) return;
        if (IsSelected && !control)
        {
            var mode = _keyboard.IsKeyPressed(Keys.G) ? GizmoMode.Translate
                : _keyboard.IsKeyPressed(Keys.R) ? GizmoMode.Rotate
                : _keyboard.IsKeyPressed(Keys.S) ? GizmoMode.NonUniformScale : (GizmoMode?)null;
            if (mode is { } value)
            {
                var key = value == GizmoMode.Translate ? Keys.G : value == GizmoMode.Rotate ? Keys.R : Keys.S;
                _gizmo.StartModalTransform(value, _keyboard.GetKeyPressPosition(key));
                if (value == GizmoMode.Rotate && _keyboard.GetKeyPressCount(Keys.R) > 1 && _keyboard.GetKeyPressCount(Keys.R) % 2 == 0)
                    _gizmo.ToggleTrackballRotation();
                _gizmo.Update(gameTime, true);
                return;
            }
        }
        if (_mouse.IsMouseButtonPressed(MouseButton.Left))
        {
            var origin = _mouse.GetPressPosition(MouseButton.Left) ?? _mouse.Position();
            if (IsSelected && (HitOrigin(origin) || HitBounds(origin)))
            {
                _gizmo.StartModalTransform(GizmoMode.Translate, origin, confirmOnMouseRelease: true);
                _gizmo.Update(gameTime, true);
                return;
            }
            if (HitOrigin(origin) || HitBounds(origin)) Select();
            else
            {
                var selected = IsSelected;
                Select(false);
                _selectionBeforeBox = selected;
                _selectionStart = origin;
                _selectionEnd = _mouse.Position();
                _isBoxSelecting = true;
                _mouse.MouseOwner = this;
                UpdateBoxSelection();
                return;
            }
        }
        // All pointer transforms use the shared modal path, never the old incremental drag branch.
        if (IsSelected) _gizmo.Update(gameTime, false);
    }

    private bool HitOrigin(Vector2 point)
    {
        var projected = _camera.InputViewport.Project(Target.Position, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
        return projected.Z is >= 0 and <= 1 && Vector2.DistanceSquared(point, new(projected.X, projected.Y)) <= 144;
    }

    private void UpdateBoxSelection()
    {
        if (_keyboard.IsKeyReleased(Keys.Escape) || _mouse.IsMouseButtonPressed(MouseButton.Right)
            || _keyboard.IsKeyDownOrReleased(Keys.LeftAlt) || _keyboard.IsKeyDownOrReleased(Keys.RightAlt))
        {
            EndBoxSelection(_selectionBeforeBox);
            return;
        }
        _selectionEnd = _mouse.Position();
        if (!_mouse.IsMouseButtonReleased(MouseButton.Left)) return;
        var rectangle = CreateSelectionRectangle();
        EndBoxSelection(rectangle.Width >= 4 && rectangle.Height >= 4
            && MarkerScreenBounds() is { } marker && rectangle.Intersects(marker));
    }

    private void EndBoxSelection(bool selected)
    {
        _isBoxSelecting = false;
        try { Select(selected); }
        finally { ReleaseMouse(); }
    }

    private Rectangle CreateSelectionRectangle() => new(
        (int)MathF.Min(_selectionStart.X, _selectionEnd.X), (int)MathF.Min(_selectionStart.Y, _selectionEnd.Y),
        (int)MathF.Abs(_selectionEnd.X - _selectionStart.X), (int)MathF.Abs(_selectionEnd.Y - _selectionStart.Y));

    private Rectangle? MarkerScreenBounds()
    {
        var vertices = CreateSelectionMarker();
        if (vertices.Length == 0) return null;
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        foreach (var vertex in vertices)
        {
            var projected = _camera.InputViewport.Project(vertex.Position, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            if (!float.IsFinite(projected.X) || !float.IsFinite(projected.Y) || projected.Z is not (>= 0 and <= 1)) return null;
            min = Vector2.Min(min, new(projected.X, projected.Y));
            max = Vector2.Max(max, new(projected.X, projected.Y));
        }
        var left = (int)MathF.Floor(min.X); var top = (int)MathF.Floor(min.Y);
        return new(left, top, (int)MathF.Ceiling(max.X) - left, (int)MathF.Ceiling(max.Y) - top);
    }

    private VertexPositionColor[] CreateSelectionMarker()
    {
        var viewport = _camera.InputViewport;
        if (viewport.Width <= 0 || viewport.Height <= 0) return [];
        var position = Target.Position;
        var projected = viewport.Project(position, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
        if (!float.IsFinite(projected.X) || !float.IsFinite(projected.Y) || projected.Z is not (>= 0 and <= 1)) return [];
        // Keep the wire cube small on screen, independent of effect size, scale and trail length.
        const float edgePixels = 14;
        var clip = Vector4.Transform(new Vector4(position, 1), _camera.ViewMatrix * _camera.ProjectionMatrix);
        var size = 2 * edgePixels * MathF.Abs(clip.W) / (viewport.Width * MathF.Abs(_camera.ProjectionMatrix.M11));
        if (!float.IsFinite(size) || size <= 0) return [];
        return LineHelper.CreateCube(Matrix.CreateScale(size) * Matrix.CreateTranslation(position), IsSelected ? Color.Yellow : Color.Gray);
    }

    public override void Draw(GameTime gameTime)
    {
        if (_disposed || !_preview.IsLoaded) return;
        var vertices = CreateSelectionMarker();
        var rectangle = CreateSelectionRectangle();
        var showBox = _isBoxSelecting && rectangle.Width >= 4 && rectangle.Height >= 4;
        if (vertices.Length == 0 && !showBox) return;
        var device = _devices.Device;
        _selectionMarker ??= new BasicEffect(device) { VertexColorEnabled = true };
        _selectionMarker.World = Matrix.Identity;
        _selectionMarker.View = _camera.ViewMatrix;
        _selectionMarker.Projection = _camera.ProjectionMatrix;
        var blend = device.BlendState;
        var depth = device.DepthStencilState;
        var rasterizer = device.RasterizerState;
        try
        {
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            if (vertices.Length != 0)
            {
                foreach (var pass in _selectionMarker.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserPrimitives(PrimitiveType.LineList, vertices, 0, vertices.Length / 2);
                }
            }
            if (showBox)
            {
                var a = new Vector3(rectangle.Left, rectangle.Top, 0);
                var b = new Vector3(rectangle.Right, rectangle.Top, 0);
                var c = new Vector3(rectangle.Right, rectangle.Bottom, 0);
                var d = new Vector3(rectangle.Left, rectangle.Bottom, 0);
                VertexPositionColor[] outline = [new(a, Color.Yellow), new(b, Color.Yellow), new(b, Color.Yellow), new(c, Color.Yellow),
                    new(c, Color.Yellow), new(d, Color.Yellow), new(d, Color.Yellow), new(a, Color.Yellow)];
                var viewport = _camera.InputViewport;
                _selectionMarker.View = Matrix.Identity;
                _selectionMarker.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
                foreach (var pass in _selectionMarker.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserPrimitives(PrimitiveType.LineList, outline, 0, outline.Length / 2);
                }
            }
        }
        finally
        {
            _selectionMarker.View = _camera.ViewMatrix;
            _selectionMarker.Projection = _camera.ProjectionMatrix;
            device.BlendState = blend;
            device.DepthStencilState = depth;
            device.RasterizerState = rasterizer;
        }
    }

    private bool HitBounds(Vector2 point)
    {
        if (_preview.PreviewBounds is not { } bounds) return false;
        var ray = _camera.CreateCameraRay(point);
        var inverse = Matrix.Invert(Target.World);
        var local = new Ray(Vector3.Transform(ray.Position, inverse), Vector3.TransformNormal(ray.Direction, inverse));
        return local.Intersects(bounds) != null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelTransform();
        _disposed = true;
        _mouse.CaptureInterrupted -= CancelTransform;
        _preview.PreparingFrame -= PrepareAttachment;
        _preview.SnapshotReloaded -= ResetHistory;
        _preview.Attachment = () => Matrix.Identity;
        _preview.AttachmentAtTime = null;
        _preview.NextAttachmentSample = null;
        _preview.IsAttachmentHistoryConstant = null;
        _gizmo?.Dispose();
        _selectionMarker?.Dispose();
        SelectionChanged = null;
        TransformCommitted = null;
    }
}
