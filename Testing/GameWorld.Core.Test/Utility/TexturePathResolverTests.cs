using GameWorld.Core.Utility;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace GameWorld.Core.Test.Utility
{
    internal class TexturePathResolverTests
    {
        [Test]
        public void FindTextureFile_MissingLegacyFlatNormal_UsesCommonTexture()
        {
            const string legacyPath =
                @"rigidmodels\buildings\textures\flatnormal.dds";
            const string commonPath = @"commontextures\flatnormal.dds";
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

            var result = TexturePathResolver.FindTextureFile(
                packFileService.Object,
                legacyPath,
                out var resolvedPath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.SameAs(commonTexture));
                Assert.That(resolvedPath, Is.EqualTo(commonPath));
            });
        }

        [Test]
        public void FindTextureFile_ExactLegacyPathExists_PrefersExactFile()
        {
            const string legacyPath =
                @"rigidmodels\buildings\textures\flatnormal.dds";
            const string commonPath = @"commontextures\flatnormal.dds";
            var exactTexture = PackFile.CreateFromBytes(
                "flatnormal.dds",
                [4, 5, 6]);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.FindFile(legacyPath, null))
                .Returns(exactTexture);

            var result = TexturePathResolver.FindTextureFile(
                packFileService.Object,
                legacyPath,
                out var resolvedPath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.SameAs(exactTexture));
                Assert.That(resolvedPath, Is.EqualTo(legacyPath));
            });
            packFileService.Verify(
                service => service.FindFile(commonPath, null),
                Times.Never);
        }

        [Test]
        public void FindTextureFile_UnrelatedMissingPath_DoesNotTryAlias()
        {
            const string missingPath = @"rigidmodels\buildings\textures\missing.dds";
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.FindFile(missingPath, null))
                .Returns((PackFile)null);

            var result = TexturePathResolver.FindTextureFile(
                packFileService.Object,
                missingPath,
                out var resolvedPath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null);
                Assert.That(resolvedPath, Is.EqualTo(missingPath));
            });
            packFileService.Verify(
                service => service.FindFile(
                    It.Is<string>(path => path.StartsWith("commontextures")),
                    null),
                Times.Never);
        }

        [Test]
        public void FindTextureFile_MissingLegacyTestBlack_UsesCommonTexture()
        {
            const string legacyPath =
                @"RIGIDMODELS/BUILDINGS/TEXTURES/TEST_BLACK.DDS";
            const string commonPath = @"commontextures\test_black.dds";
            var commonTexture = PackFile.CreateFromBytes(
                "test_black.dds",
                [7, 8, 9]);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.FindFile(legacyPath, null))
                .Returns((PackFile)null);
            packFileService
                .Setup(service => service.FindFile(commonPath, null))
                .Returns(commonTexture);

            var result = TexturePathResolver.FindTextureFile(
                packFileService.Object,
                legacyPath,
                out var resolvedPath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.SameAs(commonTexture));
                Assert.That(resolvedPath, Is.EqualTo(commonPath));
            });
        }
    }
}
