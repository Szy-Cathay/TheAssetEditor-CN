using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    internal class MoveEntryCommand : IUiCommand
    {
        public void ExecuteUp(MetaDataEditorViewModel controller)
        {
            if (controller?.ParsedFile == null)
                return;

            var selectedItems = GetSelectedItems(controller);
            if (selectedItems.Count == 0)
                return;

            var selectedSet = selectedItems.ToHashSet();
            var attributes = controller.ParsedFile.Attributes;
            var moved = false;
            for (var index = 1; index < attributes.Count; index++)
            {
                if (selectedSet.Contains(attributes[index]) &&
                    selectedSet.Contains(attributes[index - 1]) == false)
                {
                    (attributes[index - 1], attributes[index]) =
                        (attributes[index], attributes[index - 1]);
                    controller.Tags.Move(index, index - 1);
                    moved = true;
                }
            }

            if (moved)
                controller.MarkStructureChanged();
        }

        public void ExecuteDown(MetaDataEditorViewModel controller)
        {
            if (controller?.ParsedFile == null)
                return;

            var selectedItems = GetSelectedItems(controller);
            if (selectedItems.Count == 0)
                return;

            var selectedSet = selectedItems.ToHashSet();
            var attributes = controller.ParsedFile.Attributes;
            var moved = false;
            for (var index = attributes.Count - 2; index >= 0; index--)
            {
                if (selectedSet.Contains(attributes[index]) &&
                    selectedSet.Contains(attributes[index + 1]) == false)
                {
                    (attributes[index], attributes[index + 1]) =
                        (attributes[index + 1], attributes[index]);
                    controller.Tags.Move(index, index + 1);
                    moved = true;
                }
            }

            if (moved)
                controller.MarkStructureChanged();
        }

        private static List<ParsedMetadataAttribute> GetSelectedItems(
            MetaDataEditorViewModel controller)
        {
            var selectedItems = controller.Tags
                .Where(tag => tag.IsSelected)
                .Select(tag => tag._input)
                .ToList();

            if (selectedItems.Count == 0 && controller.SelectedTag != null)
            {
                controller.SelectedTag.IsSelected = true;
                selectedItems.Add(controller.SelectedTag._input);
            }

            return selectedItems;
        }
    }
}
