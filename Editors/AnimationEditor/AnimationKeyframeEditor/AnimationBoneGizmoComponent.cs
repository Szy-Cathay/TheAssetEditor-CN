using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using GameWorld.Core.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Shared.Core.Events;

namespace Editors.AnimationVisualEditors.AnimationKeyframeEditor
{
    public sealed class AnimationBoneGizmoComponent :
        BaseComponent,
        IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly IKeyboardComponent _keyboard;
        private readonly IMouseComponent _mouse;
        private readonly ArcBallCamera _camera;
        private readonly CommandExecutor _commandExecutor;
        private readonly RenderEngineComponent _renderEngine;
        private readonly IDeviceResolver _deviceResolver;
        private readonly CommandFactory _commandFactory;
        private readonly SelectionManager _selectionManager;
        private Gizmo? _gizmo;
        private TransformGizmoWrapper? _activeTransformation;
        private bool _isTransformActive;
        private bool _isEnabled;

        public AnimationBoneGizmoComponent(
            IEventHub eventHub,
            IKeyboardComponent keyboard,
            IMouseComponent mouse,
            ArcBallCamera camera,
            CommandExecutor commandExecutor,
            RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver,
            CommandFactory commandFactory,
            SelectionManager selectionManager)
        {
            _eventHub = eventHub;
            _keyboard = keyboard;
            _mouse = mouse;
            _camera = camera;
            _commandExecutor = commandExecutor;
            _renderEngine = renderEngine;
            _deviceResolver = deviceResolver;
            _commandFactory = commandFactory;
            _selectionManager = selectionManager;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Gizmo;
            DrawOrder = (int)ComponentDrawOrderEnum.Gizmo;
        }

        public override void Initialize()
        {
            if (Isinitialized)
                return;

            _gizmo = new Gizmo(
                _camera,
                _mouse,
                _deviceResolver.Device,
                _renderEngine)
            {
                ActivePivot = PivotType.ObjectCenter
            };
            _gizmo.SetKeyboard(_keyboard);
            _gizmo.TranslateEvent += OnTranslate;
            _gizmo.RotateEvent += OnRotate;
            _gizmo.ScaleEvent += OnScale;
            _gizmo.StartEvent += OnTransformStart;
            _gizmo.StopEvent += OnTransformEnd;
            _eventHub.Register<SelectionChangedEvent>(
                this,
                HandleSelectionChanged);
            OnSelectionChanged(_selectionManager.GetState());
            base.Initialize();
        }

        private void OnSelectionChanged(ISelectionState? state)
        {
            if (_gizmo == null)
                return;

            var previousTransformation = _activeTransformation;
            if (previousTransformation != null &&
                _isTransformActive)
            {
                previousTransformation.CancelTransform();
                _gizmo.AbortTransformInteraction();
                ReleaseMouseOwnership();
            }

            _isTransformActive = false;
            previousTransformation?.Dispose();
            _activeTransformation = null;
            _gizmo.Selection.Clear();

            if (state is not BoneSelectionState boneSelection ||
                boneSelection.SelectedBones.Count == 0)
            {
                _gizmo.ResetDeltas();
                return;
            }

            _activeTransformation = new TransformGizmoWrapper(
                _commandFactory,
                new List<int>(boneSelection.SelectedBones),
                boneSelection);
            _gizmo.Selection.Add(_activeTransformation);
            _gizmo.ResetDeltas();
        }

        public override void Update(GameTime gameTime)
        {
            if (_gizmo == null ||
                _selectionManager.GetState() is not BoneSelectionState)
            {
                return;
            }

            if (_gizmo.IsInModalTransform)
            {
                _gizmo.SnapEnabled = IsControlDown();
                _gizmo.Update(
                    gameTime,
                    !_keyboard.IsKeyDown(Keys.LeftAlt));
                return;
            }

            if (!_isEnabled)
                return;

            _gizmo.SnapEnabled = IsControlDown();
            _gizmo.Update(
                gameTime,
                !_keyboard.IsKeyDown(Keys.LeftAlt));
        }

        public override void Draw(GameTime gameTime)
        {
            if (_gizmo == null ||
                _selectionManager.GetState() is not BoneSelectionState ||
                (!_isEnabled && !_gizmo.IsInModalTransform))
            {
                return;
            }

            _gizmo.Draw();
        }

        private void OnTransformStart()
        {
            if (_activeTransformation == null)
                return;

            _activeTransformation.BeginTransform();
            _isTransformActive = true;
            _mouse.MouseOwner = this;
        }

        private void OnTransformEnd()
        {
            if (_gizmo == null)
                return;

            try
            {
                if (_gizmo.IsModalCancelled)
                    _activeTransformation?.CancelTransform();
                else
                    _activeTransformation?.CommitTransform(
                        _commandExecutor);
            }
            finally
            {
                _isTransformActive = false;
                _gizmo.IsModalCancelled = false;
                ReleaseMouseOwnership();
            }
        }

        private void OnTranslate(
            ITransformable transformable,
            TransformationEventArgs args)
        {
            _activeTransformation?.GizmoTranslateEvent(
                (Vector3)args.Value!,
                args.Pivot);
        }

        private void OnRotate(
            ITransformable transformable,
            TransformationEventArgs args)
        {
            _activeTransformation?.GizmoRotateEvent(
                (Matrix)args.Value!,
                args.Pivot);
        }

        private void OnScale(
            ITransformable transformable,
            TransformationEventArgs args)
        {
            _activeTransformation?.GizmoScaleEvent(
                (Vector3)args.Value!,
                args.Pivot);
        }

        public void SetGizmoMode(GizmoMode mode)
        {
            if (_gizmo == null)
                return;

            _gizmo.ActiveMode = mode;
            _isEnabled = true;
        }

        public void ResetScale()
        {
            if (_gizmo != null)
                _gizmo.ScaleModifier = 1;
        }

        public void Disable() => _isEnabled = false;

        private bool IsControlDown() =>
            _keyboard.IsKeyDown(Keys.LeftControl) ||
            _keyboard.IsKeyDown(Keys.RightControl);

        private void ReleaseMouseOwnership()
        {
            if (_mouse.MouseOwner != this)
                return;

            _mouse.MouseOwner = null;
            _mouse.ClearStates();
        }

        private void HandleSelectionChanged(
            SelectionChangedEvent notification) =>
            OnSelectionChanged(notification.NewState);

        public void Dispose()
        {
            _eventHub.UnRegister(this);
            ExceptionDispatchInfo? primaryError = null;
            var transformation = _activeTransformation;

            if (transformation != null && _isTransformActive)
            {
                try
                {
                    transformation.CancelTransform();
                }
                catch (Exception exception)
                {
                    primaryError = ExceptionDispatchInfo.Capture(
                        exception);
                }
            }

            _isTransformActive = false;
            if (_gizmo != null)
            {
                try
                {
                    _gizmo.AbortTransformInteraction();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(
                        exception);
                }
            }

            try
            {
                ReleaseMouseOwnership();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(
                    exception);
            }

            try
            {
                transformation?.Dispose();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(
                    exception);
            }

            _activeTransformation = null;
            if (_gizmo != null)
            {
                try
                {
                    _gizmo.Selection.Clear();
                    _gizmo.Dispose();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(
                        exception);
                }

                _gizmo = null;
            }

            primaryError?.Throw();
        }
    }
}
