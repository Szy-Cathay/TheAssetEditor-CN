using GameWorld.Core.Commands;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;

namespace GameWorld.Core.Components.Gizmo
{
    public class TransformGizmoWrapper : ITransformable, IDisposable
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
        BoneSelectionState _boneSelectionState;
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
        private Vector3 _backupScale;
        private bool _hasBackup = false;

        private ISelectionState _gestureSelectionState;
        private HashSet<int> _gestureAffectedVertexIndices;
        private Dictionary<int, float> _gestureFalloffWeights;
        private VertexUploadRange?[] _displayedPreviewRanges;
        private VertexUploadRange?[] _nextPreviewRanges;
        private bool _previewSynchronizationFaulted;

        private readonly record struct VertexUploadRange(
            int FirstModifiedVertex,
            int LastModifiedVertex)
        {
            public static VertexUploadRange From(VertexTransformMeshResult result)
            {
                return new VertexUploadRange(
                    result.FirstModifiedVertex,
                    result.LastModifiedVertex);
            }

            public static VertexUploadRange Union(
                VertexUploadRange first,
                VertexUploadRange second)
            {
                return new VertexUploadRange(
                    Math.Min(first.FirstModifiedVertex, second.FirstModifiedVertex),
                    Math.Max(first.LastModifiedVertex, second.LastModifiedVertex));
            }
        }

        private readonly record struct PreviewApplyResult(
            bool Accepted,
            IReadOnlyList<VertexTransformMeshResult> MeshResults)
        {
            public static PreviewApplyResult Rejected()
            {
                return new PreviewApplyResult(
                    false,
                    Array.Empty<VertexTransformMeshResult>());
            }

            public static PreviewApplyResult Applied(
                IReadOnlyList<VertexTransformMeshResult> meshResults)
            {
                return new PreviewApplyResult(true, meshResults);
            }
        }

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
            _boneSelectionState = boneSelectionState;
            _selectedBones = new List<int>(selectedBones);

            _effectedObjects = new List<MeshObject> { boneSelectionState.RenderObject.Geometry };
            RefreshBoneDisplay();
            _boneSelectionState.BoneModifiedEvent += OnBoneModified;
        }

        private void RefreshBoneDisplay()
        {
            var skeleton = _boneSelectionState.Skeleton;
            var animation = _boneSelectionState.CurrentAnimation;
            var frameIndex = _boneSelectionState.CurrentFrame;
            if (skeleton == null ||
                animation == null ||
                frameIndex < 0 ||
                frameIndex >= animation.DynamicFrames.Count)
            {
                return;
            }

            var currentFrame = AnimationSampler.Sample(
                frameIndex,
                0,
                skeleton,
                animation,
                freezeFrame: true);
            if (currentFrame == null)
                return;

            var totalBones = 0;
            var rotations = new List<Quaternion>();
            var position = Vector3.Zero;
            var scaleTotal = Vector3.Zero;
            foreach (var boneIdx in _selectedBones)
            {
                if (boneIdx < 0 || boneIdx >= currentFrame.BoneTransforms.Count)
                    continue;

                var bone = currentFrame.GetSkeletonAnimatedWorld(skeleton, boneIdx);
                var scaleSignHint =
                    GetWorldScaleSignHint(currentFrame, skeleton, boneIdx);
                if (!BoneTransformMath.TryDecomposeSignedTrs(
                        bone,
                        scaleSignHint,
                        out var scale,
                        out var rot,
                        out var trans) &&
                    !bone.Decompose(out scale, out rot, out trans))
                {
                    continue;
                }

                position += trans;
                scaleTotal += scale;
                rotations.Add(rot);
                totalBones++;
            }

            if (totalBones == 0)
                return;

            _orientation = AverageOrientation(rotations);
            _pos = position / totalBones;
            _scale = scaleTotal / totalBones;
        }

        private void OnBoneModified(BoneSelectionState state)
        {
            if (!IsTransformActive)
                RefreshBoneDisplay();
        }

        private static Vector3 GetWorldScaleSignHint(
            AnimationFrame frame,
            GameSkeleton skeleton,
            int boneIndex)
        {
            var signHint = Vector3.One;
            var visitedBones = 0;
            while (boneIndex >= 0 && visitedBones++ < skeleton.BoneCount)
            {
                signHint = BoneTransformMath.MultiplyComponents(
                    signHint,
                    frame.BoneTransforms[boneIndex].Scale);
                boneIndex = skeleton.GetParentBoneIndex(boneIndex);
            }

            return signHint;
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

        public void BeginTransform()
        {
            if (_activeCommand != null || _hasBackup)
                CancelTransform();

            ResetGestureState();
            if (_selectionState is FaceSelectionState or EdgeSelectionState)
                ComputeFalloffWeights();
            if (_selectionState is BoneSelectionState)
            {
                var boneCommand = _commandFactory.Create<TransformBoneCommand>()
                    .Configure(x => x.Configure(_selectedBones, (BoneSelectionState)_selectionState))
                    .Build();
                _backupPosition = _pos;
                _backupOrientation = _orientation;
                _backupScale = _scale;
                _activeCommand = boneCommand;
                return;
            }

            var command = _commandFactory.Create<TransformVertexCommand>()
                .Configure(x => x.Configure(_effectedObjects, Position))
                .Build();

            try
            {
                CaptureVertexBaseline();
                if (_gestureAffectedVertexIndices != null)
                {
                    command.AffectedVertexIndices =
                        new HashSet<int>(_gestureAffectedVertexIndices);
                }
                _activeCommand = command;
            }
            catch
            {
                _activeCommand = null;
                ResetGestureState();
                throw;
            }
        }

        public void CommitTransform(CommandExecutor commandExecutor)
        {
            if (_previewSynchronizationFaulted)
            {
                throw new InvalidOperationException(
                    "The transform preview is not synchronized. Cancel the transform before continuing.");
            }

            EndTransform(() =>
            {
                if (_activeCommand is TransformVertexCommand transformVertexCommand)
                {
                    ConfigureTransformCommandForCommit(transformVertexCommand);
                    if (HasVertexMutation())
                        commandExecutor.ExecuteCommand(transformVertexCommand);
                }
                else if (_activeCommand is TransformBoneCommand transformBoneCommand)
                {
                    if (transformBoneCommand.HasFrameMutation())
                        commandExecutor.ExecuteCommand(transformBoneCommand);
                }
            });
        }

        public void CancelTransform()
        {
            EndTransform(() =>
            {
                if (_activeCommand is TransformBoneCommand transformBoneCommand)
                {
                    try
                    {
                        transformBoneCommand.RestoreInitialFrame();
                    }
                    finally
                    {
                        ResetBonePreviewToBaseline();
                    }
                }
                else if (_activeCommand is TransformVertexCommand || _hasBackup)
                    RestoreVertexBaseline();
            });
        }

        public void RestoreInitialPreviewState()
        {
            if (_activeCommand is TransformBoneCommand transformBoneCommand)
            {
                try
                {
                    transformBoneCommand.RestoreInitialFrame();
                }
                finally
                {
                    ResetBonePreviewToBaseline();
                }
                return;
            }

            if (_activeCommand is not TransformVertexCommand || !_hasBackup)
                return;

            if (_previewSynchronizationFaulted)
            {
                throw new InvalidOperationException(
                    "The transform preview is not synchronized. Cancel the transform before continuing.");
            }

            try
            {
                RestoreVertexBaseline();
            }
            catch (Exception exception)
            {
                RecoverVertexPreviewAndRethrow(exception);
            }
        }

        internal void ReplaceInitialPreview(
            ModalPreviewReplacement replacement)
        {
            if (_activeCommand is TransformBoneCommand transformBoneCommand)
            {
                ReplaceBoneInitialPreview(
                    transformBoneCommand,
                    replacement);
                return;
            }

            if (_activeCommand is not TransformVertexCommand || !_hasBackup)
                return;
            if (_previewSynchronizationFaulted)
            {
                throw new InvalidOperationException(
                    "The transform preview is not synchronized. Cancel the transform before continuing.");
            }

            try
            {
                var restoreError = RestoreVertexBaselineCpuAndIndex();
                restoreError?.Throw();
                ResetGestureCandidateToBaseline();

                var result = ApplyReplacement(replacement);
                if (result.Accepted)
                    SynchronizeVertexPreview(result.MeshResults);
                else
                    SynchronizeBaselinePreview();
            }
            catch (Exception exception)
            {
                RecoverVertexPreviewAndRethrow(exception);
            }
        }

        private PreviewApplyResult ApplyReplacement(
            ModalPreviewReplacement replacement)
        {
            return replacement.Kind switch
            {
                ModalPreviewReplacementKind.RestoreOnly =>
                    PreviewApplyResult.Rejected(),
                ModalPreviewReplacementKind.Translate =>
                    TryTranslatePreview(
                        replacement.VectorValue,
                        replacement.Pivot),
                ModalPreviewReplacementKind.Rotate =>
                    TryRotatePreview(
                        replacement.RotationValue,
                        replacement.Pivot),
                ModalPreviewReplacementKind.Scale =>
                    TryScalePreview(
                        replacement.VectorValue,
                        replacement.Pivot),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(replacement))
            };
        }

        private void ReplaceBoneInitialPreview(
            TransformBoneCommand transformBoneCommand,
            ModalPreviewReplacement replacement)
        {
            ExceptionDispatchInfo primaryError = null;
            try
            {
                transformBoneCommand.RestoreInitialFrame();
                ResetBonePreviewToBaseline();
                ApplyBoneReplacement(replacement);
                return;
            }
            catch (Exception exception)
            {
                primaryError = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                transformBoneCommand.RestoreInitialFrame();
                ResetBonePreviewToBaseline();
            }
            catch (Exception recoveryException)
            {
                _logger.Error(
                    recoveryException,
                    "Failed to recover the initial bone transform preview");
            }

            primaryError.Throw();
        }

        private void ApplyBoneReplacement(
            ModalPreviewReplacement replacement)
        {
            switch (replacement.Kind)
            {
                case ModalPreviewReplacementKind.RestoreOnly:
                    return;
                case ModalPreviewReplacementKind.Translate:
                    GizmoTranslateEvent(
                        replacement.VectorValue,
                        replacement.Pivot);
                    return;
                case ModalPreviewReplacementKind.Rotate:
                    GizmoRotateEvent(
                        replacement.RotationValue,
                        replacement.Pivot);
                    return;
                case ModalPreviewReplacementKind.Scale:
                    GizmoScaleEvent(
                        replacement.VectorValue,
                        replacement.Pivot);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(replacement));
            }
        }

        private void EndTransform(Action transformEnd)
        {
            ExceptionDispatchInfo primaryError = null;
            try
            {
                transformEnd();
            }
            catch (Exception exception)
            {
                primaryError = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                try
                {
                    ReleaseVertexBaseline();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    _activeCommand = null;
                    ResetGestureState();
                }
            }

            primaryError?.Throw();
        }

        private bool HasVertexMutation()
        {
            if (_vertexTransformReplayPlan?.OperationCount <= 0 ||
                _effectedObjects == null ||
                _backupVertexArrays == null ||
                _backupIndexArrays == null)
            {
                return false;
            }

            var meshCount = Math.Min(
                _effectedObjects.Count,
                Math.Min(_backupVertexArrays.Count, _backupIndexArrays.Count));
            for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                var mesh = _effectedObjects[meshIndex];
                if (mesh?.VertexArray == null ||
                    mesh.IndexArray == null ||
                    !mesh.VertexArray.SequenceEqual(_backupVertexArrays[meshIndex]) ||
                    !mesh.IndexArray.SequenceEqual(_backupIndexArrays[meshIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetGestureState()
        {
            _totalGizomTransform = Matrix.Identity;
            _invertedWindingOrder = false;
            _vertexTransformReplayPlan = null;
            _gestureSelectionState = null;
            _gestureAffectedVertexIndices = null;
            _gestureFalloffWeights = null;
            _displayedPreviewRanges = null;
            _nextPreviewRanges = null;
            _previewSynchronizationFaulted = false;
        }

        private void ResetBonePreviewToBaseline()
        {
            ResetGestureState();
            _pos = _backupPosition;
            _orientation = _backupOrientation;
            _scale = _backupScale;
        }

        void ConfigureTransformCommandForCommit(TransformVertexCommand command)
        {
            command.InvertWindingOrder = _invertedWindingOrder;
            command.Transform = _totalGizomTransform;
            command.PivotPoint = Position;
            command.SetReplayPlan(
                _vertexTransformReplayPlan ??
                VertexTransformOperationApplier.CreateEmptyReplayPlan(
                    GestureSelectionState,
                    GestureFalloffWeights));
            if (_gestureAffectedVertexIndices != null)
            {
                command.AffectedVertexIndices =
                    new HashSet<int>(_gestureAffectedVertexIndices);
            }
            if (_gestureFalloffWeights != null)
            {
                command.FalloffWeights =
                    new Dictionary<int, float>(_gestureFalloffWeights);
            }
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
            if (_selectionState is BoneSelectionState)
            {
                var pivotPoint = GetBonePivot(pivot);
                if (!BoneTransformDelta.TryCreateTranslation(
                        translation,
                        pivotPoint,
                        out var boneDelta) ||
                    !TransformBone(boneDelta))
                {
                    return;
                }

                return;
            }

            RunDirectVertexPreview(
                () => TryTranslatePreview(translation, pivot));
        }

        public void GizmoRotateEvent(Matrix rotation, PivotType pivot)
        {
            if (_selectionState is BoneSelectionState)
            {
                var pivotPoint = GetBonePivot(pivot);
                if (!BoneTransformDelta.TryCreateRotation(
                        rotation,
                        pivotPoint,
                        out var boneDelta) ||
                    !TransformBone(boneDelta))
                {
                    return;
                }

                return;
            }

            RunDirectVertexPreview(
                () => TryRotatePreview(rotation, pivot));
        }

        public void GizmoScaleEvent(Vector3 scale, PivotType pivot)
        {
            var scaleFactor = scale + Vector3.One;
            var scaleMatrix = Matrix.CreateScale(scaleFactor);
            if (_selectionState is BoneSelectionState)
            {
                var pivotPoint = GetBonePivot(pivot);
                if (!BoneTransformDelta.TryCreateScale(
                        scaleFactor,
                        pivotPoint,
                        out var boneDelta) ||
                    !TransformBone(boneDelta))
                {
                    return;
                }

                return;
            }

            RunDirectVertexPreview(
                () => TryScalePreview(scale, pivot));
        }

        private PreviewApplyResult TryTranslatePreview(
            Vector3 translation,
            PivotType pivot)
        {
            var transform = Matrix.CreateTranslation(translation);
            if (!TryApplyTransform(
                    transform,
                    pivot,
                    GizmoMode.Translate,
                    out var results))
            {
                return PreviewApplyResult.Rejected();
            }

            Position += translation;
            _totalGizomTransform *= transform;
            return PreviewApplyResult.Applied(results);
        }

        private PreviewApplyResult TryRotatePreview(
            Matrix rotation,
            PivotType pivot)
        {
            if (!TryApplyTransform(
                    rotation,
                    pivot,
                    GizmoMode.Rotate,
                    out var results))
            {
                return PreviewApplyResult.Rejected();
            }

            _totalGizomTransform *= rotation;
            var fixedTransform = FixRotationAxis2(_totalGizomTransform);
            fixedTransform.Decompose(out var _, out var quat, out var _);
            Orientation = quat;
            return PreviewApplyResult.Applied(results);
        }

        private PreviewApplyResult TryScalePreview(
            Vector3 scale,
            PivotType pivot)
        {
            var scaleMatrix = Matrix.CreateScale(scale + Vector3.One);
            if (!TryApplyTransform(
                    scaleMatrix,
                    pivot,
                    GizmoMode.UniformScale,
                    out var results))
            {
                return PreviewApplyResult.Rejected();
            }

            Scale += scale;
            _totalGizomTransform *= scaleMatrix;
            return PreviewApplyResult.Applied(results);
        }

        private void RunDirectVertexPreview(
            Func<PreviewApplyResult> applyPreview)
        {
            try
            {
                var result = applyPreview();
                if (result.Accepted)
                    SynchronizeVertexPreview(result.MeshResults);
            }
            catch (Exception exception)
            {
                RecoverVertexPreviewAndRethrow(exception);
            }
        }

        bool TryApplyTransform(
            Matrix transform,
            PivotType pivotType,
            GizmoMode gizmoMode,
            out IReadOnlyList<VertexTransformMeshResult> results)
        {
            results = Array.Empty<VertexTransformMeshResult>();
            if (_previewSynchronizationFaulted)
                return false;

            var operationMode = gizmoMode switch
            {
                GizmoMode.Translate => VertexTransformOperationMode.Translate,
                GizmoMode.Rotate => VertexTransformOperationMode.Rotate,
                _ => VertexTransformOperationMode.Scale
            };
            var pivotPoint = pivotType == PivotType.ObjectCenter ? Position : Vector3.Zero;
            var operation = new VertexTransformOperation(operationMode, transform, pivotPoint);
            if (_vertexTransformReplayPlan == null ||
                _backupVertexArrays == null ||
                !VertexTransformOperationApplier.TryAppendOperation(
                    _effectedObjects,
                    GestureSelectionState,
                    GestureAffectedVertexIndices,
                    GestureFalloffWeights,
                    _vertexTransformReplayPlan,
                    operation,
                    out var candidatePlan) ||
                !VertexTransformOperationApplier.IsReplayPlanReversibleFromBaseline(
                    _effectedObjects,
                    _backupVertexArrays,
                    GestureSelectionState,
                    GestureAffectedVertexIndices,
                    GestureFalloffWeights,
                    candidatePlan))
            {
                return false;
            }

            VertexTransformOperationApplier.RestoreAffectedVerticesFromBaseline(
                _effectedObjects,
                _backupVertexArrays,
                GestureSelectionState,
                GestureAffectedVertexIndices,
                GestureFalloffWeights);
            results = VertexTransformOperationApplier.ApplyReplayPlan(
                _effectedObjects,
                GestureSelectionState,
                GestureAffectedVertexIndices,
                GestureFalloffWeights,
                candidatePlan,
                inverse: false);
            var isObjectSelection = GestureSelectionState is ObjectSelectionState;
            var wasInverted =
                isObjectSelection &&
                _vertexTransformReplayPlan.RawMatrices.Forward.Determinant() < 0;
            var isInverted =
                isObjectSelection &&
                candidatePlan.RawMatrices.Forward.Determinant() < 0;
            if (wasInverted != isInverted)
            {
                foreach (var geometry in _effectedObjects)
                    TransformVertexCommand.ReverseWindingOrder(geometry);
            }

            _invertedWindingOrder = isInverted;
            _vertexTransformReplayPlan = candidatePlan;
            return true;
        }

        private void SynchronizeVertexPreview(
            IReadOnlyList<VertexTransformMeshResult> results)
        {
            if (_displayedPreviewRanges == null ||
                results.Count != _effectedObjects.Count ||
                _displayedPreviewRanges.Length != _effectedObjects.Count)
            {
                throw new InvalidOperationException(
                    "Vertex preview results do not match the captured mesh set.");
            }

            if (_nextPreviewRanges == null ||
                _nextPreviewRanges.Length != results.Count)
            {
                throw new InvalidOperationException(
                    "Vertex preview scratch ranges do not match the captured mesh set.");
            }

            var nextRanges = _nextPreviewRanges;
            for (var meshIndex = 0; meshIndex < results.Count; meshIndex++)
            {
                var result = results[meshIndex];
                var mesh = _effectedObjects[meshIndex];
                if (!ReferenceEquals(result.Geometry, mesh))
                {
                    throw new InvalidOperationException(
                        "Vertex preview result geometry changed during the gesture.");
                }

                var nextRange = result.HasModifiedVertices
                    ? VertexUploadRange.From(result)
                    : (VertexUploadRange?)null;
                nextRanges[meshIndex] = nextRange;
            }

            SynchronizePreviewRanges(nextRanges);
        }

        private void SynchronizeBaselinePreview()
        {
            if (_effectedObjects == null)
                return;
            if (_nextPreviewRanges == null ||
                _nextPreviewRanges.Length != _effectedObjects.Count)
            {
                throw new InvalidOperationException(
                    "Vertex preview scratch ranges do not match the captured mesh set.");
            }

            Array.Clear(
                _nextPreviewRanges,
                0,
                _nextPreviewRanges.Length);
            SynchronizePreviewRanges(_nextPreviewRanges);
        }

        private void SynchronizePreviewRanges(
            VertexUploadRange?[] nextRanges)
        {
            if (_displayedPreviewRanges == null ||
                _effectedObjects == null ||
                nextRanges.Length != _effectedObjects.Count ||
                _displayedPreviewRanges.Length != _effectedObjects.Count)
            {
                throw new InvalidOperationException(
                    "Vertex preview ranges do not match the captured mesh set.");
            }

            ExceptionDispatchInfo uploadError = null;
            for (var meshIndex = 0; meshIndex < nextRanges.Length; meshIndex++)
            {
                var mesh = _effectedObjects[meshIndex];
                var uploadRange = UnionPreviewRanges(
                    _displayedPreviewRanges[meshIndex],
                    nextRanges[meshIndex]);
                if (!uploadRange.HasValue)
                    continue;

                try
                {
                    UploadVertexRange(mesh, uploadRange.Value);
                }
                catch (Exception exception)
                {
                    uploadError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            uploadError?.Throw();
            var previousRanges = _displayedPreviewRanges;
            _displayedPreviewRanges = nextRanges;
            _nextPreviewRanges = previousRanges;
        }

        private void UploadVertexRange(
            MeshObject mesh,
            VertexUploadRange uploadRange)
        {
            if (GestureSelectionState is ObjectSelectionState)
            {
                mesh.RebuildVertexBuffer();
                return;
            }

            mesh.RebuildVertexBufferPartial(
                uploadRange.FirstModifiedVertex,
                uploadRange.LastModifiedVertex);
        }

        private static VertexUploadRange? UnionPreviewRanges(
            VertexUploadRange? first,
            VertexUploadRange? second)
        {
            if (!first.HasValue)
                return second;
            if (!second.HasValue)
                return first;
            return VertexUploadRange.Union(first.Value, second.Value);
        }

        bool TransformBone(BoneTransformDelta delta)
        {
            if (_activeCommand is TransformBoneCommand transformBoneCommand)
            {
                try
                {
                    var applied = transformBoneCommand.ApplyTransformation(delta);
                    if (applied)
                        RefreshBoneDisplay();
                    return applied;
                }
                catch (Exception exception)
                {
                    try
                    {
                        RefreshBoneDisplay();
                    }
                    catch (Exception refreshException)
                    {
                        _logger.Error(
                            refreshException,
                            "Failed to refresh bone transform display");
                    }

                    ExceptionDispatchInfo.Capture(exception).Throw();
                }
            }

            return false;
        }

        private Vector3 GetBonePivot(PivotType pivot)
        {
            return pivot == PivotType.WorldOrigin
                ? Vector3.Zero
                : Position;
        }

        public Vector3 GetObjectCentre()
        {
            return Position;
        }

        #region Modal Transform State Backup (Blender-style)

        private void CaptureVertexBaseline()
        {
            if (_effectedObjects == null || _effectedObjects.Count == 0)
                return;

            var vertexArrays = new List<VertexPositionNormalTextureCustom[]>(_effectedObjects.Count);
            var indexArrays = new List<ushort[]>(_effectedObjects.Count);
            try
            {
                var gestureSelectionState = _selectionState.Clone();
                var gestureAffectedVertexIndices = _faceVertexIndices == null
                    ? null
                    : new HashSet<int>(_faceVertexIndices);
                var gestureFalloffWeights =
                    _falloffDistance > 0 && _falloffWeights != null
                        ? new Dictionary<int, float>(_falloffWeights)
                        : null;
                foreach (var mesh in _effectedObjects)
                {
                    var vertexBackup = new VertexPositionNormalTextureCustom[mesh.VertexCount()];
                    Array.Copy(mesh.VertexArray, vertexBackup, vertexBackup.Length);
                    vertexArrays.Add(vertexBackup);

                    var indexBackup = new ushort[mesh.IndexArray.Length];
                    Array.Copy(mesh.IndexArray, indexBackup, indexBackup.Length);
                    indexArrays.Add(indexBackup);
                }

                var replayPlan = VertexTransformOperationApplier.CreateEmptyReplayPlan(
                    gestureSelectionState,
                    gestureFalloffWeights);
                foreach (var mesh in _effectedObjects)
                    mesh.DeferBoundingBoxRebuild = true;

                _backupVertexArrays = vertexArrays;
                _backupIndexArrays = indexArrays;
                _backupPosition = _pos;
                _backupOrientation = _orientation;
                _backupScale = _scale;
                _gestureSelectionState = gestureSelectionState;
                _gestureAffectedVertexIndices = gestureAffectedVertexIndices;
                _gestureFalloffWeights = gestureFalloffWeights;
                _displayedPreviewRanges =
                    new VertexUploadRange?[_effectedObjects.Count];
                _nextPreviewRanges =
                    new VertexUploadRange?[_effectedObjects.Count];
                _vertexTransformReplayPlan = replayPlan;
                _hasBackup = true;
            }
            catch (Exception exception)
            {
                var primaryError = ExceptionDispatchInfo.Capture(exception);
                ReleaseDeferredMeshes(rebuildBoundingBoxes: false);
                ClearBackupStorage();
                primaryError.Throw();
            }
        }

        private void RestoreVertexBaseline()
        {
            if (!_hasBackup || _effectedObjects == null ||
                _backupVertexArrays == null || _backupIndexArrays == null)
            {
                return;
            }

            var primaryError = RestoreVertexBaselineCpuAndIndex();
            if (_previewSynchronizationFaulted)
            {
                var uploadError = UploadFullVertexBuffers();
                primaryError ??= uploadError;
            }
            else
            {
                try
                {
                    SynchronizeBaselinePreview();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            try
            {
                ResetGestureCandidateToBaseline();
            }
            catch (Exception exception)
            {
                primaryError ??= ExceptionDispatchInfo.Capture(exception);
            }
            ClearDisplayedPreviewRanges();
            primaryError?.Throw();
        }

        private ExceptionDispatchInfo RestoreVertexBaselineCpuAndIndex()
        {
            if (!_hasBackup || _effectedObjects == null ||
                _backupVertexArrays == null || _backupIndexArrays == null)
            {
                return null;
            }

            ExceptionDispatchInfo primaryError = null;
            if (_effectedObjects.Count != _backupVertexArrays.Count ||
                _effectedObjects.Count != _backupIndexArrays.Count)
            {
                primaryError = ExceptionDispatchInfo.Capture(
                    new InvalidOperationException(
                        "The transform baseline does not match the captured mesh set."));
            }

            var meshCount = Math.Min(
                _effectedObjects.Count,
                Math.Min(_backupVertexArrays.Count, _backupIndexArrays.Count));
            for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                var mesh = _effectedObjects[meshIndex];
                var vertexBackup = _backupVertexArrays[meshIndex];
                var indexBackup = _backupIndexArrays[meshIndex];
                try
                {
                    Array.Copy(vertexBackup, mesh.VertexArray, vertexBackup.Length);
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }

                try
                {
                    Array.Copy(indexBackup, mesh.IndexArray, indexBackup.Length);
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }

                try
                {
                    mesh.RebuildIndexBuffer();
                }
                catch (Exception exception)
                {
                    primaryError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            return primaryError;
        }

        private ExceptionDispatchInfo UploadFullVertexBuffers()
        {
            ExceptionDispatchInfo uploadError = null;
            if (_effectedObjects == null)
                return null;

            foreach (var mesh in _effectedObjects)
            {
                try
                {
                    mesh.RebuildVertexBuffer();
                }
                catch (Exception exception)
                {
                    uploadError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            return uploadError;
        }

        private void RecoverVertexPreviewAndRethrow(Exception exception)
        {
            var primaryError = ExceptionDispatchInfo.Capture(exception);
            var recoveryError = RestoreVertexBaselineCpuAndIndex();
            var uploadError = UploadFullVertexBuffers();
            recoveryError ??= uploadError;

            try
            {
                ResetGestureCandidateToBaseline();
            }
            catch (Exception recoveryException)
            {
                recoveryError ??=
                    ExceptionDispatchInfo.Capture(recoveryException);
            }
            ClearDisplayedPreviewRanges();

            _previewSynchronizationFaulted = recoveryError != null;
            if (recoveryError != null)
            {
                _logger.Error(
                    recoveryError.SourceException,
                    "Failed to synchronize the transform preview with its baseline");
            }

            primaryError.Throw();
        }

        private void ClearDisplayedPreviewRanges()
        {
            if (_displayedPreviewRanges == null)
                return;

            Array.Clear(
                _displayedPreviewRanges,
                0,
                _displayedPreviewRanges.Length);
            if (_nextPreviewRanges != null)
            {
                Array.Clear(
                    _nextPreviewRanges,
                    0,
                    _nextPreviewRanges.Length);
            }
        }

        private void ResetGestureCandidateToBaseline()
        {
            _totalGizomTransform = Matrix.Identity;
            _invertedWindingOrder = false;
            _vertexTransformReplayPlan =
                VertexTransformOperationApplier.CreateEmptyReplayPlan(
                    GestureSelectionState,
                    GestureFalloffWeights);
            _pos = _backupPosition;
            _orientation = _backupOrientation;
            _scale = _backupScale;
        }

        private ISelectionState GestureSelectionState =>
            _gestureSelectionState ?? _selectionState;

        private HashSet<int> GestureAffectedVertexIndices =>
            _gestureAffectedVertexIndices;

        private IReadOnlyDictionary<int, float> GestureFalloffWeights =>
            _gestureFalloffWeights;

        private void ReleaseVertexBaseline()
        {
            if (!_hasBackup && _backupVertexArrays == null && _backupIndexArrays == null)
                return;

            var cleanupError = ReleaseDeferredMeshes(rebuildBoundingBoxes: true);
            ClearBackupStorage();
            cleanupError?.Throw();
        }

        private ExceptionDispatchInfo ReleaseDeferredMeshes(bool rebuildBoundingBoxes)
        {
            ExceptionDispatchInfo cleanupError = null;
            if (_effectedObjects == null)
                return null;

            foreach (var mesh in _effectedObjects)
            {
                try
                {
                    mesh.DeferBoundingBoxRebuild = false;
                    if (rebuildBoundingBoxes)
                        mesh.BuildBoundingBox();
                }
                catch (Exception exception)
                {
                    cleanupError ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            return cleanupError;
        }

        private void ClearBackupStorage()
        {
            _backupVertexArrays?.Clear();
            _backupIndexArrays?.Clear();
            _backupVertexArrays = null;
            _backupIndexArrays = null;
            _hasBackup = false;
        }

        /// <summary>
        /// Check if there's a valid backup
        /// </summary>
        public bool HasBackup => _hasBackup;

        internal bool IsTransformActive => _activeCommand != null || _hasBackup;

        /// <summary>
        /// Set falloff distance for face/edge mode proportional editing
        /// </summary>
        public void SetFalloffDistance(float distance)
        {
            _falloffDistance = distance;
            if (!IsTransformActive)
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

        public void Dispose()
        {
            if (_boneSelectionState != null)
            {
                _boneSelectionState.BoneModifiedEvent -= OnBoneModified;
                _boneSelectionState = null;
            }
        }

    }
}
