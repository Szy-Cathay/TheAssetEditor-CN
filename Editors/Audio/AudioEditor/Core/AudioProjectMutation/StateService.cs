using Editors.Audio.Shared.AudioProject.Models;

namespace Editors.Audio.AudioEditor.Core.AudioProjectMutation
{
    public interface IStateService
    {
        void AddState(string stateGroupName, string stateName);
        void ValidateStateCanBeRemoved(
            string stateGroupName,
            string stateName);
        void RemoveState(string stateGroupName, string stateName);
    }

    public class StateService(IAudioEditorStateService audioEditorStateService) : IStateService
    {
        private readonly IAudioEditorStateService _audioEditorStateService = audioEditorStateService;

        public void AddState(string stateGroupName, string stateName)
        {
            var stateGroup = _audioEditorStateService.AudioProject.GetStateGroup(stateGroupName);
            var state = new State(stateName);
            stateGroup.States.InsertAlphabetically(state);
        }

        public void RemoveState(string stateGroupName, string stateName)
        {
            ValidateStateCanBeRemoved(stateGroupName, stateName);

            var stateGroup = _audioEditorStateService.AudioProject.GetStateGroup(stateGroupName);
            var state = stateGroup.GetState(stateName);
            stateGroup.States.Remove(state);
        }

        public void ValidateStateCanBeRemoved(
            string stateGroupName,
            string stateName)
        {
            foreach (var soundBank in _audioEditorStateService.AudioProject.SoundBanks)
            {
                foreach (var dialogueEvent in soundBank.DialogueEvents)
                {
                    foreach (var statePath in dialogueEvent.StatePaths)
                    {
                        foreach (var node in statePath.Nodes)
                        {
                            if (string.Equals(
                                    node.StateGroup.Name,
                                    stateGroupName,
                                    System.StringComparison.Ordinal) &&
                                string.Equals(
                                    node.State.Name,
                                    stateName,
                                    System.StringComparison.Ordinal))
                            {
                                throw new System.InvalidOperationException(
                                    global::Shared.Core.Services.LocalizationManager
                                        .Instance.GetFormat(
                                            "Msg.AudioStateInUse",
                                            stateGroupName,
                                            stateName));
                            }
                        }
                    }
                }
            }
        }
    }
}
