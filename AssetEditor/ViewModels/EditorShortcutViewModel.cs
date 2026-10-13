using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace AssetEditor.ViewModels
{
    public partial class EditorShortcutViewModel : ObservableObject
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly EditorEnums _editor;
        private readonly Type _viewModelType;

        public string DisplayName { get; set; }
        public bool IsEnabled { get; }
        public MaterialIconKind IconKind { get; }
        [ObservableProperty] public partial bool IsActive { get; set; }

        public EditorShortcutViewModel(
            EditorInfo editorInfo,
            IUiCommandFactory commandFactory,
            ApplicationSettingsService settingsService)
        {
            _uiCommandFactory = commandFactory;
            _editor = editorInfo.EditorEnum;
            _viewModelType = editorInfo.ViewModel;
            IconKind = _editor switch
            {
                EditorEnums.MountTool_Editor => MaterialIconKind.Horse,
                EditorEnums.CampaginAnimation_Editor => MaterialIconKind.Movie,
                EditorEnums.AnimationKeyFrame_Editor => MaterialIconKind.Key,
                EditorEnums.AnimationBatchExporter_Editor => MaterialIconKind.Export,
                EditorEnums.AnimationRetarget_Editor => MaterialIconKind.SwapHorizontal,
                EditorEnums.VisualSkeletonEditor => MaterialIconKind.Bone,
                EditorEnums.Audio_Editor => MaterialIconKind.MusicNote,
                EditorEnums.AudioExplorer_Editor => MaterialIconKind.VolumeHigh,
                EditorEnums.SuperView_Editor => MaterialIconKind.LayersOutline,
                EditorEnums.Vfx_Editor => MaterialIconKind.Creation,
                _ => MaterialIconKind.CubeOutline,
            };
            DisplayName = LocalizationManager.Instance.Get(editorInfo.ToolbarName);
            IsEnabled = editorInfo.IsToolbarButtonEnabled &&
                (editorInfo.SupportedGames.Count == 0 ||
                 editorInfo.SupportedGames.Contains(
                     settingsService.CurrentSettings.CurrentGame));
        }

        public bool MatchesEditor(IEditorInterface? editor) =>
            editor != null && _viewModelType.IsInstanceOfType(editor);

        [RelayCommand(CanExecute = nameof(IsEnabled))]
        private void OpenEditor()
        {
            if (IsEnabled)
                _uiCommandFactory.Create<OpenEditorCommand>().Execute(_editor);
        }
    }
}
