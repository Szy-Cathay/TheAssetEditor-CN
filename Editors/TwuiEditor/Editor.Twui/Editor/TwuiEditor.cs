using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Twui.Editor.ComponentEditor;
using Editors.Twui.Editor.Rendering;
using GameWorld.Core.WpfWindow.FactionColourSettings;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using System.Windows.Input;

namespace Editors.Twui.Editor
{
    public partial class TwuiEditor : ObservableObject, IEditorInterface, ISaveableEditor, IFileEditor
    {
        private readonly IEventHub _eventHub;
        private readonly TwuiRenderComponent _renderComponent;

        [ObservableProperty] string _displayName = "Twui Editor";

        [ObservableProperty] private bool _hasUnsavedChanges = false;
        public PackFile CurrentFile { get; set; }

        [ObservableProperty] public partial TwuiContext? ParsedTwuiFile { get; set; }
        [ObservableProperty] ComponentManger _componentManager;
        [ObservableProperty] IWpfGame _scene;
        private readonly IPackFileService _packFileService;
        public ICommand OpenFactionColourSettingsCommand { get; }

        public TwuiEditor(
            IEventHub eventHub,
            ComponentManger componentEditor,
            TwuiRenderComponent renderComponent,
            IWpfGame wpfGame,
            IPackFileService packFileService,
            IFactionColourSettingsDialogService factionColourSettingsDialog)
        {
            _eventHub = eventHub;
            _componentManager = componentEditor;
            _renderComponent = renderComponent;
            _scene = wpfGame;
            _packFileService = packFileService;
            OpenFactionColourSettingsCommand = new RelayCommand(
                factionColourSettingsDialog.ShowDialog);
            wpfGame.ForceEnsureCreated();
            renderComponent.Initialize();

            wpfGame.AddComponent(renderComponent);
        }

        public bool Save() { return true; }
        public void Close() { }

        public void LoadFile(PackFile file)
        {
            if (file == CurrentFile)
                return;

            DisplayName = LocalizationManager.Instance.GetFormat("DisplayName.TwuiEditor", Path.GetFileName(file.Name));

            var contextBuilder = new ContextBuilder(_packFileService);
            ParsedTwuiFile = contextBuilder.Create(file);

            ComponentManager.SetFile(ParsedTwuiFile);
            _renderComponent.SetFile(ParsedTwuiFile);
        }
    }
}
