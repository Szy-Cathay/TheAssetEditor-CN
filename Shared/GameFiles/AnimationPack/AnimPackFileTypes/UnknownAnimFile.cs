using Shared.Core.Misc;

namespace Shared.GameFormats.AnimationPack.AnimPackFileTypes
{
    public class UnknownAnimFile : IAnimationPackFile
    {
        public AnimationPackFileDatabase Parent { get; set; }
        public string FileName { get; set; }
        public bool IsUnknownFile { get; set; } = true;
        public NotifyAttr<bool> IsChanged { get; set; } = new NotifyAttr<bool>(false);

        ReadOnlyMemory<byte> _data;
        public ReadOnlyMemory<byte> RawData => _data;

        public UnknownAnimFile(string fileName, byte[] data)
            : this(fileName, (ReadOnlyMemory<byte>)data)
        { }

        public UnknownAnimFile(string fileName, ReadOnlyMemory<byte> data)
        {
            FileName = fileName;
            _data = data;
        }

        public void CreateFromBytes(byte[] bytes)
        {
            _data = bytes;
        }

        public byte[] ToByteArray()
        {
            return _data.ToArray();
        }
    }
}
