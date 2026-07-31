using System.Text;

namespace Shared.GameFormats.Video
{
    public class IvfFile
    {
        private const string Signature = "DKIF";
        private const ushort Version = 0;
        private const ushort HeaderLength = 32;

        private readonly CAVp8File _caVp8File;

        public IvfFile(CAVp8File caVp8File)
        {
            _caVp8File = caVp8File ??
                throw new ArgumentNullException(nameof(caVp8File));
        }

        public byte[] WriteData()
        {
            Validate();
            var (rate, scale) = ConvertFramerateToRational(
                _caVp8File.Framerate);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes(Signature));
            writer.Write(Version);
            writer.Write(HeaderLength);
            writer.Write(
                Encoding.ASCII.GetBytes(_caVp8File.CodecFourCC));
            writer.Write(_caVp8File.Width);
            writer.Write(_caVp8File.Height);
            writer.Write(rate);
            writer.Write(scale);
            writer.Write(_caVp8File.NumberOfFrames);
            writer.Write(0u);

            var frameDataOffset = 0;
            for (var frameIndex = 0;
                 frameIndex < _caVp8File.FrameTable.Count;
                 frameIndex++)
            {
                var frame = _caVp8File.FrameTable[frameIndex];
                var frameSize = checked((int)frame.Size);

                writer.Write(frame.Size);
                writer.Write((ulong)frameIndex);
                writer.Write(
                    _caVp8File.FrameData,
                    frameDataOffset,
                    frameSize);
                frameDataOffset += frameSize;
            }

            return stream.ToArray();
        }

        private void Validate()
        {
            if (_caVp8File.CodecFourCC != "VP80")
                throw new InvalidDataException("IVF export requires VP8 data.");
            if (_caVp8File.Width == 0 || _caVp8File.Height == 0)
                throw new InvalidDataException("IVF dimensions must be positive.");
            if (_caVp8File.Framerate <= 0 ||
                !float.IsFinite(_caVp8File.Framerate))
            {
                throw new InvalidDataException(
                    "IVF framerate must be finite and positive.");
            }

            if (_caVp8File.NumberOfFrames !=
                _caVp8File.FrameTable.Count)
            {
                throw new InvalidDataException(
                    "IVF frame count does not match the frame table.");
            }

            long totalFrameDataLength = 0;
            for (var frameIndex = 0;
                 frameIndex < _caVp8File.FrameTable.Count;
                 frameIndex++)
            {
                var frame = _caVp8File.FrameTable[frameIndex];
                if (frame.Size == 0 || frame.Size > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"IVF frame {frameIndex} has an invalid size.");
                }

                totalFrameDataLength = checked(
                    totalFrameDataLength + frame.Size);
                if (totalFrameDataLength > _caVp8File.FrameData.Length)
                {
                    throw new InvalidDataException(
                        $"IVF frame {frameIndex} exceeds the available frame data.");
                }
            }

            if (totalFrameDataLength != _caVp8File.FrameData.Length)
            {
                throw new InvalidDataException(
                    "IVF frame table does not consume all frame data.");
            }
        }

        private static (uint Rate, uint Scale)
            ConvertFramerateToRational(float framerate)
        {
            const uint precision = 1_000_000;
            var numerator = checked(
                (uint)Math.Round(
                    framerate * precision,
                    MidpointRounding.AwayFromZero));
            var denominator = precision;
            var divisor = GreatestCommonDivisor(
                numerator,
                denominator);
            return (numerator / divisor, denominator / divisor);
        }

        private static uint GreatestCommonDivisor(uint left, uint right)
        {
            while (right != 0)
            {
                var remainder = left % right;
                left = right;
                right = remainder;
            }

            return left;
        }
    }
}
