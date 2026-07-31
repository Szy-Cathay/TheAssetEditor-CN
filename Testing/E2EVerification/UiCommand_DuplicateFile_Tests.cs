using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification
{
    internal class UiCommand_DuplicateFile_Tests
    {
        [Test]
        [TestCase("testFile", "testFile_copy")]            // No extension
        [TestCase("testFile.anm", "testFile_copy.anm")]        // Single extension 
        [TestCase("testFile.anm.meta", "testFile_copy.anm.meta")]   // Double extension 
        public void DuplicateFileCommand(string fileName, string result)
        {
            var runner = new AssetEditorTestRunner();
            runner.PackFileService.EnforceGameFilesMustBeLoaded = false;
            var sourcePackFile = runner.CreateEmptyPackFile("SourcePack", false);
            var outputPackFile = runner.CreateEmptyPackFile("OutputPack", true);

            var fileEntry = new NewPackFileEntry("Animation\\Meta", PackFile.CreateFromASCII(fileName, "DummyContent"));
            runner.PackFileService.AddFilesToPack(sourcePackFile, [fileEntry]);
            var fileToCopy = runner.PackFileService.FindFile("Animation\\Meta\\" + fileName, sourcePackFile);

            runner.CommandFactory.Create<DuplicateFileCommand>().Execute(fileToCopy);
            var foundFile = runner.PackFileService.FindFile("Animation\\Meta\\" + result, outputPackFile);
            Assert.That(foundFile, Is.Not.Null);
        }

        [Test]
        public void DuplicateFileCommand_FolderProjectExistingCopy_CreatesNumberedCopy()
        {
            var projectRoot = Path.Combine(
                Path.GetTempPath(),
                $"ae-duplicate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(projectRoot);
            File.WriteAllText(
                Path.Combine(projectRoot, "foo.ext"),
                "source");
            File.WriteAllText(
                Path.Combine(projectRoot, "foo_copy.ext"),
                "existing");

            var runner = new AssetEditorTestRunner();
            runner.PackFileService.EnforceGameFilesMustBeLoaded = false;
            var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            runner.PackFileService.AddContainer(project, true);

            try
            {
                var source = runner.PackFileService.FindFile(
                    "foo.ext",
                    project);
                Assert.That(source, Is.Not.Null);

                new DuplicateFileCommand(
                    runner.PackFileService).Execute(source!);

                Assert.Multiple(
                    () =>
                    {
                        Assert.That(
                            File.ReadAllText(
                                Path.Combine(
                                    projectRoot,
                                    "foo_copy.ext")),
                            Is.EqualTo("existing"));
                        Assert.That(
                            File.ReadAllText(
                                Path.Combine(
                                    projectRoot,
                                    "foo_copy_2.ext")),
                            Is.EqualTo("source"));
                    });
            }
            finally
            {
                runner.PackFileService.UnloadPackContainer(project);
                Directory.Delete(projectRoot, true);
            }
        }
    }
}
