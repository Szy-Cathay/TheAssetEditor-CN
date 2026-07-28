using System;
using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Animation.AnimationClip;

namespace GameWorld.Core.Commands.Bone
{
    public class TransformBoneCommand : IRedoableCommand
    {
        List<int> _selectedBones;
        BoneSelectionState _boneSelectionState;
        BoneSelectionState _selectionSnapshot;
        AnimationClip _animation;
        int _currentFrame;
        KeyFrame _oldFrame;
        KeyFrame _newFrame;
        public string HintText => "Bone Transform";

        public bool IsMutation => true;

        public TransformBoneCommand(SelectionManager selectionManager)
        {
        }

        public void Configure(List<int> selectedBones, BoneSelectionState state)
        {
            _selectedBones = new List<int>(selectedBones);
            _boneSelectionState = state;
            _selectionSnapshot = (BoneSelectionState)state.Clone();
            _animation = state.CurrentAnimation;
            _currentFrame = state.CurrentFrame;
            _oldFrame = _animation.DynamicFrames[_currentFrame].Clone();
            _newFrame = null;
        }

        internal bool ApplyTransformation(BoneTransformDelta delta)
        {
            if (_selectedBones.Count == 0 ||
                delta.IsNoOp() ||
                _boneSelectionState.Skeleton == null)
            {
                return false;
            }

            //TODO: FIX ME
            //if(_boneSelectionState.EnableInverseKinematics)
            //{
            //    ApplyTransformationInverseKinematic(newPosition, _selectedBones[0], _boneSelectionState.InverseKinematicsEndBoneIndex);
            //    return;
            //}
            var sourceFrame = _animation.DynamicFrames[_currentFrame];
            var skeleton = _boneSelectionState.Skeleton;
            var boneCount = sourceFrame.GetBoneCountFromFrame();
            if (boneCount == 0 ||
                boneCount > skeleton.BoneCount ||
                _selectedBones.Any(index => index < 0 || index >= boneCount))
            {
                return false;
            }

            var sampledFrame = AnimationSampler.Sample(
                _currentFrame,
                0,
                skeleton,
                _animation,
                freezeFrame: true);
            if (sampledFrame == null ||
                sampledFrame.BoneTransforms.Count < boneCount)
            {
                return false;
            }

            var selectedBones = new HashSet<int>(_selectedBones);
            var sourceLocal = new Matrix[boneCount];
            var sourceWorld = new Matrix[boneCount];
            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                sourceLocal[boneIndex] =
                    Matrix.CreateScale(sourceFrame.Scale[boneIndex]) *
                    Matrix.CreateFromQuaternion(sourceFrame.Rotation[boneIndex]) *
                    Matrix.CreateTranslation(sourceFrame.Position[boneIndex]);
                sourceWorld[boneIndex] =
                    sampledFrame.GetSkeletonAnimatedWorld(skeleton, boneIndex);
                if (!BoneTransformMath.IsFinite(sourceLocal[boneIndex]) ||
                    !BoneTransformMath.IsFinite(sourceWorld[boneIndex]))
                {
                    return false;
                }
            }

            var worldDelta = delta.CreateWorldMatrix();
            if (!BoneTransformMath.IsFinite(worldDelta))
                return false;

            var resolvedWorld = new Matrix[boneCount];
            var resolutionState = new byte[boneCount];
            bool TryResolveWorld(int boneIndex)
            {
                if (resolutionState[boneIndex] == 2)
                    return true;
                if (resolutionState[boneIndex] == 1)
                    return false;

                resolutionState[boneIndex] = 1;
                var parentIndex = skeleton.GetParentBoneIndex(boneIndex);
                if (parentIndex < -1 || parentIndex >= boneCount)
                    return false;
                if (parentIndex >= 0 && !TryResolveWorld(parentIndex))
                    return false;

                if (selectedBones.Contains(boneIndex))
                {
                    resolvedWorld[boneIndex] = sourceWorld[boneIndex] * worldDelta;
                }
                else if (parentIndex == -1)
                {
                    resolvedWorld[boneIndex] = sourceWorld[boneIndex];
                }
                else
                {
                    resolvedWorld[boneIndex] =
                        sourceLocal[boneIndex] * resolvedWorld[parentIndex];
                }

                if (!BoneTransformMath.IsFinite(resolvedWorld[boneIndex]))
                    return false;

                resolutionState[boneIndex] = 2;
                return true;
            }

            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                if (!TryResolveWorld(boneIndex))
                    return false;
            }

            var orderedSelection = _selectedBones
                .Distinct()
                .OrderBy(GetBoneDepth)
                .ToList();
            var candidateComponents =
                new Dictionary<int, (Vector3 Scale, Quaternion Rotation, Vector3 Position)>();
            foreach (var selectedBone in orderedSelection)
            {
                var parentIndex = skeleton.GetParentBoneIndex(selectedBone);
                var parentWorld = parentIndex == -1
                    ? Matrix.Identity
                    : resolvedWorld[parentIndex];
                if (!BoneTransformMath.TryInvert(
                        parentWorld,
                        out var inverseParentWorld))
                    return false;

                var desiredLocal =
                    resolvedWorld[selectedBone] * inverseParentWorld;
                if (delta.Kind == BoneTransformDeltaKind.Translation)
                {
                    var sourceScale = sourceFrame.Scale[selectedBone];
                    var sourceRotation = sourceFrame.Rotation[selectedBone];
                    var translatedLocal =
                        Matrix.CreateScale(sourceScale) *
                        Matrix.CreateFromQuaternion(sourceRotation) *
                        Matrix.CreateTranslation(desiredLocal.Translation);
                    if (!BoneTransformMath.MatricesNear(
                            desiredLocal,
                            translatedLocal,
                            0.0005f))
                        return false;

                    candidateComponents[selectedBone] = (
                        sourceScale,
                        sourceRotation,
                        desiredLocal.Translation);
                    continue;
                }

                var scaleSignHint = sourceFrame.Scale[selectedBone];
                if (delta.Kind == BoneTransformDeltaKind.Scale &&
                    !HasSelectedAncestor(selectedBone))
                {
                    scaleSignHint = BoneTransformMath.MultiplyComponents(
                        scaleSignHint,
                        delta.ScaleFactor);
                }

                if (!BoneTransformMath.TryDecomposeSignedTrs(
                        desiredLocal,
                        scaleSignHint,
                        out var scale,
                        out var rotation,
                        out var position))
                {
                    return false;
                }

                candidateComponents[selectedBone] = (scale, rotation, position);
            }

            var modifiedFrame = sourceFrame.Clone();
            foreach (var selectedBone in orderedSelection)
            {
                var components = candidateComponents[selectedBone];
                modifiedFrame.Scale[selectedBone] = components.Scale;
                modifiedFrame.Rotation[selectedBone] = components.Rotation;
                modifiedFrame.Position[selectedBone] = components.Position;
            }

            _animation.DynamicFrames[_currentFrame] = modifiedFrame;
            PublishModified();
            return true;

            bool HasSelectedAncestor(int boneIndex)
            {
                var parentIndex = skeleton.GetParentBoneIndex(boneIndex);
                while (parentIndex >= 0)
                {
                    if (selectedBones.Contains(parentIndex))
                        return true;
                    parentIndex = skeleton.GetParentBoneIndex(parentIndex);
                }

                return false;
            }

            int GetBoneDepth(int boneIndex)
            {
                var depth = 0;
                var parentIndex = skeleton.GetParentBoneIndex(boneIndex);
                while (parentIndex >= 0 && depth <= boneCount)
                {
                    depth++;
                    parentIndex = skeleton.GetParentBoneIndex(parentIndex);
                }

                return depth;
            }
        }

        //TODO: FIX ME
        void ApplyTransformationInverseKinematic(Matrix newPosition, int startBone, int endBone)
        {
            var node = _boneSelectionState.RenderObject as Rmv2MeshNode;
            var animationPlayer = node.AnimationPlayer;
            var currentAnimFrame = animationPlayer.GetCurrentAnimationFrame();

            // Get the chain of bones from startBone to endBone
            var boneCount = 1;
            var boneIndex = startBone;
            while (boneIndex != endBone)
            {
                boneIndex = currentAnimFrame.GetParentBoneIndex(_boneSelectionState.Skeleton, boneIndex);
                boneCount++;
            }
            boneIndex = startBone;
            var positions = new Vector3[boneCount];
            var rotations = new Quaternion[boneCount];
            var boneLengths = new float[boneCount - 1];
            var boneIndices = new int[boneCount];
            float totalLength = 0;


            for (var i = 0; i < boneCount; i++)
            {
                var transform = Matrix.CreateScale(1);
                // Get the current bone world transform
                var currentBoneWorldTransform = currentAnimFrame.GetSkeletonAnimatedWorld(_boneSelectionState.Skeleton, boneIndex);

                // Store the position and rotation of the current bone
                positions[i] = currentBoneWorldTransform.Translation;
                currentBoneWorldTransform.Decompose(out _, out var rotation, out _);
                rotations[i] = rotation;
                boneIndices[i] = boneIndex;

                // Calculate the length of the bone and add it to the total length
                if (i < boneCount - 1)
                {
                    boneLengths[i] = Vector3.Distance(positions[i], positions[i + 1]);
                    totalLength += boneLengths[i];
                }

                // Move to the next bone in the chain
                boneIndex = currentAnimFrame.GetParentBoneIndex(_boneSelectionState.Skeleton, boneIndex);
            }

            // Check if the target is reachable
            if (Vector3.Distance(newPosition.Translation, positions[0]) > totalLength)
            {
                // The target is unreachable, move the end effector towards the target
                for (var i = boneCount - 2; i >= 0; i--)
                {
                    positions[i + 1] = newPosition.Translation;
                    var direction = Vector3.Normalize(positions[i] - positions[i + 1]);
                    positions[i] = positions[i + 1] + direction * boneLengths[i];
                }
            }
            else
            {
                // The target is reachable, apply the FABRIK algorithm
                var rootPosition = positions[0];
                var tolerance = 0.01f;
                while (Vector3.Distance(newPosition.Translation, positions[boneCount - 1]) > tolerance)
                {
                    // Stage 1: Forward reaching
                    ForwardReaching(positions, boneLengths, newPosition.Translation, boneCount - 2);

                    // Stage 2: Backward reaching
                    BackwardReaching(positions, boneLengths, rootPosition, 0);
                }

                // Update the position and rotation of each bone in the chain
                for (var i = 0; i < boneCount - 1; i++)
                {
                    _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Position[boneIndices[i]] = positions[i];
                    continue;
                    //if (i < boneCount - 1)
                    //{
                    //    var direction = Vector3.Normalize(positions[i + 1] - positions[i]);
                    //    var rotation = Quaternion.CreateFromAxisAngle(Vector3.Cross(Vector3.UnitX, direction), (float)Math.Acos(Vector3.Dot(Vector3.UnitX, direction)));
                    //    _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Rotation[boneIndices[i]] = rotation;
                    //}
                }
            }

        }

        //TODO: FIX ME
        private void ForwardReaching(Vector3[] positions, float[] boneLengths, Vector3 targetPosition, int index)
        {
            if (index < 0) return;

            positions[index + 1] = targetPosition;
            var direction = Vector3.Normalize(positions[index] - positions[index + 1]);
            positions[index] = positions[index + 1] + direction * boneLengths[index];

            ForwardReaching(positions, boneLengths, positions[index], index - 1);
        }

        //TODO: FIX ME
        private void BackwardReaching(Vector3[] positions, float[] boneLengths, Vector3 rootPosition, int index)
        {
            if (index >= positions.Length - 1) return;

            positions[index] = rootPosition;
            var direction = Vector3.Normalize(positions[index + 1] - positions[index]);
            positions[index + 1] = positions[index] + direction * boneLengths[index];

            BackwardReaching(positions, boneLengths, positions[index + 1], index + 1);
        }

        internal bool HasFrameMutation()
        {
            if (_oldFrame == null || _animation == null)
                return false;

            var currentFrame = _animation.DynamicFrames[_currentFrame];
            const float epsilon = 0.00001f;
            foreach (var selectedBone in _selectedBones)
            {
                if (Vector3.DistanceSquared(
                        currentFrame.Position[selectedBone],
                        _oldFrame.Position[selectedBone]) >
                    epsilon * epsilon ||
                    Vector3.DistanceSquared(
                        currentFrame.Scale[selectedBone],
                        _oldFrame.Scale[selectedBone]) >
                    epsilon * epsilon)
                {
                    return true;
                }

                var currentRotation = currentFrame.Rotation[selectedBone];
                var oldRotation = _oldFrame.Rotation[selectedBone];
                currentRotation.Normalize();
                oldRotation.Normalize();
                if (1 - Math.Abs(Quaternion.Dot(currentRotation, oldRotation)) > epsilon)
                    return true;
            }

            return false;
        }

        public void Undo()
        {
            if (_oldFrame == null) return;
            _animation.DynamicFrames[_currentFrame] = _oldFrame.Clone();
            PublishModified();
        }

        public void Redo()
        {
            if (_newFrame == null) return;
            _animation.DynamicFrames[_currentFrame] = _newFrame.Clone();
            PublishModified();
        }

        internal void RestoreInitialFrame()
        {
            if (_oldFrame == null) return;
            _animation.DynamicFrames[_currentFrame] = _oldFrame.Clone();
            PublishModified();
        }

        public void Execute()
        {
            _newFrame ??= _animation.DynamicFrames[_currentFrame].Clone();
        }

        private void PublishModified()
        {
            if (_boneSelectionState.RenderObject is
                Rmv2MeshNode meshNode)
            {
                meshNode.AnimationPlayer?.Refresh();
            }

            _boneSelectionState.TriggerModifiedBoneEvent(
                (BoneSelectionState)_selectionSnapshot.Clone(),
                _selectedBones);
        }
    }
}
