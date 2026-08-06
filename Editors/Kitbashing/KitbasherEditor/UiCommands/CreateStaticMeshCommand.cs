using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    public class CreateStaticMeshCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Convert the selected mesh at at the given animation frame into a static mesh";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey { get; } = null;

        private readonly AnimationsContainerComponent _animationsContainerComponent;
        private readonly SelectionManager _selectionManager;
        private readonly CommandFactory _commandFactory;
        private readonly SceneManager _sceneManager;

        public CreateStaticMeshCommand(AnimationsContainerComponent animationsContainerComponent, SelectionManager selectionManager, CommandFactory commandFactory, SceneManager sceneManager)
        {
            _animationsContainerComponent = animationsContainerComponent;
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
            _sceneManager = sceneManager;
        }

        public void Execute()
        {
            // Get the frame
            var animationPlayers = _animationsContainerComponent;
            var mainPlayer = animationPlayers.Get("MainPlayer");

            var frame = mainPlayer.GetCurrentAnimationFrame();
            if (frame == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationMustBePlaying"));
                return;
            }

            var state = _selectionManager.GetState<ObjectSelectionState>();
            if (state == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.Kitbash.SelectMesh"));
                return;
            }
            var selectedObjects = state.SelectedObjects();
            var meshes = selectedObjects.OfType<Rmv2MeshNode>().ToList();
            if (meshes.Count == 0 || meshes.Count != selectedObjects.Count)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.Kitbash.SelectOnlyMeshes"));
                return;
            }

            var root = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            var lod0 = root?.GetLodNodes().FirstOrDefault();
            if (lod0 == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.Kitbash.NoEditableLod"));
                return;
            }

            _commandFactory.Create<CreateStaticMeshFromAnimationCommand>()
                .Configure(x => x.Configure(lod0, meshes, frame))
                .BuildAndExecute();
        }
    }
}
