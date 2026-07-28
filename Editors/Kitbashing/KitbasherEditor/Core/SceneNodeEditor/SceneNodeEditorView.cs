using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.Events;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor
{
    public partial class SceneNodeEditorViewModel : ObservableObject, IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly ISceneNodeEditorFactory _editorFactory;
        private ISceneNode? _currentNode;

        [ObservableProperty] ISceneNodeEditor? _currentEditor;
        [ObservableProperty] string _emptyStateText;

        public SceneNodeEditorViewModel(
            IEventHub eventHub,
            ISceneNodeEditorFactory editorFactory)
        {
            _eventHub = eventHub;
            _editorFactory = editorFactory;
            _emptyStateText = GetText("Kitbash.Sidebar.SelectNode");

            _eventHub.Register<SceneNodeSelectedEvent>(this, OnSelectionChanged);
        }

        void OnSelectionChanged(SceneNodeSelectedEvent selectionChangedEvent)
        {
            var selectedNode =
                selectionChangedEvent.SelectedObjects.Count == 1
                    ? selectionChangedEvent.SelectedObjects[0]
                    : null;
            if (ReferenceEquals(_currentNode, selectedNode))
                return;

            ClearCurrentEditor();

            if (selectionChangedEvent.SelectedObjects.Count == 0)
            {
                EmptyStateText = GetText("Kitbash.Sidebar.SelectNode");
                return;
            }

            if (selectionChangedEvent.SelectedObjects.Count > 1)
            {
                EmptyStateText = GetFormat(
                    "Kitbash.Sidebar.MultipleSelected",
                    selectionChangedEvent.SelectedObjects.Count);
                return;
            }

            _currentNode = selectedNode;
            CurrentEditor = _editorFactory.Create(
                selectedNode!);
            EmptyStateText = CurrentEditor == null
                ? GetText("Kitbash.Sidebar.NoDetails")
                : string.Empty;
        }

        private void ClearCurrentEditor()
        {
            _currentNode = null;
            var editor = CurrentEditor;
            CurrentEditor = null;
            editor?.Dispose();
        }

        private static string GetText(string key) =>
            LocalizationManager.Instance?.Get(key) ?? key;

        private static string GetFormat(string key, params object[] args) =>
            LocalizationManager.Instance?.GetFormat(key, args) ?? key;

        public void Dispose()
        {
            _eventHub.UnRegister(this);
            ClearCurrentEditor();
        }
    }
}
