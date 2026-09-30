using System.Reflection.PortableExecutable;
using AssetEditor;
using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace AssetEditorTests;

public class ApplicationConsoleTests
{
    [TestCase(".exe")]
    [TestCase(".dll")]
    public void ApplicationBinary_UsesWindowsGuiSubsystem(string extension)
    {
        var path = Path.ChangeExtension(typeof(App).Assembly.Location, extension);
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);

        NUnitAssert.That(reader.PEHeaders.PEHeader!.Subsystem,
            Is.EqualTo(Subsystem.WindowsGui));
    }

    [Test]
    public void ClearConsoleCommand_WhenOutputIsRedirected_DoesNotThrow()
    {
        NUnitAssert.That(Console.IsOutputRedirected, Is.True,
            "The test runner must capture console output.");

        var packFileService = Mock.Of<IPackFileService>();
        var settings = new ApplicationSettingsService();
        var editorDatabase = new Mock<IEditorDatabase>();
        editorDatabase.Setup(database => database.GetEditorInfos()).Returns([]);
        var viewModel = new MenuBarViewModel(
            packFileService,
            settings,
            editorDatabase.Object,
            Mock.Of<IUiCommandFactory>(),
            new TouchedFilesRecorder(packFileService,
                Mock.Of<IGlobalEventHub>(), settings),
            Mock.Of<IPackFileContainerLoader>(),
            Mock.Of<IFolderProjectOpenService>(),
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IEventHub>());

        NUnitAssert.DoesNotThrow(() => viewModel.ClearConsoleCommand.Execute(null));
    }
}
