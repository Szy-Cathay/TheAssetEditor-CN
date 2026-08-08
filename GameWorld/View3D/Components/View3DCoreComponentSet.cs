using System.Collections.Generic;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Components
{
    public sealed class View3DCoreComponentSet
    {
        public View3DCoreComponentSet(
            CommandStackRenderer commandStackRenderer,
            IKeyboardComponent keyboard,
            IMouseComponent mouse,
            FpsComponent fps,
            ArcBallCamera camera,
            NavigationGizmoComponent navigationGizmo,
            SceneManager sceneManager,
            RenderEngineComponent renderEngine,
            GridComponent grid,
            AnimationsContainerComponent animations,
            LightControllerComponent lightController)
        {
            Components =
            [
                commandStackRenderer,
                keyboard,
                mouse,
                fps,
                camera,
                navigationGizmo,
                sceneManager,
                renderEngine,
                grid,
                animations,
                lightController,
            ];
        }

        public IReadOnlyList<IGameComponent> Components { get; }
    }
}
