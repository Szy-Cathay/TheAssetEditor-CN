using System;
using System.Collections.Generic;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace Editors.KitbasherEditor.Components
{
    public sealed class KitbashSelectionOverlayComponent :
        BaseComponent,
        IDisposable
    {
        private static readonly Vector3 WireColour = new(
            0.15f,
            0.15f,
            0.15f);
        private static readonly Vector3 SelectedColour = new(
            1.0f,
            0.47f,
            0.0f);
        private static readonly Vector3 SelectedVertexColour = new(
            2.0f,
            0.65f,
            0.0f);
        private static readonly Vector3 ActiveColour = Vector3.One;

        private const float WireHalfWidth = 0.75f;
        private const float SelectedEdgeHalfWidth = 2.0f;
        private const float ActiveEdgeHalfWidth = 2.25f;
        private const float WireDepthBias = 0.00002f;
        private const float SelectedEdgeDepthBias = 0.00004f;
        private const float ActiveEdgeDepthBias = 0.00006f;
        private const float SelectedFaceOpacity = 0.24f;
        private const float ActiveFaceOpacity = 0.34f;
        private const float SelectedFaceDepthBias = 0.00001f;
        private const float ActiveFaceDepthBias = 0.00003f;
        private const int MaxRenderEdges = 50000;

        private readonly SelectionManager _selectionManager;
        private readonly RenderEngineComponent _renderEngine;
        private readonly IScopedResourceLibrary _resourceLibrary;
        private readonly IDeviceResolver _deviceResolver;
        private readonly HashSet<Rmv2MeshNode> _outlinedMeshes = [];

        private VertexInstanceMesh? _vertexRenderer;
        private EdgeQuadInstanceMesh? _edgeQuadRenderer;
        private EdgeQuadRenderItem? _edgeQuadRenderItem;
        private VertexRenderItem? _vertexRenderItem;
        private Rmv2MeshNode? _wireframeMesh;
        private AnimatedWireframeRenderItem? _wireframeRenderItem;
        private Rmv2MeshNode? _selectedEdgeWireframeMesh;
        private AnimatedWireframeRenderItem?
            _selectedEdgeWireframeRenderItem;
        private Rmv2MeshNode? _activeEdgeWireframeMesh;
        private AnimatedWireframeRenderItem?
            _activeEdgeWireframeRenderItem;
        private AnimatedSelectionRenderItem?
            _faceSelectionRenderItem;
        private AnimatedSelectionRenderItem?
            _activeFaceSelectionRenderItem;

        private (int v0, int v1)[] _cachedEdgeIndices = [];
        private Rmv2MeshNode? _cachedEdgeMesh;
        private MeshObject? _cachedEdgeGeometry;
        private ushort[]? _cachedEdgeIndexArray;
        private int _cachedEdgeIndexLength = -1;
        private int _cachedEdgeTopologyVersion = -1;
        private Matrix _cachedEdgeRenderMatrix;
        private Vector3[] _cachedVertexWorldPositions = [];
        private Vector3 _samplePosition0;
        private Vector3 _samplePosition1;
        private int _sampleIndex0;
        private int _sampleIndex1 = 1;
        private EdgeData[] _edgeDataCache = [];
        private bool _edgeDataDirty = true;
        private bool _selectedEdgeDataDirty = true;
        private bool _activeEdgeDataDirty = true;
        private bool _vertexWireframeSelectionDirty = true;

        public KitbashSelectionOverlayComponent(
            SelectionManager selectionManager,
            RenderEngineComponent renderEngine,
            IScopedResourceLibrary resourceLibrary,
            IDeviceResolver deviceResolver)
        {
            _selectionManager = selectionManager;
            _renderEngine = renderEngine;
            _resourceLibrary = resourceLibrary;
            _deviceResolver = deviceResolver;
            _selectionManager.SelectionChanged +=
                SelectionManager_SelectionChanged;
        }

        public override void Initialize()
        {
            _vertexRenderer = new VertexInstanceMesh(
                _deviceResolver,
                _resourceLibrary)
            {
                SelectedColour = SelectedVertexColour
            };
            _edgeQuadRenderer = new EdgeQuadInstanceMesh(
                _deviceResolver,
                _resourceLibrary);
            _edgeQuadRenderItem = new EdgeQuadRenderItem
            {
                EdgeQuadRenderer = _edgeQuadRenderer
            };
            _vertexRenderItem = new VertexRenderItem
            {
                VertexRenderer = _vertexRenderer
            };

            base.Initialize();
        }

        public override void Draw(GameTime gameTime)
        {
            var selectionState = _selectionManager.GetState();

            DrawSelectionOutline(selectionState);
            DrawFaceSelection(selectionState);
            DrawVertexSelection(selectionState);
            DrawEdgeSelection(selectionState);

            base.Draw(gameTime);
        }

        private void DrawSelectionOutline(
            ISelectionState selectionState)
        {
            ClearSelectionOutline();
            if (selectionState is ObjectSelectionState objectSelection)
            {
                foreach (var selected in objectSelection.CurrentSelection())
                {
                    if (selected is Rmv2MeshNode mesh)
                    {
                        mesh.SetSelectionOutline(true);
                        _outlinedMeshes.Add(mesh);
                    }
                }
            }
            else if (selectionState.Mode is
                         GeometrySelectionMode.Edge or
                         GeometrySelectionMode.Face &&
                     selectionState.GetSingleSelectedObject() is
                         Rmv2MeshNode editMesh)
            {
                editMesh.SetSelectionOutline(true);
                _outlinedMeshes.Add(editMesh);
            }

            if (_outlinedMeshes.Count > 0)
                _renderEngine.RequestSelectionOutline();
        }

        private void ClearSelectionOutline()
        {
            foreach (var mesh in _outlinedMeshes)
                mesh.SetSelectionOutline(false);
            _outlinedMeshes.Clear();
        }

        private void DrawFaceSelection(
            ISelectionState selectionState)
        {
            if (selectionState is not
                    FaceSelectionState faceSelection ||
                faceSelection.RenderObject is not
                    Rmv2MeshNode meshNode)
            {
                return;
            }

            var pose = MeshPoseSnapshot.Capture(meshNode);
            _renderEngine.AddRenderItem(
                RenderBuckedId.Selection,
                GetFaceSelectionRenderItem(
                    pose,
                    faceSelection.SelectedFaces));
            if (faceSelection.ActiveFace is { } activeFace &&
                faceSelection.SelectedFaces.Contains(activeFace))
            {
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Selection,
                    GetActiveFaceSelectionRenderItem(
                        pose,
                        activeFace));
            }

            _renderEngine.AddRenderItem(
                RenderBuckedId.Wireframe,
                GetWireframeRenderItem(meshNode, pose));
        }

        private void DrawVertexSelection(
            ISelectionState selectionState)
        {
            if (selectionState is not
                    VertexSelectionState vertexSelection ||
                vertexSelection.RenderObject is not
                    Rmv2MeshNode meshNode ||
                _vertexRenderItem == null ||
                _edgeQuadRenderItem == null)
            {
                ResetEdgeCache();
                return;
            }

            var pose = MeshPoseSnapshot.Capture(meshNode);
            if (pose.ApplyAnimation)
            {
                _edgeDataDirty = true;
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Wireframe,
                    GetWireframeRenderItem(
                        meshNode,
                        pose,
                        vertexSelection.SelectedVertices));
                _vertexRenderItem.Node = meshNode;
                _vertexRenderItem.Pose = pose;
                _vertexRenderItem.WorldPositions = null;
                _vertexRenderItem.SelectedVertices =
                    vertexSelection;
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Normal,
                    _vertexRenderItem);
                return;
            }

            PrepareDenseVertexOverlay(
                meshNode,
                pose,
                vertexSelection);
            _renderEngine.AddRenderItem(
                RenderBuckedId.Normal,
                _edgeQuadRenderItem);
            _vertexRenderItem.Node = meshNode;
            _vertexRenderItem.ModelMatrix = pose.WorldTransform;
            _vertexRenderItem.Pose = null;
            _vertexRenderItem.WorldPositions =
                _cachedVertexWorldPositions;
            _vertexRenderItem.SelectedVertices =
                vertexSelection;
            _renderEngine.AddRenderItem(
                RenderBuckedId.Normal,
                _vertexRenderItem);
        }

        private void PrepareDenseVertexOverlay(
            Rmv2MeshNode meshNode,
            MeshPoseSnapshot pose,
            VertexSelectionState vertexSelection)
        {
            var geometry = meshNode.Geometry;
            var topologyChanged =
                _cachedEdgeMesh != meshNode ||
                _cachedEdgeGeometry != geometry ||
                !ReferenceEquals(
                    _cachedEdgeIndexArray,
                    geometry.IndexArray) ||
                _cachedEdgeIndexLength !=
                    geometry.IndexArray.Length ||
                _cachedEdgeTopologyVersion !=
                    geometry.TopologyVersion;

            if (topologyChanged)
            {
                _cachedEdgeMesh = meshNode;
                _cachedEdgeGeometry = geometry;
                _cachedEdgeIndexArray = geometry.IndexArray;
                _cachedEdgeIndexLength =
                    geometry.IndexArray.Length;
                _cachedEdgeTopologyVersion =
                    geometry.TopologyVersion;
                _cachedEdgeIndices = BuildEdges(
                    geometry.IndexArray,
                    MaxRenderEdges);
                _edgeDataCache =
                    new EdgeData[_cachedEdgeIndices.Length];

                var vertexCount = geometry.VertexCount();
                vertexSelection.SelectedVertices.RemoveAll(
                    index => index < 0 || index >= vertexCount);
                if (vertexSelection.VertexWeights.Count !=
                    vertexCount)
                {
                    vertexSelection.VertexWeights =
                        new List<float>(new float[vertexCount]);
                }

                vertexSelection.UpdateWeights(
                    _selectionManager.VertexSelectionFalloff);
                _edgeDataDirty = true;
            }

            UpdateSampleIndices(
                vertexSelection,
                geometry.VertexCount());
            DetectDenseOverlayChanges(geometry, pose);

            if (!_edgeDataDirty)
                return;

            var currentVertexCount = geometry.VertexCount();
            if (_cachedVertexWorldPositions.Length !=
                currentVertexCount)
            {
                _cachedVertexWorldPositions =
                    new Vector3[currentVertexCount];
            }

            pose.FillWorldPositions(_cachedVertexWorldPositions);
            _vertexRenderItem?.MarkDirty();
            FillEdgeData(
                _edgeDataCache,
                _cachedVertexWorldPositions,
                _cachedEdgeIndices,
                vertexSelection.VertexWeights);
            _cachedEdgeRenderMatrix = pose.WorldTransform;
            _edgeQuadRenderItem!.Edges = _edgeDataCache;
            _edgeQuadRenderItem.MarkDirty();
            _edgeDataDirty = false;

            if (currentVertexCount > 0)
            {
                _samplePosition0 = geometry.GetVertexById(
                    _sampleIndex0);
                _samplePosition1 = geometry.GetVertexById(
                    _sampleIndex1);
            }
        }

        private void UpdateSampleIndices(
            VertexSelectionState vertexSelection,
            int vertexCount)
        {
            var firstSelectedVertex = -1;
            var secondSelectedVertex = -1;
            foreach (var index in
                     vertexSelection.SelectedVertices)
            {
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

            if (secondSelectedVertex != -1)
            {
                _sampleIndex0 = firstSelectedVertex;
                _sampleIndex1 = secondSelectedVertex;
            }
            else if (firstSelectedVertex != -1)
            {
                _sampleIndex0 = firstSelectedVertex;
                _sampleIndex1 = _sampleIndex0 < vertexCount - 1
                    ? _sampleIndex0 + 1
                    : 0;
            }
            else
            {
                _sampleIndex0 = 0;
                _sampleIndex1 = vertexCount > 1 ? 1 : 0;
            }
        }

        private void DetectDenseOverlayChanges(
            MeshObject geometry,
            MeshPoseSnapshot pose)
        {
            var vertexCount = geometry.VertexCount();
            if (!_edgeDataDirty && vertexCount > 0)
            {
                var position0 = geometry.GetVertexById(
                    _sampleIndex0);
                var position1 = geometry.GetVertexById(
                    _sampleIndex1);
                if (position0 != _samplePosition0 ||
                    position1 != _samplePosition1)
                {
                    _edgeDataDirty = true;
                }
            }

            if (!_edgeDataDirty &&
                _cachedEdgeRenderMatrix != pose.WorldTransform)
            {
                _edgeDataDirty = true;
            }
        }

        private void DrawEdgeSelection(
            ISelectionState selectionState)
        {
            if (selectionState is not
                    EdgeSelectionState edgeSelection ||
                edgeSelection.RenderObject is not
                    Rmv2MeshNode meshNode)
            {
                return;
            }

            var pose = MeshPoseSnapshot.Capture(meshNode);
            _renderEngine.AddRenderItem(
                RenderBuckedId.Wireframe,
                GetWireframeRenderItem(meshNode, pose));

            if (edgeSelection.SelectedEdges.Count > 0)
            {
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Selection,
                    GetSelectedEdgeWireframeRenderItem(
                        meshNode,
                        pose,
                        edgeSelection.SelectedEdges));
            }

            if (edgeSelection.ActiveEdge is { } activeEdge &&
                edgeSelection.SelectedEdges.Contains(activeEdge))
            {
                _renderEngine.AddRenderItem(
                    RenderBuckedId.Selection,
                    GetActiveEdgeWireframeRenderItem(
                        meshNode,
                        pose,
                        activeEdge));
            }
        }

        private AnimatedWireframeRenderItem
            GetWireframeRenderItem(
                Rmv2MeshNode meshNode,
                MeshPoseSnapshot pose,
                IReadOnlyCollection<int>? selectedVertices = null)
        {
            var created = false;
            if (_wireframeRenderItem == null ||
                _wireframeMesh != meshNode)
            {
                _wireframeRenderItem?.Dispose();
                _wireframeMesh = meshNode;
                _wireframeRenderItem =
                    new AnimatedWireframeRenderItem(
                        pose,
                        _resourceLibrary,
                        new Vector4(WireColour, 1))
                    {
                        DepthBias = WireDepthBias,
                        EdgeHalfWidth = WireHalfWidth
                    };
                created = true;
            }
            else
            {
                _wireframeRenderItem.UpdatePose(pose);
            }

            if (selectedVertices == null)
            {
                _wireframeRenderItem.ClearSelectedVertices();
            }
            else if (created ||
                     _vertexWireframeSelectionDirty)
            {
                _wireframeRenderItem.UpdateSelectedVertices(
                    selectedVertices,
                    SelectedColour);
                _vertexWireframeSelectionDirty = false;
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
                        _resourceLibrary,
                        new Vector4(SelectedColour, 1),
                        0)
                    {
                        DepthBias = SelectedEdgeDepthBias,
                        EdgeHalfWidth = SelectedEdgeHalfWidth
                    };
                _selectedEdgeDataDirty = true;
            }
            else
            {
                _selectedEdgeWireframeRenderItem.UpdatePose(pose);
            }

            if (_selectedEdgeDataDirty)
            {
                _selectedEdgeWireframeRenderItem.UpdateEdges(
                    selectedEdges);
                _selectedEdgeDataDirty = false;
            }

            return _selectedEdgeWireframeRenderItem;
        }

        private AnimatedWireframeRenderItem
            GetActiveEdgeWireframeRenderItem(
                Rmv2MeshNode meshNode,
                MeshPoseSnapshot pose,
                (int v0, int v1) activeEdge)
        {
            if (_activeEdgeWireframeRenderItem == null ||
                _activeEdgeWireframeMesh != meshNode)
            {
                _activeEdgeWireframeRenderItem?.Dispose();
                _activeEdgeWireframeMesh = meshNode;
                _activeEdgeWireframeRenderItem =
                    new AnimatedWireframeRenderItem(
                        pose,
                        _resourceLibrary,
                        new Vector4(ActiveColour, 1),
                        0)
                    {
                        DepthBias = ActiveEdgeDepthBias,
                        EdgeHalfWidth = ActiveEdgeHalfWidth
                    };
                _activeEdgeDataDirty = true;
            }
            else
            {
                _activeEdgeWireframeRenderItem.UpdatePose(pose);
            }

            if (_activeEdgeDataDirty)
            {
                _activeEdgeWireframeRenderItem.UpdateEdges(
                    [activeEdge]);
                _activeEdgeDataDirty = false;
            }

            return _activeEdgeWireframeRenderItem;
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
                        _resourceLibrary,
                        new Vector4(
                            SelectedColour,
                            SelectedFaceOpacity),
                        selectedFaces)
                    {
                        DepthBias = SelectedFaceDepthBias
                    };
            }
            else
            {
                _faceSelectionRenderItem.UpdatePose(pose);
                _faceSelectionRenderItem.UpdateSelectedFaces(
                    selectedFaces);
            }

            return _faceSelectionRenderItem;
        }

        private AnimatedSelectionRenderItem
            GetActiveFaceSelectionRenderItem(
                MeshPoseSnapshot pose,
                int activeFace)
        {
            if (_activeFaceSelectionRenderItem == null)
            {
                _activeFaceSelectionRenderItem =
                    new AnimatedSelectionRenderItem(
                        pose,
                        _resourceLibrary,
                        new Vector4(
                            ActiveColour,
                            ActiveFaceOpacity),
                        [activeFace])
                    {
                        DepthBias = ActiveFaceDepthBias
                    };
            }
            else
            {
                _activeFaceSelectionRenderItem.UpdatePose(pose);
                _activeFaceSelectionRenderItem.UpdateSelectedFaces(
                    [activeFace]);
            }

            return _activeFaceSelectionRenderItem;
        }

        private void SelectionManager_SelectionChanged(
            ISelectionState selectionState)
        {
            ClearSelectionOutline();
            _edgeDataDirty = true;
            _selectedEdgeDataDirty = true;
            _activeEdgeDataDirty = true;
            _vertexWireframeSelectionDirty = true;
            _vertexRenderItem?.MarkDirty();
        }

        private void ResetEdgeCache()
        {
            _cachedVertexWorldPositions = [];
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
            _cachedEdgeIndices = [];
            _edgeDataCache = [];
            _edgeDataDirty = true;

            if (_edgeQuadRenderItem != null)
            {
                _edgeQuadRenderItem.Edges = _edgeDataCache;
                _edgeQuadRenderItem.MarkDirty();
            }
        }

        private static (int v0, int v1)[] BuildEdges(
            ReadOnlySpan<ushort> indices,
            int maxEdges)
        {
            var processedEdges =
                new HashSet<(int v0, int v1)>();
            var edges = new List<(int v0, int v1)>(
                Math.Min(maxEdges, indices.Length));
            for (var index = 0;
                 index + 2 < indices.Length;
                 index += 3)
            {
                if (AddEdge(
                        indices[index],
                        indices[index + 1],
                        processedEdges,
                        edges,
                        maxEdges) ||
                    AddEdge(
                        indices[index + 1],
                        indices[index + 2],
                        processedEdges,
                        edges,
                        maxEdges) ||
                    AddEdge(
                        indices[index],
                        indices[index + 2],
                        processedEdges,
                        edges,
                        maxEdges))
                {
                    break;
                }
            }

            return edges.ToArray();
        }

        private static bool AddEdge(
            int first,
            int second,
            HashSet<(int v0, int v1)> processedEdges,
            List<(int v0, int v1)> edges,
            int maxEdges)
        {
            if (first == second)
                return false;

            var edge = first < second
                ? (first, second)
                : (second, first);
            if (processedEdges.Add(edge))
                edges.Add(edge);
            return edges.Count == maxEdges;
        }

        private static void FillEdgeData(
            Span<EdgeData> destination,
            IReadOnlyList<Vector3> worldPositions,
            IReadOnlyList<(int v0, int v1)> edges,
            IReadOnlyList<float> weights)
        {
            for (var index = 0;
                 index < edges.Count;
                 index++)
            {
                var (first, second) = edges[index];
                destination[index] = new EdgeData
                {
                    P0 = worldPositions[first],
                    P1 = worldPositions[second],
                    C0 = Vector3.Lerp(
                        WireColour,
                        SelectedColour,
                        weights[first]),
                    C1 = Vector3.Lerp(
                        WireColour,
                        SelectedColour,
                        weights[second]),
                    Width = 0
                };
            }
        }

        public void Dispose()
        {
            ClearSelectionOutline();
            _selectionManager.SelectionChanged -=
                SelectionManager_SelectionChanged;
            _vertexRenderer?.Dispose();
            _vertexRenderer = null;
            _edgeQuadRenderer?.Dispose();
            _edgeQuadRenderer = null;
            _wireframeRenderItem?.Dispose();
            _wireframeRenderItem = null;
            _selectedEdgeWireframeRenderItem?.Dispose();
            _selectedEdgeWireframeRenderItem = null;
            _activeEdgeWireframeRenderItem?.Dispose();
            _activeEdgeWireframeRenderItem = null;
        }
    }
}
