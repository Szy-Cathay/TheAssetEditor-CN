using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;


namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class CreateExampleAnimationDbCommand : IUiCommand
    {
        private readonly IFileSaveService _saveHelper;
        private readonly IPackFileService _pfs;
        private readonly IStandardDialogs _standardDialogs;

        public CreateExampleAnimationDbCommand(
            IFileSaveService saveHelper,
            IPackFileService pfs,
            IStandardDialogs standardDialogs)
        {
            _saveHelper = saveHelper;
            _pfs = pfs;
            _standardDialogs = standardDialogs;
        }

        public PackFile? CreateAnimationDbWarhammer3()
        {
            var input = _standardDialogs.ShowTextInputDialog("New AnimPack name", "");
            if (input.Result)
                return CreateAnimationDbWarhammer3(input.Text);
            return null;
        }

        static string GenerateWh3AnimPackName(string name)
        {
            var fileName = SaveUtility.EnsureEnding(name, ".animpack");
            var filePath = @"animations/database/battle/bin/" + fileName;
            return filePath;
        }

        PackFile? CreateAnimationDbWarhammer3(string name)
        {
            var filePath = GenerateWh3AnimPackName(name);

            if (!SaveUtility.IsFilenameUnique(_pfs, filePath))
            {
                _standardDialogs.ShowDialogBox(LocalizationManager.Instance.Get("Msg.FilenameNotUnique"));
                return null;
            }

            var animPack = new AnimationPackFileDatabase("Placeholder");
            return _saveHelper.Save(filePath, AnimationPackSerializer.ConvertToBytes(animPack), false);
        }

        public void CreateAnimationDb3k()
        {
            var input = _standardDialogs.ShowTextInputDialog("New AnimPack name", "");
            if (input.Result)
            {
                var fileName = SaveUtility.EnsureEnding(input.Text, ".animpack");
                var filePath = @"animations/database/battle/bin/" + fileName;
                if (!SaveUtility.IsFilenameUnique(_pfs, filePath))
                {
                    _standardDialogs.ShowDialogBox(LocalizationManager.Instance.Get("Msg.FilenameNotUnique"));
                    return;
                }

                // Create dummy data
                var animPack = new AnimationPackFileDatabase("Placeholder");
                _saveHelper.Save(filePath, AnimationPackSerializer.ConvertToBytes(animPack), false);
            }
        }


     
    }
}
