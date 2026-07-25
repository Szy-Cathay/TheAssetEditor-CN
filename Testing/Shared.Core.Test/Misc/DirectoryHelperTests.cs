using Shared.Core.Misc;

namespace Test.Shared.Core.Misc
{
    [TestFixture]
    public class DirectoryHelperTests
    {
        [Test]
        public void ApplicationDirectory_UsesCnEditionRoot()
        {
            var expected = Path.Combine(DirectoryHelper.UserDirectory, "AssetEditor.CN");

            Assert.That(DirectoryHelper.ApplicationDirectory, Is.EqualTo(expected));
        }

        [Test]
        public void UpdateDirectories_UseLocalApplicationDataAndAreSiblings()
        {
            var tempRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AssetEditor.CN",
                "Temp");

            Assert.Multiple(() =>
            {
                Assert.That(DirectoryHelper.UpdateDirectory, Is.EqualTo(Path.Combine(tempRoot, "Update")));
                Assert.That(DirectoryHelper.UpdateBackupRootDirectory, Is.EqualTo(Path.Combine(tempRoot, "UpdateBackups")));
            });
        }
    }
}
