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
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class VertexOverlayUploadTests
{
    [Test]
    public void Draw_UnchangedOverlay_UploadsInstancesOnlyOnce()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\VertexPointShader");
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(x => x.Device).Returns(device);
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.VertexPoint))
            .Returns(effect);
        using var vertexRenderer = new VertexInstanceMesh(
            deviceResolver.Object,
            resources.Object);
        var mesh = CreateMesh(device);
        var node = new Rmv2MeshNode(
            mesh,
            Mock.Of<IRmvMaterial>(),
            null!,
            null!);
        var selection = new VertexSelectionState(node, 0);
        var renderItem = new VertexRenderItem
        {
            VertexRenderer = vertexRenderer,
            Node = node,
            WorldPositions =
            [
                new Vector3(-0.5f, 0, 0),
                new Vector3(0.5f, 0, 0)
            ],
            SelectedVertices = selection
        };
        var parameters = new CommonShaderParameters(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Backward,
            Vector3.Forward,
            0,
            0,
            0,
            1,
            Vector3.One,
            [],
            64,
            64);
        using var renderTarget = new RenderTarget2D(
            device,
            64,
            64,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(
                ClearOptions.Target |
                    ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            device.DepthStencilState =
                DepthStencilState.Default;
            device.RasterizerState =
                RasterizerState.CullNone;

            renderItem.Draw(
                device,
                parameters,
                RenderingTechnique.Normal);
            renderItem.Draw(
                device,
                parameters,
                RenderingTechnique.Normal);
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var pixels = new Color[64 * 64];
        renderTarget.GetData(pixels);
        Assert.Multiple(() =>
        {
            Assert.That(
                vertexRenderer.InstanceUploadCount,
                Is.EqualTo(1));
            Assert.That(
                pixels.Count(pixel => pixel.A != 0),
                Is.GreaterThan(0));
        });

        mesh.Dispose();
    }

    [Test]
    public void Update_LargeOverlayDoesNotDiscardVerticesAfterFixedCapacity()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\VertexPointShader");
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(x => x.Device).Returns(device);
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.VertexPoint))
            .Returns(effect);
        using var vertexRenderer = new VertexInstanceMesh(
            deviceResolver.Object,
            resources.Object);
        var mesh = CreateMesh(device);
        var node = new Rmv2MeshNode(
            mesh,
            Mock.Of<IRmvMaterial>(),
            null!,
            null!);
        var selection = new VertexSelectionState(node, 0);
        const int vertexCount = 50_001;
        selection.VertexWeights =
            Enumerable.Repeat(0.0f, vertexCount).ToList();
        var worldPositions = new Vector3[vertexCount];

        vertexRenderer.Update(worldPositions, selection);

        var instanceCount =
            typeof(VertexInstanceMesh).GetField(
                "_currentInstanceCount",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
                .GetValue(vertexRenderer);
        Assert.That(instanceCount, Is.EqualTo(vertexCount));

        mesh.Dispose();
    }

    [Test]
    public void Draw_AnimatedPoseUsesGpuSkinningWithoutPerFrameInstanceUpload()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\VertexPointShader");
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(x => x.Device).Returns(device);
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.VertexPoint))
            .Returns(effect);
        using var vertexRenderer = new VertexInstanceMesh(
            deviceResolver.Object,
            resources.Object);
        var mesh = CreateAnimatedMesh(device);
        var node = new Rmv2MeshNode(
            mesh,
            Mock.Of<IRmvMaterial>(),
            null!,
            null!);
        var selection = new VertexSelectionState(node, 0);
        var pose = GameWorld.Core.Animation.MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateTranslation(1.2f, 0, 0)],
            true);
        var renderItem = new VertexRenderItem
        {
            VertexRenderer = vertexRenderer,
            Node = node,
            SelectedVertices = selection
        };
        var poseProperty =
            typeof(VertexRenderItem).GetProperty("Pose");
        Assert.That(
            poseProperty,
            Is.Not.Null,
            "Animated vertex mode needs a GPU pose input instead of CPU world-position uploads.");
        poseProperty!.SetValue(renderItem, pose);
        using var renderTarget = new RenderTarget2D(
            device,
            64,
            64,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        var parameters = new CommonShaderParameters(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Backward,
            Vector3.Forward,
            0,
            0,
            0,
            1,
            Vector3.One,
            [],
            64,
            64);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(
                ClearOptions.Target |
                    ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            device.DepthStencilState =
                DepthStencilState.Default;
            device.RasterizerState =
                RasterizerState.CullNone;
            renderItem.Draw(
                device,
                parameters,
                RenderingTechnique.Normal);
            renderItem.Draw(
                device,
                parameters,
                RenderingTechnique.Normal);
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var pixels = new Color[64 * 64];
        renderTarget.GetData(pixels);
        Assert.Multiple(() =>
        {
            Assert.That(
                CountVisiblePixels(
                    pixels,
                    64,
                    0,
                    32),
                Is.EqualTo(0));
            Assert.That(
                CountVisiblePixels(
                    pixels,
                    64,
                    32,
                    64),
                Is.GreaterThan(0));
            Assert.That(
                vertexRenderer.InstanceUploadCount,
                Is.EqualTo(1));
        });

        mesh.Dispose();
    }

    private static MeshObject CreateMesh(
        GraphicsDevice device)
    {
        var mesh = new MeshObject(
            new GraphicsCardGeometry(device),
            "test")
        {
            VertexArray =
            [
                new VertexPositionNormalTextureCustom
                {
                    Position = new Vector4(-0.5f, 0, 0, 1)
                },
                new VertexPositionNormalTextureCustom
                {
                    Position = new Vector4(0.5f, 0, 0, 1)
                }
            ],
            IndexArray = []
        };
        mesh.BuildBoundingBox();
        return mesh;
    }

    private static MeshObject CreateAnimatedMesh(
        GraphicsDevice device)
    {
        var mesh = new MeshObject(
            new GraphicsCardGeometry(device),
            "test")
        {
            VertexArray =
            [
                CreateAnimatedVertex(-0.8f),
                CreateAnimatedVertex(-0.6f)
            ],
            IndexArray = []
        };
        mesh.ChangeVertexType(
            UiVertexFormat.Weighted,
            updateMesh: false);
        mesh.BuildBoundingBox();
        mesh.RebuildVertexBuffer();
        return mesh;
    }

    private static VertexPositionNormalTextureCustom
        CreateAnimatedVertex(float x)
    {
        return new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(x, 0, 0.5f, 1),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            BiNormal = Vector3.UnitY,
            BlendWeights = new Vector4(1, 0, 0, 0),
            BlendIndices = Vector4.Zero
        };
    }

    private static int CountVisiblePixels(
        IReadOnlyList<Color> pixels,
        int width,
        int startX,
        int endX)
    {
        var count = 0;
        for (var y = 0; y < pixels.Count / width; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                if (pixels[y * width + x].A != 0)
                    count++;
            }
        }

        return count;
    }
}
