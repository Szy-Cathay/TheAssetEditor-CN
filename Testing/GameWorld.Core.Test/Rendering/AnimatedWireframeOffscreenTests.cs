using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class AnimatedWireframeOffscreenTests
{
    [Test]
    public void Draw_ReusesUniqueEdgeBufferAndAppliesAnimation()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\AnimatedSelection");
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.AnimatedSelection))
            .Returns(effect);
        var mesh = CreateMesh(device);
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateTranslation(1.25f, 0, 0)],
            true);
        using var renderItem =
            new AnimatedWireframeRenderItem(
                pose,
                resources.Object,
                Vector4.One,
                50_000);
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
            Vector3.Zero,
            Vector3.Forward,
            0,
            0,
            0,
            1,
            []);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(
                ClearOptions.Target |
                    ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            device.BlendState = BlendState.Opaque;
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
        var leftPixels = CountVisiblePixels(
            pixels,
            64,
            0,
            32);
        var rightPixels = CountVisiblePixels(
            pixels,
            64,
            32,
            64);

        Assert.Multiple(() =>
        {
            Assert.That(
                renderItem.EdgePrimitiveCount,
                Is.EqualTo(5));
            Assert.That(
                renderItem.IndexBufferBuildCount,
                Is.EqualTo(1));
            Assert.That(leftPixels, Is.EqualTo(0));
            Assert.That(rightPixels, Is.GreaterThan(0));
        });

        mesh.Dispose();
    }

    [Test]
    public void DefaultOverlay_KeepsAllUniqueEdgesAndSupportsSelectedSubset()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\AnimatedSelection");
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.AnimatedSelection))
            .Returns(effect);
        var mesh = CreateMesh(device);
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [],
            false);
        var constructor =
            typeof(AnimatedWireframeRenderItem).GetConstructor(
                [
                    typeof(MeshPoseSnapshot),
                    typeof(IScopedResourceLibrary),
                    typeof(Vector4)
                ]);

        Assert.That(
            constructor,
            Is.Not.Null,
            "The edit overlay must default to the complete mesh instead of a fixed edge cap.");

        using var renderItem =
            (AnimatedWireframeRenderItem)constructor!.Invoke(
                [pose, resources.Object, Vector4.One]);
        var updateEdges =
            typeof(AnimatedWireframeRenderItem).GetMethod(
                "UpdateEdges");

        Assert.Multiple(() =>
        {
            Assert.That(
                renderItem.EdgePrimitiveCount,
                Is.EqualTo(5));
            Assert.That(
                updateEdges,
                Is.Not.Null,
                "Animated edge mode needs a GPU-skinned selected-edge subset.");
        });

        updateEdges!.Invoke(
            renderItem,
            [new HashSet<(int, int)> { (0, 1) }]);
        renderItem.UpdatePose(
            MeshPoseSnapshot.Create(
                mesh,
                Matrix.Identity,
                [Matrix.CreateTranslation(0.1f, 0, 0)],
                true));
        Assert.That(
            renderItem.EdgePrimitiveCount,
            Is.EqualTo(1));

        mesh.Dispose();
    }

    [Test]
    public void Draw_CoplanarOverlayRemainsVisibleWithStrictDepthTest()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\AnimatedSelection");
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.AnimatedSelection))
            .Returns(effect);
        var mesh = CreateMesh(device);
        for (var i = 0; i < mesh.VertexArray.Length; i++)
            mesh.VertexArray[i].Position.Z = 0.5f;
        mesh.RebuildVertexBuffer();
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [],
            false);
        using var renderItem =
            new AnimatedWireframeRenderItem(
                pose,
                resources.Object,
                Vector4.One,
                50_000);
        using var renderTarget = new RenderTarget2D(
            device,
            64,
            64,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        using var baseEffect = new BasicEffect(device)
        {
            DiffuseColor = Vector3.UnitX,
            LightingEnabled = false,
            TextureEnabled = false,
            VertexColorEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity,
            Projection = Matrix.Identity
        };
        using var strictDepth = new DepthStencilState
        {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = false,
            DepthBufferFunction = CompareFunction.Less
        };
        var parameters = new CommonShaderParameters(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Zero,
            Vector3.Forward,
            0,
            0,
            0,
            1,
            []);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(
                ClearOptions.Target |
                    ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState =
                DepthStencilState.Default;
            device.RasterizerState =
                RasterizerState.CullNone;
            device.Indices =
                mesh.GetGeometryContext().IndexBuffer;
            device.SetVertexBuffer(
                mesh.GetGeometryContext().VertexBuffer);
            foreach (var pass in baseEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    0,
                    0,
                    mesh.GetIndexCount() / 3);
            }

            device.DepthStencilState = strictDepth;
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
        Assert.That(
            pixels.Count(
                pixel =>
                    pixel.R > 200 &&
                    pixel.G > 200 &&
                    pixel.B > 200),
            Is.GreaterThan(0));

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
                CreateVertex(-0.9f, -0.3f),
                CreateVertex(-0.5f, -0.3f),
                CreateVertex(-0.9f, 0.3f),
                CreateVertex(-0.5f, 0.3f)
            ],
            IndexArray = [0, 1, 2, 2, 1, 3]
        };
        mesh.ChangeVertexType(
            UiVertexFormat.Weighted,
            updateMesh: false);
        mesh.BuildBoundingBox();
        mesh.RebuildIndexBuffer();
        mesh.RebuildVertexBuffer();
        return mesh;
    }

    private static VertexPositionNormalTextureCustom CreateVertex(
        float x,
        float y)
    {
        return new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(x, y, 0, 1),
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
