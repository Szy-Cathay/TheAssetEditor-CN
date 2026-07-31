using System.Reflection;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class RenderEngineSelectionMaskOffscreenTests
{
    [Test]
    public void Render3DObjects_ForegroundLineDoesNotCutSelectionMask()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            new Mock<IKeyboardComponent>().Object,
            new Mock<IMouseComponent>().Object);
        camera.Initialize();
        var resources = new ResourceLibrary(
            new Mock<IPackFileService>().Object);
        resources.Initialize(device, game.Content);
        var grid = new GridComponent(
            camera,
            resources,
            deviceResolver.Object)
        {
            ShowGrid = false
        };
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            new Mock<IEventHub>().Object,
            grid);
        renderEngine.Initialize();
        renderEngine.AddRenderLines(
        [
            new VertexPositionColor(
                new Vector3(-0.8f, 0, 0.25f),
                Color.Black),
            new VertexPositionColor(
                new Vector3(0.8f, 0, 0.25f),
                Color.Black)
        ]);
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            new SelectionMaskRenderItem(
                game.Content.Load<Effect>(
                    "Shaders\\Pbr\\SpecGloss\\SpecGloss_main"),
                device));
        using var sceneTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        using var maskTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);

        device.SetRenderTargets(
            new RenderTargetBinding(sceneTarget),
            new RenderTargetBinding(maskTarget));
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        var maskPixels = new Color[size * size];
        maskTarget.GetData(maskPixels);
        var centralAlpha = byte.MaxValue;
        for (var y = 29; y <= 34; y++)
        {
            for (var x = 16; x <= 47; x++)
            {
                centralAlpha = Math.Min(
                    centralAlpha,
                    maskPixels[y * size + x].A);
            }
        }

        Assert.That(centralAlpha, Is.EqualTo(255));
    }

    private static void InvokeRender3DObjects(
        RenderEngineComponent renderEngine)
    {
        var method = typeof(RenderEngineComponent).GetMethod(
            "Render3DObjects",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method!.Invoke(
            renderEngine,
        [
            new CommonShaderParameters(
                Matrix.Identity,
                Matrix.Identity,
                Vector3.Backward,
                Vector3.Forward,
                0,
                0,
                0,
                1,
                Vector3.One,
                []),
            RenderingTechnique.Normal
        ]);
    }

    private sealed class SelectionMaskRenderItem : IRenderItem
    {
        private readonly Effect _effect;
        private readonly Texture2D _diffuse;

        public SelectionMaskRenderItem(
            Effect effect,
            GraphicsDevice device)
        {
            _effect = effect;
            _diffuse = new Texture2D(device, 1, 1);
            _diffuse.SetData([Color.White]);
        }

        public bool SupportsTechnique(
            RenderingTechnique technique)
        {
            return technique == RenderingTechnique.Normal;
        }

        public void Draw(
            GraphicsDevice device,
            CommonShaderParameters parameters,
            RenderingTechnique renderingTechnique)
        {
            _effect.CurrentTechnique =
                _effect.Techniques["BasicColorDrawing"];
            _effect.Parameters["World"].SetValue(Matrix.Identity);
            _effect.Parameters["View"].SetValue(Matrix.Identity);
            _effect.Parameters["Projection"].SetValue(
                Matrix.Identity);
            _effect.Parameters["CameraPos"].SetValue(
                Vector3.Backward);
            _effect.Parameters["DirLightTransform"].SetValue(
                Matrix.Identity);
            _effect.Parameters["CapabilityFlag_ApplyAnimation"]
                .SetValue(false);
            _effect.Parameters["UseDiffuse"].SetValue(true);
            _effect.Parameters["DiffuseTexture"].SetValue(
                _diffuse);
            _effect.Parameters["UseSpecular"].SetValue(false);
            _effect.Parameters["UseGloss"].SetValue(false);
            _effect.Parameters["UseNormal"].SetValue(false);
            _effect.Parameters["UseAlpha"].SetValue(false);
            _effect.Parameters["UseMask"].SetValue(false);
            _effect.Parameters["CapabilityFlag_ApplyTinting"]
                .SetValue(false);
            _effect.Parameters["SelectionMaskEnabled"].SetValue(
                true);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    CreateFullScreenQuad(),
                    0,
                    2);
            }
        }

        private static VertexPositionNormalTextureCustom[]
            CreateFullScreenQuad()
        {
            return
            [
                CreateVertex(-1, -1),
                CreateVertex(-1, 1),
                CreateVertex(1, -1),
                CreateVertex(1, -1),
                CreateVertex(-1, 1),
                CreateVertex(1, 1)
            ];
        }

        private static VertexPositionNormalTextureCustom CreateVertex(
            float x,
            float y)
        {
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(x, y, 0.5f, 1),
                Normal = Vector3.UnitZ,
                Tangent = Vector3.UnitX,
                BiNormal = Vector3.UnitY,
                BlendWeights = Vector4.Zero,
                BlendIndices = Vector4.Zero
            };
        }
    }
}
