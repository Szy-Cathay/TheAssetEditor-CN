using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace AssetEditor.Services
{
    public partial class EditorManager : ObservableObject, IEditorManager
    {
        private readonly ILogger _logger = Logging.Create<EditorManager>();

        private readonly IPackFileService _packFileService;
        private readonly IEditorDatabase _editorDatabase;
        private readonly Func<string, string, MessageBoxButton, MessageBoxResult> _showMessage;
        private readonly Dictionary<IEditorInterface, PackFileContainer>
            _editorOwners = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<FolderProjectContainer, HashSet<string>> _reloadingPaths = [];

        public ObservableCollection<IEditorInterface> CurrentEditorsList { get; set; } = [];
        [ObservableProperty] private int _selectedEditorIndex = -1;

        public EditorManager(IGlobalEventHub eventHub, IPackFileService packFileService, IEditorDatabase editorDatabase)
            : this(eventHub, packFileService, editorDatabase, MessageBox.Show)
        {
        }

        internal EditorManager(
            IGlobalEventHub eventHub,
            IPackFileService packFileService,
            IEditorDatabase editorDatabase,
            Func<string, string, MessageBoxButton, MessageBoxResult> showMessage)
        {
            _packFileService = packFileService;
            _editorDatabase = editorDatabase;
            _showMessage = showMessage;

            eventHub.Register<BeforePackFileContainerRemovedEvent>(this, OnBeforeRemoved);
            eventHub.Register<ForceShutdownEvent>(this, OnForceShutdownEditor);
        }

        public IList<IEditorInterface> GetAllEditors() => CurrentEditorsList;
        public int GetCurrentEditor() => SelectedEditorIndex;

        public void SetEditorAsCurrent(IEditorInterface editor)
        {
            var index = CurrentEditorsList.IndexOf(editor);
            if (index >= 0)
                SelectedEditorIndex = index;
        }

        public IEditorInterface CreateFromFile(PackFile file, EditorEnums? preferedEditor)
        {
            var owner = file == null ? null : _packFileService.GetPackFileContainer(file);
            if (owner is FolderProjectContainer project &&
                _reloadingPaths.TryGetValue(project, out var paths) &&
                paths.Contains(_packFileService.GetFullPath(file, project).Replace('/', '\\')))
            {
                return null;
            }
            return CreateFromFileCore(file, preferedEditor, owner);
        }

        private IEditorInterface CreateFromFileCore(
            PackFile file,
            EditorEnums? preferedEditor,
            PackFileContainer? owner)
        {
            if (file == null)
            {
                _logger.Here().Error($"Attempting to open file, but file is NULL");
                return null;
            }

            for (var i = 0; i < CurrentEditorsList.Count; i++)
            {
                var existingEditor = CurrentEditorsList[i];
                if (existingEditor is IFileEditor existingFileEditor &&
                    existingFileEditor.CurrentFile == file)
                {
                    _logger.Here().Information($"Attempting to open file '{file.Name}', but is is already open");
                    if (owner != null)
                        _editorOwners[existingEditor] = owner;
                    SelectedEditorIndex = i;
                    return existingEditor;
                }
            }

            var fullFileName = _packFileService.GetFullPath(file);
            var editorViewModel = _editorDatabase.Create(fullFileName, preferedEditor);
            if (editorViewModel == null)
            {
                _logger.Here().Information($"No editor selected");
                return null;
            }

            // Attempt to load the assigned file, if the editor is a fileEditor.
            // TODO: Ensure we can only get here if we have a fileEditor
            if (editorViewModel is IFileEditor fileEditor)
            {
                // Open the file
                _logger.Here().Information($"Opening {file.Name} with {editorViewModel?.GetType().Name}");
                fileEditor.LoadFile(file);
            }

            InsertEditorIntoTab(editorViewModel);
            if (owner != null)
                _editorOwners[editorViewModel] = owner;
            return editorViewModel;
        }

        public IEditorInterface Create(EditorEnums editor, Action<IEditorInterface>? onInitializeCallback = null)
        {
            var editorViewModel = _editorDatabase.Create(editor);
            if (onInitializeCallback != null)
                onInitializeCallback(editorViewModel);

            InsertEditorIntoTab(editorViewModel);
            return editorViewModel;
        }

        public Window CreateWindow(PackFile packFile, EditorEnums? preferedEditor = null)
        {
            var fullFileName = _packFileService.GetFullPath(packFile);
            var editorViewModel = _editorDatabase.Create(fullFileName, preferedEditor);

            if (editorViewModel is IFileEditor fileEditor)
                fileEditor.LoadFile(packFile);

            var toolView = _editorDatabase.GetViewTypeFromViewModel(editorViewModel.GetType());
            var instance = Activator.CreateInstance(toolView) as Control;

            var newWindow = new Window
            {
                Style = (Style)Application.Current.Resources["CustomWindowStyle"],
                Content = instance,
                DataContext = editorViewModel,
                Title = editorViewModel.DisplayName
            };

            return newWindow;
        }

        void InsertEditorIntoTab(IEditorInterface editorView)
        {
            CurrentEditorsList.Add(editorView);
            SelectedEditorIndex = CurrentEditorsList.Count - 1;
        }

        private void OnBeforeRemoved(BeforePackFileContainerRemovedEvent e)
        {
            if (!e.AllowClose)
                return;

            e.SetApprovedCloseAction(
                () => TryCloseEditorsForContainer(e.Removed));
        }

        public bool TryCloseEditorsForContainer(
            PackFileContainer container)
        {
            ArgumentNullException.ThrowIfNull(container);
            var openEditors = CurrentEditorsList
                .Where(editor => IsOwnedBy(editor, container))
                .ToList();
            if (openEditors.Count == 0)
                return true;

            var hasUnsavedChanges = openEditors
                .OfType<ISaveableEditor>()
                .Any(editor => editor.HasUnsavedChanges);
            if (hasUnsavedChanges)
            {
                var result = _showMessage(
                    LocalizationManager.Instance.Get(
                        "Msg.UnsavedChangesOnClose"),
                    LocalizationManager.Instance.Get("Msg.CloseTitle"),
                    MessageBoxButton.OKCancel);
                if (result != MessageBoxResult.OK)
                    return false;
            }
            else
            {
                var result = _showMessage(
                    LocalizationManager.Instance.GetFormat(
                        "Msg.ClosePackWithOpenFiles",
                        container.Name,
                        openEditors[0].DisplayName),
                    LocalizationManager.Instance.Get("Msg.AreYouSure"),
                    MessageBoxButton.YesNo);
                if (result != MessageBoxResult.Yes)
                    return false;
            }

            foreach (var editor in openEditors)
                DestroyEditor(editor);
            return true;
        }

        private bool IsOwnedBy(
            IEditorInterface editor,
            PackFileContainer container)
        {
            if (_editorOwners.TryGetValue(editor, out var owner))
                return ReferenceEquals(owner, container);
            if (editor is not IFileEditor fileEditor)
                return false;

            return ReferenceEquals(
                _packFileService.GetPackFileContainer(
                    fileEditor.CurrentFile),
                container);
        }

        public IEditorReloadOperation PrepareFileReload(
            FolderProjectContainer project,
            IReadOnlyCollection<string> repositoryPaths)
        {
            var paths = repositoryPaths
                .Select(path => path.Replace('/', '\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedEditor = CurrentEditorsList.ElementAtOrDefault(SelectedEditorIndex);
            var editors = CurrentEditorsList
                .Where(editor => IsOwnedBy(editor, project) &&
                    editor is IFileEditor fileEditor &&
                    paths.Contains(_packFileService.GetFullPath(fileEditor.CurrentFile, project)
                        .Replace('/', '\\')))
                .ToList();
            if (editors.OfType<ISaveableEditor>().Any(editor => editor.HasUnsavedChanges) &&
                _showMessage(
                    LocalizationManager.Instance.Get("Msg.UnsavedChangesOnClose"),
                    LocalizationManager.Instance.Get("Msg.CloseTitle"),
                    MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            {
                throw new OperationCanceledException("The file editors were not closed.");
            }

            var states = editors.Select(editor => (
                Path: _packFileService.GetFullPath(((IFileEditor)editor).CurrentFile, project),
                Editor: _editorDatabase.GetEditorInfos()
                    .FirstOrDefault(info => info.ViewModel == editor.GetType())?.EditorEnum,
                Index: CurrentEditorsList.IndexOf(editor),
                WasSelected: ReferenceEquals(editor, selectedEditor))).ToArray();
            if (!_reloadingPaths.TryGetValue(project, out var activePaths))
            {
                activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _reloadingPaths.Add(project, activePaths);
            }
            if (activePaths.Overlaps(paths))
                throw new InvalidOperationException("These file editors are already being reloaded.");
            activePaths.UnionWith(paths);
            try
            {
                foreach (var editor in editors)
                    DestroyEditor(editor);
            }
            catch
            {
                ReleasePaths();
                throw;
            }

            var reopenedEditors = new List<IEditorInterface>();
            return new EditorReloadOperation(Reload, ReleasePaths);

            void Reload()
            {
                foreach (var editor in reopenedEditors)
                    DestroyEditor(editor);
                reopenedEditors.Clear();
                if (!_packFileService.GetAllPackfileContainers().Contains(project) ||
                    !ReferenceEquals(_packFileService.GetEditablePack(), project))
                    return;

                var nextSelection = selectedEditor;
                foreach (var state in states)
                {
                    var file = _packFileService.FindFile(state.Path, project);
                    if (file == null)
                        continue;
                    var editor = CreateFromFileCore(file, state.Editor, project) ??
                        throw new InvalidOperationException("The file editor could not be reopened.");
                    reopenedEditors.Add(editor);
                    CurrentEditorsList.Move(
                        CurrentEditorsList.IndexOf(editor),
                        Math.Min(state.Index, CurrentEditorsList.Count - 1));
                    if (state.WasSelected)
                        nextSelection = editor;
                }
                if (nextSelection != null && CurrentEditorsList.Contains(nextSelection))
                    SetEditorAsCurrent(nextSelection);
            }

            void ReleasePaths()
            {
                activePaths.ExceptWith(paths);
                if (activePaths.Count == 0)
                    _reloadingPaths.Remove(project);
            }
        }

        private sealed class EditorReloadOperation(Action reload, Action release) : IEditorReloadOperation
        {
            private bool _disposed;

            public void Reload()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                reload();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                release();
            }
        }

        private void OnForceShutdownEditor(ForceShutdownEvent e)
        {
            _logger.Here().Warning($"Attempting to force shutdown editor {e.EditorHandle.DisplayName}");
            CloseTool(e.EditorHandle);
        }

        public void CloseTool(IEditorInterface tool)
        {
            if (tool is ISaveableEditor saveableEditor && saveableEditor.HasUnsavedChanges)
            {
                if (_showMessage(LocalizationManager.Instance.Get("Msg.UnsavedChangesOnClose"), LocalizationManager.Instance.Get("Msg.CloseTitle"), MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
                    return;
            }

            DestroyEditor(tool);
        }

        public  void CloseOtherTools(IEditorInterface tool)
        {
            foreach (var editorViewModel in CurrentEditorsList.ToList())
            {
                if (editorViewModel != tool)
                    CloseTool(editorViewModel);
            }
        }

        public void CloseAllTools(IEditorInterface tool)
        {
            foreach (var editorViewModel in CurrentEditorsList.ToList())
                CloseTool(editorViewModel);
        }

        private void DestroyEditor(IEditorInterface editor)
        {
            if (!CurrentEditorsList.Remove(editor))
                return;

            _editorOwners.Remove(editor);
            _editorDatabase.DestroyEditor(editor);
            editor.Close();
        }

        public void CloseToolsToLeft(IEditorInterface tool)
        {
            var index = CurrentEditorsList.IndexOf(tool);
            for (var i = index - 1; i >= 0; i--)
                CloseTool(CurrentEditorsList[i]);
        }

        public void CloseToolsToRight(IEditorInterface tool)
        {
            var index = CurrentEditorsList.IndexOf(tool);
            for (var i = CurrentEditorsList.Count - 1; i > index; i--)
                CloseTool(CurrentEditorsList[i]);
        }

        public bool ShouldBlockCloseCommand(IEditorInterface editor, bool hasUnsavedFiles)
        {
            var hasUnsavedEditorChanges = CurrentEditorsList.Where(x => x is ISaveableEditor).Cast<ISaveableEditor>().Any(x => x.HasUnsavedChanges);

            if (!(hasUnsavedFiles || hasUnsavedEditorChanges))
                return true;

            return false;
        }

        // Move to a drop handler 
        public bool Drop(IEditorInterface node, IEditorInterface targetNode = default, bool insertAfterTargetNode = default)
        {
            var nodeIndex = CurrentEditorsList.IndexOf(node);
            var targetNodeIndex = CurrentEditorsList.IndexOf(targetNode);

            // if tabs next to each other switch places
            if (Math.Abs(nodeIndex - targetNodeIndex) == 1) 
            {
                (CurrentEditorsList[nodeIndex], CurrentEditorsList[targetNodeIndex]) = (CurrentEditorsList[targetNodeIndex], CurrentEditorsList[nodeIndex]);
            }
            // if tabs are not next to each other decide based on insertAfterTargetNode
            else
            {
                if (insertAfterTargetNode)
                    targetNodeIndex += 1;

                var item = CurrentEditorsList[nodeIndex];

                CurrentEditorsList.RemoveAt(nodeIndex);

                if (targetNodeIndex > nodeIndex)
                    targetNodeIndex--;

                CurrentEditorsList.Insert(targetNodeIndex, item);
            }

            SelectedEditorIndex = CurrentEditorsList.IndexOf(node);
            return true;
        }
    }
}
