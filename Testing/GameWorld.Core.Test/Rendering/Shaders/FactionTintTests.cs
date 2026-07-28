using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders;

[NonParallelizable]
public class FactionTintTests
{
    [TestCase(CapabilityMaterialsEnum.MetalRoughPbr_Default)]
    [TestCase(CapabilityMaterialsEnum.MetalRoughPbr_Emissive)]
    [TestCase(CapabilityMaterialsEnum.SpecGlossPbr_Default)]
    [TestCase(CapabilityMaterialsEnum.SpecGlossPbr_Advanced)]
    public void Material_RegistersFactionTintPreview(
        CapabilityMaterialsEnum materialType)
    {
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        var factory = new CapabilityMaterialFactory(settings, null!);

        var material = factory.CreateMaterial(materialType);

        Assert.That(
            material.TryGetCapability<TintCapability>(),
            Is.Not.Null);
    }

    [Test]
    public void SpecGlossCapability_BindsFactionMaskTexture()
    {
        var game = new WpfGameMock();
        var effect = game.Content.Load<Effect>(
            "Shaders\\Pbr\\SpecGloss\\SpecGloss_main");
        using var mask = new Texture2D(
            game.GraphicsDevice,
            1,
            1);
        mask.SetData([Color.Red]);
        var resources = new Mock<IScopedResourceLibrary>();
        resources
            .Setup(library => library.LoadTexture("mask.dds"))
            .Returns(mask);
        var capability = new SpecGlossCapability();
        capability.Mask.UseTexture = true;
        capability.Mask.TexturePath = "mask.dds";

        capability.Apply(effect, resources.Object);

        Assert.Multiple(() =>
        {
            Assert.That(
                effect.Parameters["UseMask"].GetValueBoolean(),
                Is.True);
            Assert.That(
                effect.Parameters["MaskTexture"].GetValueTexture2D(),
                Is.SameAs(mask));
        });
    }

    [TestCase("Shaders\\Pbr\\MetalRoughness\\MetalRoughness_main")]
    [TestCase("Shaders\\Pbr\\SpecGloss\\SpecGloss_main")]
    public void TintCapability_BindsFactionPreviewParameters(
        string effectAsset)
    {
        var game = new WpfGameMock();
        var effect = game.Content.Load<Effect>(effectAsset);
        var capability = new TintCapability
        {
            ApplyCapability = true,
            UseFactionColours = true,
            FactionColours =
            [
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 1)
            ]
        };

        capability.Apply(effect, Mock.Of<IScopedResourceLibrary>());

        Assert.Multiple(() =>
        {
            Assert.That(
                effect.Parameters["CapabilityFlag_ApplyTinting"]
                    .GetValueBoolean(),
                Is.True);
            Assert.That(
                effect.Parameters["Tint_UseFactionColours"]
                    .GetValueBoolean(),
                Is.True);
            Assert.That(
                effect.Parameters["Tint_FactionsColours"]
                    .GetValueVector3Array(),
                Is.EqualTo(capability.FactionColours));
        });
    }

    [Test]
    public void SpecGlossShader_AppliesFactionColourFromMaskChannel()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\Pbr\\SpecGloss\\SpecGloss_main");
        using var diffuse = CreateTexture(device, Color.White);
        using var mask = CreateTexture(device, Color.Red);
        ConfigureSpecGlossEffect(effect, diffuse, mask);

        var redPixel = RenderFactionColour(
            device,
            effect,
            new Vector3(1, 0, 0));
        var bluePixel = RenderFactionColour(
            device,
            effect,
            new Vector3(0, 0, 1));

        Assert.Multiple(() =>
        {
            Assert.That(redPixel.R, Is.GreaterThan(redPixel.B + 20));
            Assert.That(bluePixel.B, Is.GreaterThan(bluePixel.R + 20));
        });
    }

    private static Texture2D CreateTexture(
        GraphicsDevice device,
        Color colour)
    {
        var texture = new Texture2D(device, 1, 1);
        texture.SetData([colour]);
        return texture;
    }

    private static void ConfigureSpecGlossEffect(
        Effect effect,
        Texture2D diffuse,
        Texture2D mask)
    {
        effect.CurrentTechnique =
            effect.Techniques["BasicColorDrawing"];
        effect.Parameters["World"].SetValue(Matrix.Identity);
        effect.Parameters["View"].SetValue(Matrix.Identity);
        effect.Parameters["Projection"].SetValue(Matrix.Identity);
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
        effect.Parameters["UseMask"].SetValue(true);
        effect.Parameters["MaskTexture"].SetValue(mask);
        effect.Parameters["CapabilityFlag_ApplyTinting"]
            .SetValue(true);
        effect.Parameters["Tint_UseFactionColours"]
            .SetValue(true);
        effect.Parameters["Tint_UseTinting"].SetValue(false);
        effect.Parameters["Tint_TintColours"].SetValue(
        [
            Vector3.One,
            Vector3.One,
            Vector3.One
        ]);
    }

    private static Color RenderFactionColour(
        GraphicsDevice device,
        Effect effect,
        Vector3 factionColour)
    {
        effect.Parameters["Tint_FactionsColours"].SetValue(
        [
            factionColour,
            Vector3.One,
            Vector3.One
        ]);
        var vertices = CreateFullScreenQuad();
        using var renderTarget = new RenderTarget2D(
            device,
            32,
            32,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    vertices,
                    0,
                    2);
            }
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var pixels = new Color[32 * 32];
        renderTarget.GetData(pixels);
        return pixels[16 * 32 + 16];
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
            TextureCoordinate = Vector2.Zero,
            TextureCoordinate1 = Vector2.Zero,
            BlendWeights = Vector4.Zero,
            BlendIndices = Vector4.Zero
        };
    }
}
