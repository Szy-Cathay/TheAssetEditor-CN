using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.Editors.TextEditor;

namespace AssetEditorTests;

[NonParallelizable]
public class FolderProjectHistoryFileOperationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RestoreFile_ReopensOnlyAffectedEditorWithRestoredContent(bool unsaved)
    {
        using var harness = new Harness();
        var affected = harness.OpenEditor("example.txt");
        var other = harness.OpenEditor("other.txt");
        if (unsaved)
            affected.Text = "unsaved memory";
        other.Text = "keep other unsaved memory";
        await harness.SelectInitialFile();

        await harness.ViewModel.RestoreFileCommand.ExecuteAsync(null);

        var reopened = harness.Manager.GetAllEditors()
            .OfType<TextEditorViewModel<DefaultTextConverter>>()
            .Single(editor => editor.CurrentFile.Name == "example.txt");
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(reopened, Is.Not.SameAs(affected));
            NUnitAssert.That(reopened.Text, Is.EqualTo("version one"));
            NUnitAssert.That(reopened.HasUnsavedChanges, Is.False);
            NUnitAssert.That(harness.Manager.GetAllEditors(), Does.Contain(other));
            NUnitAssert.That(other.Text, Is.EqualTo("keep other unsaved memory"));
            NUnitAssert.That(other.HasUnsavedChanges, Is.True);
            NUnitAssert.That(harness.EditorPrompts, Is.EqualTo(unsaved ? 1 : 0));
        });
        reopened.Save();
        NUnitAssert.That(harness.Read("example.txt"), Is.EqualTo("version one"));
    }

    [Test]
    public async Task RestoreFile_CancelUnsavedEditor_KeepsDiskEditorAndHistory()
    {
        using var harness = new Harness { EditorAnswer = MessageBoxResult.Cancel };
        var editor = harness.OpenEditor("example.txt");
        editor.Text = "unsaved memory";
        await harness.SelectInitialFile();
        var before = harness.History.GetRestorePoints(harness.Root).Select(point => point.Id).ToArray();

        await harness.ViewModel.RestoreFileCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(harness.Read("example.txt"), Is.EqualTo("version two"));
            NUnitAssert.That(harness.Manager.GetAllEditors(), Does.Contain(editor));
            NUnitAssert.That(editor.Text, Is.EqualTo("unsaved memory"));
            NUnitAssert.That(harness.History.GetRestorePoints(harness.Root).Select(point => point.Id), Is.EqualTo(before));
        });
        harness.Dialogs.Verify(dialogs => dialogs.ShowExceptionWindow(
            It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void FileReload_BlocksAffectedFileUntilDisposedAndKeepsOtherFilesAvailable()
    {
        using var harness = new Harness();
        harness.OpenEditor("example.txt");
        var reload = harness.Manager.PrepareFileReload(harness.Project, ["example.txt"]);
        try
        {
            NUnitAssert.That(harness.Manager.CreateFromFile(harness.Project.FileList["example.txt"], EditorEnums.XML_Editor), Is.Null);
            NUnitAssert.That(harness.OpenEditor("other.txt"), Is.Not.Null);
            reload.Reload();
            NUnitAssert.That(harness.Manager.CreateFromFile(harness.Project.FileList["example.txt"], EditorEnums.XML_Editor), Is.Null);
        }
        finally
        {
            reload.Dispose();
        }
        NUnitAssert.That(harness.OpenEditor("example.txt"), Is.Not.Null);
        NUnitAssert.That(harness.Manager.GetAllEditors(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RestoreFile_EditorReloadFailure_RollsBackDiskAndReopensOriginalVersion()
    {
        using var harness = new Harness();
        harness.OpenEditor("example.txt");
        await harness.SelectInitialFile();
        var before = harness.History.GetRestorePoints(harness.Root).Select(point => point.Id).ToArray();
        harness.FailNextEditorOpen = true;

        await harness.ViewModel.RestoreFileCommand.ExecuteAsync(null);

        var editor = harness.Manager.GetAllEditors().OfType<TextEditorViewModel<DefaultTextConverter>>().Single();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(harness.Read("example.txt"), Is.EqualTo("version two"));
            NUnitAssert.That(editor.Text, Is.EqualTo("version two"));
            NUnitAssert.That(harness.History.GetRestorePoints(harness.Root).Select(point => point.Id), Is.EqualTo(before));
        });
        harness.Dialogs.Verify(dialogs => dialogs.ShowExceptionWindow(
            It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task DiscardNewFile_RemovesTreeNodeAndClosesAffectedEditor()
    {
        using var harness = new Harness();
        harness.Save.Save("new.txt", Encoding.ASCII.GetBytes("new file"), false);
        var editor = harness.OpenEditor("new.txt");
        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);
        harness.ViewModel.SelectedUnrecordedChange = harness.ViewModel.UnrecordedChanges.Single(change => change.Path == "new.txt");
        NUnitAssert.That(harness.Browser.Files.Single().Children.Any(node => node.Name == "new.txt"), Is.True);

        await harness.ViewModel.DiscardSelectedCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(File.Exists(Path.Combine(harness.Root, "new.txt")), Is.False);
            NUnitAssert.That(harness.Project.FileList.ContainsKey("new.txt"), Is.False);
            NUnitAssert.That(harness.Browser.Files.Single().Children.Any(node => node.Name == "new.txt"), Is.False);
            NUnitAssert.That(harness.Manager.GetAllEditors(), Does.Not.Contain(editor));
        });
    }

    private sealed class Harness : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ae-history-file-operations-{Guid.NewGuid():N}");
        public FolderProjectContainer Project { get; }
        public FolderProjectHistoryService History { get; }
        public FolderProjectRestorePoint Initial { get; }
        public EditorManager Manager { get; }
        public FileSaveService Save { get; }
        public FolderProjectHistoryViewModel ViewModel { get; }
        public PackFileBrowserViewModel Browser { get; }
        public Mock<IStandardDialogs> Dialogs { get; } = new();
        public MessageBoxResult EditorAnswer { get; set; } = MessageBoxResult.OK;
        public int EditorPrompts { get; private set; }
        public bool FailNextEditorOpen { get; set; }

        public Harness()
        {
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "example.txt"), "version one");
            File.WriteAllText(Path.Combine(Root, "other.txt"), "other content");
            Project = FolderProjectContainer.Create(Root, new FolderProjectSettings { Name = "历史操作测试" });
            History = new FolderProjectHistoryService(localization);
            Initial = History.Initialize(Root);
            File.WriteAllText(Path.Combine(Root, "example.txt"), "version two");
            Project.RefreshFromDisk();
            History.CreateRestorePoint(Root, "第二个还原点");
            var events = new TestEventHub();
            var packs = new PackFileService(events) { EnforceGameFilesMustBeLoaded = false };
            packs.AddEditableFolderProject(Project);
            Dialogs.Setup(dialogs => dialogs.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);
            Save = new FileSaveService(packs, Dialogs.Object);
            var database = new Mock<IEditorDatabase>();
            database.Setup(item => item.GetEditorInfos()).Returns([
                new EditorInfo(EditorEnums.XML_Editor, typeof(object), typeof(TextEditorViewModel<DefaultTextConverter>)),
            ]);
            database.Setup(item => item.Create(It.IsAny<string>(), It.IsAny<EditorEnums?>()))
                .Returns(() =>
                {
                    if (FailNextEditorOpen)
                    {
                        FailNextEditorOpen = false;
                        throw new IOException("The restored editor could not be loaded.");
                    }
                    return new TextEditorViewModel<DefaultTextConverter>(Save, packs, new DefaultTextConverter());
                });
            Manager = new EditorManager(events, packs, database.Object, (_, _, _) =>
            {
                EditorPrompts++;
                return EditorAnswer;
            });
            var coordinator = new FolderProjectGitOperationCoordinator(packs, Mock.Of<IFolderProjectFactory>(), History);
            ViewModel = new FolderProjectHistoryViewModel(History,
                new FolderProjectUnsavedChangesService(packs, Manager),
                Mock.Of<IFolderProjectUnsavedChangesPrompt>(), coordinator, Dialogs.Object, localization, events);
            ViewModel.OpenProject(Project);
            var menus = new Mock<IContextMenuBuilder>();
            menus.Setup(item => item.Build(It.IsAny<TreeNode>())).Returns(new ObservableCollection<ContextMenuItem2>());
            Browser = new PackFileBrowserViewModel(new ApplicationSettingsService(GameTypeEnum.Warhammer3), menus.Object, packs, events, true, false);
        }

        public TextEditorViewModel<DefaultTextConverter> OpenEditor(string path) =>
            (TextEditorViewModel<DefaultTextConverter>)Manager.CreateFromFile(Project.FileList[path], EditorEnums.XML_Editor);

        public string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

        public async Task SelectInitialFile()
        {
            await ViewModel.RefreshCommand.ExecuteAsync(null);
            ViewModel.SelectedRestorePoint = ViewModel.RestorePoints.Single(point => point.Id == Initial.Id);
            await ViewModel.SelectedChangesLoadTask;
            ViewModel.SelectedRestorePointChange = ViewModel.SelectedRestorePointChanges.Single(change => change.Path == "example.txt");
        }

        public void Dispose()
        {
            Browser.Dispose();
            Project.Dispose();
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
