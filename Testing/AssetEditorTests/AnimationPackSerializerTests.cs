using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;

namespace AssetEditorTests
{
    [TestClass]
    public class AnimationPackSerializerTests
    {
        [TestMethod]
        public void Load_WhenKnownChildParserFails_PreservesOnlyThatChildPayload()
        {
            byte[] invalidChildPayload = [0xDE, 0xAD, 0xBE, 0xEF];
            byte[] sentinelPayload = [0x11, 0x22, 0x33];
            var source = new AnimationPackFileDatabase("test.animpack");
            source.AddFile(new UnknownAnimFile("broken.bin", invalidChildPayload));
            source.AddFile(new UnknownAnimFile("sentinel.unknown", sentinelPayload));
            var parentPackBytes = AnimationPackSerializer.ConvertToBytes(source);
            var packFile = PackFile.CreateFromBytes("test.animpack", parentPackBytes);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetFullPath(packFile, It.IsAny<PackFileContainer?>()))
                .Returns("test.animpack");

            var loaded = AnimationPackSerializer.Load(packFile, packFileService.Object);

            var loadedFiles = loaded.Files.ToArray();
            Assert.AreEqual(2, loadedFiles.Length);
            Assert.IsInstanceOfType(loadedFiles[0], typeof(UnknownAnimFile));
            Assert.IsInstanceOfType(loadedFiles[1], typeof(UnknownAnimFile));

            var fallbackBytes = loadedFiles[0].ToByteArray();
            var repackedBytes = AnimationPackSerializer.ConvertToBytes(loaded);
            CollectionAssert.AreEqual(
                invalidChildPayload,
                fallbackBytes,
                $"FallbackContainsParent={fallbackBytes.SequenceEqual(parentPackBytes)}; " +
                $"RepackMatchesParent={repackedBytes.SequenceEqual(parentPackBytes)}");
            Assert.IsFalse(fallbackBytes.SequenceEqual(parentPackBytes));
            CollectionAssert.AreEqual(sentinelPayload, loadedFiles[1].ToByteArray());
            CollectionAssert.AreEqual(parentPackBytes, repackedBytes);
        }
    }
}
