using System;
using System.Collections.Generic;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Events;

namespace GameWorld.Core.Components.Selection
{
    public class SelectionChangedEvent
    {
        public ISelectionState NewState { get; internal set; }
    }

    public class SelectionManager : BaseComponent, IDisposable
    {
        ISelectionState _currentState;
        private readonly IEventHub _eventHub;
        private readonly RenderEngineComponent _renderEngine;
        BasicShader _wireframeEffect;
        BasicShader _selectedFacesEffect;
        BasicShader _outlineEffect;

        VertexInstanceMesh _vertexRenderer;
        EdgeQuadInstanceMesh _edgeQuadRenderer;
        EdgeQuadRenderItem _edgeQuadRenderItem;
        VertexRenderItem _vertexRenderItem;
        float _vertexSelectionFalloff = 0;
        private readonly IScopedResourceLibrary _resourceLib;
        private readonly IDeviceResolver _deviceResolverComponent;

        // Cached edge topology for current mesh (avoids per-frame recomputation)
        private (int v0, int v1)[] _cachedEdgeIndices = Array.Empty<(int, int)>();
        private Rmv2MeshNode _cachedEdgeMesh;
        private MeshObject _cachedEdgeGeometry;
        private ushort[] _cachedEdgeIndexArray;
        private int _cachedEdgeIndexLength = -1;
        private Matrix _cachedEdgeRenderMatrix;
        private bool _edgeDataDirty = true;

        // Sample vertex positions to detect vertex transformation without full iteration
        private Vector3 _samplePos0, _samplePos1;
        private int _sampleIdx0 = 0;
        private int _sampleIdx1 = 1;

        const int MaxRenderEdges = 50000;
        private EdgeData[] _edgeDataCache = Array.Empty<EdgeData>();

        public SelectionManager(IEventHub eventHub, RenderEngineComponent renderEngine, IScopedResourceLibrary resourceLib, IDeviceResolver deviceResolverComponent)
        {
            _eventHub = eventHub;
            _renderEngine = renderEngine;
            _resourceLib = resourceLib;
            _deviceResolverComponent = deviceResolverComponent;
        }

        public override void Initialize()
        {
            CreateSelectionSate(GeometrySelectionMode.Object, null, false);

            _vertexRenderer = new VertexInstanceMesh(_deviceResolverComponent, _resourceLib);
            _edgeQuadRenderer = new EdgeQuadInstanceMesh(_deviceResolverComponent, _resourceLib);
            _edgeQuadRenderItem = new EdgeQuadRenderItem { EdgeQuadRenderer = _edgeQuadRenderer };
            _vertexRenderItem = new VertexRenderItem { VertexRenderer = _vertexRenderer };

            _wireframeEffect = new BasicShader(_deviceResolverComponent.Device);
            _wireframeEffect.DiffuseColour = new Vector3(0.0f, 0.0f, 0.0f); // Pure black wireframe (Blender style)

            _selectedFacesEffect = new BasicShader(_deviceResolverComponent.Device);
            _selectedFacesEffect.DiffuseColour = new Vector3(1, 0, 0);
            _selectedFacesEffect.SpecularColour = new Vector3(1, 0, 0);
            _selectedFacesEffect.EnableDefaultLighting();

            _outlineEffect = new BasicShader(_deviceResolverComponent.Device);
            _outlineEffect.DiffuseColour = new Vector3(1.0f, 1.0f, 1.0f); // White for selection mask

            base.Initialize();
        }


        public ISelectionState CreateSelectionSate(GeometrySelectionMode mode, ISelectable selectedObj, bool sendEvent = true)
        {
            if (_currentState != null)
            {
                _currentState.Clear();
                _currentState.SelectionChanged -= SelectionManager_SelectionChanged;
            }

            switch (mode)
            {
                case GeometrySelectionMode.Object:
                    _currentState = new ObjectSelectionState();
                    break;

                case GeometrySelectionMode.Face:
                    _currentState = new FaceSelectionState();
                    break;

                case GeometrySelectionMode.Edge:
                    _currentState = new EdgeSelectionState();
                    break;

                case GeometrySelectionMode.Vertex:
                    _currentState = new VertexSelectionState(selectedObj, _vertexSelectionFalloff);
                    break;
                case GeometrySelectionMode.Bone:
                    _currentState = new BoneSelectionState(selectedObj);
                    break;

                default:
                    throw new Exception();
            }

            _currentState.SelectionChanged += SelectionManager_SelectionChanged;
            SelectionManager_SelectionChanged(_currentState, sendEvent);
            return _currentState;
        }

        public ISelectionState GetState() => _currentState;
        public State GetState<State>() where State : class, ISelectionState => _currentState as State;
        public ISelectionState GetStateCopy() => _currentState.Clone();
        public State GetStateCopy<State>() where State : class, ISelectionState => GetState<State>().Clone() as State;

        public void SetState(ISelectionState state)
        {
            if (state == null)
                return;

            if (_currentState != null)
                _currentState.SelectionChanged -= SelectionManager_SelectionChanged;

            _currentState = state;
            _currentState.SelectionChanged += SelectionManager_SelectionChanged;
            SelectionManager_SelectionChanged(_currentState, true);
        }

        private void SelectionManager_SelectionChanged(ISelectionState state, bool sendEvent)
        {
            _edgeDataDirty = true;
            _eventHub.Publish(new SelectionChangedEvent { NewState = state });
        }

        public override void Draw(GameTime gameTime)
        {
            var selectionState = GetState();

            if (selectionState is ObjectSelectionState objectSelectionState)
            {
                foreach (var item in objectSelectionState.CurrentSelection())
                {
                    if (item is Rmv2MeshNode mesh)
                    {
                        // Render selected mesh to outline mask (white, screen-space outline post-process handles the rest)
                        _renderEngine.AddRenderItem(RenderBuckedId.Outline, new GeometryRenderItem(mesh.Geometry, _outlineEffect, mesh.RenderMatrix));
                    }
                }
            }

            if (selectionState is FaceSelectionState selectionFaceState && selectionFaceState.RenderObject is Rmv2MeshNode meshNode)
            {
                _renderEngine.AddRenderItem(RenderBuckedId.Selection, new PartialGeometryRenderItem(meshNode.Geometry, meshNode.RenderMatrix, _selectedFacesEffect, selectionFaceState.SelectedFaces));
                _renderEngine.AddRenderItem(RenderBuckedId.Wireframe, new GeometryRenderItem(meshNode.Geometry, _wireframeEffect, meshNode.RenderMatrix));
            }

            if (selectionState is VertexSelectionState selectionVertexState && selectionVertexState.RenderObject != null)
            {
                var vertexObject = selectionVertexState.RenderObject as Rmv2MeshNode;
                var geo = vertexObject.Geometry;

                var topologyChanged =
                    _cachedEdgeMesh != vertexObject ||
                    _cachedEdgeGeometry != geo ||
                    !ReferenceEquals(_cachedEdgeIndexArray, geo.IndexArray) ||
                    _cachedEdgeIndexLength != geo.IndexArray.Length;

                // Index mutation paths replace the array. Rebuild when that identity changes.
                if (topologyChanged)
                {
                    _cachedEdgeMesh = vertexObject;
                    _cachedEdgeGeometry = geo;
                    _cachedEdgeIndexArray = geo.IndexArray;
                    _cachedEdgeIndexLength = geo.IndexArray.Length;
                    _cachedEdgeIndices = EdgeIndexCacheBuilder.Build(geo.IndexArray, MaxRenderEdges);
                    _edgeDataCache = new EdgeData[_cachedEdgeIndices.Length];

                    var topologyVertexCount = geo.VertexCount();
                    selectionVertexState.SelectedVertices.RemoveAll(
                        index => index < 0 || index >= topologyVertexCount);
                    if (selectionVertexState.VertexWeights.Count != topologyVertexCount)
                        selectionVertexState.VertexWeights = new List<float>(new float[topologyVertexCount]);
                    selectionVertexState.UpdateWeights(_vertexSelectionFalloff);

                    _edgeDataDirty = true;
                }

                var vertexCount = geo.VertexCount();
                var firstSelectedVertex = -1;
                var secondSelectedVertex = -1;
                for (var i = 0; i < selectionVertexState.SelectedVertices.Count; i++)
                {
                    var index = selectionVertexState.SelectedVertices[i];
                    if (index < 0 || index >= vertexCount)
                        continue;

                    if (firstSelectedVertex == -1)
                        firstSelectedVertex = index;
                    else
                    {
                        secondSelectedVertex = index;
                        break;
                    }
                }

                // Use selected vertices as sample targets (fixes edge freeze during transform).
                if (secondSelectedVertex != -1)
                {
                    _sampleIdx0 = firstSelectedVertex;
                    _sampleIdx1 = secondSelectedVertex;
                }
                else if (firstSelectedVertex != -1)
                {
                    _sampleIdx0 = firstSelectedVertex;
                    _sampleIdx1 = _sampleIdx0 < vertexCount - 1 ? _sampleIdx0 + 1 : 0;
                }
                else
                {
                    _sampleIdx0 = 0;
                    _sampleIdx1 = vertexCount > 1 ? 1 : 0;
                }

                // Detect vertex position changes during transformation by sampling selected vertices
                if (!_edgeDataDirty && vertexCount > 0)
                {
                    var p0 = geo.GetVertexById(_sampleIdx0);
                    var p1 = geo.GetVertexById(_sampleIdx1);
                    if (p0 != _samplePos0 || p1 != _samplePos1)
                        _edgeDataDirty = true;
                }

                if (!_edgeDataDirty && _cachedEdgeRenderMatrix != vertexObject.RenderMatrix)
                    _edgeDataDirty = true;

                // Only rebuild edge data when dirty (selection change or position change)
                if (_edgeDataDirty)
                {
                    UpdateEdgeQuadData(vertexObject, selectionVertexState);
                    _edgeDataDirty = false;

                    // Cache sample positions for next frame comparison
                    if (vertexCount > 0)
                    {
                        _samplePos0 = geo.GetVertexById(_sampleIdx0);
                        _samplePos1 = geo.GetVertexById(_sampleIdx1);
                    }
                }

                // Submit cached render items (reuse single VertexRenderItem instance)
                _renderEngine.AddRenderItem(RenderBuckedId.Normal, _edgeQuadRenderItem);
                _vertexRenderItem.Node = vertexObject;
                _vertexRenderItem.ModelMatrix = vertexObject.RenderMatrix;
                _vertexRenderItem.SelectedVertices = selectionVertexState;
                _renderEngine.AddRenderItem(RenderBuckedId.Normal, _vertexRenderItem);
            }
            else
            {
                ResetEdgeCache();
            }

            if (selectionState is EdgeSelectionState selectionEdgeState && selectionEdgeState.RenderObject is Rmv2MeshNode edgeNode)
            {
                _renderEngine.AddRenderItem(RenderBuckedId.Wireframe, new GeometryRenderItem(edgeNode.Geometry, _wireframeEffect, edgeNode.RenderMatrix));
                // Render selected edges as highlighted line segments
                var geometry = edgeNode.Geometry;
                var matrix = edgeNode.RenderMatrix;
                foreach (var edge in selectionEdgeState.SelectedEdges)
                {
                    var p0 = Vector3.Transform(geometry.GetVertexById(edge.v0), matrix);
                    var p1 = Vector3.Transform(geometry.GetVertexById(edge.v1), matrix);
                    _renderEngine.AddRenderLines(new VertexPositionColor[]
                    {
                        new VertexPositionColor(p0, Color.Orange),
                        new VertexPositionColor(p1, Color.Orange)
                    });
                }
            }

            if (selectionState is BoneSelectionState selectionBoneState && selectionBoneState.RenderObject != null)
            {
                var sceneNode = selectionBoneState.RenderObject as Rmv2MeshNode;
                var animPlayer = sceneNode.AnimationPlayer;
                var currentFrame = animPlayer.GetCurrentAnimationFrame();
                var skeleton = selectionBoneState.Skeleton;

                if (currentFrame != null && skeleton != null)
                {
                    var bones = selectionBoneState.CurrentSelection();
                    var renderMatrix = sceneNode.RenderMatrix;
                    var parentWorld = Matrix.Identity;
                    foreach (var boneIdx in bones)
                    {
                        //var currentBoneMatrix = boneMatrix * Matrix.CreateScale(ScaleMult);
                        //var parentBoneMatrix = Skeleton.GetAnimatedWorldTranform(parentIndex) * Matrix.CreateScale(ScaleMult);
                        //_lineRenderer.AddLine(Vector3.Transform(currentBoneMatrix.Translation, parentWorld), Vector3.Transform(parentBoneMatrix.Translation, parentWorld));
                        var bone = currentFrame.GetSkeletonAnimatedWorld(skeleton, boneIdx);
                        bone.Decompose(out var _, out var _, out var trans);
                        _renderEngine.AddRenderLines(LineHelper.CreateCube(Matrix.CreateScale(0.06f) * bone * renderMatrix * parentWorld, Color.Red));
                    }
                }
            }

            base.Draw(gameTime);
        }

        public void Dispose()
        {
            _eventHub?.UnRegister(this);

            if(_currentState != null)
                _currentState.SelectionChanged -= SelectionManager_SelectionChanged;

            if (_wireframeEffect != null)
            {
                _wireframeEffect.Dispose();
                _wireframeEffect = null;
            }

            if (_selectedFacesEffect != null)
            {
                _selectedFacesEffect.Dispose();
                _selectedFacesEffect = null;
            }

            if (_outlineEffect != null)
            {
                _outlineEffect.Dispose();
                _outlineEffect = null;
            }

            if (_vertexRenderer != null)
            {
                _vertexRenderer.Dispose();
                _vertexRenderer = null;
            }

            if (_edgeQuadRenderer != null)
            {
                _edgeQuadRenderer.Dispose();
                _edgeQuadRenderer = null;
            }

            _currentState?.Clear();
            _currentState = null;
        }

        public void UpdateVertexSelectionFallof(float newValue)
        {
            _vertexSelectionFalloff = Math.Clamp(newValue, 0, float.MaxValue);
            var vertexSelectionState = GetState<VertexSelectionState>();
            if (vertexSelectionState != null)
            {
                vertexSelectionState.UpdateWeights(_vertexSelectionFalloff);
                _edgeDataDirty = true;
            }
        }

        public float VertexSelectionFalloff => _vertexSelectionFalloff;

        /// <summary>
        /// Update edge quad instance data (positions + colors).
        /// Only called when dirty (mesh changed or selection changed).
        /// </summary>
        private void UpdateEdgeQuadData(Rmv2MeshNode meshNode, VertexSelectionState selectionState)
        {
            var geo = meshNode.Geometry;
            var matrix = meshNode.RenderMatrix;
            EdgeOverlayDataBuilder.Fill(
                _edgeDataCache,
                geo,
                matrix,
                _cachedEdgeIndices,
                selectionState.VertexWeights);
            _cachedEdgeRenderMatrix = matrix;

            _edgeQuadRenderItem.Edges = _edgeDataCache;
            _edgeQuadRenderItem.MarkDirty();
        }

        private void ResetEdgeCache()
        {
            if (_cachedEdgeMesh == null &&
                _cachedEdgeIndices.Length == 0 &&
                _edgeDataCache.Length == 0)
            {
                _edgeDataDirty = true;
                return;
            }

            _cachedEdgeMesh = null;
            _cachedEdgeGeometry = null;
            _cachedEdgeIndexArray = null;
            _cachedEdgeIndexLength = -1;
            _cachedEdgeIndices = Array.Empty<(int, int)>();
            _edgeDataCache = Array.Empty<EdgeData>();
            _edgeDataDirty = true;

            if (_edgeQuadRenderItem != null)
            {
                _edgeQuadRenderItem.Edges = _edgeDataCache;
                _edgeQuadRenderItem.MarkDirty();
            }
        }
    }
}

