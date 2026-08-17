using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class RemoveSelectedFileCommand : IUiCommand
    {
        private readonly IStandardDialogs _standardDialogs;

        public RemoveSelectedFileCommand(IStandardDialogs standardDialogs)
        {
            _standardDialogs = standardDialogs;
        }

        public void Execute(AnimPackViewModel editor)
        {
            var selectedItem = editor.AnimationPackItems.SelectedItem;
            if (selectedItem == null ||
                !editor.AnimationPackItems.PossibleValues.Contains(selectedItem))
            {
                return;
            }

            var confirmation = _standardDialogs.ShowYesNoBox(
                LocalizationManager.Instance.GetFormat(
                    "AnimPack.RemoveConfirm",
                    selectedItem.FileName),
                LocalizationManager.Instance.Get("AnimPack.RemoveConfirmTitle"));
            if (confirmation != ShowMessageBoxResult.OK)
                return;

            editor.AnimationPackItems.SelectedItem = default!;
            if (editor.AnimationPackItems.SelectedItem != null)
                return;

            editor.AnimationPackItems.PossibleValues.Remove(selectedItem);
            editor.HasUnsavedChanges = true;

            editor.RefreshFileFilter();
        }

    }

}
