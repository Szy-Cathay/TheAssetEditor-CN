using GameWorld.Core.Components.Input;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace GameWorld.Core.Components.Rendering
{
    /// <summary>
    /// Camera projection type
    /// </summary>
    public enum ProjectionType
    {
        Perspective,
        Orthographic
    }

    public class ArcBallCamera : BaseComponent, IDisposable
    {

        GraphicsDevice _graphicsDevice;
        private readonly IMouseComponent _mouse;
        private readonly IKeyboardComponent _keyboard;

        public ArcBallCamera(IDeviceResolver deviceResolverComponent, IKeyboardComponent keyboardComponent, IMouseComponent mouseComponent)
        {
            Zoom = 10;
            Yaw = 0.8f;
            Pitch = 0.32f;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Camera;

            _deviceResolverComponent = deviceResolverComponent;
            _mouse = mouseComponent;
            _keyboard = keyboardComponent;
            if (_mouse != null)
                _mouse.CaptureInterrupted += OnCaptureInterrupted;
        }

        public override void Initialize()
        {
            _graphicsDevice = _deviceResolverComponent.Device;
            base.Initialize();
        }

        /// <summary>
        /// Recreates our view matrix, then signals that the view matrix
        /// is clean.
        /// </summary>
        private void ReCreateViewMatrix()
        {
            //Calculate the relative position of the camera                        
            var orientation = Matrix.CreateFromYawPitchRoll(yaw, pitch, 0);
            position = Vector3.Transform(Vector3.Backward, orientation);
            //Convert the relative position to the absolute position
            position *= _zoom;
            position += _lookAt;

            //Calculate a new viewmatrix
            viewMatrix = Matrix.CreateLookAt(position, _lookAt, Vector3.Transform(Vector3.Up, orientation));
            viewMatrixDirty = false;
        }


        #region HelperMethods

        /// <summary>
        /// Moves the camera and lookAt at to the right,
        /// as seen from the camera, while keeping the same height
        /// </summary>        
        public void MoveCameraRight(float amount)
        {
            var right = Vector3.Normalize(LookAt - Position); //calculate forward
            right = Vector3.Cross(right, Vector3.Up); //calculate the real right
            right.Y = 0;
            right.Normalize();
            LookAt += right * amount;
        }

        public void MoveCameraUp(float amount)
        {
            _lookAt.Y += amount;
            viewMatrixDirty = true;
        }

        /// <summary>
        /// Moves the camera and lookAt forward,
        /// as seen from the camera, while keeping the same height
        /// </summary>        
        public void MoveCameraForward(float amount)
        {
            var forward = Vector3.Normalize(LookAt - Position);
            forward.Y = 0;
            forward.Normalize();
            LookAt += forward * amount;
        }

        #endregion

        #region FieldsAndProperties
        //We don't need an update method because the camera only needs updating
        //when we change one of it's parameters.
        //We keep track if one of our matrices is dirty
        //and reacalculate that matrix when it is accesed.
        private bool viewMatrixDirty = true;
        private bool projectionMatrixDirty = true;

        // Orthographic projection support
        private ProjectionType _projectionType = ProjectionType.Perspective;
        private float _orthoSize = 10f;

        internal bool AutoPerspectiveOnOrbit { get; set; }

        // Track viewport size changes to update projection matrix
        private int _lastViewportWidth = 0;
        private int _lastViewportHeight = 0;

        /// <summary>
        /// Current projection type (Perspective or Orthographic)
        /// </summary>
        public ProjectionType CurrentProjectionType
        {
            get => _projectionType;
            set
            {
                projectionMatrixDirty = true;
                _projectionType = value;
            }
        }

        /// <summary>
        /// Orthographic view height in world units
        /// </summary>
        public float OrthoSize
        {
            get => _orthoSize;
            set
            {
                projectionMatrixDirty = true;
                _orthoSize = Math.Max(0.1f, value);
            }
        }

        public float MinPitch = -MathHelper.Pi;
        public float MaxPitch = MathHelper.Pi;
        private float _orbitDirection = 1;
        private (float Yaw, float Pitch, float Zoom, float OrthoSize, Vector3 LookAt, ProjectionType Projection, bool AutoPerspective)? _middleNavigationStart;
        private bool _suppressMiddleUntilRelease;
        private float pitch;
        public float Pitch
        {
            get { return pitch; }
            set
            {
                viewMatrixDirty = true;
                pitch = MathHelper.Clamp(value, MinPitch, MaxPitch);
            }
        }

        private float yaw;
        public float Yaw
        {
            get { return yaw; }
            set
            {
                viewMatrixDirty = true;
                yaw = value;
            }
        }

        public static float MinZoom = 0.01f;
        public static float MaxZoom = float.MaxValue;
        private float _zoom = 1;
        public float Zoom
        {
            get { return _zoom; }
            set
            {
                viewMatrixDirty = true;
                _zoom = MathHelper.Clamp(value, MinZoom, MaxZoom);
            }
        }


        private Vector3 position;
        public Vector3 Position
        {
            get
            {
                if (viewMatrixDirty)
                {
                    ReCreateViewMatrix();
                }
                return position;
            }
        }

        private Vector3 _lookAt;
        public Vector3 LookAt
        {
            get { return _lookAt; }
            set
            {
                viewMatrixDirty = true;
                _lookAt = value;
            }
        }
        #endregion

        #region ICamera Members        

        private Matrix viewMatrix;
        private readonly IDeviceResolver _deviceResolverComponent;

        public Matrix? ViewMatrixOverride { get; set; }
        public Matrix? ProjectionMatrixOverride { get; set; }

        public Matrix ViewMatrix
        {
            get
            {
                if (ViewMatrixOverride.HasValue)
                    return ViewMatrixOverride.Value;

                if (viewMatrixDirty)
                {
                    ReCreateViewMatrix();
                }
                return viewMatrix;
            }
        }

        private Matrix _projectionMatrix;

        public Matrix ProjectionMatrix
        {
            get
            {
                if (ProjectionMatrixOverride.HasValue)
                    return ProjectionMatrixOverride.Value;

                // Check if viewport size changed (happens when window/viewport is resized)
                if (_graphicsDevice != null)
                {
                    var currentWidth = _graphicsDevice.Viewport.Width;
                    var currentHeight = _graphicsDevice.Viewport.Height;
                    if (currentWidth != _lastViewportWidth || currentHeight != _lastViewportHeight)
                    {
                        _lastViewportWidth = currentWidth;
                        _lastViewportHeight = currentHeight;
                        projectionMatrixDirty = true;
                    }
                }

                if (projectionMatrixDirty)
                {
                    _projectionMatrix = RefreshProjection();
                    projectionMatrixDirty = false;
                }
                return _projectionMatrix;
            }
        }
        #endregion

        public override void Update(GameTime gameTime)
        {
            Update(_mouse, _keyboard);
        }

        public void Update(IMouseComponent mouse, IKeyboardComponent keyboard)
        {
            if (!mouse.IsMouseOwner(this) && mouse.MouseOwner != null)
                return;
            var deltaMouseX = -mouse.DeltaPosition().X;
            var deltaMouseY = mouse.DeltaPosition().Y;
            var deltaMouseWheel = mouse.DeletaScrollWheel();
            var isMiddleMouseDown = mouse.IsMouseButtonDown(MouseButton.Middle) || mouse.IsMouseButtonPressed(MouseButton.Middle);
            var isShiftDown = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            var isCtrlDown = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);

            if (_suppressMiddleUntilRelease)
            {
                if (isMiddleMouseDown && !mouse.IsMouseButtonPressed(MouseButton.Middle))
                    return;
                _suppressMiddleUntilRelease = false;
            }
            if (mouse.MouseOwner != this)
                _middleNavigationStart = null;
            if (isMiddleMouseDown)
                _middleNavigationStart ??= (Yaw, Pitch, Zoom, OrthoSize, LookAt, CurrentProjectionType, AutoPerspectiveOnOrbit);
            if (_middleNavigationStart is { } start &&
                (keyboard.IsKeyPressed(Keys.Escape) || mouse.IsMouseButtonPressed(MouseButton.Right)))
            {
                Yaw = start.Yaw;
                Pitch = start.Pitch;
                Zoom = start.Zoom;
                OrthoSize = start.OrthoSize;
                LookAt = start.LookAt;
                CurrentProjectionType = start.Projection;
                AutoPerspectiveOnOrbit = start.AutoPerspective;
                _middleNavigationStart = null;
                _suppressMiddleUntilRelease = true;
                mouse.MouseOwner = null;
                mouse.ClearStates();
                return;
            }
            if (!isMiddleMouseDown)
                _middleNavigationStart = null;

            if (deltaMouseWheel != 0)
            {
                var zoomFactor = MathF.Pow(1.2f, Math.Clamp(deltaMouseWheel / 120f, -20, 20));
                if (_projectionType == ProjectionType.Orthographic)
                    OrthoSize *= zoomFactor;
                else
                    Zoom *= zoomFactor;
            }

            // Blender-style: Middle mouse button navigation (no Alt required)
            if (isMiddleMouseDown)
            {
                if (mouse.IsMouseButtonPressed(MouseButton.Middle) && mouse.GetPressPosition(MouseButton.Middle) is Vector2 origin)
                {
                    var displacement = mouse.Position() - origin;
                    deltaMouseX = displacement.X;
                    deltaMouseY = -displacement.Y;
                }
                // Wait until the component handling the previous click releases the mouse.
                if (!mouse.IsMouseOwner(this) && mouse.MouseOwner != null)
                    return;

                mouse.MouseOwner = this;
                mouse.BeginContinuousDrag();
                if (mouse.IsMouseButtonPressed(MouseButton.Middle))
                    _orbitDirection = MathF.Cos(Pitch) < 0 ? -1 : 1;

                if (isShiftDown)
                {
                    // Shift + Middle mouse = Pan view
                    Pan(mouse, new Vector2(deltaMouseX, -deltaMouseY));
                }
                else if (isCtrlDown)
                {
                    var factor = MathF.Exp(Math.Clamp(deltaMouseY * 0.01f, -10, 10));
                    if (_projectionType == ProjectionType.Orthographic)
                        OrthoSize *= factor;
                    else
                        Zoom *= factor;
                }
                else
                {
                    // Middle mouse only = Rotate view
                    if (_projectionType == ProjectionType.Orthographic && AutoPerspectiveOnOrbit)
                    {
                        SetProjectionTypePreservingScale(ProjectionType.Perspective);
                        AutoPerspectiveOnOrbit = false;
                    }

                    Yaw = MathHelper.WrapAngle(Yaw + deltaMouseX * 0.01f * _orbitDirection);
                    Pitch = MathHelper.WrapAngle(Pitch + deltaMouseY * 0.01f);
                }
                return; // Exit early - middle mouse handled
            }

            // Check mouse ownership for other operations
            if (!mouse.IsMouseOwner(this) && mouse.MouseOwner != null)
                return;

            if (keyboard.IsKeyReleased(Keys.F4))
            {
                Zoom = 10;
                OrthoSize = PerspectiveViewHeight;
                _lookAt = Vector3.Zero;
            }

            // Original Alt+Left/Right mouse navigation (kept for compatibility)
            var ownsMouse = mouse.MouseOwner;
            var isAltDown = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
            if (isAltDown)
            {
                mouse.MouseOwner = this;
            }
            else
            {
                // Only release mouse ownership if middle mouse is not pressed
                if (ownsMouse == this && !isMiddleMouseDown)
                {
                    mouse.MouseOwner = null;
                    mouse.ClearStates();
                    return;
                }
            }

            if (isAltDown)
            {
                mouse.MouseOwner = this;
                if (mouse.IsMouseButtonDown(MouseButton.Left))
                {
                    mouse.BeginContinuousDrag();
                    if (mouse.IsMouseButtonPressed(MouseButton.Left))
                        _orbitDirection = MathF.Cos(Pitch) < 0 ? -1 : 1;
                    Yaw = MathHelper.WrapAngle(Yaw + deltaMouseX * 0.01f * _orbitDirection);
                    Pitch = MathHelper.WrapAngle(Pitch + deltaMouseY * 0.01f);
                }
                if (mouse.IsMouseButtonDown(MouseButton.Right))
                {
                    mouse.BeginContinuousDrag();
                    Pan(mouse, new Vector2(deltaMouseX, -deltaMouseY));
                }
                if (!mouse.IsMouseButtonDown(MouseButton.Left) && !mouse.IsMouseButtonDown(MouseButton.Right))
                    mouse.EndContinuousDrag();
            }
        }

        internal void Pan(IMouseComponent mouse, Vector2 displacement)
        {
            var size = mouse.GetScreenSize();
            if (size.X <= 0 || size.Y <= 0)
                return;
            var viewport = new Viewport(0, 0, (int)size.X, (int)size.Y);
            var center = viewport.Project(LookAt, ProjectionMatrix, ViewMatrix, Matrix.Identity);
            var start = viewport.Unproject(center, ProjectionMatrix, ViewMatrix, Matrix.Identity);
            var end = viewport.Unproject(center + new Vector3(displacement, 0), ProjectionMatrix, ViewMatrix, Matrix.Identity);
            LookAt += start - end;
        }

        public float PerspectiveViewHeight => Zoom * 2 * MathF.Tan(MathHelper.PiOver4 / 2);

        public void FrameBounds(BoundingBox bounds)
        {
            var center = (bounds.Min + bounds.Max) * 0.5f;
            LookAt = center;
            // Keep the current scale when framing a single vertex or bone.
            if (Vector3.DistanceSquared(bounds.Min, bounds.Max) < 0.00000001f)
                return;

            var size = _mouse.GetScreenSize();
            var aspect = size.X > 0 && size.Y > 0 ? size.X / size.Y : _graphicsDevice?.Viewport.AspectRatio ?? 1;
            var tanHalfFov = MathF.Tan(MathHelper.PiOver4 / 2);
            var distance = MinZoom;
            var viewHeight = 0f;
            var depth = 0f;
            foreach (var corner in bounds.GetCorners())
            {
                var point = Vector3.TransformNormal(corner - center, ViewMatrix);
                var halfHeight = Math.Max(Math.Abs(point.Y), Math.Abs(point.X) / aspect) * 1.2f;
                viewHeight = Math.Max(viewHeight, halfHeight * 2);
                distance = Math.Max(distance, point.Z + halfHeight / tanHalfFov);
                depth = Math.Max(depth, point.Z);
            }
            if (CurrentProjectionType == ProjectionType.Orthographic)
            {
                OrthoSize = viewHeight;
                Zoom = Math.Max(OrthoSize / (2 * tanHalfFov), depth + 0.02f);
            }
            else
                Zoom = Math.Max(distance, depth + 0.02f);
        }

        public void SetProjectionTypePreservingScale(ProjectionType type)
        {
            if (type == CurrentProjectionType)
                return;
            if (type == ProjectionType.Orthographic)
                OrthoSize = PerspectiveViewHeight;
            else
                Zoom = OrthoSize / (2 * MathF.Tan(MathHelper.PiOver4 / 2));
            CurrentProjectionType = type;
        }


        Matrix RefreshProjection()
        {
            if (_projectionType == ProjectionType.Perspective)
            {
                return Matrix.CreatePerspectiveFieldOfView(
                    MathHelper.ToRadians(45), // 45 degree angle
                    _graphicsDevice.Viewport.Width /
                    (float)_graphicsDevice.Viewport.Height,
                    .01f, 25000) * Matrix.CreateScale(-1, 1, 1);
            }
            else
            {
                return CreateOrthographicProjection();
            }
        }

        private Matrix CreateOrthographicProjection()
        {
            float aspectRatio = _graphicsDevice.Viewport.Width / (float)_graphicsDevice.Viewport.Height;
            return Matrix.CreateOrthographic(
                _orthoSize * aspectRatio,  // width
                _orthoSize,                 // height
                0.01f,                      // near
                25000f                      // far
            ) * Matrix.CreateScale(-1, 1, 1);
        }

        public Viewport InputViewport
        {
            get
            {
                var size = _mouse.GetScreenSize();
                return size.X > 0 && size.Y > 0 ? new Viewport(0, 0, (int)size.X, (int)size.Y) : _graphicsDevice.Viewport;
            }
        }

        public Ray CreateCameraRay(Vector2 mouseLocation)
        {
            var projection = ProjectionMatrix;

            var nearPoint = InputViewport.Unproject(new Vector3(mouseLocation.X,
                   mouseLocation.Y, 0.0f),
                   projection,
                   ViewMatrix,
                   Matrix.Identity);

            var farPoint = InputViewport.Unproject(new Vector3(mouseLocation.X,
                    mouseLocation.Y, 1.0f),
                    projection,
                    ViewMatrix,
                    Matrix.Identity);

            var direction = farPoint - nearPoint;
            direction.Normalize();

            return new Ray(nearPoint, direction);
        }

        public BoundingFrustum UnprojectRectangle(Rectangle source)
        {
            var viewport = InputViewport;
            var width = Math.Max(1, source.Width);
            var height = Math.Max(1, source.Height);
            var centerX = (source.X + source.Width * 0.5f) / viewport.Width * 2 - 1;
            var centerY = 1 - (source.Y + source.Height * 0.5f) / viewport.Height * 2;
            // Crop in clip space so perspective, orthographic and mirrored projections agree.
            var crop = Matrix.CreateScale((float)viewport.Width / width, (float)viewport.Height / height, 1);
            crop.M41 = -centerX * crop.M11;
            crop.M42 = -centerY * crop.M22;
            return new BoundingFrustum(ViewMatrix * ProjectionMatrix * crop);
        }

        public void Dispose()
        {
            if (_mouse != null)
            {
                _mouse.CaptureInterrupted -= OnCaptureInterrupted;
                if (_mouse.MouseOwner == this)
                    _mouse.MouseOwner = null;
            }
            _graphicsDevice = null;
        }

        private void OnCaptureInterrupted()
        {
            _middleNavigationStart = null;
            _suppressMiddleUntilRelease = true;
        }
    }
}
