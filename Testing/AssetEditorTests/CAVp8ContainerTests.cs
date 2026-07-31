using System.Text;
using Shared.GameFormats.Video;

namespace AssetEditorTests
{
    [TestClass]
    public class CAVp8ContainerTests
    {
        [TestMethod]
        public void ReadData_RebuildsFrameDataFromRecordedOffsets()
        {
            var caVp8 = new CAVp8File(CreateCaVp8WithGap());

            CollectionAssert.AreEqual(
                new byte[] { 1, 2, 3, 4, 5 },
                caVp8.FrameData);
            Assert.AreEqual(2, caVp8.FrameTable.Count);
            Assert.AreEqual(0u, caVp8.FrameTable[0].Offset);
            Assert.AreEqual(3u, caVp8.FrameTable[1].Offset);
        }

        [TestMethod]
        public void ReadData_RejectsFrameThatExtendsIntoFrameTable()
        {
            var bytes = CreateCaVp8WithGap();
            const int secondFrameRecordOffset = 47 + 9;
            BitConverter.GetBytes(46u).CopyTo(bytes, secondFrameRecordOffset);
            BitConverter.GetBytes(3u).CopyTo(
                bytes,
                secondFrameRecordOffset + sizeof(uint));

            Assert.ThrowsException<InvalidDataException>(
                () => new CAVp8File(bytes));
        }

        [TestMethod]
        public void WriteIvf_WritesAllFramesUsingCanonicalFrameData()
        {
            var bytes = new IvfFile(
                new CAVp8File(CreateCaVp8WithGap()))
                .WriteData();

            Assert.AreEqual("DKIF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.AreEqual((ushort)0, BitConverter.ToUInt16(bytes, 4));
            Assert.AreEqual((ushort)32, BitConverter.ToUInt16(bytes, 6));
            Assert.AreEqual("VP80", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.AreEqual((ushort)16, BitConverter.ToUInt16(bytes, 12));
            Assert.AreEqual((ushort)8, BitConverter.ToUInt16(bytes, 14));
            Assert.AreEqual(25u, BitConverter.ToUInt32(bytes, 16));
            Assert.AreEqual(1u, BitConverter.ToUInt32(bytes, 20));
            Assert.AreEqual(2u, BitConverter.ToUInt32(bytes, 24));

            Assert.AreEqual(3u, BitConverter.ToUInt32(bytes, 32));
            Assert.AreEqual(0ul, BitConverter.ToUInt64(bytes, 36));
            CollectionAssert.AreEqual(
                new byte[] { 1, 2, 3 },
                bytes[44..47]);

            Assert.AreEqual(2u, BitConverter.ToUInt32(bytes, 47));
            Assert.AreEqual(1ul, BitConverter.ToUInt64(bytes, 51));
            CollectionAssert.AreEqual(
                new byte[] { 4, 5 },
                bytes[59..61]);
            Assert.AreEqual(61, bytes.Length);
        }

        [TestMethod]
        public void WriteIvf_RejectsFrameTableThatExceedsFrameData()
        {
            var caVp8 = new CAVp8File(CreateCaVp8WithGap());
            caVp8.FrameTable[1].Size = 10;

            Assert.ThrowsException<InvalidDataException>(
                () => new IvfFile(caVp8).WriteData());
        }

        private static byte[] CreateCaVp8WithGap()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes("CAMV"));
            writer.Write((ushort)0);
            writer.Write((ushort)32);
            writer.Write(Encoding.ASCII.GetBytes("VP80"));
            writer.Write((ushort)16);
            writer.Write((ushort)8);
            writer.Write(40f);
            writer.Write(1u);
            writer.Write(1u);
            writer.Write(47u);
            writer.Write(2u);
            writer.Write(3u);

            writer.Write(new byte[] { 1, 2, 3, 99, 98, 4, 5 });

            writer.Write(40u);
            writer.Write(3u);
            writer.Write(true);
            writer.Write(45u);
            writer.Write(2u);
            writer.Write(false);

            return stream.ToArray();
        }
    }
}
