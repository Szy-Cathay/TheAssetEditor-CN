using Shared.ByteParsing;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta
{
    public class MetaDataFileParserTests
    {
        [Test]
        public void GenerateBytes_PreservesOpaquePayloadForUnknownMetadata()
        {
            var parser = new MetaDataFileParser(new UnknownMetadataDatabase());
            var original = BuildMetadataFile(
                "CODEX_UNKNOWN_TAG",
                attributeVersion: 77,
                payload: [0x10, 0x20, 0x30, 0x40]);

            var parsed = parser.ParseFile(original);
            var written = parser.GenerateBytes(parsed.Version, parsed);

            Assert.That(written, Is.EqualTo(original));
        }

        static byte[] BuildMetadataFile(string tag, int attributeVersion, byte[] payload)
        {
            var data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(2));
            data.AddRange(BitConverter.GetBytes(1));
            data.AddRange(ByteParsers.String.WriteCaString(tag));
            data.AddRange(BitConverter.GetBytes(attributeVersion));
            data.AddRange(payload);
            return data.ToArray();
        }

        sealed class UnknownMetadataDatabase : IMetaDataDatabase
        {
            public string GetDescription(string metaDataTagName) => string.Empty;

            public string GetDescriptionSafe(string metaDataTagName) => string.Empty;

            public List<string> GetSupportedTypes() => [];

            public List<Type> GetDefinition(string metadataName) => [];
        }
    }
}
