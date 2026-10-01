using Shared.Core.Misc;
using Shared.ByteParsing;

namespace Shared.GameFormats.AnimationPack.AnimPackFileTypes
{
    public class CampaignAnimationPackFile : IAnimationPackFile
    {
        public AnimationPackFileDatabase Parent { get; set; }
        public string FileName { get; set; }
        public bool IsUnknownFile { get; set; }
        public NotifyAttr<bool> IsChanged { get; set; } = new(false);
        public CampaignAnimationBin Data { get; private set; }

        public CampaignAnimationPackFile(string fileName, byte[] bytes)
        {
            FileName = fileName;
            CreateFromBytes(bytes);
        }

        public void CreateFromBytes(byte[] bytes) => Data = CampaignAnimationBinLoader.Load(new ByteChunk(bytes));

        public byte[] ToByteArray() => CampaignAnimationBinLoader.Write(Data, Path.GetFileNameWithoutExtension(FileName.Replace('/', Path.DirectorySeparatorChar)));
    }
}
