#nullable enable
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.WsModel;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders;

[NonParallelizable]
public class FlagWindShaderPixelTests
{
    [TestCase(.4f, 2.80255599f)]
    [TestCase(1.3f, 2.93365192f)]
    public void NativeVertexEquation_UsesSquareRootPhaseAndTheRotatedWorldBasis(float time, float expectedDepth)
    {
        using var fixture = new Fixture();
        // Diagnostic quad; expected depths follow the shipping VS with explicit wind, not game acceptance.
        var pixel = fixture.Render(time, Vector2.UnitX * .2f, new(.8f, .2f, .6f, 1));
        Assert.That(pixel.W, Is.EqualTo(expectedDepth).Within(.0003f));
    }

    [Test]
    public void OriginalVertexAlpha_IsWindWeightAndDoesNotChangeSurfaceOpacity()
    {
        using var fixture = new Fixture();
        var pinned = fixture.Render(.4f, Vector2.UnitX * .2f, new(.8f, .2f, .6f, 0));
        var originalByteColour = new Vector4(.8f, .2f, .6f, 128f / 255f);
        Assert.That(new Color(originalByteColour).A, Is.EqualTo(128));
        var half = fixture.Render(.4f, Vector2.UnitX * .2f, originalByteColour);
        Assert.Multiple(() =>
        {
            Assert.That(pinned.W, Is.EqualTo(3).Within(.00001f));
            Assert.That(half.W, Is.EqualTo(2.90749169f).Within(.0003f));
        });
    }

    [Test]
    public void NativeWeighted2Shader_UsesItsAdditionalOneTenthVertexGain()
    {
        using var fixture = new Fixture();
        var pixel = fixture.Render(.4f, Vector2.UnitX * .2f, new(.8f, .2f, .6f, 1), vertexScale: .1f);
        Assert.That(pixel.W, Is.EqualTo(2.98025560f).Within(.00003f));
    }

    [Test]
    public void ExplicitZeroWind_IsFiniteAndDoesNotAnimateTheDiagnosticGeometry()
    {
        using var fixture = new Fixture();
        Assert.That(fixture.Render(3, Vector2.Zero, Vector4.One).W, Is.EqualTo(3).Within(.00001f));
    }

    [Test]
    public void DisabledOrdinaryMaterial_DoesNotInheritFlagWindFromSharedEffect()
    {
        using var fixture = new Fixture();
        var before = fixture.Render(.4f, Vector2.UnitX * .2f, Vector4.One, enabled: false);
        _ = fixture.Render(.4f, Vector2.UnitX * .2f, Vector4.One);
        var after = fixture.Render(1.3f, Vector2.UnitX * .2f, Vector4.One, enabled: false);
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void OriginalVertexRed_ControlsTheTwoClothNormalsWithoutMovingTheGeometry()
    {
        using var fixture = new Fixture();
        var noDetail = fixture.Render(.4f, Vector2.UnitX * .2f, new(0, .2f, .6f, 0), normalSurface: true);
        var withDetail = fixture.Render(.4f, Vector2.UnitX * .2f, new(1, .2f, .6f, 0), normalSurface: true);
        var difference = Vector3.Distance(new(noDetail.X, noDetail.Y, noDetail.Z), new(withDetail.X, withDetail.Y, withDetail.Z));
        Assert.That(difference, Is.GreaterThan(.0001f));
    }

    [Test]
    public void VerifiedFlag_DoesNotAccessAnUnboundRmvMaskButOrdinaryMaterialsKeepTheFallback()
    {
        using var fixture = new Fixture();
        fixture.VerifyMaskBindingPolicy();
    }

    [TestCase(2, 155f / 255f, 0f, 0f, 0f, 2.04705882f)]
    [TestCase(4, 128f / 255f, 64f / 255f, 32f / 255f, 0f, 2.05333333f)]
    [TestCase(4, 128f / 255f, 0f, 0f, 1f, 2.4f)]
    [TestCase(4, .5f, 1f / 256f, 0f, 1f, 2.40078125f)]
    public void NativeFlagSkinning_UsesResidualWeightsAndTheShippingUnusedBoneRule(
        int count, float x, float y, float z, float unused, float nativeDepth)
    {
        using var fixture = new Fixture();
        // Fixed shipping-VS goldens: bone Z .2/.6/1/1.4, object Z .4, eye Z 3.
        // The two-bone case uses Z .2/1.1; raw second/fourth weights are deliberately unusable.
        var pixel = fixture.Render(0, Vector2.Zero, Vector4.One, weights: new(x, y, z, unused), weightCount: count);
        Assert.That(pixel.W, Is.EqualTo(nativeDepth).Within(.00002f));
    }

    [TestCase(2, 155f / 255f, 0f, 0f, 0f, 2.63529412f)]
    [TestCase(4, 128f / 255f, 64f / 255f, 32f / 255f, 0f, 2.27215686f)]
    public void OrdinaryMaterial_PreservesItsExistingRawWeightAndHomogeneousPositionRule(
        int count, float x, float y, float z, float w, float existingDepth)
    {
        using var fixture = new Fixture();
        var pixel = fixture.Render(0, Vector2.Zero, Vector4.One, enabled: false,
            weights: new(x, y, z, w), weightCount: count);
        Assert.That(pixel.W, Is.EqualTo(existingDepth).Within(.00002f));
    }

    private sealed class Fixture : IDisposable
    {
        private const int Size = 64;
        private readonly WpfGameMock _game = new();
        private readonly Effect _effect;
        private readonly RenderTarget2D _target;
        private readonly Texture2D _baseNormal;
        private readonly Texture2D _cloth;
        private readonly Mock<IScopedResourceLibrary> _resources = new();
        private readonly FlagWindCapability _flag = new()
        {
            WindInfluenceStrength = .6f,
            ClothNormal1Path = "cloth",
            ClothNormal2Path = "cloth",
        };

        public Fixture()
        {
            var device = _game.GraphicsDevice;
            _effect = _game.Content.Load<Effect>("Shaders/Pbr/MetalRoughness/MetalRoughness_main").Clone();
            _target = new(device, Size, Size, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            _baseNormal = new(device, 1, 1);
            _baseNormal.SetData([new Color(255, 128, 255, 128)]);
            _cloth = new(device, 2, 1);
            _cloth.SetData([new Color(255, 128, 255, 0), new Color(255, 128, 255, 255)]);
            _resources.Setup(r => r.LoadTexture("cloth", false, false)).Returns(_cloth);
            _effect.Parameters["NormalTexture"].SetValue(_baseNormal);
            _effect.Parameters["UseDiffuse"].SetValue(false);
            _effect.Parameters["UseNormal"].SetValue(true);
            _effect.Parameters["UseGloss"].SetValue(false);
            _effect.Parameters["UseAlpha"].SetValue(false);
            _effect.Parameters["UseMask"].SetValue(false);
            _effect.Parameters["CapabilityFlag_ApplyAnimation"].SetValue(false);
            _effect.Parameters["CapabilityFlag_ApplyTinting"].SetValue(false);
            _effect.Parameters["CapabilityFlag_ApplyEmissive"].SetValue(false);
        }

        public Vector4 Render(float time, Vector2 wind, Vector4 colour, bool enabled = true, bool normalSurface = false,
            float vertexScale = 1, Vector4? weights = null, int weightCount = 0)
        {
            var eye = new Vector3(0, 0, 3);
            var common = new CommonShaderParametersCapability();
            common.Assign(new(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(2.5f, 2.5f, .1f, 10), eye, Vector3.Zero,
                0, 0, 0, 1, Vector3.One, [], MaterialTimeSeconds: time, WindVector: wind),
                Matrix.CreateRotationZ(MathHelper.PiOver2) * Matrix.CreateTranslation(0, 0, weights == null ? 0 : .4f));
            common.Apply(_effect, _resources.Object);
            _effect.Parameters["CapabilityFlag_ApplyAnimation"].SetValue(weights != null);
            _effect.Parameters["Animation_WeightCount"].SetValue(weightCount);
            if (weights != null)
            {
                var transforms = Enumerable.Repeat(Matrix.Identity, 256).ToArray();
                transforms[0] = Matrix.CreateTranslation(0, 0, .2f);
                transforms[1] = Matrix.CreateTranslation(0, 0, weightCount == 2 ? 1.1f : .6f);
                transforms[2] = Matrix.CreateTranslation(0, 0, 1);
                transforms[3] = Matrix.CreateTranslation(0, 0, 1.4f);
                _effect.Parameters["Animation_Tranforms"].SetValue(transforms);
            }
            _flag.Enabled = enabled;
            _flag.VertexWindScale = vertexScale;
            _flag.Apply(_effect, _resources.Object);
            Assert.Multiple(() =>
            {
                Assert.That(_effect.Parameters["FlagWindVector"].GetValueVector2(), Is.EqualTo(wind));
                Assert.That(_effect.Parameters["FlagWindTimeSeconds"].GetValueSingle(), Is.EqualTo(time));
                Assert.That(_effect.Parameters["FlagWindEnabled"].GetValueBoolean(), Is.EqualTo(enabled));
                Assert.That(_effect.Parameters["FlagVertexWindScale"].GetValueSingle(), Is.EqualTo(vertexScale));
            });

            var device = _game.GraphicsDevice;
            device.SetRenderTarget(_target);
            device.Clear(Color.Transparent);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            _effect.CurrentTechnique = _effect.Techniques[normalSurface ? "BasicColorDrawing" : "ViewportGeometry"];
            _effect.CurrentTechnique.Passes[0].Apply();
            VertexPositionNormalTextureCustom Vertex(float x, float y) => new()
            {
                Position = new(x, y, 0, 1),
                Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, BiNormal = Vector3.UnitY,
                TextureCoordinate = new(.25f, .5f), VertexColour = new Color(colour),
                BlendWeights = weights ?? Vector4.Zero, BlendIndices = new(0, 1, 2, 3),
            };
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip,
                new[] { Vertex(-.8f, -.8f), Vertex(-.8f, .8f), Vertex(.8f, -.8f), Vertex(.8f, .8f) }, 0, 2);
            device.SetRenderTarget(null);
            var pixels = new Vector4[Size * Size];
            _target.GetData(pixels);
            return pixels[Size / 2 * Size + Size / 2];
        }

        public void VerifyMaskBindingPolicy()
        {
            const string missingMask = "original_rmv_mask_missing.dds";
            var rmv = new WeightedMaterial();
            rmv.SetTexture(TextureType.Mask, missingMask);
            _resources.Setup(r => r.LoadTexture("base_normal", false, false)).Returns(_baseNormal);
            _resources.Setup(r => r.LoadTexture(missingMask, false, false)).Throws<FileNotFoundException>();
            var factory = new CapabilityMaterialFactory(new ApplicationSettingsService(GameTypeEnum.ThreeKingdoms), _resources.Object);
            WsModelMaterialFile Source(string shader) => new($$"""
                <material><name>weighted4_alpha_off</name><shader>shaders/{{shader}}.xml.shader</shader>
                  <textures>
                    <texture><slot>s_xml_normal</slot><source>base_normal</source></texture>
                    <texture><slot>s_xml_cloth_normal_1</slot><source>cloth</source></texture>
                    <texture><slot>s_xml_cloth_normal_2</slot><source>cloth</source></texture>
                  </textures>
                </material>
                """);
            var flag = factory.Create(rmv, Source("weighted4_character_flag")).GetCapability<MetalRoughCapability>();
            flag.Apply(_effect, _resources.Object);
            _resources.Verify(r => r.LoadTexture(missingMask, false, false), Times.Never);
            Assert.Multiple(() =>
            {
                Assert.That(flag.Mask.UseTexture, Is.False);
                Assert.That(flag.Mask.TexturePath, Is.EqualTo(missingMask));
                Assert.That(flag.NormalMap.TexturePath, Is.EqualTo("base_normal"));
                Assert.That(_effect.Parameters["UseMask"].GetValueBoolean(), Is.False);
                Assert.That(rmv.GetTexture(TextureType.Mask)!.Value.Path, Is.EqualTo(missingMask));
            });
            var ordinary = factory.Create(rmv, Source("weighted4_character")).GetCapability<MetalRoughCapability>();
            Assert.That(ordinary.Mask.UseTexture, Is.True);
            Assert.Throws<FileNotFoundException>(() => ordinary.Apply(_effect, _resources.Object));
            _resources.Verify(r => r.LoadTexture(missingMask, false, false), Times.Once);
        }

        public void Dispose()
        {
            _effect.Dispose();
            _target.Dispose();
            _baseNormal.Dispose();
            _cloth.Dispose();
        }
    }
}
