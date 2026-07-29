using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class SelectionMaskOffscreenTests
{
    [TestCase(
        "Shaders\\Pbr\\MetalRoughness\\MetalRoughness_main")]
    [TestCase(
        "Shaders\\Pbr\\SpecGloss\\SpecGloss_main")]
    public void MainMaterial_ExposesSelectionMaskParameter(
        string effectAsset)
    {
        var game = new WpfGameMock();
        var effect = game.Content.Load<Effect>(effectAsset);

        Assert.That(
            effect.Parameters["SelectionMaskEnabled"],
            Is.Not.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CapabilityMaterial_PropagatesSelectionMaskToEffect(
        bool selectionMaskEnabled)
    {
        var game = new WpfGameMock();
        var effect = game.Content.Load<Effect>(
            "Shaders\\Pbr\\SpecGloss\\SpecGloss_main");
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library =>
                library.GetStaticEffect(
                    ShaderTypes.Pbr_SpecGloss))
            .Returns(effect);
        var material = new SelectionMaskTestMaterial(
            resources.Object);
        material.SetTechnique(RenderingTechnique.Normal);
        material.SetSelectionMask(selectionMaskEnabled);

        material.Apply(
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
            Matrix.Identity);

        Assert.That(
            effect.Parameters["SelectionMaskEnabled"]
                .GetValueBoolean(),
            Is.EqualTo(selectionMaskEnabled));
    }

    [TestCase(true, 255)]
    [TestCase(false, 0)]
    public void SpecGlossMainPass_WritesSelectionMaskAlongsideScene(
        bool selectionMaskEnabled,
        byte expectedMaskAlpha)
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\Pbr\\SpecGloss\\SpecGloss_main");
        using var diffuse = new Texture2D(device, 1, 1);
        diffuse.SetData([Color.White]);
        ConfigureEffect(
            effect,
            diffuse,
            selectionMaskEnabled);
        using var sceneTarget = new RenderTarget2D(
            device,
            32,
            32,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24,
            4,
            RenderTargetUsage.DiscardContents);
        using var maskTarget = new RenderTarget2D(
            device,
            32,
            32,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            4,
            RenderTargetUsage.DiscardContents);
        using var spriteBatch = new SpriteBatch(device);

        try
        {
            device.SetRenderTargets(
                new RenderTargetBinding(sceneTarget),
                new RenderTargetBinding(maskTarget));
            device.Clear(
                ClearOptions.Target |
                    ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend);
            spriteBatch.Draw(
                diffuse,
                new Rectangle(0, 0, 32, 32),
                Color.Black);
            spriteBatch.End();
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState =
                DepthStencilState.Default;
            device.RasterizerState =
                RasterizerState.CullNone;

            foreach (var pass in
                     effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    CreateFullScreenQuad(),
                    0,
                    2);
            }
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var scenePixels = new Color[32 * 32];
        var maskPixels = new Color[32 * 32];
        sceneTarget.GetData(scenePixels);
        maskTarget.GetData(maskPixels);
        var centerIndex = 16 * 32 + 16;

        Assert.Multiple(() =>
        {
            Assert.That(
                scenePixels[centerIndex].A,
                Is.EqualTo(255));
            Assert.That(
                maskPixels[centerIndex].A,
                Is.EqualTo(expectedMaskAlpha));
        });
    }

    private static void ConfigureEffect(
        Effect effect,
        Texture2D diffuse,
        bool selectionMaskEnabled)
    {
        effect.CurrentTechnique =
            effect.Techniques["BasicColorDrawing"];
        effect.Parameters["World"].SetValue(Matrix.Identity);
        effect.Parameters["View"].SetValue(Matrix.Identity);
        effect.Parameters["Projection"].SetValue(
            Matrix.Identity);
        effect.Parameters["CameraPos"].SetValue(
            new Vector3(0, 0, -2));
        effect.Parameters["DirLightTransform"].SetValue(
            Matrix.Identity);
        effect.Parameters["CapabilityFlag_ApplyAnimation"]
            .SetValue(false);
        effect.Parameters["UseDiffuse"].SetValue(true);
        effect.Parameters["DiffuseTexture"].SetValue(diffuse);
        effect.Parameters["UseSpecular"].SetValue(false);
        effect.Parameters["UseGloss"].SetValue(false);
        effect.Parameters["UseNormal"].SetValue(false);
        effect.Parameters["UseAlpha"].SetValue(false);
        effect.Parameters["UseMask"].SetValue(false);
        effect.Parameters["CapabilityFlag_ApplyTinting"]
            .SetValue(false);
        effect.Parameters["SelectionMaskEnabled"].SetValue(
            selectionMaskEnabled);
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
            Position = new Vector4(x, y, 0, 1),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            BiNormal = Vector3.UnitY,
            BlendWeights = Vector4.Zero,
            BlendIndices = Vector4.Zero
        };
    }

    private sealed class SelectionMaskTestMaterial :
        CapabilityMaterial
    {
        public SelectionMaskTestMaterial(
            IScopedResourceLibrary resourceLibrary)
            : base(
                CapabilityMaterialsEnum.SpecGlossPbr_Default,
                ShaderTypes.Pbr_SpecGloss,
                resourceLibrary)
        {
            Capabilities =
            [
                new CommonShaderParametersCapability()
            ];
            _renderingTechniqueMap[
                RenderingTechnique.Normal] =
                "BasicColorDrawing";
        }

        protected override CapabilityMaterial
            CreateCloneInstance()
        {
            return new SelectionMaskTestMaterial(
                _resourceLibrary);
        }
    }
}
