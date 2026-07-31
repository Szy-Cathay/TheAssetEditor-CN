using System.Buffers.Binary;
using System.Text;
using Shared.GameFormats.Audio.Codecs.Vorbis;
using Shared.GameFormats.Video;

namespace AssetEditorTests
{
    [TestClass]
    public class CAVp8WebMTests
    {
        [TestMethod]
        public void WriteData_WritesVp8TrackAndCnApplicationMetadata()
        {
            var bytes = CreateWebM().WriteData();

            CollectionAssert.AreEqual(
                new byte[] { 0x1A, 0x45, 0xDF, 0xA3 },
                bytes[..4]);
            StringAssert.Contains(
                Encoding.ASCII.GetString(bytes),
                "webm");
            StringAssert.Contains(
                Encoding.ASCII.GetString(bytes),
                "V_VP8");
            StringAssert.Contains(
                Encoding.ASCII.GetString(bytes),
                "AssetEditor.CN");
        }

        [TestMethod]
        public void WriteData_LongAudioTail_PreservesPacketTimestampsAcrossClusters()
        {
            var webM = CreateWebMWithLongAudioTail();

            var timestamps = ReadAudioBlockTimestamps(webM.WriteData());

            Assert.AreEqual(4, timestamps.Count);
            for (var index = 1; index < timestamps.Count; index++)
            {
                Assert.IsTrue(
                    timestamps[index] > timestamps[index - 1],
                    "Audio block timestamps must remain strictly increasing.");
            }
            CollectionAssert.AreEqual(
                new long[] { 0, 32768, 40000, 41000 },
                timestamps);
        }

        [TestMethod]
        public void WriteData_AudioLongerThanVideo_ExtendsSegmentDuration()
        {
            var webM = CreateWebMWithLongAudioTail();

            var durationMs = ReadDurationMilliseconds(webM.WriteData());
            var videoEndMs =
                webM.FrameTable.Count / (double)webM.Framerate * 1000.0;
            var audioEndMs =
                webM.VorbisAudioPackets[^1].TimestampMilliseconds;

            Assert.IsTrue(
                durationMs >= Math.Max(videoEndMs, audioEndMs),
                $"Duration {durationMs}ms does not cover the audio and video end time.");
        }

        [TestMethod]
        public void WriteData_RejectsFrameTableThatExceedsFrameData()
        {
            var webM = CreateWebM();
            webM.FrameTable[1].Size = 10;

            Assert.ThrowsException<InvalidDataException>(
                () => webM.WriteData());
        }

        [TestMethod]
        public void WriteData_RejectsIncompleteVorbisTrack()
        {
            var webM = CreateWebM();
            webM.VorbisCodecPrivate = [1, 2, 3];
            webM.VorbisChannels = 1;
            webM.VorbisSampleRate = 44100;

            Assert.ThrowsException<InvalidDataException>(
                () => webM.WriteData());
        }

        private static WebMFile CreateWebMWithLongAudioTail()
        {
            var webM = CreateWebM();
            webM.VorbisCodecPrivate = [1, 2, 3];
            webM.VorbisSampleRate = 48000;
            webM.VorbisChannels = 2;
            webM.VorbisAudioPackets =
            [
                new VorbisAudioPacket
                {
                    Data = [10],
                    TimestampMilliseconds = 0,
                },
                new VorbisAudioPacket
                {
                    Data = [11],
                    TimestampMilliseconds = 32768,
                },
                new VorbisAudioPacket
                {
                    Data = [12],
                    TimestampMilliseconds = 40000,
                },
                new VorbisAudioPacket
                {
                    Data = [13],
                    TimestampMilliseconds = 41000,
                },
            ];
            return webM;
        }

        private static WebMFile CreateWebM()
        {
            return new WebMFile
            {
                Width = 16,
                Height = 8,
                Framerate = 25,
                FrameTable =
                [
                    new FrameTableRecord
                    {
                        Offset = 0,
                        Size = 3,
                        IsKeyFrame = true,
                    },
                    new FrameTableRecord
                    {
                        Offset = 3,
                        Size = 2,
                        IsKeyFrame = false,
                    },
                ],
                FrameData = [1, 2, 3, 4, 5],
            };
        }

        private static List<long> ReadAudioBlockTimestamps(byte[] data)
        {
            var timestamps = new List<long>();
            var clusterId = new byte[] { 0x1F, 0x43, 0xB6, 0x75 };
            var searchOffset = 0;
            while (true)
            {
                var clusterOffset =
                    FindSequence(data, clusterId, searchOffset);
                if (clusterOffset < 0)
                    break;

                var offset = clusterOffset + clusterId.Length;
                var clusterSize = checked((int)ReadVint(data, ref offset));
                var clusterEnd = checked(offset + clusterSize);
                long? clusterTimestamp = null;

                while (offset < clusterEnd)
                {
                    var elementId = ReadElementId(data, ref offset);
                    var elementSize =
                        checked((int)ReadVint(data, ref offset));
                    var elementEnd = checked(offset + elementSize);
                    if (elementEnd > clusterEnd)
                        throw new InvalidDataException(
                            "Cluster element exceeds its declared size.");

                    if (elementId == 0xE7)
                    {
                        clusterTimestamp =
                            checked((long)ReadUnsigned(
                                data,
                                offset,
                                elementSize));
                    }
                    else if (elementId == 0xA3 &&
                             elementSize >= 4 &&
                             data[offset] == 0x82)
                    {
                        if (!clusterTimestamp.HasValue)
                        {
                            throw new InvalidDataException(
                                "SimpleBlock appears before the cluster timestamp.");
                        }

                        var relativeTimestamp =
                            BinaryPrimitives.ReadInt16BigEndian(
                                data.AsSpan(offset + 1, 2));
                        timestamps.Add(
                            clusterTimestamp.Value +
                            relativeTimestamp);
                    }

                    offset = elementEnd;
                }

                searchOffset = clusterEnd;
            }

            return timestamps;
        }

        private static double ReadDurationMilliseconds(byte[] data)
        {
            var durationId = new byte[] { 0x44, 0x89 };
            var durationOffset = FindSequence(data, durationId, 0);
            if (durationOffset < 0)
                throw new InvalidDataException("Duration element was not found.");

            var offset = durationOffset + durationId.Length;
            var durationSize = checked((int)ReadVint(data, ref offset));
            if (durationSize != sizeof(double))
                throw new InvalidDataException("Duration must be a double.");

            var bits = BinaryPrimitives.ReadInt64BigEndian(
                data.AsSpan(offset, durationSize));
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static uint ReadElementId(byte[] data, ref int offset)
        {
            var firstByte = data[offset];
            var marker = 0x80;
            var length = 1;
            while ((firstByte & marker) == 0)
            {
                marker >>= 1;
                length++;
                if (length > 4)
                    throw new InvalidDataException("Invalid EBML element ID.");
            }

            uint value = 0;
            for (var index = 0; index < length; index++)
                value = (value << 8) | data[offset++];
            return value;
        }

        private static ulong ReadVint(byte[] data, ref int offset)
        {
            var firstByte = data[offset++];
            var marker = 0x80;
            var length = 1;
            while ((firstByte & marker) == 0)
            {
                marker >>= 1;
                length++;
                if (length > 8)
                    throw new InvalidDataException("Invalid EBML VINT.");
            }

            ulong value = (uint)(firstByte & (marker - 1));
            for (var index = 1; index < length; index++)
                value = (value << 8) | data[offset++];
            return value;
        }

        private static ulong ReadUnsigned(
            byte[] data,
            int offset,
            int length)
        {
            ulong value = 0;
            for (var index = 0; index < length; index++)
                value = (value << 8) | data[offset + index];
            return value;
        }

        private static int FindSequence(
            byte[] data,
            byte[] sequence,
            int startIndex)
        {
            for (var index = startIndex;
                 index <= data.Length - sequence.Length;
                 index++)
            {
                if (data.AsSpan(index, sequence.Length)
                    .SequenceEqual(sequence))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
