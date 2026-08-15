using Shared.ByteParsing;
using Shared.Core.PackFiles.Utility;

namespace Shared.Core.PackFiles.Models
{

    // This should only be used for unit tests - move to test project later
    public class FileSystemSource : IDataSource
    {
        public long Size { get; private set; } = 0;

        protected string _filepath;

        public FileSystemSource(string filepath)
            : this(filepath, new FileInfo(filepath).Length)
        {
        }

        internal FileSystemSource(string filepath, long size)
        {
            if (size < 0 || size > uint.MaxValue)
                throw new InvalidOperationException($"This file's size ({size:N0}) is too large. The maximum file size {uint.MaxValue:N0}.");

            Size = (uint)size;
            _filepath = filepath;
        }
        

        public byte[] ReadData() => File.ReadAllBytes(_filepath);

        public void CopyTo(Stream destination)
        {
            using var source = new FileStream(
                _filepath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);
            source.CopyTo(destination);
        }

        public byte[] PeekData(int size)
        {
            using (var reader = new BinaryReader(new FileStream(_filepath, FileMode.Open)))
            {
                var output = new byte[size];
                reader.Read(output, 0, size);
                return output;
            }
        }

        public ByteChunk ReadDataAsChunk() => new ByteChunk(ReadData());

        public CompressionFormat CompressionFormat { get => CompressionFormat.None; }
    }


}
