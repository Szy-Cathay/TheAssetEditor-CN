using System.Linq;
using CommunityToolkit.Diagnostics;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using GameWorld.Core.Components;

namespace GameWorld.Core.Components.Rendering
{
    public class RenderEngineComponent : BaseComponent, IDisposable
    {
        Color _backgroundColour;

        private readonly Dictionary<RasterizerStateEnum, RasterizerState> _rasterStates = [];
        private readonly IWpfGame _wpfGame;
        private readonly ResourceLibrary _resourceLibrary;
        private readonly ArcBallCamera _camera;
        private readonly Dictionary<RenderBuckedId, List<IRenderItem>> _renderItems = [];
        private readonly List<VertexPositionColor> _renderLines = [];
        private readonly List<VertexPositionColor> _overlayLines = [];
        private VertexPositionColor[] _renderLinesArray;     // Cached array to avoid ToArray() per frame
        private VertexPositionColor[] _overlayLinesArray;    // Cached array for overlay lines
        private readonly IDeviceResolver _deviceResolverComponent;
        private readonly SceneRenderParametersStore _sceneLightParameters;
        private readonly IEventHub _eventHub;
        private readonly GridComponent _gridComponent;

        bool _cullingEnabled = false;
        bool _bigSceneDepthBiasMode = false;
        bool _drawGlow = true;

        private BloomFilter _bloomFilter;
        private OutlineFilter _outlineFilter;
        private QuadRenderer _quadRenderer;
        Texture2D _whiteTexture;

        RenderTarget2D _defaultRenderTarget;
        RenderTarget2D _glowRenderTarget;
        RenderTarget2D _selectionMaskTarget;
        RenderTargetBinding[] _mainRenderTargets;
        bool _selectionOutlineRequested;

        public SpriteBatch CommonSpriteBatch { get; private set; }
        public SpriteFont DefaultFont { get; private set; }

        /// <summary>
        /// Viewport shading mode - controls how 3D objects are rendered
        /// </summary>
        public ViewportShadingMode ShadingMode { get; set; } = ViewportShadingMode.Textured;

        public RenderEngineComponent(IWpfGame wpfGame, ResourceLibrary resourceLibrary, ArcBallCamera camera, IDeviceResolver deviceResolverComponent, ApplicationSettingsService applicationSettingsService, SceneRenderParametersStore sceneLightParametersStore, IEventHub eventHub, GridComponent gridComponent)
        {
            UpdateOrder = (int)ComponentUpdateOrderEnum.RenderEngine;
            DrawOrder = (int)ComponentDrawOrderEnum.RenderEngine;

            var settings = applicationSettingsService.CurrentSettings;
            if (settings.RenderEngineBackgroundColour == BackgroundColour.Custom)
                _backgroundColour = ApplicationSettingsHelper.ParseCustomBackgroundColour(settings.CustomBackgroundColour);
            else
                _backgroundColour = ApplicationSettingsHelper.GetEnumAsColour(settings.RenderEngineBackgroundColour);
            _wpfGame = wpfGame;
            _resourceLibrary = resourceLibrary;
            _camera = camera;

            _deviceResolverComponent = deviceResolverComponent;
            _sceneLightParameters = sceneLightParametersStore;
            _eventHub = eventHub;
            _gridComponent = gridComponent;

            foreach (RenderBuckedId value in Enum.GetValues(typeof(RenderBuckedId)))
                _renderItems.Add(value, new List<IRenderItem>(100));

            _renderLines = new List<VertexPositionColor>(1000);

            _eventHub.Register<SelectionChangedEvent>(this, OnSelectionChanged);
        }

        void OnSelectionChanged(SelectionChangedEvent changedEvent)
        {
            if (changedEvent.NewState.Mode == GeometrySelectionMode.Object)
                _drawGlow = true;
            else
                _drawGlow = false;
        }

        public override void Initialize()
        {
            RebuildRasterStates(_cullingEnabled, _bigSceneDepthBiasMode);

            var device = _deviceResolverComponent.Device;

            _quadRenderer = new QuadRenderer(device);

            _bloomFilter = new BloomFilter();
            _bloomFilter.Load(device, _resourceLibrary, device.Viewport.Width, device.Viewport.Height);
            _bloomFilter.BloomPreset = BloomFilter.BloomPresets.SuperWide;

            _outlineFilter = new OutlineFilter();
            _outlineFilter.Load(device, _resourceLibrary, _quadRenderer);

            _whiteTexture = new Texture2D(_deviceResolverComponent.Device, 1, 1);
            _whiteTexture.SetData(new[] { Color.White });

            CommonSpriteBatch = new SpriteBatch(device);
            DefaultFont = _wpfGame.Content.Load<SpriteFont>("Fonts//DefaultFont");
        }

        void RebuildRasterStates(bool cullingEnabled, bool bigSceneDepthBias)
        {
            _cullingEnabled = cullingEnabled;
            _bigSceneDepthBiasMode = bigSceneDepthBias;

            // Set renderState to something we dont use, so we can rebuild the ones we care about
            _deviceResolverComponent.Device.RasterizerState = RasterizerState.CullNone;
            RasterStateHelper.Rebuild(_rasterStates, _cullingEnabled, _bigSceneDepthBiasMode);
        }

        public bool BackfaceCulling { get => _cullingEnabled; set => RebuildRasterStates(value, _bigSceneDepthBiasMode); }
        public bool LargeSceneCulling { get => _bigSceneDepthBiasMode; set => RebuildRasterStates(_cullingEnabled, value); }

        public void AddRenderItem(RenderBuckedId id, IRenderItem item)
        {
            _renderItems[id].Add(item);
        }

        public void RequestSelectionOutline()
        {
            _selectionOutlineRequested = true;
        }

        public void AddRenderLines(VertexPositionColor[] lineVertices)
        {
            Guard.IsTrue(lineVertices.Length % 2 == 0);
            _renderLines.AddRange(lineVertices);
        }

        public void AddOverlayLines(VertexPositionColor[] lineVertices)
        {
            Guard.IsTrue(lineVertices.Length % 2 == 0);
            _overlayLines.AddRange(lineVertices);
        }

        public override void Update(GameTime gameTime)
        {
            foreach (var value in _renderItems.Keys)
                _renderItems[value].Clear();

            _renderLines.Clear();
            _overlayLines.Clear();
            _selectionOutlineRequested = false;
        }

        public override void Draw(GameTime gameTime)
        {
            var device = _deviceResolverComponent.Device;
            var spriteBatch = CommonSpriteBatch;
            var screenWidth = device.Viewport.Width;
            var screenHeight = device.Viewport.Height;
            if (screenWidth <= 10 || screenHeight <= 10)
            {
                // Dont render the screen if its super small,
                // as it causes some werid corner case issues for some users
                return;
            }

            var commonShaderParameters = CommonShaderParameterBuilder.Build(_camera, _sceneLightParameters, screenHeight, screenWidth);

            _defaultRenderTarget = RenderTargetHelper.GetRenderTarget(device, _defaultRenderTarget, enableMsaa: true);
            _glowRenderTarget = RenderTargetHelper.GetRenderTarget(device, _glowRenderTarget, enableMsaa: false);

            // Configure render targets
            var backBufferRenderTarget = device.GetRenderTargets()[0].RenderTarget as RenderTarget2D;
            if (_selectionOutlineRequested)
            {
                EnsureSelectionMaskTarget(
                    device,
                    screenWidth,
                    screenHeight);
                device.SetRenderTargets(_mainRenderTargets);
                device.Clear(
                    ClearOptions.Target |
                        ClearOptions.DepthBuffer,
                    Color.Transparent,
                    1,
                    0);
            }
            else
            {
                device.SetRenderTarget(_defaultRenderTarget);
            }

            // 2D drawing
            Render2DObjects(device, commonShaderParameters);

            // Clear depth buffer before 3D rendering (SpriteBatch uses DepthStencilState.None,
            // so depth buffer may contain garbage from the new render target)
            device.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0);

            // Infinite grid (rendered before 3D objects so objects correctly occlude it)
            device.DepthStencilState = DepthStencilState.Default;
            _gridComponent.RenderGrid(device, commonShaderParameters);

            // 3D drawing - Normal scene
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.Opaque;
            Render3DObjects(commonShaderParameters, RenderingTechnique.Normal);

            // 3D drawing - Emissive (only if scene contains emissive-capable objects)
            bool hasEmissiveItems = _renderItems[RenderBuckedId.Normal].Any(item => item.SupportsTechnique(RenderingTechnique.Emissive));

            if (hasEmissiveItems)
            {
                device.SetRenderTarget(_glowRenderTarget);
                Render3DObjects(commonShaderParameters, RenderingTechnique.Emissive);
            }

            // Screen-space selection outline
            if (_selectionOutlineRequested)
            {
                _outlineFilter.Draw(_selectionMaskTarget, screenWidth, screenHeight);

                // Composite scene
                device.SetRenderTarget(backBufferRenderTarget);
                spriteBatch.Begin();
                spriteBatch.Draw(_defaultRenderTarget, new Rectangle(0, 0, screenWidth, screenHeight), Color.White);
                spriteBatch.End();

                // Draw outline on top
                var outlineTarget = _outlineFilter.GetOutlineTarget();
                if (outlineTarget != null)
                {
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                    spriteBatch.Draw(outlineTarget, new Rectangle(0, 0, screenWidth, screenHeight), Color.White);
                    spriteBatch.End();
                }
            }
            else
            {
                // No outline - just composite scene
                device.SetRenderTarget(backBufferRenderTarget);
                spriteBatch.Begin();
                spriteBatch.Draw(_defaultRenderTarget, new Rectangle(0, 0, screenWidth, screenHeight), Color.White);
                spriteBatch.End();
            }

            if (_drawGlow && hasEmissiveItems)
            {
                // While re-sizing or changing view, there is a small chance that the
                // bloomRenderTarget could be null
                var bloomRenderTarget = _bloomFilter.Draw(_glowRenderTarget, screenWidth, screenHeight);
                if (bloomRenderTarget != null)
                {
                    device.SetRenderTarget(backBufferRenderTarget);
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
                    spriteBatch.Draw(bloomRenderTarget, new Rectangle(0, 0, screenWidth, screenHeight), Color.White);
                    spriteBatch.End();
                }
            }
        }

        private void Render2DObjects(GraphicsDevice device, CommonShaderParameters commonShaderParameters)
        {
            var spriteBatch = CommonSpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            // Clear the screen
            spriteBatch.Draw(_whiteTexture, new Rectangle(0, 0, device.Viewport.Width, device.Viewport.Height), _backgroundColour);

            foreach (var item in _renderItems[RenderBuckedId.Font])
                item.Draw(device, commonShaderParameters, RenderingTechnique.Normal);
            spriteBatch.End();
        }

        void Render3DObjects(CommonShaderParameters commonShaderParameters, RenderingTechnique renderingTechnique)
        {
            var device = _deviceResolverComponent.Device;

            // Apply shading mode to the normal render bucket
            if (ShadingMode == ViewportShadingMode.Wireframe)
                device.RasterizerState = _rasterStates[RasterizerStateEnum.Wireframe];
            else
                device.RasterizerState = _rasterStates[RasterizerStateEnum.Normal];

            if (renderingTechnique == RenderingTechnique.Normal && _renderLines.Count != 0)
            {
                var shader = _resourceLibrary.GetStaticEffect(ShaderTypes.Line);
                shader.Parameters["View"].SetValue(commonShaderParameters.View);
                shader.Parameters["Projection"].SetValue(commonShaderParameters.Projection);
                shader.Parameters["World"].SetValue(Matrix.Identity);

                foreach (var pass in shader.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    // Reuse cached array instead of allocating via ToArray() each frame
                    if (_renderLinesArray == null || _renderLinesArray.Length < _renderLines.Count)
                        _renderLinesArray = new VertexPositionColor[_renderLines.Count];
                    _renderLines.CopyTo(_renderLinesArray, 0);
                    device.DrawUserPrimitives(PrimitiveType.LineList, _renderLinesArray, 0, _renderLines.Count / 2);
                }
            }

            foreach (var item in _renderItems[RenderBuckedId.Normal])
                item.Draw(device, commonShaderParameters, renderingTechnique);

            device.RasterizerState = _rasterStates[RasterizerStateEnum.Wireframe];
            foreach (var item in _renderItems[RenderBuckedId.Wireframe])
                item.Draw(device, commonShaderParameters, renderingTechnique);

            // Overlay lines rendered AFTER wireframe bucket (e.g. gradient edges for vertex selection)
            if (renderingTechnique == RenderingTechnique.Normal && _overlayLines.Count != 0)
            {
                var shader = _resourceLibrary.GetStaticEffect(ShaderTypes.Line);
                shader.Parameters["View"].SetValue(commonShaderParameters.View);
                shader.Parameters["Projection"].SetValue(commonShaderParameters.Projection);
                shader.Parameters["World"].SetValue(Matrix.Identity);

                device.RasterizerState = _rasterStates[RasterizerStateEnum.Normal];
                foreach (var pass in shader.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (_overlayLinesArray == null || _overlayLinesArray.Length < _overlayLines.Count)
                        _overlayLinesArray = new VertexPositionColor[_overlayLines.Count];
                    _overlayLines.CopyTo(_overlayLinesArray, 0);
                    device.DrawUserPrimitives(PrimitiveType.LineList, _overlayLinesArray, 0, _overlayLines.Count / 2);
                }
            }

            device.RasterizerState = _rasterStates[RasterizerStateEnum.SelectedFaces];
            foreach (var item in _renderItems[RenderBuckedId.Selection])
                item.Draw(device, commonShaderParameters, renderingTechnique);
        }

        void EnsureSelectionMaskTarget(
            GraphicsDevice device,
            int screenWidth,
            int screenHeight)
        {
            var msaaCount =
                _defaultRenderTarget.MultiSampleCount;
            if (_selectionMaskTarget == null ||
                _selectionMaskTarget.Width != screenWidth ||
                _selectionMaskTarget.Height != screenHeight ||
                _selectionMaskTarget.MultiSampleCount !=
                    msaaCount)
            {
                _selectionMaskTarget?.Dispose();
                _selectionMaskTarget = new RenderTarget2D(
                    device,
                    screenWidth,
                    screenHeight,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None,
                    msaaCount,
                    RenderTargetUsage.DiscardContents);
                _mainRenderTargets =
                [
                    new RenderTargetBinding(
                        _defaultRenderTarget),
                    new RenderTargetBinding(
                        _selectionMaskTarget)
                ];
            }
        }

        public void Dispose()
        {
            _eventHub.UnRegister(this);

            CommonSpriteBatch?.Dispose();
            CommonSpriteBatch = null;

            _bloomFilter.Dispose();
            _outlineFilter.Dispose();
            _defaultRenderTarget.Dispose();
            _glowRenderTarget.Dispose();
            _selectionMaskTarget?.Dispose();
            _selectionMaskTarget = null;
            _whiteTexture.Dispose();

            _renderLines.Clear();
            _renderItems.Clear();

            foreach (var item in _rasterStates.Values)
                item.Dispose();
            _rasterStates.Clear();
        }
    }

    /// <summary>
    /// Viewport shading mode for 3D rendering
    /// </summary>
    public enum ViewportShadingMode
    {
        Textured,   // Default: PBR materials with textures
        Solid,      // Solid fill without textures (same as Textured for now)
        Wireframe   // Wireframe only
    }
}
