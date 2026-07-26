using NUnit.Framework;
using Shared.ByteParsing.Parsers;
using System.Text;

namespace Shared.ByteParsingTest.Parsers
{
    [TestFixture]
    public class StringParserTest
    {
        [Test]
        public void EncodeObject_RoundTrips()
        {
            var parser = new StringParser();
            var input = "hello";
            var bytes = parser.Encode((object)input);

            Assert.That(bytes, Is.Not.Null);

            var ok = parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(value, Is.EqualTo(input));
        }

        [Test]
        public void Utf8Chinese_UsesByteLengthPrefixAndRoundTrips()
        {
            var parser = new StringParser();
            var input = "中文";

            var bytes = parser.WriteCaString(input);

            Assert.That(BitConverter.ToInt16(bytes, 0), Is.EqualTo(6));
            Assert.That(parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(value, Is.EqualTo(input));
            Assert.That(bytesRead, Is.EqualTo(8));
        }

        [Test]
        public void Utf8Emoji_UsesByteLengthPrefixAndRoundTrips()
        {
            var parser = new StringParser();
            var input = "😀";

            var bytes = parser.WriteCaString(input);

            Assert.That(BitConverter.ToInt16(bytes, 0), Is.EqualTo(4));
            Assert.That(parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(value, Is.EqualTo(input));
            Assert.That(bytesRead, Is.EqualTo(6));
        }

        [Test]
        public void OptionalUtf8_UsesByteLengthPrefixAndRoundTrips()
        {
            var parser = new OptionalStringParser();
            var input = "中文";

            var bytes = parser.WriteCaString(input);

            Assert.That(bytes[0], Is.EqualTo(1));
            Assert.That(BitConverter.ToInt16(bytes, 1), Is.EqualTo(6));
            Assert.That(parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(value, Is.EqualTo(input));
            Assert.That(bytesRead, Is.EqualTo(9));
        }

        [Test]
        public void Utf16_UsesCodeUnitLengthPrefixAndRoundTrips()
        {
            var parser = new StringAsciiParser();
            var input = "😀";

            var bytes = parser.WriteCaString(input);

            Assert.That(BitConverter.ToInt16(bytes, 0), Is.EqualTo(2));
            Assert.That(bytes.Length, Is.EqualTo(2 + Encoding.Unicode.GetByteCount(input)));
            Assert.That(parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(value, Is.EqualTo(input));
            Assert.That(bytesRead, Is.EqualTo(bytes.Length));
        }
    }
}
