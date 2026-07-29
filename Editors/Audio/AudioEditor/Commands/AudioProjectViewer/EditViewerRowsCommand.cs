using System.Collections.Generic;
using System.Data;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Table;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.Commands.AudioProjectViewer
{
    public class EditViewerRowsCommand(
        IAudioEditorStateService audioEditorStateService,
        IEventHub eventHub) : IUiCommand
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IEventHub _eventHub = eventHub;

        private readonly ILogger _logger = Logging.Create<EditViewerRowsCommand>();

        public void Execute(List<DataRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            _audioEditorStateService.StorePendingEditedViewerRows(
                new List<DataRow>(rows));
            _eventHub.Publish(new ViewerTableRowEditedEvent(rows[0]));

            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            _logger.Here().Information($"Editing {selectedAudioProjectExplorerNode.Type} row in Audio Project Viewer table for {selectedAudioProjectExplorerNode.Name}");
        }
    }
}
