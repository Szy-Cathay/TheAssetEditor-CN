using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using Shared.Core.Services;

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
    public partial class Gizmo : IDisposable
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
        private Matrix _modalWorldOrientation;
        private Matrix _modalLocalOrientation;
        private TransformSpace _modalInitialSpace;
        private Vector2 _modalScreenPivot;
        private Vector2 _modalScaleStartVector;
        private Vector2 _modalRotationPrevious;
        private double _modalRotationAngle;
        private Matrix _modalViewOrientation;
        private Vector2 _modalTrackballAngles;
        public bool IsTrackballRotation { get; private set; }
        private float _modalScreenRotationSign = 1;
        private Vector3 _modalDisplayTranslation;
        private float _modalDisplayAngle;
        private float _modalDisplayScale = 1;
        private readonly VertexPositionColor[] _modalGuideVertices = new VertexPositionColor[128];
        public TransformSpace ModalConstraintSpace { get; private set; }
        public bool IsModalCancelled = false;            // Flag to distinguish cancel from confirm

        // Blender-style virtual mouse value accumulator
        // This ensures frame-rate independent movement and smooth Shift behavior
        private struct VirtualMouseValue
        {
            public Vector2 Prev;      // Previous virtual position
            public Vector2 Accum;     // Accumulated displacement
        }
        private VirtualMouseValue _virtualMouse;
        private bool _confirmOnMouseRelease;

        /// <summary>
        /// Flag to indicate that modal transform just finished.
        /// The active selection input component should check this to prevent selecting other objects.
        /// </summary>
        public bool JustFinishedModalTransform { get; private set; } = false;

        private bool _suppressPointerGestureUntilRelease;

        // -- Numeric Input (Blender-style) -- //
        // User can type numbers directly after G/R/S for precise input
        private string _numericInput = "";            // Current numeric input string
        public bool IsInNumericInput = false;         // Whether user is typing a number
        private float _numericValue = 0f;             // Parsed numeric value
        private ModalPreviewReplacement? _lastModalPreviewReplacement;


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
        public float ModalRotationSnapValue = 5f;
        public float ModalScaleSnapValue = 0.1f;
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
        public void StartModalTransform(GizmoMode mode, Vector2? mouseOrigin = null, bool confirmOnMouseRelease = false)
        {
            if (Selection.Count == 0)
                return;

            ActiveMode = mode;
            IsTrackballRotation = false;
            _confirmOnMouseRelease = confirmOnMouseRelease;
            ActiveAxis = GizmoAxis.None;
            ResetNumericInput();
            _modalWorldOrientation = Matrix.CreateFromQuaternion(Quaternion.CreateFromRotationMatrix(SceneWorld));
            _modalLocalOrientation = Matrix.CreateFromQuaternion(Quaternion.Normalize(Selection[0].Orientation));
            _modalInitialSpace = GizmoDisplaySpace;
            SetModalConstraintSpace(_modalInitialSpace);
            IsInModalTransform = true;
            IsModalCancelled = false;
            _lastModalPreviewReplacement =
                ModalPreviewReplacement.RestoreOnly(ActivePivot);

            // Save initial mouse position (Blender: imval)
            _modalTransformStartMousePos = mouseOrigin ?? _mouse.Position();

            // Initialize virtual mouse accumulator (Blender-style)
            // Reference: transform_input.cc applyMouseInput()
            // IMPORTANT: Prev must be current mouse position, not Zero, to avoid first-frame jump
            _virtualMouse.Prev = _modalTransformStartMousePos;
            _virtualMouse.Accum = Vector2.Zero;

            // Save pivot position
            UpdateGizmoPosition();
            _modalStartPivot = _position;
            var projectedPivot = GetInputViewport().Project(_modalStartPivot, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            _modalScreenPivot = new Vector2(projectedPivot.X, projectedPivot.Y);
            _modalScaleStartVector = _modalTransformStartMousePos - _modalScreenPivot;
            if (_modalScaleStartVector.LengthSquared() < 1)
                _modalScaleStartVector = Vector2.UnitX;
            _modalRotationPrevious = _modalTransformStartMousePos;
            _modalRotationAngle = 0;
            _modalViewOrientation = Matrix.Invert(_camera.ViewMatrix);
            _modalTrackballAngles = Vector2.Zero;
            var projection = _camera.ProjectionMatrix;
            _modalScreenRotationSign = projection.M11 * projection.M22 - projection.M12 * projection.M21 < 0 ? -1 : 1;
            _modalDisplayTranslation = Vector3.Zero;
            _modalDisplayAngle = 0;
            _modalDisplayScale = 1;

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
            _mouse.BeginContinuousDrag(mode != GizmoMode.Rotate);
        }

        public void ToggleTrackballRotation()
        {
            if (!IsInModalTransform || ActiveMode != GizmoMode.Rotate || _confirmOnMouseRelease)
                return;
            IsTrackballRotation = !IsTrackballRotation;
            ActiveAxis = GizmoAxis.None;
            ResetNumericInput();
            _mouse.EndContinuousDrag();
            _mouse.BeginContinuousDrag(IsTrackballRotation);
            _virtualMouse.Prev = _mouse.Position();
            _modalRotationPrevious = _virtualMouse.Prev;
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
            ResetNumericInput();
            _lastModalPreviewReplacement = null;

            // Reset cursor to default
            _mouse.ResetCursor();
            _mouse.EndContinuousDrag();

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
            ResetNumericInput();
            ResetDeltas();
            _lastModalPreviewReplacement = null;

            // Reset cursor to default
            _mouse.ResetCursor();
            _mouse.EndContinuousDrag();

            StopEvent?.Invoke();
        }

        public void AbortTransformInteraction()
        {
            _mouse.EndContinuousDrag();
            var wasModalTransform = IsInModalTransform;

            IsInModalTransform = false;
            IsModalCancelled = false;
            ActiveAxis = GizmoAxis.None;
            _suppressPointerGestureUntilRelease =
                _mouse.State().LeftButton == ButtonState.Pressed;
            _virtualMouse = default;
            ResetNumericInput();
            _lastModalPreviewReplacement = null;
            ResetDeltas();

            if (wasModalTransform)
            {
                JustFinishedModalTransform = true;
                _mouse.ResetCursor();
            }
        }

        /// <summary>
        /// Clear the JustFinishedModalTransform flag after the active selection input component has checked it.
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
            var planeLock = (_keyboard.IsKeyDownOrReleased(Keys.LeftShift) ||
                _keyboard.IsKeyDownOrReleased(Keys.RightShift)) && ActiveMode != GizmoMode.Rotate;
            if (!IsTrackballRotation && !_numericExpressionMode && _keyboard.IsKeyReleased(Keys.X))
                CycleModalConstraint(planeLock ? GizmoAxis.YZ : GizmoAxis.X);
            else if (!IsTrackballRotation && !_numericExpressionMode && _keyboard.IsKeyReleased(Keys.Y))
                CycleModalConstraint(planeLock ? GizmoAxis.XZ : GizmoAxis.Y);
            else if (!IsTrackballRotation && !_numericExpressionMode && _keyboard.IsKeyReleased(Keys.Z))
                CycleModalConstraint(planeLock ? GizmoAxis.XY : GizmoAxis.Z);

            // Cancel via Right mouse button or Escape
            if (_mouse.IsMouseButtonPressed(MouseButton.Right) || _keyboard.IsKeyReleased(Keys.Escape))
            {
                CancelModalTransform();
                return;
            }

            // The input layer removes cursor warps before publishing continuous coordinates.
            var currentMousePos = _mouse.Position();
            var frameDelta = currentMousePos - _virtualMouse.Prev;
            _virtualMouse.Prev = currentMousePos;

            // -- Precision Mode (Shift key) --
            // Blender: Scale the frame delta for precise control
            const float precisionFactor = 0.1f;  // Blender default: 1/10
            bool isPrecisionNow = _keyboard.IsKeyDown(Keys.LeftShift) || _keyboard.IsKeyDown(Keys.RightShift);

            if (ActiveMode == GizmoMode.Rotate && !IsTrackballRotation)
            {
                var previous = _modalRotationPrevious - _modalScreenPivot;
                var current = currentMousePos - _modalScreenPivot;
                if (previous.LengthSquared() >= 1 && current.LengthSquared() >= 1)
                {
                    var angle = Math.Atan2((double)previous.X * current.Y - (double)previous.Y * current.X,
                        (double)previous.X * current.X + (double)previous.Y * current.Y);
                    _modalRotationAngle += angle * (isPrecisionNow ? 1.0 / 30 : 1);
                }
                _modalRotationPrevious = currentMousePos;
            }

            if (isPrecisionNow)
            {
                frameDelta *= ActiveMode == GizmoMode.Rotate ? 1f / 30 : precisionFactor;
            }

            // Accumulate the delta (Blender-style virtual accumulator)
            _virtualMouse.Accum += frameDelta;

            // The final displacement is the accumulated value
            var finalDisplacement = _virtualMouse.Accum;

            if (IsInNumericInput)
            {
                ApplyNumericInput();
            }
            else
            {
                // Apply transform based on mode using absolute displacement.
                switch (ActiveMode)
                {
                    case GizmoMode.Translate:
                        {
                            var totalTranslation = CalculateAbsoluteTranslation(finalDisplacement);
                            ApplyModalTranslationFromInitial(totalTranslation);
                            break;
                        }
                    case GizmoMode.Rotate:
                        {
                            if (IsTrackballRotation)
                                ApplyTrackballRotationFromInitial(new Vector2(finalDisplacement.Y,
                                    finalDisplacement.X * _modalScreenRotationSign) * 0.01f);
                            else
                                ApplyModalRotationFromInitial((float)_modalRotationAngle);
                            break;
                        }
                    case GizmoMode.NonUniformScale:
                    case GizmoMode.UniformScale:
                        {
                            var currentVector = _modalScaleStartVector + finalDisplacement;
                            var totalScaleFactor = currentVector.Length() / _modalScaleStartVector.Length();
                            if (Vector2.Dot(currentVector, _modalScaleStartVector) < 0)
                                totalScaleFactor = -totalScaleFactor;
                            ApplyModalScaleFromInitial(totalScaleFactor);
                            break;
                        }
                }
            }

            // Commit the same preview displayed for this input frame.
            if ((_confirmOnMouseRelease ? _mouse.IsMouseButtonReleased(MouseButton.Left) : _mouse.IsMouseButtonPressed(MouseButton.Left)) ||
                _keyboard.IsKeyReleased(Keys.Enter))
            {
                if (!IsInNumericInput || _numericValid)
                    ConfirmModalTransform();
            }
        }

        private void SetModalConstraintSpace(TransformSpace space)
        {
            ModalConstraintSpace = space;
            _rotationMatrix = space == TransformSpace.Local
                ? _modalLocalOrientation
                : _modalWorldOrientation;
        }

        private void CycleModalConstraint(GizmoAxis axis)
        {
            if (ActiveAxis != axis)
            {
                ActiveAxis = axis;
                SetModalConstraintSpace(_modalInitialSpace);
            }
            else if (ModalConstraintSpace == _modalInitialSpace)
            {
                SetModalConstraintSpace(_modalInitialSpace == TransformSpace.World
                    ? TransformSpace.Local
                    : TransformSpace.World);
            }
            else
            {
                ActiveAxis = GizmoAxis.None;
                SetModalConstraintSpace(_modalInitialSpace);
            }
        }

        private bool IsModalPrecisionEnabled =>
            _keyboard != null && (_keyboard.IsKeyDown(Keys.LeftShift) || _keyboard.IsKeyDown(Keys.RightShift));

        private static float SnapModalValue(float value, float increment) =>
            increment > 0 ? MathF.Round(value / increment) * increment : value;

        /// <summary>
        /// Calculate absolute translation from accumulated mouse displacement
        /// This is frame-rate independent and matches Blender's approach
        /// </summary>
        private Vector3 CalculateAbsoluteTranslation(Vector2 totalDisplacement)
        {
            var viewport = GetInputViewport();
            var projection = _camera.ProjectionMatrix;
            var view = _camera.ViewMatrix;
            var viewProjection = view * projection;
            var clipPivot = Vector4.Transform(new Vector4(_modalStartPivot, 1), viewProjection);
            var clipDelta = new Vector4(totalDisplacement.X * 2 / viewport.Width * clipPivot.W,
                -totalDisplacement.Y * 2 / viewport.Height * clipPivot.W, 0, 0);
            // Blender's InputVector converts displacement at the pivot's depth, independent of grab offset.
            var worldDelta = Vector4.Transform(clipDelta, Matrix.Invert(viewProjection));
            var freeDelta = new Vector3(worldDelta.X, worldDelta.Y, worldDelta.Z);
            if (ActiveAxis == GizmoAxis.None)
                return freeDelta;

            var axis = ActiveAxis switch
            {
                GizmoAxis.X or GizmoAxis.YZ => _rotationMatrix.Right,
                GizmoAxis.Y or GizmoAxis.XZ => _rotationMatrix.Up,
                _ => _rotationMatrix.Backward
            };
            var planeLock = ActiveAxis is GizmoAxis.YZ or GizmoAxis.XZ or GizmoAxis.XY;
            var viewInverse = Matrix.Invert(view);
            var viewBackward = Vector3.Normalize(viewInverse.Backward);
            // Blender axisProjection switches to vertical depth control within five degrees of the view axis.
            if (!planeLock && MathF.Abs(Vector3.Dot(axis, viewBackward)) > MathF.Cos(MathHelper.ToRadians(5)))
            {
                var depthControl = Vector3.Dot(freeDelta, Vector3.Normalize(viewInverse.Up)) * 2;
                return axis * (-depthControl * MathF.Abs(depthControl));
            }

            var center = _modalStartPivot;
            if (!planeLock)
            {
                var distance = MathF.Abs(Vector3.Dot(center - viewInverse.Translation, viewBackward));
                if (distance < 1)
                    center -= viewBackward * (1 - distance);
            }
            Vector3 ViewDirectionAt(Vector3 point) => projection.M44 == 0
                ? Vector3.Normalize(viewInverse.Translation - point)
                : viewBackward;
            var centerDirection = ViewDirectionAt(center);
            var normal = planeLock ? axis : centerDirection - axis * Vector3.Dot(centerDirection, axis);
            var fallback = planeLock ? freeDelta - axis * Vector3.Dot(freeDelta, axis)
                : axis * Vector3.Dot(freeDelta, axis);
            if (normal.LengthSquared() < 0.000001f ||
                (planeLock && MathF.Abs(Vector3.Dot(axis, centerDirection)) < 0.001f))
                return fallback;

            normal.Normalize();
            var direction = ViewDirectionAt(center + freeDelta);
            var denominator = Vector3.Dot(normal, direction);
            if (MathF.Abs(denominator) < 0.000001f)
                return fallback;
            var constrained = freeDelta - direction * (Vector3.Dot(freeDelta, normal) / denominator);
            return planeLock ? constrained : axis * Vector3.Dot(constrained, axis);
        }

        private Viewport GetInputViewport()
        {
            var size = _mouse.GetScreenSize();
            return size.X > 0 && size.Y > 0
                ? new Viewport(0, 0, (int)size.X, (int)size.Y)
                : _graphics.Viewport;
        }

        /// <summary>
        /// Apply translation from initial state using absolute displacement
        /// Called each frame with total translation from initial position
        /// </summary>
        private void ApplyModalTranslationFromInitial(Vector3 totalTranslation)
        {
            // Apply translation snap (increment snap)
            if (SnapEnabled && !IsInNumericInput && TranslationSnapValue > 0)
            {
                var increment = TranslationSnapValue * (IsModalPrecisionEnabled ? 0.1f : 1f);
                var local = Vector3.TransformNormal(totalTranslation, Matrix.Transpose(_rotationMatrix));
                totalTranslation = new Vector3(
                    SnapModalValue(local.X, increment),
                    SnapModalValue(local.Y, increment),
                    SnapModalValue(local.Z, increment)
                );
                totalTranslation = Vector3.TransformNormal(totalTranslation, _rotationMatrix);
            }

            _modalDisplayTranslation = totalTranslation;
            if (totalTranslation == Vector3.Zero)
            {
                RequestModalPreviewReplacement(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            RequestModalPreviewReplacement(
                ModalPreviewReplacement.Translate(
                    totalTranslation,
                    ActivePivot));
        }

        private void RequestModalPreviewReplacement(
            ModalPreviewReplacement replacement)
        {
            if (_lastModalPreviewReplacement.HasValue &&
                _lastModalPreviewReplacement.Value == replacement)
            {
                return;
            }

            ReplacePreviewFromInitialRequested?.Invoke(replacement);
            _lastModalPreviewReplacement = replacement;
        }

        /// <summary>
        /// Handle numeric input during modal transform (Blender-style)
        /// User can type numbers directly for precise transformation
        /// </summary>
        private void ApplyModalRotationFromInitial(float totalAngle)
        {
            if (SnapEnabled && !IsInNumericInput)
                totalAngle = SnapModalValue(totalAngle, MathHelper.ToRadians(IsModalPrecisionEnabled ? 1f : ModalRotationSnapValue));

            _modalDisplayAngle = MathHelper.ToDegrees(totalAngle);
            if (totalAngle == 0)
            {
                RequestModalPreviewReplacement(
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
                        axis = _rotationMatrix.Backward;
                        break;
                    default:
                        RequestModalPreviewReplacement(
                            ModalPreviewReplacement.RestoreOnly(ActivePivot));
                        return;
                }
            }

            if (!IsInNumericInput)
            {
                // The game camera mirrors its projection horizontally.
                totalAngle *= _modalScreenRotationSign;
                if (ActiveAxis != GizmoAxis.None)
                {
                    if (Vector3.Dot(axis, _camera.LookAt - _camera.Position) < 0)
                        totalAngle = -totalAngle;
                    _modalDisplayAngle = MathHelper.ToDegrees(totalAngle);
                }
            }

            Matrix rotMatrix = Matrix.CreateFromAxisAngle(axis, totalAngle);

            RequestModalPreviewReplacement(
                ModalPreviewReplacement.Rotate(rotMatrix, ActivePivot));
        }

        private void ApplyTrackballRotationFromInitial(Vector2 angles)
        {
            if (SnapEnabled && !IsInNumericInput)
            {
                var increment = MathHelper.ToRadians(IsModalPrecisionEnabled ? 1f : ModalRotationSnapValue);
                angles = new Vector2(SnapModalValue(angles.X, increment), SnapModalValue(angles.Y, increment));
            }
            _modalTrackballAngles = angles;
            var axis = Vector3.Normalize(_modalViewOrientation.Right) * angles.X +
                Vector3.Normalize(_modalViewOrientation.Up) * angles.Y;
            var angle = axis.Length();
            RequestModalPreviewReplacement(angle < 0.0000001f
                ? ModalPreviewReplacement.RestoreOnly(ActivePivot)
                : ModalPreviewReplacement.Rotate(Matrix.CreateFromAxisAngle(axis / angle, angle), ActivePivot));
        }

        /// <summary>
        /// Apply scale from initial state (called each frame with total scale factor)
        /// </summary>
        private void ApplyModalScaleFromInitial(float scaleFactor)
        {
            if (SnapEnabled && !IsInNumericInput)
                scaleFactor = SnapModalValue(scaleFactor, ModalScaleSnapValue * (IsModalPrecisionEnabled ? 0.1f : 1f));

            if (!float.IsFinite(scaleFactor))
            {
                RequestModalPreviewReplacement(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            _modalDisplayScale = scaleFactor;
            var scale = CreateModalScaleDelta(scaleFactor, ActiveAxis);

            if (scale == Vector3.Zero)
            {
                RequestModalPreviewReplacement(
                    ModalPreviewReplacement.RestoreOnly(ActivePivot));
                return;
            }

            RequestModalPreviewReplacement(
                ModalPreviewReplacement.Scale(scale, ActivePivot, _rotationMatrix));
        }

        internal static Vector3 CreateModalScaleDelta(float scaleFactor, GizmoAxis axis)
        {
            var delta = scaleFactor - 1f;
            return axis switch
            {
                GizmoAxis.X => new Vector3(delta, 0, 0),
                GizmoAxis.Y => new Vector3(0, delta, 0),
                GizmoAxis.Z => new Vector3(0, 0, delta),
                GizmoAxis.YZ => new Vector3(0, delta, delta),
                GizmoAxis.XZ => new Vector3(delta, 0, delta),
                GizmoAxis.XY => new Vector3(delta, delta, 0),
                _ => new Vector3(delta)
            };
        }

        internal static Vector3 CreateNumericTranslation(
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            GizmoAxis axis,
            float value)
        {
            var direction = axis switch
            {
                GizmoAxis.X => right,
                GizmoAxis.Y => up,
                GizmoAxis.Z => forward,
                GizmoAxis.YZ => up + forward,
                GizmoAxis.XZ => right + forward,
                GizmoAxis.XY => right + up,
                _ => right
            };
            return direction * value;
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
                    if (!suppressReleaseThisUpdate && _mouse.State().LeftButton == ButtonState.Released && _mouse.State().RightButton == ButtonState.Released)
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

            _screenScale = (_camera.CurrentProjectionType == ProjectionType.Orthographic
                ? _camera.OrthoSize / (2 * MathF.Tan(MathHelper.PiOver4 / 2))
                : vLength.Length()) / scaleFactor;
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
                case GizmoAxis.XY:
                    plane = new Plane(Vector3.Forward, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).Z);
                    break;
                case GizmoAxis.Z:
                case GizmoAxis.Y:
                case GizmoAxis.YZ:
                    plane = new Plane(Vector3.Left, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).X);
                    break;
                case GizmoAxis.XZ:
                    plane = new Plane(Vector3.Down, Vector3.Transform(_position, Matrix.Invert(_rotationMatrix)).Y);
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
                    case GizmoAxis.XY:
                        deltaTransform = new Vector3(mouseDragDelta.X, mouseDragDelta.Y, 0);
                        break;
                    case GizmoAxis.XZ:
                        deltaTransform = new Vector3(mouseDragDelta.X, 0, mouseDragDelta.Z);
                        break;
                    case GizmoAxis.YZ:
                        deltaTransform = new Vector3(0, mouseDragDelta.Y, mouseDragDelta.Z);
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
            if (ActiveMode is GizmoMode.Translate or GizmoMode.NonUniformScale)
            {
                if (axis == GizmoAxis.XY)
                {
                    ApplyLineColor(2, 2, color);
                    ApplyLineColor(8, 2, color);
                }
                else if (axis == GizmoAxis.XZ)
                {
                    ApplyLineColor(4, 2, color);
                    ApplyLineColor(14, 2, color);
                }
                else if (axis == GizmoAxis.YZ)
                {
                    ApplyLineColor(10, 2, color);
                    ApplyLineColor(16, 2, color);
                }
            }
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
        public void SelectAxis(Vector2 mousePosition)
        {
            ActiveAxis = GizmoAxis.None;
            if (!Enabled || Selection.Count == 0 || _suppressPointerGestureUntilRelease)
                return;

            if (ActiveMode is GizmoMode.Translate or GizmoMode.NonUniformScale or GizmoMode.UniformScale)
            {
                SelectTranslationHandle(mousePosition);
                return;
            }

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

        private void SelectTranslationHandle(Vector2 mousePosition)
        {
            var viewport = _camera.InputViewport;
            var bestDistance = 7f;
            var bestDepth = float.MaxValue;
            for (var index = 0; index < 3; index++)
            {
                var start = viewport.Project(_translationLineVertices[index * 6].Position,
                    _camera.ProjectionMatrix, _camera.ViewMatrix, _gizmoWorld);
                var stop = viewport.Project(_translationLineVertices[index * 6 + 1].Position,
                    _camera.ProjectionMatrix, _camera.ViewMatrix, _gizmoWorld);
                if (!float.IsFinite(start.X) || !float.IsFinite(stop.X) || start.Z < 0 || stop.Z < 0 || start.Z > 1 || stop.Z > 1)
                    continue;
                var first = new Vector2(start.X, start.Y);
                var segment = new Vector2(stop.X, stop.Y) - first;
                var amount = segment.LengthSquared() > 0.0001f
                    ? Math.Clamp(Vector2.Dot(mousePosition - first, segment) / segment.LengthSquared(), 0, 1)
                    : 1f;
                var distance = Vector2.Distance(mousePosition, first + amount * segment);
                var depth = MathHelper.Lerp(start.Z, stop.Z, amount);
                if (distance < bestDistance || MathF.Abs(distance - bestDistance) < 0.001f && depth < bestDepth)
                {
                    bestDistance = distance;
                    bestDepth = depth;
                    ActiveAxis = (GizmoAxis)index;
                }
            }
            if (ActiveAxis != GizmoAxis.None)
                return;

            var worldRay = _camera.CreateCameraRay(mousePosition);
            var inverse = Matrix.Invert(_gizmoWorld);
            var ray = new Ray(Vector3.Transform(worldRay.Position, inverse), Vector3.TransformNormal(worldRay.Direction, inverse));
            var closest = float.MaxValue;
            TestPlane(GizmoAxis.XY, Vector3.UnitZ);
            TestPlane(GizmoAxis.XZ, Vector3.UnitY);
            TestPlane(GizmoAxis.YZ, Vector3.UnitX);
            if (ActiveAxis != GizmoAxis.None)
                return;

            TestTip(GizmoAxis.X, XSphere);
            TestTip(GizmoAxis.Y, YSphere);
            TestTip(GizmoAxis.Z, ZSphere);

            void TestPlane(GizmoAxis axis, Vector3 normal)
            {
                if (ActiveMode == GizmoMode.UniformScale)
                    return;
                var denominator = Vector3.Dot(ray.Direction, normal);
                if (MathF.Abs(denominator) < 0.00001f)
                    return;
                var distance = -Vector3.Dot(ray.Position, normal) / denominator;
                if (distance < 0 || distance >= closest)
                    return;
                var point = ray.Position + ray.Direction * distance;
                var first = axis == GizmoAxis.YZ ? point.Y : point.X;
                var second = axis == GizmoAxis.XY ? point.Y : point.Z;
                if (first < LINE_OFFSET * 0.15f || first > LINE_OFFSET || second < LINE_OFFSET * 0.15f || second > LINE_OFFSET)
                    return;
                ActiveAxis = axis;
                closest = distance;
            }

            void TestTip(GizmoAxis axis, BoundingSphere sphere)
            {
                var distance = sphere.Intersects(worldRay);
                if (distance.HasValue && distance.Value < closest)
                {
                    ActiveAxis = axis;
                    closest = distance.Value;
                }
            }
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
            var inputViewport = GetInputViewport();
            var renderSize = new Vector2(_graphics.Viewport.Width, _graphics.Viewport.Height);
            var coordinateScale = renderSize / new Vector2(inputViewport.Width, inputViewport.Height);
            var mouse = (_mouse.CapturedCursorPosition ?? _mouse.Position()) * coordinateScale;
            _graphics.BlendState = BlendState.AlphaBlend;
            _graphics.DepthStencilState = DepthStencilState.None;
            _graphics.RasterizerState = RasterizerState.CullNone;
            if (ActiveAxis != GizmoAxis.None)
                DrawModalConstraintAxes();
            if (ActiveMode != GizmoMode.Translate)
                DrawModalGuide(_modalScreenPivot * coordinateScale, mouse, renderSize);

            static string Format(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var value = IsInNumericInput ? NumericInputLabel() : ActiveMode switch
            {
                GizmoMode.Translate => $"{Format(_modalDisplayTranslation.X)}, {Format(_modalDisplayTranslation.Y)}, {Format(_modalDisplayTranslation.Z)}",
                GizmoMode.Rotate => IsTrackballRotation
                    ? $"{Format(MathHelper.ToDegrees(_modalTrackballAngles.X))}, {Format(MathHelper.ToDegrees(_modalTrackballAngles.Y))}"
                    : Format(_modalDisplayAngle),
                _ => Format(_modalDisplayScale)
            };
            var key = ActiveMode switch
            {
                GizmoMode.Translate => "Viewport.Transform.Move",
                GizmoMode.Rotate => IsTrackballRotation ? "Viewport.Transform.Trackball" : "Viewport.Transform.Rotate",
                _ => "Viewport.Transform.Scale"
            };
            var unit = ActiveMode == GizmoMode.Rotate ? "°" : ActiveMode == GizmoMode.Translate ? "" : "x";
            var text = $"{LocalizationManager.Instance.Get(key)}: {value}{unit}";
            if (ActiveAxis != GizmoAxis.None)
            {
                var spaceKey = ModalConstraintSpace == TransformSpace.Local ? "Viewport.Transform.Local" : "Viewport.Transform.World";
                text = $"{ActiveAxis} ({LocalizationManager.Instance.Get(spaceKey)})\n{text}";
            }
            var font = _renderEngineComponent.DefaultFont;
            var size = font.MeasureString(text);
            var position = mouse + new Vector2(15);
            if (position.X + size.X > renderSize.X - 8)
                position.X = mouse.X - size.X - 15;
            position = Vector2.Clamp(position, new Vector2(8), Vector2.Max(new Vector2(8), renderSize - size - new Vector2(8)));
            var sprites = _renderEngineComponent.CommonSpriteBatch;
            sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            sprites.DrawString(font, text, position + Vector2.One, Color.Black);
            sprites.DrawString(font, text, position, Color.Yellow);
            sprites.End();
        }

        private void DrawModalGuide(Vector2 pivot, Vector2 mouse, Vector2 size)
        {
            var distance = Vector2.Distance(pivot, mouse);
            if (!float.IsFinite(distance) || distance < 1)
                return;
            var direction = (mouse - pivot) / distance;
            var dashSpacing = MathF.Max(10, distance / 64);
            var count = 0;
            for (var offset = 0f; offset < distance && count < _modalGuideVertices.Length; offset += dashSpacing)
            {
                _modalGuideVertices[count++] = new VertexPositionColor(new Vector3(pivot + direction * offset, 0), Color.White);
                _modalGuideVertices[count++] = new VertexPositionColor(new Vector3(pivot + direction * MathF.Min(offset + dashSpacing * 0.5f, distance), 0), Color.White);
            }
            _lineEffect.World = Matrix.Identity;
            _lineEffect.View = Matrix.Identity;
            _lineEffect.Projection = Matrix.CreateOrthographicOffCenter(0, size.X, size.Y, 0, 0, 1);
            _graphics.DepthStencilState = DepthStencilState.None;
            _lineEffect.CurrentTechnique.Passes[0].Apply();
            _graphics.DrawUserPrimitives(PrimitiveType.LineList, _modalGuideVertices, 0, count / 2);
        }

        private void DrawModalConstraintAxes()
        {
            var length = MathF.Max(10, Vector3.Distance(_camera.Position, _modalStartPivot) * 10);
            var count = 0;
            for (var index = 0; index < 3; index++)
            {
                var visible = index switch
                {
                    0 => ActiveAxis is GizmoAxis.X or GizmoAxis.XY or GizmoAxis.XZ,
                    1 => ActiveAxis is GizmoAxis.Y or GizmoAxis.XY or GizmoAxis.YZ,
                    _ => ActiveAxis is GizmoAxis.Z or GizmoAxis.XZ or GizmoAxis.YZ
                };
                if (!visible)
                    continue;
                var direction = index == 0 ? _rotationMatrix.Right : index == 1 ? _rotationMatrix.Up : _rotationMatrix.Forward;
                _modalGuideVertices[count++] = new VertexPositionColor(_modalStartPivot - direction * length, _axisColors[index]);
                _modalGuideVertices[count++] = new VertexPositionColor(_modalStartPivot + direction * length, _axisColors[index]);
            }
            _lineEffect.World = Matrix.Identity;
            _lineEffect.View = _camera.ViewMatrix;
            _lineEffect.Projection = _camera.ProjectionMatrix;
            _lineEffect.CurrentTechnique.Passes[0].Apply();
            _graphics.DrawUserPrimitives(PrimitiveType.LineList, _modalGuideVertices, 0, count / 2);
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

        public event Action<ModalPreviewReplacement>
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

    public enum ModalPreviewReplacementKind
    {
        RestoreOnly,
        Translate,
        Rotate,
        Scale
    }

    public readonly record struct ModalPreviewReplacement
    {
        public ModalPreviewReplacementKind Kind { get; }
        public Vector3 VectorValue { get; }
        public Matrix RotationValue { get; }
        public Matrix ScaleOrientation { get; }
        public PivotType Pivot { get; }

        private ModalPreviewReplacement(
            ModalPreviewReplacementKind kind,
            Vector3 vectorValue,
            Matrix rotationValue,
            PivotType pivot,
            Matrix? scaleOrientation = null)
        {
            Kind = kind;
            VectorValue = vectorValue;
            RotationValue = rotationValue;
            Pivot = pivot;
            ScaleOrientation = scaleOrientation ?? Matrix.Identity;
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
            PivotType pivot,
            Matrix? orientation = null)
        {
            return new ModalPreviewReplacement(
                ModalPreviewReplacementKind.Scale,
                value,
                Matrix.Identity,
                pivot,
                orientation);
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
