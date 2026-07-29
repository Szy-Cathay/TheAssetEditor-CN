using System.Windows.Input;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Shortcuts;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Shortcuts;
using Editors.Audio.AudioEditor.Events.Settings.Shortcuts;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.Core
{
    public enum AudioEditorFocusTarget
    {
        Unknown,
        AudioProjectExplorer,
        AudioFilesExplorer,
        AudioProjectEditor,
        AudioProjectViewer,
        Settings,
        WaveformVisualiser
    }

    public enum AudioEditorShortcut
    {
        None,
        AddRow,
        EditRow,
        SetRecommendedSettings,
        CopyRows,
        PasteRows,
        RemoveRows,
        RemoveSettingsAudioFiles
    }

    public interface IShortcutService
    {
        void HandleShortcut(
            KeyEventArgs e,
            AudioEditorFocusTarget focusTarget,
            bool isTextInputFocussed,
            bool isSettingsAudioFilesListViewFocussed);
    }

    public class ShortcutService(IEventHub eventHub) : IShortcutService
    {
        private readonly IEventHub _eventHub = eventHub;

        public void HandleShortcut(
            KeyEventArgs e,
            AudioEditorFocusTarget focusTarget,
            bool isTextInputFocussed,
            bool isSettingsAudioFilesListViewFocussed)
        {
            var shortcut = ResolveShortcut(
                e.Key,
                Keyboard.Modifiers,
                focusTarget,
                isTextInputFocussed,
                isSettingsAudioFilesListViewFocussed);
            switch (shortcut)
            {
                case AudioEditorShortcut.AddRow:
                    _eventHub.Publish(
                        new EditorAddRowShortcutActivatedEvent());
                    break;
                case AudioEditorShortcut.EditRow:
                    _eventHub.Publish(
                        new ViewerEditRowShortcutActivatedEvent());
                    break;
                case AudioEditorShortcut.SetRecommendedSettings:
                    _eventHub.Publish(
                        new EditorSetRecommendedVOSettingsShortcutActivatedEvent());
                    break;
                case AudioEditorShortcut.CopyRows:
                    _eventHub.Publish(new ViewerCopyRowsShortcutActivatedEvent());
                    break;
                case AudioEditorShortcut.PasteRows:
                    _eventHub.Publish(new ViewerPasteRowsShortcutActivatedEvent());
                    break;
                case AudioEditorShortcut.RemoveRows:
                    _eventHub.Publish(new ViewerRemoveRowsShortcutActivatedEvent());
                    break;
                case AudioEditorShortcut.RemoveSettingsAudioFiles:
                    _eventHub.Publish(
                        new SettingsRemoveSelectedAudioFilesShortcutActivatedEvent());
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        public static AudioEditorShortcut ResolveShortcut(
            Key key,
            ModifierKeys modifiers,
            AudioEditorFocusTarget focusTarget,
            bool isTextInputFocussed,
            bool isSettingsAudioFilesListViewFocussed)
        {
            if (isTextInputFocussed)
                return AudioEditorShortcut.None;

            if (focusTarget == AudioEditorFocusTarget.AudioProjectEditor &&
                modifiers == ModifierKeys.Control &&
                key == Key.Q)
            {
                return AudioEditorShortcut.AddRow;
            }

            if (focusTarget == AudioEditorFocusTarget.AudioProjectViewer)
            {
                if (modifiers == ModifierKeys.Control && key == Key.E)
                    return AudioEditorShortcut.EditRow;
                if (modifiers == ModifierKeys.Control && key == Key.C)
                    return AudioEditorShortcut.CopyRows;
                if (modifiers == ModifierKeys.Control && key == Key.V)
                    return AudioEditorShortcut.PasteRows;
                if (modifiers == ModifierKeys.None &&
                    (key == Key.Delete || key == Key.Back))
                {
                    return AudioEditorShortcut.RemoveRows;
                }
            }

            if (focusTarget == AudioEditorFocusTarget.Settings)
            {
                if (modifiers == ModifierKeys.Control && key == Key.R)
                    return AudioEditorShortcut.SetRecommendedSettings;
                if (isSettingsAudioFilesListViewFocussed &&
                    modifiers == ModifierKeys.None &&
                    (key == Key.Delete || key == Key.Back))
                {
                    return AudioEditorShortcut.RemoveSettingsAudioFiles;
                }
            }

            return AudioEditorShortcut.None;
        }
    }
}
