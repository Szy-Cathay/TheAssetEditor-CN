using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Commands.AudioProjectEditor;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Enablement;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Shortcuts;
using Editors.Audio.AudioEditor.Events.AudioProjectEditor.Table;
using Editors.Audio.AudioEditor.Events.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Events.AudioProjectViewer.Table;
using Editors.Audio.AudioEditor.Presentation.AudioProjectEditor.Table;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Shared.Table;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.Storage;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Editors.Audio.AudioEditor.Presentation.AudioProjectEditor
{
    public partial class AudioProjectEditorViewModel : ObservableObject
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IEventHub _eventHub;
        private readonly IAudioEditorStateService _audioEditorStateService;
        private readonly IEditorTableServiceFactory _tableServiceFactory;
        private readonly IAudioRepository _audioRepository;

        private readonly ILogger _logger = Logging.Create<AudioProjectEditorViewModel>();

        [ObservableProperty] private string _editorLabel;
        [ObservableProperty] private DataTable _table = new();
        [ObservableProperty] private ObservableCollection<DataGridColumn> _dataGridColumns = [];
        [ObservableProperty] private string _dataGridTag;
        [ObservableProperty] private bool _isAddRowButtonEnabled = false;
        [ObservableProperty] private bool _showModdedStatesOnly;
        [ObservableProperty] private bool _isShowModdedStatesCheckBoxEnabled = false;
        [ObservableProperty] private bool _isShowModdedStatesCheckBoxVisible = false;
        [ObservableProperty] private bool _isEditorVisible = false;
        [ObservableProperty] private bool _isEditing;
        [ObservableProperty] private string _commitButtonLabel =
            LocalizationManager.Instance.Get("AudioProjectEditor.AddRow");
        [ObservableProperty] private string _commitButtonToolTip =
            LocalizationManager.Instance.Get(
                "AudioProjectEditor.AddRow.ToolTip");

        public AudioProjectEditorViewModel (
            IUiCommandFactory uiCommandFactory,
            IEventHub eventHub,
            IAudioEditorStateService audioEditorStateService,
            IEditorTableServiceFactory tableServiceFactory,
            IAudioRepository audioRepository)
        {
            _uiCommandFactory = uiCommandFactory;
            _eventHub = eventHub;
            _audioEditorStateService = audioEditorStateService;
            _tableServiceFactory = tableServiceFactory;
            _audioRepository = audioRepository;

            EditorLabel = LocalizationManager.Instance.Get("AudioEditor.Panel.AudioProjectEditor");

            _eventHub.Register<AudioProjectLoadedEvent>(this, OnAudioProjectInitialised);
            _eventHub.Register<AudioProjectExplorerNodeSelectedEvent>(this, OnAudioProjectExplorerNodeSelected);
            _eventHub.Register<EditorTableColumnAddRequestedEvent>(this, OnEditorTableColumnAddRequested);
            _eventHub.Register<EditorTableRowAddRequestedEvent>(this, OnEditorTableRowAddRequested);
            _eventHub.Register<EditorDataGridColumnAddRequestedEvent>(this, OnEditorDataGridColumnAddRequested);
            _eventHub.Register<EditorTableRowAddedToViewerEvent>(this, OnEditorTableRowAddedToViewer);
            _eventHub.Register<ViewerTableRowEditedEvent>(this, OnViewerTableRowEdited);
            _eventHub.Register<AudioFilesChangedEvent>(this, OnAudioFilesChanged);
            _eventHub.Register<EditorDataGridTextboxTextChangedEvent>(this, OnEditorDataGridTextboxTextChanged);
            _eventHub.Register<MovieFileChangedEvent>(this, OnMovieFileChanged);
            _eventHub.Register<EditorAddRowButtonEnablementUpdateRequestedEvent>(this, OnEditorAddRowButtonEnablementUpdateRequested);
            _eventHub.Register<EditorAddRowShortcutActivatedEvent>(this, OnEditorAddRowShortcutActivated);
        }

        private void OnAudioProjectInitialised(AudioProjectLoadedEvent e)
        {
            _audioEditorStateService.StorePendingEditedViewerRows([]);
            ResetEditMode();
            ResetEditorVisibility();
            ResetEditorLabel();
            ResetButtonEnablement();
            ResetTable();
        }

        private void OnAudioProjectExplorerNodeSelected(AudioProjectExplorerNodeSelectedEvent e)
        {
            _audioEditorStateService.StorePendingEditedViewerRows([]);
            ResetEditMode();
            ResetEditorVisibility();
            ResetEditorLabel();
            ResetButtonEnablement();
            ResetTable();

            var selectedAudioProjectExplorerNode = e.TreeNode;
            if (selectedAudioProjectExplorerNode == null)
                return;

            if (selectedAudioProjectExplorerNode.IsActionEvent())
            {
                MakeEditorVisible();
                SetEditorLabel(selectedAudioProjectExplorerNode.Name);
                LoadTable(selectedAudioProjectExplorerNode.Type);
            }
            else if (selectedAudioProjectExplorerNode.IsDialogueEvent())
            {
                MakeEditorVisible();
                SetEditorLabel(WpfHelpers.DuplicateUnderscores(selectedAudioProjectExplorerNode.Name));
                LoadTable(selectedAudioProjectExplorerNode.Type);

                var moddedStatesCount = _audioEditorStateService.AudioProject.StateGroups.SelectMany(stateGroup => stateGroup.States).Count();
                if (moddedStatesCount > 0)
                    IsShowModdedStatesCheckBoxEnabled = true;
            }
            else if (selectedAudioProjectExplorerNode.IsStateGroup())
            {
                MakeEditorVisible();
                SetEditorLabel(WpfHelpers.DuplicateUnderscores(selectedAudioProjectExplorerNode.Name));
                LoadTable(selectedAudioProjectExplorerNode.Type);
            }
            else
                return;

            _logger.Here().Information($"Loaded {selectedAudioProjectExplorerNode.Type}: {selectedAudioProjectExplorerNode.Name}");

            SetAddRowButtonEnablement();
            SetShowModdedStatesOnlyButtonEnablementAndVisibility();
        }

        private void OnEditorTableColumnAddRequested(EditorTableColumnAddRequestedEvent e) => AddTableColumn(e.Column);

        private void AddTableColumn(DataColumn column)
        {
            if (!Table.Columns.Contains(column.ColumnName))
                Table.Columns.Add(column);
        }

        private void OnEditorTableRowAddRequested(EditorTableRowAddRequestedEvent e) => AddTableRow(e.Row);

        private void AddTableRow(DataRow row)
        {
            if (row.Table == Table)
                Table.Rows.Add(row);
            else
                Table.ImportRow(row);

            if (Table.Rows.Count != 1)
                throw new Exception("There must only be one row in the Editor table.");

            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            _logger.Here().Information($"Added {selectedAudioProjectExplorerNode.Type} row to Audio Project Editor table for {selectedAudioProjectExplorerNode.Name} ");
        }

        private void OnEditorDataGridColumnAddRequested(EditorDataGridColumnAddRequestedEvent e) => AddDataGridColumns(e.Column);

        private void AddDataGridColumns(DataGridColumn column)
        {
            if (column is null)
                return;

            // Prevent the same instance being added twice
            if (DataGridColumns.Contains(column))
                return;

            var headerName = column.Header?.ToString() ?? string.Empty;

            // Prevent two different instances with the same header text
            var existingColumn = DataGridColumns
                .FirstOrDefault(col => string.Equals(col.Header?.ToString(), headerName, StringComparison.Ordinal));

            if (existingColumn != null)
                DataGridColumns.Remove(existingColumn);

            DataGridColumns.Add(column);
        }


        private void OnEditorTableRowAddedToViewer(EditorTableRowAddedToViewerEvent e)
        {
            ResetEditMode();
            // Clear rows to ensure there will only one row
            Table.Rows.Clear();

            // Re-initialise table
            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            var dataGridService = _tableServiceFactory.GetService(selectedAudioProjectExplorerNode.Type);
            dataGridService.InitialiseTable(Table);

            // Handle enablement last because the row needs to be cleared before being checked
            SetAddRowButtonEnablement();
        }   

        private void OnViewerTableRowEdited(ViewerTableRowEditedEvent e)
        {
            IsEditing = true;
            CommitButtonLabel = LocalizationManager.Instance.Get(
                "AudioProjectEditor.SaveEdit");
            CommitButtonToolTip = LocalizationManager.Instance.Get(
                "AudioProjectEditor.SaveEdit.ToolTip");
            // Clear rows to ensure there will only one row
            Table.Rows.Clear();

            _eventHub.Publish(new EditorTableRowAddRequestedEvent(e.Row));
        }

        private void OnAudioFilesChanged(AudioFilesChangedEvent e) =>
            SetAddRowButtonEnablement();

        private void OnEditorDataGridTextboxTextChanged(EditorDataGridTextboxTextChangedEvent e) => SetAddRowButtonEnablement();

        private void OnMovieFileChanged(MovieFileChangedEvent e) => SetMovieFilePath(e.MovieFilePath);

        private void SetMovieFilePath(string movieFilePath)
        {
            var relativePath = Path.GetRelativePath("movies", movieFilePath);
            var withoutExtension = Path.ChangeExtension(relativePath, null);
            var slashesToUnderscores = withoutExtension.Replace("\\", "_");
            var eventName = $"Play_Movie_{slashesToUnderscores}";

            var row = Table.Rows[0];
            row[TableInformation.ActionEventColumnName] = eventName;
        }

        public void OnEditorAddRowButtonEnablementUpdateRequested(EditorAddRowButtonEnablementUpdateRequestedEvent e) => SetAddRowButtonEnablement();

        private void LoadTable(AudioProjectTreeNodeType selectedNodeType)
        {
            var tableService = _tableServiceFactory.GetService(selectedNodeType);
            tableService.Load(Table);
        }

        private void OnEditorAddRowShortcutActivated(EditorAddRowShortcutActivatedEvent e)
        {
            if (IsAddRowButtonEnabled && _audioEditorStateService.EditorRow != null)
                AddRowsToViewer();
        }

        [RelayCommand] public void AddRowsToViewer()
        {
            var rows = new List<DataRow>();
            var row = Table.Rows[0];
            rows.Add(row);

            // TODO: Add support for vocalisation Action Events when implemented
            // To hide the complexity of creating Pause / Resume / Stop Action Events
            // from the user we create them for them for the necessary types of audio
            // We put the Play Action Event first in the list so it's created before the others as they need to reference it
            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode.IsActionEvent())
            {
                var actionEventName = TableHelpers.GetActionEventNameFromRow(row);
                var actionEventSuffix = TableHelpers.RemoveActionEventPrefix(actionEventName);
                if (selectedAudioProjectExplorerNode.IsMusicActionEvent())
                {
                    var pauseActionEventRow = TableHelpers.CreateRow(Table, $"Pause_{actionEventSuffix}");
                    rows.Add(pauseActionEventRow);

                    var resumeActionEventRow = TableHelpers.CreateRow(Table, $"Resume_{actionEventSuffix}");
                    rows.Add(resumeActionEventRow);

                    var stopActionEventRow = TableHelpers.CreateRow(Table, $"Stop_{actionEventSuffix}");
                    rows.Add(stopActionEventRow);
                }
                else if (selectedAudioProjectExplorerNode.IsBattleAbilityActionEvent())
                {
                    var stopActionEventRow = TableHelpers.CreateRow(Table, $"Stop_{actionEventSuffix}");
                    rows.Add(stopActionEventRow);
                }
            }

            _uiCommandFactory.Create<AddRowsToViewerCommand>().Execute(rows);
        }

        [RelayCommand]
        public void CancelEdit()
        {
            if (!IsEditing)
                return;

            _audioEditorStateService.StorePendingEditedViewerRows([]);
            _audioEditorStateService.StoreEditorRow(null);
            ResetEditMode();
            ResetTable();
            LoadTable(
                _audioEditorStateService
                    .SelectedAudioProjectExplorerNode.Type);
            SetAddRowButtonEnablement();
            _eventHub.Publish(new AudioProjectEditCancelledEvent());
        }

        partial void OnShowModdedStatesOnlyChanged(bool value)
        {
            _audioEditorStateService.StoreModdedStatesOnly(ShowModdedStatesOnly);

            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode.IsDialogueEvent())
            {
                ResetTable();

                // Reload the table from scratch because ComboBoxes values can't be updated
                LoadTable(selectedAudioProjectExplorerNode.Type);
            }
        }

        private void SetAddRowButtonEnablement()
        {
            IsAddRowButtonEnabled = false;
            _audioEditorStateService.StoreEditorRow(null);

            if (Table.Rows.Count == 0)
                return;

            var areAudioFilesSet = AreAudioFilesSet();
            if (!areAudioFilesSet)
                return;

            var doesRowExist = DoesRowExist();
            if (doesRowExist)
            {
                IsAddRowButtonEnabled = false;
                return;
            }

            var areEditorCellsEmpty = AreEditorCellsEmpty();
            if (areEditorCellsEmpty)
            {
                IsAddRowButtonEnabled = false;
                return;
            }
            else
            {
                IsAddRowButtonEnabled = true;
                _audioEditorStateService.StoreEditorRow(Table.Rows[0]);
                return;
            }
        }

        private bool AreAudioFilesSet()
        {
            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            if (!selectedAudioProjectExplorerNode.IsStateGroup())
            {
                if (_audioEditorStateService.AudioFiles.Count == 0)
                    return false;
            }

            return true;
        }

        private bool DoesRowExist()
        {
            var row = Table.Rows[0];
            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode.IsActionEvent())
            {
                var actionEventName = TableHelpers.GetActionEventNameFromRow(row);
                var actionEvent = _audioEditorStateService.AudioProject.GetActionEvent(actionEventName);
                var isPendingEditedRow = _audioEditorStateService.PendingEditedViewerRows
                    .Any(pendingRow => string.Equals(
                        TableHelpers.GetActionEventNameFromRow(pendingRow),
                        actionEventName,
                        StringComparison.Ordinal));
                if (actionEvent != null && !isPendingEditedRow)
                    return true;
            }
            else if (selectedAudioProjectExplorerNode.IsDialogueEvent())
            {
                var dialogueEvent = _audioEditorStateService.AudioProject.GetDialogueEvent(selectedAudioProjectExplorerNode.Name);
                var statePathName = TableHelpers.GetStatePathNameFromRow(row, _audioRepository, dialogueEvent.Name);
                var statePath = dialogueEvent.GetStatePath(statePathName);
                var isPendingEditedRow = _audioEditorStateService.PendingEditedViewerRows
                    .Any(pendingRow => string.Equals(
                        TableHelpers.GetStatePathNameFromRow(
                            pendingRow,
                            _audioRepository,
                            dialogueEvent.Name),
                        statePathName,
                        StringComparison.Ordinal));
                if (statePath != null && !isPendingEditedRow)
                    return true;
            }
            else if (selectedAudioProjectExplorerNode.IsStateGroup())
            {
                var stateGroup = _audioEditorStateService.AudioProject.GetStateGroup(selectedAudioProjectExplorerNode.Name);
                var stateName = TableHelpers.GetStateNameFromRow(row);
                var state = stateGroup.GetState(stateName);
                var isPendingEditedRow = _audioEditorStateService.PendingEditedViewerRows
                    .Any(pendingRow => string.Equals(
                        TableHelpers.GetStateNameFromRow(pendingRow),
                        stateName,
                        StringComparison.Ordinal));
                if (state != null && !isPendingEditedRow)
                    return true;
            }
            return false;
        }

        private bool AreEditorCellsEmpty()
        {
            var row = Table.Rows[0];
            return Table.Columns
                .Cast<DataColumn>()
                .Any(column => row.IsNull(column) || (row[column] is string value && (string.IsNullOrEmpty(value) || value == "Play_")));
        }

        private void SetShowModdedStatesOnlyButtonEnablementAndVisibility()
        {
            var selectedAudioProjectExplorerNode = _audioEditorStateService.SelectedAudioProjectExplorerNode;
            if (selectedAudioProjectExplorerNode.IsDialogueEvent())
            {
                IsShowModdedStatesCheckBoxVisible = true;

                var moddedStatesCount = _audioEditorStateService.AudioProject.StateGroups
                    .SelectMany(stateGroup => stateGroup.States)
                    .Count();
                if (moddedStatesCount > 0)
                    IsShowModdedStatesCheckBoxEnabled = true;
                else if (moddedStatesCount == 0)
                    IsShowModdedStatesCheckBoxEnabled = false;
            }
            else
                IsShowModdedStatesCheckBoxVisible = false;
        }

        private void ResetTable()
        {            
            // Table.Clear() only removes the rows, so we need to do Table.Columns.Clear() to clear it fully. If we don't clear the table properly
            // it leads to issues where e.g. a Dialogue Event may reference a table from a previous Dialogue Event with different State Groups.
            Table.Clear();
            Table.Columns.Clear();
            DataGridColumns.Clear();
        }

        private void ResetButtonEnablement()
        {
            IsAddRowButtonEnabled = false;
            IsShowModdedStatesCheckBoxEnabled = false;
        }

        private void ResetEditMode()
        {
            IsEditing = false;
            CommitButtonLabel = LocalizationManager.Instance.Get(
                "AudioProjectEditor.AddRow");
            CommitButtonToolTip = LocalizationManager.Instance.Get(
                "AudioProjectEditor.AddRow.ToolTip");
        }

        private void SetEditorLabel(string label) =>
            EditorLabel = $"{LocalizationManager.Instance.Get("AudioEditor.Panel.AudioProjectEditor")} - {label}";

        private void ResetEditorLabel() =>
            EditorLabel = LocalizationManager.Instance.Get("AudioEditor.Panel.AudioProjectEditor");

        private void MakeEditorVisible() => IsEditorVisible = true;

        private void ResetEditorVisibility() => IsEditorVisible = false;
    }
}
