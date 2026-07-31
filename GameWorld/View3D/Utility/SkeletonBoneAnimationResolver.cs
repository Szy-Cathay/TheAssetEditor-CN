using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Utility
{
    public class SkeletonBoneAnimationResolver
    {
        private readonly ISkeletonProvider _animationProvider;
        private readonly int _boneIndex;
        private readonly bool _useBindPoseWhenDisabled;

        public SkeletonBoneAnimationResolver(
            ISkeletonProvider gameSkeleton,
            int boneIndex,
            bool useBindPoseWhenDisabled = true)
        {
            _animationProvider = gameSkeleton;
            _boneIndex = boneIndex;
            _useBindPoseWhenDisabled = useBindPoseWhenDisabled;
        }

        public Matrix GetWorldTransform()
        {
            return _animationProvider.Skeleton.GetAnimatedWorldTranform(_boneIndex);
        }

        public Matrix GetWorldTransformIfAnimating()
        {
            var skeleton = _animationProvider.Skeleton;
            if (skeleton == null || _boneIndex < 0 || _boneIndex >= skeleton.BoneCount)
                return Matrix.Identity;
            if (!_useBindPoseWhenDisabled && !skeleton.AnimationPlayer.IsEnabled)
                return Matrix.Identity;
            return skeleton.GetAnimatedWorldTranform(_boneIndex);
        }

        public Matrix GetTransformIfAnimating()
        {
            var skeleton = _animationProvider.Skeleton;
            if (skeleton == null || _boneIndex < 0 || _boneIndex >= skeleton.BoneCount)
                return Matrix.Identity;
            if (!_useBindPoseWhenDisabled && !skeleton.AnimationPlayer.IsEnabled)
                return Matrix.Identity;
            return skeleton.GetAnimatedTranform(_boneIndex);
        }
    }
}
