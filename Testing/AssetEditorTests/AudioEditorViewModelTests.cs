using System.Data;
using System.Text.Json;
using Editors.Audio.AudioEditor.Commands.AudioProjectEditor;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Events.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Enablement;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Shortcuts;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Table;
using Editors.Audio.AudioEditor.Events.Settings.Shortcuts;
using Editors.Audio.AudioEditor.Presentation;
using Editors.Audio.AudioEditor.Presentation.AudioProjectEditor;
using Editors.Audio.AudioEditor.Presentation.AudioProjectEditor.Table;
using Editors.Audio.AudioEditor.Presentation.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Presentation.AudioProjectViewer.Table;
using Editors.Audio.AudioEditor.Presentation.Settings;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.Shared.AudioProject.Compiler;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.UI.ValueConverters;
using Editors.Audio.Shared.Wwise;
using Moq;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioEditorViewModelTests
    {
        [TestInitialize]
        public void LoadLocalization()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
        }

        [TestMethod]
        public void ViewerActionEvents_FiltersTypesThatShareASoundBank()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProjectFileName = "test.aproj",
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    Wh3ActionEventInformation.GetName(Wh3ActionEventType.BattleAbilities),
                    AudioProjectTreeNodeType.ActionEventType)
            };
            var soundBankName =
                $"{Wh3SoundBankInformation.GetName(Wh3SoundBank.BattleIndividualMagic)}_test";
            var soundBank = new SoundBank(
                soundBankName,
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            soundBank.ActionEvents.Add(new ActionEvent(
                1,
                "ability_event",
                [],
                Wh3ActionEventType.BattleAbilities));
            soundBank.ActionEvents.Add(new ActionEvent(
                2,
                "magic_event",
                [],
                Wh3ActionEventType.BattleMagic));
            state.AudioProject = new AudioProjectFile
            {
                SoundBanks = [soundBank]
            };
            var names = new List<string>();
            eventHub.Register<ViewerTableRowAddRequestedEvent>(
                this,
                e => names.Add(
                    (string)e.Row[TableInformation.ActionEventColumnName]));
            var table = new DataTable();
            table.Columns.Add(TableInformation.ActionEventColumnName);
            var service = new ViewerActionEventTableService(eventHub, state);

            service.InitialiseTable(table);

            CollectionAssert.AreEqual(
                new[] { "ability_event" },
                names);
        }

        [TestMethod]
        public void InvalidEditorRow_ClearsTheShortcutState()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile(),
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    Wh3ActionEventInformation.GetName(Wh3ActionEventType.BattleAbilities),
                    AudioProjectTreeNodeType.ActionEventType),
                AudioFiles =
                [
                    new AudioFile(
                        Guid.NewGuid(),
                        1,
                        "test.wav",
                        "audio\\test.wav")
                ]
            };
            var viewModel = new AudioProjectEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                Mock.Of<IEditorTableServiceFactory>(),
                Mock.Of<IAudioRepository>());
            viewModel.Table.Columns.Add(TableInformation.ActionEventColumnName);
            var row = viewModel.Table.NewRow();
            row[TableInformation.ActionEventColumnName] = "Play_test";
            viewModel.Table.Rows.Add(row);
            viewModel.OnEditorAddRowButtonEnablementUpdateRequested(
                new EditorAddRowButtonEnablementUpdateRequestedEvent());
            Assert.IsNotNull(state.EditorRow);

            row[TableInformation.ActionEventColumnName] = string.Empty;
            viewModel.OnEditorAddRowButtonEnablementUpdateRequested(
                new EditorAddRowButtonEnablementUpdateRequestedEvent());

            Assert.IsFalse(viewModel.IsAddRowButtonEnabled);
            Assert.IsNull(state.EditorRow);
        }

        [TestMethod]
        public void RemovedEditedViewerRow_EndsEditModeWithoutReadingDetachedData()
        {
            var originalTable = new DataTable();
            originalTable.Columns.Add(TableInformation.ActionEventColumnName);
            var originalRow = originalTable.Rows.Add("Play_original");
            originalTable.Rows.Remove(originalRow);

            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile(),
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    Wh3ActionEventInformation.GetName(
                        Wh3ActionEventType.BattleAbilities),
                    AudioProjectTreeNodeType.ActionEventType),
                AudioFiles =
                [
                    new AudioFile(
                        Guid.NewGuid(),
                        1,
                        "test.wav",
                        "audio\\test.wav")
                ],
                PendingEditedViewerRows = [originalRow]
            };
            var tableService = Mock.Of<IEditorTableService>();
            var tableServiceFactory = new Mock<IEditorTableServiceFactory>();
            tableServiceFactory
                .Setup(x => x.GetService(
                    AudioProjectTreeNodeType.ActionEventType))
                .Returns(tableService);
            var viewModel = new AudioProjectEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                tableServiceFactory.Object,
                Mock.Of<IAudioRepository>())
            {
                IsEditing = true
            };
            viewModel.Table.Columns.Add(
                TableInformation.ActionEventColumnName);
            viewModel.Table.Rows.Add("Play_replacement");

            viewModel.OnEditorAddRowButtonEnablementUpdateRequested(
                new EditorAddRowButtonEnablementUpdateRequestedEvent());

            Assert.IsFalse(viewModel.IsEditing);
            Assert.AreEqual(0, state.PendingEditedViewerRows.Count);
            Assert.IsFalse(viewModel.IsAddRowButtonEnabled);
        }

        [TestMethod]
        public void EditedItemsFilter_ShowsSavedActionEventTypesAfterProjectReload()
        {
            var eventHub = new TestEventHub();
            var soundBank = new SoundBank(
                "battle_individual_magic_test",
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            soundBank.ActionEvents.Add(new ActionEvent(
                1,
                "ability_event",
                [
                    Editors.Audio.Shared.AudioProject.Models.Action.CreatePlay(
                        11,
                        Shared.GameFormats.Wwise.Enums.AkBkHircType.Sound,
                        101,
                        201)
                ],
                Wh3ActionEventType.BattleAbilities));
            soundBank.ActionEvents.Add(new ActionEvent(
                2,
                "magic_event",
                [
                    Editors.Audio.Shared.AudioProject.Models.Action.CreatePlay(
                        12,
                        Shared.GameFormats.Wwise.Enums.AkBkHircType.Sound,
                        102,
                        202)
                ],
                Wh3ActionEventType.BattleMagic));
            var audioProject = new AudioProjectFile
            {
                Language = Wh3LanguageInformation.GetLanguageAsString(
                    Wh3Language.EnglishUK),
                SoundBanks = [soundBank]
            };
            var reopenedAudioProject =
                JsonSerializer.Deserialize<AudioProjectFile>(
                    JsonSerializer.Serialize(audioProject.Clean()));
            Assert.IsNotNull(reopenedAudioProject);
            var state = new AudioEditorStateService
            {
                AudioProject = reopenedAudioProject,
                AudioProjectFileName = "test.aproj"
            };
            var treeBuilder = new AudioProjectTreeBuilderService(state);
            var filter = new AudioProjectTreeFilterService(state);
            var viewModel = new AudioProjectExplorerViewModel(
                eventHub,
                state,
                treeBuilder,
                filter);

            eventHub.Publish(new AudioProjectLoadedEvent());
            var actionEventTypes = viewModel.AudioProjectTree
                .SelectMany(root => root.Children)
                .SelectMany(soundBankNode => soundBankNode.Children)
                .Single(node =>
                    node.Type == AudioProjectTreeNodeType.ActionEvents)
                .Children;
            viewModel.ShowEditedItemsOnly = true;

            var visibleTypeNames = actionEventTypes
                .Where(node => node.IsVisible)
                .Select(node => node.Name)
                .ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    Wh3ActionEventInformation.GetName(
                        Wh3ActionEventType.BattleAbilities),
                    Wh3ActionEventInformation.GetName(
                        Wh3ActionEventType.BattleMagic)
                },
                visibleTypeNames);
            viewModel.Dispose();
        }

        [TestMethod]
        public void RecommendedVoiceSettingsShortcut_AppliesForMultipleFiles()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                EditorRow = new DataTable().NewRow(),
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    Wh3ActionEventInformation.GetName(Wh3ActionEventType.BattleAbilities),
                    AudioProjectTreeNodeType.ActionEventType)
            };
            var viewModel = new SettingsViewModel(
                eventHub,
                state,
                Mock.Of<IAudioRepository>())
            {
                ContainerType = ContainerType.Sequence
            };
            var audioFiles = new List<AudioFile>
            {
                new(Guid.NewGuid(), 1, "one.wav", "audio\\one.wav"),
                new(Guid.NewGuid(), 2, "two.wav", "audio\\two.wav")
            };
            eventHub.Publish(new AudioFilesChangedEvent(
                audioFiles,
                false,
                false,
                false));

            eventHub.Publish(
                new EditorSetRecommendedVOSettingsShortcutActivatedEvent());

            Assert.IsTrue(viewModel.IsSetRecommendedVOSettingsEnabled);
            Assert.AreEqual(ContainerType.Random, viewModel.ContainerType);
            Assert.AreEqual(RandomType.Shuffle, viewModel.RandomType);
        }

        [TestMethod]
        public void RecommendedVoiceSettings_RequiresMultipleFiles()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    Wh3ActionEventInformation.GetName(Wh3ActionEventType.BattleAbilities),
                    AudioProjectTreeNodeType.ActionEventType)
            };
            var viewModel = new SettingsViewModel(
                eventHub,
                state,
                Mock.Of<IAudioRepository>());
            eventHub.Publish(new AudioFilesChangedEvent(
                [
                    new AudioFile(
                        Guid.NewGuid(),
                        1,
                        "one.wav",
                        "audio\\one.wav")
                ],
                false,
                false,
                false));

            Assert.IsFalse(viewModel.IsSetRecommendedVOSettingsEnabled);
        }

        [TestMethod]
        public void AudioSettingsEnumConverter_UsesChineseDisplayNames()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            var converter = new EnumConverter();

            var value = converter.Convert(
                ContainerType.Random,
                typeof(string),
                null,
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.AreEqual("随机", value);
        }

        [TestMethod]
        public async Task CompileAudioProject_TracksAsyncCompilerState()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile(),
                AudioProjectFileName = "test.aproj",
                AudioProjectFilePath = "audio\\test.aproj"
            };
            var compiler = new Mock<IAudioProjectCompilerService>();
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.Save(
                    state.AudioProject,
                    state.AudioProjectFileName,
                    state.AudioProjectFilePath,
                    false))
                .Returns(true);
            var compilerStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCompiler = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            compiler
                .Setup(x => x.CompileAsync(
                    It.IsAny<AudioProjectFile>(),
                    state.AudioProjectFileName,
                    state.AudioProjectFilePath,
                    It.IsAny<AudioProjectCompileTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    AudioProjectFile _,
                    string _,
                    string _,
                    AudioProjectCompileTarget _,
                    CancellationToken cancellationToken) =>
                {
                    compilerStarted.TrySetResult();
                    await releaseCompiler.Task.WaitAsync(cancellationToken);
                    return true;
                });
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                fileService.Object,
                compiler.Object,
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                Mock.Of<IStandardDialogs>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            eventHub.Publish(new AudioProjectLoadedEvent());
            var selectedTarget = new AudioProjectCompileTarget(
                Wh3Language.Chinese);

            var compileTask = viewModel.CompileAudioProject(
                selectedTarget,
                CancellationToken.None);
            await compilerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(viewModel.IsCompiling);
            Assert.IsFalse(compileTask.IsCompleted);

            releaseCompiler.TrySetResult();
            await compileTask;

            Assert.IsFalse(viewModel.IsCompiling);
            Assert.AreEqual(9, viewModel.CompileTargets.Count);
            Assert.AreEqual(
                AudioProjectCompileTarget.AllLanguages,
                viewModel.CompileTargets[0].Target);
            Assert.IsFalse(viewModel.CompileTargets.Any(option =>
                option.Target.Language == Wh3Language.Sfx));
            compiler.Verify(x => x.CompileAsync(
                It.IsAny<AudioProjectFile>(),
                state.AudioProjectFileName,
                state.AudioProjectFilePath,
                selectedTarget,
                It.IsAny<CancellationToken>()));
        }

        [TestMethod]
        public async Task CompileAudioProject_WhenSaveIsCancelled_DoesNotReportAnError()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile(),
                AudioProjectFileName = "test.aproj",
                AudioProjectFilePath = "audio\\test.aproj"
            };
            var compiler = new Mock<IAudioProjectCompilerService>();
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.Save(
                    state.AudioProject,
                    state.AudioProjectFileName,
                    state.AudioProjectFilePath,
                    false))
                .Returns(false);
            var dialogs = new Mock<IStandardDialogs>();
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                fileService.Object,
                compiler.Object,
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            eventHub.Publish(new AudioProjectLoadedEvent());

            await viewModel.CompileAudioProject(
                AudioProjectCompileTarget.AllLanguages,
                CancellationToken.None);

            Assert.AreEqual(
                LocalizationManager.Instance.Get(
                    "AudioEditor.Compile.Cancelled"),
                viewModel.CompileStatus);
            Assert.IsFalse(viewModel.IsCompiling);
            compiler.Verify(
                x => x.CompileAsync(
                    It.IsAny<AudioProjectFile>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<AudioProjectCompileTarget>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task CompileAudioProject_WhenCompilerObservesCancellation_ReportsCancelled()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile(),
                AudioProjectFileName = "test.aproj",
                AudioProjectFilePath = "audio\\test.aproj"
            };
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.Save(
                    state.AudioProject,
                    state.AudioProjectFileName,
                    state.AudioProjectFilePath,
                    false))
                .Returns(true);
            using var cancellation = new CancellationTokenSource();
            var compiler = new Mock<IAudioProjectCompilerService>();
            compiler
                .Setup(x => x.CompileAsync(
                    It.IsAny<AudioProjectFile>(),
                    state.AudioProjectFileName,
                    state.AudioProjectFilePath,
                    It.IsAny<AudioProjectCompileTarget>(),
                    cancellation.Token))
                .Returns((
                    AudioProjectFile _,
                    string _,
                    string _,
                    AudioProjectCompileTarget _,
                    CancellationToken _) =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(false);
                });
            var dialogs = new Mock<IStandardDialogs>();
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                fileService.Object,
                compiler.Object,
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            eventHub.Publish(new AudioProjectLoadedEvent());

            await viewModel.CompileAudioProject(
                AudioProjectCompileTarget.AllLanguages,
                cancellation.Token);

            Assert.AreEqual(
                LocalizationManager.Instance.Get(
                    "AudioEditor.Compile.Cancelled"),
                viewModel.CompileStatus);
            Assert.IsFalse(viewModel.IsCompiling);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public void AudioProjectDirtyState_TracksChangesLoadsAndSuccessfulSaves()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile(),
                AudioProjectFileName = "test.aproj",
                AudioProjectFilePath = "audio\\test.aproj"
            };
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.Save(
                    state.AudioProject,
                    state.AudioProjectFileName,
                    state.AudioProjectFilePath,
                    false))
                .Returns(true);
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                fileService.Object,
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                Mock.Of<IStandardDialogs>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);

            eventHub.Publish(new AudioProjectChangedEvent());
            Assert.IsTrue(viewModel.HasUnsavedChanges);

            eventHub.Publish(new AudioProjectLoadedEvent(true));
            Assert.IsTrue(viewModel.HasUnsavedChanges);

            eventHub.Publish(new AudioProjectLoadedEvent());
            Assert.IsFalse(viewModel.HasUnsavedChanges);

            eventHub.Publish(new AudioProjectChangedEvent());
            Assert.IsTrue(viewModel.Save());
            Assert.IsFalse(viewModel.HasUnsavedChanges);
        }

        [TestMethod]
        public void LoadAudioProject_WithUnsavedChangesAndRejectedPrompt_DoesNotLoad()
        {
            var eventHub = new TestEventHub();
            var fileService = new Mock<IAudioEditorFileService>();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs
                .Setup(x => x.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(ShowMessageBoxResult.Cancel);
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                new AudioEditorStateService(),
                fileService.Object,
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!)
            {
                HasUnsavedChanges = true
            };

            viewModel.LoadAudioProject();

            fileService.Verify(x => x.LoadFromDialog(), Times.Never);
        }

        [TestMethod]
        public async Task LoadAudioProject_KeepsEditorLockedUntilAsyncLoadFinishes()
        {
            var eventHub = new TestEventHub();
            var loadCanFinish = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.LoadFromDialogAsync(
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<System.Action>()))
                .Returns(async (
                    IProgress<AudioLoadProgress> _,
                    CancellationToken cancellationToken,
                    System.Action loadStarted) =>
                {
                    loadStarted();
                    await loadCanFinish.Task.WaitAsync(cancellationToken);
                    eventHub.Publish(new AudioProjectLoadedEvent());
                    return true;
                });
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                new AudioEditorStateService(),
                fileService.Object,
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                Mock.Of<IStandardDialogs>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);

            var loadTask = viewModel.LoadAudioProject(
                CancellationToken.None);

            Assert.IsTrue(viewModel.IsLoading);
            Assert.IsTrue(viewModel.IsBusy);
            Assert.IsFalse(viewModel.IsEditorIdle);
            Assert.IsFalse(viewModel.CanEditAudioProject);
            Assert.IsFalse(loadTask.IsCompleted);

            loadCanFinish.SetResult(true);
            await loadTask;

            Assert.IsFalse(viewModel.IsLoading);
            Assert.IsFalse(viewModel.IsBusy);
            Assert.IsTrue(viewModel.IsEditorIdle);
            Assert.IsTrue(viewModel.CanEditAudioProject);
        }

        [TestMethod]
        public async Task LoadAudioProject_KeepsProgressPopupClosedWhileChoosingProject()
        {
            AudioEditorViewModel viewModel = null!;
            bool? popupActiveWhenDialogOpened = null;
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.LoadFromDialogAsync(
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<System.Action>()))
                .Returns((
                    IProgress<AudioLoadProgress> _,
                    CancellationToken _,
                    System.Action loadStarted) =>
                {
                    popupActiveWhenDialogOpened =
                        viewModel.IsLoadProgressWindowActive;
                    loadStarted();
                    return Task.FromResult(false);
                });
            viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                new TestEventHub(),
                new AudioEditorStateService(),
                fileService.Object,
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                Mock.Of<IStandardDialogs>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);

            await viewModel.LoadAudioProject(CancellationToken.None);

            Assert.IsFalse(popupActiveWhenDialogOpened);
        }

        [TestMethod]
        public async Task LoadAudioProject_WhenAsyncLoadFails_PreservesEditorAndShowsError()
        {
            var eventHub = new TestEventHub();
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.LoadFromDialogAsync(
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<System.Action>()))
                .ThrowsAsync(new InvalidOperationException("load failed"));
            var dialogs = new Mock<IStandardDialogs>();
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                new AudioEditorStateService(),
                fileService.Object,
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            eventHub.Publish(new AudioProjectLoadedEvent());

            await viewModel.LoadAudioProject(CancellationToken.None);

            Assert.IsTrue(viewModel.IsAudioProjectLoaded);
            Assert.IsTrue(viewModel.CanEditAudioProject);
            Assert.AreEqual(
                LocalizationManager.Instance.Get(
                    "AudioEditor.Load.Failed"),
                viewModel.CompileStatus);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.Is<string>(message =>
                        message.Contains("load failed")),
                    It.IsAny<string>()),
                Times.Once);
        }

        [TestMethod]
        public void LoadAudioProject_UnsupportedGame_DoesNotLoad()
        {
            var fileService = new Mock<IAudioEditorFileService>();
            var dialogs = new Mock<IStandardDialogs>();
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                new TestEventHub(),
                new AudioEditorStateService(),
                fileService.Object,
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Rome2),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);

            viewModel.LoadAudioProject();

            fileService.Verify(x => x.LoadFromDialog(), Times.Never);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        [TestMethod]
        public void RefreshSourceIds_WithoutLoadedProject_DoesNotPrompt()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService =
                new Mock<IAudioEditorIntegrityService>();
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                new TestEventHub(),
                new AudioEditorStateService(),
                Mock.Of<IAudioEditorFileService>(),
                Mock.Of<IAudioProjectCompilerService>(),
                integrityService.Object,
                Mock.Of<IShortcutService>(),
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);

            viewModel.RefreshSourceIds();

            dialogs.Verify(
                x => x.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
            integrityService.Verify(
                x => x.RefreshSourceIds(
                    It.IsAny<AudioProjectFile>()),
                Times.Never);
        }

        [TestMethod]
        public void AudioProjectSelection_WhenCleared_HidesStaleSettings()
        {
            var eventHub = new TestEventHub();
            var viewModel = new AudioEditorViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                new AudioEditorStateService(),
                Mock.Of<IAudioEditorFileService>(),
                Mock.Of<IAudioProjectCompilerService>(),
                Mock.Of<IAudioEditorIntegrityService>(),
                Mock.Of<IShortcutService>(),
                Mock.Of<IStandardDialogs>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            eventHub.Publish(
                new AudioProjectExplorerNodeSelectedEvent(
                    AudioProjectTreeNode.CreateNode(
                        "Play_test",
                        AudioProjectTreeNodeType.ActionEventType)));
            Assert.IsTrue(viewModel.IsSettingsBorderVisible);

            eventHub.Publish(
                new AudioProjectExplorerNodeSelectedEvent(null!));

            Assert.IsFalse(viewModel.IsSettingsBorderVisible);
        }

    }
}
