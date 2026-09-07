using System;
using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Services
{
    public class FocusSelectableObjectService
    {
        private readonly ILogger _logger = Logging.Create<FocusSelectableObjectService>();
        private readonly SelectionManager _selectionManager;
        private readonly ArcBallCamera _arcBallCamera;
        private readonly SceneManager _sceneManager;

        public FocusSelectableObjectService(SelectionManager selectionManager, ArcBallCamera arcBallCamera, SceneManager sceneManager)
        {
            _selectionManager = selectionManager;
            _arcBallCamera = arcBallCamera;
            _sceneManager = sceneManager;
        }

        public void LookAt(Vector3 position) => _arcBallCamera.LookAt = position;

        public void FocusSelection() => Focus(_selectionManager.GetState());

        public void FocusScene()
        {
            var mainNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            if (mainNode == null)
                return;

            var nodes = mainNode.GetMeshNodes(0)
                .Select(x => x as ISelectable)
                .Where(x => x != null)
                .ToList();

            FocusObjects(nodes);
        }

        public void FocusObjects(List<ISelectable> items)
        {
            FramePositions(items.SelectMany(item => GetWorldPositions(item,
                Enumerable.Range(0, item.Geometry.VertexCount()))));
        }

        IEnumerable<Vector3> GetWorldPositions(ISelectable item, IEnumerable<int> indices)
        {
            var pose = item is Rmv2MeshNode mesh ? MeshPoseSnapshot.Capture(mesh) : null;
            var world = pose?.WorldTransform ?? _sceneManager.GetWorldPosition(item);
            foreach (var index in indices)
            {
                if (index >= 0 && index < item.Geometry.VertexCount())
                    yield return pose?.GetWorldPosition(index) ?? Vector3.Transform(item.Geometry.GetVertexById(index), world);
            }
        }

        void FramePositions(IEnumerable<Vector3> positions)
        {
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            var hasPoint = false;
            foreach (var point in positions)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                    continue;
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                hasPoint = true;
            }
            if (hasPoint)
                _arcBallCamera.FrameBounds(new BoundingBox(min, max));
        }

        void Focus(ISelectionState selectionState)
        {
            _logger.Here().Information("Focusing on selection");

            if (selectionState is ObjectSelectionState objectState)
            {
                FocusObjects(objectState.SelectedObjects());
            }
            else if (selectionState is VertexSelectionState vertexSelection)
            {
                FramePositions(GetWorldPositions(vertexSelection.RenderObject, vertexSelection.SelectedVertices));
            }
            else if (selectionState is FaceSelectionState faceSelection)
            {
                var indices = faceSelection.SelectedFaces.SelectMany(face => new[]
                {
                    faceSelection.RenderObject.Geometry.GetIndex(face),
                    faceSelection.RenderObject.Geometry.GetIndex(face + 1),
                    faceSelection.RenderObject.Geometry.GetIndex(face + 2)
                });
                FramePositions(GetWorldPositions(faceSelection.RenderObject, indices));
            }
            else if (selectionState is EdgeSelectionState edgeSelection)
            {
                FramePositions(GetWorldPositions(edgeSelection.RenderObject, edgeSelection.GetSelectedVertexIndices()));
            }
            else if (selectionState is BoneSelectionState boneSelection)
            {
                var world = _sceneManager.GetWorldPosition(boneSelection.RenderObject);
                var objectPos = world.Translation;
                if (boneSelection.SelectedBones.Count == 0)
                    return;

                var currentFrame = AnimationSampler.Sample(
                    boneSelection.CurrentFrame,
                    0,
                    boneSelection.Skeleton,
                    boneSelection.CurrentAnimation,
                    freezeFrame: true);
                if (currentFrame == null)
                {
                    _arcBallCamera.LookAt = objectPos;
                    return;
                }

                var positions = new List<Vector3>();
                foreach (var boneIndex in boneSelection.SelectedBones)
                {
                    if (boneIndex < 0 ||
                        boneIndex >= currentFrame.BoneTransforms.Count)
                    {
                        continue;
                    }

                    var bonePosition = currentFrame
                        .GetSkeletonAnimatedWorld(
                            boneSelection.Skeleton,
                            boneIndex)
                        .Translation;
                    positions.Add(Vector3.Transform(bonePosition, world));
                }

                FramePositions(positions);
            }
        }


        public void ResetCamera()
        {
            _arcBallCamera.LookAt = Vector3.Zero;
            _arcBallCamera.Zoom = 10;
            _arcBallCamera.OrthoSize = _arcBallCamera.PerspectiveViewHeight;
        }
    }
}
