using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AssetEditor.Services;
using AssetEditor.UiCommands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.Commands;
using Editors.Reports.Animation;
using Editors.Reports.Audio;
using Editors.Reports.DeepSearch;
using Editors.Reports.Files;
using Editors.Reports.Geometry;
using Editors.Shared.Core.Services;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

namespace AssetEditor.ViewModels
{
    public partial class MenuBarViewModel : ObservableObject
    {
        private readonly IPackFileService _packfileService;
        private readonly ApplicationSettingsService _settingsService;
        private readonly IEditorDatabase _editorDatabase;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly TouchedFilesRecorder _touchedFilesRecorder;
        private readonly IPackFileContainerLoader _packFileContainerLoader;
        private readonly IFolderProjectOpenService _folderProjectOpenService;
        private readonly IStandardDialogs _standardDialogs;

        public ObservableCollection<RecentPackFileItem> RecentReferencePacks { get; set; } = [];
        public ObservableCollection<RecentPackFileItem> RecentFolderProjects { get; set; } = [];
        public ObservableCollection<EditorShortcutViewModel> Editors { get; set; } = [];

        public MenuBarViewModel(IPackFileService packfileService,
            ApplicationSettingsService settingsService,
            IEditorDatabase editorDatabase,
            IUiCommandFactory uiCommandFactory,
            TouchedFilesRecorder touchedFilesRecorder,
            IPackFileContainerLoader packFileContainerLoader,
            IFolderProjectOpenService folderProjectOpenService,
            IStandardDialogs standardDialogs,
            IEventHub eventHub)
        {
            _packfileService = packfileService;
            _settingsService = settingsService;
            _editorDatabase = editorDatabase;
            _uiCommandFactory = uiCommandFactory;
            _touchedFilesRecorder = touchedFilesRecorder;
            _packFileContainerLoader = packFileContainerLoader;
            _folderProjectOpenService = folderProjectOpenService;
            _standardDialogs = standardDialogs;
            var settings = settingsService.CurrentSettings;
            settings.RecentPackFilePaths.CollectionChanged +=
                (sender, args) => CreateRecentReferencePackItems();
            settings.RecentFolderProjectPaths.CollectionChanged +=
                (sender, args) => CreateRecentFolderProjectItems();
            CreateRecentReferencePackItems();
            CreateRecentFolderProjectItems();
            CreateTools();
            eventHub.Register<PackFileContainerSetAsMainEditableEvent>(
                this,
                _ =>
                {
                    OpenFolderProjectVersionControlCommand
                        .NotifyCanExecuteChanged();
                    GeneratePackCommand.NotifyCanExecuteChanged();
                    CreateAnimPackWarhammer3Command
                        .NotifyCanExecuteChanged();
                    CreateAnimPack3kCommand.NotifyCanExecuteChanged();
                });
        }

        [RelayCommand] private void OpenSettingsWindow() => _uiCommandFactory.Create<OpenSettingsDialogCommand>().Execute();
        [RelayCommand] private void OpenReferencePack() => _uiCommandFactory.Create<OpenReferencePackCommand>().Execute();
        [RelayCommand] private void CreateFolderProject() => _uiCommandFactory.Create<CreateFolderProjectCommand>().Execute();
        [RelayCommand] private void OpenFolderProject() => _uiCommandFactory.Create<OpenFolderProjectCommand>().Execute();
        [RelayCommand] private void ImportPackAsFolderProject() => _uiCommandFactory.Create<ImportPackAsFolderProjectCommand>().Execute();
        [RelayCommand(CanExecute =
            nameof(CanOpenFolderProjectVersionControl))]
        private void OpenFolderProjectVersionControl() =>
            _uiCommandFactory
                .Create<OpenFolderProjectVersionControlCommand>()
                .Execute();

        private bool CanOpenFolderProjectVersionControl() =>
            CanEditFolderProject();

        [RelayCommand(CanExecute = nameof(CanEditFolderProject))]
        private void CreateAnimPackWarhammer3() => _uiCommandFactory.Create<CreateExampleAnimationDbCommand>().CreateAnimationDbWarhammer3();
        [RelayCommand(CanExecute = nameof(CanEditFolderProject))]
        private void CreateAnimPack3k() => _uiCommandFactory.Create<CreateExampleAnimationDbCommand>().CreateAnimationDb3k();

        [RelayCommand(CanExecute = nameof(CanGeneratePack))]
        private void GeneratePack() => _uiCommandFactory
            .Create<SavePackFileContainerCommand>()
            .Execute();

        private bool CanGeneratePack() => CanEditFolderProject();
        private bool CanEditFolderProject() =>
            _packfileService.GetEditablePack() is FolderProjectContainer;
        [RelayCommand] private void OpenWh2AnimpackUpdater() => new AnimPackUpdaterService(_packfileService).Process();
        [RelayCommand] private void GenerateRmv2Report() => _uiCommandFactory.Create<Rmv2ReportCommand>().Execute();
        [RelayCommand] private void GenerateMetaDataReport() => _uiCommandFactory.Create<GenerateMetaDataReportCommand>().Execute();
        [RelayCommand] private void GenerateFileListReport() => _uiCommandFactory.Create<FileListReportCommand>().Execute();
        [RelayCommand] private void GenerateMetaDataJsonsReport() => _uiCommandFactory.Create<GenerateMetaJsonDataReportCommand>().Execute();
        [RelayCommand] private void GenerateMaterialReport() => _uiCommandFactory.Create<MaterialReportCommand>().Execute();
        [RelayCommand] private void GenerateDialogueEventInfoPrinterReport() => _uiCommandFactory.Create<GenerateDialogueEventInfoPrinterReportCommand>().Execute();
        [RelayCommand] private void GenerateDialogueEventAndEventNamePrinterReport() => _uiCommandFactory.Create<GenerateDialogueEventAndEventNamePrinterReportCommand>().Execute();
        [RelayCommand] private void GenerateDatDumperReport() => _uiCommandFactory.Create<GenerateDatDumperReportCommand>().Execute();


        [RelayCommand] private void TouchedFileRecorderStart() => _touchedFilesRecorder.Start();
        [RelayCommand] private void TouchedFileRecorderPrint() => _touchedFilesRecorder.Print();
        [RelayCommand] private void TouchedFileRecorderExtract() => _touchedFilesRecorder.ExtractFilesToPack(@"c:\temp\extractedPack.pack");
        [RelayCommand] private void TouchedFileRecorderStop() => _touchedFilesRecorder.Stop();

        [RelayCommand] private void ClearConsole() => Console.Clear();
        [RelayCommand] private void PrintScope() => _uiCommandFactory.Create<PrintScopesCommand>().Execute();
        [RelayCommand] private void Search() => _uiCommandFactory.Create<DeepSearchCommand>().Execute();
        [RelayCommand] private void OpenAttilaPacks() => _uiCommandFactory.Create<OpenGamePackCommand>().Execute(GameTypeEnum.Attila);
        [RelayCommand] private void OpenRomeRemasteredPacks() => _uiCommandFactory.Create<OpenGamePackCommand>().Execute(GameTypeEnum.RomeRemastered);
        [RelayCommand] private void OpenThreeKingdomsPacks() => _uiCommandFactory.Create<OpenGamePackCommand>().Execute(GameTypeEnum.ThreeKingdoms);
        [RelayCommand] private void OpenWarhammer2Packs() => _uiCommandFactory.Create<OpenGamePackCommand>().Execute(GameTypeEnum.Warhammer2);
        [RelayCommand] private void OpenWarhammer3Packs() => _uiCommandFactory.Create<OpenGamePackCommand>().Execute(GameTypeEnum.Warhammer3);
        [RelayCommand] private void OpenTroyPacks() => _uiCommandFactory.Create<OpenGamePackCommand>().Execute(GameTypeEnum.Troy);

        [RelayCommand] private void OpenAnimatedPropTutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.youtube.com/watch?v=b68hSHZ5raY");
        [RelayCommand] private void OpenAnimationBasicsTutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://youtu.be/H10jDrHJ_Uo?si=XnePs_0X5CQjxLZZ");
        [RelayCommand] private void OpenAssetEdBasic0Tutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.youtube.com/watch?v=iVjAVEn8jYc");
        [RelayCommand] private void OpenAssetEdBasic1Tutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.youtube.com/watch?v=7HN4oA2LsFM");
        [RelayCommand] private void OpenSkragTutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.youtube.com/watch?v=MhvbZfNp8Qw");
        [RelayCommand] private void OpenTzarGuardTutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.youtube.com/watch?v=ONRAKJUmuiM");
        [RelayCommand] private void OpenKostalynTutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.youtube.com/watch?v=AXw99yc74CY");
        [RelayCommand] private void OpenRecolouringModelsTutorial() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://youtu.be/azDq2IRnr1U?si=GammGsisnCzGKYiA");

        [RelayCommand] private void OpenHelp() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://tw-modding.com/index.php/Tutorial:AssetEditor");
        [RelayCommand] private void OpenPatreon() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://www.patreon.com/TheAssetEditor");
        [RelayCommand] private void OpenDiscord() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://discord.gg/6Djf2sCczC");
        [RelayCommand] private void DownloadRme() => _uiCommandFactory.Create<OpenWebpageCommand>().Execute("https://github.com/mr-phazer/RME_Release/releases/latest");

        [RelayCommand] private static void OpenAssetEditorFolder() => Process.Start("explorer.exe", DirectoryHelper.ApplicationDirectory);

        [RelayCommand]
        private static void ClearAssetEditorFolder()
        {
            try { Directory.Delete(DirectoryHelper.ApplicationDirectory, true); } catch { }
        }

        [RelayCommand] private void TogglePackFileExplorer() => _uiCommandFactory.Create<TogglePackFileExplorerCommand>().Execute();

        void CreateRecentReferencePackItems()
        {
            var settings = _settingsService.CurrentSettings;

            RecentReferencePacks.Clear();
            var menuItemViewModels = settings.RecentPackFilePaths.Select(path => new RecentPackFileItem(
                path,
                () =>
                {
                    var container = _packFileContainerLoader.Load(path);
                    if (container == null)
                    {
                        _standardDialogs.ShowDialogBox(LocalizationManager.Instance.GetFormat("Msg.UnableToLoadPackfiles", path));
                        return;
                    }

                    _packfileService.AddReferencePack(container);

                }
            ));
            foreach (var menuItem in menuItemViewModels.Reverse())
            {
                RecentReferencePacks.Add(menuItem);
            }
        }

        void CreateRecentFolderProjectItems()
        {
            var settings = _settingsService.CurrentSettings;

            RecentFolderProjects.Clear();
            var items = settings.RecentFolderProjectPaths.Select(
                path => new RecentPackFileItem(
                    path,
                    () => _folderProjectOpenService.Open(path)));

            foreach (var item in items.Reverse())
                RecentFolderProjects.Add(item);
        }

        void CreateTools()
        {
            var infos = _editorDatabase
                .GetEditorInfos()
                .OrderBy(x => x.ToolbarName)
                .Where(x => x.AddToolbarButton)
                .ToList();

            foreach (var item in infos)
            {
                Editors.Add(new EditorShortcutViewModel(
                    item,
                    _uiCommandFactory,
                    _settingsService));
            }
        }
    }
}
