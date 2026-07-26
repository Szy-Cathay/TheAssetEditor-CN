using Shared.ByteParsing;
using Shared.GameFormats.Dat;

namespace AssetEditorTests
{
    [TestClass]
    public class DatFileParserTests
    {
        [TestMethod]
        public void WriteData_NonAsciiEventUsesUtf8ByteLengthAndRoundTrips()
        {
            var input = "中文";
            var file = new SoundDatFile();
            file.EventWithStateGroup.Add(new SoundDatFile.DatEventWithStateGroup
            {
                Event = input,
                Value = 1.5f
            });

            var bytes = DatFileParser.WriteData(file);

            Assert.AreEqual(6, BitConverter.ToInt32(bytes, sizeof(uint)));
            Assert.AreEqual(input, DatFileParser.ReadStr32(new ByteChunk(bytes, sizeof(uint))));
        }
    }
}
