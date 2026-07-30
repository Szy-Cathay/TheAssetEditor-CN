using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    internal class DeleteEntryCommand : IUiCommand
    {
        public void Execute(MetaDataEditorViewModel controller)
        {
            if (controller?.ParsedFile == null)
                return;

            var selectedTags = controller.Tags
                .Where(tag => tag.IsSelected)
                .ToList();
            if (selectedTags.Count == 0 && controller.SelectedTag != null)
                selectedTags.Add(controller.SelectedTag);

            var removedAny = false;
            foreach (var selectedTag in selectedTags)
            {
                if (controller.ParsedFile.Attributes.Remove(selectedTag._input) == false)
                    continue;

                controller.Tags.Remove(selectedTag);
                removedAny = true;
            }

            if (removedAny == false)
                return;

            controller.SelectedTag = controller.Tags.FirstOrDefault();
            if (controller.SelectedTag != null)
                controller.SelectedTag.IsSelected = true;
            controller.MarkStructureChanged();
        }

    }
}
