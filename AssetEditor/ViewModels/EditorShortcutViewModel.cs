using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace AssetEditor.ViewModels
{
    public partial class EditorShortcutViewModel
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly EditorEnums _editor;

        public string DisplayName { get; set; }
        public bool IsEnabled{ get; set; }

        public EditorShortcutViewModel(
            EditorInfo editorInfo,
            IUiCommandFactory commandFactory,
            ApplicationSettingsService settingsService)
        {
            _uiCommandFactory = commandFactory;
            _editor = editorInfo.EditorEnum;
            DisplayName = LocalizationManager.Instance.Get(editorInfo.ToolbarName);
            IsEnabled = editorInfo.IsToolbarButtonEnabled &&
                (editorInfo.SupportedGames.Count == 0 ||
                 editorInfo.SupportedGames.Contains(
                     settingsService.CurrentSettings.CurrentGame));
        }

        [RelayCommand] private void OpenEditor() => _uiCommandFactory.Create<OpenEditorCommand>().Execute(_editor);
    }
}
