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
            position = Vector3.Transform(Vector3.Backward, Matrix.CreateFromYawPitchRoll(yaw, pitch, 0));
            //Convert the relative position to the absolute position
            position *= _zoom;
            position += _lookAt;

            //Calculate a new viewmatrix
            viewMatrix = Matrix.CreateLookAt(position, _lookAt, Vector3.Up);
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
        /// Orthographic view size (half-height of the view)
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

        public float MinPitch = -MathHelper.PiOver2 + 0.3f;
        public float MaxPitch = MathHelper.PiOver2 - 0.3f;
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
            var deltaMouseX = -mouse.DeltaPosition().X;
            var deltaMouseY = mouse.DeltaPosition().Y;
            var deltaMouseWheel = mouse.DeletaScrollWheel();

            // Scroll wheel zoom works even without mouse ownership (Blender-style)
            if (deltaMouseWheel != 0)
            {
                if (Math.Abs(deltaMouseWheel) > 250)   // Weird bug, sometimes this value is very large, probably related to state clearing. Temp fix
                    deltaMouseWheel = 250 * Math.Sign(deltaMouseWheel);

                // In orthographic mode, adjust OrthoSize instead of Zoom
                if (_projectionType == ProjectionType.Orthographic)
                {
                    // Slower zoom in ortho mode for better control
                    _orthoSize += deltaMouseWheel * 0.001f * _orthoSize;
                    _orthoSize = Math.Max(0.1f, _orthoSize);  // Prevent zero or negative
                    projectionMatrixDirty = true;
                }
                else
                {
                    var oldZoom = Zoom / 10;
                    Zoom += deltaMouseWheel * 0.005f * oldZoom;
                }
            }

            // Check for middle mouse button (Blender-style navigation)
            var isMiddleMouseDown = mouse.IsMouseButtonDown(MouseButton.Middle);
            var isShiftDown = keyboard.IsKeyDown(Keys.LeftShift);

            // Blender-style: Middle mouse button navigation (no Alt required)
            if (isMiddleMouseDown)
            {
                // Wait until the component handling the previous click releases the mouse.
                if (!mouse.IsMouseOwner(this) && mouse.MouseOwner != null)
                    return;

                mouse.MouseOwner = this;

                if (isShiftDown)
                {
                    // Shift + Middle mouse = Pan view
                    MoveCameraRight(deltaMouseX * 0.01f * Zoom * .1f);
                    MoveCameraUp(-deltaMouseY * 0.01f * Zoom * .1f);
                }
                else
                {
                    // Middle mouse only = Rotate view
                    if (_projectionType == ProjectionType.Orthographic && AutoPerspectiveOnOrbit)
                    {
                        CurrentProjectionType = ProjectionType.Perspective;
                        AutoPerspectiveOnOrbit = false;
                    }

                    Yaw += deltaMouseX * 0.01f;
                    Pitch += deltaMouseY * 0.01f;
                }
                return; // Exit early - middle mouse handled
            }

            // Check mouse ownership for other operations
            if (!mouse.IsMouseOwner(this) && mouse.MouseOwner != null)
                return;

            if (keyboard.IsKeyReleased(Keys.F4))
            {
                Zoom = 10;
                _lookAt = Vector3.Zero;
            }

            // Original Alt+Left/Right mouse navigation (kept for compatibility)
            var ownsMouse = mouse.MouseOwner;
            if (keyboard.IsKeyDown(Keys.LeftAlt))
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

            if (keyboard.IsKeyDown(Keys.LeftAlt))
            {
                mouse.MouseOwner = this;
                if (mouse.IsMouseButtonDown(MouseButton.Left))
                {
                    Yaw += deltaMouseX * 0.01f;
                    Pitch += deltaMouseY * 0.01f;
                }
                if (mouse.IsMouseButtonDown(MouseButton.Right))
                {
                    MoveCameraRight(deltaMouseX * 0.01f * Zoom * .1f);
                    MoveCameraUp(-deltaMouseY * 0.01f * Zoom * .1f);
                }
            }
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

        /// <summary>
        /// Move camera in orthographic view (screen-space panning)
        /// </summary>
        public void MoveOrthoCamera(Vector2 delta)
        {
            float factor = _orthoSize * 0.002f;
            // Both X and Y are negated to match visual expectation (drag direction)
            MoveCameraRight(-delta.X * factor);
            MoveCameraUp(delta.Y * factor);
        }

        public Ray CreateCameraRay(Vector2 mouseLocation)
        {
            var projection = ProjectionMatrix;

            var nearPoint = _graphicsDevice.Viewport.Unproject(new Vector3(mouseLocation.X,
                   mouseLocation.Y, 0.0f),
                   projection,
                   ViewMatrix,
                   Matrix.Identity);

            var farPoint = _graphicsDevice.Viewport.Unproject(new Vector3(mouseLocation.X,
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
            //http://forums.create.msdn.com/forums/p/6690/35401.aspx , by "The Friggm"
            // Many many thanks to him...

            // Point in screen space of the center of the region selected
            var regionCenterScreen = new Vector2(source.Center.X, source.Center.Y);

            // Generate the projection matrix for the screen region
            var regionProjMatrix = ProjectionMatrix;

            // Calculate the region dimensions in the projection matrix. M11 is inverse of width, M22 is inverse of height.
            regionProjMatrix.M11 /= source.Width / (float)_graphicsDevice.Viewport.Width;
            regionProjMatrix.M22 /= source.Height / (float)_graphicsDevice.Viewport.Height;

            // Calculate the region center in the projection matrix. M31 is horizonatal center.
            regionProjMatrix.M31 = (regionCenterScreen.X - _graphicsDevice.Viewport.Width / 2f) / (source.Width / 2f);

            // M32 is vertical center. Notice that the screen has low Y on top, projection has low Y on bottom.
            regionProjMatrix.M32 = -(regionCenterScreen.Y - _graphicsDevice.Viewport.Height / 2f) / (source.Height / 2f);

            return new BoundingFrustum(ViewMatrix * regionProjMatrix);
        }

        public void Dispose()
        {
            _graphicsDevice = null;
        }
    }
}
