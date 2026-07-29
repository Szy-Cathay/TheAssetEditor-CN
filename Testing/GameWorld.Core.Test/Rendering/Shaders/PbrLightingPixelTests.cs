using System.Threading;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class PbrLightingPixelTests
    {
        private const int RenderTargetSize = 32;
        private const float LightIntensity = 0.1f;

        private static readonly string[] s_effectAssets =
        [
            "Shaders\\Pbr\\MetalRoughness\\MetalRoughness_main",
            "Shaders\\Pbr\\SpecGloss\\SpecGloss_main",
        ];

        private static readonly short[] s_indices = [0, 1, 2, 2, 1, 3];

        private GraphicsDeviceServiceMock _graphicsDeviceService = null!;
        private ContentManager _content = null!;
        private TextureCube _blackCube = null!;

        [SetUp]
        public void SetUp()
        {
            _graphicsDeviceService = new GraphicsDeviceServiceMock();

            var services = new GameServiceContainer();
            services.AddService<IGraphicsDeviceService>(_graphicsDeviceService);

            var contentRoot = Path.Combine(TestContext.CurrentContext.TestDirectory, "Content");
            Assert.That(Directory.Exists(contentRoot), Is.True, $"Compiled content was not found at {contentRoot}");

            _content = new ContentManager(services, contentRoot);
            _blackCube = CreateSolidCube(_graphicsDeviceService.GraphicsDevice, Color.Black);
        }

        [TearDown]
        public void TearDown()
        {
            _blackCube.Dispose();
            _content.Dispose();
            _graphicsDeviceService.Release();
        }

        [TestCaseSource(nameof(s_effectAssets))]
        public void CameraFollowingLight_WhenCameraAndSurfaceRotateTogether_ResponseIsStable(string effectAsset)
        {
            var unrotated = Render(effectAsset, Matrix.Identity, new Vector3(0, 0, 3), Vector3.One);

            var rotation = Matrix.CreateRotationY(MathHelper.PiOver2);
            var rotatedCamera = Vector3.Transform(new Vector3(0, 0, 3), rotation);
            var rotated = Render(effectAsset, rotation, rotatedCamera, Vector3.One);

            var unrotatedLuminance = GetLuminance(unrotated);
            var rotatedLuminance = GetLuminance(rotated);
            var tolerance = Math.Max(3.0f / 255.0f, Math.Max(unrotatedLuminance, rotatedLuminance) * 0.03f);

            Assert.Multiple(() =>
            {
                Assert.That(unrotatedLuminance, Is.GreaterThan(3.0f / 255.0f), "The reference render must be visibly lit.");
                Assert.That(rotatedLuminance, Is.GreaterThan(3.0f / 255.0f), "The rotated render must be visibly lit.");
                Assert.That(Math.Abs(unrotatedLuminance - rotatedLuminance), Is.LessThanOrEqualTo(tolerance));
            });
        }

        [TestCaseSource(nameof(s_effectAssets))]
        public void ConstantLightColour_WhenUploadedAsGreen_SuppressesRedAndBlue(string effectAsset)
        {
            var response = Render(effectAsset, Matrix.Identity, new Vector3(0, 0, 3), Vector3.UnitY);
            var suppressedChannelTolerance = Math.Max(2.0f / 255.0f, response.Y * 0.02f);

            Assert.Multiple(() =>
            {
                Assert.That(response.Y, Is.GreaterThan(3.0f / 255.0f), "The selected green channel must remain visibly lit.");
                Assert.That(response.X, Is.LessThanOrEqualTo(suppressedChannelTolerance), "The red channel was not suppressed.");
                Assert.That(response.Z, Is.LessThanOrEqualTo(suppressedChannelTolerance), "The blue channel was not suppressed.");
            });
        }

        private Vector3 Render(string effectAsset, Matrix world, Vector3 cameraPosition, Vector3 lightColour)
        {
            var device = _graphicsDeviceService.GraphicsDevice;
            using var effect = _content.Load<Effect>(effectAsset).Clone();
            using var renderTarget = new RenderTarget2D(
                device,
                RenderTargetSize,
                RenderTargetSize,
                false,
                SurfaceFormat.Color,
                DepthFormat.Depth24);

            ConfigureMaterial(effect);

            var commonParameters = new CommonShaderParametersCapability
            {
                View = Matrix.CreateLookAt(cameraPosition, Vector3.Zero, Vector3.Up),
                Projection = Matrix.CreateOrthographic(2.5f, 2.5f, 0.1f, 10.0f),
                CameraPosition = cameraPosition,
                CameraLookAt = Vector3.Zero,
                ModelMatrix = world,
                LightIntensityMult = LightIntensity,
                LightColour = lightColour,
            };
            commonParameters.Apply(effect, null!);

            device.SetRenderTarget(renderTarget);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1.0f, 0);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;

            effect.CurrentTechnique = effect.Techniques["BasicColorDrawing"];
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                CreateQuadVertices(),
                0,
                4,
                s_indices,
                0,
                2);

            device.SetRenderTarget(null);

            var pixels = new Color[RenderTargetSize * RenderTargetSize];
            renderTarget.GetData(pixels);
            return AverageCenterPixels(pixels);
        }

        private void ConfigureMaterial(Effect effect)
        {
            effect.Parameters["UseDiffuse"]?.SetValue(false);
            effect.Parameters["UseSpecular"]?.SetValue(false);
            effect.Parameters["UseNormal"]?.SetValue(false);
            effect.Parameters["UseGloss"]?.SetValue(false);
            effect.Parameters["UseAlpha"]?.SetValue(false);
            effect.Parameters["UseMask"]?.SetValue(false);
            effect.Parameters["CapabilityFlag_ApplyAnimation"]?.SetValue(false);
            effect.Parameters["CapabilityFlag_ApplyTinting"]?.SetValue(false);
            effect.Parameters["CapabilityFlag_ApplyEmissive"]?.SetValue(false);
            effect.Parameters["SelectionMaskEnabled"]?.SetValue(false);
            effect.Parameters["tex_cube_diffuse"]?.SetValue(_blackCube);
            effect.Parameters["tex_cube_specular"]?.SetValue(_blackCube);
        }

        private static TextureCube CreateSolidCube(GraphicsDevice device, Color colour)
        {
            var cube = new TextureCube(device, 1, false, SurfaceFormat.Color);
            var pixel = new[] { colour };

            foreach (var face in Enum.GetValues<CubeMapFace>())
                cube.SetData(face, pixel);

            return cube;
        }

        private static VertexPositionNormalTextureCustom[] CreateQuadVertices()
        {
            return
            [
                CreateVertex(-1, -1),
                CreateVertex(-1, 1),
                CreateVertex(1, -1),
                CreateVertex(1, 1),
            ];
        }

        private static VertexPositionNormalTextureCustom CreateVertex(float x, float y)
        {
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(x, y, 0, 1),
                Normal = Vector3.UnitZ,
                TextureCoordinate = Vector2.Zero,
                Tangent = Vector3.UnitX,
                BiNormal = Vector3.UnitY,
                BlendWeights = Vector4.Zero,
                BlendIndices = Vector4.Zero,
                TextureCoordinate1 = Vector2.Zero,
            };
        }

        private static Vector3 AverageCenterPixels(Color[] pixels)
        {
            var sum = Vector3.Zero;
            var center = RenderTargetSize / 2;

            for (var y = center - 1; y <= center + 1; y++)
            {
                for (var x = center - 1; x <= center + 1; x++)
                    sum += pixels[y * RenderTargetSize + x].ToVector3();
            }

            return sum / 9.0f;
        }

        private static float GetLuminance(Vector3 colour)
        {
            return Vector3.Dot(colour, new Vector3(0.2126f, 0.7152f, 0.0722f));
        }

    }
}
