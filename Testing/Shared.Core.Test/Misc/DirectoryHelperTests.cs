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
        public void UpdateDirectory_IsUnderCnEditionRoot()
        {
            var expected = Path.Combine(
                DirectoryHelper.UserDirectory,
                "AssetEditor.CN",
                "Temp",
                "Update");

            Assert.That(DirectoryHelper.UpdateDirectory, Is.EqualTo(expected));
        }
    }
}
