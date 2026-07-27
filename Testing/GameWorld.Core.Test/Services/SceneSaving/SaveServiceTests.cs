using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Geometry;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Services.SceneSaving.Material;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;

namespace Testing.GameWorld.Core.Services.SceneSaving
{
    [TestFixture]
    public class SaveServiceTests
    {
        [Test]
        public void Save_WhenGeometryGenerationFails_DoesNotGenerateMaterialOrPublishSavedEvent()
        {
            var context = CreateContext();
            context.GeometryStrategy
                .Setup(strategy => strategy.Generate(context.MainNode, context.Settings))
                .Returns((RmvFile)null);

            var result = context.Service.Save(context.MainNode, context.Settings);

            Assert.That(result.Status, Is.False);
            context.MaterialStrategy.Verify(
                strategy => strategy.Generate(
                    It.IsAny<MainEditableNode>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>()),
                Times.Never);
            context.EventHub.Verify(
                hub => hub.Publish(It.IsAny<ScopedFileSavedEvent>()),
                Times.Never);
        }

        [Test]
        public void Save_WhenMaterialGenerationFails_DoesNotPublishSavedEvent()
        {
            var context = CreateContext();
            context.GeometryStrategy
                .Setup(strategy => strategy.Generate(context.MainNode, context.Settings))
                .Returns(new RmvFile());
            context.MaterialStrategy
                .Setup(strategy => strategy.Generate(
                    context.MainNode,
                    context.Settings.OutputName,
                    context.Settings.OnlySaveVisible))
                .Returns(new WsMaterialResult(false, null, null));

            var result = context.Service.Save(context.MainNode, context.Settings);

            Assert.That(result.Status, Is.False);
            context.EventHub.Verify(
                hub => hub.Publish(It.IsAny<ScopedFileSavedEvent>()),
                Times.Never);
        }

        [Test]
        public void Save_WhenAllRequestedOutputsSucceed_PublishesSavedEventAndReturnsOutputs()
        {
            var context = CreateContext();
            var rmvFile = new RmvFile();
            const string materialPath = "variantmeshes/test.wsmodel";
            const string materialContent = "<model />";
            context.GeometryStrategy
                .Setup(strategy => strategy.Generate(context.MainNode, context.Settings))
                .Returns(rmvFile);
            context.MaterialStrategy
                .Setup(strategy => strategy.Generate(
                    context.MainNode,
                    context.Settings.OutputName,
                    context.Settings.OnlySaveVisible))
                .Returns(new WsMaterialResult(true, materialContent, materialPath));

            var result = context.Service.Save(context.MainNode, context.Settings);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.True);
                Assert.That(result.GeneratedMesh, Is.SameAs(rmvFile));
                Assert.That(result.GeneratedMaterialPath, Is.EqualTo(materialPath));
                Assert.That(result.GeneratedMaterialContent, Is.EqualTo(materialContent));
            });
            context.EventHub.Verify(
                hub => hub.Publish(It.Is<ScopedFileSavedEvent>(
                    notification => notification.NewPath == context.Settings.OutputName)),
                Times.Once);
        }

        private static SaveServiceTestContext CreateContext()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetEditablePack())
                .Returns(new PackFileContainer("test.pack"));

            var eventHub = new Mock<IEventHub>();
            var geometryStrategy = new Mock<IGeometryStrategy>();
            geometryStrategy
                .SetupGet(strategy => strategy.StrategyId)
                .Returns(GeometryStrategy.Rmv7);

            var materialStrategy = new Mock<IMaterialStrategy>();
            materialStrategy
                .SetupGet(strategy => strategy.StrategyId)
                .Returns(MaterialStrategy.WsModel_Warhammer3);

            var lodStrategy = new Mock<ILodGenerationStrategy>();
            lodStrategy
                .SetupGet(strategy => strategy.StrategyId)
                .Returns(LodStrategy.NoLodGeneration);

            var settings = new GeometrySaveSettings(
                new ApplicationSettingsService(GameTypeEnum.Warhammer3))
            {
                OutputName = "variantmeshes/test.rigid_model_v2",
                GeometryOutputType = GeometryStrategy.Rmv7,
                MaterialOutputType = MaterialStrategy.WsModel_Warhammer3,
                LodGenerationMethod = LodStrategy.NoLodGeneration,
            };
            var mainNode = new MainEditableNode(
                "Editable Model",
                new SkeletonNode(null),
                packFileService.Object);

            var service = new SaveService(
                packFileService.Object,
                eventHub.Object,
                new GeometryStrategyProvider([geometryStrategy.Object]),
                new LodStrategyProvider([lodStrategy.Object]),
                new MaterialStrategyProvider([materialStrategy.Object]));

            return new SaveServiceTestContext(
                service,
                eventHub,
                geometryStrategy,
                materialStrategy,
                mainNode,
                settings);
        }

        private sealed record SaveServiceTestContext(
            SaveService Service,
            Mock<IEventHub> EventHub,
            Mock<IGeometryStrategy> GeometryStrategy,
            Mock<IMaterialStrategy> MaterialStrategy,
            MainEditableNode MainNode,
            GeometrySaveSettings Settings);
    }
}
