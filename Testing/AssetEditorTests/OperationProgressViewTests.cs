using NUnit.Framework;
using Microsoft.Extensions.DependencyInjection;
using AssetEditor.Views.FolderProjectVersionControl;
using Shared.Core.Services;
using Shared.Ui.Common.OperationProgress;
using Shared.Ui.Common.ValueConverters;

using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class OperationProgressViewTests
{
    private static readonly string[][] LoadingSurfacePaths =
    [
        ["AssetEditor", "Views", "FolderProject", "FolderProjectProgressWindow.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectGitPanelView.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectGitRepositoryView.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectVersionControlWindow.xaml"],
        ["AssetEditor", "Views", "MainWindow.xaml"],
        ["Editors", "Audio", "AudioEditor", "Presentation", "AudioEditorView.xaml"],
        ["Editors", "Audio", "AudioEditor", "Presentation", "NewAudioProject", "NewAudioProjectWindow.xaml"],
        ["Editors", "Audio", "AudioExplorer", "AudioExplorerView.xaml"],
        ["Editors", "Audio", "AudioProjectConverter", "AudioProjectConverterWindow.xaml"],
        ["Editors", "Audio", "DialogueEventMerger", "DialogueEventMergerWindow.xaml"],
    ];
    private static readonly string[][] GitLoadingSurfacePaths =
    [
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectGitPanelView.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectGitRepositoryView.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectVersionControlWindow.xaml"],
    ];

    [Test]
    public void Report_UpdatesCurrentProgressAndKeepsDetailHistory()
    {
        InvokeWithWpfApplication(() =>
        {
            var view = new OperationProgressView();
            view.Report(
                new OperationProgressUpdate(
                    "正在读取文件",
                    @"audio\first.wav",
                    1,
                    2));
            view.Report(
                new OperationProgressUpdate(
                    "正在解压文件",
                    @"audio\second.wem",
                    2,
                    2));

            Assert.Multiple(() =>
            {
                Assert.That(view.StatusText, Is.EqualTo("正在解压文件"));
                Assert.That(
                    view.CurrentDetailText,
                    Is.EqualTo(@"audio\second.wem"));
                Assert.That(view.ProgressValue, Is.EqualTo(2));
                Assert.That(view.ProgressMaximum, Is.EqualTo(2));
                Assert.That(view.IsProgressIndeterminate, Is.False);
                Assert.That(
                    view.DetailHistory,
                    Is.EqualTo(
                        new[]
                        {
                            @"audio\first.wav",
                            @"audio\second.wem",
                        }));
            });
        });
    }

    [Test]
    public void Report_WithoutTotal_UsesIndeterminateProgress()
    {
        InvokeWithWpfApplication(() =>
        {
            var view = new OperationProgressView();
            view.Report(
                new OperationProgressUpdate(
                    "正在建立首次本地版本",
                    "Git 正在登记工程文件"));

            Assert.That(view.IsProgressIndeterminate, Is.True);
        });
    }

    [Test]
    public void BoundProgressValues_UpdateSummaryImmediately()
    {
        InvokeWithWpfApplication(() =>
        {
            var view = new OperationProgressView
            {
                IsOperationActive = true,
                ProgressMaximum = 2,
                ProgressValue = 1,
                IsProgressIndeterminate = false,
            };

            Assert.That(view.ProgressSummaryText, Does.StartWith("1 / 2"));
        });
    }

    [Test]
    public void DetailsExpansion_RendersAVisibleHistoryPanel()
    {
        InvokeWithWpfApplication(() =>
        {
            var view = new OperationProgressView { Width = 640 };
            view.Report(
                new OperationProgressUpdate(
                    "正在解压并写入文件",
                    "audio/wwise/english(uk)/first.wem",
                    1,
                    2));
            view.Report(
                new OperationProgressUpdate(
                    "正在解压并写入文件",
                    "audio/wwise/english(uk)/second.wem",
                    2,
                    2));

            var host = new Window
            {
                Content = view,
                Width = 660,
                SizeToContent = SizeToContent.Height,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                host.Show();
                host.UpdateLayout();
                var compactHeight = view.ActualHeight;

                view.IsDetailsExpanded = true;
                host.UpdateLayout();
                var expandedHeight = view.ActualHeight;

                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(view.ActualWidth),
                    (int)Math.Ceiling(expandedHeight),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                bitmap.Render(view);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        expandedHeight,
                        Is.GreaterThan(compactHeight));
                    Assert.That(
                        view.DetailsHeaderText,
                        Is.EqualTo("收起详情"));
                    Assert.That(bitmap.PixelHeight, Is.GreaterThan(100));
                });
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Test]
    public void AllLoadingSurfaces_UseExpandableOperationProgress()
    {
        var solutionRoot = FindSolutionRoot();

        var missing = LoadingSurfacePaths
            .Where(parts => !XDocument
                .Load(Path.Combine([solutionRoot, .. parts]))
                .Descendants()
                .Any(element =>
                    element.Name.LocalName ==
                    nameof(OperationProgressView)))
            .Select(parts => string.Join("/", parts))
            .ToArray();

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void GitLoadingSurfaces_BindRealStageDetailAndCounts()
    {
        var solutionRoot = FindSolutionRoot();
        var missingBindings = new List<string>();

        foreach (var parts in GitLoadingSurfacePaths)
        {
            var progressView = XDocument
                .Load(Path.Combine([solutionRoot, .. parts]))
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == nameof(OperationProgressView));
            var attributes = progressView.Attributes().ToDictionary(
                attribute => attribute.Name.LocalName,
                attribute => attribute.Value);
            foreach (var expected in new[]
                     {
                         "LoadingProgressStatusText",
                         "LoadingProgressDetailText",
                         "LoadingProgressValue",
                         "LoadingProgressMaximum",
                         "LoadingProgressIsIndeterminate",
                     })
            {
                if (!attributes.Values.Any(value => value.Contains(
                        expected,
                        StringComparison.Ordinal)))
                {
                    missingBindings.Add(
                        $"{string.Join('/', parts)}: {expected}");
                }
            }
        }

        Assert.That(missingBindings, Is.Empty);
    }

    [Test]
    public void MergeLoadingSurface_MatchesGitLoadingSurfaceStyle()
    {
        InvokeWithWpfApplication(() =>
        {
            const string converterKey = "BoolToCollapsedConverter";
            var resources = Application.Current.Resources;
            var hadConverter = resources.Contains(converterKey);
            var previousConverter = hadConverter
                ? resources[converterKey]
                : null;
            var previousMainWindow = Application.Current.MainWindow;
            var owner = new Window
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            resources[converterKey] = new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Visible,
                FalseValue = Visibility.Collapsed,
            };
            Application.Current.MainWindow = owner;
            owner.Show();
            try
            {
                using var mergeWindow =
                    new FolderProjectVersionControlWindow();
                var repositoryView = new FolderProjectGitRepositoryView();
                var mergeProgress =
                    FindLogicalDescendant<OperationProgressView>(mergeWindow);
                var repositoryProgress =
                    FindLogicalDescendant<OperationProgressView>(
                        repositoryView);
                var mergeSurface = LogicalTreeHelper.GetParent(
                    mergeProgress!) as Border;
                var repositorySurface = LogicalTreeHelper.GetParent(
                    repositoryProgress!) as Border;

                Assert.Multiple(() =>
                {
                    Assert.That(mergeProgress, Is.Not.Null);
                    Assert.That(repositoryProgress, Is.Not.Null);
                    Assert.That(mergeSurface, Is.Not.Null);
                    Assert.That(repositorySurface, Is.Not.Null);
                    Assert.That(
                        mergeSurface!.Background?.ToString(),
                        Is.EqualTo(
                            repositorySurface!.Background?.ToString()));
                    Assert.That(
                        mergeSurface.BorderBrush?.ToString(),
                        Is.EqualTo(
                            repositorySurface.BorderBrush?.ToString()));
                    Assert.That(
                        mergeSurface.BorderThickness,
                        Is.EqualTo(repositorySurface.BorderThickness));
                    Assert.That(
                        mergeSurface.CornerRadius,
                        Is.EqualTo(repositorySurface.CornerRadius));
                });
            }
            finally
            {
                Application.Current.MainWindow = previousMainWindow;
                owner.Close();
                if (hadConverter)
                    resources[converterKey] = previousConverter;
                else
                    resources.Remove(converterKey);
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

    private static void InvokeWithWpfApplication(Action action)
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var services = new ServiceCollection()
            .AddSingleton(localization)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            action);
    }

    private static T? FindLogicalDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper
                     .GetChildren(parent)
                     .OfType<DependencyObject>())
        {
            if (child is T match)
                return match;

            var descendant = FindLogicalDescendant<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }
}
