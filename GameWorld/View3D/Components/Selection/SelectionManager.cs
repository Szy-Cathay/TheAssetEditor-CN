using System;
using System.Collections.Generic;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
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
        public event Action<ISelectionState>? StateChanged;
        private readonly IEventHub _eventHub;
        private readonly RenderEngineComponent _renderEngine;
        VertexInstanceMesh _vertexRenderer;
        EdgeQuadInstanceMesh _edgeQuadRenderer;
        EdgeQuadRenderItem _edgeQuadRenderItem;
        VertexRenderItem _vertexRenderItem;
        float _vertexSelectionFalloff = 0;
        private readonly IScopedResourceLibrary _resourceLib;
        private readonly IDeviceResolver _deviceResolverComponent;
        private readonly MeshPoseRenderCache _poseRenderCache = new();
        private readonly HashSet<Rmv2MeshNode>
            _outlinedMeshes = [];
        private Rmv2MeshNode? _wireframeMesh;
        private AnimatedWireframeRenderItem?
            _wireframeRenderItem;
        private Rmv2MeshNode? _selectedEdgeWireframeMesh;
        private AnimatedWireframeRenderItem?
            _selectedEdgeWireframeRenderItem;
        private AnimatedSelectionRenderItem?
            _faceSelectionRenderItem;

        // Cached edge topology for current mesh (avoids per-frame recomputation)
        private (int v0, int v1)[] _cachedEdgeIndices = Array.Empty<(int, int)>();
        private Rmv2MeshNode _cachedEdgeMesh;
        private MeshObject _cachedEdgeGeometry;
        private ushort[] _cachedEdgeIndexArray;
        private int _cachedEdgeIndexLength = -1;
        private int _cachedEdgeTopologyVersion = -1;
        private Matrix _cachedEdgeRenderMatrix;
        private bool _edgeDataDirty = true;
        private Vector3[] _cachedVertexWorldPositions =
            Array.Empty<Vector3>();
        private AnimationPlayer? _cachedVertexAnimationPlayer;
        private long _cachedVertexAnimationTimeUs =
            long.MinValue;

        // Sample vertex positions to detect vertex transformation without full iteration
        private Vector3 _samplePos0, _samplePos1;
        private int _sampleIdx0 = 0;
        private int _sampleIdx1 = 1;

        const int MaxRenderEdges = 50000;
        private EdgeData[] _edgeDataCache = Array.Empty<EdgeData>();
        private (int v0, int v1)[] _selectedEdgeIndicesCache = Array.Empty<(int, int)>();
        private EdgeData[] _selectedEdgeDataCache = Array.Empty<EdgeData>();
        private Matrix _cachedSelectedEdgeRenderMatrix;
        private Vector3 _selectedEdgeSamplePos0;
        private Vector3 _selectedEdgeSamplePos1;
        private bool _selectedEdgeDataDirty = true;
        private AnimationPlayer?
            _cachedSelectedEdgeAnimationPlayer;
        private long _cachedSelectedEdgeAnimationTimeUs =
            long.MinValue;

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
            StateChanged?.Invoke(_currentState);
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
            _currentState.SelectionChanged -= SelectionManager_SelectionChanged;
            _currentState.SelectionChanged += SelectionManager_SelectionChanged;
            SelectionManager_SelectionChanged(_currentState, true);
            StateChanged?.Invoke(_currentState);
        }

        private void SelectionManager_SelectionChanged(ISelectionState state, bool sendEvent)
        {
            _edgeDataDirty = true;
            _selectedEdgeDataDirty = true;
            _poseRenderCache.Clear();
            ClearObjectOutlines();
            _vertexRenderItem?.MarkDirty();
            _eventHub.Publish(new SelectionChangedEvent { NewState = state });
        }

        public override void Draw(GameTime gameTime)
        {
            var selectionState = GetState();

            if (selectionState is ObjectSelectionState objectSelectionState)
            {
                var outlineRequested = false;
                foreach (var item in objectSelectionState.CurrentSelection())
                {
                    if (item is Rmv2MeshNode mesh)
                    {
                        mesh.SetSelectionOutline(true);
                        _outlinedMeshes.Add(mesh);
                        outlineRequested = true;
                    }
                }

                if (outlineRequested)
                    _renderEngine.RequestSelectionOutline();
            }

            if (selectionState is FaceSelectionState selectionFaceState && selectionFaceState.RenderObject is Rmv2MeshNode meshNode)
            {
                var pose =
                    _poseRenderCache.Capture(meshNode);
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Selection,
                    GetFaceSelectionRenderItem(
                        pose,
                        selectionFaceState.SelectedFaces));
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Wireframe,
                    GetWireframeRenderItem(meshNode, pose));
            }

            if (selectionState is VertexSelectionState selectionVertexState &&
                selectionVertexState.RenderObject is
                    Rmv2MeshNode vertexObject)
            {
                var pose =
                    _poseRenderCache.Capture(vertexObject);
                if (!ShouldRenderDenseEditOverlay(
                        vertexObject,
                        pose))
                {
                    _edgeDataDirty = true;
                    _renderEngine.AddRenderItem(
                        RenderBuckedId.Wireframe,
                        GetWireframeRenderItem(
                            vertexObject,
                            pose));
                    _vertexRenderItem.Node =
                        vertexObject;
                    _vertexRenderItem.Pose = pose;
                    _vertexRenderItem.WorldPositions = null;
                    _vertexRenderItem.SelectedVertices =
                        selectionVertexState;
                    _renderEngine.AddRenderItem(
                        RenderBuckedId.Normal,
                        _vertexRenderItem);
                }
                else
                {
                    var geo = vertexObject.Geometry;
                    var topologyChanged =
                        _cachedEdgeMesh != vertexObject ||
                        _cachedEdgeGeometry != geo ||
                        !ReferenceEquals(
                            _cachedEdgeIndexArray,
                            geo.IndexArray) ||
                        _cachedEdgeIndexLength !=
                            geo.IndexArray.Length ||
                        _cachedEdgeTopologyVersion !=
                            geo.TopologyVersion;

                    // Index mutation paths replace the array. Rebuild when that identity changes.
                    if (topologyChanged)
                    {
                        _cachedEdgeMesh = vertexObject;
                        _cachedEdgeGeometry = geo;
                        _cachedEdgeIndexArray =
                            geo.IndexArray;
                        _cachedEdgeIndexLength =
                            geo.IndexArray.Length;
                        _cachedEdgeTopologyVersion =
                            geo.TopologyVersion;
                        _cachedEdgeIndices =
                            EdgeIndexCacheBuilder.Build(
                                geo.IndexArray,
                                MaxRenderEdges);
                        _edgeDataCache =
                            new EdgeData[
                                _cachedEdgeIndices.Length];

                        var topologyVertexCount =
                            geo.VertexCount();
                        selectionVertexState
                            .SelectedVertices
                            .RemoveAll(
                                index =>
                                    index < 0 ||
                                    index >=
                                        topologyVertexCount);
                        if (selectionVertexState
                                .VertexWeights.Count !=
                            topologyVertexCount)
                        {
                            selectionVertexState
                                .VertexWeights =
                                new List<float>(
                                    new float[
                                        topologyVertexCount]);
                        }
                        selectionVertexState.UpdateWeights(
                            _vertexSelectionFalloff);

                        _edgeDataDirty = true;
                    }

                    var vertexCount = geo.VertexCount();
                    var firstSelectedVertex = -1;
                    var secondSelectedVertex = -1;
                    for (var i = 0;
                         i < selectionVertexState
                             .SelectedVertices.Count;
                         i++)
                    {
                        var index = selectionVertexState
                            .SelectedVertices[i];
                        if (index < 0 ||
                            index >= vertexCount)
                        {
                            continue;
                        }

                        if (firstSelectedVertex == -1)
                        {
                            firstSelectedVertex = index;
                        }
                        else
                        {
                            secondSelectedVertex = index;
                            break;
                        }
                    }

                    // Use selected vertices as sample targets (fixes edge freeze during transform).
                    if (secondSelectedVertex != -1)
                    {
                        _sampleIdx0 =
                            firstSelectedVertex;
                        _sampleIdx1 =
                            secondSelectedVertex;
                    }
                    else if (firstSelectedVertex != -1)
                    {
                        _sampleIdx0 =
                            firstSelectedVertex;
                        _sampleIdx1 =
                            _sampleIdx0 < vertexCount - 1
                                ? _sampleIdx0 + 1
                                : 0;
                    }
                    else
                    {
                        _sampleIdx0 = 0;
                        _sampleIdx1 =
                            vertexCount > 1 ? 1 : 0;
                    }

                    // Detect vertex position changes during transformation by sampling selected vertices.
                    if (!_edgeDataDirty &&
                        vertexCount > 0)
                    {
                        var p0 = geo.GetVertexById(
                            _sampleIdx0);
                        var p1 = geo.GetVertexById(
                            _sampleIdx1);
                        if (p0 != _samplePos0 ||
                            p1 != _samplePos1)
                        {
                            _edgeDataDirty = true;
                        }
                    }

                    if (!_edgeDataDirty &&
                        _cachedEdgeRenderMatrix !=
                            pose.WorldTransform)
                    {
                        _edgeDataDirty = true;
                    }

                    var animationTimeUs =
                        GetAnimationTimeUs(
                            vertexObject,
                            pose);
                    if (!_edgeDataDirty &&
                        (!ReferenceEquals(
                             _cachedVertexAnimationPlayer,
                             vertexObject
                                 .AnimationPlayer) ||
                         _cachedVertexAnimationTimeUs !=
                             animationTimeUs))
                    {
                        _edgeDataDirty = true;
                    }

                    // Only rebuild animated positions and edge data when the paused pose or geometry changes.
                    if (_edgeDataDirty)
                    {
                        if (_cachedVertexWorldPositions
                                .Length != vertexCount)
                        {
                            _cachedVertexWorldPositions =
                                new Vector3[vertexCount];
                        }
                        pose.FillWorldPositions(
                            _cachedVertexWorldPositions);
                        _vertexRenderItem.MarkDirty();
                        UpdateEdgeQuadData(
                            pose,
                            _cachedVertexWorldPositions,
                            selectionVertexState);
                        _edgeDataDirty = false;
                        _cachedVertexAnimationPlayer =
                            vertexObject.AnimationPlayer;
                        _cachedVertexAnimationTimeUs =
                            animationTimeUs;

                        // Cache sample positions for next frame comparison.
                        if (vertexCount > 0)
                        {
                            _samplePos0 =
                                geo.GetVertexById(
                                    _sampleIdx0);
                            _samplePos1 =
                                geo.GetVertexById(
                                    _sampleIdx1);
                        }
                    }

                    _renderEngine.AddRenderItem(
                        RenderBuckedId.Normal,
                        _edgeQuadRenderItem);
                    _vertexRenderItem.Node =
                        vertexObject;
                    _vertexRenderItem.ModelMatrix =
                        pose.WorldTransform;
                    _vertexRenderItem.Pose = null;
                    _vertexRenderItem.WorldPositions =
                        _cachedVertexWorldPositions;
                    _vertexRenderItem.SelectedVertices =
                        selectionVertexState;
                    _renderEngine.AddRenderItem(
                        RenderBuckedId.Normal,
                        _vertexRenderItem);
                }
            }
            else
            {
                ResetEdgeCache();
            }

            if (selectionState is EdgeSelectionState selectionEdgeState && selectionEdgeState.RenderObject is Rmv2MeshNode edgeNode)
            {
                var pose =
                    _poseRenderCache.Capture(edgeNode);
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Wireframe,
                    GetWireframeRenderItem(edgeNode, pose));

                if (!ShouldRenderDenseEditOverlay(
                        edgeNode,
                        pose))
                {
                    if (selectionEdgeState
                            .SelectedEdges.Count > 0)
                    {
                        _renderEngine.AddRenderItem(
                            RenderBuckedId.Selection,
                            GetSelectedEdgeWireframeRenderItem(
                                edgeNode,
                                pose,
                                selectionEdgeState
                                    .SelectedEdges));
                    }
                }
                else
                {
                    if (!_selectedEdgeDataDirty &&
                        _selectedEdgeIndicesCache.Length >
                            0)
                    {
                        var sampleEdge =
                            _selectedEdgeIndicesCache[0];
                        if (_cachedSelectedEdgeRenderMatrix !=
                                pose.WorldTransform ||
                            _selectedEdgeSamplePos0 !=
                                edgeNode.Geometry
                                    .GetVertexById(
                                        sampleEdge.v0) ||
                            _selectedEdgeSamplePos1 !=
                                edgeNode.Geometry
                                    .GetVertexById(
                                        sampleEdge.v1))
                        {
                            _selectedEdgeDataDirty = true;
                        }
                    }

                    var animationTimeUs =
                        GetAnimationTimeUs(
                            edgeNode,
                            pose);
                    if (!_selectedEdgeDataDirty &&
                        (!ReferenceEquals(
                             _cachedSelectedEdgeAnimationPlayer,
                             edgeNode.AnimationPlayer) ||
                         _cachedSelectedEdgeAnimationTimeUs !=
                             animationTimeUs))
                    {
                        _selectedEdgeDataDirty = true;
                    }

                    if (_selectedEdgeDataDirty)
                    {
                        var worldPositions =
                            selectionEdgeState
                                .SelectedEdges.Count == 0
                                ? Array.Empty<Vector3>()
                                : pose.GetWorldPositions();
                        UpdateSelectedEdgeQuadData(
                            pose,
                            worldPositions,
                            selectionEdgeState);
                        _cachedSelectedEdgeAnimationPlayer =
                            edgeNode.AnimationPlayer;
                        _cachedSelectedEdgeAnimationTimeUs =
                            animationTimeUs;
                    }

                    if (_selectedEdgeDataCache.Length >
                        0)
                    {
                        _renderEngine.AddRenderItem(
                            RenderBuckedId.Selection,
                            _edgeQuadRenderItem);
                    }
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

            ClearObjectOutlines();
            _wireframeRenderItem?.Dispose();
            _wireframeRenderItem = null;
            _wireframeMesh = null;
            _selectedEdgeWireframeRenderItem?.Dispose();
            _selectedEdgeWireframeRenderItem = null;
            _selectedEdgeWireframeMesh = null;
            _poseRenderCache.Clear();
            _currentState?.Clear();
            _currentState = null;
        }

        public void UpdateVertexSelectionFallof(float newValue)
        {
            var clampedValue = Math.Clamp(newValue, 0, float.MaxValue);
            if (_vertexSelectionFalloff == clampedValue)
                return;

            _vertexSelectionFalloff = clampedValue;
            var vertexSelectionState = GetState<VertexSelectionState>();
            if (vertexSelectionState != null)
            {
                vertexSelectionState.UpdateWeights(_vertexSelectionFalloff);
                _edgeDataDirty = true;
            }
        }

        public float VertexSelectionFalloff => _vertexSelectionFalloff;

        private AnimatedWireframeRenderItem
            GetWireframeRenderItem(
                Rmv2MeshNode meshNode,
                MeshPoseSnapshot pose)
        {
            if (_wireframeRenderItem == null ||
                _wireframeMesh != meshNode)
            {
                _wireframeRenderItem?.Dispose();
                _wireframeMesh = meshNode;
                _wireframeRenderItem =
                    new AnimatedWireframeRenderItem(
                        pose,
                        _resourceLib,
                        new Vector4(0, 0, 0, 1));
            }
            else
            {
                _wireframeRenderItem.UpdatePose(pose);
            }

            return _wireframeRenderItem;
        }

        private AnimatedWireframeRenderItem
            GetSelectedEdgeWireframeRenderItem(
                Rmv2MeshNode meshNode,
                MeshPoseSnapshot pose,
                IEnumerable<(int v0, int v1)> selectedEdges)
        {
            if (_selectedEdgeWireframeRenderItem == null ||
                _selectedEdgeWireframeMesh != meshNode)
            {
                _selectedEdgeWireframeRenderItem?.Dispose();
                _selectedEdgeWireframeMesh = meshNode;
                _selectedEdgeWireframeRenderItem =
                    new AnimatedWireframeRenderItem(
                        pose,
                        _resourceLib,
                        new Vector4(1, 0.47f, 0, 1),
                        0)
                    {
                        DepthBias = 0.00004f
                    };
                _selectedEdgeDataDirty = true;
            }
            else
            {
                _selectedEdgeWireframeRenderItem.UpdatePose(
                    pose);
            }

            if (_selectedEdgeDataDirty)
            {
                _selectedEdgeWireframeRenderItem.UpdateEdges(
                    selectedEdges);
                _selectedEdgeDataDirty = false;
            }

            return _selectedEdgeWireframeRenderItem;
        }

        private AnimatedSelectionRenderItem
            GetFaceSelectionRenderItem(
                MeshPoseSnapshot pose,
                IReadOnlyList<int> selectedFaces)
        {
            if (_faceSelectionRenderItem == null)
            {
                _faceSelectionRenderItem =
                    new AnimatedSelectionRenderItem(
                        pose,
                        _resourceLib,
                        new Vector4(1, 0, 0, 1),
                        selectedFaces);
            }
            else
            {
                _faceSelectionRenderItem.UpdatePose(pose);
                _faceSelectionRenderItem
                    .UpdateSelectedFaces(selectedFaces);
            }

            return _faceSelectionRenderItem;
        }

        private void ClearObjectOutlines()
        {
            foreach (var mesh in _outlinedMeshes)
                mesh.SetSelectionOutline(false);
            _outlinedMeshes.Clear();
        }

        internal static bool ShouldRenderDenseEditOverlay(
            Rmv2MeshNode meshNode,
            MeshPoseSnapshot pose)
        {
            return !pose.ApplyAnimation;
        }

        private static long GetAnimationTimeUs(
            Rmv2MeshNode meshNode,
            MeshPoseSnapshot pose)
        {
            return pose.ApplyAnimation
                ? meshNode.AnimationPlayer
                    ?.GetTimeUs() ?? -1
                : -1;
        }

        /// <summary>
        /// Update edge quad instance data (positions + colors).
        /// Only called when dirty (mesh changed or selection changed).
        /// </summary>
        private void UpdateEdgeQuadData(
            MeshPoseSnapshot pose,
            IReadOnlyList<Vector3> worldPositions,
            VertexSelectionState selectionState)
        {
            EdgeOverlayDataBuilder.Fill(
                _edgeDataCache,
                worldPositions,
                _cachedEdgeIndices,
                selectionState.VertexWeights);
            _cachedEdgeRenderMatrix =
                pose.WorldTransform;

            _edgeQuadRenderItem.Edges = _edgeDataCache;
            _edgeQuadRenderItem.MarkDirty();
        }

        private void UpdateSelectedEdgeQuadData(
            MeshPoseSnapshot pose,
            IReadOnlyList<Vector3> worldPositions,
            EdgeSelectionState selectionState)
        {
            var selectedEdgeCount = selectionState.SelectedEdges.Count;
            if (_selectedEdgeIndicesCache.Length != selectedEdgeCount)
            {
                _selectedEdgeIndicesCache = new (int, int)[selectedEdgeCount];
                _selectedEdgeDataCache = new EdgeData[selectedEdgeCount];
            }

            selectionState.SelectedEdges.CopyTo(_selectedEdgeIndicesCache);
            EdgeOverlayDataBuilder.FillSelected(
                _selectedEdgeDataCache,
                worldPositions,
                _selectedEdgeIndicesCache);

            _cachedSelectedEdgeRenderMatrix =
                pose.WorldTransform;
            if (selectedEdgeCount > 0)
            {
                var sampleEdge = _selectedEdgeIndicesCache[0];
                _selectedEdgeSamplePos0 =
                    pose.Geometry.GetVertexById(sampleEdge.v0);
                _selectedEdgeSamplePos1 =
                    pose.Geometry.GetVertexById(sampleEdge.v1);
            }

            _edgeQuadRenderItem.Edges = _selectedEdgeDataCache;
            _edgeQuadRenderItem.MarkDirty();
            _selectedEdgeDataDirty = false;
        }

        private void ResetEdgeCache()
        {
            _cachedVertexWorldPositions =
                Array.Empty<Vector3>();
            _cachedVertexAnimationPlayer = null;
            _cachedVertexAnimationTimeUs =
                long.MinValue;

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
            _cachedEdgeTopologyVersion = -1;
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

