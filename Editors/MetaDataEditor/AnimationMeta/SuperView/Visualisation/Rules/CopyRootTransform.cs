using System;
using System.Collections.ObjectModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    public class CopyRootTransform : ILocalSpaceAnimationRule
    {
        readonly ILogger _logger = Logging.Create<CopyRootTransform>();
        readonly ISkeletonProvider _skeletonProvider;
        readonly int _boneId;

        bool _hasError = false;
        readonly Func<Vector3> _offsetPosition;
        readonly Func<Quaternion> _offsetOrientation;

        public CopyRootTransform(ISkeletonProvider skeleton, int boneId, Vector3 offsetPos, Quaternion offsetRot)
            : this(
                skeleton,
                boneId,
                () => offsetPos,
                () => offsetRot)
        {
        }

        public CopyRootTransform(
            ISkeletonProvider skeleton,
            int boneId,
            Func<Vector3> offsetPosition,
            Func<Quaternion> offsetOrientation)
        {
            _skeletonProvider = skeleton;
            _boneId = boneId;
            _offsetPosition = offsetPosition;
            _offsetOrientation = offsetOrientation;
        }

        public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float v)
        {
            if (boneId != 0 || _hasError || _boneId == -1)
                return;

            try
            {
                var transform = _skeletonProvider.Skeleton.GetAnimatedWorldTranform(_boneId);
                var m = Matrix.CreateFromQuaternion(_offsetOrientation()) *
                    Matrix.CreateTranslation(_offsetPosition()) *
                    transform;
                frame.BoneTransforms[0].WorldTransform = m;
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(CopyRootTransform)} - {e.Message}");
                _hasError = true;
            }
        }
    }
}
