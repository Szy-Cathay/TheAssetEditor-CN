using System;
using System.IO;
﻿using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.EventHandlers;
using Editors.KitbasherEditor.Services;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels.SceneExplorer;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.MenuBarViews;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.Common;

namespace Editors.KitbasherEditor.ViewModels
{
    public partial class KitbasherViewModel : ObservableObject, 
        IEditorInterface, 
        IFileEditor,
        ISaveableEditor,
        IDropTarget<TreeNode>
    {
        private readonly ILogger _logger = Logging.Create<KitbasherViewModel>();

        private readonly KitbashViewDropHandler _dropHandler;
        private readonly KitbashSceneCreator _kitbashSceneCreator;
        private readonly FocusSelectableObjectService _focusSelectableObjectComponent;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly CommandExecutor _commandExecutor;
        private long _savedDocumentStateId;

        public IWpfGame Scene { get; set; }
        public SceneExplorerViewModel SceneExplorer { get; set; }
        public SceneNodeEditorViewModel SceneNodeEditor { get; set; }
        public MenuBarViewModel MenuBar { get; set; }
        public AnimationControllerViewModel Animation { get; set; }

        [ObservableProperty] string _displayName = "Kitbash Tool";
        [ObservableProperty] GridLength _leftColumnWidth = new(0.75, GridUnitType.Star);
        [ObservableProperty] GridLength _rightColumnWidth = new(0.25, GridUnitType.Star);

        PackFile _inputFileReference;
        public PackFile CurrentFile { get => _inputFileReference; }

        private bool _hasUnsavedChanges;

        public KitbasherViewModel(
            IEventHub eventHub,
            IWpfGame gameWorld,
            MenuBarViewModel menuBarViewModel,
            AnimationControllerViewModel animationControllerViewModel,
            SceneExplorerViewModel sceneExplorerViewModel,
            KitbashViewDropHandler dropHandler,
            KitbashSceneCreator kitbashSceneCreator,
            FocusSelectableObjectService focusSelectableObjectComponent,
            IUiCommandFactory uiCommandFactory,
            CommandExecutor commandExecutor,
            IComponentInserter componentInserter,
            SelectionManager selectionManager,
            SkeletonChangedHandler skeletonChangedHandler, 
            SceneNodeEditorViewModel sceneNodeEditorView)
        {
            _dropHandler = dropHandler;
            _kitbashSceneCreator = kitbashSceneCreator;
            _focusSelectableObjectComponent = focusSelectableObjectComponent;
            _uiCommandFactory = uiCommandFactory;
            _commandExecutor = commandExecutor;
            _savedDocumentStateId = commandExecutor.CurrentDocumentStateId;
            Scene = gameWorld;
            Animation = animationControllerViewModel;
            SceneExplorer = sceneExplorerViewModel;
            MenuBar = menuBarViewModel;
            SceneNodeEditor = sceneNodeEditorView;
            selectionManager.VertexSelectionEdgeGradientEnabled = true;
            
            // Events
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<CommandStackChangedEvent>(this, OnCommandStackChanged);
            eventHub.Register<CommandStackUndoEvent>(this, OnCommandStackUndo);
            skeletonChangedHandler.Subscribe(eventHub);
            
            // Ensure all game components are added to the editor
            componentInserter.Execute();
        }

        public void LoadFile(PackFile fileToLoad)
        {
            try
            {
                _inputFileReference = fileToLoad;
                _kitbashSceneCreator.CreateFromPackFile(fileToLoad);
                var shouldFocusScene = string.Equals(Path.GetExtension(fileToLoad.Name), ".variantmeshdefinition", StringComparison.InvariantCultureIgnoreCase) == false;
                if (shouldFocusScene)
                    _focusSelectableObjectComponent.FocusScene();
                DisplayName = fileToLoad.Name;
                _savedDocumentStateId = _commandExecutor.CurrentDocumentStateId;
                HasUnsavedChanges = false;
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Unable to load file '{fileToLoad?.Name}' \n {e.Message}");
                throw new Exception($"Unable to load file '{fileToLoad?.Name}", e);
            }
        }

        public bool Save()
        {
            var command = _uiCommandFactory.Create<SaveCommand>();
            return command.Result?.Status == true;
        }

        public void Close() { }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                _hasUnsavedChanges = value;
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        public bool AllowDrop(TreeNode node, TreeNode targeNode = null) => _dropHandler.AllowDrop(node, targeNode);
        public bool Drop(TreeNode node, TreeNode targeNode = null) => _dropHandler.Drop(node);

        void OnFileSaved(ScopedFileSavedEvent notification)
        {
            _savedDocumentStateId = _commandExecutor.CurrentDocumentStateId;
            HasUnsavedChanges = false;
            DisplayName = Path.GetFileName(notification.NewPath);
        }

        void OnCommandStackChanged(CommandStackChangedEvent notification)
        {
            UpdateUnsavedState();
        }

        void OnCommandStackUndo(CommandStackUndoEvent notification)
        {
            UpdateUnsavedState();
        }

        void UpdateUnsavedState()
        {
            HasUnsavedChanges = _commandExecutor.CurrentDocumentStateId != _savedDocumentStateId;
        }
    }
}
