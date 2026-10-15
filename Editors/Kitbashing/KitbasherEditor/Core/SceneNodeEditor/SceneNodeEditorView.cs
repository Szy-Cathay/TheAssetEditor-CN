using System.ComponentModel;
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
        private readonly SceneNodePropertyEditor _propertyEditor;
        private ISceneNode? _currentNode;
        private bool _refreshingName;

        [ObservableProperty] ISceneNodeEditor? _currentEditor;
        [ObservableProperty] string _emptyStateText;
        [ObservableProperty] string _selectedNodeName = string.Empty;

        public SceneNodeEditorViewModel(
            IEventHub eventHub,
            ISceneNodeEditorFactory editorFactory,
            SceneNodePropertyEditor propertyEditor)
        {
            _eventHub = eventHub;
            _editorFactory = editorFactory;
            _propertyEditor = propertyEditor;
            _emptyStateText = GetText("Kitbash.Sidebar.SelectNode");

            _eventHub.Register<SceneNodeSelectedEvent>(this, OnSelectionChanged);
        }

        public bool CanRenameSelectedNode => CurrentEditor != null &&
            (_currentNode is Rmv2MeshNode || _currentNode?.GetType() == typeof(GroupNode));

        public bool IsSelectedNodeNameReadOnly => !CanRenameSelectedNode;

        partial void OnCurrentEditorChanged(ISceneNodeEditor? value)
        {
            OnPropertyChanged(nameof(CanRenameSelectedNode));
            OnPropertyChanged(nameof(IsSelectedNodeNameReadOnly));
        }

        partial void OnSelectedNodeNameChanged(string value)
        {
            if (_refreshingName)
                return;
            if (!CanRenameSelectedNode || _currentNode is not { } node)
            {
                UpdateSelectedNodeName();
                return;
            }
            _propertyEditor.Update(node.Name, value, name => node.Name = name);
        }

        void OnSelectionChanged(SceneNodeSelectedEvent selectionChangedEvent)
        {
            var selectedNode =
                selectionChangedEvent.SelectedObjects.Count == 1
                    ? selectionChangedEvent.SelectedObjects[0]
                    : null;
            if (selectedNode != null && ReferenceEquals(_currentNode, selectedNode))
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
            if (_currentNode is INotifyPropertyChanged observable)
                observable.PropertyChanged += OnNodePropertyChanged;
            UpdateSelectedNodeName();
            CurrentEditor = _editorFactory.Create(
                selectedNode!);
            EmptyStateText = CurrentEditor == null
                ? GetText("Kitbash.Sidebar.NoDetails")
                : string.Empty;
        }

        private void ClearCurrentEditor()
        {
            if (_currentNode is INotifyPropertyChanged observable)
                observable.PropertyChanged -= OnNodePropertyChanged;
            _currentNode = null;
            SelectedNodeName = string.Empty;
            var editor = CurrentEditor;
            CurrentEditor = null;
            editor?.Dispose();
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(ISceneNode.Name))
                UpdateSelectedNodeName();
        }

        private void UpdateSelectedNodeName()
        {
            _refreshingName = true;
            try
            {
                SelectedNodeName = _currentNode switch
                {
                    MainEditableNode => GetText("MainNode.Header"),
                    SkeletonNode { Skeleton: not null } skeleton => skeleton.Skeleton.SkeletonName,
                    _ => _currentNode?.Name ?? string.Empty
                };
            }
            finally
            {
                _refreshingName = false;
            }
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
