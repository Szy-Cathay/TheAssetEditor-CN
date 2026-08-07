using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using System;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    public class DockEquipmentRule : IWorldSpaceAnimationRule
    {
        ILogger _logger = Logging.Create<CopyRootTransform>();
        bool _hasError = false;

        int _equipmentSlotToDock;
        AnimationClip _dockAnimation;
        ISkeletonProvider _skeletonProvider;
        MetaDataTimeRange _activeTimeRange;
        int _dockTargetkBoneId;
        Matrix _offset;

        public DockEquipmentRule(int dockTargetkBoneId, int equipmentSlotToDock, AnimationClip dockAnimation, ISkeletonProvider skeletonProvider, float startTime, float endTime)
        {
            _dockTargetkBoneId = dockTargetkBoneId;
            _dockAnimation = dockAnimation;
            _skeletonProvider = skeletonProvider;
            _activeTimeRange = new MetaDataTimeRange(
                startTime,
                endTime,
                MetaDataZeroRangeBehavior.WholeAnimation);

            try
            {
                _equipmentSlotToDock = skeletonProvider.Skeleton
                    .GetBoneIndexByName(GetEquipmentBoneName(
                        equipmentSlotToDock));
                _offset = Matrix.Identity;
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(DockEquipmentRule)} - {e.Message}");
                _hasError = true;
            }
        }

        public static string GetEquipmentBoneName(int propBoneId) =>
            "be_prop_" + propBoneId;

        public void TransformFrameWorldSpace(AnimationFrame frame, float time)
        {
            if (_hasError)
                return;

            try
            {
                if (_activeTimeRange.Contains(time))
                {
                    var offsetFrame = AnimationSampler.Sample(0, _skeletonProvider.Skeleton, _dockAnimation);
                    _offset = offsetFrame.GetSkeletonAnimatedWorldDiff(_skeletonProvider.Skeleton, _dockTargetkBoneId, _equipmentSlotToDock);

                    var propTransform = _skeletonProvider.Skeleton.GetAnimatedWorldTranform(_dockTargetkBoneId);
                    frame.BoneTransforms[_equipmentSlotToDock].WorldTransform = _offset * propTransform;
                }
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(DockEquipmentRule)} - {e.Message}");
                _hasError = true;
            }
        }
    }
}
