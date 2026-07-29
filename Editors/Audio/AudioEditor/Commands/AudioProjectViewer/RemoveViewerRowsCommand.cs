using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Editors.Audio.AudioEditor.Commands.AudioProjectMutation;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Core.AudioProjectMutation;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Enablement;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.Shared.AudioProject.Models;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.Audio.AudioEditor.Commands.AudioProjectViewer
{
    public class RemoveViewerRowsCommand(
        IAudioEditorStateService audioEditorStateService,
        IAudioProjectMutationUICommandFactory audioProjectMutationUICommandFactory,
        IStateService stateService,
        IEventHub eventHub,
        IStandardDialogs standardDialogs) : IUiCommand
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;
        private readonly IAudioProjectMutationUICommandFactory _audioProjectMutationUICommandFactory = audioProjectMutationUICommandFactory;
        private readonly IStateService _stateService = stateService;
        private readonly IEventHub _eventHub = eventHub;
        private readonly IStandardDialogs _standardDialogs =
            standardDialogs;

        public void Execute(List<DataRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            try
            {
                if (selectedAudioProjectExplorerNode.IsStateGroup())
                {
                    foreach (var row in rows)
                    {
                        _stateService.ValidateStateCanBeRemoved(
                            selectedAudioProjectExplorerNode.Name,
                            TableHelpers.GetStateNameFromRow(row));
                    }
                }
            }
            catch (System.InvalidOperationException exception)
            {
                _standardDialogs.ShowDialogBox(
                    exception.Message,
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                _eventHub.Publish(
                    new EditorAddRowButtonEnablementUpdateRequestedEvent());
                return;
            }

            var backup = CloneAudioProject(
                _audioEditorStateService.AudioProject);
            var removedRowCount = 0;
            try
            {
                foreach (var row in rows)
                {
                    _audioProjectMutationUICommandFactory
                        .Create(
                            MutationType.Remove,
                            selectedAudioProjectExplorerNode.Type)
                        .Execute(row);
                    removedRowCount++;
                }
            }
            catch (System.InvalidOperationException exception)
            {
                RestoreAudioProject(backup);
                removedRowCount = 0;
                _standardDialogs.ShowDialogBox(
                    exception.Message,
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
            }
            catch
            {
                RestoreAudioProject(backup);
                throw;
            }

            _eventHub.Publish(new EditorAddRowButtonEnablementUpdateRequestedEvent());
            if (removedRowCount > 0)
                _eventHub.Publish(new AudioProjectChangedEvent());
        }

        private void RestoreAudioProject(
            AudioProjectFile backup)
        {
            _audioEditorStateService.StoreAudioProject(backup);
            _eventHub.Publish(new AudioProjectLoadedEvent(true));
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
