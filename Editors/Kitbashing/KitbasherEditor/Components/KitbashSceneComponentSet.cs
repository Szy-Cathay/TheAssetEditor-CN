using GameWorld.Core.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.Components
{
    public sealed class KitbashSceneComponentSet
    {
        public KitbashSelectionOverlayComponent SelectionOverlay { get; }
        public KitbashSelectionInputComponent SelectionInput { get; }
        public KitbashModelGizmoComponent ModelGizmo { get; }
        public KitbashSelectionSettings SelectionSettings { get; } = new();
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
            IScopedResourceLibrary resourceLibrary,
            IWpfGame scene)
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
                ModelGizmo,
                SelectionSettings,
                scene);
            SelectionOverlay = new KitbashSelectionOverlayComponent(
                selectionManager,
                renderEngine,
                resourceLibrary,
                deviceResolver,
                SelectionSettings);
            Components =
            [
                SelectionOverlay,
                SelectionInput,
                ModelGizmo,
                new SelectionGestureCapture(SelectionInput)
            ];
        }

        private sealed class SelectionGestureCapture : BaseComponent
        {
            private readonly KitbashSelectionInputComponent _selection;
            public SelectionGestureCapture(KitbashSelectionInputComponent selection)
            {
                _selection = selection;
                UpdateOrder = (int)ComponentUpdateOrderEnum.InputCapture;
            }
            public override void Update(GameTime gameTime) => _selection.CaptureSelectionGesture();
        }
    }
}
