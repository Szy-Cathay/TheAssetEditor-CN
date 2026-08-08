using System;
using System.Collections.Generic;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using MouseButton = GameWorld.Core.Components.Input.MouseButton;

namespace AnimationEditor.MountAnimationCreator
{
    public sealed class MountVertexSelectionComponent :
        BaseComponent,
        IDisposable
    {
        private readonly SelectionManager _selectionManager;
        private readonly IKeyboardComponent _keyboard;
        private readonly IMouseComponent _mouse;
        private readonly ArcBallCamera _camera;
        private readonly IDeviceResolver _deviceResolver;
        private readonly IScopedResourceLibrary _resourceLibrary;
        private readonly RenderEngineComponent _renderEngine;
        private VertexInstanceMesh? _vertexRenderer;
        private VertexRenderItem? _vertexRenderItem;
        private Rmv2MeshNode? _wireframeMesh;
        private AnimatedWireframeRenderItem? _wireframeRenderItem;
        private bool _isMouseDown;
        private Vector2 _startDrag;
        private Vector2 _currentMousePosition;

        public MountVertexSelectionComponent(
            SelectionManager selectionManager,
            IKeyboardComponent keyboard,
            IMouseComponent mouse,
            ArcBallCamera camera,
            IDeviceResolver deviceResolver,
            IScopedResourceLibrary resourceLibrary,
            RenderEngineComponent renderEngine)
        {
            _selectionManager = selectionManager;
            _keyboard = keyboard;
            _mouse = mouse;
            _camera = camera;
            _deviceResolver = deviceResolver;
            _resourceLibrary = resourceLibrary;
            _renderEngine = renderEngine;
            UpdateOrder = (int)ComponentUpdateOrderEnum.SelectionComponent;
        }

        public override void Initialize()
        {
            if (_selectionManager.GetState() == null)
            {
                _selectionManager.CreateSelectionSate(
                    GeometrySelectionMode.Object,
                    null,
                    false);
            }

            _vertexRenderer = new VertexInstanceMesh(
                _deviceResolver,
                _resourceLibrary);
            _vertexRenderItem = new VertexRenderItem
            {
                VertexRenderer = _vertexRenderer,
            };
            base.Initialize();
        }

        public override void Update(GameTime gameTime)
        {
            if (_keyboard.IsKeyReleased(Keys.Tab) &&
                !_keyboard.IsKeyDownOrReleased(Keys.LeftAlt) &&
                !_keyboard.IsKeyDownOrReleased(Keys.RightAlt))
            {
                ToggleVertexSelectionMode();
            }

            if (_selectionManager.GetState() is not
                VertexSelectionState vertexSelection)
            {
                return;
            }

            if (!_mouse.IsMouseOwner(this))
                return;

            _currentMousePosition = _mouse.Position();
            if (_mouse.IsMouseButtonPressed(MouseButton.Left))
            {
                _startDrag = _currentMousePosition;
                _isMouseDown = true;
                if (_mouse.MouseOwner != this)
                    _mouse.MouseOwner = this;
            }

            if (_mouse.IsMouseButtonReleased(MouseButton.Left))
            {
                var rectangle = CreateSelectionRectangle(
                    _startDrag,
                    _currentMousePosition);
                var isModification =
                    _keyboard.IsKeyDown(Keys.LeftShift);
                var removeSelection =
                    _keyboard.IsKeyDown(Keys.LeftControl);
                if (_isMouseDown && rectangle.Width * rectangle.Height >= 10)
                {
                    SelectFromRectangle(
                        vertexSelection,
                        rectangle,
                        isModification,
                        removeSelection);
                }
                else
                {
                    SelectFromPoint(
                        vertexSelection,
                        _currentMousePosition,
                        isModification,
                        removeSelection);
                }

                _isMouseDown = false;
            }

            if (!_isMouseDown && _mouse.MouseOwner == this)
            {
                _mouse.MouseOwner = null;
                _mouse.ClearStates();
            }
        }

        private void ToggleVertexSelectionMode()
        {
            var state = _selectionManager.GetState();
            if (state is ObjectSelectionState objectSelection)
            {
                var selectedObject =
                    objectSelection.GetSingleSelectedObject();
                if (selectedObject != null)
                {
                    _selectionManager.CreateSelectionSate(
                        GeometrySelectionMode.Vertex,
                        selectedObject);
                }
                return;
            }

            if (state is not VertexSelectionState vertexSelection)
                return;

            var renderObject = vertexSelection.RenderObject;
            var replacement = (ObjectSelectionState)
                _selectionManager.CreateSelectionSate(
                    GeometrySelectionMode.Object,
                    null);
            replacement.ModifySelection([renderObject], false);
        }

        private void SelectFromRectangle(
            VertexSelectionState state,
            Rectangle screenRectangle,
            bool isModification,
            bool removeSelection)
        {
            var worldPositions = GetWorldPositions(state);
            var intersects = IntersectionMath.IntersectVertices(
                _camera.UnprojectRectangle(screenRectangle),
                state.RenderObject.Geometry,
                worldPositions,
                out var vertices);
            ApplySelection(
                state,
                intersects ? vertices : [],
                isModification,
                removeSelection);
        }

        private void SelectFromPoint(
            VertexSelectionState state,
            Vector2 mousePosition,
            bool isModification,
            bool removeSelection)
        {
            var viewport = _deviceResolver.Device.Viewport;
            var distance = IntersectionMath.IntersectVertex(
                mousePosition,
                state.RenderObject.Geometry,
                GetWorldPositions(state),
                _camera.ViewMatrix * _camera.ProjectionMatrix,
                viewport.Width,
                viewport.Height,
                out var vertex,
                new HashSet<int>(state.SelectedVertices));
            ApplySelection(
                state,
                distance == null ? [] : [vertex],
                isModification,
                removeSelection);
        }

        private static Vector3[] GetWorldPositions(
            VertexSelectionState state)
        {
            return state.RenderObject is Rmv2MeshNode mesh
                ? MeshPoseSnapshot.Capture(mesh).GetWorldPositions()
                : state.RenderObject.Geometry.GetVertexList()
                    .ConvertAll(vertex => Vector3.Transform(
                        vertex,
                        state.RenderObject.RenderMatrix))
                    .ToArray();
        }

        private void ApplySelection(
            VertexSelectionState state,
            IEnumerable<int> vertices,
            bool isModification,
            bool removeSelection)
        {
            if (!isModification && !removeSelection)
                state.SetSelection(vertices);
            else
                state.ModifySelection(vertices, removeSelection);

            _vertexRenderItem?.MarkDirty();
        }

        public override void Draw(GameTime gameTime)
        {
            if (_selectionManager.GetState() is not
                    VertexSelectionState selection ||
                selection.RenderObject is not Rmv2MeshNode mesh ||
                _vertexRenderItem == null)
            {
                return;
            }

            var pose = MeshPoseSnapshot.Capture(mesh);
            if (_wireframeRenderItem == null ||
                _wireframeMesh != mesh)
            {
                _wireframeRenderItem?.Dispose();
                _wireframeMesh = mesh;
                _wireframeRenderItem =
                    new AnimatedWireframeRenderItem(
                        pose,
                        _resourceLibrary,
                        new Vector4(0.15f, 0.15f, 0.15f, 1));
            }
            else
            {
                _wireframeRenderItem.UpdatePose(pose);
            }

            _renderEngine.AddRenderItem(
                RenderBuckedId.Wireframe,
                _wireframeRenderItem);
            _vertexRenderItem.Node = mesh;
            _vertexRenderItem.Pose = pose;
            _vertexRenderItem.WorldPositions = null;
            _vertexRenderItem.SelectedVertices = selection;
            _renderEngine.AddRenderItem(
                RenderBuckedId.Normal,
                _vertexRenderItem);
        }

        public void Dispose()
        {
            _wireframeRenderItem?.Dispose();
            _wireframeRenderItem = null;
            _wireframeMesh = null;
            _vertexRenderer?.Dispose();
            _vertexRenderer = null;
            _vertexRenderItem = null;
        }

        private static Rectangle CreateSelectionRectangle(
            Vector2 start,
            Vector2 stop)
        {
            return new Rectangle(
                (int)Math.Min(start.X, stop.X),
                (int)Math.Min(start.Y, stop.Y),
                Math.Abs((int)(stop.X - start.X)),
                Math.Abs((int)(stop.Y - start.Y)));
        }
    }
}
