using System.Windows;
using System.Windows.Controls;
using AssetEditor.Views;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiMainShellTests
{
    private static readonly IReadOnlyDictionary<string, Type> ExpectedStyles =
        new Dictionary<string, Type>
        {
            ["AeShell.ActivityBar"] = typeof(ListBox),
            ["AeShell.ActivityItem"] = typeof(ListBoxItem),
            ["AeShell.WorkspaceSidebar"] = typeof(TabControl),
            ["AeShell.EditorTabs"] = typeof(CachedTabControl),
            ["AeShell.EditorTabItem"] = typeof(TabItem),
            ["AeShell.TabCloseButton"] = typeof(Button),
            ["AeShell.SidebarHeader"] = typeof(TextBlock),
            ["AeShell.StatusBar"] = typeof(Border),
            ["AeShell.StatusText"] = typeof(TextBlock),
            ["AeShell.Splitter"] = typeof(GridSplitter),
        };

    [Test]
    public void ShellDictionary_ExposesOnlyKeyedApprovedStyles()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var dictionary = Load(
                    "Themes/DesignSystem/Shell.xaml");

                NUnitAssert.Multiple(() =>
                {
                    foreach (var pair in ExpectedStyles)
                    {
                        var style = dictionary[pair.Key] as Style;
                        NUnitAssert.That(style, Is.Not.Null, pair.Key);
                        NUnitAssert.That(
                            style!.TargetType,
                            Is.EqualTo(pair.Value),
                            pair.Key);
                    }

                    NUnitAssert.That(
                        dictionary.Keys.OfType<Type>(),
                        Is.Empty,
                        "Shell styles must not replace implicit styles.");
                });
            });
    }

    [Test]
    public void MainShellXaml_PreservesBehaviorAndUsesApprovedStructure()
    {
        var root = FindSolutionRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "AssetEditor",
            "Views",
            "MainWindow.xaml"));
        var shell = File.ReadAllText(Path.Combine(
            root,
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Shell.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                mainWindow,
                Does.Contain("Style=\"{StaticResource AeShell.ActivityBar}\""));
            NUnitAssert.That(
                mainWindow.Split(
                    "GitWorkspace.SelectedSidebarTabIndex",
                    StringSplitOptions.None),
                Has.Length.EqualTo(3),
                "Activity bar and workspace content must share the same selection.");
            NUnitAssert.That(
                mainWindow,
                Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(mainWindow, Does.Contain("ToolTip="));
            NUnitAssert.That(mainWindow, Does.Contain("Handler=\"TabItem_Drop\""));
            NUnitAssert.That(mainWindow, Does.Contain("Handler=\"TabItem_MouseMove\""));
            NUnitAssert.That(mainWindow, Does.Contain("Handler=\"TabItem_MouseDown\""));
            NUnitAssert.That(mainWindow, Does.Contain("MouseMiddleClick.Command"));
            NUnitAssert.That(mainWindow, Does.Contain("CloseToolCommand"));
            NUnitAssert.That(mainWindow, Does.Contain("HasUnsavedChanges"));
            NUnitAssert.That(mainWindow, Does.Not.Contain("Foreground=\"Red\""));
            NUnitAssert.That(mainWindow, Does.Not.Contain("搜索文件或命令"));
            NUnitAssert.That(
                mainWindow,
                Does.Not.Contain("<RowDefinition Height=\"28\" />"));
            NUnitAssert.That(
                mainWindow,
                Does.Contain(
                    "<RowDefinition Height=\"{StaticResource AeSize.TabGridLength}\" />"));
            NUnitAssert.That(
                mainWindow,
                Does.Contain("Margin=\"0,24,0,0\""));
            NUnitAssert.That(shell, Does.Contain("PART_ItemsHolder"));
            NUnitAssert.That(shell, Does.Contain("EmptyEditorState"));
        });
    }

    [Test]
    public void MainShellLocalization_ContainsVisibleShellCopy()
    {
        var language = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(language, Does.Contain("MainWindow.Activity.Resources"));
            NUnitAssert.That(language, Does.Contain("MainWindow.Activity.Git"));
            NUnitAssert.That(language, Does.Contain("MainWindow.EmptyEditor.Title"));
            NUnitAssert.That(language, Does.Contain("MainWindow.EmptyEditor.Description"));
        });
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate AssetEditor.CN.sln.");
    }

    private static ResourceDictionary Load(string path) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AssetEditor.CN;component/{path}"),
    };
}
