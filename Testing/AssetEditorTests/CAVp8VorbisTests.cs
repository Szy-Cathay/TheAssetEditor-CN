using Shared.GameFormats.Wwise.Wem.V132.Encoding;

namespace AssetEditorTests
{
    [TestClass]
    public class CAVp8VorbisTests
    {
        [TestMethod]
        public void CodebookLibrary_LoadsPackedCodebooks()
        {
            var library = new WwiseCodebookLibrary();

            Assert.IsTrue(library.LibraryCount > 0);
            var firstCodebook = library.GetCodebook(0);
            Assert.IsTrue(firstCodebook.BitCount > 0);
            Assert.IsTrue(firstCodebook.Data.Length > 0);
        }
    }
}
