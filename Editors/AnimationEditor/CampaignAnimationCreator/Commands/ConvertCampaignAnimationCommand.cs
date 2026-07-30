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
            out AnimationClip? convertedAnimation)
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

                frame.Position[rootBone.BoneIndex] = Vector3.Zero;
                frame.Rotation[rootBone.BoneIndex] = Quaternion.Identity;
            }

            convertedAnimation = animationCopy;
            return true;
        }

        private void ShowError(string localizationKey, params object[] args)
        {
            var message = args.Length == 0
                ? _localizationManager.Get(localizationKey)
                : _localizationManager.GetFormat(localizationKey, args);
            _standardDialogs.ShowDialogBox(message, _localizationManager.Get("Msg.GeneralError"));
        }
    }
}
