using Shared.ByteParsing;
using Shared.Core.PackFiles.Utility;

namespace Shared.Core.PackFiles.Models
{
    public interface IDataSource
    {
        long Size { get; }
        byte[] ReadData();
        void CopyTo(Stream destination)
        {
            destination.Write(ReadData());
        }
        byte[] PeekData(int size);
        ByteChunk ReadDataAsChunk();

        CompressionFormat CompressionFormat { get; }
    }


}
