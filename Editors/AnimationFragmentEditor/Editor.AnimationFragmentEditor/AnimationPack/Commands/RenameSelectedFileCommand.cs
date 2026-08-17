using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class RenameSelectedFileCommand : IUiCommand
    {
        private readonly IStandardDialogs _standardDialogs;

        public RenameSelectedFileCommand(IStandardDialogs standardDialogs)
        {
            _standardDialogs = standardDialogs;
        }

        public void Execute(AnimPackViewModel editor)
        {
            var animFile = editor.AnimationPackItems.PossibleValues.FirstOrDefault(file => file == editor.AnimationPackItems.SelectedItem);
            if (animFile == null)
                return;

            var newFileName = GetNewFileName(animFile.FileName);
            if (newFileName != null && newFileName != animFile.FileName)
            {
                animFile.FileName = newFileName;
                editor.HasUnsavedChanges = true;
            }

            // way to refresh the view
            editor.RefreshFileFilter();
        }

        protected virtual string? GetNewFileName(string currentFileName)
        {
            var input = _standardDialogs.ShowTextInputDialog(
                LocalizationManager.Instance.Get("AnimPack.RenameFileTitle"),
                currentFileName);
            if (input.Result)
                return input.Text;

            return null;
        }
    }

}
