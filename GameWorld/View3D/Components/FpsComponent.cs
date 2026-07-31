using System;
using System.Linq;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace GameWorld.Core.Components
{
    public class FpsComponent : BaseComponent
    {
        private const float OverlayFontScale = 0.5f;
        private int _frames;
        private int _liveFrames;
        private TimeSpan _timeElapsed;
        private readonly RenderEngineComponent _renderEngineComponent;
        private readonly SceneManager _sceneManager;
        private readonly LocalizationManager _localizationManager;

        // Cached scene statistics (updated once per second)
        private int _objectCount;
        private int _vertexCount;
        private int _faceCount;

        public FpsComponent(RenderEngineComponent renderEngineComponent, SceneManager sceneManager, LocalizationManager localizationManager)
        {
            _renderEngineComponent = renderEngineComponent;
            _sceneManager = sceneManager;
            _localizationManager = localizationManager;
        }

        public override void Update(GameTime gameTime)
        {
            _timeElapsed += gameTime.ElapsedGameTime;
            if (_timeElapsed >= TimeSpan.FromSeconds(1))
            {
                _timeElapsed -= TimeSpan.FromSeconds(1);
                _frames = _liveFrames;
                _liveFrames = 0;

                // Update scene statistics
                UpdateSceneStatistics();
            }
        }

        private void UpdateSceneStatistics()
        {
            var meshNodes = SceneNodeHelper.GetChildrenOfType<IEditableGeometry>(_sceneManager.RootNode);
            _objectCount = meshNodes.Count;
            _vertexCount = 0;
            _faceCount = 0;
            foreach (var node in meshNodes)
            {
                if (node.Geometry != null)
                {
                    _vertexCount += node.Geometry.VertexCount();
                    _faceCount += node.Geometry.IndexArray.Length / 3;
                }
            }
        }

        public override void Draw(GameTime gameTime)
        {
            _liveFrames++;

            var fpsItem = new FontRenderItem(
                _renderEngineComponent,
                _localizationManager.GetFormat("Viewport.Stats.FrameRate", _frames),
                new Vector2(5, 5),
                Color.White,
                _renderEngineComponent.ViewportOverlayFont,
                OverlayFontScale);
            _renderEngineComponent.AddRenderItem(RenderBuckedId.Font, fpsItem);

            var statsItem = new FontRenderItem(
                _renderEngineComponent,
                _localizationManager.GetFormat("Viewport.Stats.Scene", _objectCount, _vertexCount, _faceCount),
                new Vector2(5, 25),
                Color.LightGray,
                _renderEngineComponent.ViewportOverlayFont,
                OverlayFontScale);
            _renderEngineComponent.AddRenderItem(RenderBuckedId.Font, statsItem);
        }
    }
}
