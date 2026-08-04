using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace GameWorld.Core.Components.Navigation
{
    /// <summary>
    /// Navigation Gizmo Component - Integrates navigation indicator, view switching and camera transition
    /// </summary>
    public class NavigationGizmoComponent : BaseComponent, IDisposable
    {
        private readonly ArcBallCamera _camera;
        private readonly IKeyboardComponent _keyboard;
        private readonly IMouseComponent _mouse;
        private readonly RenderEngineComponent _renderEngine;
        private readonly IDeviceResolver _deviceResolver;
        private readonly FocusSelectableObjectService _focusService;
        private readonly GizmoComponent _gizmoComponent;

        private NavigationGizmo _navigationGizmo;
        private CameraTransition _cameraTransition;
        private bool _ownsMouseClick;
        private bool _releaseMouseOnNextUpdate;

        // Orthographic view state
        private bool _isInOrthoView = false;
        private ViewPresetType _currentOrthoView = ViewPresetType.Perspective;

        public bool IsInOrthoView => _isInOrthoView;
        public ViewPresetType CurrentView => _currentOrthoView;

        public NavigationGizmoComponent(
            ArcBallCamera camera,
            IKeyboardComponent keyboard,
            IMouseComponent mouse,
            RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver,
            FocusSelectableObjectService focusService,
            GizmoComponent gizmoComponent)
        {
            _camera = camera;
            _keyboard = keyboard;
            _mouse = mouse;
            _renderEngine = renderEngine;
            _deviceResolver = deviceResolver;
            _focusService = focusService;
            _gizmoComponent = gizmoComponent;

            UpdateOrder = (int)ComponentUpdateOrderEnum.NavigationGizmo;
            DrawOrder = (int)ComponentDrawOrderEnum.NavigationGizmo;
        }

        public override void Initialize()
        {
            _navigationGizmo = new NavigationGizmo(
                _deviceResolver.Device,
                _camera,
                _mouse,
                _renderEngine
            );

            _cameraTransition = new CameraTransition(_camera);

            // Subscribe to gizmo view preset requests
            _navigationGizmo.ViewPresetRequested += OnViewPresetRequested;
        }

        public override void Update(GameTime gameTime)
        {
            if (_releaseMouseOnNextUpdate)
            {
                if (_mouse.MouseOwner == this)
                {
                    _mouse.MouseOwner = null;
                    _mouse.ClearStates();
                }
                _releaseMouseOnNextUpdate = false;
            }

            if (_ownsMouseClick && _mouse.IsMouseButtonReleased(MouseButton.Left))
            {
                _ownsMouseClick = false;
                _releaseMouseOnNextUpdate = true;
            }

            // Update camera transition animation
            _cameraTransition.Update(gameTime);

            // If transitioning, skip other processing
            if (_cameraTransition.IsTransitioning)
                return;

            if (_isInOrthoView &&
                _currentOrthoView != ViewPresetType.Perspective &&
                _camera.CurrentProjectionType == ProjectionType.Perspective)
            {
                _isInOrthoView = false;
                _currentOrthoView = ViewPresetType.Perspective;
            }

            // Handle numpad shortcuts
            if (_gizmoComponent.Gizmo?.IsInModalTransform != true)
                HandleNumpadShortcuts();

            // Update gizmo hover state
            _navigationGizmo.Update(gameTime);

            // Handle mouse click on navigation gizmo
            if (_mouse.IsMouseButtonPressed(MouseButton.Left) &&
                (_mouse.MouseOwner == null || _mouse.MouseOwner == this))
            {
                if (_navigationGizmo.HandleClick(_mouse.Position()))
                {
                    _mouse.MouseOwner = this;
                    _ownsMouseClick = true;
                }
            }

        }

        private void HandleNumpadShortcuts()
        {
            // Numpad 1 - Front view / Ctrl+Numpad1 - Back view
            if (_keyboard.IsKeyReleased(Keys.NumPad1))
            {
                var isCtrlDown = IsCtrlDown();
                var view = isCtrlDown
                    ? ViewPresetType.Back
                    : ViewPresetType.Front;
                SwitchToView(isCtrlDown
                    ? view
                    : ViewPresets.ResolveRepeatedAxisView(view, _currentOrthoView));
            }

            // Numpad 3 - Right view / Ctrl+Numpad3 - Left view
            if (_keyboard.IsKeyReleased(Keys.NumPad3))
            {
                var isCtrlDown = IsCtrlDown();
                var view = isCtrlDown
                    ? ViewPresetType.Left
                    : ViewPresetType.Right;
                SwitchToView(isCtrlDown
                    ? view
                    : ViewPresets.ResolveRepeatedAxisView(view, _currentOrthoView));
            }

            // Numpad 7 - Top view / Ctrl+Numpad7 - Bottom view
            if (_keyboard.IsKeyReleased(Keys.NumPad7))
            {
                var isCtrlDown = IsCtrlDown();
                var view = isCtrlDown
                    ? ViewPresetType.Bottom
                    : ViewPresetType.Top;
                SwitchToView(isCtrlDown
                    ? view
                    : ViewPresets.ResolveRepeatedAxisView(view, _currentOrthoView));
            }

            // Numpad 5 - Toggle perspective/orthographic
            if (_keyboard.IsKeyReleased(Keys.NumPad5))
            {
                ToggleProjectionType();
            }

            // Numpad . (Decimal) - Focus on selection (Blender style)
            if (_keyboard.IsKeyReleased(Keys.Decimal))
            {
                _focusService?.FocusSelection();
            }
        }

        private bool IsCtrlDown()
        {
            return _keyboard.IsKeyDown(Keys.LeftControl) || _keyboard.IsKeyDown(Keys.RightControl);
        }

        private void SwitchToView(ViewPresetType view)
        {
            _currentOrthoView = view;
            _isInOrthoView = (view != ViewPresetType.Perspective);
            _camera.AutoPerspectiveOnOrbit = _isInOrthoView;
            _cameraTransition.StartTransition(view, _camera.LookAt);
        }

        private void ToggleProjectionType()
        {
            if (_isInOrthoView)
            {
                // Exit ortho: switch to perspective, keep current camera angles (Blender behavior)
                _isInOrthoView = false;
                _currentOrthoView = ViewPresetType.Perspective;
                _camera.AutoPerspectiveOnOrbit = false;
                _camera.CurrentProjectionType = ProjectionType.Perspective;
            }
            else
            {
                // Enter ortho: just toggle projection type, keep current camera angles (Blender behavior)
                // Blender's Numpad 5 only flips rv3d->persp without changing view rotation
                _isInOrthoView = true;
                _currentOrthoView = ViewPresetType.Perspective; // No specific axis view
                _camera.AutoPerspectiveOnOrbit = false;
                _camera.CurrentProjectionType = ProjectionType.Orthographic;
                _camera.OrthoSize = _camera.Zoom * 0.5f;
            }
        }

        private void OnViewPresetRequested(ViewPresetType view)
        {
            SwitchToView(view);
        }

        public override void Draw(GameTime gameTime)
        {
            // Draw navigation gizmo (always on top)
            _navigationGizmo?.Draw();
        }

        public void Dispose()
        {
            if (_mouse.MouseOwner == this)
            {
                _mouse.MouseOwner = null;
                _mouse.ClearStates();
            }
            _navigationGizmo?.Dispose();
        }
    }
}
