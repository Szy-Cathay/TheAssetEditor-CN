using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace AnimationEditor.CampaignAnimationCreator.Commands
{
    public class ConvertCampaignAnimationCommand
    {
        private readonly IStandardDialogs _standardDialogs;
        private readonly LocalizationManager _localizationManager;

        public ConvertCampaignAnimationCommand(
            IStandardDialogs standardDialogs,
            LocalizationManager localizationManager)
        {
            _standardDialogs = standardDialogs;
            _localizationManager = localizationManager;
        }

        public bool Execute(
            AnimationClip? sourceAnimation,
            SkeletonBoneNode? rootBone,
            out AnimationClip? convertedAnimation,
            GameSkeleton? skeleton = null)
        {
            convertedAnimation = null;

            if (sourceAnimation == null)
            {
                ShowError("CampaignAnim.Error.NoAnimation");
                return false;
            }

            if (rootBone == null)
            {
                ShowError("CampaignAnim.Error.NoRootBone");
                return false;
            }

            var animationCopy = sourceAnimation.Clone();
            if (animationCopy.DynamicFrames.Count == 0)
            {
                ShowError("CampaignAnim.Error.NoFrames");
                return false;
            }

            for (var frameIndex = 0; frameIndex < animationCopy.DynamicFrames.Count; frameIndex++)
            {
                var frame = animationCopy.DynamicFrames[frameIndex];
                if (frame.Position.Count != frame.Rotation.Count || frame.Rotation.Count != frame.Scale.Count)
                {
                    ShowError("CampaignAnim.Error.IncompleteFrame", frameIndex + 1);
                    return false;
                }

                if (rootBone.BoneIndex < 0 || rootBone.BoneIndex >= frame.Position.Count)
                {
                    ShowError(
                        "CampaignAnim.Error.BoneOutOfRange",
                        frameIndex + 1,
                        rootBone.BoneIndex);
                    return false;
                }
            }

            var coordinateCorrection = GetFixedCoordinateCorrection(animationCopy, rootBone.BoneIndex);
            int[] childBoneIndices = [];
            if (coordinateCorrection != null)
            {
                if (skeleton == null)
                {
                    ShowError("CampaignAnim.Error.NoSkeletonForCorrection");
                    return false;
                }

                if (rootBone.BoneIndex >= skeleton.BoneCount)
                {
                    ShowError("CampaignAnim.Error.BoneOutOfRange", 1, rootBone.BoneIndex);
                    return false;
                }

                // Coordinate bases belong to top-level roots, not ordinary pose bones.
                if (skeleton.GetParentBoneIndex(rootBone.BoneIndex) != -1)
                {
                    coordinateCorrection = null;
                }
                else
                {
                    for (var frameIndex = 0; frameIndex < animationCopy.DynamicFrames.Count; frameIndex++)
                    {
                        if (animationCopy.DynamicFrames[frameIndex].Position.Count != skeleton.BoneCount)
                        {
                            ShowError("CampaignAnim.Error.IncompleteFrame", frameIndex + 1);
                            return false;
                        }
                    }

                    childBoneIndices = Enumerable.Range(0, skeleton.BoneCount)
                        .Where(index => skeleton.GetParentBoneIndex(index) == rootBone.BoneIndex)
                        .ToArray();
                }
            }

            foreach (var frame in animationCopy.DynamicFrames)
            {
                if (coordinateCorrection is Quaternion correction)
                {
                    var correctionMatrix = Matrix.CreateFromQuaternion(correction);
                    foreach (var childBoneIndex in childBoneIndices)
                    {
                        frame.Position[childBoneIndex] = Vector3.Transform(frame.Position[childBoneIndex], correction);
                        var childRotation = Matrix.CreateFromQuaternion(frame.Rotation[childBoneIndex]) * correctionMatrix;
                        frame.Rotation[childBoneIndex] = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(childRotation));
                    }
                }

                frame.Position[rootBone.BoneIndex] = Vector3.Zero;
                frame.Rotation[rootBone.BoneIndex] = Quaternion.Identity;
            }

            convertedAnimation = animationCopy;
            return true;
        }

        private static Quaternion? GetFixedCoordinateCorrection(AnimationClip animation, int boneIndex)
        {
            var rotation = animation.DynamicFrames[0].Rotation[boneIndex];
            if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() <= float.Epsilon)
                return null;

            rotation.Normalize();
            var basis = Matrix.CreateFromQuaternion(rotation);
            // Preserve fixed axis permutations that change the up axis; remove heading as before.
            if (basis.Up.Y >= 1 - 0.00001f || !IsAxisAligned(basis.Right) ||
                !IsAxisAligned(basis.Up) || !IsAxisAligned(basis.Backward))
            {
                return null;
            }

            foreach (var frame in animation.DynamicFrames)
            {
                var frameRotation = frame.Rotation[boneIndex];
                if (!float.IsFinite(frameRotation.LengthSquared()) || frameRotation.LengthSquared() <= float.Epsilon)
                    return null;

                frameRotation.Normalize();
                // q and -q represent the same rotation; allow animation-file quantization noise.
                if (MathF.Min((frameRotation - rotation).LengthSquared(), (frameRotation + rotation).LengthSquared()) > 0.00000001f)
                    return null;
            }

            return rotation;
        }

        private static bool IsAxisAligned(Vector3 axis) =>
            MathF.Max(MathF.Abs(axis.X), MathF.Max(MathF.Abs(axis.Y), MathF.Abs(axis.Z))) >= 1 - 0.00001f;

        private void ShowError(string localizationKey, params object[] args)
        {
            var message = args.Length == 0
                ? _localizationManager.Get(localizationKey)
                : _localizationManager.GetFormat(localizationKey, args);
            _standardDialogs.ShowDialogBox(message, _localizationManager.Get("Msg.GeneralError"));
        }
    }
}
