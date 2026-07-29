using System.Threading;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    internal class PbrLightColourEffectTests
    {
        private GraphicsDeviceServiceMock _graphicsDeviceService = null!;
        private ContentManager _content = null!;
        private ArcBallCamera _camera = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _graphicsDeviceService = new GraphicsDeviceServiceMock();

            var services = new GameServiceContainer();
            services.AddService<IGraphicsDeviceService>(_graphicsDeviceService);
            _content = new ContentManager(
                services,
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Content"));

            var deviceResolver = new Mock<IDeviceResolver>();
            deviceResolver
                .SetupGet(x => x.Device)
                .Returns(_graphicsDeviceService.GraphicsDevice);

            _camera = new ArcBallCamera(
                deviceResolver.Object,
                Mock.Of<IKeyboardComponent>(),
                Mock.Of<IMouseComponent>());
            _camera.Initialize();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _camera.Dispose();
            _content.Dispose();
            _graphicsDeviceService.Release();
        }

        [TestCase("Shaders/Pbr/MetalRoughness/MetalRoughness_main")]
        [TestCase("Shaders/Pbr/SpecGloss/SpecGloss_main")]
        public void Apply_UploadsSceneLightColourToCompiledEffect(string effectAsset)
        {
            var expected = new Vector3(0.2f, 0.7f, 0.4f);
            var sceneParameters = new SceneRenderParametersStore
            {
                LightColour = expected
            };

            var commonParameters = CommonShaderParameterBuilder.Build(_camera, sceneParameters);
            Assert.That(commonParameters.LightColour, Is.EqualTo(expected));

            var capability = new CommonShaderParametersCapability();
            capability.Assign(commonParameters, Matrix.Identity);

            var clonedCapability = (CommonShaderParametersCapability)capability.Clone();
            Assert.That(clonedCapability.LightColour, Is.EqualTo(expected));

            var effect = _content.Load<Effect>(effectAsset);
            clonedCapability.Apply(effect, null!);

            Assert.That(
                effect.Parameters["Constant_LightColour"].GetValueVector3(),
                Is.EqualTo(expected));
        }

        [Test]
        public void SceneParameters_DefaultLightColourIsWhite()
        {
            var sceneParameters = new SceneRenderParametersStore();

            Assert.That(sceneParameters.LightColour, Is.EqualTo(Vector3.One));
        }
    }
}
