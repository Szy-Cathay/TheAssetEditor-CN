using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace GameWorld.Core.Components
{
    public interface IComponentInserter
    {
        void Execute(
            View3DCoreComponentSet coreComponents,
            params IGameComponent[] editorComponents);
    }

    public class ComponentInserter : IComponentInserter
    {
        private readonly IWpfGame _wpfGame;
        public ComponentInserter(IWpfGame wpfGame)
        {
            _wpfGame = wpfGame;
        }

        public void Execute(
            View3DCoreComponentSet coreComponents,
            params IGameComponent[] editorComponents)
        {
            _wpfGame.ForceEnsureCreated();
            foreach (var component in coreComponents.Components)
                _wpfGame.AddComponent(component);
            foreach (var component in editorComponents)
                _wpfGame.AddComponent(component);
        }
    }
}
