using Editors.Ipc;

namespace Test.Ipc
{
    public class ExternalPackStartupArgumentParserTests
    {
        [Test]
        public void FindPackPath_ReturnsFirstNormalizedPackOnly()
        {
            var first = @"D:\mods\first.pack";
            var second = @"D:\mods\second.pack";

            var result = ExternalPackStartupArgumentParser.FindPackPath(
                ["-devcfg", "config.json", $"\"{first}\"", second]);

            Assert.That(result, Is.EqualTo(Path.GetFullPath(first)));
        }

        [Test]
        public void FindPackPath_IgnoresNonPackArguments()
        {
            var result = ExternalPackStartupArgumentParser.FindPackPath(
                ["-devcfg", "config.json", @"D:\mods\notes.txt"]);

            Assert.That(result, Is.Null);
        }
    }
}
