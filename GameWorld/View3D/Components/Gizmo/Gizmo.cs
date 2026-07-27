using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

// -------------------------------------------------------------
// -- XNA 3D Gizmo (Component)
// -------------------------------------------------------------
// -- open-source gizmo component for any 3D level editor.
// -- contains any feature you may be looking for in a transformation gizmo.
// -- 
// -- for additional information and instructions visit codeplex.
// --
// -- codeplex url: http://xnagizmo.codeplex.com/
// --
// -----------------Please Do Not Remove ----------------------
// -- Work by Tom Looman, licensed under Ms-PL
// -- My Blog: http://coreenginedev.blogspot.com
// -- My Portfolio: http://tomlooman.com
// -- You may find additional XNA resources and information on these sites.
// ------------------------------------------------------------

namespace GameWorld.Core.Components.Gizmo
{
    public class Gizmo : IDisposable
    {
        /// <summary>
        /// only active if atleast one entity is selected.
        /// </summary>
        private bool _isActive = true;

        /// <summary>
        /// Enabled if gizmo should be able to select objects and axis.
        /// </summary>
        public bool Enabled { get; set; }

        private readonly GraphicsDevice _graphics;
        private readonly RenderEngineComponent _renderEngineComponent;

        private readonly BasicEffect _lineEffect;
        private readonly BasicEffect _meshEffect;


        // -- Screen Scale -- //
        private float _screenScale;
        public float ScaleModifier { get; set; } = 1;

        // -- Position - Rotation -- //
        private Vector3 _position = Vector3.Zero;
        private Matrix _rotationMatrix = Matrix.Identity;

        public Matrix AxisMatrix
        {
            get { return _rotationMatrix; }
        }

        private Vector3 _localForward = Vector3.Forward;
        private Vector3 _localUp = Vector3.Up;
        private Vector3 _localRight;

        // -- Matrices -- //
        private Matrix _objectOrientedWorld;
        private Matrix _axisAlignedWorld;
        private Matrix[] _modelLocalSpace;

        // used for all drawing, assigned by local- or world-space matrices
        private Matrix _gizmoWorld = Matrix.Identity;

        // the matrix used to apply to your whole scene, usually matrix.identity (default scale, origin on 0,0,0 etc.)
        public Matrix SceneWorld;

        // -- Lines (Vertices) -- //
        private VertexPositionColor[] _translationLineVertices;
        private const float LINE_LENGTH = 3f;
        private const float LINE_OFFSET = 1f;

        // -- Colors -- //
        private Color[] _axisColors = new Color[3] { Color.Red, Color.Green, Color.Blue };
        private Color _highlightColor = Color.Gold;

        // -- UI Text -- //
        private string[] _axisText = new string[3] { "X", "Y", "Z" };
        private Vector3 _axisTextOffset = new Vector3(0, 0.5f, 0);

        // -- Modes & Selections -- //
        public GizmoAxis ActiveAxis = GizmoAxis.None;
        public GizmoMode ActiveMode = GizmoMode.Translate;
        public TransformSpace GizmoDisplaySpace = TransformSpace.World;
        public TransformSpace GizmoValueSpace = TransformSpace.Local;
        public PivotType ActivePivot = PivotType.SelectionCenter;

        // -- Blender-style Modal Transform -- //
        // Based on Blender's TransInfo and TransData structures
        // Reference: Blender source/blender/editors/transform/transform_input.cc
        public bool IsInModalTransform = false;
        private Vector2 _modalTransformStartMousePos;    // Initial mouse position (imval in Blender)
        private Vector3 _modalStartPivot;                // Initial pivot position
        public bool IsModalCancelled = false;            // Flag to distinguish cancel from confirm

        // Blender-style virtual mouse value accumulator
        // This ensures frame-rate independent movement and smooth Shift behavior
        private struct VirtualMouseValue
        {
            public Vector2 Prev;      // Previous virtual position
            public Vector2 Accum;     // Accumulated displacement
        }
        private VirtualMouseValue _virtualMouse;

        /// <summary>
        /// Flag to indicate that modal transform just finished.
        /// SelectionComponent should check this to prevent selecting other objects.
        /// </summary>
        public bool JustFinishedModalTransform { get; private set; } = false;

        // For precise ray-plane intersection calculation (like Blender's InputVector)
        private Vector3 _lastModalIntersection = Vector3.Zero;
        private bool _suppressPointerGestureUntilRelease;

        // Dashed line parameters from mouse to pivot during modal transform
        private const int DASH_SCREEN_LENGTH = 20;  // Length of each dash in screen pixels
        private const int MAX_DASHES = 200;          // Maximum number of dashes to draw

        // -- Numeric Input (Blender-style) -- //
        // User can type numbers directly after G/R/S for precise input
        private string _numericInput = "";            // Current numeric input string
        public bool IsInNumericInput = false;         // Whether user is typing a number
        private float _numericValue = 0f;             // Parsed numeric value


        #region BoundingSpheres

        private const float RADIUS = 1f;

        private BoundingSphere XSphere
        {
            get
            {
                return new BoundingSphere(Vector3.Transform(_translationLineVertices[1].Position, _gizmoWorld), RADIUS * _screenScale * ScaleModifier);
            }
        }

        private BoundingSphere YSphere
        {
            get
            {
                return new BoundingSphere(Vector3.Transform(_translationLineVertices[7].Position, _gizmoWorld), RADIUS * _screenScale * ScaleModifier);
            }
        }

        private BoundingSphere ZSphere
        {
            get
            {
                return new BoundingSphere(Vector3.Transform(_translationLineVertices[13].Position, _gizmoWorld), RADIUS * _screenScale * ScaleModifier);
            }
        }

        #endregion



        // -- Selection -- //
        public List<ITransformable> Selection = new List<ITransformable>();


        // -- Translation Variables -- //
        private Vector3 _lastIntersectionPosition;
        private Vector3 _intersectPosition;

        public bool SnapEnabled = false;
        public float RotationSnapValue = 30;
        public float TranslationSnapValue = 1.0f;
        private float _rotationSnapDelta;


        private readonly ArcBallCamera _camera;
        private readonly IMouseComponent _mouse;
        private IKeyboardComponent _keyboard;


        public Gizmo(ArcBallCamera camera, IMouseComponent mouse, GraphicsDevice graphics, RenderEngineComponent renderEngineComponent)
        {
            SceneWorld = Matrix.Identity;
            _graphics = graphics;
            _renderEngineComponent = renderEngineComponent;

            _camera = camera;
            _mouse = mouse;

            Enabled = true;

            _lineEffect = new BasicEffect(graphics) { VertexColorEnabled = true, AmbientLightColor = Vector3.One, EmissiveColor = Vector3.One };
            _meshEffect = new BasicEffect(graphics);

            Initialize();
        }

        /// <summary>
        /// Set keyboard component for axis locking via X/Y/Z keys
        /// </summary>
        public void SetKeyboard(IKeyboardComponent keyboard)
        {
            _keyboard = keyboard;
        }

        /// <summary>
        /// Start Blender-style modal transform (mouse movement transforms without dragging)
        /// </summary>
        public void StartModalTransform(GizmoMode mode)
        {
            if (Selection.Count == 0)
                return;

            ActiveMode = mode;
            ActiveAxis = GizmoAxis.None;
            IsInModalTransform = true;
            IsModalCancelled = false;

            // Save initial mouse position (Blender: imval)
            _modalTransformStartMousePos = _mouse.Position();

            // Initialize virtual mouse accumulator (Blender-style)
            // Reference: transform_input.cc applyMouseInput()
            // IMPORTANT: Prev must be current mouse position, not Zero, to avoid first-frame jump
            _virtualMouse.Prev = _modalTransformStartMousePos;
            _virtualMouse.Accum = Vector2.Zero;

            // Save pivot position
            UpdateGizmoPosition();
            _modalStartPivot = _position;
            _lastModalIntersection = Vector3.Zero;

            // Set cursor based on mode (Blender-style)
            ModalCursorType cursorType = mode switch
            {
                GizmoMode.Translate => ModalCursorType.Move,
                GizmoMode.Rotate => ModalCursorType.Rotate,
                GizmoMode.NonUniformScale or GizmoMode.UniformScale => ModalCursorType.Scale,
                _ => ModalCursorType.Default
            };
            _mouse.SetModalCursor(cursorType);

            StartEvent?.Invoke();
        }

        /// <summary>
        /// Confirm modal transform - keep current state
        /// </summary>
        public void ConfirmModalTransform()
        {
            if (!IsInModalTransform)
                return;

            IsInModalTransform = false;
            ActiveAxis = GizmoAxis.None;
            IsModalCancelled = false;
            JustFinishedModalTransform = true;  // Prevent next selection

            // Reset cursor to default
            _mouse.ResetCursor();

            StopEvent?.Invoke();
        }

        /// <summary>
        /// Cancel modal transform - restore all objects to initial state
        /// </summary>
        public void CancelModalTransform()
        {
            if (!IsInModalTransform)
                return;

            IsInModalTransform = false;
            ActiveAxis = GizmoAxis.None;
            IsModalCancelled = true;
            JustFinishedModalTransform = true;  // Prevent next selection
            ResetDeltas();

            // Reset cursor to default
            _mouse.ResetCursor();

            StopEvent?.Invoke();
        }

        internal void AbortTransformInteraction()
        {
            var wasModalTransform = IsInModalTransform;

            IsInModalTransform = false;
            IsModalCancelled = false;
            ActiveAxis = GizmoAxis.None;
            _suppressPointerGestureUntilRelease =
                _mouse.State().LeftButton == ButtonState.Pressed;
            _virtualMouse = default;
            _lastModalIntersection = Vector3.Zero;
            _numericInput = "";
            IsInNumericInput = false;
            _numericValue = 0f;
            ResetDeltas();

            if (wasModalTransform)
            {
                JustFinishedModalTransform = true;
                _mouse.ResetCursor();
            }
        }

        /// <summary>
        /// Clear the JustFinishedModalTransform flag after SelectionComponent has checked it.
        /// </summary>
        public void ClearJustFinishedFlag()
        {
            JustFinishedModalTransform = false;
        }

        private void Initialize()
        {
            // -- Set local-space offset -- //
            _modelLocalSpace = new Matrix[3];
            _modelLocalSpace[0] = Matrix.CreateWorld(new Vector3(LINE_LENGTH, 0, 0), Vector3.Left, Vector3.Up);
            _modelLocalSpace[1] = Matrix.CreateWorld(new Vector3(0, LINE_LENGTH, 0), Vector3.Down, Vector3.Left);
            _modelLocalSpace[2] = Matrix.CreateWorld(new Vector3(0, 0, LINE_LENGTH), Vector3.Forward, Vector3.Up);

            const float halfLineOffset = LINE_OFFSET / 2;


            // fill array with vertex-data
            var vertexList = new List<VertexPositionColor>(18);

            // helper to apply colors
            var xColor = _axisColors[0];
            var yColor = _axisColors[1];
            var zColor = _axisColors[2];


            // -- X Axis -- // index 0 - 5
            vertexList.Add(new VertexPositionColor(new Vector3(halfLineOffset, 0, 0), xColor));
            vertexList.Add(new VertexPositionColor(new Vector3(LINE_LENGTH, 0, 0), xColor));

            vertexList.Add(new VertexPositionColor(new Vector3(LINE_OFFSET, 0, 0), xColor));
            vertexList.Add(new VertexPositionColor(new Vector3(LINE_OFFSET, LINE_OFFSET, 0), xColor));

            vertexList.Add(new VertexPositionColor(new Vector3(LINE_OFFSET, 0, 0), xColor));
            vertexList.Add(new VertexPositionColor(new Vector3(LINE_OFFSET, 0, LINE_OFFSET), xColor));

            // -- Y Axis -- // index 6 - 11
            vertexList.Add(new VertexPositionColor(new Vector3(0, halfLineOffset, 0), yColor));
            vertexList.Add(new VertexPositionColor(new Vector3(0, LINE_LENGTH, 0), yColor));

            vertexList.Add(new VertexPositionColor(new Vector3(0, LINE_OFFSET, 0), yColor));
            vertexList.Add(new VertexPositionColor(new Vector3(LINE_OFFSET, LINE_OFFSET, 0), yColor));

            vertexList.Add(new VertexPositionColor(new Vector3(0, LINE_OFFSET, 0), yColor));
            vertexList.Add(new VertexPositionColor(new Vector3(0, LINE_OFFSET, LINE_OFFSET), yColor));

            // -- Z Axis -- // index 12 - 17
            vertexList.Add(new VertexPositionColor(new Vector3(0, 0, halfLineOffset), zColor));
            vertexList.Add(new VertexPositionColor(new Vector3(0, 0, LINE_LENGTH), zColor));

            vertexList.Add(new VertexPositionColor(new Vector3(0, 0, LINE_OFFSET), zColor));
            vertexList.Add(new VertexPositionColor(new Vector3(LINE_OFFSET, 0, LINE_OFFSET), zColor));

            vertexList.Add(new VertexPositionColor(new Vector3(0, 0, LINE_OFFSET), zColor));
            vertexList.Add(new VertexPositionColor(new Vector3(0, LINE_OFFSET, LINE_OFFSET), zColor));

            // -- Convert to array -- //
            _translationLineVertices = vertexList.ToArray();
        }

        public void ResetDeltas()
        {
            _lastIntersectionPosition = Vector3.Zero;
            _intersectPosition = Vector3.Zero;
        }

        /// <summary>
        /// Update Blender-style modal transform (mouse movement without dragging)
        /// Uses Blender's virtual mouse accumulator pattern for frame-rate independent movement
        /// Reference: Blender transform_input.cc applyMouseInput()
        /// </summary>
        private void UpdateModalTransform(GameTime gameTime)
        {
            UpdateGizmoPosition();

            // -- Numeric Input Handling (Blender-style) -- //
            // User can type numbers directly for precise input
            HandleNumericInput();

            // Axis/Plane locking via X/Y/Z keys (Blender-style)
            // Shift+X/Y/Z = plane lock, X/Y/Z alone = axis lock
            bool isShiftHeld = _keyboard.IsKeyDown(Keys.LeftShift) || _keyboard.IsKeyDown(Keys.RightShift);

            if (isShiftHeld)
            {
                if (_keyboard.IsKeyReleased(Keys.X))
                    ActiveAxis = (ActiveAxis == GizmoAxis.YZ) ? GizmoAxis.None : GizmoAxis.YZ;
                else if (_keyboard.IsKeyReleased(Keys.Y))
                    ActiveAxis = (ActiveAxis == GizmoAxis.XZ) ? GizmoAxis.None : GizmoAxis.XZ;
                else if (_keyboard.IsKeyReleased(Keys.Z))
                    ActiveAxis = (ActiveAxis == GizmoAxis.XY) ? GizmoAxis.None : GizmoAxis.XY;
            }
            else
            {
                if (_keyboard.IsKeyReleased(Keys.X))
                    ActiveAxis = (ActiveAxis == GizmoAxis.X) ? GizmoAxis.None : GizmoAxis.X;
                else if (_keyboard.IsKeyReleased(Keys.Y))
                    ActiveAxis = (ActiveAxis == GizmoAxis.Y) ? GizmoAxis.None : GizmoAxis.Y;
                else if (_keyboard.IsKeyReleased(Keys.Z))
                    ActiveAxis = (ActiveAxis == GizmoAxis.Z) ? GizmoAxis.None : GizmoAxis.Z;
            }

            // Cancel via Right mouse button or Escape
            if (_mouse.IsMouseButtonPressed(MouseButton.Right) || _keyboard.IsKeyReleased(Keys.Escape))
            {
                CancelModalTransform();
                return;
            }

            // Confirm via Left mouse button or Enter (with or without numeric input)
            if (_mouse.IsMouseButtonPressed(MouseButton.Left) || _keyboard.IsKeyReleased(Keys.Enter))
            {
                // If there's numeric input, apply it
                if (IsInNumericInput && _numericInput.Length > 0)
                {
                    ApplyNumericInput();
                }
                ConfirmModalTransform();
                return;
            }

            // -- Blender-style Virtual Mouse Accumulator (Frame-Rate Independent) --
            // Reference: transform_input.cc:489-526
            // Key concept: Track mouse movement incrementally, not from absolute position
            // This makes infinite drag (cursor wrapping) much simpler to handle

            var currentMousePos = _mouse.Position();
            var screenSize = _mouse.GetScreenSize();
            var viewportWidth = (int)screenSize.X;
            var viewportHeight = (int)screenSize.Y;

            // Calculate frame delta (mouse movement since last frame)
            var frameDelta = currentMousePos - _virtualMouse.Prev;

            // Infinite drag - wrap cursor when mouse approaches viewport edge
            const int triggerZone = 20;
            const int safeZone = 80;

            var wrappedPos = currentMousePos;
            bool needWrap = false;

            if (currentMousePos.X < triggerZone)
            {
                wrappedPos.X = viewportWidth - safeZone;
                needWrap = true;
            }
            else if (currentMousePos.X > viewportWidth - triggerZone)
            {
                wrappedPos.X = safeZone;
                needWrap = true;
            }

            if (currentMousePos.Y < triggerZone)
            {
                wrappedPos.Y = viewportHeight - safeZone;
                needWrap = true;
            }
            else if (currentMousePos.Y > viewportHeight - triggerZone)
            {
                wrappedPos.Y = safeZone;
                needWrap = true;
            }

            if (needWrap)
            {
                // Wrap cursor to other side
                _mouse.SetCursorPosition((int)wrappedPos.X, (int)wrappedPos.Y);

                // Recalculate frame delta accounting for wrap
                // The actual movement is: user moved to currentMousePos, then we wrapped to wrappedPos
                // So the effective movement for this frame is just the movement before wrap
                // And we set Prev to wrappedPos so next frame starts fresh from there
                frameDelta = currentMousePos - _virtualMouse.Prev;  // Movement before wrap
                _virtualMouse.Prev = wrappedPos;  // Next frame starts from wrapped position
            }
            else
            {
                _virtualMouse.Prev = currentMousePos;
            }

            // -- Precision Mode (Shift key) --
            // Blender: Scale the frame delta for precise control
            const float precisionFactor = 0.1f;  // Blender default: 1/10
            bool isPrecisionNow = _keyboard.IsKeyDown(Keys.LeftShift) || _keyboard.IsKeyDown(Keys.RightShift);

            if (isPrecisionNow)
            {
                frameDelta *= precisionFactor;
            }

            // Accumulate the delta (Blender-style virtual accumulator)
            _virtualMouse.Accum += frameDelta;

            // The final displacement is the accumulated value
            var finalDisplacement = _virtualMouse.Accum;

            // Apply transform based on mode using ABSOLUTE displacement
            switch (ActiveMode)
            {
                case GizmoMode.Translate:
                    {
                        // For translation, use absolute displacement from initial position
                        Vector3 totalTranslation = CalculateAbsoluteTranslation(finalDisplacement);
                        ApplyModalTranslationFromInitial(totalTranslation);
                        break;
                    }
                case GizmoMode.Rotate:
                    {
                        // For rotation, use absolute angle from initial position
                        const float radiansPerPixel = 0.002f;
                        float totalRotation = finalDisplacement.X * radiansPerPixel;
                        ApplyModalRotationFromInitial(totalRotation);
                        break;
                    }
                case GizmoMode.NonUniformScale:
                case GizmoMode.UniformScale:
                    {
                        // For scale, use absolute scale factor from initial position
                        float totalScaleFactor;
                        if (IsInNumericInput && _numericInput.Length > 0)
                        {
                            totalScaleFactor = _numericValue;
                        }
                        else
                        {
                            totalScaleFactor = 1.0f + finalDisplacement.Y * 0.01f;
                        }
                        ApplyModalScaleFromInitial(totalScaleFactor);
                        break;
                    }
            }
        }

        /// <summary>
        /// Calculate absolute translation from accumulated mouse displacement
        /// This is frame-rate independent and matches Blender's approach
        /// </summary>
        private Vector3 CalculateAbsoluteTranslation(Vector2 totalDisplacement)
        {
            if (ActiveAxis == GizmoAxis.None)
            {
                // Free translation on view plane
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();

                float distanceToObject = (_position - _camera.Position).Length();
                float sensitivity = 0.001f * distanceToObject;

                Vector3 cameraRight = Vector3.Cross(viewDir, Vector3.Up);
                if (cameraRight.LengthSquared() < 0.001f)
                    cameraRight = Vector3.Cross(viewDir, Vector3.UnitX);
                cameraRight.Normalize();
                Vector3 cameraUp = Vector3.Cross(cameraRight, viewDir);
                cameraUp.Normalize();

                return cameraRight * -totalDisplacement.X * sensitivity + cameraUp * -totalDisplacement.Y * sensitivity;
            }
            else if (ActiveAxis == GizmoAxis.YZ || ActiveAxis == GizmoAxis.XZ || ActiveAxis == GizmoAxis.XY)
            {
                // Plane-locked translation: free movement on view plane, then zero out the excluded axis
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();

                float distanceToObject = (_position - _camera.Position).Length();
                float sensitivity = 0.001f * distanceToObject;

                Vector3 cameraRight = Vector3.Cross(viewDir, Vector3.Up);
                if (cameraRight.LengthSquared() < 0.001f)
                    cameraRight = Vector3.Cross(viewDir, Vector3.UnitX);
                cameraRight.Normalize();
                Vector3 cameraUp = Vector3.Cross(cameraRight, viewDir);
                cameraUp.Normalize();

                Vector3 result = cameraRight * -totalDisplacement.X * sensitivity + cameraUp * -totalDisplacement.Y * sensitivity;

                // Zero out the excluded axis in local space
                Vector3 excludeAxis = ActiveAxis switch
                {
                    GizmoAxis.YZ => _rotationMatrix.Right,      // exclude X
                    GizmoAxis.XZ => _rotationMatrix.Up,          // exclude Y
                    GizmoAxis.XY => _rotationMatrix.Forward,     // exclude Z
                    _ => Vector3.Zero
                };
                excludeAxis.Normalize();

                // Project out the excluded axis component
                result -= Vector3.Dot(result, excludeAxis) * excludeAxis;
                return result;
            }
            else
            {
                // Axis-constrained translation
                return CalculateAxisConstrainedAbsoluteDelta(totalDisplacement);
            }
        }

        /// <summary>
        /// Calculate axis-constrained absolute translation
        /// </summary>
        private Vector3 CalculateAxisConstrainedAbsoluteDelta(Vector2 totalDisplacement)
        {
            Vector3 axisDirection;
            switch (ActiveAxis)
            {
                case GizmoAxis.X:
                    axisDirection = _rotationMatrix.Right;
                    break;
                case GizmoAxis.Y:
                    axisDirection = _rotationMatrix.Up;
                    break;
                case GizmoAxis.Z:
                    axisDirection = _rotationMatrix.Forward;
                    break;
                default:
                    return Vector3.Zero;
            }

            // Project mouse displacement onto axis
            var axisStart = _graphics.Viewport.Project(_modalStartPivot, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            var axisEnd = _graphics.Viewport.Project(_modalStartPivot + axisDirection, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            var screenAxis = new Vector2(axisEnd.X - axisStart.X, axisEnd.Y - axisStart.Y);
            var screenAxisLength = screenAxis.Length();

            if (screenAxisLength < 0.001f)
                return Vector3.Zero;

            var movement = (totalDisplacement.X * screenAxis.X + totalDisplacement.Y * screenAxis.Y) / screenAxisLength;

            float distanceToObject = (_modalStartPivot - _camera.Position).Length();
            float worldScale = distanceToObject * 0.001f;

            return axisDirection * movement * worldScale;
        }

        /// <summary>
        /// Apply translation from initial state using absolute displacement
        /// Called each frame with total translation from initial position
        /// </summary>
        private void ApplyModalTranslationFromInitial(Vector3 totalTranslation)
        {
            // Apply translation snap (increment snap)
            if (SnapEnabled && TranslationSnapValue > 0)
            {
                totalTranslation = new Vector3(
                    (float)Math.Round(totalTranslation.X / TranslationSnapValue) * TranslationSnapValue,
                    (float)Math.Round(totalTranslation.Y / TranslationSnapValue) * TranslationSnapValue,
                    (float)Math.Round(totalTranslation.Z / TranslationSnapValue) * TranslationSnapValue
                );
            }

            if (totalTranslation == Vector3.Zero)
            {
                ReplacePreviewFromInitialRequested?.Invoke(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            ReplacePreviewFromInitialRequested?.Invoke(
                ModalPreviewReplacement.Translate(
                    totalTranslation,
                    ActivePivot));
        }

        /// <summary>
        /// Handle numeric input during modal transform (Blender-style)
        /// User can type numbers directly for precise transformation
        /// </summary>
        private void HandleNumericInput()
        {
            // Number keys (0-9) - use IsKeyReleased to detect key press
            for (int i = 0; i <= 9; i++)
            {
                var key = (Keys)((int)Keys.D0 + i);
                if (_keyboard.IsKeyReleased(key))
                {
                    if (!IsInNumericInput)
                    {
                        IsInNumericInput = true;
                        _numericInput = "";
                        _numericValue = 0f;
                    }
                    _numericInput += i.ToString();
                    ParseNumericInput();
                    return;
                }
            }

            // Numpad number keys
            for (int i = 0; i <= 9; i++)
            {
                var key = (Keys)((int)Keys.NumPad0 + i);
                if (_keyboard.IsKeyReleased(key))
                {
                    if (!IsInNumericInput)
                    {
                        IsInNumericInput = true;
                        _numericInput = "";
                        _numericValue = 0f;
                    }
                    _numericInput += i.ToString();
                    ParseNumericInput();
                    return;
                }
            }

            // Minus sign (for negative numbers)
            if (_keyboard.IsKeyReleased(Keys.OemMinus) || _keyboard.IsKeyReleased(Keys.Subtract))
            {
                if (!IsInNumericInput)
                {
                    IsInNumericInput = true;
                    _numericInput = "-";
                    _numericValue = 0f;
                }
                else if (!_numericInput.StartsWith("-"))
                {
                    _numericInput = "-" + _numericInput;
                    ParseNumericInput();
                }
                return;
            }

            // Decimal point
            if (_keyboard.IsKeyReleased(Keys.OemPeriod) || _keyboard.IsKeyReleased(Keys.Decimal) || _keyboard.IsKeyReleased(Keys.OemComma))
            {
                if (!IsInNumericInput)
                {
                    IsInNumericInput = true;
                    _numericInput = "0.";
                    _numericValue = 0f;
                }
                else if (!_numericInput.Contains("."))
                {
                    _numericInput += ".";
                    ParseNumericInput();
                }
                return;
            }

            // Backspace - delete last character
            if (_keyboard.IsKeyReleased(Keys.Back))
            {
                if (IsInNumericInput && _numericInput.Length > 0)
                {
                    _numericInput = _numericInput.Substring(0, _numericInput.Length - 1);
                    if (_numericInput.Length == 0 || _numericInput == "-")
                    {
                        _numericValue = 0f;
                    }
                    else
                    {
                        ParseNumericInput();
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Parse the numeric input string to a float value
        /// </summary>
        private void ParseNumericInput()
        {
            if (float.TryParse(_numericInput, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                _numericValue = result;
            }
        }

        /// <summary>
        /// Apply the numeric input value as transformation
        /// Called when user presses Enter or Left mouse button
        /// </summary>
        private void ApplyNumericInput()
        {
            if (!IsInNumericInput || _numericInput.Length == 0)
                return;

            switch (ActiveMode)
            {
                case GizmoMode.Translate:
                    {
                        // Numeric value is the distance to move
                        Vector3 direction;
                        if (ActiveAxis == GizmoAxis.None)
                        {
                            // Move along view direction's X axis (screen right)
                            Vector3 viewDir = _camera.LookAt - _camera.Position;
                            viewDir.Normalize();
                            direction = Vector3.Cross(viewDir, Vector3.Up);
                            if (direction.LengthSquared() < 0.001f)
                                direction = Vector3.Cross(viewDir, Vector3.UnitX);
                            direction.Normalize();
                        }
                        else
                        {
                            switch (ActiveAxis)
                            {
                                case GizmoAxis.X:
                                    direction = _rotationMatrix.Right;
                                    break;
                                case GizmoAxis.Y:
                                    direction = _rotationMatrix.Up;
                                    break;
                                case GizmoAxis.Z:
                                    direction = _rotationMatrix.Forward;
                                    break;
                                default:
                                    direction = Vector3.UnitX;
                                    break;
                            }
                        }
                        Vector3 translation = direction * _numericValue;
                        ApplyModalTranslationFromInitial(translation);
                        break;
                    }
                case GizmoMode.Rotate:
                    {
                        // Numeric value is the rotation angle in degrees
                        float angleRadians = MathHelper.ToRadians(_numericValue);
                        ApplyModalRotationFromInitial(angleRadians);
                        break;
                    }
                case GizmoMode.NonUniformScale:
                case GizmoMode.UniformScale:
                    {
                        // Numeric value is the scale factor
                        ApplyModalScaleFromInitial(_numericValue);
                        break;
                    }
            }

            // Reset numeric input state
            IsInNumericInput = false;
            _numericInput = "";
            _numericValue = 0f;
        }

        /// <summary>
        /// Update gizmo world matrix for rendering (scale and position)
        /// This is needed because modal transform early-exits from Update()
        /// </summary>
        private void UpdateGizmoWorldMatrix()
        {
            // -- Scale Gizmo to fit on-screen -- //
            var vLength = _camera.Position - _position;
            const float scaleFactor = 25;

            _screenScale = vLength.Length() / scaleFactor;
            var screenScaleMatrix = Matrix.CreateScale(new Vector3(_screenScale * ScaleModifier));

            _localForward = Vector3.Transform(Vector3.Forward, Matrix.CreateFromQuaternion(Selection[0].Orientation));
            _localUp = Vector3.Transform(Vector3.Up, Matrix.CreateFromQuaternion(Selection[0].Orientation));

            // -- Vector Rotation (Local/World) -- //
            _localForward.Normalize();
            _localRight = Vector3.Cross(_localForward, _localUp);
            _localUp = Vector3.Cross(_localRight, _localForward);
            _localRight.Normalize();
            _localUp.Normalize();

            // -- Create Both World Matrices -- //
            _objectOrientedWorld = screenScaleMatrix * Matrix.CreateWorld(_position, _localForward, _localUp);
            _axisAlignedWorld = screenScaleMatrix * Matrix.CreateWorld(_position, SceneWorld.Forward, SceneWorld.Up);

            // Assign World
            if (GizmoDisplaySpace == TransformSpace.World || ActiveMode == GizmoMode.UniformScale)
            {
                _gizmoWorld = _axisAlignedWorld;

                _rotationMatrix.Forward = SceneWorld.Forward;
                _rotationMatrix.Up = SceneWorld.Up;
                _rotationMatrix.Right = SceneWorld.Right;
            }
            else
            {
                _gizmoWorld = _objectOrientedWorld;

                _rotationMatrix.Forward = _localForward;
                _rotationMatrix.Up = _localUp;
                _rotationMatrix.Right = _localRight;
            }
        }

        /// <summary>
        /// Calculate world-space transform delta from mouse movement
        /// Like Blender's convertViewVec / ED_view3d_win_to_delta
        /// Uses ray-plane intersection for accurate world-space movement
        /// </summary>
        private Vector3 CalculateModalTransformDelta(Vector2 mouseDelta)
        {
            if (ActiveAxis == GizmoAxis.None)
            {
                // Free translation on view plane using ray-plane intersection
                // This ensures movement speed matches mouse movement visually

                // Create a plane perpendicular to camera view direction, passing through pivot
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();
                Plane viewPlane = new Plane(viewDir, -Vector3.Dot(viewDir, _position));

                // Get rays for current and last mouse positions
                var currentRay = _camera.CreateCameraRay(_mouse.Position());
                var lastRay = _camera.CreateCameraRay(_mouse.Position() - mouseDelta);

                // Find intersections with view plane
                var currentIntersect = currentRay.Intersects(viewPlane);
                var lastIntersect = lastRay.Intersects(viewPlane);

                if (currentIntersect.HasValue && lastIntersect.HasValue)
                {
                    var currentPoint = currentRay.Position + currentRay.Direction * currentIntersect.Value;
                    var lastPoint = lastRay.Position + lastRay.Direction * lastIntersect.Value;
                    return currentPoint - lastPoint;
                }

                // Fallback: use simple screen-space calculation
                float distanceToObject = (_position - _camera.Position).Length();
                float sensitivity = 0.001f * distanceToObject;

                Vector3 cameraRight = Vector3.Cross(viewDir, Vector3.Up);
                if (cameraRight.LengthSquared() < 0.001f)
                    cameraRight = Vector3.Cross(viewDir, Vector3.UnitX);
                cameraRight.Normalize();
                Vector3 cameraUp = Vector3.Cross(cameraRight, viewDir);
                cameraUp.Normalize();

                return cameraRight * mouseDelta.X * sensitivity + cameraUp * -mouseDelta.Y * sensitivity;
            }
            else if (ActiveAxis == GizmoAxis.YZ || ActiveAxis == GizmoAxis.XZ || ActiveAxis == GizmoAxis.XY)
            {
                // Plane-locked: free movement then project out excluded axis
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();
                Plane viewPlane = new Plane(viewDir, -Vector3.Dot(viewDir, _position));

                var currentRay = _camera.CreateCameraRay(_mouse.Position());
                var lastRay = _camera.CreateCameraRay(_mouse.Position() - mouseDelta);

                var currentIntersect = currentRay.Intersects(viewPlane);
                var lastIntersect = lastRay.Intersects(viewPlane);

                Vector3 result;
                if (currentIntersect.HasValue && lastIntersect.HasValue)
                {
                    var currentPoint = currentRay.Position + currentRay.Direction * currentIntersect.Value;
                    var lastPoint = lastRay.Position + lastRay.Direction * lastIntersect.Value;
                    result = currentPoint - lastPoint;
                }
                else
                {
                    float distanceToObject = (_position - _camera.Position).Length();
                    float sensitivity = 0.001f * distanceToObject;
                    Vector3 cameraRight = Vector3.Cross(viewDir, Vector3.Up);
                    if (cameraRight.LengthSquared() < 0.001f)
                        cameraRight = Vector3.Cross(viewDir, Vector3.UnitX);
                    cameraRight.Normalize();
                    Vector3 cameraUp = Vector3.Cross(cameraRight, viewDir);
                    cameraUp.Normalize();
                    result = cameraRight * mouseDelta.X * sensitivity + cameraUp * -mouseDelta.Y * sensitivity;
                }

                Vector3 excludeAxis = ActiveAxis switch
                {
                    GizmoAxis.YZ => _rotationMatrix.Right,
                    GizmoAxis.XZ => _rotationMatrix.Up,
                    GizmoAxis.XY => _rotationMatrix.Forward,
                    _ => Vector3.Zero
                };
                excludeAxis.Normalize();
                result -= Vector3.Dot(result, excludeAxis) * excludeAxis;
                return result;
            }
            else
            {
                // Axis-constrained translation using ray-plane intersection
                return CalculateAxisConstrainedDelta(mouseDelta);
            }
        }

        /// <summary>
        /// Calculate frame-by-frame translation for incremental movement
        /// This provides smooth, responsive feel like Blender
        /// </summary>
        private Vector3 CalculateFrameTranslation(Vector2 frameDelta)
        {
            if (ActiveAxis == GizmoAxis.None)
            {
                // Free translation - use simple screen-space to world-space conversion
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();

                float distanceToObject = (_position - _camera.Position).Length();
                float sensitivity = 0.001f * distanceToObject;

                Vector3 cameraRight = Vector3.Cross(viewDir, Vector3.Up);
                if (cameraRight.LengthSquared() < 0.001f)
                    cameraRight = Vector3.Cross(viewDir, Vector3.UnitX);
                cameraRight.Normalize();
                Vector3 cameraUp = Vector3.Cross(cameraRight, viewDir);
                cameraUp.Normalize();

                return cameraRight * -frameDelta.X * sensitivity + cameraUp * -frameDelta.Y * sensitivity;
            }
            else if (ActiveAxis == GizmoAxis.YZ || ActiveAxis == GizmoAxis.XZ || ActiveAxis == GizmoAxis.XY)
            {
                // Plane-locked: free movement then project out excluded axis
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();

                float distanceToObject = (_position - _camera.Position).Length();
                float sensitivity = 0.001f * distanceToObject;

                Vector3 cameraRight = Vector3.Cross(viewDir, Vector3.Up);
                if (cameraRight.LengthSquared() < 0.001f)
                    cameraRight = Vector3.Cross(viewDir, Vector3.UnitX);
                cameraRight.Normalize();
                Vector3 cameraUp = Vector3.Cross(cameraRight, viewDir);
                cameraUp.Normalize();

                Vector3 result = cameraRight * -frameDelta.X * sensitivity + cameraUp * -frameDelta.Y * sensitivity;

                Vector3 excludeAxis = ActiveAxis switch
                {
                    GizmoAxis.YZ => _rotationMatrix.Right,
                    GizmoAxis.XZ => _rotationMatrix.Up,
                    GizmoAxis.XY => _rotationMatrix.Forward,
                    _ => Vector3.Zero
                };
                excludeAxis.Normalize();
                result -= Vector3.Dot(result, excludeAxis) * excludeAxis;
                return result;
            }
            else
            {
                // Axis-constrained translation
                return CalculateAxisConstrainedFrameDelta(frameDelta);
            }
        }

        /// <summary>
        /// Calculate axis-constrained frame delta for incremental movement
        /// </summary>
        private Vector3 CalculateAxisConstrainedFrameDelta(Vector2 frameDelta)
        {
            // Get axis direction
            Vector3 axisDirection;
            switch (ActiveAxis)
            {
                case GizmoAxis.X:
                    axisDirection = _rotationMatrix.Right;
                    break;
                case GizmoAxis.Y:
                    axisDirection = _rotationMatrix.Up;
                    break;
                case GizmoAxis.Z:
                    axisDirection = _rotationMatrix.Forward;
                    break;
                default:
                    return Vector3.Zero;
            }

            // Project axis to screen to get movement scale
            var axisStart = _graphics.Viewport.Project(_position, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            var axisEnd = _graphics.Viewport.Project(_position + axisDirection, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            var screenAxis = new Vector2(axisEnd.X - axisStart.X, axisEnd.Y - axisStart.Y);
            var screenAxisLength = screenAxis.Length();

            if (screenAxisLength < 0.001f)
                return Vector3.Zero;

            // Calculate movement along axis based on mouse delta projected onto axis direction
            // Use dot product to get movement along the axis
            var movement = (frameDelta.X * screenAxis.X + frameDelta.Y * screenAxis.Y) / screenAxisLength;

            // Scale: 1 pixel = small world unit, scale by distance for consistent feel
            float distanceToObject = (_position - _camera.Position).Length();
            float worldScale = distanceToObject * 0.001f;

            return axisDirection * movement * worldScale;
        }

        /// <summary>
        /// Calculate delta for axis-constrained movement using ray-plane intersection
        /// Same method as HandleTranslateAndScale in normal gizmo operation
        /// </summary>
        private Vector3 CalculateAxisConstrainedDelta(Vector2 mouseDelta)
        {
            Plane plane;
            switch (ActiveAxis)
            {
                case GizmoAxis.X:
                    plane = new Plane(Vector3.Forward, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).Z);
                    break;
                case GizmoAxis.Y:
                case GizmoAxis.Z:
                    plane = new Plane(Vector3.Left, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).X);
                    break;
                default:
                    return Vector3.Zero;
            }

            var ray = _camera.CreateCameraRay(_mouse.Position());
            var transform = Matrix.Invert(_rotationMatrix);
            ray.Position = Vector3.Transform(ray.Position, transform);
            ray.Direction = Vector3.TransformNormal(ray.Direction, transform);

            Vector3 deltaTransform = Vector3.Zero;
            var intersection = ray.Intersects(plane);
            if (intersection.HasValue)
            {
                var intersectPosition = ray.Position + ray.Direction * intersection.Value;
                var mouseDragDelta = Vector3.Zero;
                if (_lastModalIntersection != Vector3.Zero)
                    mouseDragDelta = intersectPosition - _lastModalIntersection;

                // Clamp large deltas
                var length = mouseDragDelta.Length();
                if (length > 0.5f)
                {
                    var direction = Vector3.Normalize(mouseDragDelta);
                    mouseDragDelta = direction * 0.5f;
                }

                switch (ActiveAxis)
                {
                    case GizmoAxis.X:
                        deltaTransform = new Vector3(mouseDragDelta.X, 0, 0);
                        break;
                    case GizmoAxis.Y:
                        deltaTransform = new Vector3(0, mouseDragDelta.Y, 0);
                        break;
                    case GizmoAxis.Z:
                        deltaTransform = new Vector3(0, 0, mouseDragDelta.Z);
                        break;
                }

                _lastModalIntersection = intersectPosition;
            }

            // Convert from local to world space
            return Vector3.Transform(deltaTransform, _rotationMatrix);
        }

        /// <summary>
        /// Apply rotation from initial state (called each frame with total rotation angle)
        /// Uses the event to transform from initial position around pivot
        /// </summary>
        private void ApplyModalRotationFromInitial(float totalAngle)
        {
            if (totalAngle == 0)
            {
                ReplacePreviewFromInitialRequested?.Invoke(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            // Calculate rotation matrix based on axis
            Vector3 axis;
            if (ActiveAxis == GizmoAxis.None)
            {
                // Blender-style: Free rotation around view direction (screen normal)
                // This makes the object rotate within the screen plane
                Vector3 viewDir = _camera.LookAt - _camera.Position;
                viewDir.Normalize();
                axis = viewDir;
            }
            else
            {
                switch (ActiveAxis)
                {
                    case GizmoAxis.X:
                        axis = _rotationMatrix.Right;
                        break;
                    case GizmoAxis.Y:
                        axis = _rotationMatrix.Up;
                        break;
                    case GizmoAxis.Z:
                        axis = _rotationMatrix.Forward;
                        break;
                    default:
                        ReplacePreviewFromInitialRequested?.Invoke(
                            ModalPreviewReplacement.RestoreOnly(ActivePivot));
                        return;
                }
            }

            Matrix rotMatrix = Matrix.CreateFromAxisAngle(axis, totalAngle);

            ReplacePreviewFromInitialRequested?.Invoke(
                ModalPreviewReplacement.Rotate(rotMatrix, ActivePivot));
        }

        /// <summary>
        /// Apply scale from initial state (called each frame with total scale factor)
        /// </summary>
        private void ApplyModalScaleFromInitial(float scaleFactor)
        {
            if (scaleFactor == 0)
            {
                ReplacePreviewFromInitialRequested?.Invoke(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            // Ensure scale doesn't go negative or zero
            scaleFactor = Math.Max(0.001f, scaleFactor);

            Vector3 scale;
            if (ActiveAxis == GizmoAxis.None)
            {
                // Uniform scale
                scale = new Vector3(scaleFactor - 1.0f, scaleFactor - 1.0f, scaleFactor - 1.0f);
            }
            else
            {
                // Non-uniform scale on specific axis
                scale = Vector3.Zero;
                switch (ActiveAxis)
                {
                    case GizmoAxis.X:
                        scale = new Vector3(scaleFactor - 1.0f, 0, 0);
                        break;
                    case GizmoAxis.Y:
                        scale = new Vector3(0, scaleFactor - 1.0f, 0);
                        break;
                    case GizmoAxis.Z:
                        scale = new Vector3(0, 0, scaleFactor - 1.0f);
                        break;
                }
            }

            if (scale == Vector3.Zero)
            {
                ReplacePreviewFromInitialRequested?.Invoke(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            ReplacePreviewFromInitialRequested?.Invoke(
                ModalPreviewReplacement.Scale(scale, ActivePivot));
        }

        private void UpdateGizmoVisuals()
        {
            // Update visual elements for modal transform
            // Highlight active axis
            ApplyColor(GizmoAxis.X, ActiveAxis == GizmoAxis.X ? _highlightColor : _axisColors[0]);
            ApplyColor(GizmoAxis.Y, ActiveAxis == GizmoAxis.Y ? _highlightColor : _axisColors[1]);
            ApplyColor(GizmoAxis.Z, ActiveAxis == GizmoAxis.Z ? _highlightColor : _axisColors[2]);
        }

        public void Update(GameTime gameTime, bool enableMove)
        {
            // Handle Blender-style modal transform (no gizmo display, just transform)
            if (IsInModalTransform && _keyboard != null)
            {
                UpdateModalTransform(gameTime);
                return;
            }

            var suppressReleaseThisUpdate = false;
            if (_suppressPointerGestureUntilRelease)
            {
                ActiveAxis = GizmoAxis.None;
                ResetDeltas();
                if (_mouse.State().LeftButton == ButtonState.Pressed)
                {
                    UpdateGizmoPosition();
                    return;
                }

                _suppressPointerGestureUntilRelease = false;
                suppressReleaseThisUpdate = true;
            }

            // Blender-style axis/plane locking via X/Y/Z keys during transform
            if (_keyboard != null && _mouse.IsMouseButtonDown(MouseButton.Left))
            {
                bool isShift = _keyboard.IsKeyDown(Keys.LeftShift) || _keyboard.IsKeyDown(Keys.RightShift);
                if (isShift)
                {
                    if (_keyboard.IsKeyReleased(Keys.X))
                        ActiveAxis = GizmoAxis.YZ;
                    else if (_keyboard.IsKeyReleased(Keys.Y))
                        ActiveAxis = GizmoAxis.XZ;
                    else if (_keyboard.IsKeyReleased(Keys.Z))
                        ActiveAxis = GizmoAxis.XY;
                }
                else
                {
                    if (_keyboard.IsKeyReleased(Keys.X))
                        ActiveAxis = GizmoAxis.X;
                    else if (_keyboard.IsKeyReleased(Keys.Y))
                        ActiveAxis = GizmoAxis.Y;
                    else if (_keyboard.IsKeyReleased(Keys.Z))
                        ActiveAxis = GizmoAxis.Z;
                }
            }

            if (_isActive && enableMove)
            {
                var translateScaleLocal = Vector3.Zero;
                var translateScaleWorld = Vector3.Zero;

                var rotationLocal = Matrix.Identity;
                var rotationWorld = Matrix.Identity;

                if (_mouse.IsMouseButtonDown(MouseButton.Left) && ActiveAxis != GizmoAxis.None)
                {
                    if (_mouse.LastState().LeftButton == ButtonState.Released)
                        StartEvent?.Invoke();

                    switch (ActiveMode)
                    {
                        case GizmoMode.UniformScale:
                        case GizmoMode.NonUniformScale:
                        case GizmoMode.Translate:
                            HandleTranslateAndScale(_mouse.Position(), out translateScaleLocal, out translateScaleWorld);
                            break;
                        case GizmoMode.Rotate:
                            HandleRotation(gameTime, out rotationLocal, out rotationWorld);
                            break;
                    }
                }
                else
                {
                    if (!suppressReleaseThisUpdate &&
                        _mouse.LastState().LeftButton == ButtonState.Pressed &&
                        _mouse.State().LeftButton == ButtonState.Released)
                        StopEvent?.Invoke();

                    ResetDeltas();
                    if (_mouse.State().LeftButton == ButtonState.Released && _mouse.State().RightButton == ButtonState.Released)
                        SelectAxis(_mouse.Position());
                }

                UpdateGizmoPosition();

                // -- Trigger Translation, Rotation & Scale events -- //
                if (_mouse.IsMouseButtonDown(MouseButton.Left))
                {
                    if (translateScaleWorld != Vector3.Zero)
                    {
                        if (ActiveMode == GizmoMode.Translate)
                        {
                            foreach (var entity in Selection)
                                OnTranslateEvent(entity, translateScaleWorld);
                        }
                        else
                        {
                            foreach (var entity in Selection)
                                OnScaleEvent(entity, translateScaleWorld);
                        }
                    }
                    if (rotationWorld != Matrix.Identity)
                    {
                        foreach (var entity in Selection)
                            OnRotateEvent(entity, rotationWorld);
                    }
                }
            }

            if (Selection.Count == 0)
            {
                _isActive = false;
                ActiveAxis = GizmoAxis.None;
                return;
            }

            // helps solve visual lag (1-frame-lag) after selecting a new entity
            if (!_isActive)
                UpdateGizmoPosition();

            _isActive = true;

            // -- Scale Gizmo to fit on-screen -- //
            var vLength = _camera.Position - _position;
            const float scaleFactor = 25;

            _screenScale = vLength.Length() / scaleFactor;
            var screenScaleMatrix = Matrix.CreateScale(new Vector3(_screenScale * ScaleModifier));

            _localForward = Vector3.Transform(Vector3.Forward, Matrix.CreateFromQuaternion(Selection[0].Orientation)); //Selection[0].Forward;
            _localUp = Vector3.Transform(Vector3.Up, Matrix.CreateFromQuaternion(Selection[0].Orientation));  //Selection[0].Up;

            // -- Vector Rotation (Local/World) -- //
            _localForward.Normalize();
            _localRight = Vector3.Cross(_localForward, _localUp);
            _localUp = Vector3.Cross(_localRight, _localForward);
            _localRight.Normalize();
            _localUp.Normalize();

            // -- Create Both World Matrices -- //
            _objectOrientedWorld = screenScaleMatrix * Matrix.CreateWorld(_position, _localForward, _localUp);
            _axisAlignedWorld = screenScaleMatrix * Matrix.CreateWorld(_position, SceneWorld.Forward, SceneWorld.Up);

            // Assign World
            if (GizmoDisplaySpace == TransformSpace.World ||
                //ActiveMode == GizmoMode.Rotate ||
                //ActiveMode == GizmoMode.NonUniformScale ||
                ActiveMode == GizmoMode.UniformScale)
            {
                _gizmoWorld = _axisAlignedWorld;

                // align lines, boxes etc. with the grid-lines
                _rotationMatrix.Forward = SceneWorld.Forward;
                _rotationMatrix.Up = SceneWorld.Up;
                _rotationMatrix.Right = SceneWorld.Right;
            }
            else
            {
                _gizmoWorld = _objectOrientedWorld;

                // align lines, boxes etc. with the selected object
                _rotationMatrix.Forward = _localForward;
                _rotationMatrix.Up = _localUp;
                _rotationMatrix.Right = _localRight;
            }

            // -- Reset Colors to default -- //
            ApplyColor(GizmoAxis.X, _axisColors[0]);
            ApplyColor(GizmoAxis.Y, _axisColors[1]);
            ApplyColor(GizmoAxis.Z, _axisColors[2]);

            // -- Apply Highlight -- //
            ApplyColor(ActiveAxis, _highlightColor);
        }

        private void HandleTranslateAndScale(Vector2 mousePosition, out Vector3 out_transformLocal, out Vector3 out_transfromWorld)
        {
            Plane plane;
            switch (ActiveAxis)
            {
                case GizmoAxis.X:
                    plane = new Plane(Vector3.Forward, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).Z);
                    break;
                case GizmoAxis.Z:
                case GizmoAxis.Y:
                    plane = new Plane(Vector3.Left, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).X);
                    break;
                default:
                    throw new Exception("This should never happen - No axis inside HandleTranslateAndScale");
            }


            var ray = _camera.CreateCameraRay(mousePosition);
            var transform = Matrix.Invert(_rotationMatrix);
            ray.Position = Vector3.Transform(ray.Position, transform);
            ray.Direction = Vector3.TransformNormal(ray.Direction, transform);

            var deltaTransform = Vector3.Zero;
            var intersection = ray.Intersects(plane);
            if (intersection.HasValue)
            {
                _intersectPosition = ray.Position + ray.Direction * intersection.Value;
                var mouseDragDelta = Vector3.Zero;
                if (_lastIntersectionPosition != Vector3.Zero)
                    mouseDragDelta = _intersectPosition - _lastIntersectionPosition;

                var length = mouseDragDelta.Length();
                if (length > 0.5f)
                {
                    var direction = Vector3.Normalize(mouseDragDelta);
                    mouseDragDelta = direction * 0.5f;
                }
                switch (ActiveAxis)
                {
                    case GizmoAxis.X:
                        deltaTransform = new Vector3(mouseDragDelta.X, 0, 0);
                        break;
                    case GizmoAxis.Y:
                        deltaTransform = new Vector3(0, mouseDragDelta.Y, 0);
                        break;
                    case GizmoAxis.Z:
                        deltaTransform = new Vector3(0, 0, mouseDragDelta.Z);
                        break;
                }

                _lastIntersectionPosition = _intersectPosition;
            }

            if (ActiveMode == GizmoMode.Translate)
            {
                var localResult = Vector3.Transform(deltaTransform, SceneWorld);
                var worldResult = Vector3.Transform(deltaTransform, _rotationMatrix);

                // Apply translation snap for non-modal gizmo drag
                if (SnapEnabled && TranslationSnapValue > 0)
                {
                    localResult = new Vector3(
                        (float)Math.Round(localResult.X / TranslationSnapValue) * TranslationSnapValue,
                        (float)Math.Round(localResult.Y / TranslationSnapValue) * TranslationSnapValue,
                        (float)Math.Round(localResult.Z / TranslationSnapValue) * TranslationSnapValue
                    );
                    worldResult = new Vector3(
                        (float)Math.Round(worldResult.X / TranslationSnapValue) * TranslationSnapValue,
                        (float)Math.Round(worldResult.Y / TranslationSnapValue) * TranslationSnapValue,
                        (float)Math.Round(worldResult.Z / TranslationSnapValue) * TranslationSnapValue
                    );
                }

                out_transformLocal = localResult;
                out_transfromWorld = worldResult;
            }
            else if (ActiveMode == GizmoMode.NonUniformScale || ActiveMode == GizmoMode.UniformScale)
            {
                out_transformLocal = deltaTransform;
                out_transfromWorld = deltaTransform;
            }
            else
            {
                throw new Exception("This should never happen - Not scale or translate inside HandleTranslateAndScale");
            }
        }

        private void HandleRotation(GameTime gameTime, out Matrix out_transformLocal, out Matrix out_transfromWorld)
        {
            var delta = _mouse.DeltaPosition().X * (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (SnapEnabled)
            {
                var snapValue = MathHelper.ToRadians(RotationSnapValue);
                _rotationSnapDelta += delta;
                var snapped = (int)(_rotationSnapDelta / snapValue) * snapValue;
                _rotationSnapDelta -= snapped;
                delta = snapped;
            }

            // rotation matrix to transform - if more than one objects selected, always use world-space.
            var rot = Matrix.Identity;
            rot.Forward = SceneWorld.Forward;
            rot.Up = SceneWorld.Up;
            rot.Right = SceneWorld.Right;

            var rotationMatrixLocal = Matrix.Identity;
            rotationMatrixLocal.Forward = SceneWorld.Forward;
            rotationMatrixLocal.Up = SceneWorld.Up;
            rotationMatrixLocal.Right = SceneWorld.Right;

            switch (ActiveAxis)
            {
                case GizmoAxis.X:
                    rot *= Matrix.CreateFromAxisAngle(_rotationMatrix.Right, delta);
                    rotationMatrixLocal *= Matrix.CreateFromAxisAngle(SceneWorld.Right, delta);
                    break;
                case GizmoAxis.Y:
                    rot *= Matrix.CreateFromAxisAngle(_rotationMatrix.Up, delta);
                    rotationMatrixLocal *= Matrix.CreateFromAxisAngle(SceneWorld.Up, delta);
                    break;
                case GizmoAxis.Z:
                    rot *= Matrix.CreateFromAxisAngle(_rotationMatrix.Forward, delta);
                    rotationMatrixLocal *= Matrix.CreateFromAxisAngle(SceneWorld.Forward, delta);
                    break;
            }

            out_transformLocal = rotationMatrixLocal;
            out_transfromWorld = rot;
        }


        /// <summary>
        /// Helper method for applying color to the gizmo lines.
        /// </summary>
        private void ApplyColor(GizmoAxis axis, Color color)
        {
            switch (ActiveMode)
            {
                case GizmoMode.NonUniformScale:
                case GizmoMode.Translate:
                    switch (axis)
                    {
                        case GizmoAxis.X:
                            ApplyLineColor(0, 6, color);
                            break;
                        case GizmoAxis.Y:
                            ApplyLineColor(6, 6, color);
                            break;
                        case GizmoAxis.Z:
                            ApplyLineColor(12, 6, color);
                            break;
                    }
                    break;
                case GizmoMode.Rotate:
                    switch (axis)
                    {
                        case GizmoAxis.X:
                            ApplyLineColor(0, 6, color);
                            break;
                        case GizmoAxis.Y:
                            ApplyLineColor(6, 6, color);
                            break;
                        case GizmoAxis.Z:
                            ApplyLineColor(12, 6, color);
                            break;
                    }
                    break;
                case GizmoMode.UniformScale:
                    ApplyLineColor(0, _translationLineVertices.Length,
                                   ActiveAxis == GizmoAxis.None ? _axisColors[0] : _highlightColor);
                    break;
            }
        }

        private void ApplyLineColor(int startindex, int count, Color color)
        {
            for (var i = startindex; i < startindex + count; i++)
                _translationLineVertices[i].Color = color;
        }

        /// <summary>
        /// Per-frame check to see if mouse is hovering over any axis.
        /// </summary>
        private void SelectAxis(Vector2 mousePosition)
        {
            if (!Enabled)
                return;

            var closestintersection = float.MaxValue;
            var ray = _camera.CreateCameraRay(mousePosition);

            var intersection = XSphere.Intersects(ray);
            if (intersection.HasValue)
                if (intersection.Value < closestintersection)
                {
                    ActiveAxis = GizmoAxis.X;
                    closestintersection = intersection.Value;
                }
            intersection = YSphere.Intersects(ray);
            if (intersection.HasValue)
                if (intersection.Value < closestintersection)
                {
                    ActiveAxis = GizmoAxis.Y;
                    closestintersection = intersection.Value;
                }
            intersection = ZSphere.Intersects(ray);
            if (intersection.HasValue)
                if (intersection.Value < closestintersection)
                {
                    ActiveAxis = GizmoAxis.Z;
                    closestintersection = intersection.Value;
                }

            if (closestintersection >= float.MaxValue || closestintersection <= float.MinValue)
                ActiveAxis = GizmoAxis.None;
        }


        /// <summary>
        /// Set position of the gizmo, position will be center of all selected entities.
        /// </summary>
        private void UpdateGizmoPosition()
        {
            switch (ActivePivot)
            {
                case PivotType.ObjectCenter:
                    if (Selection.Count > 0)
                        _position = Selection[0].GetObjectCentre();
                    break;
                case PivotType.SelectionCenter:
                    _position = GetSelectionCenter();
                    break;
                case PivotType.WorldOrigin:
                    _position = SceneWorld.Translation;
                    break;
            }
        }

        /// <summary>
        /// Returns center position of all selected objectes.
        /// </summary>
        /// <returns></returns>
        private Vector3 GetSelectionCenter()
        {
            if (Selection.Count == 0)
                return Vector3.Zero;

            var center = Vector3.Zero;
            foreach (var selected in Selection)
                center += selected.Position;
            return center / Selection.Count;
        }

        #region Draw
        public void Draw()
        {
            // During modal transform, only draw the dashed line (not the gizmo)
            if (IsInModalTransform)
            {
                DrawModalTransformVisuals();
                return;
            }

            if (!_isActive)
                return;

            _graphics.BlendState = BlendState.AlphaBlend;
            _graphics.DepthStencilState = DepthStencilState.None;
            _graphics.RasterizerState = RasterizerState.CullNone;

            var view = _camera.ViewMatrix;
            var projection = _camera.ProjectionMatrix;

            // -- Draw Lines -- //
            _lineEffect.World = _gizmoWorld;
            _lineEffect.View = view;
            _lineEffect.Projection = projection;

            _lineEffect.CurrentTechnique.Passes[0].Apply();
            _graphics.DrawUserPrimitives(PrimitiveType.LineList, _translationLineVertices, 0, _translationLineVertices.Length / 2);


            // draw the 3d meshes
            for (var i = 0; i < 3; i++) //(order: x, y, z)
            {
                GizmoModel activeModel;
                switch (ActiveMode)
                {
                    case GizmoMode.Translate:
                        activeModel = Geometry.Translate;
                        break;
                    case GizmoMode.Rotate:
                        activeModel = Geometry.Rotate;
                        break;
                    default:
                        activeModel = Geometry.Scale;
                        break;
                }

                Vector3 color;
                switch (ActiveMode)
                {
                    case GizmoMode.UniformScale:
                        color = _axisColors[0].ToVector3();
                        break;
                    default:
                        color = _axisColors[i].ToVector3();
                        break;
                }

                _meshEffect.World = _modelLocalSpace[i] * _gizmoWorld;
                _meshEffect.View = view;
                _meshEffect.Projection = projection;

                _meshEffect.DiffuseColor = color;
                _meshEffect.EmissiveColor = color;

                _meshEffect.CurrentTechnique.Passes[0].Apply();

                _graphics.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,
                    activeModel.Vertices, 0, activeModel.Vertices.Length,
                    activeModel.Indices, 0, activeModel.Indices.Length / 3);
            }

            _graphics.DepthStencilState = DepthStencilState.Default;

            Draw2D(view, projection);
        }

        /// <summary>
        /// Draw dashed line from mouse to pivot during modal transform (Blender-style)
        /// </summary>
        private void DrawModalTransformVisuals()
        {
            var view = _camera.ViewMatrix;
            var projection = _camera.ProjectionMatrix;

            // Get pivot position
            UpdateGizmoPosition();
            var pivotPos = _position;

            // Get mouse screen position
            var mouseScreenPos = _mouse.Position();

            // Project pivot to screen
            var pivotScreen = _graphics.Viewport.Project(pivotPos, projection, view, Matrix.Identity);
            var mouseScreenStart = new Vector3(mouseScreenPos.X, mouseScreenPos.Y, pivotScreen.Z);

            // Draw axis indicator text
            DrawAxisIndicator(view, projection);

            // Draw numeric input display (Blender-style)
            DrawNumericInput();
        }

        /// <summary>
        /// Draw a dashed line using screen-space dash length for consistent visual appearance
        /// </summary>
        private void DrawDashedLineScreenSpace(Vector2 screenStart, Vector2 screenEnd, Vector3 worldStart, Vector3 worldEnd)
        {
            // Calculate screen-space distance
            var screenDelta = screenEnd - screenStart;
            var screenDistance = screenDelta.Length();
            if (screenDistance < 1f)
                return;

            // Calculate number of dashes based on screen-space length
            var dashCount = Math.Min((int)(screenDistance / DASH_SCREEN_LENGTH), MAX_DASHES);
            if (dashCount < 2)
                dashCount = 2;

            // Calculate world-space direction and step
            var worldDirection = worldEnd - worldStart;
            var worldDistance = worldDirection.Length();
            if (worldDistance < 0.001f)
                return;
            worldDirection.Normalize();

            var vertices = new List<VertexPositionColor>();
            var worldDashLength = worldDistance / dashCount;

            // Create dashed pattern (draw every other dash)
            for (int i = 0; i < dashCount; i += 2)
            {
                var dashStart = worldStart + worldDirection * (i * worldDashLength);
                var dashEnd = worldStart + worldDirection * (Math.Min(i + 1, dashCount) * worldDashLength);

                vertices.Add(new VertexPositionColor(dashStart, Color.White));
                vertices.Add(new VertexPositionColor(dashEnd, Color.White));
            }

            if (vertices.Count < 2)
                return;

            _graphics.BlendState = BlendState.AlphaBlend;
            _graphics.DepthStencilState = DepthStencilState.None;
            _graphics.RasterizerState = RasterizerState.CullNone;

            _lineEffect.World = Matrix.Identity;
            _lineEffect.View = _camera.ViewMatrix;
            _lineEffect.Projection = _camera.ProjectionMatrix;
            _lineEffect.CurrentTechnique.Passes[0].Apply();

            _graphics.DrawUserPrimitives(PrimitiveType.LineList, vertices.ToArray(), 0, vertices.Count / 2);

            _graphics.DepthStencilState = DepthStencilState.Default;
        }

        /// <summary>
        /// Draw axis indicator during modal transform (shows X/Y/Z when locked)
        /// </summary>
        private void DrawAxisIndicator(Matrix view, Matrix projection)
        {
            _renderEngineComponent.CommonSpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // Show active axis/plane text near cursor
            if (ActiveAxis != GizmoAxis.None)
            {
                var mousePos = _mouse.Position();
                var axisText = ActiveAxis.ToString();

                // Determine color based on axis/plane
                Color axisColor = ActiveAxis switch
                {
                    GizmoAxis.X => Color.Red,
                    GizmoAxis.Y => Color.Green,
                    GizmoAxis.Z => Color.Blue,
                    GizmoAxis.YZ => Color.Red,    // YZ plane excludes X (red)
                    GizmoAxis.XZ => Color.Green,  // XZ plane excludes Y (green)
                    GizmoAxis.XY => Color.Blue,   // XY plane excludes Z (blue)
                    _ => Color.White
                };

                _renderEngineComponent.CommonSpriteBatch.DrawString(
                    _renderEngineComponent.DefaultFont,
                    axisText,
                    new Vector2(mousePos.X + 15, mousePos.Y + 15),
                    axisColor);
            }

            _renderEngineComponent.CommonSpriteBatch.End();
        }

        /// <summary>
        /// Draw numeric input display during modal transform (Blender-style)
        /// Shows the typed number near the cursor
        /// </summary>
        private void DrawNumericInput()
        {
            if (!IsInNumericInput || _numericInput.Length == 0)
                return;

            _renderEngineComponent.CommonSpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            var mousePos = _mouse.Position();

            // Format the display text based on mode
            string displayText = ActiveMode switch
            {
                GizmoMode.Translate => $"Move: {_numericInput}",
                GizmoMode.Rotate => $"Rotate: {_numericInput}°",
                GizmoMode.NonUniformScale or GizmoMode.UniformScale => $"Scale: {_numericInput}x",
                _ => _numericInput
            };

            // Add axis indicator if locked
            if (ActiveAxis != GizmoAxis.None)
            {
                displayText = $"{ActiveAxis}: {_numericInput}";
                if (ActiveMode == GizmoMode.Rotate)
                    displayText += "°";
            }

            _renderEngineComponent.CommonSpriteBatch.DrawString(
                _renderEngineComponent.DefaultFont,
                displayText,
                new Vector2(mousePos.X + 15, mousePos.Y + 35),
                Color.Yellow);

            _renderEngineComponent.CommonSpriteBatch.End();
        }

        private void Draw2D(Matrix view, Matrix projection)
        {
            _renderEngineComponent.CommonSpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // -- Draw Axis identifiers ("X,Y,Z") -- // 
            for (var i = 0; i < 3; i++)
            {
                var screenPos =
                  _graphics.Viewport.Project(_modelLocalSpace[i].Translation + _modelLocalSpace[i].Backward + _axisTextOffset,
                                             projection, view, _gizmoWorld);

                if (screenPos.Z < 0f || screenPos.Z > 1.0f)
                    continue;

                var color = _axisColors[i];
                switch (i)
                {
                    case 0:
                        if (ActiveAxis == GizmoAxis.X)
                            color = _highlightColor;
                        break;
                    case 1:
                        if (ActiveAxis == GizmoAxis.Y)
                            color = _highlightColor;
                        break;
                    case 2:
                        if (ActiveAxis == GizmoAxis.Z)
                            color = _highlightColor;
                        break;
                }

                _renderEngineComponent.CommonSpriteBatch.DrawString(_renderEngineComponent.DefaultFont, _axisText[i], new Vector2(screenPos.X, screenPos.Y), color);
            }

            _renderEngineComponent.CommonSpriteBatch.End();
        }

        /// <summary>
        /// returns a string filled with status info of the gizmo component. (includes: mode/space/snapping/precision/pivot)
        /// </summary>
        /// <returns></returns>
        #endregion



        #region Event Triggers
        public event TransformationEventHandler TranslateEvent;
        public event TransformationEventHandler RotateEvent;
        public event TransformationEventHandler ScaleEvent;

        public event TransformationStartDelegate StartEvent;
        public event TransformationStopDelegate StopEvent;

        internal event Action<ModalPreviewReplacement>
            ReplacePreviewFromInitialRequested;

        private void OnTranslateEvent(ITransformable transformable, Vector3 delta)
        {
            TranslateEvent?.Invoke(transformable, new TransformationEventArgs(delta, ActivePivot));
        }

        private void OnRotateEvent(ITransformable transformable, Matrix delta)
        {
            RotateEvent?.Invoke(transformable, new TransformationEventArgs(delta, ActivePivot));
        }

        private void OnScaleEvent(ITransformable transformable, Vector3 delta)
        {
            ScaleEvent?.Invoke(transformable, new TransformationEventArgs(delta, ActivePivot));
        }

        #endregion

        #region Helper Functions
        public void ToggleActiveSpace()
        {
            GizmoDisplaySpace = GizmoDisplaySpace == TransformSpace.Local ? TransformSpace.World : TransformSpace.Local;
        }

        public void Dispose()
        {
            _lineEffect.Dispose();
            _meshEffect.Dispose();
        }


        #endregion
    }


    #region Gizmo EventHandlers

    internal enum ModalPreviewReplacementKind
    {
        RestoreOnly,
        Translate,
        Rotate,
        Scale
    }

    internal readonly record struct ModalPreviewReplacement
    {
        public ModalPreviewReplacementKind Kind { get; }
        public Vector3 VectorValue { get; }
        public Matrix RotationValue { get; }
        public PivotType Pivot { get; }

        private ModalPreviewReplacement(
            ModalPreviewReplacementKind kind,
            Vector3 vectorValue,
            Matrix rotationValue,
            PivotType pivot)
        {
            Kind = kind;
            VectorValue = vectorValue;
            RotationValue = rotationValue;
            Pivot = pivot;
        }

        public static ModalPreviewReplacement RestoreOnly(PivotType pivot)
        {
            return new ModalPreviewReplacement(
                ModalPreviewReplacementKind.RestoreOnly,
                Vector3.Zero,
                Matrix.Identity,
                pivot);
        }

        public static ModalPreviewReplacement Translate(
            Vector3 value,
            PivotType pivot)
        {
            return new ModalPreviewReplacement(
                ModalPreviewReplacementKind.Translate,
                value,
                Matrix.Identity,
                pivot);
        }

        public static ModalPreviewReplacement Rotate(
            Matrix value,
            PivotType pivot)
        {
            return new ModalPreviewReplacement(
                ModalPreviewReplacementKind.Rotate,
                Vector3.Zero,
                value,
                pivot);
        }

        public static ModalPreviewReplacement Scale(
            Vector3 value,
            PivotType pivot)
        {
            return new ModalPreviewReplacement(
                ModalPreviewReplacementKind.Scale,
                value,
                Matrix.Identity,
                pivot);
        }
    }

    public class TransformationEventArgs
    {
        public ValueType Value;
        public PivotType Pivot;
        public TransformationEventArgs(ValueType value, PivotType pivot)
        {
            Value = value;
            Pivot = pivot;
        }
    }
    public delegate void TransformationStartDelegate();
    public delegate void TransformationStopDelegate();
    public delegate void TransformationEventHandler(ITransformable transformable, TransformationEventArgs e);

    #endregion

    #region Gizmo Enums

    public enum GizmoAxis
    {
        X,
        Y,
        Z,
        YZ,   // Shift+X: lock to YZ plane (exclude X)
        XZ,   // Shift+Y: lock to XZ plane (exclude Y)
        XY,   // Shift+Z: lock to XY plane (exclude Z)
        None
    }

    public enum GizmoMode
    {
        Translate,
        Rotate,
        NonUniformScale,
        UniformScale
    }

    public enum TransformSpace
    {
        Local,
        World
    }

    public enum PivotType
    {
        ObjectCenter,
        SelectionCenter,
        WorldOrigin
    }

    #endregion
}
