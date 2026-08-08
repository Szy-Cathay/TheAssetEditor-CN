using GameWorld.Core.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Shared.Core.Events;

namespace Editors.KitbasherEditor.Components
{
    public sealed class KitbashSceneComponentSet
    {
        public KitbashSelectionOverlayComponent SelectionOverlay { get; }
        public KitbashSelectionInputComponent SelectionInput { get; }
        public KitbashModelGizmoComponent ModelGizmo { get; }
        public IGameComponent[] Components { get; }

        public KitbashSceneComponentSet(
            IEventHub eventHub,
            IKeyboardComponent keyboard,
            IMouseComponent mouse,
            ArcBallCamera camera,
            CommandExecutor commandExecutor,
            RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver,
            CommandFactory commandFactory,
            SelectionManager selectionManager,
            SceneManager sceneManager,
            IScopedResourceLibrary resourceLibrary)
        {
            ModelGizmo = new KitbashModelGizmoComponent(
                eventHub,
                keyboard,
                mouse,
                camera,
                commandExecutor,
                renderEngine,
                deviceResolver,
                commandFactory,
                selectionManager);
            SelectionInput = new KitbashSelectionInputComponent(
                mouse,
                keyboard,
                camera,
                selectionManager,
                deviceResolver,
                commandFactory,
                sceneManager,
                renderEngine,
                ModelGizmo);
            SelectionOverlay = new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                resourceLibrary,
                deviceResolver);
            Components =
            [
                SelectionOverlay,
                SelectionInput,
                ModelGizmo
            ];
        }
    }
}
