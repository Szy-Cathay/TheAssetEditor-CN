using System;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Serilog;
using Shared.Core.Events;
using Shared.Core.ErrorHandling;
using System.Runtime.ExceptionServices;

namespace Editors.KitbasherEditor.Components
{
    public sealed class KitbashModelGizmoComponent :
        BaseComponent,
        IDisposable
    {
        private readonly IMouseComponent _mouse;
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger =
            Logging.Create<KitbashModelGizmoComponent>();

        private readonly IKeyboardComponent _keyboard;
        private readonly SelectionManager _selectionManager;
        private readonly CommandExecutor _commandManager;
        private readonly ArcBallCamera _camera;
        private readonly RenderEngineComponent _resourceLibary;
        private readonly IDeviceResolver _deviceResolverComponent;
        private readonly CommandFactory _commandFactory;
        Gizmo _gizmo;
        bool _isEnabled = false;
        TransformGizmoWrapper _activeTransformation;
        bool _isCtrlPressed = false;

        // Edit mode state (Tab to toggle, 1/2/3 for sub-modes)
        private GeometrySelectionMode _lastEditSubMode = GeometrySelectionMode.Vertex;


        public KitbashModelGizmoComponent(IEventHub eventHub,
            IKeyboardComponent keyboardComponent, IMouseComponent mouseComponent, ArcBallCamera camera, CommandExecutor commandExecutor,
            RenderEngineComponent resourceLibary, IDeviceResolver deviceResolverComponent, CommandFactory commandFactory,
            SelectionManager selectionManager)
        {
            UpdateOrder = (int)ComponentUpdateOrderEnum.Gizmo;
            DrawOrder = (int)ComponentDrawOrderEnum.Gizmo;
            _eventHub = eventHub;
            _keyboard = keyboardComponent;
            _mouse = mouseComponent;
            _camera = camera;
            _commandManager = commandExecutor;
            _resourceLibary = resourceLibary;
            _deviceResolverComponent = deviceResolverComponent;
            _commandFactory = commandFactory;
            _selectionManager = selectionManager;

            _eventHub.Register<SelectionChangedEvent>(this, Handle);
            _mouse.CaptureInterrupted += OnCaptureInterrupted;
        }

        public override void Initialize()
        {
            _gizmo = new Gizmo(_camera, _mouse, _deviceResolverComponent.Device, _resourceLibary);
            _gizmo.ActivePivot = PivotType.ObjectCenter;
            _gizmo.SetKeyboard(_keyboard);  // Enable keyboard axis locking
            _gizmo.TranslateEvent += GizmoTranslateEvent;
            _gizmo.RotateEvent += GizmoRotateEvent;
            _gizmo.ScaleEvent += GizmoScaleEvent;
            _gizmo.StartEvent += GizmoTransformStart;
            _gizmo.StopEvent += GizmoTransformEnd;
            _gizmo.ReplacePreviewFromInitialRequested +=
                OnReplacePreviewFromInitial;
        }

        /// <summary>
        /// Get the Gizmo instance for KitbashSelectionInputComponent to check modal transform state.
        /// </summary>
        public Gizmo Gizmo => _gizmo;

        private void OnCaptureInterrupted()
        {
            if (_gizmo?.IsInModalTransform == true)
                _gizmo.CancelModalTransform();
        }

        private void OnSelectionChanged(ISelectionState state)
        {
            ExceptionDispatchInfo primaryError = null;
            var previousTransformation = _activeTransformation;
            if (previousTransformation?.IsTransformActive == true)
            {
                try
                {
                    previousTransformation.CancelTransform();
                }
                catch (Exception exception)
                {
                    primaryError = ExceptionDispatchInfo.Capture(exception);
                }

                try
                {
                    _gizmo.AbortTransformInteraction();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }

                try
                {
                    ReleaseMouseOwnership();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            try
            {
                previousTransformation?.Dispose();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(exception);
            }

            _activeTransformation = null;
            try
            {
                _gizmo.Selection.Clear();
                var replacement = TransformGizmoWrapper.CreateFromSelectionState(state, _commandFactory);
                if (replacement != null)
                {
                    // Pass falloff value for face/edge mode proportional editing
                    if (state is FaceSelectionState || state is EdgeSelectionState)
                        replacement.SetFalloffDistance(_selectionManager.VertexSelectionFalloff);
                    _gizmo.Selection.Add(replacement);
                }

                _activeTransformation = replacement;
                _gizmo.ResetDeltas();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(exception);
                _activeTransformation = null;
                _gizmo.Selection.Clear();
            }

            // Note: Don't auto-enable Gizmo here - user must click toolbar icon first
            primaryError?.Throw();
        }

        private void OnReplacePreviewFromInitial(
            ModalPreviewReplacement replacement)
        {
            if (_activeTransformation == null)
                return;

            ExceptionDispatchInfo primaryError;
            try
            {
                _activeTransformation.ReplaceInitialPreview(replacement);
                return;
            }
            catch (Exception exception)
            {
                primaryError = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                _activeTransformation.CancelTransform();
            }
            catch (Exception exception)
            {
                _logger.Error(
                    exception,
                    "Failed to cancel a rejected modal transform preview");
            }

            try
            {
                _gizmo.AbortTransformInteraction();
            }
            catch (Exception exception)
            {
                _logger.Error(
                    exception,
                    "Failed to abort a rejected modal transform interaction");
            }

            try
            {
                ReleaseMouseOwnership();
            }
            catch (Exception exception)
            {
                _logger.Error(
                    exception,
                    "Failed to release mouse ownership after a modal transform error");
            }

            primaryError.Throw();
        }

        private void GizmoTransformStart()
        {
            // Update falloff on active transformation when starting transform
            if (_activeTransformation != null && _selectionManager.GetState() is FaceSelectionState or EdgeSelectionState)
                _activeTransformation.SetFalloffDistance(_selectionManager.VertexSelectionFalloff);
            _activeTransformation?.BeginTransform();
            _mouse.MouseOwner = this;
        }

        private void GizmoTransformEnd()
        {
            var isCancelled = _gizmo.IsModalCancelled;
            try
            {
                if (isCancelled)
                    _activeTransformation?.CancelTransform();
                else
                    _activeTransformation?.CommitTransform(_commandManager);
            }
            finally
            {
                _gizmo.IsModalCancelled = false;
                ReleaseMouseOwnership();
            }
        }

        private void ReleaseMouseOwnership()
        {
            if (_mouse.MouseOwner != this)
                return;

            _mouse.MouseOwner = null;
            _mouse.ClearStates();
        }


        private void GizmoTranslateEvent(ITransformable transformable, TransformationEventArgs e)
        {
            _activeTransformation?.GizmoTranslateEvent((Vector3)e.Value, e.Pivot);
        }

        private void GizmoRotateEvent(ITransformable transformable, TransformationEventArgs e)
        {
            _activeTransformation?.GizmoRotateEvent((Matrix)e.Value, e.Pivot);
        }

        private void GizmoScaleEvent(ITransformable transformable, TransformationEventArgs e)
        {
            _activeTransformation?.GizmoScaleEvent((Vector3)e.Value, e.Pivot);
        }

        public override void Update(GameTime gameTime)
        {
            if (_mouse.MouseOwner is KitbashSelectionInputComponent)
                return;
            var selectionMode = _selectionManager.GetState().Mode;
            if (!IsSupportedSelectionMode(selectionMode))
                return;

            _activeTransformation?
                .RefreshDisplayForAnimationState();

            // Blender-style hotkey triggers for modal transform
            // G = Translate, R = Rotate, S = Scale
            // Active whenever there is a selection (no need to enable Gizmo first)
            // Press hotkey to enter modal transform mode, mouse moves to transform
            // Left click to confirm, Right click or Escape to cancel
            if (_gizmo.Selection.Count > 0 && !_gizmo.IsInModalTransform &&
                !_keyboard.IsKeyDownOrReleased(Keys.LeftControl) &&
                !_keyboard.IsKeyDownOrReleased(Keys.RightControl) &&
                !_keyboard.IsKeyDownOrReleased(Keys.LeftAlt) &&
                !_keyboard.IsKeyDownOrReleased(Keys.RightAlt))
            {
                if (_keyboard.IsKeyPressed(Keys.G))
                {
                    StartModalTransform(GizmoMode.Translate);
                }
                else if (_keyboard.IsKeyPressed(Keys.R))
                {
                    StartModalTransform(GizmoMode.Rotate);
                    if (_keyboard.GetKeyPressCount(Keys.R) > 1 && _keyboard.GetKeyPressCount(Keys.R) % 2 == 0)
                        _gizmo.ToggleTrackballRotation();
                }
                else if (_keyboard.IsKeyPressed(Keys.S))
                {
                    StartModalTransform(GizmoMode.NonUniformScale);
                }
            }
            else if (_gizmo.IsInModalTransform && !_gizmo.IsInNumericInput &&
                _gizmo.ActiveMode == GizmoMode.Rotate && _keyboard.IsKeyPressed(Keys.R))
            {
                if (_keyboard.GetKeyPressCount(Keys.R) <= 1 || _keyboard.GetKeyPressCount(Keys.R) % 2 == 1)
                    _gizmo.ToggleTrackballRotation();
            }

            // Tab = Toggle edit mode (Object <-> last sub-mode)
            // Only when not in modal transform
            if (!_gizmo.IsInModalTransform &&
                _keyboard.IsKeyReleased(Keys.Tab) &&
                !_keyboard.IsKeyDownOrReleased(Keys.LeftAlt) &&
                !_keyboard.IsKeyDownOrReleased(Keys.RightAlt))
            {
                HandleEditModeToggle();
                return;
            }

            // 1/2/3 = Switch sub-mode in edit mode (Blender: Vertex/Edge/Face)
            if (IsMeshEditMode(selectionMode) && !_gizmo.IsInModalTransform)
            {
                if (_keyboard.IsKeyReleased(Keys.D1))
                {
                    SwitchEditSubMode(GeometrySelectionMode.Vertex);
                    return;
                }
                if (_keyboard.IsKeyReleased(Keys.D2))
                {
                    SwitchEditSubMode(GeometrySelectionMode.Edge);
                    return;
                }
                if (_keyboard.IsKeyReleased(Keys.D3))
                {
                    SwitchEditSubMode(GeometrySelectionMode.Face);
                    return;
                }
            }

            // Handle modal transform updates
            if (_gizmo.IsInModalTransform)
            {
                // Ctrl key toggles snap during modal transform
                _isCtrlPressed =
                    _keyboard.IsKeyDown(Keys.LeftControl) ||
                    _keyboard.IsKeyDown(Keys.RightControl);
                _gizmo.SnapEnabled = _isCtrlPressed;

                var isCameraMoving = _keyboard.IsKeyDown(Keys.LeftAlt);
                _gizmo.Update(gameTime, !isCameraMoving);
                return;
            }

            if (!_isEnabled)
                return;

            _isCtrlPressed =
                _keyboard.IsKeyDown(Keys.LeftControl) ||
                _keyboard.IsKeyDown(Keys.RightControl);
            _gizmo.SnapEnabled = _isCtrlPressed;

            var isCameraMoving2 = _keyboard.IsKeyDown(Keys.LeftAlt) || _keyboard.IsKeyDown(Keys.RightAlt);
            if (!isCameraMoving2 && _mouse.MouseOwner == null && _mouse.IsMouseButtonPressed(MouseButton.Left))
            {
                var origin = _mouse.GetPressPosition(MouseButton.Left) ?? _mouse.Position();
                _gizmo.SelectAxis(origin);
                if (_gizmo.ActiveAxis != GizmoAxis.None)
                {
                    var axis = _gizmo.ActiveAxis;
                    _gizmo.StartModalTransform(_gizmo.ActiveMode, origin, confirmOnMouseRelease: true);
                    _gizmo.ActiveAxis = axis;
                    _gizmo.Update(gameTime, true);
                    return;
                }
            }
            _gizmo.Update(gameTime, !isCameraMoving2);
        }

        /// <summary>
        /// Toggle edit mode (Tab key, Blender behavior)
        /// Object mode -> edit mode (last sub-mode or vertex)
        /// Edit mode -> object mode
        /// </summary>
        private void HandleEditModeToggle()
        {
            var currentMode = _selectionManager.GetState().Mode;

            if (currentMode == GeometrySelectionMode.Object)
            {
                // Enter edit mode: switch to last sub-mode (or vertex by default)
                var selectedObj = _selectionManager.GetState().GetSingleSelectedObject();
                if (selectedObj == null)
                    return; // Need an object selected to enter edit mode

                _commandFactory.Create<ObjectSelectionModeCommand>()
                    .Configure(x => x.Configure(selectedObj, _lastEditSubMode))
                    .BuildAndExecute();
            }
            else if (currentMode == GeometrySelectionMode.Face || currentMode == GeometrySelectionMode.Edge || currentMode == GeometrySelectionMode.Vertex || currentMode == GeometrySelectionMode.Bone)
            {
                // Save current sub-mode for next time
                if (IsMeshEditMode(currentMode))
                    _lastEditSubMode = currentMode;
                // Exit to object mode
                var selectedObj = _selectionManager.GetState().GetSingleSelectedObject();
                _commandFactory.Create<ObjectSelectionModeCommand>()
                    .Configure(x => x.Configure(selectedObj, GeometrySelectionMode.Object))
                    .BuildAndExecute();
            }
        }

        /// <summary>
        /// Switch sub-mode within edit mode (1/2/3 keys, Blender behavior)
        /// </summary>
        private void SwitchEditSubMode(GeometrySelectionMode newMode)
        {
            var currentMode = _selectionManager.GetState().Mode;
            if (currentMode == newMode)
                return;

            var selectedObj = _selectionManager.GetState().GetSingleSelectedObject();
            if (selectedObj == null)
                return;

            _commandFactory.Create<ObjectSelectionModeCommand>()
                .Configure(x => x.Configure(selectedObj, newMode))
                .BuildAndExecute();
            _lastEditSubMode = newMode;
        }

        /// <summary>
        /// Start Blender-style modal transform
        /// </summary>
        private void StartModalTransform(GizmoMode mode)
        {
            // Don't set _isEnabled = true - Gizmo should not be visible during modal transform
            _mouse.MouseOwner = this;

            var key = mode switch
            {
                GizmoMode.Translate => Keys.G,
                GizmoMode.Rotate => Keys.R,
                _ => Keys.S
            };
            _gizmo.StartModalTransform(mode, _keyboard.GetKeyPressPosition(key));
        }

        public void SetGizmoMode(GizmoMode mode)
        {
            _gizmo.ActiveMode = mode;
            _isEnabled = true;
        }

        public void SetGizmoPivot(PivotType type)
        {
            _gizmo.ActivePivot = type;
        }

        public void Disable()
        {
            _isEnabled = false;
        }

        public override void Draw(GameTime gameTime)
        {
            var selectionMode = _selectionManager.GetState().Mode;
            if (!IsSupportedSelectionMode(selectionMode))
                return;

            // During modal transform, always draw (for dashed line visuals)
            // Otherwise, only draw if gizmo is enabled
            if (!_isEnabled && !_gizmo.IsInModalTransform)
                return;

            _gizmo.Draw();
        }

        public void ResetScale()
        {
            _gizmo.ScaleModifier = 1;
        }

        public void ModifyGizmoScale(float v)
        {
            _gizmo.ScaleModifier += v;
        }

        private static bool IsSupportedSelectionMode(GeometrySelectionMode mode)
        {
            return mode is
                GeometrySelectionMode.Object or
                GeometrySelectionMode.Face or
                GeometrySelectionMode.Edge or
                GeometrySelectionMode.Vertex or
                GeometrySelectionMode.Bone;
        }

        private static bool IsMeshEditMode(GeometrySelectionMode mode)
        {
            return mode is
                GeometrySelectionMode.Vertex or
                GeometrySelectionMode.Edge or
                GeometrySelectionMode.Face;
        }

        public void Dispose()
        {
            _mouse.CaptureInterrupted -= OnCaptureInterrupted;
            ExceptionDispatchInfo primaryError = null;
            var transformation = _activeTransformation;

            if (transformation?.IsTransformActive == true)
            {
                try
                {
                    transformation.CancelTransform();
                }
                catch (Exception exception)
                {
                    primaryError = ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (_gizmo != null)
            {
                try
                {
                    _gizmo.AbortTransformInteraction();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            try
            {
                ReleaseMouseOwnership();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                transformation?.Dispose();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(exception);
            }

            _activeTransformation = null;
            if (_gizmo != null)
            {
                try
                {
                    _gizmo.Selection.Clear();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }

                try
                {
                    _gizmo.Dispose();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            primaryError?.Throw();
        }

        public void Handle(SelectionChangedEvent notification) => OnSelectionChanged(notification.NewState);
    }
}
