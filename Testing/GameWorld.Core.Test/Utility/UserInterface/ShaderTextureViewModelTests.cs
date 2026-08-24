using GameWorld.Core.Commands;
using GameWorld.Core.Services;
using GameWorld.Core.Utility.UserInterface;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.Types;

namespace GameWorld.Core.Test.Utility.UserInterface
{
    internal class ShaderTextureViewModelTests
    {
        [Test]
        public void DisabledTexture_DoesNotRequirePath_AndReenabledRestoresValidation()
        {
            var textureInput = new GameWorld.Core.Rendering.Materials.Capabilities.Utility.TextureInput(
                TextureType.MaterialMap)
            {
                TexturePath = string.Empty,
                UseTexture = false,
            };
            var viewModel = new ShaderTextureViewModel(
                textureInput,
                Mock.Of<IPackFileService>(),
                Mock.Of<IUiCommandFactory>(),
                Mock.Of<IScopedResourceLibrary>(),
                Mock.Of<IStandardDialogs>());

            Assert.That(viewModel.HasErrors, Is.False);

            viewModel.ShouldRenderTexture = true;

            Assert.That(viewModel.HasErrors, Is.True);

            viewModel.ShouldRenderTexture = false;

            Assert.That(viewModel.HasErrors, Is.False);
        }

        [Test]
        public void LegacyDefaultTexture_WithCommonAlias_IsValidWithoutChangingPath()
        {
            const string legacyPath =
                @"rigidmodels\buildings\textures\flatnormal.dds";
            const string commonPath = @"commontextures\flatnormal.dds";
            var textureInput = new GameWorld.Core.Rendering.Materials.Capabilities.Utility.TextureInput(
                TextureType.Normal)
            {
                TexturePath = legacyPath,
                UseTexture = true,
            };
            var commonTexture = PackFile.CreateFromBytes(
                "flatnormal.dds",
                [1, 2, 3]);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.FindFile(legacyPath, null))
                .Returns((PackFile)null);
            packFileService
                .Setup(service => service.FindFile(commonPath, null))
                .Returns(commonTexture);

            var viewModel = new ShaderTextureViewModel(
                textureInput,
                packFileService.Object,
                Mock.Of<IUiCommandFactory>(),
                Mock.Of<IScopedResourceLibrary>(),
                Mock.Of<IStandardDialogs>());

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.HasErrors, Is.False);
                Assert.That(viewModel.Path, Is.EqualTo(legacyPath));
                Assert.That(textureInput.TexturePath, Is.EqualTo(legacyPath));
            });
        }
    }
}
