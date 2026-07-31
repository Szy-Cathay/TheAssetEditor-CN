using System.Collections.Concurrent;
using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Components.Rendering
{
    public record SaveRenderImageSettings(
        string Name,
        bool OpenFolder,
        float ImageUpScaleFactor,
        string OutputFolder)
    {
        public Action<Exception>? FailureHandler { get; init; }
    }

    internal sealed class PendingRenderCapture
    {
        private readonly ConcurrentQueue<SaveRenderImageSettings> _pending =
            new();

        public void Request(SaveRenderImageSettings settings)
        {
            _pending.Enqueue(settings);
        }

        public SaveRenderImageSettings? Consume()
        {
            return _pending.TryDequeue(out var settings)
                ? settings
                : null;
        }
    }

    internal static class RenderCaptureMath
    {
        private const int MaximumDimension = 8192;

        public static bool TryGetCaptureSize(
            int width,
            int height,
            float scale,
            out int outputWidth,
            out int outputHeight)
        {
            outputWidth = 0;
            outputHeight = 0;
            if (width <= 0 ||
                height <= 0 ||
                (scale != 1.0f && scale != 2.0f))
            {
                return false;
            }

            var scaledWidth = (long)(width * scale);
            var scaledHeight = (long)(height * scale);
            if (scaledWidth > MaximumDimension ||
                scaledHeight > MaximumDimension)
            {
                return false;
            }

            outputWidth = (int)scaledWidth;
            outputHeight = (int)scaledHeight;
            return true;
        }
    }

    internal sealed class GraphicsDeviceStateSnapshot
    {
        private readonly RenderTargetBinding[] _renderTargets;
        private readonly Viewport _viewport;
        private readonly Rectangle _scissorRectangle;
        private readonly BlendState _blendState;
        private readonly Color _blendFactor;
        private readonly DepthStencilState _depthStencilState;
        private readonly RasterizerState _rasterizerState;
        private readonly SamplerState _samplerState;
        private readonly Texture? _texture;
        private readonly IndexBuffer? _indices;

        private GraphicsDeviceStateSnapshot(
            GraphicsDevice device)
        {
            _renderTargets = device.GetRenderTargets();
            _viewport = device.Viewport;
            _scissorRectangle = device.ScissorRectangle;
            _blendState = device.BlendState;
            _blendFactor = device.BlendFactor;
            _depthStencilState = device.DepthStencilState;
            _rasterizerState = device.RasterizerState;
            _samplerState = device.SamplerStates[0];
            _texture = device.Textures[0];
            _indices = device.Indices;
        }

        public static GraphicsDeviceStateSnapshot Capture(
            GraphicsDevice device)
        {
            return new GraphicsDeviceStateSnapshot(device);
        }

        public void Restore(GraphicsDevice device)
        {
            if (_renderTargets.Length == 0)
                device.SetRenderTarget(null);
            else
                device.SetRenderTargets(_renderTargets);

            device.Viewport = _viewport;
            device.ScissorRectangle = _scissorRectangle;
            device.BlendState = _blendState;
            device.BlendFactor = _blendFactor;
            device.DepthStencilState = _depthStencilState;
            device.RasterizerState = _rasterizerState;
            device.SamplerStates[0] = _samplerState;
            device.Textures[0] = _texture;
            device.Indices = _indices;
        }
    }

    internal static class PhotoCaptureSurface
    {
        public static RenderTarget2D Render(
            GraphicsDevice device,
            int width,
            int height,
            Action<GraphicsDevice> drawScene)
        {
            var target = new RenderTarget2D(
                device,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.Depth24,
                0,
                RenderTargetUsage.PreserveContents);
            try
            {
                device.SetRenderTarget(target);
                device.Clear(
                    ClearOptions.Target |
                        ClearOptions.DepthBuffer,
                    Color.Transparent,
                    1,
                    0);
                drawScene(device);
                return target;
            }
            catch
            {
                target.Dispose();
                throw;
            }
            finally
            {
                device.SetRenderTarget(null);
            }
        }
    }

    internal static class PhotoCaptureSceneRenderer
    {
        public static void Draw(
            GraphicsDevice device,
            CommonShaderParameters parameters,
            IEnumerable<IRenderItem> items,
            RenderingTechnique technique,
            RasterizerState rasterizerState)
        {
            device.RasterizerState = rasterizerState;
            foreach (var item in items)
            {
                if (item.IncludeInPhotoCapture)
                    item.Draw(device, parameters, technique);
            }
        }
    }
}
