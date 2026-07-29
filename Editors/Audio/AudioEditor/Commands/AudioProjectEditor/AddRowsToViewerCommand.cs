using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Editors.Audio.AudioEditor.Commands.AudioProjectMutation;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Table;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Table;
using Editors.Audio.Shared.AudioProject.Models;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.Commands.AudioProjectEditor
{
    public class AddRowsToViewerCommand(
        IAudioEditorStateService audioEditorStateService,
        IAudioProjectMutationUICommandFactory audioProjectMutationUICommandFactory,
        IEventHub eventHub) : IUiCommand
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IAudioProjectMutationUICommandFactory _audioProjectMutationUICommandFactory = audioProjectMutationUICommandFactory;
        private readonly IEventHub _eventHub = eventHub;

        public void Execute(List<DataRow> rows)
        {
            var backup = CloneAudioProject(
                _audioEditorStateService.AudioProject);
            try
            {
                var selectedNode =
                    _audioEditorStateService.SelectedAudioProjectExplorerNode;
                foreach (var pendingRow in
                         _audioEditorStateService.PendingEditedViewerRows)
                {
                    _audioProjectMutationUICommandFactory
                        .Create(MutationType.Remove, selectedNode.Type)
                        .Execute(pendingRow);
                }

                foreach (var row in rows)
                {
                    _audioProjectMutationUICommandFactory
                        .Create(MutationType.Add, selectedNode.Type)
                        .Execute(row);
                    _eventHub.Publish(
                        new ViewerTableRowAddRequestedEvent(row));
                    _eventHub.Publish(
                        new EditorTableRowAddedToViewerEvent());
                }
            }
            catch
            {
                _audioEditorStateService.StoreAudioProject(backup);
                _audioEditorStateService.StorePendingEditedViewerRows([]);
                _eventHub.Publish(new AudioProjectLoadedEvent(true));
                throw;
            }

            _audioEditorStateService.StorePendingEditedViewerRows([]);
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
