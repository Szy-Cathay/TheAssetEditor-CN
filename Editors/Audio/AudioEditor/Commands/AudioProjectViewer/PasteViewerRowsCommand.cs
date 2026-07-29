using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Editors.Audio.AudioEditor.Commands.AudioProjectMutation;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Enablement;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Table;
using Editors.Audio.Shared.AudioProject.Models;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.Commands.AudioProjectViewer
{
    public class PasteViewerRowsCommand(
        IAudioEditorStateService audioEditorStateService,
        IAudioProjectMutationUICommandFactory audioProjectMutationUICommandFactory,
        IEventHub eventHub) : IUiCommand
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IAudioProjectMutationUICommandFactory _audioProjectMutationUICommandFactory = audioProjectMutationUICommandFactory;
        private readonly IEventHub _eventHub = eventHub;

        public void Execute(List<DataRow> copiedRows)
        {
            if (copiedRows == null || copiedRows.Count == 0)
                return;

            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            var backup = CloneAudioProject(
                _audioEditorStateService.AudioProject);
            try
            {
                foreach (var row in copiedRows)
                {
                    _audioProjectMutationUICommandFactory
                        .Create(
                            MutationType.AddByPaste,
                            selectedAudioProjectExplorerNode.Type)
                        .Execute(row);
                    _eventHub.Publish(
                        new ViewerTableRowAddRequestedEvent(row));
                }
            }
            catch
            {
                _audioEditorStateService.StoreAudioProject(backup);
                _eventHub.Publish(new AudioProjectLoadedEvent(true));
                throw;
            }

            _eventHub.Publish(
                new EditorAddRowButtonEnablementUpdateRequestedEvent());
            _eventHub.Publish(new AudioProjectChangedEvent());
        }

        private static AudioProjectFile CloneAudioProject(
            AudioProjectFile audioProject)
        {
            var json = JsonSerializer.Serialize(audioProject);
            return JsonSerializer.Deserialize<AudioProjectFile>(json) ??
                throw new JsonException(
                    "Unable to create an Audio Project transaction backup.");
        }
    }
}
