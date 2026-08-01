using System.Reflection;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class RenderEngineSelectionMaskOffscreenTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Render3DObjects_SelectedEdgeRemainsOrangeOverWireframe(
        bool animated)
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
        var eventHub = new Mock<IEventHub>();
        using var scopedResources =
            new ScopedResourceLibrary(
                resources,
                eventHub.Object,
                new Mock<IStandardDialogs>().Object);
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
            eventHub.Object,
            grid);
        renderEngine.Initialize();
        var selectionManager = new SelectionManager(
            eventHub.Object,
            renderEngine,
            scopedResources,
            deviceResolver.Object);
        selectionManager.Initialize();
        var mesh = CreateMesh(device, animated);
        var material = new Mock<IRmvMaterial>();
        material.SetupGet(value => value.ModelName).Returns("test");
        material.SetupGet(value => value.PivotPoint).Returns(Vector3.Zero);
        var animationPlayer = animated
            ? CreateAnimationPlayer()
            : new AnimationPlayer { IsEnabled = false };
        var node = new Rmv2MeshNode(
            mesh,
            material.Object,
            null!,
            animationPlayer);
        selectionManager.SetState(
            new EdgeSelectionState
            {
                RenderObject = node,
                SelectedEdges = [(0, 1)]
            });
        using var surface = new SolidMeshRenderItem(device);
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            surface);
        selectionManager.Draw(new GameTime());
        var selectionRenderItems = GetRenderItems(
            renderEngine,
            RenderBuckedId.Selection);
        Assert.That(
            selectionRenderItems,
            Has.Exactly(1)
                .TypeOf<AnimatedWireframeRenderItem>(),
            "Static and animated selected edges must use the same visible, depth-biased wireframe path.");
        using var renderTarget = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        renderTarget.GetData(pixels);
        var orangePixels = pixels.Count(IsOrange);
        var widestOrangeColumn = Enumerable.Range(0, size)
            .Max(
                x => Enumerable.Range(0, size)
                    .Count(
                        y => IsOrange(pixels[y * size + x])));
        var initialOrangeRow = GetAverageOrangeRow(
            pixels,
            size);
        Assert.Multiple(() =>
        {
            Assert.That(
                orangePixels,
                Is.GreaterThan(0),
                "A selected edge must remain orange when it overlaps the black edit wireframe.");
            Assert.That(
                widestOrangeColumn,
                Is.GreaterThanOrEqualTo(2),
                "A selected edge must be visibly thicker than the one-pixel edit wireframe.");
        });

        mesh.VertexArray[0].Position.Y += 0.6f;
        mesh.VertexArray[1].Position.Y += 0.6f;
        mesh.RebuildVertexBufferPartial(0, 1);
        renderEngine.Update(new GameTime());
        renderEngine.AddRenderItem(
            RenderBuckedId.Normal,
            surface);
        selectionManager.Draw(new GameTime());

        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Transparent,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        InvokeRender3DObjects(renderEngine);
        device.SetRenderTarget(null);
        renderTarget.GetData(pixels);
        var movedOrangeRow = GetAverageOrangeRow(
            pixels,
            size);

        Assert.That(
            Math.Abs(movedOrangeRow - initialOrangeRow),
            Is.GreaterThan(10),
            "The orange selected edge must follow edited vertices instead of remaining at the original position.");

        selectionManager.Dispose();
        mesh.Dispose();
    }

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

    private static IReadOnlyList<IRenderItem> GetRenderItems(
        RenderEngineComponent renderEngine,
        RenderBuckedId bucket)
    {
        var field = typeof(RenderEngineComponent).GetField(
            "_renderItems",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        var renderItems = field!.GetValue(renderEngine) as
            IReadOnlyDictionary<RenderBuckedId, List<IRenderItem>>;
        Assert.That(renderItems, Is.Not.Null);
        return renderItems![bucket];
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

    private sealed class SolidMeshRenderItem :
        IRenderItem,
        IDisposable
    {
        private readonly BasicEffect _effect;
        private readonly VertexPositionColor[] _vertices =
        [
            new(new Vector3(-0.8f, -0.2f, 0.5f), Color.Gray),
            new(new Vector3(-0.8f, 0.2f, 0.5f), Color.Gray),
            new(new Vector3(0.8f, -0.2f, 0.5f), Color.Gray),
            new(new Vector3(0.8f, -0.2f, 0.5f), Color.Gray),
            new(new Vector3(-0.8f, 0.2f, 0.5f), Color.Gray),
            new(new Vector3(0.8f, 0.2f, 0.5f), Color.Gray)
        ];

        public SolidMeshRenderItem(GraphicsDevice device)
        {
            _effect = new BasicEffect(device)
            {
                VertexColorEnabled = true,
                World = Matrix.Identity,
                View = Matrix.Identity,
                Projection = Matrix.Identity
            };
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
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices,
                    0,
                    2);
            }
        }

        public void Dispose()
        {
            _effect.Dispose();
        }
    }

    private static MeshObject CreateMesh(
        GraphicsDevice device,
        bool animated)
    {
        var mesh = new MeshObject(
            new GraphicsCardGeometry(device),
            "test")
        {
            VertexArray =
            [
                CreateVertex(-0.8f, -0.2f),
                CreateVertex(0.8f, -0.2f),
                CreateVertex(-0.8f, 0.2f),
                CreateVertex(0.8f, 0.2f)
            ],
            IndexArray = [0, 1, 2, 2, 1, 3]
        };
        mesh.ChangeVertexType(
            animated
                ? UiVertexFormat.Weighted
                : UiVertexFormat.Static,
            updateMesh: false);
        mesh.BuildBoundingBox();
        mesh.RebuildIndexBuffer();
        mesh.RebuildVertexBuffer();
        return mesh;
    }

    private static AnimationPlayer CreateAnimationPlayer()
    {
        var player = new AnimationPlayer();
        var skeletonFile = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                SkeletonName = "test_skeleton"
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Name = "root",
                    ParentId = -1
                }
            ]
        };
        var skeletonFrame = new AnimationFile.Frame();
        skeletonFrame.Transforms.Add(new RmvVector3(0, 0, 0));
        skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var skeletonPart = new AnimationFile.AnimationPart();
        skeletonPart.DynamicFrames.Add(skeletonFrame);
        skeletonFile.AnimationParts.Add(skeletonPart);
        var skeleton = new GameSkeleton(skeletonFile, player);
        var clip = new AnimationClip();
        clip.DynamicFrames.Add(
            new AnimationClip.KeyFrame
            {
                Position = [Vector3.Zero],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One]
            });
        clip.PlayTimeInSec = 1;
        player.SetAnimation(clip, skeleton);
        player.IsEnabled = true;
        player.Pause();
        player.Refresh();
        return player;
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
            BlendWeights = new Vector4(1, 0, 0, 0),
            BlendIndices = Vector4.Zero
        };
    }

    private static bool IsOrange(Color pixel)
    {
        return pixel.R > 200 &&
               pixel.G is > 70 and < 190 &&
               pixel.B < 40 &&
               pixel.A > 0;
    }

    private static double GetAverageOrangeRow(
        IReadOnlyList<Color> pixels,
        int size)
    {
        var rowSum = 0;
        var count = 0;
        for (var index = 0; index < pixels.Count; index++)
        {
            if (!IsOrange(pixels[index]))
                continue;

            rowSum += index / size;
            count++;
        }

        Assert.That(
            count,
            Is.GreaterThan(0),
            "The selected edge must produce orange pixels.");
        return (double)rowSum / count;
    }
}
