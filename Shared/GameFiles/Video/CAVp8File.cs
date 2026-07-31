using System.Text;

namespace Shared.GameFormats.Video
{
    public class CAVp8ExtraHeaderData
    {
        public byte UnknownByte { get; set; }
        public uint UnknownUInt32First { get; set; }
        public uint UnknownUInt32Second { get; set; }
    }

    public class CAVp8File
    {
        private const string Signature = "CAMV";
        private const string SupportedCodec = "VP80";
        private const int BaseHeaderLength = 40;
        private const int StandardFrameRecordLength = 9;
        private const int ExtendedFrameRecordLength = 13;

        public ushort Version { get; private set; }
        public string CodecFourCC { get; private set; } = SupportedCodec;
        public ushort Width { get; private set; }
        public ushort Height { get; private set; }
        public uint NumberOfFrames { get; private set; }
        public uint LargestFrameSize { get; private set; }
        public float Framerate { get; private set; }
        public CAVp8ExtraHeaderData? ExtraData { get; private set; }
        public List<FrameTableRecord> FrameTable { get; private set; } = [];
        public byte[] FrameData { get; private set; } = [];

        public CAVp8File(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            ReadData(data);
        }

        private void ReadData(byte[] data)
        {
            if (data.Length < BaseHeaderLength)
                throw new InvalidDataException("CA_VP8 file is shorter than its base header.");

            using var reader = new BinaryReader(
                new MemoryStream(data, writable: false));

            var signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != Signature)
            {
                throw new InvalidDataException(
                    $"CA_VP8 signature mismatch: expected '{Signature}' but got '{signature}'.");
            }

            Version = reader.ReadUInt16();
            var rawHeaderLength = reader.ReadUInt16();
            CodecFourCC = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (CodecFourCC != SupportedCodec)
            {
                throw new InvalidDataException(
                    $"CA_VP8 codec '{CodecFourCC}' is not supported.");
            }

            Width = reader.ReadUInt16();
            Height = reader.ReadUInt16();
            if (Width == 0 || Height == 0)
                throw new InvalidDataException("CA_VP8 dimensions must be positive.");

            var millisecondsPerFrame = reader.ReadSingle();
            if (millisecondsPerFrame <= 0 ||
                !float.IsFinite(millisecondsPerFrame))
            {
                throw new InvalidDataException(
                    "CA_VP8 frame duration must be finite and positive.");
            }

            reader.ReadUInt32();
            var numberOfFramesMinusOne = reader.ReadUInt32();
            var frameTableOffset = reader.ReadUInt32();
            NumberOfFrames = reader.ReadUInt32();
            LargestFrameSize = reader.ReadUInt32();

            if (NumberOfFrames > int.MaxValue)
                throw new InvalidDataException("CA_VP8 frame count is too large.");

            if (numberOfFramesMinusOne == NumberOfFrames)
            {
                EnsureRemaining(reader, 9, "CA_VP8 extended header");
                ExtraData = new CAVp8ExtraHeaderData
                {
                    UnknownByte = reader.ReadByte(),
                    UnknownUInt32First = reader.ReadUInt32(),
                    UnknownUInt32Second = reader.ReadUInt32(),
                };
            }

            var expectedHeaderEnd = checked((long)rawHeaderLength + 8);
            if (reader.BaseStream.Position != expectedHeaderEnd)
            {
                throw new InvalidDataException(
                    $"CA_VP8 header size mismatch: expected {expectedHeaderEnd} bytes but read {reader.BaseStream.Position}.");
            }

            if (frameTableOffset < expectedHeaderEnd ||
                frameTableOffset > data.Length)
            {
                throw new InvalidDataException(
                    "CA_VP8 frame table offset is outside the file.");
            }

            var frameTableLength = data.Length - (long)frameTableOffset;
            var standardLength =
                (long)NumberOfFrames * StandardFrameRecordLength;
            var extendedLength =
                (long)NumberOfFrames * ExtendedFrameRecordLength;
            var recordLength = frameTableLength switch
            {
                var length when length == standardLength =>
                    StandardFrameRecordLength,
                var length when length == extendedLength =>
                    ExtendedFrameRecordLength,
                _ => throw new InvalidDataException(
                    "CA_VP8 frame table length does not match its frame count."),
            };

            reader.BaseStream.Position = frameTableOffset;
            var canonicalOffset = 0u;
            var frameTable = new List<FrameTableRecord>(
                checked((int)NumberOfFrames));
            using var decodedFrames = new MemoryStream();

            for (var frameIndex = 0;
                 frameIndex < NumberOfFrames;
                 frameIndex++)
            {
                var frameOffset = reader.ReadUInt32();
                var frameSize = reader.ReadUInt32();
                if (recordLength == ExtendedFrameRecordLength)
                    reader.ReadUInt32();
                var isKeyFrame = reader.ReadBoolean();

                if (frameSize == 0 || frameSize > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"CA_VP8 frame {frameIndex} has an invalid size.");
                }

                var frameEnd = (ulong)frameOffset + frameSize;
                if (frameOffset < expectedHeaderEnd ||
                    frameEnd > frameTableOffset)
                {
                    throw new InvalidDataException(
                        $"CA_VP8 frame {frameIndex} points outside the frame-data region.");
                }

                var tablePosition = reader.BaseStream.Position;
                reader.BaseStream.Position = frameOffset;
                var frameBytes = reader.ReadBytes(checked((int)frameSize));
                if (frameBytes.Length != frameSize)
                {
                    throw new InvalidDataException(
                        $"CA_VP8 frame {frameIndex} is truncated.");
                }

                decodedFrames.Write(frameBytes);
                reader.BaseStream.Position = tablePosition;

                frameTable.Add(new FrameTableRecord
                {
                    Offset = canonicalOffset,
                    Size = frameSize,
                    IsKeyFrame = isKeyFrame,
                });
                canonicalOffset = checked(canonicalOffset + frameSize);
                if (canonicalOffset > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "CA_VP8 decoded frame data is too large.");
                }
            }

            if (reader.BaseStream.Position != data.Length)
            {
                throw new InvalidDataException(
                    "CA_VP8 frame table contains trailing data.");
            }

            FrameTable = frameTable;
            FrameData = decodedFrames.ToArray();
            Framerate = 1000f / millisecondsPerFrame;
            if (!float.IsFinite(Framerate) || Framerate <= 0)
                throw new InvalidDataException("CA_VP8 framerate is invalid.");
        }

        private static void EnsureRemaining(
            BinaryReader reader,
            int requiredBytes,
            string fieldName)
        {
            if (reader.BaseStream.Length - reader.BaseStream.Position <
                requiredBytes)
            {
                throw new InvalidDataException($"{fieldName} is truncated.");
            }
        }
    }
}
