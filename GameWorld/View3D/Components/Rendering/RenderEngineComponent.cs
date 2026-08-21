using System.IO;
using System.Linq;
using CommunityToolkit.Diagnostics;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Core.Settings;
using GameWorld.Core.Components;

namespace GameWorld.Core.Components.Rendering
{
    public class RenderEngineComponent : BaseComponent, IDisposable
    {
        private readonly Serilog.ILogger _logger =
            Logging.Create<RenderEngineComponent>();
        Color _backgroundColour;

        private readonly Dictionary<RasterizerStateEnum, RasterizerState> _rasterStates = [];
        private readonly IWpfGame _wpfGame;
        private readonly ResourceLibrary _resourceLibrary;
        private readonly ArcBallCamera _camera;
        private readonly Dictionary<RenderBuckedId, List<IRenderItem>> _renderItems = [];
        private readonly List<VertexPositionColor> _renderLines = [];
        private readonly List<VertexPositionColor> _overlayLines = [];
        private readonly List<VertexPositionColor>
            _translucentPreviewTriangles = [];
        private readonly List<EdgeData> _previewEdges = [];
        private VertexPositionColor[] _renderLinesArray;     // Cached array to avoid ToArray() per frame
        private VertexPositionColor[] _overlayLinesArray;    // Cached array for overlay lines
        private VertexPositionColor[]? _previewTriangleArray;
        private EdgeData[]? _previewEdgeArray;
        private EdgeQuadInstanceMesh? _previewEdgeRenderer;
        private readonly IDeviceResolver _deviceResolverComponent;
        private readonly SceneRenderParametersStore _sceneLightParameters;
        private readonly IEventHub _eventHub;
        private readonly GridComponent _gridComponent;

        bool _cullingEnabled = false;
        bool _bigSceneDepthBiasMode = false;
        bool _drawGlow = true;
        private readonly PendingRenderCapture _pendingCapture =
            new();

        private BloomFilter _bloomFilter;
        private BloomFilter _captureBloomFilter;
        private OutlineFilter _outlineFilter;
        private QuadRenderer _quadRenderer;
        Texture2D _whiteTexture;

        RenderTarget2D _defaultRenderTarget;
        RenderTarget2D _glowRenderTarget;
        RenderTarget2D _selectionMaskTarget;
        RenderTarget2D _transparentCaptureTarget;
        RenderTargetBinding[] _mainRenderTargets;
        bool _selectionOutlineRequested;

        public SpriteBatch CommonSpriteBatch { get; private set; }
        public SpriteFont DefaultFont { get; private set; }
        public SpriteFont ViewportOverlayFont { get; private set; }

        private ViewportShadingMode _shadingMode =
            ViewportShadingMode.MaterialPreview;

        public event Action<ViewportShadingMode>? ShadingModeChanged;

        /// <summary>
        /// Viewport shading mode - controls how 3D objects are rendered
        /// </summary>
        public ViewportShadingMode ShadingMode
        {
            get => _shadingMode;
            set
            {
                if (_shadingMode == value)
                    return;

                _shadingMode = value;
                ShadingModeChanged?.Invoke(value);
            }
        }
        public bool SquareViewport { get; set; }
        public RenderTarget2D? LastFrame =>
            SquareViewport ? _transparentCaptureTarget : null;

        public RenderEngineComponent(IWpfGame wpfGame, ResourceLibrary resourceLibrary, ArcBallCamera camera, IDeviceResolver deviceResolverComponent, ApplicationSettingsService applicationSettingsService, SceneRenderParametersStore sceneLightParametersStore, IEventHub eventHub, GridComponent gridComponent)
        {
            UpdateOrder = (int)ComponentUpdateOrderEnum.RenderEngine;
            DrawOrder = (int)ComponentDrawOrderEnum.RenderEngine;

            _wpfGame = wpfGame;
            _resourceLibrary = resourceLibrary;
            _camera = camera;

            _deviceResolverComponent = deviceResolverComponent;
            _sceneLightParameters = sceneLightParametersStore;
            _eventHub = eventHub;
            _gridComponent = gridComponent;

            ApplyViewportSettings(
                ViewportRenderSettings.From(
                    applicationSettingsService.CurrentSettings));

            foreach (RenderBuckedId value in Enum.GetValues(typeof(RenderBuckedId)))
                _renderItems.Add(value, new List<IRenderItem>(100));

            _renderLines = new List<VertexPositionColor>(1000);

            _eventHub.Register<SelectionChangedEvent>(this, OnSelectionChanged);
            _eventHub.Register<ViewportRenderSettingsChangedEvent>(
                this,
                OnViewportRenderSettingsChanged);
        }

        private void OnViewportRenderSettingsChanged(
            ViewportRenderSettingsChangedEvent changedEvent)
        {
            ApplyViewportSettings(changedEvent.Settings);
        }

        private void ApplyViewportSettings(
            ViewportRenderSettings settings)
        {
            _backgroundColour = settings.BackgroundColour ==
                BackgroundColour.Custom
                    ? ApplicationSettingsHelper.ParseCustomBackgroundColour(
                        settings.CustomBackgroundColour)
                    : ApplicationSettingsHelper.GetEnumAsColour(
                        settings.BackgroundColour);

            if (_rasterStates.Count == 0)
                _cullingEnabled = settings.SimulateGameBackfaces;
            else if (_cullingEnabled != settings.SimulateGameBackfaces)
                RebuildRasterStates(
                    settings.SimulateGameBackfaces,
                    _bigSceneDepthBiasMode);

            _gridComponent.ShowGrid = settings.ShowGrid;
            _gridComponent.GridColur =
                ApplicationSettingsHelper.ParseCustomBackgroundColour(
                    settings.GridColour).ToVector3();

            _sceneLightParameters.ApplyGlobalLighting(settings);
        }

        public void SaveNextFrame(
            SaveRenderImageSettings settings)
        {
            _pendingCapture.Request(settings);
            _logger.Here().Information(
                "Photo Studio capture requested: {Name}",
                settings.Name);
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

            _captureBloomFilter = new BloomFilter();
            _captureBloomFilter.Load(
                device,
                _resourceLibrary,
                device.Viewport.Width,
                device.Viewport.Height,
                quadRenderer: _quadRenderer,
                cloneEffect: true);
            _captureBloomFilter.BloomPreset =
                BloomFilter.BloomPresets.SuperWide;

            _outlineFilter = new OutlineFilter();
            _outlineFilter.Load(device, _resourceLibrary, _quadRenderer);

            _whiteTexture = new Texture2D(_deviceResolverComponent.Device, 1, 1);
            _whiteTexture.SetData(new[] { Color.White });

            CommonSpriteBatch = new SpriteBatch(device);
            DefaultFont = _wpfGame.Content.Load<SpriteFont>("Fonts//DefaultFont");
            ViewportOverlayFont = _wpfGame.Content.Load<SpriteFont>(
                "Fonts//ViewportOverlayFont");
            _previewEdgeRenderer = new EdgeQuadInstanceMesh(
                device,
                _resourceLibrary.GetStaticEffect(ShaderTypes.EdgeQuad));
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

        public void AddTranslucentPreviewTriangles(
            VertexPositionColor[] triangleVertices)
        {
            Guard.IsTrue(triangleVertices.Length % 3 == 0);
            _translucentPreviewTriangles.AddRange(triangleVertices);
        }

        public void AddPreviewEdges(EdgeData[] edges)
        {
            _previewEdges.AddRange(edges);
        }

        public override void Update(GameTime gameTime)
        {
            foreach (var value in _renderItems.Keys)
                _renderItems[value].Clear();

            _renderLines.Clear();
            _overlayLines.Clear();
            _translucentPreviewTriangles.Clear();
            _previewEdges.Clear();
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
                if (SquareViewport)
                    device.Clear(Color.Transparent);
            }

            // 2D drawing
            Render2DObjects(device, commonShaderParameters);

            // Clear depth buffer before 3D rendering (SpriteBatch uses DepthStencilState.None,
            // so depth buffer may contain garbage from the new render target)
            device.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0);

            // Infinite grid (rendered before 3D objects so objects correctly occlude it)
            device.DepthStencilState = DepthStencilState.Default;
            _gridComponent.RenderGrid(device, commonShaderParameters);

            var shadingPipeline =
                ViewportShadingPolicy.Resolve(ShadingMode);

            // 3D drawing - selected viewport surface pipeline
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.Opaque;
            Render3DObjects(
                commonShaderParameters,
                shadingPipeline.SurfaceTechnique);

            // Editing modes deliberately skip texture-driven emissive bloom.
            var hasEmissiveItems =
                shadingPipeline.EnableBloom &&
                _renderItems[RenderBuckedId.Normal]
                    .Any(item => item.SupportsTechnique(
                        RenderingTechnique.Emissive));
            Texture2D? bloomRenderTarget = null;

            if (hasEmissiveItems)
            {
                device.SetRenderTarget(_glowRenderTarget);
                device.Clear(Color.Transparent);
                Render3DObjects(commonShaderParameters, RenderingTechnique.Emissive);

                if (_drawGlow)
                {
                    bloomRenderTarget = _bloomFilter.Draw(
                        _glowRenderTarget,
                        screenWidth,
                        screenHeight);
                }
            }

            // Screen-space selection outline
            if (_selectionOutlineRequested)
            {
                _outlineFilter.Draw(_selectionMaskTarget, screenWidth, screenHeight);

                // Composite scene
                device.SetRenderTarget(backBufferRenderTarget);
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend);
                DrawViewportBackground(
                    spriteBatch,
                    screenWidth,
                    screenHeight);
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
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend);
                DrawViewportBackground(
                    spriteBatch,
                    screenWidth,
                    screenHeight);
                spriteBatch.Draw(_defaultRenderTarget, new Rectangle(0, 0, screenWidth, screenHeight), Color.White);
                spriteBatch.End();
            }

            if (bloomRenderTarget != null)
            {
                device.SetRenderTarget(backBufferRenderTarget);
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Additive);
                spriteBatch.Draw(
                    bloomRenderTarget,
                    new Rectangle(0, 0, screenWidth, screenHeight),
                    Color.White);
                spriteBatch.End();
            }

            if (SquareViewport)
            {
                CaptureTransparentFrame(
                    device,
                    spriteBatch,
                    bloomRenderTarget,
                    screenWidth,
                    screenHeight);
            }

            HandlePendingCapture(screenWidth, screenHeight);
        }

        private void DrawViewportBackground(
            SpriteBatch spriteBatch,
            int width,
            int height)
        {
            if (SquareViewport)
            {
                spriteBatch.Draw(
                    _whiteTexture,
                    new Rectangle(0, 0, width, height),
                    _backgroundColour);
            }
        }

        private void HandlePendingCapture(
            int screenWidth,
            int screenHeight)
        {
            var settings = _pendingCapture.Consume();
            if (settings == null)
                return;

            if (!RenderCaptureMath.TryGetCaptureSize(
                    screenWidth,
                    screenHeight,
                    settings.ImageUpScaleFactor,
                    out var width,
                    out var height))
            {
                _logger.Here().Error(
                    "Rejected Photo Studio capture size {Width}x{Height} at {Scale}x",
                    screenWidth,
                    screenHeight,
                    settings.ImageUpScaleFactor);
                ReportCaptureFailure(
                    settings,
                    new InvalidOperationException(
                        $"Unsupported Photo Studio capture size: " +
                        $"{screenWidth}x{screenHeight} at " +
                        $"{settings.ImageUpScaleFactor}x."));
                return;
            }

            var device = _deviceResolverComponent.Device;
            var state = GraphicsDeviceStateSnapshot.Capture(device);
            RenderTarget2D outputTarget = null;
            Exception? renderFailure = null;
            try
            {
                using var normalTarget =
                    PhotoCaptureSurface.Render(
                        device,
                        width,
                        height,
                        captureDevice =>
                            DrawPhotoCaptureScene(
                                captureDevice,
                                width,
                                height,
                                RenderingTechnique.Normal));

                var hasEmissiveItems =
                    _renderItems[RenderBuckedId.Normal]
                        .Any(item =>
                            item.IncludeInPhotoCapture &&
                            item.SupportsTechnique(
                                RenderingTechnique.Emissive));
                using var glowTarget = hasEmissiveItems
                    ? PhotoCaptureSurface.Render(
                        device,
                        width,
                        height,
                        captureDevice =>
                            DrawPhotoCaptureScene(
                                captureDevice,
                                width,
                                height,
                                RenderingTechnique.Emissive))
                    : null;

                outputTarget = new RenderTarget2D(
                    device,
                    width,
                    height,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None,
                    0,
                    RenderTargetUsage.PreserveContents);
                device.SetRenderTarget(outputTarget);
                device.Clear(Color.Transparent);
                CommonSpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Opaque);
                CommonSpriteBatch.Draw(
                    normalTarget,
                    new Rectangle(0, 0, width, height),
                    Color.White);
                CommonSpriteBatch.End();

                if (glowTarget != null)
                {
                    var bloomTarget = _captureBloomFilter.Draw(
                        glowTarget,
                        width,
                        height);
                    if (bloomTarget != null)
                    {
                        device.SetRenderTarget(outputTarget);
                        CommonSpriteBatch.Begin(
                            SpriteSortMode.Deferred,
                            BlendState.Additive);
                        CommonSpriteBatch.Draw(
                            bloomTarget,
                            new Rectangle(0, 0, width, height),
                            Color.White);
                        CommonSpriteBatch.End();
                    }
                }
            }
            catch (Exception exception)
            {
                outputTarget?.Dispose();
                outputTarget = null;
                _logger.Here().Error(
                    exception,
                    "Photo Studio capture rendering failed");
                renderFailure = exception;
            }
            finally
            {
                state.Restore(device);
                _ = _camera.ProjectionMatrix;
            }

            if (renderFailure != null)
            {
                ReportCaptureFailure(settings, renderFailure);
                return;
            }

            if (outputTarget == null)
                return;

            Exception? saveFailure = null;
            try
            {
                SavePhotoCapture(outputTarget, settings);
            }
            catch (Exception exception)
            {
                _logger.Here().Error(
                    exception,
                    "Photo Studio capture saving failed");
                saveFailure = exception;
            }
            finally
            {
                outputTarget.Dispose();
            }

            if (saveFailure != null)
                ReportCaptureFailure(settings, saveFailure);
        }

        private void ReportCaptureFailure(
            SaveRenderImageSettings settings,
            Exception exception)
        {
            try
            {
                settings.FailureHandler?.Invoke(exception);
            }
            catch (Exception callbackException)
            {
                _logger.Here().Error(
                    callbackException,
                    "Photo Studio capture failure handler failed");
            }
        }

        private void DrawPhotoCaptureScene(
            GraphicsDevice device,
            int width,
            int height,
            RenderingTechnique technique)
        {
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.Opaque;
            var parameters = CommonShaderParameterBuilder.Build(
                _camera,
                _sceneLightParameters,
                height,
                width);
            PhotoCaptureSceneRenderer.Draw(
                device,
                parameters,
                _renderItems[RenderBuckedId.Normal],
                technique,
                _rasterStates[RasterizerStateEnum.Normal]);
        }

        private static void SavePhotoCapture(
            RenderTarget2D target,
            SaveRenderImageSettings settings)
        {
            DirectoryHelper.EnsureCreated(settings.OutputFolder);
            var name = Path.GetFileName(settings.Name);
            if (string.IsNullOrWhiteSpace(name))
                name = "Screenshot";

            var outputPath = Path.Combine(
                settings.OutputFolder,
                $"{name}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            using (var stream = File.Create(outputPath))
            {
                target.SaveAsPng(
                    stream,
                    target.Width,
                    target.Height);
            }

            if (settings.OpenFolder)
                DirectoryHelper.OpenFolderAndSelectFile(outputPath);
        }

        private void Render2DObjects(GraphicsDevice device, CommonShaderParameters commonShaderParameters)
        {
            var spriteBatch = CommonSpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            if (!SquareViewport)
            {
                spriteBatch.Draw(
                    _whiteTexture,
                    new Rectangle(
                        0,
                        0,
                        device.Viewport.Width,
                        device.Viewport.Height),
                    _backgroundColour);
            }

            foreach (var item in _renderItems[RenderBuckedId.Font])
                item.Draw(device, commonShaderParameters, RenderingTechnique.Normal);
            spriteBatch.End();
        }

        void Render3DObjects(CommonShaderParameters commonShaderParameters, RenderingTechnique renderingTechnique)
        {
            var device = _deviceResolverComponent.Device;
            var previousViewport = device.Viewport;

            try
            {
                if (SquareViewport)
                {
                    var size = Math.Min(
                        previousViewport.Width,
                        previousViewport.Height);
                    device.Viewport = new Viewport(
                        previousViewport.X +
                            (previousViewport.Width - size) / 2,
                        previousViewport.Y +
                            (previousViewport.Height - size) / 2,
                        size,
                        size);
                }

                var shadingPipeline =
                    ViewportShadingPolicy.Resolve(ShadingMode);
                if (shadingPipeline.FillMode == FillMode.WireFrame)
                    device.RasterizerState = _rasterStates[RasterizerStateEnum.Wireframe];
                else
                    device.RasterizerState = _rasterStates[RasterizerStateEnum.Normal];

                foreach (var item in _renderItems[RenderBuckedId.Normal])
                    item.Draw(device, commonShaderParameters, renderingTechnique);

                // Draw depth-tested helpers after meshes so they cannot punch
                // holes into the selection mask before selected geometry renders.
                var isSurfacePass =
                    renderingTechnique != RenderingTechnique.Emissive;
                if (isSurfacePass)
                {
                    DrawTranslucentPreviewSurfaces(
                        device,
                        commonShaderParameters);
                    DrawPreviewEdges(
                        device,
                        commonShaderParameters);
                }
                if (isSurfacePass &&
                    _renderLines.Count != 0)
                {
                    var shader =
                        _resourceLibrary.GetStaticEffect(ShaderTypes.Line);
                    shader.Parameters["View"].SetValue(
                        commonShaderParameters.View);
                    shader.Parameters["Projection"].SetValue(
                        commonShaderParameters.Projection);
                    shader.Parameters["World"].SetValue(Matrix.Identity);

                    foreach (var pass in shader.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        if (_renderLinesArray == null ||
                            _renderLinesArray.Length < _renderLines.Count)
                        {
                            _renderLinesArray =
                                new VertexPositionColor[_renderLines.Count];
                        }
                        _renderLines.CopyTo(_renderLinesArray, 0);
                        device.DrawUserPrimitives(
                            PrimitiveType.LineList,
                            _renderLinesArray,
                            0,
                            _renderLines.Count / 2);
                    }
                }

                device.RasterizerState =
                    _rasterStates[RasterizerStateEnum.Wireframe];
                foreach (var item in _renderItems[RenderBuckedId.Wireframe])
                    item.Draw(
                        device,
                        commonShaderParameters,
                        isSurfacePass
                            ? RenderingTechnique.Normal
                            : renderingTechnique);

                if (isSurfacePass &&
                    _overlayLines.Count != 0)
                {
                    var shader =
                        _resourceLibrary.GetStaticEffect(ShaderTypes.Line);
                    shader.Parameters["View"].SetValue(
                        commonShaderParameters.View);
                    shader.Parameters["Projection"].SetValue(
                        commonShaderParameters.Projection);
                    shader.Parameters["World"].SetValue(Matrix.Identity);

                    device.RasterizerState =
                        _rasterStates[RasterizerStateEnum.Normal];
                    foreach (var pass in shader.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        if (_overlayLinesArray == null ||
                            _overlayLinesArray.Length < _overlayLines.Count)
                        {
                            _overlayLinesArray =
                                new VertexPositionColor[_overlayLines.Count];
                        }
                        _overlayLines.CopyTo(_overlayLinesArray, 0);
                        device.DrawUserPrimitives(
                            PrimitiveType.LineList,
                            _overlayLinesArray,
                            0,
                            _overlayLines.Count / 2);
                    }
                }

                device.RasterizerState =
                    _rasterStates[RasterizerStateEnum.SelectedFaces];
                foreach (var item in _renderItems[RenderBuckedId.Selection])
                    item.Draw(
                        device,
                        commonShaderParameters,
                        isSurfacePass
                            ? RenderingTechnique.Normal
                            : renderingTechnique);
            }
            finally
            {
                device.Viewport = previousViewport;
            }
        }

        private void DrawTranslucentPreviewSurfaces(
            GraphicsDevice device,
            CommonShaderParameters parameters)
        {
            if (_translucentPreviewTriangles.Count == 0)
                return;

            if (_previewTriangleArray == null ||
                _previewTriangleArray.Length <
                    _translucentPreviewTriangles.Count)
            {
                _previewTriangleArray = new VertexPositionColor[
                    _translucentPreviewTriangles.Count];
            }
            _translucentPreviewTriangles.CopyTo(
                _previewTriangleArray,
                0);

            var effect = _resourceLibrary.GetStaticEffect(
                ShaderTypes.Line);
            effect.Parameters["World"].SetValue(Matrix.Identity);
            effect.Parameters["View"].SetValue(parameters.View);
            effect.Parameters["Projection"].SetValue(
                parameters.Projection);

            var previousBlendState = device.BlendState;
            var previousDepthState = device.DepthStencilState;
            var previousRasterizerState = device.RasterizerState;
            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.DepthRead;
            device.RasterizerState = RasterizerState.CullNone;
            try
            {
                effect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _previewTriangleArray,
                    0,
                    _translucentPreviewTriangles.Count / 3);
            }
            finally
            {
                device.BlendState = previousBlendState;
                device.DepthStencilState = previousDepthState;
                device.RasterizerState = previousRasterizerState;
            }
        }

        private void DrawPreviewEdges(
            GraphicsDevice device,
            CommonShaderParameters parameters)
        {
            if (_previewEdges.Count == 0 ||
                _previewEdgeRenderer == null)
            {
                return;
            }

            if (_previewEdgeArray == null ||
                _previewEdgeArray.Length != _previewEdges.Count)
            {
                _previewEdgeArray = new EdgeData[_previewEdges.Count];
            }
            _previewEdges.CopyTo(_previewEdgeArray, 0);
            _previewEdgeRenderer.Update(_previewEdgeArray);
            _previewEdgeRenderer.Draw(
                parameters.View,
                parameters.Projection,
                device.Viewport.Height,
                device.Viewport.Width,
                device);
        }

        private void CaptureTransparentFrame(
            GraphicsDevice device,
            SpriteBatch spriteBatch,
            Texture2D? bloomRenderTarget,
            int width,
            int height)
        {
            var state = GraphicsDeviceStateSnapshot.Capture(device);
            try
            {
                _transparentCaptureTarget =
                    RenderTargetHelper.GetRenderTarget(
                        device,
                        _transparentCaptureTarget,
                        enableMsaa: false);
                device.SetRenderTarget(_transparentCaptureTarget);
                device.Clear(Color.Transparent);

                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend);
                spriteBatch.Draw(
                    _defaultRenderTarget,
                    new Rectangle(0, 0, width, height),
                    Color.White);
                spriteBatch.End();

                if (bloomRenderTarget != null)
                {
                    spriteBatch.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.Additive);
                    spriteBatch.Draw(
                        bloomRenderTarget,
                        new Rectangle(0, 0, width, height),
                        Color.White);
                    spriteBatch.End();
                }
            }
            finally
            {
                state.Restore(device);
            }
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
            }

            if (_mainRenderTargets == null ||
                !ReferenceEquals(
                    _mainRenderTargets[0].RenderTarget,
                    _defaultRenderTarget) ||
                !ReferenceEquals(
                    _mainRenderTargets[1].RenderTarget,
                    _selectionMaskTarget))
            {
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
            _captureBloomFilter.Dispose();
            _outlineFilter.Dispose();
            _defaultRenderTarget.Dispose();
            _glowRenderTarget.Dispose();
            _selectionMaskTarget?.Dispose();
            _selectionMaskTarget = null;
            _transparentCaptureTarget?.Dispose();
            _transparentCaptureTarget = null;
            _whiteTexture.Dispose();
            _previewEdgeRenderer?.Dispose();

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
        MaterialPreview,
        Solid,
        Wireframe
    }
}
