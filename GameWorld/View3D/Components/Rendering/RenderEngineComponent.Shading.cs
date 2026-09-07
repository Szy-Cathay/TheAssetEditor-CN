using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Components.Rendering;

public partial class RenderEngineComponent
{
    private ViewportLightingResources? _viewportLightingResources;
    private RenderTarget2D? _surfaceGeometryTarget;

    private CommonShaderParameters PrepareViewportShading(GraphicsDevice device, CommonShaderParameters parameters)
    {
        var settings = ShadingSettings;
        parameters = parameters with { ViewportShading = settings };
        if (ShadingMode == ViewportShadingMode.MaterialPreview && settings.UseLocalLighting)
        {
            parameters = parameters with
            {
                LightIntensityMult = settings.LightIntensity,
                EnvLightRotationsRadians_Y = MathHelper.ToRadians(settings.EnvironmentRotation)
            };
            if (settings.Environment != ViewportEnvironment.Game)
            {
                _viewportLightingResources ??= new ViewportLightingResources(device);
                var maps = _viewportLightingResources.GetEnvironment(settings.Environment);
                parameters = parameters with { ViewportDiffuse = maps.Diffuse, ViewportSpecular = maps.Specular };
            }
        }
        if (ShadingMode != ViewportShadingMode.Solid)
            return parameters;
        if (settings.SolidLighting != ViewportSolidLighting.Studio)
        {
            _viewportLightingResources ??= new ViewportLightingResources(device);
            parameters = parameters with { ViewportMatcap = _viewportLightingResources.GetMatcap(settings.SolidLighting) };
        }
        if (settings.CavityStrength <= 0 && settings.ShadowStrength <= 0)
            return parameters;

        var width = device.Viewport.Width;
        var height = device.Viewport.Height;
        if (_surfaceGeometryTarget == null || _surfaceGeometryTarget.IsDisposed ||
            _surfaceGeometryTarget.Width != width || _surfaceGeometryTarget.Height != height)
        {
            _surfaceGeometryTarget?.Dispose();
            _surfaceGeometryTarget = new RenderTarget2D(device, width, height, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
        }
        var targets = device.GetRenderTargets();
        var viewport = device.Viewport;
        var depth = device.DepthStencilState;
        var blend = device.BlendState;
        var raster = device.RasterizerState;
        try
        {
            // The previous frame may still have this texture bound for sampling.
            for (var i = 0; i < 16; i++)
                device.Textures[i] = null;
            device.SetRenderTarget(_surfaceGeometryTarget);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            if (SquareViewport)
            {
                var size = Math.Min(width, height);
                device.Viewport = new Viewport((width - size) / 2, (height - size) / 2, size, size);
            }
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = _rasterStates[RasterizerStateEnum.Normal];
            foreach (var item in _renderItems[RenderBuckedId.Normal])
                if (item.IsMeshSurface)
                    item.Draw(device, parameters, RenderingTechnique.ViewportGeometry);
        }
        finally
        {
            device.SetRenderTargets(targets);
            device.Viewport = viewport;
            device.DepthStencilState = depth;
            device.BlendState = blend;
            device.RasterizerState = raster;
        }
        return parameters with { ViewportGeometry = _surfaceGeometryTarget };
    }

    private readonly BlendState _surfaceDepthBlend = new()
    {
        IndependentBlendEnable = true,
        ColorWriteChannels = ColorWriteChannels.None,
        ColorWriteChannels1 = ColorWriteChannels.All
    };
    private readonly BlendState _surfaceColourBlend = new()
    {
        IndependentBlendEnable = true,
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
        ColorWriteChannels1 = ColorWriteChannels.None
    };
    private readonly DepthStencilState _occludedSurfaceDepth = new()
    {
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.Greater
    };

    private void DrawViewportSurfaces(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique technique)
    {
        var wireframe = ShadingMode == ViewportShadingMode.Wireframe;
        if (technique == RenderingTechnique.Emissive || !wireframe && !ShadingSettings.XRay)
        {
            DrawMeshes(parameters with { ViewportShading = ShadingSettings }, technique);
            return;
        }

        var depth = device.DepthStencilState;
        var blend = device.BlendState;
        var raster = device.RasterizerState;
        try
        {
            // Fill depth and the silhouette mask, but leave the scene colour untouched.
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = _surfaceDepthBlend;
            device.RasterizerState = _rasterStates[RasterizerStateEnum.Normal];
            DrawMeshes(parameters with { SurfaceOpacity = 1 }, RenderingTechnique.Solid);

            device.RasterizerState = raster;
            device.BlendState = _surfaceColourBlend;
            var surfaceParameters = parameters with { ViewportShading = ShadingSettings, ViewportWireframe = wireframe };
            if (wireframe && ShadingSettings.XRay)
            {
                device.DepthStencilState = _occludedSurfaceDepth;
                DrawMeshes(surfaceParameters with { SurfaceOpacity = 0.16f * ShadingSettings.WireframeOpacity }, RenderingTechnique.Solid);
            }
            device.DepthStencilState = DepthStencilState.DepthRead;
            DrawMeshes(surfaceParameters with
            {
                SurfaceOpacity = wireframe ? ShadingSettings.WireframeOpacity : ShadingSettings.XRayOpacity
            }, wireframe ? RenderingTechnique.Solid : technique);
        }
        finally
        {
            device.DepthStencilState = depth;
            device.BlendState = blend;
            device.RasterizerState = raster;
        }

        void DrawMeshes(CommonShaderParameters current, RenderingTechnique currentTechnique)
        {
            foreach (var item in _renderItems[RenderBuckedId.Normal])
                if (item.IsMeshSurface)
                    item.Draw(device, current, currentTechnique);
        }
    }
}
