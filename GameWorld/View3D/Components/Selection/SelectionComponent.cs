using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using System.Windows.Forms;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Edge;
using GameWorld.Core.Commands.Face;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Services;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using MouseButton = GameWorld.Core.Components.Input.MouseButton;

namespace GameWorld.Core.Components.Selection
{
    public class SelectionComponent : BaseComponent, IDisposable
    {
        //SpriteBatch _spriteBatch;
        Texture2D _textTexture;
        bool _isMouseDown = false;
        Vector2 _startDrag;
        Vector2 _currentMousePos;

        // Flag to skip next selection after modal transform confirmation
        private bool _skipNextSelection = false;

        private readonly IKeyboardComponent _keyboardComponent;
        private readonly IMouseComponent _mouseComponent;
        private readonly ArcBallCamera _camera;
        private readonly SelectionManager _selectionManager;
        private readonly IDeviceResolver _deviceResolverComponent;
        private readonly CommandFactory _commandFactory;
        private readonly SceneManager _sceneManger;
        private readonly RenderEngineComponent _resourceLibrary;
        private readonly GizmoComponent _gizmoComponent;

        public SelectionComponent(
            IMouseComponent mouseComponent, IKeyboardComponent keyboardComponent,
            ArcBallCamera camera, SelectionManager selectionManager,
            IDeviceResolver deviceResolverComponent, CommandFactory commandFactory,
            SceneManager sceneManager, RenderEngineComponent resourceLibrary,
            GizmoComponent gizmoComponent)
        {
            _mouseComponent = mouseComponent;
            _keyboardComponent = keyboardComponent;
            _camera = camera;
            _selectionManager = selectionManager;
            _deviceResolverComponent = deviceResolverComponent;
            _commandFactory = commandFactory;
            _sceneManger = sceneManager;
            _resourceLibrary = resourceLibrary;
            _gizmoComponent = gizmoComponent;
        }

        public override void Initialize()
        {
            UpdateOrder = (int)ComponentUpdateOrderEnum.SelectionComponent;
            DrawOrder = (int)ComponentDrawOrderEnum.SelectionComponent;

            //_spriteBatch = new SpriteBatch(_deviceResolverComponent.Device);
            _textTexture = new Texture2D(_deviceResolverComponent.Device, 1, 1);
            _textTexture.SetData(new Color[1 * 1] { Color.White });

            base.Initialize();
        }

        public override void Update(GameTime gameTime)
        {
            // Check if Gizmo just finished modal transform - skip this selection
            var gizmo = _gizmoComponent?.Gizmo;
            if (gizmo != null && gizmo.JustFinishedModalTransform)
            {
                gizmo.ClearJustFinishedFlag();
                _skipNextSelection = true;
            }

            // Keyboard shortcuts work regardless of mouse ownership
            HandleSelectionKeyboardShortcuts();
            ChangeSelectionMode();

            if (!_mouseComponent.IsMouseOwner(this))
                return;

            _currentMousePos = _mouseComponent.Position();

            if (_mouseComponent.IsMouseButtonPressed(MouseButton.Left))
            {
                _startDrag = _mouseComponent.Position();
                _isMouseDown = true;

                if (_mouseComponent.MouseOwner != this)
                    _mouseComponent.MouseOwner = this;
            }

            if (_mouseComponent.IsMouseButtonReleased(MouseButton.Left))
            {
                // Skip selection if this is immediately after modal transform confirmation
                if (_skipNextSelection)
                {
                    _skipNextSelection = false;
                    _isMouseDown = false;
                    return;
                }

                if (_isMouseDown)
                {
                    var selectionRectangle = CreateSelectionRectangle(_startDrag, _currentMousePos);
                    var isSelectionRect = IsSelectionRectangle(selectionRectangle);
                    if (isSelectionRect)
                        SelectFromRectangle(selectionRectangle, _keyboardComponent.IsKeyDown(Keys.LeftShift), _keyboardComponent.IsKeyDown(Keys.LeftControl));
                    else
                        SelectFromPoint(_currentMousePos, _keyboardComponent.IsKeyDown(Keys.LeftShift), _keyboardComponent.IsKeyDown(Keys.LeftControl));
                }
                else
                    SelectFromPoint(_currentMousePos, _keyboardComponent.IsKeyDown(Keys.LeftShift), _keyboardComponent.IsKeyDown(Keys.LeftControl));

                _isMouseDown = false;
            }

            if (!_isMouseDown)
            {
                if (_mouseComponent.MouseOwner == this)
                {
                    _mouseComponent.MouseOwner = null;
                    _mouseComponent.ClearStates();
                    return;
                }
            }
        }

        public void SelectFromIndex(int index)
        {
            var selectable = _sceneManger.GetByIndex(index);
            if (selectable == null)
                return;

            //_selectionManager.CreateSelectionSate(GeometrySelectionMode.Object, selectable, true);
            _commandFactory.Create<ObjectSelectionCommand>().Configure(x => x.Configure([selectable], false, true)).BuildAndExecute();
        }

        void SelectFromRectangle(Rectangle screenRect, bool isSelectionModification, bool removeSelection)
        {
            var unprojectedSelectionRect = _camera.UnprojectRectangle(screenRect);

            var currentState = _selectionManager.GetState();
            if (currentState.Mode == GeometrySelectionMode.Face && currentState is FaceSelectionState faceState)
            {
                if (IntersectionMath.IntersectFaces(unprojectedSelectionRect, faceState.RenderObject.Geometry, faceState.RenderObject.RenderMatrix, out var faces))
                    _commandFactory.Create<FaceSelectionCommand>().Configure(x => x.Configure(faces, isSelectionModification, removeSelection)).BuildAndExecute();
                else if (!isSelectionModification && !removeSelection && faceState.SelectedFaces.Count > 0)
                    _commandFactory.Create<FaceSelectionCommand>().Configure(x => x.Configure(new List<int>(), false, false)).BuildAndExecute();
                return;
            }
            else if (currentState.Mode == GeometrySelectionMode.Vertex && currentState is VertexSelectionState vertexState)
            {
                if (IntersectionMath.IntersectVertices(unprojectedSelectionRect, vertexState.RenderObject.Geometry, vertexState.RenderObject.RenderMatrix, out var vertices))
                    _commandFactory.Create<VertexSelectionCommand>().Configure(x => x.Configure(vertices, isSelectionModification, removeSelection)).BuildAndExecute();
                else if (!isSelectionModification && !removeSelection && vertexState.SelectedVertices.Count > 0)
                    _commandFactory.Create<VertexSelectionCommand>().Configure(x => x.Configure(new List<int>(), false, false)).BuildAndExecute();
                return;
            }
            else if (currentState.Mode == GeometrySelectionMode.Edge && currentState is EdgeSelectionState edgeState)
            {
                var edgeObject = edgeState.RenderObject as Rmv2MeshNode;
                if (edgeObject != null &&
                    IntersectionMath.IntersectEdges(
                        unprojectedSelectionRect,
                        edgeObject.Geometry,
                        edgeObject.RenderMatrix,
                        out var edges))
                {
                    _commandFactory.Create<EdgeSelectionCommand>()
                        .Configure(x => x.Configure(edges, isSelectionModification, removeSelection))
                        .BuildAndExecute();
                }
                else if (!isSelectionModification && !removeSelection && edgeState.SelectedEdges.Count > 0)
                {
                    _commandFactory.Create<EdgeSelectionCommand>()
                        .Configure(x => x.Configure([], false, false))
                        .BuildAndExecute();
                }
                return;
            }
            else if (currentState.Mode == GeometrySelectionMode.Bone && currentState is BoneSelectionState boneState)
            {
                if (boneState.RenderObject == null)
                {
                    MessageBox.Show(LocalizationManager.Instance.Get("Msg.NoObjectSelected"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (boneState.CurrentAnimation == null)
                {
                    MessageBox.Show(LocalizationManager.Instance.Get("Msg.NoAnimationPlaying"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var vertexObject = boneState.RenderObject as Rmv2MeshNode;
                if (IntersectionMath.IntersectBones(unprojectedSelectionRect, vertexObject, boneState.Skeleton, vertexObject.RenderMatrix, out var bones))
                {
                    foreach (var bone in bones)
                    {
                        Console.WriteLine($"bone id: {bone}");
                    }
                    _commandFactory.Create<BoneSelectionCommand>().Configure(x => x.Configure(bones, isSelectionModification, removeSelection)).BuildAndExecute();
                }
                return;
            }

            // Object mode only: pick objects
            var selectedObjects = _sceneManger.SelectObjects(unprojectedSelectionRect);
            if (selectedObjects.Count() == 0 && isSelectionModification == false)
            {
                if (currentState.SelectionCount() != 0)
                    _commandFactory.Create<ObjectSelectionCommand>().Configure(x => x.Configure(new List<ISelectable>(), false, false)).BuildAndExecute();
            }
            else if (selectedObjects != null)
            {
                _commandFactory.Create<ObjectSelectionCommand>().Configure(x => x.Configure(selectedObjects, isSelectionModification, removeSelection)).BuildAndExecute();
            }
        }

        void SelectFromPoint(Vector2 mousePosition, bool isSelectionModification, bool removeSelection)
        {
            var ray = _camera.CreateCameraRay(mousePosition);
            var currentState = _selectionManager.GetState();

            // Edit mode: handle mode-specific selection, never fall through to object picking
            if (currentState is FaceSelectionState faceState)
            {
                if (IntersectionMath.IntersectFace(ray, faceState.RenderObject.Geometry, faceState.RenderObject.RenderMatrix, out var selectedFace) != null)
                    _commandFactory.Create<FaceSelectionCommand>().Configure(x => x.Configure(selectedFace.Value, isSelectionModification, removeSelection)).BuildAndExecute();
                else if (!isSelectionModification && !removeSelection && faceState.SelectedFaces.Count > 0)
                    _commandFactory.Create<FaceSelectionCommand>().Configure(x => x.Configure(new List<int>(), false, false)).BuildAndExecute();
                return;
            }

            if (currentState is VertexSelectionState vertexState)
            {
                var viewProjection = _camera.ViewMatrix * _camera.ProjectionMatrix;
                var viewport = _deviceResolverComponent.Device.Viewport;
                if (IntersectionMath.IntersectVertex(mousePosition, vertexState.RenderObject.Geometry, vertexState.RenderObject.RenderMatrix,
                    viewProjection, viewport.Width, viewport.Height, out var selecteVert,
                    new HashSet<int>(vertexState.SelectedVertices)) != null)
                    _commandFactory.Create<VertexSelectionCommand>().Configure(x => x.Configure(new List<int>() { selecteVert }, isSelectionModification, removeSelection)).BuildAndExecute();
                else if (!isSelectionModification && !removeSelection && vertexState.SelectedVertices.Count > 0)
                    _commandFactory.Create<VertexSelectionCommand>().Configure(x => x.Configure(new List<int>(), false, false)).BuildAndExecute();
                return;
            }

            if (currentState is EdgeSelectionState edgeState)
            {
                var edgeObject = edgeState.RenderObject as Rmv2MeshNode;
                var viewProjection = _camera.ViewMatrix * _camera.ProjectionMatrix;
                var viewport = _deviceResolverComponent.Device.Viewport;
                if (edgeObject != null &&
                    IntersectionMath.IntersectEdge(
                        mousePosition,
                        edgeObject.Geometry,
                        edgeObject.RenderMatrix,
                        viewProjection,
                        viewport.Width,
                        viewport.Height,
                        out var selectedEdge,
                        edgeState.SelectedEdges) != null)
                {
                    _commandFactory.Create<EdgeSelectionCommand>()
                        .Configure(x => x.Configure([selectedEdge], isSelectionModification, removeSelection))
                        .BuildAndExecute();
                }
                else if (!isSelectionModification && !removeSelection && edgeState.SelectedEdges.Count > 0)
                {
                    _commandFactory.Create<EdgeSelectionCommand>()
                        .Configure(x => x.Configure([], false, false))
                        .BuildAndExecute();
                }
                return;
            }

            // Object mode only: pick objects
            var selectedObject = _sceneManger.SelectObject(ray);
            if (selectedObject == null && isSelectionModification == false)
            {
                // Object mode: clear selection if not empty
                if (currentState.SelectionCount() != 0)
                    _commandFactory.Create<ObjectSelectionCommand>().Configure(x => x.Configure(new List<ISelectable>(), false, false)).BuildAndExecute();
            }
            else if (selectedObject != null)
            {
                _commandFactory.Create<ObjectSelectionCommand>().Configure(x => x.Configure(selectedObject, isSelectionModification, removeSelection)).BuildAndExecute();
            }
        }

        public bool SetObjectSelectionMode()
        {
            var selectionState = _selectionManager.GetState();
            if (_selectionManager.GetState().Mode != GeometrySelectionMode.Object)
            {
                _commandFactory.Create<ObjectSelectionModeCommand>().Configure(x => x.Configure(selectionState.GetSingleSelectedObject(), GeometrySelectionMode.Object)).BuildAndExecute();
                return true;
            }
            return false;
        }

        public bool SetFaceSelectionMode()
        {
            var selectionState = _selectionManager.GetState();
            if (_selectionManager.GetState().Mode != GeometrySelectionMode.Face)
            {
                var selectedObject = selectionState.GetSingleSelectedObject();
                if (selectedObject != null)
                {
                    _commandFactory.Create<ObjectSelectionModeCommand>().Configure(x => x.Configure(selectedObject, GeometrySelectionMode.Face)).BuildAndExecute();
                    return true;
                }

            }
            return false;
        }

        public bool SetVertexSelectionMode()
        {
            var selectionState = _selectionManager.GetState();
            if (_selectionManager.GetState().Mode != GeometrySelectionMode.Vertex)
            {
                var selectedObject = selectionState.GetSingleSelectedObject();
                if (selectedObject != null)
                {
                    _commandFactory.Create<ObjectSelectionModeCommand>().Configure(x => x.Configure(selectedObject, GeometrySelectionMode.Vertex)).BuildAndExecute();
                    return true;
                }
            }
            return false;
        }

        public bool SetEdgeSelectionMode()
        {
            var selectionState = _selectionManager.GetState();
            if (_selectionManager.GetState().Mode != GeometrySelectionMode.Edge)
            {
                var selectedObject = selectionState.GetSingleSelectedObject();
                if (selectedObject != null)
                {
                    _commandFactory.Create<ObjectSelectionModeCommand>().Configure(x => x.Configure(selectedObject, GeometrySelectionMode.Edge)).BuildAndExecute();
                    return true;
                }
            }
            return false;
        }

        public bool SetBoneSelectionMode()
        {
            var selectionState = _selectionManager.GetState();
            if (_selectionManager.GetState().Mode != GeometrySelectionMode.Bone)
            {
                var selectedObject = selectionState.GetSingleSelectedObject();
                if (selectedObject != null)
                {
                    _commandFactory.Create<ObjectSelectionModeCommand>().Configure(x => x.Configure(selectedObject, GeometrySelectionMode.Bone)).BuildAndExecute();
                    return true;
                }
            }
            return false;
        }


        bool ChangeSelectionMode()
        {
            // F1/F2/F3 removed - use Tab to toggle Object/Edit mode, 1/2/3 for sub-modes (handled by GizmoComponent)
            if (_keyboardComponent.IsKeyReleased(Keys.F9))
            {
                if (SetBoneSelectionMode())
                    return true;
            }

            return false;
        }

        bool HandleSelectionKeyboardShortcuts()
        {
            var currentState = _selectionManager.GetState();

            // A key = Select All / Deselect All (Blender behavior: toggle)
            if (_keyboardComponent.IsKeyReleased(Keys.A) && !_keyboardComponent.IsKeyDown(Keys.LeftControl))
            {
                if (currentState.Mode == GeometrySelectionMode.Object)
                {
                    var objState = currentState as ObjectSelectionState;
                    if (objState != null && objState.SelectionCount() > 0)
                    {
                        // Deselect All
                        _commandFactory.Create<ObjectSelectionCommand>()
                            .Configure(x => x.Configure(new List<ISelectable>(), false, false))
                            .BuildAndExecute();
                    }
                    else
                    {
                        // Select All
                        var allSelectables = SceneNodeHelper.GetChildrenOfType<ISelectable>(_sceneManger.RootNode);
                        _commandFactory.Create<ObjectSelectionCommand>()
                            .Configure(x => x.Configure(allSelectables, false, false))
                            .BuildAndExecute();
                    }
                    return true;
                }
            }

            // Ctrl+I = Invert Selection
            if (_keyboardComponent.IsKeyDown(Keys.LeftControl) && _keyboardComponent.IsKeyReleased(Keys.I))
            {
                if (currentState.Mode == GeometrySelectionMode.Object && currentState is ObjectSelectionState objState)
                {
                    var allSelectables = SceneNodeHelper.GetChildrenOfType<ISelectable>(_sceneManger.RootNode);
                    var currentlySelected = objState.CurrentSelection();
                    var inverted = allSelectables.Where(x => !currentlySelected.Contains(x)).ToList();
                    _commandFactory.Create<ObjectSelectionCommand>()
                        .Configure(x => x.Configure(inverted, false, false))
                        .BuildAndExecute();
                    return true;
                }
            }

            // Ctrl+L = Select Linked (Blender behavior: select all connected geometry)
            if (_keyboardComponent.IsKeyDown(Keys.LeftControl) && _keyboardComponent.IsKeyReleased(Keys.L))
            {
                return SelectLinked(currentState);
            }

            return false;
        }

        /// <summary>
        /// Ctrl+L Select Linked - BFS to find all connected geometry (Blender behavior)
        /// For faces: propagate through shared edges
        /// For vertices: propagate through shared edges (adjacent vertices)
        /// For edges: propagate through shared vertices
        /// </summary>
        bool SelectLinked(ISelectionState state)
        {
            if (state is FaceSelectionState faceState && faceState.RenderObject != null && faceState.SelectedFaces.Count > 0)
            {
                var geometry = faceState.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                // Build face adjacency from shared edges
                var edgeToFaces = new Dictionary<(int, int), List<int>>();
                for (int i = 0; i < indexBuffer.Count; i += 3)
                {
                    var verts = new[] { indexBuffer[i], indexBuffer[i + 1], indexBuffer[i + 2] };
                    for (int j = 0; j < 3; j++)
                    {
                        var edge = (Math.Min(verts[j], verts[(j + 1) % 3]), Math.Max(verts[j], verts[(j + 1) % 3]));
                        if (!edgeToFaces.ContainsKey(edge))
                            edgeToFaces[edge] = new List<int>();
                        edgeToFaces[edge].Add(i);
                    }
                }

                // BFS from selected faces
                var visited = new HashSet<int>(faceState.SelectedFaces);
                var queue = new Queue<int>(faceState.SelectedFaces);
                while (queue.Count > 0)
                {
                    var face = queue.Dequeue();
                    var v0 = indexBuffer[face];
                    var v1 = indexBuffer[face + 1];
                    var v2 = indexBuffer[face + 2];
                    var edges = new[] {
                        (Math.Min(v0, v1), Math.Max(v0, v1)),
                        (Math.Min(v1, v2), Math.Max(v1, v2)),
                        (Math.Min(v0, v2), Math.Max(v0, v2))
                    };
                    foreach (var edge in edges)
                    {
                        if (edgeToFaces.TryGetValue(edge, out var adjacentFaces))
                        {
                            foreach (var adjFace in adjacentFaces)
                            {
                                if (visited.Add(adjFace))
                                    queue.Enqueue(adjFace);
                            }
                        }
                    }
                }

                _commandFactory.Create<FaceSelectionCommand>()
                    .Configure(x => x.Configure(new List<int>(visited), false, false))
                    .BuildAndExecute();
                return true;
            }
            else if (state is VertexSelectionState vertState && vertState.RenderObject != null && vertState.SelectedVertices.Count > 0)
            {
                var geometry = vertState.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                // Build vertex adjacency from edges
                var vertexAdj = new Dictionary<int, HashSet<int>>();
                for (int i = 0; i < indexBuffer.Count; i += 3)
                {
                    var v0 = indexBuffer[i];
                    var v1 = indexBuffer[i + 1];
                    var v2 = indexBuffer[i + 2];
                    if (!vertexAdj.ContainsKey(v0)) vertexAdj[v0] = new HashSet<int>();
                    if (!vertexAdj.ContainsKey(v1)) vertexAdj[v1] = new HashSet<int>();
                    if (!vertexAdj.ContainsKey(v2)) vertexAdj[v2] = new HashSet<int>();
                    vertexAdj[v0].Add(v1); vertexAdj[v0].Add(v2);
                    vertexAdj[v1].Add(v0); vertexAdj[v1].Add(v2);
                    vertexAdj[v2].Add(v0); vertexAdj[v2].Add(v1);
                }

                // BFS from selected vertices
                var visited = new HashSet<int>(vertState.SelectedVertices);
                var queue = new Queue<int>(vertState.SelectedVertices);
                while (queue.Count > 0)
                {
                    var v = queue.Dequeue();
                    if (vertexAdj.TryGetValue(v, out var adjacent))
                    {
                        foreach (var adj in adjacent)
                        {
                            if (visited.Add(adj))
                                queue.Enqueue(adj);
                        }
                    }
                }

                _commandFactory.Create<VertexSelectionCommand>()
                    .Configure(x => x.Configure(new List<int>(visited), false, false))
                    .BuildAndExecute();
                return true;
            }
            else if (state is EdgeSelectionState edgeState && edgeState.RenderObject != null && edgeState.SelectedEdges.Count > 0)
            {
                var geometry = edgeState.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                // Build edge adjacency from shared vertices
                var edgeAdj = new Dictionary<(int, int), HashSet<(int, int)>>();
                var allEdges = new HashSet<(int, int)>();
                for (int i = 0; i < indexBuffer.Count; i += 3)
                {
                    var v0 = indexBuffer[i];
                    var v1 = indexBuffer[i + 1];
                    var v2 = indexBuffer[i + 2];
                    var triEdges = new[] {
                        (Math.Min(v0, v1), Math.Max(v0, v1)),
                        (Math.Min(v1, v2), Math.Max(v1, v2)),
                        (Math.Min(v0, v2), Math.Max(v0, v2))
                    };
                    foreach (var e in triEdges)
                    {
                        allEdges.Add(e);
                        if (!edgeAdj.ContainsKey(e)) edgeAdj[e] = new HashSet<(int, int)>();
                    }
                    // Edges sharing a vertex are adjacent
                    for (int j = 0; j < 3; j++)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            if (j != k) edgeAdj[triEdges[j]].Add(triEdges[k]);
                        }
                    }
                }

                // BFS from selected edges
                var visited = new HashSet<(int, int)>(edgeState.SelectedEdges);
                var queue = new Queue<(int, int)>(edgeState.SelectedEdges);
                while (queue.Count > 0)
                {
                    var e = queue.Dequeue();
                    if (edgeAdj.TryGetValue(e, out var adjacent))
                    {
                        foreach (var adj in adjacent)
                        {
                            if (visited.Add(adj))
                                queue.Enqueue(adj);
                        }
                    }
                }

                _commandFactory.Create<EdgeSelectionCommand>()
                    .Configure(x => x.Configure(new List<(int, int)>(visited), false, false))
                    .BuildAndExecute();
                return true;
            }

            return false;
        }

        public override void Draw(GameTime gameTime)
        {
            if (_isMouseDown)
            {
                var destination = CreateSelectionRectangle(_startDrag, _currentMousePos);
                var lineWidth = 2;
                var top = new Rectangle(destination.X, destination.Y, destination.Width, lineWidth);
                var bottom = new Rectangle(destination.X, destination.Y + destination.Height, destination.Width + 2, lineWidth);
                var left = new Rectangle(destination.X, destination.Y, lineWidth, destination.Height);
                var right = new Rectangle(destination.X + destination.Width, destination.Y, lineWidth, destination.Height);

                _resourceLibrary.CommonSpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                _resourceLibrary.CommonSpriteBatch.Draw(_textTexture, destination, Color.White * 0.5f);
                _resourceLibrary.CommonSpriteBatch.Draw(_textTexture, top, Color.Red * 0.75f);
                _resourceLibrary.CommonSpriteBatch.Draw(_textTexture, bottom, Color.Red * 0.75f);
                _resourceLibrary.CommonSpriteBatch.Draw(_textTexture, left, Color.Red * 0.75f);
                _resourceLibrary.CommonSpriteBatch.Draw(_textTexture, right, Color.Red * 0.75f);
                _resourceLibrary.CommonSpriteBatch.End();
            }
        }

        Rectangle CreateSelectionRectangle(Vector2 start, Vector2 stop)
        {
            var width = Math.Abs((int)(_currentMousePos.X - _startDrag.X));
            var height = Math.Abs((int)(_currentMousePos.Y - _startDrag.Y));

            var x = (int)Math.Min(start.X, stop.X);
            var y = (int)Math.Min(start.Y, stop.Y);

            return new Rectangle(x, y, width, height);
        }

        bool IsSelectionRectangle(Rectangle rect)
        {
            var area = rect.Width * rect.Height;
            if (area < 10)
                return false;

            return true;
        }

        public void Dispose()
        {
            _textTexture.Dispose();
        }
    }
}

