using Test.TestingUtility.TestUtility;

namespace Test.Shared.Core.Misc
{
    [TestFixture]
    public class PathHelperTests
    {
        [TestCase("Debug")]
        [TestCase("Release")]
        public void GetProjectOutputPath_UsesConfigurationFromTestDirectory(string configuration)
        {
            var testDirectory = Path.Combine(
                Path.GetTempPath(),
                "tests",
                "bin",
                configuration,
                "net10.0-windows");

            var path = PathHelper.GetProjectOutputPath(
                "GameWorld\\ContentProject",
                "Content",
                testDirectory);

            Assert.That(
                path,
                Is.EqualTo(Path.Combine(
                    "GameWorld\\ContentProject",
                    "bin",
                    configuration,
                    "net10.0-windows",
                    "Content")));
        }
    }
}
