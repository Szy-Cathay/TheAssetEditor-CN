using NUnit.Framework;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Services;
using Shared.Ui.Common.OperationProgress;

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
        ["AssetEditor", "Views", "Startup", "StartupPackLoadingWindow.xaml"],
        ["Editors", "Audio", "AudioEditor", "Presentation", "AudioEditorView.xaml"],
        ["Editors", "Audio", "AudioEditor", "Presentation", "NewAudioProject", "NewAudioProjectWindow.xaml"],
        ["Editors", "Audio", "AudioExplorer", "AudioExplorerView.xaml"],
        ["Editors", "Audio", "AudioProjectConverter", "AudioProjectConverterWindow.xaml"],
        ["Editors", "Audio", "DialogueEventMerger", "DialogueEventMergerWindow.xaml"],
        ["Shared", "SharedUI", "BaseDialogs", "PackFileTree", "PackFileBrowserView.xaml"],
    ];
    private static readonly string[][] GitLoadingSurfacePaths =
    [
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectGitPanelView.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectGitRepositoryView.xaml"],
        ["AssetEditor", "Views", "FolderProjectVersionControl", "FolderProjectVersionControlWindow.xaml"],
    ];
    private static readonly string[][] LoadingWindowPaths =
    [
        ["AssetEditor", "Views", "FolderProject", "FolderProjectProgressWindow.xaml"],
        ["AssetEditor", "Views", "Startup", "StartupPackLoadingWindow.xaml"],
        ["Editors", "Audio", "AudioEditor", "Presentation", "NewAudioProject", "NewAudioProjectWindow.xaml"],
        ["Editors", "Audio", "AudioProjectConverter", "AudioProjectConverterWindow.xaml"],
        ["Editors", "Audio", "DialogueEventMerger", "DialogueEventMergerWindow.xaml"],
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
    public void AudioLoadingSurfaces_BindRawDetailAndCounts()
    {
        var solutionRoot = FindSolutionRoot();
        var paths = LoadingSurfacePaths.Where(parts =>
            parts.Contains("AudioEditorView.xaml") ||
            parts.Contains("NewAudioProjectWindow.xaml") ||
            parts.Contains("AudioExplorerView.xaml") ||
            parts.Contains("AudioProjectConverterWindow.xaml") ||
            parts.Contains("DialogueEventMergerWindow.xaml"));
        var missingBindings = new List<string>();

        foreach (var parts in paths)
        {
            var progressViews = XDocument
                .Load(Path.Combine([solutionRoot, .. parts]))
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == nameof(OperationProgressView) &&
                    !element.Attributes().Any(attribute =>
                        attribute.Value.Contains(
                            "OperationProgress.AudioPreview",
                            StringComparison.Ordinal)));
            foreach (var progressView in progressViews)
            {
                var attributes = progressView.Attributes().ToDictionary(
                    attribute => attribute.Name.LocalName,
                    attribute => attribute.Value);
                foreach (var required in new[]
                         {
                             "CurrentDetailText",
                             "ProgressValue",
                             "ProgressMaximum",
                             "IsProgressIndeterminate",
                         })
                {
                    if (!attributes.ContainsKey(required))
                    {
                        missingBindings.Add(
                            $"{string.Join('/', parts)}: {required}");
                    }
                }
            }
        }

        Assert.That(missingBindings, Is.Empty);
    }

    [Test]
    public void BlockingLoadingWindows_UseTheUnifiedWindowChrome()
    {
        var solutionRoot = FindSolutionRoot();
        var mismatches = LoadingWindowPaths
            .Where(parts => !XDocument
                .Load(Path.Combine([solutionRoot, .. parts]))
                .Root!
                .Attributes()
                .Any(attribute =>
                    attribute.Name.LocalName == "Style" &&
                    attribute.Value.Contains(
                        "CustomWindowStyle",
                        StringComparison.Ordinal)))
            .Select(parts => string.Join("/", parts))
            .ToArray();

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public void OperationProgressView_OwnsTheUnifiedLoadingSurface()
    {
        InvokeWithWpfApplication(() =>
        {
            var view = new OperationProgressView();
            var surface = view.Content as Border;

            Assert.Multiple(() =>
            {
                Assert.That(surface, Is.Not.Null);
                Assert.That(surface!.Padding, Is.EqualTo(new Thickness(12)));
                Assert.That(surface.Background, Is.Not.Null);
                Assert.That(surface.BorderBrush, Is.Not.Null);
                Assert.That(surface.BorderThickness, Is.EqualTo(new Thickness(1)));
                Assert.That(surface.CornerRadius.TopLeft, Is.GreaterThan(0));
            });
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

}
