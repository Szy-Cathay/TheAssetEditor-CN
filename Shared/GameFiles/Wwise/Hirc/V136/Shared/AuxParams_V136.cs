using Shared.ByteParsing;

namespace Shared.GameFormats.Wwise.Hirc.V136.Shared
{
    public class AuxParams_V136
    {
        public byte BitVector { get; set; }
        public uint AuxBus0 { get; set; }
        public uint AuxBus1 { get; set; }
        public uint AuxBus2 { get; set; }
        public uint AuxBus3 { get; set; }
        public uint ReflectionsAuxBus { get; set; }

        public void ReadData(ByteChunk chunk)
        {
            BitVector = chunk.ReadByte();
            if ((BitVector >> 3 & 1) == 1)
            {
                AuxBus0 = chunk.ReadUInt32();
                AuxBus1 = chunk.ReadUInt32();
                AuxBus2 = chunk.ReadUInt32();
                AuxBus3 = chunk.ReadUInt32();
            }
            ReflectionsAuxBus = chunk.ReadUInt32();
        }

        public byte[] WriteData()
        {
            using var memStream = new MemoryStream();
            memStream.Write(ByteParsers.Byte.EncodeValue(BitVector, out _));
            if ((BitVector & 8) != 0)
            {
                memStream.Write(ByteParsers.UInt32.EncodeValue(AuxBus0, out _));
                memStream.Write(ByteParsers.UInt32.EncodeValue(AuxBus1, out _));
                memStream.Write(ByteParsers.UInt32.EncodeValue(AuxBus2, out _));
                memStream.Write(ByteParsers.UInt32.EncodeValue(AuxBus3, out _));
            }
            memStream.Write(ByteParsers.UInt32.EncodeValue(ReflectionsAuxBus, out _));
            return memStream.ToArray();
        }

        public uint GetSize()
        {
            var bitVectorSize = ByteHelper.GetPropertyTypeSize(BitVector);
            var auxBusSize = (BitVector & 8) != 0
                ? 4u * ByteHelper.GetPropertyTypeSize(AuxBus0)
                : 0u;
            var reflectionsAuxBusSize = ByteHelper.GetPropertyTypeSize(ReflectionsAuxBus);
            return bitVectorSize + auxBusSize + reflectionsAuxBusSize;
        }
    }
}
