using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameWorld.Core.Components.Gizmo
{
    public class TransformGizmoWrapper : ITransformable
    {
        protected ILogger _logger = Logging.Create<TransformGizmoWrapper>();

        Vector3 _pos;
        public Vector3 Position { get => _pos; set { _pos = value; } }

        Vector3 _scale = Vector3.One;
        public Vector3 Scale { get => _scale; set { _scale = value; } }

        Quaternion _orientation = Quaternion.Identity;
        public Quaternion Orientation { get => _orientation; set { _orientation = value; } }

        ICommand _activeCommand;

        List<MeshObject> _effectedObjects;
        List<int> _selectedBones;
        private readonly CommandFactory _commandFactory;
        ISelectionState _selectionState;
        // Vertex indices from selected faces (used by FaceSelectionState transform)
        HashSet<int> _faceVertexIndices;
        // Falloff weights for face/edge mode proportional editing
        Dictionary<int, float> _falloffWeights;
        float _falloffDistance = 0f;

        Matrix _totalGizomTransform = Matrix.Identity;
        bool _invertedWindingOrder = false;
        VertexTransformReplayPlan? _vertexTransformReplayPlan;

        // -- Modal transform state backup (like Blender's TransData.iloc) -- //
        private List<VertexPositionNormalTextureCustom[]> _backupVertexArrays;
        private List<ushort[]> _backupIndexArrays;
        private Vector3 _backupPosition;                 // Backup initial position for rotation center
        private Quaternion _backupOrientation;           // Backup initial orientation
        private bool _hasBackup = false;

        // -- Partial VBO upload tracking -- //
        private int _modifiedMin = int.MaxValue;
        private int _modifiedMax = -1;
        private bool _hasModifications = false;

        public TransformGizmoWrapper(CommandFactory commandFactory, List<MeshObject> effectedObjects, ISelectionState vertexSelectionState)
        {
            _commandFactory = commandFactory;
            _selectionState = vertexSelectionState;

            if (_selectionState as ObjectSelectionState != null)
            {
                _effectedObjects = effectedObjects;

                foreach (var item in _effectedObjects)
                    Position += item.MeshCenter;

                Position = Position / _effectedObjects.Count;
            }
            if (_selectionState is VertexSelectionState vertSelectionState)
            {
                _effectedObjects = effectedObjects;

                for (var i = 0; i < vertSelectionState.SelectedVertices.Count; i++)
                    Position += _effectedObjects[0].GetVertexById(vertSelectionState.SelectedVertices[i]);

                Position = Position / vertSelectionState.SelectedVertices.Count;
            }
            if (_selectionState is FaceSelectionState faceSelectionState)
            {
                _effectedObjects = effectedObjects;

                // Extract vertex indices from selected faces
                var indexBuffer = _effectedObjects[0].GetIndexBuffer();
                _faceVertexIndices = new HashSet<int>();
                foreach (var face in faceSelectionState.SelectedFaces)
                {
                    _faceVertexIndices.Add(indexBuffer[face]);
                    _faceVertexIndices.Add(indexBuffer[face + 1]);
                    _faceVertexIndices.Add(indexBuffer[face + 2]);
                }

                // Compute center position from face vertices
                foreach (var vertIdx in _faceVertexIndices)
                    Position += _effectedObjects[0].GetVertexById(vertIdx);

                if (_faceVertexIndices.Count > 0)
                    Position = Position / _faceVertexIndices.Count;
            }
            if (_selectionState is EdgeSelectionState edgeSelectionState)
            {
                _effectedObjects = effectedObjects;

                // Extract vertex indices from selected edges
                _faceVertexIndices = edgeSelectionState.GetSelectedVertexIndices();

                // Compute center position from edge vertices
                foreach (var vertIdx in _faceVertexIndices)
                    Position += _effectedObjects[0].GetVertexById(vertIdx);

                if (_faceVertexIndices.Count > 0)
                    Position = Position / _faceVertexIndices.Count;
            }
        }

        public TransformGizmoWrapper(CommandFactory commandFactory, List<int> selectedBones, BoneSelectionState boneSelectionState)
        {
            _commandFactory = commandFactory;
            _selectionState = boneSelectionState;
            _selectedBones = selectedBones;

            _effectedObjects = new List<MeshObject> { boneSelectionState.RenderObject.Geometry };

            var sceneNode = boneSelectionState.RenderObject as Rmv2MeshNode;
            var animPlayer = sceneNode.AnimationPlayer;
            var currentFrame = animPlayer.GetCurrentAnimationFrame();
            var skeleton = boneSelectionState.Skeleton;

            if (currentFrame == null) return;

            var bones = boneSelectionState.SelectedBones;
            var totalBones = bones.Count;
            var rotations = new List<Quaternion>();
            foreach (var boneIdx in bones)
            {
                var bone = currentFrame.GetSkeletonAnimatedWorld(skeleton, boneIdx);
                bone.Decompose(out var scale, out var rot, out var trans);
                Position += trans;
                Scale += scale;
                rotations.Add(rot);

            }

            Orientation = AverageOrientation(rotations);
            Position = Position / totalBones;
            Scale = Scale / totalBones;
        }

        private Quaternion AverageOrientation(List<Quaternion> orientations)
        {
            var average = orientations[0];
            for (var i = 1; i < orientations.Count; i++)
            {
                average = Quaternion.Slerp(average, orientations[i], 1.0f / (i + 1));
            }
            return average;
        }

        public void Start(CommandExecutor commandManager)
        {

            if (_activeCommand is TransformVertexCommand transformVertexCommand)
            {
                //   MessageBox.Show("Transform debug check - Please inform the creator of the tool that you got this message. Would also love it if you tried undoing your last command to see if that works..\n E-001");
                ConfigureTransformCommandForCommit(transformVertexCommand);
                commandManager.ExecuteCommand(_activeCommand);
                _activeCommand = null;
                if (_hasBackup)
                    ClearBackup();
                _vertexTransformReplayPlan = null;
            }

            if (_activeCommand is TransformBoneCommand transformBoneCommand)
            {
                var matrix = _totalGizomTransform;
                matrix.Translation = Position;
                transformBoneCommand.Transform = matrix;
                commandManager.ExecuteCommand(_activeCommand);
                _activeCommand = null;
            }

            if (_selectionState is BoneSelectionState)
            {
                _totalGizomTransform = Matrix.Identity;
                _activeCommand = _commandFactory.Create<TransformBoneCommand>().Configure(x => x.Configure(_selectedBones, (BoneSelectionState)_selectionState)).Build();
            }
            else
            {
                _totalGizomTransform = Matrix.Identity;
                _activeCommand = _commandFactory.Create<TransformVertexCommand>().Configure(x => x.Configure(_effectedObjects, Position)).Build();
                // Pass affected vertex indices for Face/Edge mode undo
                if (_activeCommand is TransformVertexCommand tvc && _faceVertexIndices != null)
                    tvc.AffectedVertexIndices = new HashSet<int>(_faceVertexIndices);
                BackupVertexState();
            }

        }

        public void Stop(CommandExecutor commandManager)
        {
            if (_activeCommand is TransformVertexCommand transformVertexCommand)
            {
                ConfigureTransformCommandForCommit(transformVertexCommand);
                commandManager.ExecuteCommand(_activeCommand);
                _activeCommand = null;
                ClearBackup();
                _vertexTransformReplayPlan = null;
                return;
            }

            if (_activeCommand is TransformBoneCommand transformBoneCommand)
            {
                var matrix = _totalGizomTransform;
                matrix.Translation = Position;
                transformBoneCommand.Transform = matrix;
                commandManager.ExecuteCommand(_activeCommand);
                _activeCommand = null;
                return;
            }
        }

        /// <summary>
        /// Confirm modal transform - record the final transform for undo/redo
        /// This is different from the normal gizmo flow where Start/Stop are used
        /// </summary>
        public void ConfirmModalTransform(CommandExecutor commandManager)
        {
            // Create the command with current transform state
            if (_selectionState is BoneSelectionState)
            {
                var command = _commandFactory.Create<TransformBoneCommand>()
                    .Configure(x => x.Configure(_selectedBones, (BoneSelectionState)_selectionState))
                    .Build();
                var matrix = _totalGizomTransform;
                matrix.Translation = Position;
                command.Transform = matrix;
                commandManager.ExecuteCommand(command);
            }
            else
            {
                var command = _commandFactory.Create<TransformVertexCommand>()
                    .Configure(x => x.Configure(_effectedObjects, Position))
                    .Build();
                ConfigureTransformCommandForCommit(command);
                commandManager.ExecuteCommand(command);
            }

            // Reset state after confirming
            _totalGizomTransform = Matrix.Identity;
            _invertedWindingOrder = false;
            _vertexTransformReplayPlan = null;
        }

        void ConfigureTransformCommandForCommit(TransformVertexCommand command)
        {
            command.InvertWindingOrder = _invertedWindingOrder;
            command.Transform = _totalGizomTransform;
            command.PivotPoint = Position;
            command.SetReplayPlan(
                _vertexTransformReplayPlan ??
                VertexTransformOperationApplier.CreateEmptyReplayPlan(
                    _selectionState,
                    _falloffDistance > 0 ? _falloffWeights : null));
            if (_faceVertexIndices != null)
                command.AffectedVertexIndices = new HashSet<int>(_faceVertexIndices);
            if (_falloffWeights != null && _falloffDistance > 0)
                command.FalloffWeights = new Dictionary<int, float>(_falloffWeights);
        }

        Matrix FixRotationAxis2(Matrix transform)
        {
            // Decompose the transform matrix into its scale, rotation, and translation components
            transform.Decompose(out var scale, out var rotation, out var translation);

            // Create a quaternion representing a 180-degree rotation around the X axis
            var flipQuaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.Pi);

            // Apply the rotation to the quaternion to correct the axis alignment
            var correctedQuaternion = flipQuaternion * rotation;

            // Recompose the transform matrix with the corrected rotation
            var fixedTransform = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(correctedQuaternion) * Matrix.CreateTranslation(translation);

            return fixedTransform;
        }


        public void GizmoTranslateEvent(Vector3 translation, PivotType pivot)
        {
            if (!ApplyTransform(Matrix.CreateTranslation(translation), pivot, GizmoMode.Translate))
                return;

            Position += translation;
            _totalGizomTransform *= Matrix.CreateTranslation(translation);
        }

        public void GizmoRotateEvent(Matrix rotation, PivotType pivot)
        {
            if (!ApplyTransform(rotation, pivot, GizmoMode.Rotate))
                return;

            _totalGizomTransform *= rotation;

            var fixedTransform = FixRotationAxis2(_totalGizomTransform);
            fixedTransform.Decompose(out var _, out var quat, out var _);
            Orientation = quat;
        }

        public void GizmoScaleEvent(Vector3 scale, PivotType pivot)
        {
            var realScale = scale + Vector3.One;
            var scaleMatrix = Matrix.CreateScale(scale + Vector3.One);
            if (!ApplyTransform(scaleMatrix, pivot, GizmoMode.UniformScale))
                return;

            Scale += scale;

            _totalGizomTransform *= scaleMatrix;

            if (_selectionState is BoneSelectionState &&
                CountNegativeAxis(realScale) % 2 != 0)
            {
                _invertedWindingOrder = !_invertedWindingOrder;

                foreach (var geo in _effectedObjects)
                {
                    var indexes = geo.GetIndexBuffer();
                    for (var i = 0; i < indexes.Count; i += 3)
                    {
                        var temp = indexes[i + 2];
                        indexes[i + 2] = indexes[i + 0];
                        indexes[i + 0] = temp;
                    }
                    geo.SetIndexBuffer(indexes);
                }
            }
        }

        int CountNegativeAxis(Vector3 vector)
        {
            var result = 0;
            if (vector.X < 0) result++;
            if (vector.Y < 0) result++;
            if (vector.Z < 0) result++;
            return result;
        }

        bool ApplyTransform(Matrix transform, PivotType pivotType, GizmoMode gizmoMode)
        {
            if (_selectionState is BoneSelectionState)
            {
                if (!transform.Decompose(out _, out var rotation, out _))
                    return false;

                var objCenter = pivotType == PivotType.ObjectCenter ? Position : Vector3.Zero;
                TransformBone(
                    Matrix.CreateScale(Scale) *
                    Matrix.CreateFromQuaternion(rotation) *
                    Matrix.CreateTranslation(Position),
                    objCenter,
                    gizmoMode);
                return true;
            }

            var operationMode = gizmoMode switch
            {
                GizmoMode.Translate => VertexTransformOperationMode.Translate,
                GizmoMode.Rotate => VertexTransformOperationMode.Rotate,
                _ => VertexTransformOperationMode.Scale
            };
            var pivotPoint = pivotType == PivotType.ObjectCenter ? Position : Vector3.Zero;
            var operation = new VertexTransformOperation(operationMode, transform, pivotPoint);
            var falloffWeights = _falloffDistance > 0 ? _falloffWeights : null;
            if (_vertexTransformReplayPlan == null ||
                _backupVertexArrays == null ||
                !VertexTransformOperationApplier.TryAppendOperation(
                    _effectedObjects,
                    _selectionState,
                    _faceVertexIndices,
                    falloffWeights,
                    _vertexTransformReplayPlan,
                    operation,
                    out var candidatePlan) ||
                !VertexTransformOperationApplier.IsReplayPlanReversibleFromBaseline(
                    _effectedObjects,
                    _backupVertexArrays,
                    _selectionState,
                    _faceVertexIndices,
                    falloffWeights,
                    candidatePlan))
            {
                return false;
            }

            VertexTransformOperationApplier.RestoreAffectedVerticesFromBaseline(
                _effectedObjects,
                _backupVertexArrays,
                _selectionState,
                _faceVertexIndices,
                falloffWeights);
            var results = VertexTransformOperationApplier.ApplyReplayPlan(
                _effectedObjects,
                _selectionState,
                _faceVertexIndices,
                falloffWeights,
                candidatePlan,
                inverse: false);
            var candidateInvertedWindingOrder =
                candidatePlan.RawMatrices.Forward.Determinant() < 0;
            if (candidateInvertedWindingOrder != _invertedWindingOrder)
            {
                foreach (var geometry in _effectedObjects)
                    TransformVertexCommand.ReverseWindingOrder(geometry);
            }

            _invertedWindingOrder = candidateInvertedWindingOrder;
            _vertexTransformReplayPlan = candidatePlan;
            foreach (var result in results)
            {
                if (_selectionState is VertexSelectionState && result.HasModifiedVertices)
                {
                    _modifiedMin = Math.Min(_modifiedMin, result.FirstModifiedVertex);
                    _modifiedMax = Math.Max(_modifiedMax, result.LastModifiedVertex);
                    _hasModifications = true;
                }

                if (_hasModifications && _selectionState is not ObjectSelectionState)
                    result.Geometry.RebuildVertexBufferPartial(_modifiedMin, _modifiedMax);
                else
                    result.Geometry.RebuildVertexBuffer();
            }

            return true;
        }

        void TransformBone(Matrix transform, Vector3 objCenter, GizmoMode gizmoMode)
        {
            if (_activeCommand is TransformBoneCommand transformBoneCommand)
            {
                transformBoneCommand.ApplyTransformation(transform, gizmoMode);
            }
        }

        public Vector3 GetObjectCentre()
        {
            return Position;
        }

        #region Modal Transform State Backup (Blender-style)

        /// <summary>
        /// Backup current vertex state for modal transform (like Blender's createTransData)
        /// Call this when modal transform starts
        /// </summary>
        public void BackupVertexState()
        {
            _modifiedMin = int.MaxValue;
            _modifiedMax = -1;
            _hasModifications = false;

            if (_effectedObjects == null || _effectedObjects.Count == 0)
                return;

            _backupVertexArrays = new List<VertexPositionNormalTextureCustom[]>();
            _backupIndexArrays = new List<ushort[]>();

            foreach (var mesh in _effectedObjects)
            {
                // Single Array.Copy instead of 4 separate element-by-element lists
                var backup = new VertexPositionNormalTextureCustom[mesh.VertexCount()];
                Array.Copy(mesh.VertexArray, backup, mesh.VertexCount());
                _backupVertexArrays.Add(backup);

                // Backup index array as raw ushort[]
                var indexBackup = new ushort[mesh.IndexArray.Length];
                Array.Copy(mesh.IndexArray, indexBackup, mesh.IndexArray.Length);
                _backupIndexArrays.Add(indexBackup);

                // Defer BoundingBox rebuild during modal transform to avoid O(n) per-frame cost
                mesh.DeferBoundingBoxRebuild = true;
            }

            // Backup position and orientation for rotation center
            _backupPosition = _pos;
            _backupOrientation = _orientation;
            _vertexTransformReplayPlan =
                VertexTransformOperationApplier.CreateEmptyReplayPlan(
                    _selectionState,
                    _falloffDistance > 0 ? _falloffWeights : null);

            _hasBackup = true;
        }

        /// <summary>
        /// Restore vertex state from backup (like Blender's restoreTransObjects)
        /// Call this when modal transform is cancelled or when recalculating from initial state
        /// </summary>
        /// <param name="resetTransform">Whether to reset internal transform state (true for cancel, false for recalculating)</param>
        /// <param name="skipGpuUpload">If true, only restore VertexArray without uploading to GPU (used when next ApplyTransform will overwrite immediately)</param>
        public void RestoreVertexState(bool resetTransform = true, bool skipGpuUpload = false)
        {
            if (!_hasBackup || _effectedObjects == null)
                return;

            _modifiedMin = int.MaxValue;
            _modifiedMax = -1;
            _hasModifications = false;

            for (int meshIndex = 0; meshIndex < _effectedObjects.Count && meshIndex < _backupVertexArrays.Count; meshIndex++)
            {
                var mesh = _effectedObjects[meshIndex];
                var backup = _backupVertexArrays[meshIndex];

                // Single Array.Copy instead of element-by-element field assignment
                Array.Copy(backup, mesh.VertexArray, backup.Length);

                // Restore index array with Array.Copy
                Array.Copy(_backupIndexArrays[meshIndex], mesh.IndexArray, _backupIndexArrays[meshIndex].Length);

                if (!skipGpuUpload)
                {
                    mesh.RebuildIndexBuffer();
                    mesh.RebuildVertexBuffer();
                }
            }

            // Reset internal state only when explicitly requested (e.g., on cancel)
            if (resetTransform)
            {
                _totalGizomTransform = Matrix.Identity;
                _invertedWindingOrder = false;
                _vertexTransformReplayPlan =
                    VertexTransformOperationApplier.CreateEmptyReplayPlan(
                        _selectionState,
                        _falloffDistance > 0 ? _falloffWeights : null);
                // Restore position and orientation for correct rotation center
                _pos = _backupPosition;
                _orientation = _backupOrientation;
            }
        }

        /// <summary>
        /// Clear backup data (call when modal transform is confirmed or done)
        /// </summary>
        public void ClearBackup()
        {
            // Restore BoundingBox rebuild and rebuild once with final vertex positions
            if (_effectedObjects != null)
            {
                foreach (var mesh in _effectedObjects)
                {
                    mesh.DeferBoundingBoxRebuild = false;
                    mesh.BuildBoundingBox();
                }
            }

            _backupVertexArrays?.Clear();
            _backupIndexArrays?.Clear();
            _hasBackup = false;
        }

        /// <summary>
        /// Check if there's a valid backup
        /// </summary>
        public bool HasBackup => _hasBackup;

        /// <summary>
        /// Reset the total gizmo transform (call when starting fresh transform)
        /// </summary>
        public void ResetTotalTransform()
        {
            _totalGizomTransform = Matrix.Identity;
            _invertedWindingOrder = false;
            _vertexTransformReplayPlan = _hasBackup
                ? VertexTransformOperationApplier.CreateEmptyReplayPlan(
                    _selectionState,
                    _falloffDistance > 0 ? _falloffWeights : null)
                : null;
        }

        /// <summary>
        /// Set falloff distance for face/edge mode proportional editing
        /// </summary>
        public void SetFalloffDistance(float distance)
        {
            _falloffDistance = distance;
            ComputeFalloffWeights();
        }

        /// <summary>
        /// Compute falloff weights for all vertices based on distance from selected face/edge vertices.
        /// Weight = 1.0 for directly selected vertices, linearly falloff to 0 at _falloffDistance.
        /// </summary>
        void ComputeFalloffWeights()
        {
            if (_faceVertexIndices == null || _faceVertexIndices.Count == 0 || _falloffDistance <= 0 || _effectedObjects == null || _effectedObjects.Count == 0)
                return;

            _falloffWeights = new Dictionary<int, float>();
            var geo = _effectedObjects[0];
            var vertexArray = geo.VertexArray;
            var vertCount = geo.VertexCount();

            // Pre-compute selected vertex positions
            var selectedPositions = new Vector3[_faceVertexIndices.Count];
            int idx = 0;
            foreach (var vertIdx in _faceVertexIndices)
            {
                var pos = vertexArray[vertIdx].Position;
                selectedPositions[idx++] = new Vector3(pos.X, pos.Y, pos.Z);
            }

            // Compute weights for all vertices
            for (int i = 0; i < vertCount; i++)
            {
                if (_faceVertexIndices.Contains(i))
                {
                    _falloffWeights[i] = 1.0f;
                }
                else
                {
                    var pos = vertexArray[i].Position;
                    var currentPos = new Vector3(pos.X, pos.Y, pos.Z);
                    float minDist = float.MaxValue;
                    for (int j = 0; j < selectedPositions.Length; j++)
                    {
                        var dx = currentPos.X - selectedPositions[j].X;
                        var dy = currentPos.Y - selectedPositions[j].Y;
                        var dz = currentPos.Z - selectedPositions[j].Z;
                        var distSq = dx * dx + dy * dy + dz * dz;
                        if (distSq < minDist) minDist = distSq;
                    }
                    var dist = MathF.Sqrt(minDist);
                    if (dist <= _falloffDistance)
                        _falloffWeights[i] = 1.0f - dist / _falloffDistance;
                }
            }
        }

        #endregion

        public static TransformGizmoWrapper CreateFromSelectionState(ISelectionState state, CommandFactory commandFactory)
        {
            if (state is ObjectSelectionState objectSelectionState)
            {
                var transformables = objectSelectionState.CurrentSelection().Where(x => x is ITransformable).Select(x => x.Geometry);
                if (transformables.Any())
                    return new TransformGizmoWrapper(commandFactory, transformables.ToList(), state);
            }
            else if (state is VertexSelectionState vertexSelectionState)
            {
                if (vertexSelectionState.SelectedVertices.Count != 0)
                    return new TransformGizmoWrapper(commandFactory, new List<MeshObject>() { vertexSelectionState.RenderObject.Geometry }, vertexSelectionState);
            }
            else if (state is FaceSelectionState faceSelectionState)
            {
                if (faceSelectionState.SelectedFaces.Count != 0 && faceSelectionState.RenderObject != null)
                    return new TransformGizmoWrapper(commandFactory, new List<MeshObject>() { faceSelectionState.RenderObject.Geometry }, faceSelectionState);
            }
            else if (state is EdgeSelectionState edgeSelectionState)
            {
                if (edgeSelectionState.SelectedEdges.Count != 0 && edgeSelectionState.RenderObject != null)
                    return new TransformGizmoWrapper(commandFactory, new List<MeshObject>() { edgeSelectionState.RenderObject.Geometry }, edgeSelectionState);
            }
            else if (state is BoneSelectionState boneSelectionState)
            {
                if (boneSelectionState.SelectedBones.Count != 0)
                    return new TransformGizmoWrapper(commandFactory, boneSelectionState.SelectedBones, boneSelectionState);
            }
            return null;
        }

    }
}
