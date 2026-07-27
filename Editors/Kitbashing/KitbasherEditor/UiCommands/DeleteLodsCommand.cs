using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.SceneNodes;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    public class DeleteLodsCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Delete all but first lod";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;


        private readonly SceneManager _sceneManager;
        private readonly CommandFactory _commandFactory;

        public DeleteLodsCommand(
            SceneManager sceneManager,
            CommandFactory commandFactory)
        {
            _sceneManager = sceneManager;
            _commandFactory = commandFactory;
        }

        public void Execute()
        {
            var rootNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            if (rootNode == null)
                return;
            var lods = rootNode.GetLodNodes();

            var itemsToDelete = lods
                .Skip(1)
                .Cast<ISceneNode>()
                .ToList();
            if (itemsToDelete.Count == 0)
                return;

            _commandFactory.Create<DeleteObjectsCommand>()
                .Configure(command => command.Configure(itemsToDelete))
                .BuildAndExecute();
        }
    }
}
