using Shared.Core.Services;

namespace Test.Shared.Core.Services
{
    internal class VersionCheckerTests
    {
        [Test]
        public void NormalizeVersion_PreservesZeroBuildForReleaseComparison()
        {
            var currentVersion = VersionChecker.NormalizeVersion(new Version(1, 1, 0, 0));
            var releaseVersion = VersionChecker.ParseReleaseVersion("v1.1.0");

            Assert.Multiple(() =>
            {
                Assert.That(currentVersion.ToString(), Is.EqualTo("1.1.0"));
                Assert.That(currentVersion, Is.EqualTo(releaseVersion));
            });
        }
    }
}
