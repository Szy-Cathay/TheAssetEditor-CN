using AssetEditor.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;

namespace AssetEditorTests;

public class FolderProjectUnsavedChangesServiceTests
{
    [NUnit.Framework.Test]
    public void SelectedPaths_OnlyMatchUnsavedEditorsFromThatProject()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "target" });
        var firstFile = PackFile.CreateFromBytes("first.txt", [1]);
        var secondFile = PackFile.CreateFromBytes("second.txt", [2]);
        var firstEditor = CreateEditor(firstFile, true);
        var secondEditor = CreateEditor(secondFile, true);
        var editorManager = new Mock<IEditorManager>();
        editorManager.Setup(manager => manager.GetAllEditors())
            .Returns([firstEditor.Object, secondEditor.Object]);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetAllPackfileContainers())
            .Returns([project]);
        packFileService.Setup(service => service.GetPackFileContainer(
                It.IsAny<PackFile>()))
            .Returns(project);
        packFileService.Setup(service => service.GetFullPath(
                firstFile,
                project))
            .Returns("first.txt");
        packFileService.Setup(service => service.GetFullPath(
                secondFile,
                project))
            .Returns("second.txt");
        var service = new FolderProjectUnsavedChangesService(
            packFileService.Object,
            editorManager.Object);

        var hasFirst = service.HasUnsavedChanges(
            project.ProjectRoot,
            ["first.txt"]);
        var saved = service.SaveUnsavedChanges(
            project.ProjectRoot,
            ["first.txt"]);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(hasFirst, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(saved, NUnit.Framework.Is.True);
        });
        firstEditor.As<ISaveableEditor>()
            .Verify(editor => editor.Save(), Times.Once);
        secondEditor.As<ISaveableEditor>()
            .Verify(editor => editor.Save(), Times.Never);
    }

    private static Mock<IEditorInterface> CreateEditor(
        PackFile file,
        bool hasUnsavedChanges)
    {
        var editor = new Mock<IEditorInterface>();
        editor.As<IFileEditor>().SetupGet(item => item.CurrentFile)
            .Returns(file);
        editor.As<ISaveableEditor>()
            .SetupGet(item => item.HasUnsavedChanges)
            .Returns(hasUnsavedChanges);
        editor.As<ISaveableEditor>()
            .Setup(item => item.Save())
            .Returns(true);
        return editor;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-unsaved-service-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
