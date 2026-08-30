using Editors.AnimatioReTarget.Editor.BoneHandling;
using System.IO;
using Editors.AnimatioReTarget.Editor.Settings;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;

namespace Editors.AnimatioReTarget.Editor
{
    public class AnimationRemapperService
    {
        private readonly IEnumerable<SkeletonBoneNode_new> _bones;
        private readonly AnimationGenerationSettings _settings;

        public AnimationRemapperService(AnimationGenerationSettings settings, IEnumerable<SkeletonBoneNode_new> bones)
        {
            _settings = settings;
            _bones = bones;
        }

        public AnimationClip ReMapAnimation(GameSkeleton copyFromSkeleton, GameSkeleton copyToSkeleton, AnimationClip animationToCopy)
        {
            var speedMultiplier = _settings.AnimationSpeedMult;
            if (!float.IsFinite(speedMultiplier) || speedMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(_settings.AnimationSpeedMult), "Animation speed multiplier must be greater than zero.");
            if (!float.IsFinite(_settings.SkeletonScale) ||
                _settings.SkeletonScale <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_settings.SkeletonScale),
                    GetLocalizedText(
                        "AnimReTarget.Error.InvalidSkeletonScale",
                        "Skeleton scale must be greater than zero."));
            }

            var invalidBoneLength = EnumerateBones(_bones).FirstOrDefault(
                bone => !float.IsFinite(bone.BoneLengthMult) ||
                        bone.BoneLengthMult <= 0);
            if (invalidBoneLength != null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(invalidBoneLength.BoneLengthMult),
                    GetLocalizedText(
                        "AnimReTarget.Error.InvalidBoneLengthMultiplier",
                        "Bone length multiplier must be greater than zero."));
            }

            var originalFrameCount = animationToCopy.DynamicFrames.Count;
            if (originalFrameCount == 0)
            {
                throw new InvalidDataException(GetLocalizedText(
                    "AnimReTarget.Error.EmptySourceAnimation",
                    "Source animation must contain at least one frame."));
            }
            var sourceTimebase = animationToCopy.Timebase ??
                throw new InvalidDataException(
                    GetLocalizedText(
                        "AnimReTarget.Error.InvalidSourceDuration",
                        "Source animation must have a positive duration."));
            var targetTimebase = sourceTimebase.WithPlaybackSpeed(
                speedMultiplier);
            var newFrameCount = targetTimebase.FrameCount;

            //animationToCopy.RemoveOptimizations(copyFromSkeleton);
            var resampledAnimationToCopy = originalFrameCount == 1
                ? animationToCopy.Clone()
                : GameWorld.Core.Animation.AnimationEditor.ReSample(
                    copyFromSkeleton,
                    animationToCopy,
                    newFrameCount,
                    targetTimebase.Duration);
            resampledAnimationToCopy.Duration = targetTimebase.Duration;
            var newAnimation = CreateNewAnimation(copyToSkeleton, resampledAnimationToCopy);

            if (!HaveEquivalentSkeletonDefinitions(
                    copyFromSkeleton,
                    copyToSkeleton))
                TransferAnimationWorld(copyFromSkeleton, copyToSkeleton, resampledAnimationToCopy, newAnimation);
            else
                newAnimation = resampledAnimationToCopy;

            if (_settings.ApplyRelativeScale)
                ApplyRelativeScale(copyFromSkeleton, copyToSkeleton, newAnimation);

            // Apply the "rules"
            SnapBonesToWorld(copyFromSkeleton, copyToSkeleton, newAnimation, resampledAnimationToCopy);
            FreezeBones(copyToSkeleton, newAnimation);
            ApplyOffsets(copyToSkeleton, newAnimation);
            FixAttachmentPoints(copyFromSkeleton, copyToSkeleton, newAnimation, resampledAnimationToCopy);
            ApplyAnimationScale(newAnimation, copyToSkeleton);
            ApplyBoneLengthMult(newAnimation, copyToSkeleton);

            return newAnimation;
        }

        private static string GetLocalizedText(string key, string fallback) =>
            LocalizationManager.Instance == null
                ? fallback
                : LocalizationManager.Instance.Get(key);

        private static IEnumerable<SkeletonBoneNode_new> EnumerateBones(
            IEnumerable<SkeletonBoneNode_new> roots)
        {
            foreach (var bone in roots)
            {
                yield return bone;
                foreach (var child in EnumerateBones(bone.Children))
                    yield return child;
            }
        }

        private static bool HaveEquivalentSkeletonDefinitions(
            GameSkeleton source,
            GameSkeleton target)
        {
            if (ReferenceEquals(source, target))
                return true;
            if (source.BoneCount != target.BoneCount)
                return false;

            for (var boneIndex = 0; boneIndex < source.BoneCount; boneIndex++)
            {
                if (!string.Equals(
                        source.BoneNames[boneIndex],
                        target.BoneNames[boneIndex],
                        StringComparison.OrdinalIgnoreCase) ||
                    source.GetParentBoneIndex(boneIndex) !=
                    target.GetParentBoneIndex(boneIndex) ||
                    Vector3.Distance(
                        source.Translation[boneIndex],
                        target.Translation[boneIndex]) > 0.00001f)
                {
                    return false;
                }

                var sourceRotation = Quaternion.Normalize(
                    source.Rotation[boneIndex]);
                var targetRotation = Quaternion.Normalize(
                    target.Rotation[boneIndex]);
                if (1 - MathF.Abs(Quaternion.Dot(
                        sourceRotation,
                        targetRotation)) > 0.00001f)
                {
                    return false;
                }
            }

            return true;
        }


        void TransferAnimationWorld(GameSkeleton copyFromSkeleton, GameSkeleton copyToSkeleton, AnimationClip animationToCopy, AnimationClip newAnimation)
        {
            var frameCount = animationToCopy.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var copyFromFrame = AnimationSampler.Sample(
                    frameIndex,
                    0,
                    copyFromSkeleton,
                    animationToCopy);
                var targetFrame = newAnimation.DynamicFrames[frameIndex];
                for (var i = 0; i < copyToSkeleton.BoneCount; i++)
                {
                    var mappedIndex = BoneHelper_new.GetMappedIndex(_bones, i);
                    if (mappedIndex == null)
                        continue;

                    var targetBoneIndex = mappedIndex.Value;
                    var desiredBonePosWorld = RetargetWorldTransform(
                        copyFromSkeleton,
                        copyToSkeleton,
                        copyFromFrame,
                        targetBoneIndex,
                        i);

                    var fromParentBoneIndex = copyToSkeleton.GetParentBoneIndex(i);
                    if (fromParentBoneIndex != -1)
                    {
                        var parentWorld = GetAnimatedWorldTransform(
                            copyToSkeleton,
                            targetFrame,
                            fromParentBoneIndex);
                        desiredBonePosWorld = desiredBonePosWorld * Matrix.Invert(parentWorld);
                    }

                    desiredBonePosWorld.Decompose(out var _, out var boneRotation, out var bonePosition);

                    var boneSettings = BoneHelper_new.GetBoneFromId(_bones, i);
                    if (boneSettings == null)
                        continue;
                    if (boneSettings.ApplyRotation == true)
                        targetFrame.Rotation[i] = boneRotation;
                    if (boneSettings.ApplyTranslation == true)
                        targetFrame.Position[i] = bonePosition;

                }
            }
        }

        void ApplyRelativeScale(GameSkeleton copyFromSkeleton, GameSkeleton copyToSkeleton, AnimationClip animationToScale)
        {
            var frameCount = animationToScale.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                for (var i = 0; i < copyToSkeleton.BoneCount; i++)
                {
                    var boneSettings = BoneHelper_new.GetBoneFromId(_bones, i);
                    var mappedIndex = BoneHelper_new.GetMappedIndex(_bones, i);

                    if (mappedIndex != null)
                    {
                        var targetBoneIndex = mappedIndex.Value;
                        var copyFromParentIndex = copyFromSkeleton.GetParentBoneIndex(targetBoneIndex);
                        var copyToParentIndex = copyToSkeleton.GetParentBoneIndex(i);

                        if (copyToParentIndex != -1 && copyFromParentIndex != -1)
                        {
                            var toBone0 = copyToSkeleton.GetWorldTransform(i).Translation;
                            var toBone1 = copyToSkeleton.GetWorldTransform(copyToParentIndex).Translation;
                            var targetBoneLength = Vector3.Distance(toBone0, toBone1);

                            var fromBone0 = copyFromSkeleton.GetWorldTransform(targetBoneIndex).Translation;
                            var fromBone1 = copyFromSkeleton.GetWorldTransform(copyFromParentIndex).Translation;
                            var fromBoneLength = Vector3.Distance(fromBone0, fromBone1);

                            if (fromBoneLength == 0 || targetBoneLength == 0)
                            {
                                targetBoneLength = 1;
                                fromBoneLength = 1;
                            }

                            var relativeScale = targetBoneLength / fromBoneLength;
                            var targetBindTranslation = copyToSkeleton.Translation[i];
                            var animationTranslationDelta =
                                animationToScale.DynamicFrames[frameIndex].Position[i] -
                                targetBindTranslation;
                            animationToScale.DynamicFrames[frameIndex].Position[i] =
                                targetBindTranslation +
                                animationTranslationDelta * relativeScale;
                        }
                    }
                }
            }
        }

        void SnapBonesToWorld(GameSkeleton copyFromSkeleton, GameSkeleton copyToSkeleton, AnimationClip animationToScale, AnimationClip animationToCopy)
        {
            var frameCount = animationToScale.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var copyFromFrame = AnimationSampler.Sample(frameIndex, 0, copyFromSkeleton, animationToCopy);
                var targetFrame = animationToScale.DynamicFrames[frameIndex];

                for (var i = 0; i < copyToSkeleton.BoneCount; i++)
                {
                    var boneSettings = BoneHelper_new.GetBoneFromId(_bones, i);
                    if (boneSettings == null)
                        continue;
                    if (boneSettings.ForceSnapToWorld == false)
                        continue;

                    var mappedIndex = BoneHelper_new.GetMappedIndex(_bones, i);
                    if (mappedIndex == null)
                        continue;

                    var fromParentBoneIndex = copyToSkeleton.GetParentBoneIndex(i);
                    if (fromParentBoneIndex == -1)
                        continue;

                    var targetBoneIndex = mappedIndex.Value;
                    var desiredBonePosWorld = RetargetWorldTransform(
                        copyFromSkeleton,
                        copyToSkeleton,
                        copyFromFrame,
                        targetBoneIndex,
                        i);

                    var parentWorld = GetAnimatedWorldTransform(
                        copyToSkeleton,
                        targetFrame,
                        fromParentBoneIndex);

                    var bonePositionLocalSpace = desiredBonePosWorld * Matrix.Invert(parentWorld);
                    bonePositionLocalSpace.Decompose(out var _, out var boneRotation, out var bonePosition);

                    // Apply the values to the animation
                    targetFrame.Rotation[i] = boneRotation;
                    targetFrame.Position[i] = bonePosition;
                }
            }
        }

        static Matrix RetargetWorldTransform(
            GameSkeleton sourceSkeleton,
            GameSkeleton targetSkeleton,
            AnimationFrame sourceFrame,
            int sourceBoneIndex,
            int targetBoneIndex)
        {
            var sourceBindWorld = sourceSkeleton.GetWorldTransform(sourceBoneIndex);
            var sourceAnimatedWorld = sourceFrame.GetSkeletonAnimatedWorld(
                sourceSkeleton,
                sourceBoneIndex);
            var sourceAnimationDelta = Matrix.Invert(sourceBindWorld) * sourceAnimatedWorld;
            return targetSkeleton.GetWorldTransform(targetBoneIndex) * sourceAnimationDelta;
        }

        static Matrix GetAnimatedWorldTransform(
            GameSkeleton skeleton,
            AnimationClip.KeyFrame frame,
            int boneIndex)
        {
            var worldTransform = GetLocalTransform(frame, boneIndex);
            var parentBoneIndex = skeleton.GetParentBoneIndex(boneIndex);
            while (parentBoneIndex != -1)
            {
                worldTransform *= GetLocalTransform(frame, parentBoneIndex);
                parentBoneIndex = skeleton.GetParentBoneIndex(parentBoneIndex);
            }

            return worldTransform;
        }

        static Matrix GetLocalTransform(AnimationClip.KeyFrame frame, int boneIndex) =>
            Matrix.CreateScale(frame.Scale[boneIndex]) *
            Matrix.CreateFromQuaternion(frame.Rotation[boneIndex]) *
            Matrix.CreateTranslation(frame.Position[boneIndex]);

        void ApplyOffsets(GameSkeleton copyToSkeleton, AnimationClip animationToScale)
        {
            var frameCount = animationToScale.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var targetFrame = animationToScale.DynamicFrames[frameIndex];
                for (var i = 0; i < copyToSkeleton.BoneCount; i++)
                {
                    var fromParentBoneIndex = copyToSkeleton.GetParentBoneIndex(i);
                    if (fromParentBoneIndex == -1)
                        continue;

                    var boneSettings = BoneHelper_new.GetBoneFromId(_bones, i);
                    if (boneSettings == null)
                        continue;

                    var desiredBonePosWorld = MathUtil.CreateRotation(new Vector3((float)boneSettings.RotationOffset.X.Value, (float)boneSettings.RotationOffset.Y.Value, (float)boneSettings.RotationOffset.Z.Value)) *
                        GetAnimatedWorldTransform(copyToSkeleton, targetFrame, i) *
                        Matrix.CreateTranslation(new Vector3((float)boneSettings.TranslationOffset.X.Value, (float)boneSettings.TranslationOffset.Y.Value, (float)boneSettings.TranslationOffset.Z.Value));

                    var parentWorld = GetAnimatedWorldTransform(
                        copyToSkeleton,
                        targetFrame,
                        fromParentBoneIndex);
                    var bonePositionLocalSpace = desiredBonePosWorld * Matrix.Invert(parentWorld);
                    bonePositionLocalSpace.Decompose(out var _, out var boneRotation, out var bonePosition);

                    targetFrame.Rotation[i] = boneRotation;
                    targetFrame.Position[i] = bonePosition;

                    if (boneSettings.IsLocalOffset)
                    {
                        // Todo - Some inverse fuckery to children
                        var childBones = copyToSkeleton.GetDirectChildBones(i);
                    }
                }
            }
        }

        void FixAttachmentPoints(GameSkeleton copyFromSkeleton, GameSkeleton copyToSkeleton, AnimationClip animationToFix, AnimationClip animationToCopy)
        {
            var frameCount = animationToCopy.DynamicFrames.Count;

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var copyFromFrame = AnimationSampler.Sample(
                    frameIndex,
                    0,
                    copyFromSkeleton,
                    animationToCopy);
                var targetFrame = animationToFix.DynamicFrames[frameIndex];
                for (var i = 0; i < copyToSkeleton.BoneCount; i++)
                {
                    // Does this bone have a thing to fix?
                    var boneSettings = BoneHelper_new.GetBoneFromId(_bones, i);
                    if (boneSettings == null)
                        continue;

                    var mappedIndex = BoneHelper_new.GetMappedIndex(_bones, i);
                    if (boneSettings.SelectedRelativeBone == null || mappedIndex == null)
                        continue;

                    var fromParentBoneIndex = copyToSkeleton.GetParentBoneIndex(i);
                    var boneIndexHandSelf = boneSettings.SelectedRelativeBone.BoneIndex;
                    if (fromParentBoneIndex == -1 ||
                        boneIndexHandSelf == i ||
                        boneIndexHandSelf < 0 ||
                        boneIndexHandSelf >= copyToSkeleton.BoneCount ||
                        mappedIndex.Value < 0 ||
                        mappedIndex.Value >= copyFromSkeleton.BoneCount)
                    {
                        continue;
                    }

                    // self attach - The attachment point to move | copyToSkeleton -> boneIndex i
                    // target attach - the attachment point to move to |  copyFromSkeleton -> boneIndex targetBoneIndex
                    // self hand - Reference point | copyToSkeleton -> boneIndex SelectedRelativeBone.index
                    // target hand-  reference point | copyFromSkeleton -> boneIndex self hand mapping index

                    var boneIndexAttachmentPointSelf = i;
                    var boneIndexAttachmentPointSource = mappedIndex.Value;
                    var mappedIndexRef = BoneHelper_new.GetMappedIndex(_bones, boneIndexHandSelf);
                    if (mappedIndexRef == null ||
                        mappedIndexRef.Value < 0 ||
                        mappedIndexRef.Value >= copyFromSkeleton.BoneCount)
                        continue;

                    var boneIndexHandSource = mappedIndexRef.Value;


                    var self = copyFromFrame.GetSkeletonAnimatedWorld(copyFromSkeleton, boneIndexAttachmentPointSource);
                    var hand = copyFromFrame.GetSkeletonAnimatedWorld(copyFromSkeleton, boneIndexHandSource);

                    var sourceRelativeTransform = self * Matrix.Invert(hand);
                    sourceRelativeTransform.Decompose(
                        out var _,
                        out var _,
                        out var sourceRelativePosition);

                    var desiredBonePosWorld = GetAnimatedWorldTransform(
                        copyToSkeleton,
                        targetFrame,
                        boneIndexHandSelf);

                    desiredBonePosWorld = Matrix.CreateTranslation(
                                              sourceRelativePosition) *
                                          desiredBonePosWorld;

                    // Reapply offsets
                    desiredBonePosWorld = MathUtil.CreateRotation(new Vector3((float)boneSettings.RotationOffset.X.Value, (float)boneSettings.RotationOffset.Y.Value, (float)boneSettings.RotationOffset.Z.Value)) *
                        desiredBonePosWorld *
                        Matrix.CreateTranslation(new Vector3((float)boneSettings.TranslationOffset.X.Value, (float)boneSettings.TranslationOffset.Y.Value, (float)boneSettings.TranslationOffset.Z.Value));

                    //   desiredBonePosWorld = copyFromFrame.GetSkeletonAnimatedWorld(copyFromSkeleton, targetBoneIndex) * Matrix.CreateScale(1);


                    var parentWorld = GetAnimatedWorldTransform(
                        copyToSkeleton,
                        targetFrame,
                        fromParentBoneIndex);

                    var bonePositionLocalSpace = desiredBonePosWorld * Matrix.Invert(parentWorld);
                    bonePositionLocalSpace.Decompose(out var _, out var boneRotation, out var bonePosition);

                    //animationToFix.DynamicFrames[frameIndex].Rotation[i] = boneRotation;
                    targetFrame.Position[i] = bonePosition;
                }
            }
        }

        void ApplyAnimationScale(AnimationClip animation, GameSkeleton copyToSkeleton)
        {
            var frameCount = animation.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                animation.DynamicFrames[frameIndex].Scale[0] = new Vector3((float)_settings.SkeletonScale);
            }


        }

        void ApplyBoneLengthMult(AnimationClip animation, GameSkeleton copyToSkeleton)
        {
            var frameCount = animation.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                for (var boneIndex = 0; boneIndex < copyToSkeleton.BoneCount; boneIndex++)
                {
                    var boneSettings = BoneHelper_new.GetBoneFromId(_bones, boneIndex);
                    if (boneSettings == null)
                        continue;

                    animation.DynamicFrames[frameIndex].Position[boneIndex] = animation.DynamicFrames[frameIndex].Position[boneIndex] * (float)boneSettings.BoneLengthMult;
                }
            }
        }

        void FreezeBones(GameSkeleton copyToSkeleton, AnimationClip animation)
        {
            var frameCount = animation.DynamicFrames.Count;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                for (var i = 0; i < copyToSkeleton.BoneCount; i++)
                {
                    var mappedIndex = BoneHelper_new.GetMappedIndex(_bones, i);
                    if (mappedIndex != null)
                    {
                        var boneSettings = BoneHelper_new.GetBoneFromId(_bones, i);
                        if (boneSettings == null)
                            continue;

                        if (boneSettings.FreezeTranslation)
                            animation.DynamicFrames[frameIndex].Position[i] = Vector3.Zero;

                        if (boneSettings.FreezeRotation)
                            animation.DynamicFrames[frameIndex].Rotation[i] = Quaternion.Identity;
                        if (boneSettings.FreezeRotationZ)
                        {
                            var currentRotation = Quaternion.Normalize(animation.DynamicFrames[frameIndex].Rotation[i]);
                            var currentTwist = ExtractTwistAroundZ(currentRotation);
                            var currentSwing = currentRotation * Quaternion.Inverse(currentTwist);
                            var firstFrameTwist = ExtractTwistAroundZ(animation.DynamicFrames[0].Rotation[i]);
                            animation.DynamicFrames[frameIndex].Rotation[i] = Quaternion.Normalize(currentSwing * firstFrameTwist);
                        }
                    }
                    else
                    {
                        if (_settings.ZeroUnmappedBones)
                        {
                            animation.DynamicFrames[frameIndex].Rotation[i] = Quaternion.Identity;
                            animation.DynamicFrames[frameIndex].Position[i] = Vector3.Zero;
                        }
                    }


                }
            }
        }

        static Quaternion ExtractTwistAroundZ(Quaternion rotation)
        {
            var lengthSquared = rotation.Z * rotation.Z + rotation.W * rotation.W;
            if (lengthSquared <= float.Epsilon)
                return Quaternion.Identity;

            var inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
            return new Quaternion(0, 0, rotation.Z * inverseLength, rotation.W * inverseLength);
        }


        AnimationClip CreateNewAnimation(GameSkeleton skeleton, AnimationClip animationToCopy)
        {
            var frameCount = animationToCopy.DynamicFrames.Count;

            var newAnimation = new AnimationClip();
            newAnimation.Duration = animationToCopy.Duration;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                newAnimation.DynamicFrames.Add(new AnimationClip.KeyFrame());
                for (var i = 0; i < skeleton.BoneCount; i++)
                {
                    newAnimation.DynamicFrames[frameIndex].Rotation.Add(skeleton.Rotation[i]);
                    newAnimation.DynamicFrames[frameIndex].Position.Add(skeleton.Translation[i]);
                    newAnimation.DynamicFrames[frameIndex].Scale.Add(Vector3.One);
                }
            }

            for (var i = 0; i < skeleton.BoneCount; i++)
            {
                if (newAnimation.DynamicFrames.Count != 0)
                    newAnimation.DynamicFrames[0].Scale[0] = Vector3.One;
            }
            return newAnimation;
        }
    }

    public static class BoneHelper_new
    {
        public static SkeletonBoneNode_new? GetBoneFromId(IEnumerable<SkeletonBoneNode_new> root, int boneId)
        {
            foreach (SkeletonBoneNode_new item in root)
            {
                if (item.BoneIndex == boneId)
                    return item;

                var result = GetBoneFromId(item.Children, boneId);
                if (result != null)
                    return result;
            }
            return null;
        }

        public static int? GetMappedIndex(IEnumerable<SkeletonBoneNode_new> bones, int boneId)
        {
            var bone = GetBoneFromId(bones, boneId);
            if (bone == null || bone.HasMapping == false)
                return null;

            return bone.MappedIndex;
        }
    }
}
