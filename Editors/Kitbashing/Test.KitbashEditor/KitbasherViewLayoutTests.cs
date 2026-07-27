using System.IO;
using System.Xml.Linq;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class KitbasherViewLayoutTests
    {
        [Test]
        public void RightPanelSplitter_DeclaresIndependentColumnResizeCursor()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "KitbasherView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var splitter = document
                .Descendants(presentation + "GridSplitter")
                .Single(element =>
                    (string?)element.Attribute("Grid.Column") == "1" &&
                    (string?)element.Attribute("Grid.RowSpan") == "5");

            Assert.Multiple(() =>
            {
                Assert.That(
                    (string?)splitter.Attribute("Cursor"),
                    Is.EqualTo("SizeWE"));
                Assert.That(
                    (string?)splitter.Attribute("ResizeDirection"),
                    Is.EqualTo("Columns"));
                Assert.That(
                    (string?)splitter.Attribute("ResizeBehavior"),
                    Is.EqualTo("PreviousAndNext"));
            });
        }

        private static string GetRepositoryFilePath(params string[] pathParts)
        {
            var directory = new DirectoryInfo(
                TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
                {
                    return Path.Combine(
                        [directory.FullName, .. pathParts]);
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Unable to locate the AssetEditor.CN repository root.");
        }
    }
}
