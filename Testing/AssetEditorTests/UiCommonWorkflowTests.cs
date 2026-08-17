using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommonControls.BaseDialogs;
using CommonControls.BaseDialogs.ErrorListDialog;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.ErrorListDialog;
using Shared.Ui.BaseDialogs.StandardDialog;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiCommonWorkflowTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void WorkflowDictionary_ExposesOnlyKeyedRequiredStyles()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Workflows.xaml");
        NUnitAssert.That(File.Exists(path), Is.True);

        var document = XDocument.Load(path);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var styles = document.Descendants(presentation + "Style").ToArray();
        var keys = styles
            .Select(style => style.Attribute(xaml + "Key")?.Value)
            .ToHashSet();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(styles, Is.Not.Empty);
            NUnitAssert.That(styles, Has.All.Matches<XElement>(style =>
                style.Attribute(xaml + "Key") is not null));
            NUnitAssert.That(keys, Is.SupersetOf(new[]
            {
                "AeWorkflow.SettingsNavigation",
                "AeWorkflow.SettingsNavigationItem",
                "AeWorkflow.DialogFooter",
                "AeWorkflow.FailurePanel",
            }));
        });
    }

    [Test]
    public void SettingsAndStandardDialogs_UseSemanticWorkflowResources()
    {
        var root = FindSolutionRoot();
        var settings = XDocument.Load(Path.Combine(
            root,
            "AssetEditor",
            "Views",
            "Settings",
            "SettingsView.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var categories = settings.Descendants(presentation + "TabControl")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "SettingsCategories");
        var dialogPaths = new[]
        {
            Path.Combine("StandardDialog", "MessageDialogWindow.xaml"),
            Path.Combine("StandardDialog", "Text", "TextInputWindow.xaml"),
            Path.Combine(
                "StandardDialog",
                "Text",
                "TitleDescriptionInputWindow.xaml"),
            Path.Combine(
                "StandardDialog",
                "ErrorDialog",
                "ErrorListWindow.xaml"),
            Path.Combine(
                "StandardDialog",
                "ExceptionHandling",
                "CustomExceptionWindow.xaml"),
            Path.Combine(
                "StandardDialog",
                "PackFile",
                "PackFileBrowserWindow.xaml"),
            Path.Combine(
                "StandardDialog",
                "PackFile",
                "SavePackFileWindow.xaml"),
        };
        var dialogSources = dialogPaths.Select(path => File.ReadAllText(
            Path.Combine(
                root,
                "Shared",
                "SharedUI",
                "BaseDialogs",
                path))).ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                categories.Attribute("Style")?.Value,
                Is.EqualTo(
                    "{StaticResource AeWorkflow.SettingsNavigation}"));
            NUnitAssert.That(
                categories.Descendants(presentation + "TabItem"),
                Has.All.Matches<XElement>(item =>
                    item.Attribute("Style")?.Value ==
                    "{StaticResource AeWorkflow.SettingsNavigationItem}"));
            NUnitAssert.That(
                dialogSources,
                Has.None.Contains("#FF1C1C1C"));
            NUnitAssert.That(
                dialogSources,
                Has.All.Contains("AeBrush."));
            NUnitAssert.That(
                dialogSources.SelectMany(source =>
                    source.Split("AeButton.").Skip(1)),
                Is.Not.Empty);
        });
    }

    [Test]
    public void DialogContent_DoesNotUseDecorativePanelCards()
    {
        var root = FindSolutionRoot();
        var paths = new[]
        {
            Path.Combine(
                "AssetEditor",
                "Views",
                "FolderProject",
                "FolderProjectSetupWindow.xaml"),
            Path.Combine(
                "AssetEditor",
                "Views",
                "ExternalPack",
                "ExternalPackOpenChoiceWindow.xaml"),
            Path.Combine(
                "Shared",
                "SharedUI",
                "BaseDialogs",
                "StandardDialog",
                "ErrorDialog",
                "ErrorListWindow.xaml"),
            Path.Combine(
                "Shared",
                "SharedUI",
                "BaseDialogs",
                "StandardDialog",
                "ExceptionHandling",
                "CustomExceptionWindow.xaml"),
            Path.Combine(
                "Shared",
                "SharedUI",
                "BaseDialogs",
                "StandardDialog",
                "PackFile",
                "SavePackFileWindow.xaml"),
            Path.Combine(
                "Editors",
                "AnimationReTarget",
                "Editors.AnimatioReTarget",
                "Editor",
                "Saving",
                "SaveWindow.xaml"),
        };

        NUnitAssert.That(
            paths.Select(path => File.ReadAllText(Path.Combine(root, path))),
            Has.None.Contains("AeSurface.Panel"));
    }

    [Test]
    public void ErrorListView_RendersStructuredErrorDetails()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var view = new ErrorListView
                {
                    DataContext = new ErrorListViewModel
                    {
                        ErrorItems =
                        [
                            new ErrorListDataItem
                            {
                                ErrorType = "Error",
                                ItemName = "STAND",
                                Description = "Animation file is missing",
                                IsError = true,
                            },
                        ],
                    },
                };
                var window = new Window
                {
                    Width = 720,
                    Height = 320,
                    Content = view,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var list = FindVisualDescendants<ListView>(view).Single();
                    var item = (ListViewItem)list.ItemContainerGenerator
                        .ContainerFromIndex(0);
                    var visibleText = FindVisualDescendants<TextBlock>(item)
                        .Select(text => text.Text)
                        .ToArray();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            FindVisualDescendants<GridViewRowPresenter>(item),
                            Has.Exactly(1).Items);
                        NUnitAssert.That(visibleText, Does.Contain("Error"));
                        NUnitAssert.That(visibleText, Does.Contain("STAND"));
                        NUnitAssert.That(
                            visibleText,
                            Does.Contain("Animation file is missing"));
                        NUnitAssert.That(
                            visibleText,
                            Does.Not.Contain(
                                "Shared.Core.ErrorHandling.ErrorListDataItem"));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void LoadingAndProgressSurfaces_UseSemanticFeedbackStyles()
    {
        var root = FindSolutionRoot();
        var paths = new[]
        {
            Path.Combine(
                root,
                "Shared",
                "SharedUI",
                "Common",
                "OperationProgress",
                "OperationProgressView.xaml"),
            Path.Combine(
                root,
                "AssetEditor",
                "Views",
                "Startup",
                "StartupPackLoadingWindow.xaml"),
            Path.Combine(
                root,
                "AssetEditor",
                "Views",
                "FolderProject",
                "FolderProjectProgressWindow.xaml"),
        };
        var sources = paths.Select(File.ReadAllText).ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sources, Has.All.Contains("AeBrush."));
            NUnitAssert.That(
                sources[0],
                Does.Contain("{StaticResource AeProgress.Bar}"));
            NUnitAssert.That(
                sources[1],
                Does.Contain("{StaticResource AeWorkflow.FailurePanel}"));
        });
    }

    [Test]
    public void StandardDialogs_AssignMainWindowOwnerBeforeShowingModal()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "Shared",
            "SharedUI",
            "BaseDialogs",
            "StandardDialog",
            "StandardDialogs.cs"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("ApplyOwner"));
            NUnitAssert.That(
                source,
                Does.Contain("Application.Current?.MainWindow"));
            NUnitAssert.That(
                source,
                Does.Not.Contain("dialog.ShowDialog()"));
        });
    }

    [Test]
    public void CommonDialogs_InheritTheLoadedMainWindowOwner()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var previousMainWindow = Application.Current.MainWindow;
            var owner = new Window
            {
                Width = 640,
                Height = 480,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                Application.Current.MainWindow = owner;
                owner.Show();
                using var message = new MessageDialogWindow(
                    "确认",
                    "是否继续？",
                    MessageDialogButtonSet.YesNo);
                var text = new TextInputWindow("输入", "测试");
                var description = new TitleDescriptionInputWindow(
                    "输入说明",
                    "标题",
                    "说明",
                    "测试",
                    "内容");

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(message.Owner, Is.SameAs(owner));
                    NUnitAssert.That(text.Owner, Is.SameAs(owner));
                    NUnitAssert.That(description.Owner, Is.SameAs(owner));
                });

                text.Close();
                description.Close();
            }
            finally
            {
                Application.Current.MainWindow = previousMainWindow;
                owner.Close();
            }
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
            "Could not locate the solution root.");
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }
}
