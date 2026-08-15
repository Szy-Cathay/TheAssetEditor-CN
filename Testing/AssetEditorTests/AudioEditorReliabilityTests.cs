using System.Collections.ObjectModel;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Editors.Audio.AudioEditor.Commands.AudioProjectEditor;
using Editors.Audio.AudioEditor.Commands.AudioProjectMutation;
using Editors.Audio.AudioEditor.Commands.AudioProjectViewer;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Core.AudioProjectMutation;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Presentation.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Presentation.NewAudioProject;
using Editors.Audio.AudioEditor.Presentation.AudioProjectViewer;
using Editors.Audio.AudioEditor.Presentation.AudioProjectViewer.Table;
using Editors.Audio.AudioEditor.Presentation.Settings;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.AudioProjectConverter;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Factories;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise.Generators;
using Editors.Audio.AudioProjectMerger;
using Editors.Audio.DialogueEventMerger;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise.Enums;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioEditorReliabilityTests
    {
        [TestInitialize]
        public void LoadLocalization()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
        }

        [TestMethod]
        public void AudioProjectSave_PreservesChineseTextAsUtf8()
        {
            byte[]? savedBytes = null;
            var fileSaveService = new Mock<IFileSaveService>();
            fileSaveService
                .Setup(x => x.Save(
                    "audio\\audio_projects\\测试.aproj",
                    It.IsAny<byte[]>(),
                    false))
                .Callback((string _, byte[] bytes, bool _) => savedBytes = bytes);
            var service = new AudioProjectFileService(
                Mock.Of<IPackFileService>(),
                fileSaveService.Object,
                Mock.Of<IStandardDialogs>());
            var project = new AudioProjectFile
            {
                Language = "简体中文",
                AudioFiles =
                [
                    new AudioFile(
                        Guid.NewGuid(),
                        1,
                        "台词.wav",
                        "audio\\测试\\台词.wav")
                ]
            };

            service.Save(
                project,
                "测试.aproj",
                "audio\\audio_projects\\测试.aproj");

            Assert.IsNotNull(savedBytes);
            var json = Encoding.UTF8.GetString(savedBytes);
            var savedProject = JsonSerializer.Deserialize<AudioProjectFile>(json);
            Assert.IsNotNull(savedProject);
            Assert.AreEqual("简体中文", savedProject.Language);
            Assert.AreEqual("台词.wav", savedProject.AudioFiles.Single().WavPackFileName);
        }

        [TestMethod]
        public void AudioProjectLoad_WhenPackFileIsMissing_ReportsRequestedPath()
        {
            var service = new AudioProjectFileService(
                Mock.Of<IPackFileService>(),
                Mock.Of<IFileSaveService>(),
                Mock.Of<IStandardDialogs>());

            var exception = Assert.ThrowsException<FileNotFoundException>(
                () => service.Load(
                    "missing.aproj",
                    "audio\\audio_projects\\missing.aproj"));

            StringAssert.Contains(
                exception.Message,
                "audio\\audio_projects\\missing.aproj");
        }

        [TestMethod]
        public void AudioProjectLoad_WhenJsonIsNull_ReportsInvalidProject()
        {
            var service = new AudioProjectFileService(
                Mock.Of<IPackFileService>(),
                Mock.Of<IFileSaveService>(),
                Mock.Of<IStandardDialogs>());
            var packFile = PackFile.CreateFromBytes(
                "broken.aproj",
                Encoding.UTF8.GetBytes("null"));

            var exception = Assert.ThrowsException<InvalidDataException>(
                () => service.DeserialiseAudioProject(packFile));

            StringAssert.Contains(exception.Message, "broken.aproj");
        }

        [TestMethod]
        public void AudioProjectLoad_WhenRequiredDataIsMissing_ReportsInvalidProject()
        {
            var service = new AudioProjectFileService(
                Mock.Of<IPackFileService>(),
                Mock.Of<IFileSaveService>(),
                Mock.Of<IStandardDialogs>());
            var packFile = PackFile.CreateFromBytes(
                "incomplete.aproj",
                Encoding.UTF8.GetBytes(
                    "{\"Language\":null,\"SoundBanks\":null}"));

            var exception = Assert.ThrowsException<InvalidDataException>(
                () => service.DeserialiseAudioProject(packFile));

            StringAssert.Contains(
                exception.Message,
                "incomplete.aproj");
        }

        [TestMethod]
        public void Shortcuts_AreLimitedToTheFocusedPanel()
        {
            Assert.AreEqual(
                AudioEditorShortcut.AddRow,
                ShortcutService.ResolveShortcut(
                    Key.Q,
                    ModifierKeys.Control,
                    AudioEditorFocusTarget.AudioProjectEditor,
                    false,
                    false));
            Assert.AreEqual(
                AudioEditorShortcut.None,
                ShortcutService.ResolveShortcut(
                    Key.Q,
                    ModifierKeys.Control,
                    AudioEditorFocusTarget.AudioProjectViewer,
                    false,
                    false));
            Assert.AreEqual(
                AudioEditorShortcut.None,
                ShortcutService.ResolveShortcut(
                    Key.Delete,
                    ModifierKeys.None,
                    AudioEditorFocusTarget.AudioFilesExplorer,
                    false,
                    false));
            Assert.AreEqual(
                AudioEditorShortcut.RemoveRows,
                ShortcutService.ResolveShortcut(
                    Key.Delete,
                    ModifierKeys.None,
                    AudioEditorFocusTarget.AudioProjectViewer,
                    false,
                    false));
            Assert.AreEqual(
                AudioEditorShortcut.RemoveSettingsAudioFiles,
                ShortcutService.ResolveShortcut(
                    Key.Delete,
                    ModifierKeys.None,
                    AudioEditorFocusTarget.Settings,
                    false,
                    true));
            Assert.AreEqual(
                AudioEditorShortcut.None,
                ShortcutService.ResolveShortcut(
                    Key.C,
                    ModifierKeys.Control,
                    AudioEditorFocusTarget.AudioProjectViewer,
                    true,
                    false));
        }

        [TestMethod]
        public void EditViewerRow_LeavesOriginalInProjectUntilCommit()
        {
            var table = new DataTable();
            table.Columns.Add("Action Event");
            var row = table.Rows.Add("Play_test");
            var state = new AudioEditorStateService
            {
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    "Battle Abilities",
                    AudioProjectTreeNodeType.ActionEventType)
            };
            var command = new EditViewerRowsCommand(
                state,
                new TestEventHub());

            command.Execute([row]);

            Assert.AreEqual(DataRowState.Added, row.RowState);
            Assert.AreSame(row, state.PendingEditedViewerRows.Single());
        }

        [TestMethod]
        public void RemoveState_WhenDialoguePathUsesIt_IsRejected()
        {
            var stateGroup = StateGroup.CreateForAudioProjectFile(
                "weather",
                []);
            var state = new State("rain");
            stateGroup.States.Add(state);
            var dialogueEvent = new DialogueEvent("test_dialogue");
            dialogueEvent.StatePaths.Add(new StatePath(
                [new StatePath.Node(stateGroup, state)],
                1,
                AkBkHircType.Sound));
            var soundBank = new SoundBank(
                "test",
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            soundBank.DialogueEvents.Add(dialogueEvent);
            var editorState = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile
                {
                    StateGroups = [stateGroup],
                    SoundBanks = [soundBank]
                }
            };
            var service = new StateService(editorState);

            Assert.ThrowsException<InvalidOperationException>(
                () => service.RemoveState("weather", "rain"));
            Assert.AreSame(state, stateGroup.States.Single());
        }

        [TestMethod]
        public void RemoveMultipleStates_WhenOneIsInUse_DoesNotPartiallyRemove()
        {
            var stateGroup = StateGroup.CreateForAudioProjectFile(
                "weather",
                []);
            var dryState = new State("dry");
            var rainState = new State("rain");
            stateGroup.States.Add(dryState);
            stateGroup.States.Add(rainState);
            var dialogueEvent = new DialogueEvent("test_dialogue");
            dialogueEvent.StatePaths.Add(new StatePath(
                [new StatePath.Node(stateGroup, rainState)],
                1,
                AkBkHircType.Sound));
            var soundBank = new SoundBank(
                "test",
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            soundBank.DialogueEvents.Add(dialogueEvent);
            var editorState = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile
                {
                    StateGroups = [stateGroup],
                    SoundBanks = [soundBank]
                },
                SelectedAudioProjectExplorerNode =
                    AudioProjectTreeNode.CreateNode(
                        "weather",
                        AudioProjectTreeNodeType.StateGroup)
            };
            var table = new DataTable();
            table.Columns.Add(TableInformation.StateColumnName);
            var dryRow = table.Rows.Add("dry");
            var rainRow = table.Rows.Add("rain");
            var mutationCommand =
                new Mock<IAudioProjectMutationUICommand>();
            var mutationFactory =
                new Mock<IAudioProjectMutationUICommandFactory>();
            mutationFactory
                .Setup(x => x.Create(
                    MutationType.Remove,
                    AudioProjectTreeNodeType.StateGroup))
                .Returns(mutationCommand.Object);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new RemoveViewerRowsCommand(
                editorState,
                mutationFactory.Object,
                new StateService(editorState),
                new TestEventHub(),
                dialogs.Object);

            command.Execute([dryRow, rainRow]);

            CollectionAssert.AreEquivalent(
                new[] { dryState, rainState },
                stateGroup.States.ToArray());
            mutationCommand.Verify(
                x => x.Execute(It.IsAny<DataRow>()),
                Times.Never);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        [TestMethod]
        public void RemoveMultipleRows_WhenOneRemovalFails_RestoresProject()
        {
            var table = new DataTable();
            table.Columns.Add(TableInformation.ActionEventColumnName);
            var firstRow = table.Rows.Add("Play_first");
            var secondRow = table.Rows.Add("Play_second");
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile
                {
                    Language = "english(uk)"
                },
                SelectedAudioProjectExplorerNode =
                    AudioProjectTreeNode.CreateNode(
                        "Battle Abilities",
                        AudioProjectTreeNodeType.ActionEventType)
            };
            var callCount = 0;
            var mutationCommand =
                new Mock<IAudioProjectMutationUICommand>();
            mutationCommand
                .Setup(x => x.Execute(It.IsAny<DataRow>()))
                .Callback<DataRow>(_ =>
                {
                    if (Interlocked.Increment(ref callCount) == 1)
                    {
                        state.AudioProject.Language = "mutated";
                        return;
                    }

                    throw new InvalidOperationException("remove failed");
                });
            var mutationFactory =
                new Mock<IAudioProjectMutationUICommandFactory>();
            mutationFactory
                .Setup(x => x.Create(
                    MutationType.Remove,
                    AudioProjectTreeNodeType.ActionEventType))
                .Returns(mutationCommand.Object);
            var eventHub = new TestEventHub();
            var recovered = false;
            var changed = false;
            eventHub.Register<AudioProjectLoadedEvent>(
                this,
                e => recovered = e.IsRecovery);
            eventHub.Register<AudioProjectChangedEvent>(
                this,
                _ => changed = true);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new RemoveViewerRowsCommand(
                state,
                mutationFactory.Object,
                Mock.Of<IStateService>(),
                eventHub,
                dialogs.Object);

            command.Execute([firstRow, secondRow]);

            Assert.AreEqual(
                "english(uk)",
                state.AudioProject.Language);
            Assert.IsTrue(recovered);
            Assert.IsFalse(changed);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    "remove failed",
                    It.IsAny<string>()),
                Times.Once);
        }

        [TestMethod]
        public void RemoveDependentActionEvent_WhenPlayExists_PreservesTheModel()
        {
            var actionEventNodeName =
                Wh3ActionEventInformation.GetName(
                    Wh3ActionEventType.BattleAbilities);
            var soundBankName =
                $"{Wh3SoundBankInformation.GetName(
                    Wh3ActionEventInformation.GetSoundBank(
                        actionEventNodeName))}_test";
            var soundBank = new SoundBank(
                soundBankName,
                Wh3SoundBank.BattleIndividualMagic,
                "sfx");
            var playActionEvent = new ActionEvent(
                1,
                "Play_test",
                [],
                Wh3ActionEventType.BattleAbilities);
            var pauseActionEvent = new ActionEvent(
                2,
                "Pause_test",
                [],
                Wh3ActionEventType.BattleAbilities);
            soundBank.ActionEvents.Add(playActionEvent);
            soundBank.ActionEvents.Add(pauseActionEvent);
            var editorState = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile
                {
                    SoundBanks = [soundBank]
                },
                AudioProjectFileName = "test.aproj"
            };
            var service = new ActionEventService(
                editorState,
                Mock.Of<IAudioRepository>(),
                Mock.Of<IActionEventFactory>());

            Assert.ThrowsException<InvalidOperationException>(() =>
                service.RemoveActionEvent(
                    actionEventNodeName,
                    pauseActionEvent.Name));

            Assert.AreSame(
                pauseActionEvent,
                soundBank.GetActionEvent(pauseActionEvent.Name));
        }

        [TestMethod]
        public void ResetSettings_PreservesAudioFilesAndResetsRepetitionToggle()
        {
            var eventHub = new TestEventHub();
            var state = new AudioEditorStateService
            {
                SelectedAudioProjectExplorerNode = AudioProjectTreeNode.CreateNode(
                    "Battle Abilities",
                    AudioProjectTreeNodeType.ActionEventType)
            };
            var audioFile = new AudioFile(
                Guid.NewGuid(),
                1,
                "test.wav",
                "audio\\test.wav");
            var viewModel = new SettingsViewModel(
                eventHub,
                state,
                Mock.Of<IAudioRepository>())
            {
                AudioFiles = new ObservableCollection<AudioFile>([audioFile]),
                EnableRepetitionInterval = true
            };
            state.StoreAudioFiles([audioFile]);

            viewModel.ResetSettings();

            Assert.IsFalse(viewModel.EnableRepetitionInterval);
            Assert.AreEqual(1, viewModel.AudioFiles.Count);
            Assert.AreSame(audioFile, viewModel.AudioFiles[0]);
        }

        [DataTestMethod]
        [DataRow("测试", true, "测试")]
        [DataRow("测试.aproj", true, "测试")]
        [DataRow("  测试  ", true, "测试")]
        [DataRow("测试 项目", false, null)]
        [DataRow("bad/name", false, null)]
        [DataRow("CON", false, null)]
        [DataRow("lpt1.aproj", false, null)]
        [DataRow("project.", false, null)]
        [DataRow("..", false, null)]
        [DataRow("   ", false, null)]
        public void AudioProjectNameValidation_NormalisesOnlySafeNames(
            string input,
            bool expectedValid,
            string? expectedName)
        {
            var isValid = AudioProjectNameValidator.TryNormalize(
                input,
                out var normalizedName);

            Assert.AreEqual(expectedValid, isValid);
            Assert.AreEqual(expectedName, normalizedName);
        }

        [TestMethod]
        public void AddRowsToViewer_WhenEditedReplacementFails_RestoresProject()
        {
            var table = new DataTable();
            table.Columns.Add(TableInformation.StateColumnName);
            var originalRow = table.Rows.Add("rain");
            var replacementRow = table.Rows.Add("storm");
            var stateGroup = StateGroup.CreateForAudioProjectFile(
                "weather",
                [new State("rain")]);
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile
                {
                    Language = "english(uk)",
                    StateGroups = [stateGroup]
                },
                SelectedAudioProjectExplorerNode =
                    AudioProjectTreeNode.CreateNode(
                        "weather",
                        AudioProjectTreeNodeType.StateGroup),
                PendingEditedViewerRows = [originalRow]
            };
            var removeCommand = new Mock<IAudioProjectMutationUICommand>();
            removeCommand
                .Setup(x => x.Execute(originalRow))
                .Callback(() => state.AudioProject.StateGroups[0].States.Clear());
            var addCommand = new Mock<IAudioProjectMutationUICommand>();
            addCommand
                .Setup(x => x.Execute(replacementRow))
                .Throws(new InvalidOperationException("replacement failed"));
            var factory = new Mock<IAudioProjectMutationUICommandFactory>();
            factory
                .Setup(x => x.Create(
                    MutationType.Remove,
                    AudioProjectTreeNodeType.StateGroup))
                .Returns(removeCommand.Object);
            factory
                .Setup(x => x.Create(
                    MutationType.Add,
                    AudioProjectTreeNodeType.StateGroup))
                .Returns(addCommand.Object);
            var command = new AddRowsToViewerCommand(
                state,
                factory.Object,
                new TestEventHub());

            Assert.ThrowsException<InvalidOperationException>(
                () => command.Execute([replacementRow]));

            Assert.AreEqual(
                "rain",
                state.AudioProject.StateGroups.Single().States.Single().Name);
            Assert.AreEqual(0, state.PendingEditedViewerRows.Count);
        }

        [TestMethod]
        public void PasteViewerRows_WhenOnePasteFails_RestoresProject()
        {
            var table = new DataTable();
            table.Columns.Add("State");
            var firstRow = table.Rows.Add("first");
            var secondRow = table.Rows.Add("second");
            var state = new AudioEditorStateService
            {
                AudioProject = new AudioProjectFile
                {
                    Language = "english(uk)"
                },
                SelectedAudioProjectExplorerNode =
                    AudioProjectTreeNode.CreateNode(
                        "dialogue",
                        AudioProjectTreeNodeType.DialogueEvent)
            };
            var callCount = 0;
            var pasteCommand =
                new Mock<IAudioProjectMutationUICommand>();
            pasteCommand
                .Setup(x => x.Execute(It.IsAny<DataRow>()))
                .Callback<DataRow>(_ =>
                {
                    if (Interlocked.Increment(ref callCount) == 1)
                    {
                        state.AudioProject.Language = "mutated";
                        return;
                    }

                    throw new InvalidOperationException("paste failed");
                });
            var factory =
                new Mock<IAudioProjectMutationUICommandFactory>();
            factory
                .Setup(x => x.Create(
                    MutationType.AddByPaste,
                    AudioProjectTreeNodeType.DialogueEvent))
                .Returns(pasteCommand.Object);
            var eventHub = new TestEventHub();
            var recovered = false;
            var changed = false;
            eventHub.Register<AudioProjectLoadedEvent>(
                this,
                e => recovered = e.IsRecovery);
            eventHub.Register<AudioProjectChangedEvent>(
                this,
                _ => changed = true);
            var command = new PasteViewerRowsCommand(
                state,
                factory.Object,
                eventHub);

            Assert.ThrowsException<InvalidOperationException>(
                () => command.Execute([firstRow, secondRow]));

            Assert.AreEqual(
                "english(uk)",
                state.AudioProject.Language);
            Assert.IsTrue(recovered);
            Assert.IsFalse(changed);
        }

        [TestMethod]
        public void CopiedViewerRows_SurviveViewChangesAndClearOnNewProject()
        {
            var table = new DataTable();
            table.Columns.Add("voice");
            var sourceRow = table.Rows.Add("line_one");
            var sourceNode = AudioProjectTreeNode.CreateNode(
                "source_dialogue",
                AudioProjectTreeNodeType.DialogueEvent);
            var state = new AudioEditorStateService
            {
                SelectedAudioProjectExplorerNode = sourceNode,
                SelectedViewerRows = [sourceRow]
            };
            var eventHub = new TestEventHub();
            var viewModel = new AudioProjectViewerViewModel(
                Mock.Of<IUiCommandFactory>(),
                eventHub,
                state,
                Mock.Of<IViewerTableServiceFactory>(),
                Mock.Of<IAudioRepository>())
            {
                Table = table,
                IsCopyEnabled = true
            };

            viewModel.CopyRows();
            var copiedRow = state.CopiedViewerRows.Single();
            Assert.AreNotSame(sourceRow, copiedRow);

            table.Clear();
            table.Columns.Clear();
            Assert.AreEqual("line_one", copiedRow["voice"]);

            eventHub.Publish(new AudioProjectLoadedEvent(true));
            Assert.AreSame(copiedRow, state.CopiedViewerRows.Single());

            eventHub.Publish(new AudioProjectLoadedEvent());
            Assert.AreEqual(0, state.CopiedViewerRows.Count);
            Assert.IsNull(state.CopiedFromAudioProjectExplorerNode);
        }

        [TestMethod]
        public void NewAudioProject_InvalidNameIsNotSilentlyRewritten()
        {
            var viewModel = new NewAudioProjectViewModel(
                Mock.Of<IPackFileService>(),
                Mock.Of<IAudioEditorFileService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                Mock.Of<IStandardDialogs>());

            viewModel.AudioProjectFileName = "my project";

            Assert.AreEqual("my project", viewModel.AudioProjectFileName);
            Assert.IsFalse(viewModel.IsOkButtonEnabled);
        }

        [TestMethod]
        public async Task NewAudioProject_WhenSaveIsCancelled_KeepsDialogAndState()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetEditablePack())
                .Returns(new PackFileContainer("test.pack"));
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.Save(
                    It.IsAny<AudioProjectFile>(),
                    "test.aproj",
                    "audio\\audio_projects\\test.aproj",
                    true))
                .Returns(false);
            var closed = false;
            var viewModel = new NewAudioProjectViewModel(
                packFileService.Object,
                fileService.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                Mock.Of<IStandardDialogs>())
            {
                AudioProjectFileName = "test"
            };
            viewModel.SetCloseAction(() => closed = true);

            await viewModel.CreateAudioProject();

            Assert.IsFalse(closed);
            fileService.Verify(
                x => x.Load(
                    It.IsAny<AudioProjectFile>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>()),
                Times.Never);
            fileService.Verify(
                x => x.LoadAsync(
                    It.IsAny<AudioProjectFile>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task NewAudioProject_WithoutEditablePack_ShowsErrorAndStaysOpen()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var closed = false;
            var viewModel = new NewAudioProjectViewModel(
                Mock.Of<IPackFileService>(),
                Mock.Of<IAudioEditorFileService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                dialogs.Object)
            {
                AudioProjectFileName = "test"
            };
            viewModel.SetCloseAction(() => closed = true);

            await viewModel.CreateAudioProject();

            Assert.IsFalse(closed);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        [TestMethod]
        public async Task NewAudioProject_LocksDialogUntilAsyncLoadCompletes()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetEditablePack())
                .Returns(new PackFileContainer("test.pack"));
            var loadCanFinish = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fileService = new Mock<IAudioEditorFileService>();
            fileService
                .Setup(x => x.Save(
                    It.IsAny<AudioProjectFile>(),
                    "test.aproj",
                    "audio\\audio_projects\\test.aproj",
                    true))
                .Returns(true);
            fileService
                .Setup(x => x.LoadAsync(
                    It.IsAny<AudioProjectFile>(),
                    "test.aproj",
                    "audio\\audio_projects\\test.aproj",
                    false,
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(loadCanFinish.Task);
            var closed = false;
            var viewModel = new NewAudioProjectViewModel(
                packFileService.Object,
                fileService.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                Mock.Of<IStandardDialogs>())
            {
                AudioProjectFileName = "test"
            };
            viewModel.SetCloseAction(() => closed = true);

            var creationTask = viewModel.CreateAudioProject(
                CancellationToken.None);

            Assert.IsTrue(viewModel.IsCreating);
            Assert.IsFalse(viewModel.IsOkButtonEnabled);
            Assert.IsFalse(closed);
            Assert.IsFalse(creationTask.IsCompleted);

            loadCanFinish.SetResult(true);
            await creationTask;

            Assert.IsFalse(viewModel.IsCreating);
            Assert.IsTrue(closed);
        }

        [TestMethod]
        public void AudioProjectLoad_WhenIntegrityCheckFails_RestoresRepository()
        {
            var oldProject = new AudioProjectFile
            {
                Language = "english(uk)"
            };
            var state = new AudioEditorStateService
            {
                AudioProject = oldProject,
                AudioProjectFileName = "old.aproj",
                AudioProjectFilePath = "audio\\old.aproj"
            };
            var snapshot = new AudioRepositorySnapshot(
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                true);
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.CreateSnapshot())
                .Returns(snapshot);
            var integrity = new Mock<IAudioEditorIntegrityService>();
            integrity
                .Setup(x => x.CheckAudioProjectDialogueEventIntegrity(
                    It.IsAny<AudioProjectFile>()))
                .Throws(new InvalidOperationException("invalid project"));
            var service = new AudioEditorFileService(
                new TestEventHub(),
                Mock.Of<IAudioProjectFileService>(),
                state,
                Mock.Of<IAudioPackOutputService>(),
                repository.Object,
                integrity.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3));
            var newProject = new AudioProjectFile
            {
                Language = "english(uk)"
            };

            Assert.ThrowsException<InvalidOperationException>(
                () => service.Load(
                    newProject,
                    "new.aproj",
                    "audio\\new.aproj",
                    false));

            Assert.AreSame(oldProject, state.AudioProject);
            Assert.AreEqual("old.aproj", state.AudioProjectFileName);
            repository.Verify(x => x.Restore(snapshot), Times.Once);
        }

        [TestMethod]
        public async Task AudioProjectLoadAsync_DoesNotBlockAndCommitsOnlyAfterPreparation()
        {
            var oldProject = new AudioProjectFile
            {
                Language = "english(uk)"
            };
            var state = new AudioEditorStateService
            {
                AudioProject = oldProject,
                AudioProjectFileName = "old.aproj",
                AudioProjectFilePath = "audio\\old.aproj"
            };
            var snapshot = new AudioRepositorySnapshot(
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                true);
            using var repositoryStarted = new ManualResetEventSlim();
            using var finishRepositoryLoad = new ManualResetEventSlim();
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.CreateSnapshot())
                .Returns(snapshot);
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback((
                    List<string> _,
                    IProgress<AudioLoadProgress> _,
                    CancellationToken cancellationToken) =>
                {
                    repositoryStarted.Set();
                    finishRepositoryLoad.Wait(cancellationToken);
                });
            var service = new AudioEditorFileService(
                new TestEventHub(),
                Mock.Of<IAudioProjectFileService>(),
                state,
                Mock.Of<IAudioPackOutputService>(),
                repository.Object,
                Mock.Of<IAudioEditorIntegrityService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3));

            var loadTask = service.LoadAsync(
                new AudioProjectFile
                {
                    Language = "english(uk)"
                },
                "new.aproj",
                "audio\\new.aproj",
                false);
            try
            {
                Assert.IsTrue(repositoryStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsFalse(loadTask.IsCompleted);
                Assert.AreSame(oldProject, state.AudioProject);
            }
            finally
            {
                finishRepositoryLoad.Set();
            }

            Assert.IsTrue(await loadTask);
            Assert.AreEqual("new.aproj", state.AudioProjectFileName);
            Assert.AreEqual("audio\\new.aproj", state.AudioProjectFilePath);
            Assert.AreNotSame(oldProject, state.AudioProject);
        }

        [TestMethod]
        public async Task AudioProjectLoadAsync_WhenCancelled_RestoresRepositoryAndOldState()
        {
            var oldProject = new AudioProjectFile
            {
                Language = "english(uk)"
            };
            var state = new AudioEditorStateService
            {
                AudioProject = oldProject,
                AudioProjectFileName = "old.aproj",
                AudioProjectFilePath = "audio\\old.aproj"
            };
            var snapshot = new AudioRepositorySnapshot(
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                true);
            using var repositoryStarted = new ManualResetEventSlim();
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.CreateSnapshot())
                .Returns(snapshot);
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback((
                    List<string> _,
                    IProgress<AudioLoadProgress> _,
                    CancellationToken cancellationToken) =>
                {
                    repositoryStarted.Set();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                });
            var service = new AudioEditorFileService(
                new TestEventHub(),
                Mock.Of<IAudioProjectFileService>(),
                state,
                Mock.Of<IAudioPackOutputService>(),
                repository.Object,
                Mock.Of<IAudioEditorIntegrityService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3));
            using var cancellation = new CancellationTokenSource();

            var loadTask = service.LoadAsync(
                new AudioProjectFile
                {
                    Language = "english(uk)"
                },
                "new.aproj",
                "audio\\new.aproj",
                false,
                null,
                cancellation.Token);
            Assert.IsTrue(repositoryStarted.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                async () => await loadTask);

            Assert.AreSame(oldProject, state.AudioProject);
            Assert.AreEqual("old.aproj", state.AudioProjectFileName);
            repository.Verify(x => x.Restore(snapshot), Times.Once);
        }

        [TestMethod]
        public void AudioFilesExplorer_IgnoresOtherPacksAndUnsubscribesOnDispose()
        {
            var eventHub = new TestEventHub();
            var editablePack = new PackFileContainer("editable.pack");
            var otherPack = new PackFileContainer("other.pack");
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetEditablePack())
                .Returns(editablePack);
            var treeBuilder = new Mock<IAudioFilesTreeBuilderService>();
            treeBuilder
                .Setup(x => x.BuildTree(editablePack))
                .Returns([]);
            var viewModel = new AudioFilesExplorerViewModel(
                eventHub,
                eventHub,
                Mock.Of<IUiCommandFactory>(),
                packFileService.Object,
                new AudioEditorStateService(),
                treeBuilder.Object,
                Mock.Of<IAudioFilesTreeSearchFilterService>(),
                Mock.Of<IWaveformVisualisationCacheService>());
            treeBuilder.Invocations.Clear();

            eventHub.PublishGlobalEvent(
                new PackFileContainerFilesAddedEvent(
                    otherPack,
                    [PackFile.CreateFromBytes("other.wav", [])]));

            treeBuilder.Verify(
                x => x.BuildTree(It.IsAny<PackFileContainer>()),
                Times.Never);

            viewModel.Dispose();
            eventHub.PublishGlobalEvent(
                new PackFileContainerSetAsMainEditableEvent(editablePack));

            treeBuilder.Verify(
                x => x.BuildTree(It.IsAny<PackFileContainer>()),
                Times.Never);
        }

        [TestMethod]
        public async Task DialogueMerger_LoadsRepositoryOnlyDuringAsyncInitialisation()
        {
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.GetModdedSoundBankFilePaths("for_merging"))
                .Returns([]);
            var viewModel = new DialogueEventMergerViewModel(
                repository.Object,
                Mock.Of<ISoundBankGeneratorService>(),
                Mock.Of<IStandardDialogs>());

            repository.Verify(
                x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            await viewModel.InitializeAsync(CancellationToken.None);

            repository.Verify(
                x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.IsFalse(viewModel.IsLoading);
        }

        [TestMethod]
        public async Task DialogueMerger_WhenInitialLoadIsCancelled_DoesNotReportAnError()
        {
            using var cancellation = new CancellationTokenSource();
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    cancellation.Token))
                .Callback((
                    List<string> _,
                    IProgress<AudioLoadProgress> _,
                    CancellationToken cancellationToken) =>
                {
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                });
            var dialogs = new Mock<IStandardDialogs>();
            var viewModel = new DialogueEventMergerViewModel(
                repository.Object,
                Mock.Of<ISoundBankGeneratorService>(),
                dialogs.Object);

            await viewModel.InitializeAsync(cancellation.Token);

            Assert.IsFalse(viewModel.IsLoading);
            Assert.IsFalse(viewModel.IsBusy);
            Assert.AreEqual(
                LocalizationManager.Instance.Get(
                    "DialogueEventMerger.Cancelled"),
                viewModel.LoadStatus);
            dialogs.Verify(
                x => x.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task DialogueMerger_WhenRepositoryLoadFails_RemainsDisabled()
        {
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("load failed"));
            var viewModel = new DialogueEventMergerViewModel(
                repository.Object,
                Mock.Of<ISoundBankGeneratorService>(),
                Mock.Of<IStandardDialogs>())
            {
                SoundBankSuffix = "merged"
            };
            viewModel.ModdedSoundBanks.Add(
                new ModdedSoundBank(
                    "audio\\wwise\\english(uk)\\test_for_merging.bnk",
                    isChecked: true));

            await viewModel.InitializeAsync(CancellationToken.None);

            Assert.IsFalse(viewModel.IsOkButtonEnabled);
        }

        [TestMethod]
        public void DialogueMerger_RejectsWindowsReservedSoundBankSuffix()
        {
            var viewModel = new DialogueEventMergerViewModel(
                Mock.Of<IAudioRepository>(),
                Mock.Of<ISoundBankGeneratorService>(),
                Mock.Of<IStandardDialogs>());

            viewModel.SoundBankSuffix = "CON";

            Assert.IsFalse(viewModel.IsSoundBankSuffixSet);
            Assert.IsFalse(viewModel.IsOkButtonEnabled);
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(
                    viewModel.SoundBankSuffixError));
        }

        [TestMethod]
        public async Task DialogueMerger_GenerationCanBeCancelledWithoutClosingEarly()
        {
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.GetModdedSoundBankFilePaths("for_merging"))
                .Returns(
                [
                    "audio\\wwise\\english(uk)\\test_for_merging.bnk"
                ]);
            var generator = new Mock<ISoundBankGeneratorService>();
            generator
                .Setup(x => x.GenerateMergedDialogueEventSoundBanksAsync(
                    It.IsAny<List<string>>(),
                    "merged",
                    It.IsAny<CancellationToken>()))
                .Returns(
                    async (
                        List<string> _,
                        string _,
                        CancellationToken cancellationToken) =>
                    {
                        await Task.Delay(
                            Timeout.Infinite,
                            cancellationToken);
                        return true;
                    });
            using var lifetimeCancellation =
                new CancellationTokenSource();
            var viewModel = new DialogueEventMergerViewModel(
                repository.Object,
                generator.Object,
                Mock.Of<IStandardDialogs>());

            await viewModel.InitializeAsync(
                lifetimeCancellation.Token);
            viewModel.SoundBankSuffix = "merged";

            var generationTask =
                viewModel.GenerateMergedDialogueEventSoundBankAsync(
                    CancellationToken.None);

            Assert.IsTrue(viewModel.IsGenerating);
            Assert.IsTrue(viewModel.IsBusy);
            Assert.IsFalse(viewModel.IsOkButtonEnabled);

            lifetimeCancellation.Cancel();
            await generationTask;

            Assert.IsFalse(viewModel.IsGenerating);
            Assert.IsFalse(viewModel.IsBusy);
            Assert.AreEqual(
                LocalizationManager.Instance.Get(
                    "DialogueEventMerger.Cancelled"),
                viewModel.LoadStatus);
        }

        [TestMethod]
        public async Task AudioProjectConverter_WhenRepositoryLoadFails_RemainsDisabled()
        {
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.Load(
                    It.IsAny<List<string>>(),
                    It.IsAny<IProgress<AudioLoadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("load failed"));
            var viewModel = new AudioProjectConverterViewModel(
                Mock.Of<IStandardDialogs>(),
                Mock.Of<IAudioPackOutputService>(),
                repository.Object,
                Mock.Of<IAudioEditorFileService>(),
                Mock.Of<IAudioProjectFileService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                new Mock<VgStreamWrapper>().Object)
            {
                AudioProjectName = "project",
                OutputDirectoryPath = "audio\\audio_projects",
                WemsDirectoryPath = "C:\\wems",
                BnksDirectoryPath = "C:\\bnks",
                VOActorSubstring = "actor",
                IsUsingWwiseProject = false
            };

            await viewModel.InitializeAsync(CancellationToken.None);

            Assert.IsFalse(viewModel.IsOkButtonEnabled);
        }

        [TestMethod]
        public async Task AudioProjectConverter_RejectsEmptyVOActorFilters()
        {
            var viewModel = new AudioProjectConverterViewModel(
                Mock.Of<IStandardDialogs>(),
                Mock.Of<IAudioPackOutputService>(),
                Mock.Of<IAudioRepository>(),
                Mock.Of<IAudioEditorFileService>(),
                Mock.Of<IAudioProjectFileService>(),
                new ApplicationSettingsService(
                    GameTypeEnum.Warhammer3),
                new Mock<VgStreamWrapper>().Object)
            {
                AudioProjectName = "project",
                OutputDirectoryPath = "audio\\audio_projects",
                WemsDirectoryPath = "C:\\wems",
                BnksDirectoryPath = "C:\\bnks",
                VOActorSubstring = " ,  , ",
                IsUsingWwiseProject = false
            };

            await viewModel.InitializeAsync(
                CancellationToken.None);

            Assert.IsFalse(viewModel.IsOkButtonEnabled);

            viewModel.VOActorSubstring =
                "vo_actor_empire, vo_actor_empire";
            Assert.IsTrue(viewModel.IsOkButtonEnabled);
        }

        [TestMethod]
        public async Task AudioProjectConverter_AppendMode_RequiresExistingProjectInsteadOfNewTarget()
        {
            var selectedProject = new AudioProjectFileServiceLoadResult(
                new AudioProjectFile
                {
                    Language = "english(uk)"
                },
                "existing.aproj",
                "audio\\audio_projects\\existing.aproj");
            var audioProjectFileService =
                new Mock<IAudioProjectFileService>();
            audioProjectFileService
                .Setup(x => x.LoadFromDialog())
                .Returns(selectedProject);
            var viewModel = new AudioProjectConverterViewModel(
                Mock.Of<IStandardDialogs>(),
                Mock.Of<IAudioPackOutputService>(),
                Mock.Of<IAudioRepository>(),
                Mock.Of<IAudioEditorFileService>(),
                audioProjectFileService.Object,
                new ApplicationSettingsService(
                    GameTypeEnum.Warhammer3),
                new Mock<VgStreamWrapper>().Object)
            {
                AudioProjectName = string.Empty,
                OutputDirectoryPath = string.Empty,
                WemsDirectoryPath = "C:\\wems",
                BnksDirectoryPath = "C:\\bnks",
                VOActorSubstring = "vo_actor_cathay",
                IsUsingWwiseProject = false
            };

            await viewModel.InitializeAsync(
                CancellationToken.None);

            viewModel.IsAppendingToExistingProject = true;
            Assert.IsFalse(viewModel.IsOkButtonEnabled);

            viewModel.SetExistingAudioProjectPath();

            Assert.AreEqual(
                selectedProject.FilePath,
                viewModel.ExistingAudioProjectPath);
            Assert.IsTrue(viewModel.IsOkButtonEnabled);
        }

        [TestMethod]
        public void AudioProjectMerge_WithDifferentLanguages_IsRejected()
        {
            var baseProject = new AudioProjectFile
            {
                Language = "english(uk)"
            };
            var mergingProject = new AudioProjectFile
            {
                Language = "french"
            };

            Assert.ThrowsException<InvalidOperationException>(
                () => AudioProjectFileMerger.Merge(
                    baseProject,
                    mergingProject,
                    "base",
                    "merging"));

            Assert.AreEqual(0, baseProject.SoundBanks.Count);
        }

        [TestMethod]
        public void StatePathInsert_RejectsDuplicateNames()
        {
            var stateGroup = StateGroup.CreateForStatePath("weather");
            var state = new State("rain");
            var first = new StatePath(
                [new StatePath.Node(stateGroup, state)],
                1,
                AkBkHircType.Sound);
            var duplicate = new StatePath(
                [new StatePath.Node(stateGroup, state)],
                2,
                AkBkHircType.Sound);
            var statePaths = new List<StatePath> { first };

            Assert.ThrowsException<ArgumentException>(() =>
                statePaths.InsertAlphabetically(duplicate));
        }

        [TestMethod]
        public void AudioProjectMerge_IdenticalStatePathsAreNotDuplicated()
        {
            var baseBank = new SoundBank(
                "battle_base",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var mergingBank = new SoundBank(
                "battle_merging",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var baseDialogue = new DialogueEvent("dialogue");
            var mergingDialogue = new DialogueEvent("dialogue");
            var stateGroup = StateGroup.CreateForStatePath("weather");
            var state = new State("rain");
            baseDialogue.StatePaths.Add(new StatePath(
                [new StatePath.Node(stateGroup, state)],
                1,
                AkBkHircType.Sound));
            mergingDialogue.StatePaths.Add(new StatePath(
                [new StatePath.Node(stateGroup, state)],
                1,
                AkBkHircType.Sound));
            baseBank.DialogueEvents.Add(baseDialogue);
            mergingBank.DialogueEvents.Add(mergingDialogue);
            var baseProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [baseBank]
            };
            var mergingProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [mergingBank]
            };

            AudioProjectFileMerger.Merge(
                baseProject,
                mergingProject,
                "base",
                "merging");

            Assert.AreEqual(
                1,
                baseDialogue.StatePaths.Count);
        }

        [TestMethod]
        public void AudioProjectMerge_ConflictingStatePathNodesAreRejected()
        {
            var baseBank = new SoundBank(
                "battle_base",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var mergingBank = new SoundBank(
                "battle_merging",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var baseDialogue = new DialogueEvent("dialogue");
            var mergingDialogue = new DialogueEvent("dialogue");
            var baseStateGroup =
                StateGroup.CreateForStatePath("weather");
            var mergingStateGroup =
                StateGroup.CreateForStatePath("weather");
            mergingStateGroup.Id++;
            var baseState = new State("rain");
            var mergingState = new State("rain");
            mergingState.Id++;
            baseDialogue.StatePaths.Add(new StatePath(
                [new StatePath.Node(baseStateGroup, baseState)],
                1,
                AkBkHircType.Sound));
            mergingDialogue.StatePaths.Add(new StatePath(
                [new StatePath.Node(mergingStateGroup, mergingState)],
                1,
                AkBkHircType.Sound));
            baseBank.DialogueEvents.Add(baseDialogue);
            mergingBank.DialogueEvents.Add(mergingDialogue);
            var baseProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [baseBank]
            };
            var mergingProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [mergingBank]
            };

            Assert.ThrowsException<InvalidOperationException>(() =>
                AudioProjectFileMerger.Merge(
                    baseProject,
                    mergingProject,
                    "base",
                    "merging"));
        }

        [TestMethod]
        public void AudioProjectMerge_ConflictingIdsAreRejected()
        {
            var baseBank = new SoundBank(
                "battle_base",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var mergingBank = new SoundBank(
                "battle_merging",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            baseBank.ActionEvents.Add(new ActionEvent(
                42,
                "Play_first",
                [],
                Wh3ActionEventType.BattleAbilities));
            mergingBank.ActionEvents.Add(new ActionEvent(
                42,
                "Play_second",
                [],
                Wh3ActionEventType.BattleAbilities));
            var baseProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [baseBank]
            };
            var mergingProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [mergingBank]
            };

            Assert.ThrowsException<InvalidOperationException>(() =>
                AudioProjectFileMerger.Merge(
                    baseProject,
                    mergingProject,
                    "base",
                    "merging"));
        }

        [TestMethod]
        public void AudioProjectMerge_MergesAudioReferencesAndNormalisesBankIds()
        {
            var audioFileGuid = Guid.NewGuid();
            var baseAudioFile = new AudioFile(
                audioFileGuid,
                7,
                "line.wav",
                "audio\\line.wav")
            {
                Sounds = [1]
            };
            var mergingAudioFile = new AudioFile(
                audioFileGuid,
                7,
                "line.wav",
                "audio\\line.wav")
            {
                Sounds = [2]
            };
            var baseBank = new SoundBank(
                "battle_base",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var mergingBank = new SoundBank(
                "battle_merging",
                Wh3SoundBank.BattleIndividualMagic,
                "english(uk)");
            var action = new
                Editors.Audio.Shared.AudioProject.Models.Action(
                    10,
                    AkBkHircType.Sound,
                    AkActionType.Play,
                    20,
                    mergingBank.Id);
            mergingBank.ActionEvents.Add(new ActionEvent(
                11,
                "Play_line",
                [action],
                Wh3ActionEventType.BattleAbilities));
            var baseProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [baseBank],
                AudioFiles = [baseAudioFile]
            };
            var mergingProject = new AudioProjectFile
            {
                Language = "english(uk)",
                SoundBanks = [mergingBank],
                AudioFiles = [mergingAudioFile]
            };

            AudioProjectFileMerger.Merge(
                baseProject,
                mergingProject,
                "base",
                "merging");

            CollectionAssert.AreEqual(
                new uint[] { 1, 2 },
                baseProject.AudioFiles.Single().Sounds);
            Assert.AreEqual(
                baseBank.Id,
                baseBank.ActionEvents.Single()
                    .Actions.Single().BankId);
        }

        [TestMethod]
        public void AudioProjectMerger_RejectsSameInputsAndInvalidOutputName()
        {
            var viewModel = new AudioProjectMergerViewModel(
                Mock.Of<IStandardDialogs>(),
                Mock.Of<IPackFileService>(),
                Mock.Of<IAudioProjectFileService>(),
                Mock.Of<IAudioEditorFileService>())
            {
                OutputDirectoryPath = "audio\\audio_projects",
                BaseAudioProjectPath = "audio\\base.aproj",
                MergingAudioProjectPath = "audio\\base.aproj",
                MergedAudioProjectName = "merged"
            };

            Assert.IsFalse(viewModel.IsOkButtonEnabled);

            viewModel.MergingAudioProjectPath = "audio\\other.aproj";
            viewModel.MergedAudioProjectName = "bad name";
            Assert.IsFalse(viewModel.IsOkButtonEnabled);

            viewModel.MergedAudioProjectName = "merged";

            Assert.IsTrue(viewModel.IsOkButtonEnabled);
        }

        [TestMethod]
        public async Task DialogueMerger_WhenSelectedBanksHaveIdClashes_DoesNotGenerate()
        {
            var selectedBanks = new List<string>
            {
                "audio\\wwise\\english(uk)\\one_for_merging.bnk",
                "audio\\wwise\\english(uk)\\two_for_merging.bnk"
            };
            var integrity = new Mock<IAudioEditorIntegrityService>();
            integrity
                .Setup(x => x.CheckMergingSoundBanksIdIntegrity(
                    selectedBanks))
                .Returns(false);
            var repository = new Mock<IAudioRepository>();
            var service = new SoundBankGeneratorService(
                Mock.Of<IAudioPackOutputService>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                repository.Object,
                integrity.Object);

            Assert.IsFalse(
                await service.GenerateMergedDialogueEventSoundBanksAsync(
                    selectedBanks,
                    "merged",
                    CancellationToken.None));

            repository.Verify(
                x => x.GetVanillaDialogueEventsByBnkByLanguage(),
                Times.Never);
        }

        [TestMethod]
        public void AudioPackOutputBatch_FolderProjectUsesOneWorkspaceWrite()
        {
            var projectRoot = Path.Combine(
                Path.GetTempPath(),
                $"ae-audio-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(projectRoot);
            try
            {
                using var editablePack = FolderProjectContainer.Create(
                    projectRoot,
                    new FolderProjectSettings { Name = "工程" });
                IReadOnlyCollection<PackFileWrite>? capturedWrites = null;
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(x => x.GetEditablePack())
                    .Returns(editablePack);
                packFileService
                    .Setup(x => x.ApplyFileWrites(
                        editablePack,
                        It.IsAny<IReadOnlyCollection<PackFileWrite>>()))
                    .Callback((
                        PackFileContainer _,
                        IReadOnlyCollection<PackFileWrite> writes) =>
                        capturedWrites = writes)
                    .Returns([]);
                var fileSaveService = new Mock<IFileSaveService>();
                fileSaveService
                    .Setup(x => x.Save(
                        It.IsAny<string>(),
                        It.IsAny<byte[]>(),
                        It.IsAny<bool>()))
                    .Throws(
                        new InvalidOperationException(
                            "The per-file save path was used."));
                var service = new AudioPackOutputService(
                    packFileService.Object,
                    fileSaveService.Object,
                    Mock.Of<IStandardDialogs>());

                var result = service.SaveBatch(
                    [
                        new AudioPackOutput(
                            "first.wem",
                            @"audio\first.wem",
                            [1]),
                        new AudioPackOutput(
                            "second.wem",
                            @"audio\second.wem",
                            [2]),
                    ]);

                Assert.IsTrue(result);
                CollectionAssert.AreEqual(
                    new[]
                    {
                        @"audio\first.wem",
                        @"audio\second.wem",
                    },
                    capturedWrites!
                        .Select(write => write.Path)
                        .ToArray());
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [TestMethod]
        public void AudioPackOutputBatch_WhenSaveFails_RestoresExistingFiles()
        {
            var editablePack = new PackFileContainer("test.pack");
            var existing = PackFile.CreateFromBytes(
                "existing.bnk",
                [1, 2, 3]);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetEditablePack())
                .Returns(editablePack);
            packFileService
                .Setup(x => x.FindFile(
                    "audio\\existing.bnk",
                    editablePack))
                .Returns(existing);
            packFileService
                .Setup(x => x.FindFile(
                    "audio\\new.bnk",
                    editablePack))
                .Returns((PackFile)null!);
            var fileSaveService = new Mock<IFileSaveService>();
            fileSaveService
                .Setup(x => x.Save(
                    "audio\\existing.bnk",
                    It.IsAny<byte[]>(),
                    false))
                .Callback((
                    string _,
                    byte[] bytes,
                    bool _) => existing.DataSource = new MemorySource(bytes))
                .Returns(existing);
            fileSaveService
                .Setup(x => x.Save(
                    "audio\\new.bnk",
                    It.IsAny<byte[]>(),
                    false))
                .Throws(new InvalidOperationException("disk failed"));
            var service = new AudioPackOutputService(
                packFileService.Object,
                fileSaveService.Object,
                Mock.Of<IStandardDialogs>());

            Assert.ThrowsException<InvalidOperationException>(
                () => service.SaveBatch(
                    [
                        new AudioPackOutput(
                            "existing.bnk",
                            "audio\\existing.bnk",
                            [9]),
                        new AudioPackOutput(
                            "new.bnk",
                            "audio\\new.bnk",
                            [8])
                    ]));

            packFileService.Verify(
                x => x.SaveFile(
                    existing,
                    It.Is<byte[]>(bytes =>
                        bytes.SequenceEqual(new byte[] { 1, 2, 3 }))),
                Times.Once);
        }
    }
}
