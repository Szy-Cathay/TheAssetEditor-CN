using CommonControls.BaseDialogs;
using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class RenameSelectedFileCommand : IUiCommand
    { 
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
            editor.AnimationPackItems.RefreshFilter();
        }

        protected virtual string? GetNewFileName(string currentFileName)
        {
            var window = new TextInputWindow("Rename Anim File", currentFileName);
            if (window.ShowDialog() == true)
                return window.TextValue;

            return null;
        }
    }

}
