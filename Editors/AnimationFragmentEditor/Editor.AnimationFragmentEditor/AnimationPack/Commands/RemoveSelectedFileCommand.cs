using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class RemoveSelectedFileCommand : IUiCommand
    {
        public void Execute(AnimPackViewModel editor)
        {
            var selectedItem = editor.AnimationPackItems.SelectedItem;
            if (selectedItem != null && editor.AnimationPackItems.PossibleValues.Remove(selectedItem))
                editor.HasUnsavedChanges = true;

            editor.AnimationPackItems.RefreshFilter();
        }

    }

}
