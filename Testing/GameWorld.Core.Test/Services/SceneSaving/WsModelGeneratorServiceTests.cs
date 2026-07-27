using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.Services.SceneSaving.Material;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Testing.GameWorld.Core.Services.SceneSaving
{
    [TestFixture]
    public class WsModelGeneratorServiceTests
    {
        [Test]
        public void GenerateWsModel_WhenSaveSucceeds_ReturnsSuccessfulPathAndContent()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetEditablePack())
                .Returns(new PackFileContainer("test.pack"));
            var fileSaveService = new Mock<IFileSaveService>();
            var serializer = new Mock<IMaterialToWsMaterialSerializer>();
            var service = new WsModelGeneratorService(
                packFileService.Object,
                fileSaveService.Object);

            var result = service.GenerateWsModel(
                serializer.Object,
                "variantmeshes/test.rigid_model_v2",
                []);

            Assert.Multiple(() =>
            {
                Assert.That(result.Result, Is.True);
                Assert.That(
                    result.GeneratedFilePath,
                    Is.EqualTo("variantmeshes/test.wsmodel"));
                Assert.That(result.Content, Does.Contain("<model version=\"1\">"));
                Assert.That(
                    result.Content,
                    Does.Contain(
                        "<geometry>variantmeshes/test.rigid_model_v2</geometry>"));
            });
            fileSaveService.Verify(
                saveService => saveService.Save(
                    "variantmeshes/test.wsmodel",
                    It.IsAny<byte[]>(),
                    false),
                Times.Once);
        }
    }
}
