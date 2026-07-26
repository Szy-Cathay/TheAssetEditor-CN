using System;
using System.Collections.Generic;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Gizmo;
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
        public Matrix Transform { get; set; }

        public string HintText => "Bone Transform";

        public bool IsMutation => true;

        private Matrix _oldTransform = Matrix.Identity;

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
            _oldTransform = Matrix.Identity;
        }

        public bool ApplyTransformation(Matrix newTransform, GizmoMode gizmoMode)
        {
            if (_selectedBones.Count == 0)
                return false;

            //TODO: FIX ME
            //if(_boneSelectionState.EnableInverseKinematics)
            //{
            //    ApplyTransformationInverseKinematic(newPosition, _selectedBones[0], _boneSelectionState.InverseKinematicsEndBoneIndex);
            //    return;
            //}
            if (!_oldTransform.Decompose(
                    out var oldScale,
                    out var oldRotation,
                    out var oldTranslation) ||
                !newTransform.Decompose(
                    out var newScale,
                    out var newRotation,
                    out var newTranslation))
            {
                return false;
            }

            var translationDelta = newTranslation - oldTranslation;
            var rotationDelta = Quaternion.Inverse(oldRotation) * newRotation;
            rotationDelta.Normalize();
            if (gizmoMode is GizmoMode.NonUniformScale or GizmoMode.UniformScale &&
                (Math.Abs(oldScale.X) < 0.000001f ||
                 Math.Abs(oldScale.Y) < 0.000001f ||
                 Math.Abs(oldScale.Z) < 0.000001f))
            {
                return false;
            }

            var scaleDelta = new Vector3(
                newScale.X / oldScale.X,
                newScale.Y / oldScale.Y,
                newScale.Z / oldScale.Z);
            _oldTransform = newTransform;

            var isNoOp = gizmoMode switch
            {
                GizmoMode.Translate => translationDelta == Vector3.Zero,
                GizmoMode.Rotate =>
                    Math.Abs(Quaternion.Dot(rotationDelta, Quaternion.Identity)) >
                    0.999999f,
                GizmoMode.NonUniformScale or GizmoMode.UniformScale =>
                    scaleDelta == Vector3.One,
                _ => throw new InvalidOperationException("unknown gizmo mode")
            };
            if (isNoOp)
                return false;

            var modifiedFrame = _animation.DynamicFrames[_currentFrame].Clone();
            foreach (var selectedBone in _selectedBones)
            {
                switch (gizmoMode)
                {
                    case GizmoMode.Translate:
                        modifiedFrame.Position[selectedBone] += translationDelta;
                        break;
                    case GizmoMode.Rotate:
                        modifiedFrame.Rotation[selectedBone] *= rotationDelta;
                        modifiedFrame.Rotation[selectedBone].Normalize();
                        break;
                    case GizmoMode.NonUniformScale:
                    case GizmoMode.UniformScale:
                        modifiedFrame.Scale[selectedBone] = new Vector3(
                            modifiedFrame.Scale[selectedBone].X * scaleDelta.X,
                            modifiedFrame.Scale[selectedBone].Y * scaleDelta.Y,
                            modifiedFrame.Scale[selectedBone].Z * scaleDelta.Z);
                        break;
                }
            }

            _animation.DynamicFrames[_currentFrame] = modifiedFrame;
            PublishModified();
            return true;
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
            _oldTransform = Matrix.Identity;
            PublishModified();
        }

        public void Execute()
        {
            _newFrame ??= _animation.DynamicFrames[_currentFrame].Clone();
        }

        private void PublishModified()
        {
            _boneSelectionState.TriggerModifiedBoneEvent(
                (BoneSelectionState)_selectionSnapshot.Clone(),
                _selectedBones);
        }
    }
}
