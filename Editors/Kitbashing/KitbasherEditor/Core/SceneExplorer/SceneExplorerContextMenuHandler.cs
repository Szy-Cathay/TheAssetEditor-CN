using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Events;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;

namespace KitbasherEditor.ViewModels.SceneExplorerNodeViews
{
    public partial class SceneExplorerContextMenuHandler : ObservableObject, IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly SceneManager _sceneManager;
        private readonly CommandFactory _commandFactory;
        private readonly SelectionManager _selectionManager;
        ISceneNode _activeNode;
        IEnumerable<ISceneNode> _activeNodes;

        [ObservableProperty] ObservableCollection<ContextMenuItem> _items = [];

        public SceneExplorerContextMenuHandler(IEventHub eventHub, SceneManager sceneManager, CommandFactory commandFactory, SelectionManager selectionManager)
        {
            _eventHub = eventHub;
            _sceneManager = sceneManager;
            _commandFactory = commandFactory;
            _selectionManager = selectionManager;
            _eventHub.Register<SceneNodeSelectedEvent>(this, OnSelectionChanged);
        }

        void OnSelectionChanged(SceneNodeSelectedEvent selectionChangedEvent)
        {
            var activeNodes = selectionChangedEvent.SelectedObjects;

            Items.Clear();

            if (!activeNodes.Any())
                return;

            _activeNodes = activeNodes;
            _activeNode = activeNodes.First();

            if (_activeNodes.Count() > 1)
                return;

            if (CanMakeEditable(_activeNode))
                Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.MakeEditable"), new RelayCommand(MakeEditable)));

            if (IsUngroupable(_activeNode))
                Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.Ungroup"), new RelayCommand(Ungroup)));

            if (IsLockable(_activeNode))
                Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.Lock"), new RelayCommand(ToggleLock)));
            else if (IsUnlockable(_activeNode))
                Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.Unlock"), new RelayCommand(ToggleLock)));

            if (Items.Count != 0)
                Items.Add(null);
            Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.InvertSelection"), new RelayCommand(InvertSelection)));
            Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.SelectSimilarlyNamed"), new RelayCommand(SelectSimilar)));

            if (IsRemovable(_activeNode))
            {
                if (Items.Count != 0)
                    Items.Add(null);
                Items.Add(new ContextMenuItem(LocalizationManager.Instance.Get("Kitbash.Context.Remove"), new RelayCommand(RemoveNode)));
            }
        }

        bool CanMakeEditable(ISceneNode node)
        {
            if (node.IsEditable == false)
            {
                if (node is Rmv2ModelNode)
                    return true;
                if (node is Rmv2MeshNode)
                    return true;
                if (node is WsModelGroup)
                    return true;
            }
            return false;
        }

        bool IsUngroupable(ISceneNode node)
        {
            if (node is GroupNode gn && gn.IsUngroupable)
                return true;
            else if (node.Parent is GroupNode g && g.IsUngroupable)
                return true;

            return false;
        }

        bool IsRemovable(ISceneNode node)
        {
            if (node.Name == "Root")
                return false;
            if (node.Name == "Editable Model")
                return false;
            if (node.Name == "Reference meshes")
                return false;

            if (node is Rmv2LodNode)
            {
                if (node.Parent.Name == "Editable Model")
                    return false;
            }

            if (node is SlotNode)
                return true;

            if (node is SlotsNode)
                return true;

            if (node is SkeletonNode)
                return false;

            return true;
        }

        bool IsLockable(ISceneNode node)
        {
            if (node.IsEditable == true)
            {
                if (node is ISelectable selectable)
                {
                    if (selectable.IsSelectable == true)
                    {
                        if (node is Rmv2ModelNode)
                            return true;
                        if (node is Rmv2MeshNode)
                            return true;
                    }
                }
                else if (node is GroupNode groupNode && groupNode.IsLockable)
                {
                    if (groupNode.IsSelectable == true)
                        return true;
                }
            }

            return false;
        }

        bool IsUnlockable(ISceneNode node)
        {
            if (node.IsEditable == true)
            {
                if (node is ISelectable selectable)
                {
                    if (selectable.IsSelectable == false)
                    {
                        if (node is Rmv2ModelNode)
                            return true;
                        if (node is Rmv2MeshNode)
                            return true;
                    }
                }
                else if (node is GroupNode groupNode)
                {
                    if (groupNode.IsSelectable == false && groupNode.IsLockable)
                        return true;
                }
            }

            return false;
        }

        void RemoveNode()
        {
            _commandFactory.Create<DeleteObjectsCommand>()
                .Configure(x => x.Configure([_activeNode]))
                .BuildAndExecute();
        }

        void MakeEditable()
        {
            var mainNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            if (mainNode == null)
                return;
            _commandFactory.Create<MakeNodeEditableCommand>()
                .Configure(x => x.Configure(mainNode, _activeNode))
                .BuildAndExecute();
        }

        void Ungroup()
        {
            if (_activeNode is GroupNode gn && gn.IsUngroupable)
                _commandFactory.Create<UnGroupObjectsCommand>().Configure(x => x.Configure(_activeNode.Parent, _activeNode.Children.OfType<ISelectable>().ToList(), _activeNode)).BuildAndExecute();
            else if (_activeNode.Parent is GroupNode g &&
                     g.IsUngroupable &&
                     _activeNode is ISelectable selectable)
            {
                _commandFactory.Create<UnGroupObjectsCommand>()
                    .Configure(x => x.Configure(_activeNode.Parent.Parent, [selectable], _activeNode.Parent))
                    .BuildAndExecute();
            }
        }

        void InvertSelection()
        {
            var nodesToSelect = _activeNode.Parent.Children.Except(_activeNodes)
                .Where(x=>x is ISelectable)
                .Cast<ISelectable>()
                .ToList();

            _commandFactory.Create<ObjectSelectionCommand>()
                .Configure(x => x.Configure(nodesToSelect, false, false))
                .BuildAndExecute();
        }

        void SelectSimilar()
        {
            var m = Regex.Match(_activeNode.Name, @".*_");
            if (!m.Success)
                return;

            var selectedNodes = _activeNode.Parent.Children.Where(siblingNode =>
            {
                var siblingMatchResult = Regex.Match(siblingNode.Name, @".*_");
                return siblingMatchResult.Success && siblingMatchResult.Value == m.Value;
            });


            var nodesToSelect = selectedNodes.Where(x => x is ISelectable)
                .Cast<ISelectable>()
                .ToList();

            _commandFactory.Create<ObjectSelectionCommand>()
                .Configure(x => x.Configure(nodesToSelect, false, false))
                .BuildAndExecute();
        }

        void ToggleLock()
        {
            if (_activeNode.IsEditable == true && _activeNode is ISelectable selectable)
            {
                selectable.IsSelectable = !selectable.IsSelectable;
            }
            if (_activeNode.IsEditable == true && _activeNode is GroupNode groupNode && groupNode.IsLockable)
            {
                groupNode.IsSelectable = !groupNode.IsSelectable;
            }
        }

        public void Dispose()
        {
            _eventHub.UnRegister(this);
        }
    }
}
