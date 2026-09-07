using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders;

[NonParallelizable]
public partial class ViewportSolidShadingPixelTests
{
    private const int RenderTargetSize = 32;

    private static readonly string[] s_effectAssets =
    [
        "Shaders\\Pbr\\MetalRoughness\\MetalRoughness_main",
        "Shaders\\Pbr\\SpecGloss\\SpecGloss_main"
    ];

    [TestCaseSource(nameof(s_effectAssets))]
    public void SolidDrawing_IgnoresMaterialTextures(
        string effectAsset)
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        using var effect = game.Content
            .Load<Effect>(effectAsset)
            .Clone();
        using var redTexture = CreateTexture(
            device,
            Color.Red);
        using var blueTexture = CreateTexture(
            device,
            Color.Blue);

        var redResult = Render(
            device,
            effect,
            redTexture);
        var blueResult = Render(
            device,
            effect,
            blueTexture);

        Assert.Multiple(() =>
        {
            Assert.That(
                Vector3.Distance(redResult, blueResult),
                Is.LessThan(1.0f / 255.0f));
            Assert.That(
                redResult.Length(),
                Is.GreaterThan(0.25f),
                "Solid mode must remain visibly lit.");
        });
    }

    private static Vector3 Render(
        GraphicsDevice device,
        Effect effect,
        Texture2D diffuseTexture,
        Action<Effect>? configure = null,
        string technique = "SolidDrawing")
    {
        using var renderTarget = new RenderTarget2D(
            device,
            RenderTargetSize,
            RenderTargetSize,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        ConfigureEffect(effect, diffuseTexture);
        configure?.Invoke(effect);

        device.SetRenderTarget(renderTarget);
        device.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.Black,
            1,
            0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique =
            effect.Techniques[technique];
        effect.CurrentTechnique.Passes[0].Apply();
        device.DrawUserPrimitives(
            PrimitiveType.TriangleStrip,
            CreateQuadVertices(),
            0,
            2);
        device.SetRenderTarget(null);

        var pixels = new Color[
            RenderTargetSize * RenderTargetSize];
        renderTarget.GetData(pixels);
        return pixels[
            RenderTargetSize / 2 * RenderTargetSize +
            RenderTargetSize / 2].ToVector3();
    }

    private static void ConfigureEffect(
        Effect effect,
        Texture2D diffuseTexture)
    {
        var cameraPosition = new Vector3(0, 0, 3);
        new CommonShaderParametersCapability
        {
            View = Matrix.CreateLookAt(
                cameraPosition,
                Vector3.Zero,
                Vector3.Up),
            Projection = Matrix.CreateOrthographic(
                2.5f,
                2.5f,
                0.1f,
                10),
            CameraPosition = cameraPosition,
            CameraLookAt = Vector3.Zero,
            ModelMatrix = Matrix.Identity,
            LightIntensityMult = 1,
            LightColour = Vector3.One
        }.Apply(effect, null!);

        effect.Parameters["DiffuseTexture"]?
            .SetValue(diffuseTexture);
        effect.Parameters["UseDiffuse"]?.SetValue(true);
        effect.Parameters["UseSpecular"]?.SetValue(false);
        effect.Parameters["UseNormal"]?.SetValue(false);
        effect.Parameters["UseGloss"]?.SetValue(false);
        effect.Parameters["UseAlpha"]?.SetValue(false);
        effect.Parameters["UseMask"]?.SetValue(false);
        effect.Parameters["CapabilityFlag_ApplyAnimation"]?
            .SetValue(false);
        effect.Parameters["CapabilityFlag_ApplyTinting"]?
            .SetValue(false);
        effect.Parameters["CapabilityFlag_ApplyEmissive"]?
            .SetValue(false);
        effect.Parameters["SelectionMaskEnabled"]?
            .SetValue(false);
    }

    private static Texture2D CreateTexture(
        GraphicsDevice device,
        Color colour)
    {
        var texture = new Texture2D(device, 1, 1);
        texture.SetData([colour]);
        return texture;
    }

    private static VertexPositionNormalTextureCustom[]
        CreateQuadVertices()
    {
        return
        [
            CreateVertex(-1, -1),
            CreateVertex(-1, 1),
            CreateVertex(1, -1),
            CreateVertex(1, 1)
        ];
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
            TextureCoordinate = Vector2.Zero,
            TextureCoordinate1 = Vector2.Zero,
            BlendWeights = Vector4.Zero,
            BlendIndices = Vector4.Zero
        };
    }
}
