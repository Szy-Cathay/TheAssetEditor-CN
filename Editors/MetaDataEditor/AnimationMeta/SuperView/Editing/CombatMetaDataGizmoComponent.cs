using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Editors.AnimationMeta.SuperView.Editing;

public sealed class CombatMetaDataGizmoComponent : BaseComponent, IDisposable
{
    private readonly ArcBallCamera _camera;
    private readonly IMouseComponent _mouse;
    private readonly IKeyboardComponent _keyboard;
    private readonly RenderEngineComponent _renderEngine;
    private readonly IDeviceResolver _deviceResolver;
    private readonly CombatMetaDataEditSession _session;
    private readonly TargetAdapter _adapter = new();

    private Gizmo? _gizmo;
    private bool _dragActive;
    private bool _isEnabled;
    private CombatMetaDataTransformMode _transformMode;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            if (!value)
                InterruptDragInteraction();
        }
    }

    public CombatMetaDataTransformMode TransformMode
    {
        get => _transformMode;
        set
        {
            if (_transformMode == value)
                return;

            InterruptDragInteraction();
            _transformMode = value;
            if (_gizmo != null)
            {
                _gizmo.ActiveMode = value ==
                    CombatMetaDataTransformMode.Rotate
                        ? GizmoMode.Rotate
                        : GizmoMode.Translate;
                _gizmo.ResetDeltas();
            }
        }
    }

    public CombatMetaDataGizmoComponent(
        ArcBallCamera camera,
        IMouseComponent mouse,
        IKeyboardComponent keyboard,
        RenderEngineComponent renderEngine,
        IDeviceResolver deviceResolver,
        CombatMetaDataEditSession session)
    {
        _camera = camera;
        _mouse = mouse;
        _keyboard = keyboard;
        _renderEngine = renderEngine;
        _deviceResolver = deviceResolver;
        _session = session;

        UpdateOrder = (int)ComponentUpdateOrderEnum.Gizmo;
        DrawOrder = (int)ComponentDrawOrderEnum.Gizmo;
    }

    public override void Initialize()
    {
        _gizmo = new Gizmo(
            _camera,
            _mouse,
            _deviceResolver.Device,
            _renderEngine)
        {
            ActiveMode = _transformMode == CombatMetaDataTransformMode.Rotate
                ? GizmoMode.Rotate
                : GizmoMode.Translate,
            ActivePivot = PivotType.ObjectCenter,
            GizmoDisplaySpace = TransformSpace.Local
        };
        _gizmo.SetKeyboard(_keyboard);
        _gizmo.TranslateEvent += OnTranslate;
        _gizmo.RotateEvent += OnRotate;
        _gizmo.StartEvent += OnDragStart;
        _gizmo.StopEvent += OnDragEnd;
        _gizmo.Selection.Add(_adapter);
    }

    public void RefreshTarget()
    {
        InterruptDragInteraction();
        SyncAdapter();
        _gizmo?.ResetDeltas();
    }

    public override void Update(GameTime gameTime)
    {
        if (_dragActive && _mouse.MouseOwner != this)
            InterruptDragInteraction();

        if (!IsActive)
            return;

        if (!_dragActive)
            SyncAdapter();

        var isCameraMoving = _keyboard.IsKeyDown(Keys.LeftAlt);
        _gizmo!.Update(gameTime, !isCameraMoving);
    }

    public override void Draw(GameTime gameTime)
    {
        if (IsActive)
            _gizmo!.Draw();
    }

    private bool IsActive =>
        _gizmo != null &&
        IsEnabled &&
        _session.CurrentWorldPosition != null;

    private void SyncAdapter()
    {
        var position = _session.CurrentWorldPosition;
        if (position != null)
            _adapter.Position = position.Value;
        var orientation = _session.CurrentWorldOrientation;
        if (orientation != null)
            _adapter.Orientation = orientation.Value;
        else
            _adapter.Orientation = Quaternion.Identity;
    }

    private void OnDragStart()
    {
        if (!IsActive || !_session.BeginGesture(TransformMode))
            return;

        _mouse.MouseOwner = this;
        _dragActive = true;
    }

    private void OnDragEnd()
    {
        if (!_dragActive)
            return;

        var cancelled = _gizmo?.IsModalCancelled == true;
        ReleaseMouseOwnership();
        if (cancelled)
            _session.CancelGesture();
        else
            _session.EndGesture();
    }

    private void OnTranslate(
        ITransformable transformable,
        TransformationEventArgs args)
    {
        if (!IsActive || !_dragActive)
            return;

        var delta = (Vector3)args.Value!;
        if (_session.Translate(delta))
            _adapter.Position += delta;
    }

    private void OnRotate(
        ITransformable transformable,
        TransformationEventArgs args)
    {
        if (!IsActive || !_dragActive)
            return;

        var delta = (Matrix)args.Value!;
        if (_session.Rotate(delta) &&
            _session.CurrentWorldOrientation is Quaternion orientation)
        {
            _adapter.Orientation = orientation;
        }
    }

    private void InterruptDragInteraction()
    {
        if (!_dragActive)
            return;

        _dragActive = false;
        _session.CancelGesture();
        _gizmo?.AbortTransformInteraction();
        ReleaseMouseOwnership();
    }

    private void ReleaseMouseOwnership()
    {
        _dragActive = false;
        if (_mouse.MouseOwner == this)
            _mouse.MouseOwner = null!;
        _mouse.ClearStates();
    }

    public void Dispose()
    {
        InterruptDragInteraction();
        _gizmo?.Dispose();
    }

    private sealed class TargetAdapter : ITransformable
    {
        public Vector3 Position { get; set; }
        public Vector3 Scale { get; set; } = Vector3.One;
        public Quaternion Orientation { get; set; } = Quaternion.Identity;
        public Vector3 GetObjectCentre() => Position;
    }
}
