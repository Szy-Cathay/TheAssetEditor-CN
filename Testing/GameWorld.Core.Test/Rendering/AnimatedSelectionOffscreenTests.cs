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
public class AnimatedSelectionOffscreenTests
{
    [TestCase(true, "AnimatedSelection")]
    [TestCase(false, "StaticSelection")]
    public void Draw_SelectionMaskUsesPoseAppropriateShaderPath(
        bool applyAnimation,
        string expectedTechnique)
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
        var geometryContext =
            new GraphicsCardGeometry(device);
        var mesh = CreateMesh(geometryContext);
        mesh.RebuildIndexBuffer();
        mesh.RebuildVertexBuffer();
        var pose = MeshPoseSnapshot.Create(
            mesh,
            Matrix.Identity,
            [Matrix.CreateTranslation(1.4f, 0, 0)],
            applyAnimation);
        var renderItem = new AnimatedSelectionRenderItem(
            pose,
            resources.Object,
            Vector4.One);
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
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState =
                DepthStencilState.Default;
            device.RasterizerState =
                RasterizerState.CullNone;
            renderItem.Draw(
                device,
                new CommonShaderParameters(
                    Matrix.Identity,
                    Matrix.Identity,
                    Vector3.Zero,
                    Vector3.Forward,
                    0,
                    0,
                    0,
                    1,
                    Vector3.One,
                    []),
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
                effect.CurrentTechnique.Name,
                Is.EqualTo(expectedTechnique));
            Assert.That(
                effect.Parameters[
                        "CapabilityFlag_ApplyAnimation"]
                    .GetValueBoolean(),
                Is.EqualTo(applyAnimation));
            Assert.That(
                effect.Parameters["Animation_WeightCount"]
                    .GetValueInt32(),
                Is.EqualTo(applyAnimation ? 2 : 0));
            Assert.That(
                leftPixels,
                applyAnimation
                    ? Is.EqualTo(0)
                    : Is.GreaterThan(0));
            Assert.That(
                rightPixels,
                applyAnimation
                    ? Is.GreaterThan(0)
                    : Is.EqualTo(0));
        });
        mesh.Dispose();
    }

    private static MeshObject CreateMesh(
        IGraphicsCardGeometry geometryContext)
    {
        var mesh = new MeshObject(
            geometryContext,
            "test_skeleton")
        {
            VertexArray =
            [
                CreateVertex(-0.9f, -0.2f),
                CreateVertex(-0.5f, -0.2f),
                CreateVertex(-0.7f, 0.2f)
            ],
            IndexArray = [0, 1, 2]
        };
        mesh.ChangeVertexType(
            UiVertexFormat.Weighted,
            updateMesh: false);
        mesh.BuildBoundingBox();
        return mesh;
    }

    private static VertexPositionNormalTextureCustom
        CreateVertex(float x, float y)
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
