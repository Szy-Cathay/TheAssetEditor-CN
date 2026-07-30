using GameWorld.Core.Animation;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.Animation;

namespace AnimationEditor.CampaignAnimationCreator.Commands
{
    public class SaveCampaignAnimationCommand
    {
        private readonly IPackFileService _packFileService;
        private readonly IFileSaveService _fileSaveService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly LocalizationManager _localizationManager;

        public SaveCampaignAnimationCommand(
            IPackFileService packFileService,
            IFileSaveService fileSaveService,
            IStandardDialogs standardDialogs,
            LocalizationManager localizationManager)
        {
            _packFileService = packFileService;
            _fileSaveService = fileSaveService;
            _standardDialogs = standardDialogs;
            _localizationManager = localizationManager;
        }

        public bool Execute(GameSkeleton? skeleton, AnimationClip? animationClip)
        {
            if (skeleton == null)
            {
                ShowError("CampaignAnim.Error.NoSkeletonToSave");
                return false;
            }

            if (animationClip == null)
            {
                ShowError("CampaignAnim.Error.NoAnimationToSave");
                return false;
            }

            if (_packFileService.GetEditablePack() == null)
            {
                ShowError("CampaignAnim.Error.NoEditablePackToSave");
                return false;
            }

            var animationFile = animationClip.ConvertToFileFormat(skeleton);
            var bytes = AnimationFile.ConvertToBytes(animationFile);
            return _fileSaveService.SaveAs(".anim", bytes) != null;
        }

        private void ShowError(string localizationKey)
        {
            _standardDialogs.ShowDialogBox(
                _localizationManager.Get(localizationKey),
                _localizationManager.Get("Msg.GeneralError"));
        }
    }
}
