using Editors.KitbasherEditor.ChildEditors.MaterialSelection;
using Editors.KitbasherEditor.Commands;
using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Misc;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    internal class AssignMaterialFromOtherMeshUiCommand : IScopedKitbasherUiCommand
    {
        private readonly SelectionManager _selectionManager;
        private readonly SceneManager _sceneManager;
        private readonly IAbstractFormFactory<MaterialSourceWindow> _windowFactory;
        private readonly CommandFactory _commandFactory;

        public string ToolTip { get; set; } = "Assign a already know material to selected objects";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey => null;

        public AssignMaterialFromOtherMeshUiCommand(SelectionManager selectionManager, SceneManager sceneManager,
            IAbstractFormFactory<MaterialSourceWindow> windowFactory, CommandFactory commandFactory)
        {
            _selectionManager = selectionManager;
            _sceneManager = sceneManager;
            _windowFactory = windowFactory;
            _commandFactory = commandFactory;
        }

        public void Execute()
        {
            if (_selectionManager.GetState() is not ObjectSelectionState selection)
                return;
            var targets = selection.SelectedObjects().OfType<Rmv2MeshNode>().ToList();
            if (targets.Count == 0)
                return;

            var sources = _sceneManager.GetEnumeratorConditional(node => node is Rmv2MeshNode).OfType<Rmv2MeshNode>();
            using var window = _windowFactory.Create();
            window.Initialize(sources, targets.Count);
            if (window.ShowDialog() != true || window.SelectedMesh == null)
                return;

            _commandFactory.Create<AssignMaterialFromOtherMeshCommand>()
                .Configure(command => command.Configure(window.SelectedMesh.Material, targets))
                .BuildAndExecute();
        }
    }
}
