using NUnit.Framework;
using Microsoft.Extensions.DependencyInjection;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Presentation.Settings;
using Editors.Audio.DialogueEventMerger;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.Generators;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

using System.Diagnostics;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moq;

using AudioFile = Editors.Audio.Shared.AudioProject.Models.AudioFile;

using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class OperationProgressViewTests
{
    private static readonly (string[] Path, int HostCount)[] IndependentProgressHostSurfaces =
    [
        (["AssetEditor", "Views", "FolderProjectHistory", "FolderProjectHistoryView.xaml"], 1),
        (["AssetEditor", "Views", "MainWindow.xaml"], 1),
        (["Editors", "AnimationReTarget", "Editors.AnimatioReTarget", "Editor", "Saving", "SaveWindow.xaml"], 1),
        (["Editors", "Audio", "AudioEditor", "Presentation", "NewAudioProject", "NewAudioProjectWindow.xaml"], 1),
        (["Editors", "Audio", "AudioProjectConverter", "AudioProjectConverterWindow.xaml"], 1),
        (["Editors", "Audio", "DialogueEventMerger", "DialogueEventMergerWindow.xaml"], 1),
        (["Editors", "ImportExportEditor", "Editors.ImportExport", "Exporting", "Presentation", "ExportWindow.xaml"], 1),
        (["Editors", "ImportExportEditor", "Editors.ImportExport", "Importing", "Presentation", "ImportWindow.xaml"], 1),
        (["Shared", "SharedUI", "BaseDialogs", "PackFileTree", "PackFileBrowserView.xaml"], 1),
    ];
    private static readonly (string[] Path, int HostCount)[] AudioEditorProgressHostSurfaces =
    [
        (["Editors", "Audio", "AudioEditor", "Presentation", "AudioEditorView.xaml"], 2),
        (["Editors", "Audio", "AudioExplorer", "AudioExplorerView.xaml"], 3),
    ];
    private static readonly string[][] LoadingWindowPaths =
    [
        ["AssetEditor", "Views", "FolderProject", "FolderProjectProgressWindow.xaml"],
        ["AssetEditor", "Views", "Startup", "StartupPackLoadingWindow.xaml"],
        ["Shared", "SharedUI", "Common", "OperationProgress", "OperationProgressWindow.xaml"],
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
    public void SoundBankGeneration_ReportsCreatingBeforeExpensiveWork()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "Audio",
            "Shared",
            "Wwise",
            "Generators",
            "SoundBankGeneratorService.cs"));
        var methodStart = source.IndexOf(
            "public async Task<bool> GenerateMergedDialogueEventSoundBanksAsync",
            StringComparison.Ordinal);
        var methodSource = source[methodStart..];

        Assert.That(
            methodSource.IndexOf(
                "\"AudioOperation.Merge.Creating\"",
                StringComparison.Ordinal),
            Is.LessThan(methodSource.IndexOf(
                "CreateMergedDialogueEventSoundBankOutputs(",
                StringComparison.Ordinal)));
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
            var view = new OperationProgressView
            {
                Width = 640,
                UseDeferredVisibility = false,
            };
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
    public void AllOperationProgressWindowHosts_AreInventoried()
    {
        var solutionRoot = FindSolutionRoot();
        var expectedPaths = IndependentProgressHostSurfaces
            .Concat(AudioEditorProgressHostSurfaces)
            .Select(item => string.Join(Path.DirectorySeparatorChar, item.Path))
            .ToArray();
        var actualPaths = new[] { "AssetEditor", "Editors", "Shared" }
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(solutionRoot, root),
                "*.xaml",
                SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Where(path => File.ReadAllText(path)
                .Contains(nameof(OperationProgressWindowHost), StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(solutionRoot, path))
            .ToArray();

        Assert.That(actualPaths, Is.EquivalentTo(expectedPaths));
    }

    [Test]
    public void IndependentProgressSurfaces_UseWindowHosts()
    {
        var solutionRoot = FindSolutionRoot();
        var mismatches = IndependentProgressHostSurfaces
            .Concat(AudioEditorProgressHostSurfaces)
            .Select(item =>
            {
                var document = XDocument.Load(Path.Combine(
                    [solutionRoot, .. item.Path]));
                var hosts = document.Descendants().Count(element =>
                    element.Name.LocalName ==
                    nameof(OperationProgressWindowHost));
                var embeddedViews = document.Descendants().Count(element =>
                    element.Name.LocalName ==
                    nameof(OperationProgressView));
                var untitledHosts = document.Descendants().Count(element =>
                    element.Name.LocalName ==
                        nameof(OperationProgressWindowHost) &&
                    element.Attribute("WindowTitle") is null);
                return hosts == item.HostCount && embeddedViews == 0 &&
                       untitledHosts == 0
                    ? null
                    : $"{string.Join('/', item.Path)}: " +
                      $"hosts={hosts}, embedded={embeddedViews}, " +
                      $"untitled={untitledHosts}";
            })
            .Where(message => message is not null)
            .ToArray();

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public void AudioEditorLoadingSurfaces_UseIndependentWindowHosts()
    {
        var solutionRoot = FindSolutionRoot();
        var mismatches = AudioEditorProgressHostSurfaces
            .Select(item =>
            {
                var document = XDocument.Load(Path.Combine(
                    [solutionRoot, .. item.Path]));
                var hosts = document.Descendants().Count(element =>
                    element.Name.LocalName ==
                    nameof(OperationProgressWindowHost));
                var embeddedViews = document.Descendants().Count(element =>
                    element.Name.LocalName ==
                    nameof(OperationProgressView));
                return hosts == item.HostCount && embeddedViews == 0
                    ? null
                    : $"{string.Join('/', item.Path)}: " +
                      $"hosts={hosts}, embedded={embeddedViews}";
            })
            .Where(message => message is not null)
            .ToArray();

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public void OnlyApprovedSurfaces_ContainOperationProgressViews()
    {
        var solutionRoot = FindSolutionRoot();
        var allowed = LoadingWindowPaths
            .Select(parts => Path.GetFullPath(Path.Combine(
                [solutionRoot, .. parts])))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productRoots = new[]
        {
            "AssetEditor",
            "Editors",
            "Shared",
            "GameWorld",
        };
        var offenders = productRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(solutionRoot, root),
                "*.xaml",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => XDocument.Load(path).Descendants().Any(element =>
                element.Name.LocalName == nameof(OperationProgressView)))
            .Where(path => !allowed.Contains(Path.GetFullPath(path)))
            .Select(path => Path.GetRelativePath(solutionRoot, path))
            .OrderBy(path => path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ProductXaml_UsesProgressBarsOnlyInsideUnifiedProgressView()
    {
        var solutionRoot = FindSolutionRoot();
        var allowed = Path.GetFullPath(Path.Combine(
            solutionRoot,
            "Shared",
            "SharedUI",
            "Common",
            "OperationProgress",
            "OperationProgressView.xaml"));
        var offenders = new[] { "AssetEditor", "Editors", "Shared", "GameWorld" }
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(solutionRoot, root),
                "*.xaml",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => XDocument.Load(path).Descendants().Any(element =>
                element.Name.LocalName == nameof(ProgressBar)))
            .Where(path => !Path.GetFullPath(path).Equals(
                allowed,
                StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(solutionRoot, path))
            .OrderBy(path => path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void DedicatedLoadingWindows_ContainExpandableProgressViews()
    {
        var solutionRoot = FindSolutionRoot();
        var missing = LoadingWindowPaths
            .Where(parts => !XDocument
                .Load(Path.Combine([solutionRoot, .. parts]))
                .Descendants()
                .Any(element => element.Name.LocalName ==
                    nameof(OperationProgressView)))
            .Select(parts => string.Join("/", parts))
            .ToArray();

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void DedicatedLoadingWindows_UseSharedFeedbackTiming()
    {
        var solutionRoot = FindSolutionRoot();
        var paths = new[]
        {
            Path.Combine(
                solutionRoot,
                "AssetEditor",
                "Views",
                "FolderProject",
                "FolderProjectProgressWindow.xaml.cs"),
            Path.Combine(
                solutionRoot,
                "AssetEditor",
                "Views",
                "Startup",
                "StartupPackLoadingWindow.xaml.cs"),
        };
        var missing = paths
            .Where(path => !File.ReadAllText(path).Contains(
                nameof(OperationProgressVisibilityController),
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(solutionRoot, path))
            .ToArray();

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void IndependentProgressHost_DelaysShowAndKeepsVisibleBriefly()
    {
        InvokeWithWpfApplication(() =>
        {
            var host = new OperationProgressWindowHost
            {
                WindowTitle = "正在读取音频",
                StatusText = "正在解析 WEM",
                CurrentDetailText = @"audio\test.wem",
                ProgressValue = 3,
                ProgressMaximum = 8,
                IsProgressIndeterminate = false,
            };
            var owner = new Window
            {
                Content = host,
                Width = 320,
                Height = 180,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                owner.Show();
                host.IsOperationActive = true;
                PumpDispatcher(TimeSpan.FromMilliseconds(150));

                Assert.That(
                    Application.Current.Windows
                        .OfType<OperationProgressWindow>(),
                    Is.Empty,
                    "A short operation displayed a progress window.");

                PumpDispatcher(TimeSpan.FromMilliseconds(450));

                var popup = Application.Current.Windows
                    .OfType<OperationProgressWindow>()
                    .Single();
                popup.UpdateLayout();
                var progress = FindVisualDescendant<OperationProgressView>(
                    popup);

                Assert.Multiple(() =>
                {
                    Assert.That(popup.Owner, Is.SameAs(owner));
                    Assert.That(popup.Title, Is.EqualTo("正在读取音频"));
                    Assert.That(progress, Is.Not.Null);
                    Assert.That(progress!.StatusText, Is.EqualTo("正在解析 WEM"));
                    Assert.That(
                        progress.CurrentDetailText,
                        Is.EqualTo(@"audio\test.wem"));
                    Assert.That(progress.ProgressValue, Is.EqualTo(3));
                    Assert.That(progress.ProgressMaximum, Is.EqualTo(8));
                    Assert.That(progress.IsProgressIndeterminate, Is.False);
                });

                host.IsOperationActive = false;
                PumpDispatcher(TimeSpan.FromMilliseconds(150));
                Assert.That(
                    Application.Current.Windows
                        .OfType<OperationProgressWindow>(),
                    Has.Exactly(1).Items,
                    "The progress window disappeared before its minimum visible duration.");

                PumpDispatcher(TimeSpan.FromMilliseconds(250));
                Assert.That(
                    Application.Current.Windows
                        .OfType<OperationProgressWindow>(),
                    Is.Empty);
            }
            finally
            {
                host.IsOperationActive = false;
                owner.Close();
            }
        });
    }

    [Test]
    public void IndependentProgressHost_CompleteAsync_WaitsUntilWindowCloses()
    {
        InvokeWithWpfApplication(() =>
        {
            var host = new OperationProgressWindowHost
            {
                WindowTitle = "正在读取音频",
                StatusText = "正在解析 WEM",
            };
            var owner = new Window
            {
                Content = host,
                Width = 320,
                Height = 180,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                owner.Show();
                host.IsOperationActive = true;
                PumpDispatcher(TimeSpan.FromMilliseconds(600));

                var completion = host.CompleteAsync();

                Assert.That(completion.IsCompleted, Is.False);
                PumpDispatcher(TimeSpan.FromMilliseconds(350));
                Assert.Multiple(() =>
                {
                    Assert.That(completion.IsCompletedSuccessfully, Is.True);
                    Assert.That(
                        Application.Current.Windows
                            .OfType<OperationProgressWindow>(),
                        Is.Empty);
                });
            }
            finally
            {
                host.IsOperationActive = false;
                owner.Close();
            }
        });
    }

    [Test]
    public async Task DialogueMerger_GenerationFailure_ClosesProgressBeforeErrorDialog()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var repository = new Mock<IAudioRepository>();
        repository
            .Setup(value => value.LoadDialogueEventMergerData(
                "for_merging",
                It.IsAny<IProgress<AudioLoadProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
            [
                @"audio\wwise\chinese\test_for_merging.bnk",
            ]);
        var generation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new Mock<ISoundBankGeneratorService>();
        generator
            .Setup(value => value.GenerateMergedDialogueEventSoundBanksAsync(
                It.IsAny<List<string>>(),
                "ui_probe",
                It.IsAny<CancellationToken>()))
            .Returns(generation.Task);
        var progressWindowsAtError = -1;
        var dialogs = new Mock<IStandardDialogs>();
        dialogs
            .Setup(value => value.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback(() => progressWindowsAtError =
                Application.Current.Windows
                    .OfType<OperationProgressWindow>()
                    .Count());
        var viewModel = new DialogueEventMergerViewModel(
            repository.Object,
            generator.Object,
            dialogs.Object);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SoundBankSuffix = "ui_probe";

        InvokeWithWpfApplication(() =>
        {
            Application.Current.Resources["InvBoolConverter"] =
                new Shared.Ui.Common.ValueConverters
                    .InverseBooleanConverter();
            var window = new DialogueEventMergerWindow
            {
                DataContext = viewModel,
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -10000,
                Top = -10000,
            };
            Task? generationTask = null;
            try
            {
                window.Show();
                generationTask =
                    viewModel.GenerateMergedDialogueEventSoundBankAsync(
                        CancellationToken.None);
                PumpDispatcher(TimeSpan.FromMilliseconds(650));

                Assert.That(
                    Application.Current.Windows
                        .OfType<OperationProgressWindow>(),
                    Has.Exactly(1).Items,
                    "The failure path was not tested with a visible progress window.");

                generation.SetException(
                    new InvalidOperationException(
                        "No editable Pack was selected."));
                PumpDispatcher(TimeSpan.FromMilliseconds(400));

                Assert.Multiple(() =>
                {
                    Assert.That(
                        generationTask.IsCompletedSuccessfully,
                        Is.True);
                    Assert.That(progressWindowsAtError, Is.Zero);
                    Assert.That(viewModel.IsBusy, Is.False);
                });
            }
            finally
            {
                generation.TrySetCanceled();
                foreach (var progressWindow in Application.Current.Windows
                             .OfType<OperationProgressWindow>()
                             .ToArray())
                {
                    progressWindow.Complete();
                }

                window.Close();
            }
        });

        dialogs.Verify(
            value => value.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public void EmbeddedProgress_DelaysShowAndKeepsVisibleBriefly()
    {
        InvokeWithWpfApplication(() =>
        {
            var progress = new OperationProgressView
            {
                StatusText = "正在读取音频",
            };
            var host = new Window
            {
                Content = progress,
                Width = 700,
                Height = 240,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                long shownTimestamp = 0;
                long hiddenTimestamp = 0;
                progress.IsVisibleChanged += (_, eventArgs) =>
                {
                    if (eventArgs.NewValue is true)
                    {
                        shownTimestamp = Stopwatch.GetTimestamp();
                        progress.IsOperationActive = false;
                    }
                    else if (shownTimestamp != 0)
                    {
                        hiddenTimestamp = Stopwatch.GetTimestamp();
                    }
                };

                host.Show();
                var startedTimestamp = Stopwatch.GetTimestamp();
                progress.IsOperationActive = true;
                var completedVisibilityCycle = PumpDispatcherUntil(
                    () => hiddenTimestamp != 0,
                    TimeSpan.FromSeconds(3));

                var timingTolerance = TimeSpan.FromMilliseconds(10);
                Assert.Multiple(() =>
                {
                    Assert.That(completedVisibilityCycle, Is.True);
                    Assert.That(
                        Stopwatch.GetElapsedTime(
                            startedTimestamp,
                            shownTimestamp),
                        Is.GreaterThanOrEqualTo(
                            OperationProgressVisibilityController.ShowDelay));
                    Assert.That(
                        Stopwatch.GetElapsedTime(
                            shownTimestamp,
                            hiddenTimestamp),
                        Is.GreaterThanOrEqualTo(
                            OperationProgressVisibilityController
                                .MinimumVisibleDuration - timingTolerance));
                    Assert.That(
                        progress.Visibility,
                        Is.EqualTo(Visibility.Collapsed));
                });
            }
            finally
            {
                host.Close();
            }
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void IndependentProgressWindow_RendersAcrossThemes(ThemeType theme)
    {
        InvokeWithWpfApplication(() =>
        {
            var previousTheme = ThemesController.CurrentTheme;
            OperationProgressWindow? window = null;
            try
            {
                ThemesController.SetTheme(theme);
                var host = new OperationProgressWindowHost
                {
                    WindowTitle = "正在加载资源",
                    StatusText = "正在读取 Pack",
                    CurrentDetailText = @"variantmeshes\example.rigid_model_v2",
                    IsOperationActive = true,
                    IsProgressIndeterminate = false,
                    ProgressValue = 4,
                    ProgressMaximum = 10,
                };
                window = new OperationProgressWindow(host)
                {
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                };
                window.Show();
                window.UpdateLayout();

                var bitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(window.ActualWidth)),
                    Math.Max(1, (int)Math.Ceiling(window.ActualHeight)),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                bitmap.Render(window);

                Assert.Multiple(() =>
                {
                    Assert.That(window.Title, Is.EqualTo("正在加载资源"));
                    Assert.That(
                        window.Background,
                        Is.EqualTo(Application.Current.FindResource("AeBrush.Canvas")));
                    Assert.That(bitmap.PixelWidth, Is.GreaterThan(600));
                    Assert.That(bitmap.PixelHeight, Is.GreaterThan(150));
                });
            }
            finally
            {
                window?.Complete();
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    [Test]
    public void AudioLoadingSurfaces_BindRawDetailAndCounts()
    {
        var solutionRoot = FindSolutionRoot();
        var paths = IndependentProgressHostSurfaces
            .Select(item => item.Path)
            .Concat(AudioEditorProgressHostSurfaces.Select(item => item.Path))
            .Where(parts =>
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
                    element.Name.LocalName == nameof(OperationProgressWindowHost) ||
                    element.Name.LocalName == nameof(OperationProgressView));
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
    public void AudioProgressPopups_UseTheirCurrentOperationStates()
    {
        var solutionRoot = FindSolutionRoot();
        var converter = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Editors",
            "Audio",
            "AudioProjectConverter",
            "AudioProjectConverterWindow.xaml"));
        var dialogueMerger = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Editors",
            "Audio",
            "DialogueEventMerger",
            "DialogueEventMergerWindow.xaml"));
        var audioEditor = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Editors",
            "Audio",
            "AudioEditor",
            "Presentation",
            "AudioEditorView.xaml"));
        var audioExplorer = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Editors",
            "Audio",
            "AudioExplorer",
            "AudioExplorerView.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(
                converter,
                Does.Contain(
                    "IsOperationActive=\"{Binding IsBusy}\""));
            Assert.That(
                converter,
                Does.Contain(
                    "Binding=\"{Binding IsBusy}\" Value=\"True\""));
            Assert.That(
                dialogueMerger,
                Does.Contain(
                    "IsOperationActive=\"{Binding IsBusy}\""));
            Assert.That(
                dialogueMerger,
                Does.Contain(
                    "Binding=\"{Binding IsBusy}\" Value=\"True\""));
            Assert.That(
                audioEditor,
                Does.Contain(
                    "IsOperationActive=\"{Binding IsLoadProgressWindowActive}\""));
            Assert.That(
                audioExplorer,
                Does.Contain(
                    "IsOperationActive=\"{Binding IsLoadProgressWindowActive}\""));
        });
    }

    [Test]
    public void AudioSettingsFileList_RendersFileNameAndPathColumns()
    {
        InvokeWithWpfApplication(() =>
        {
            var viewModel = new SettingsViewModel(
                new TestEventHub(),
                new AudioEditorStateService(),
                Mock.Of<IAudioRepository>())
            {
                IsSettingsVisible = true,
            };
            viewModel.AudioFiles.Add(new AudioFile(
                Guid.NewGuid(),
                1,
                "cathay_alchemist1_attack.wav",
                @"audio\audio_wav\cathay_alchemist1_attack.wav"));
            var view = new SettingsView
            {
                DataContext = viewModel,
                Width = 900,
                Height = 500,
            };
            var window = new Window
            {
                Content = view,
                Width = 920,
                Height = 520,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var renderedText = FindVisualDescendants<TextBlock>(view)
                    .Select(textBlock => textBlock.Text)
                    .ToArray();

                Assert.Multiple(() =>
                {
                    Assert.That(
                        renderedText,
                        Does.Contain("cathay_alchemist1_attack.wav"));
                    Assert.That(
                        renderedText,
                        Does.Contain(
                            @"audio\audio_wav\cathay_alchemist1_attack.wav"));
                    Assert.That(
                        renderedText,
                        Has.None.EqualTo(typeof(AudioFile).FullName));
                });
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Test]
    public void OperationProgressView_UsesFlatWindowContent()
    {
        InvokeWithWpfApplication(() =>
        {
            var view = new OperationProgressView();
            var content = view.Content as Grid;

            Assert.Multiple(() =>
            {
                Assert.That(content, Is.Not.Null);
                Assert.That(view.Content, Is.Not.TypeOf<Border>());
                Assert.That(
                    content!.Children.OfType<ProgressBar>().Count(),
                    Is.EqualTo(0));
                Assert.That(
                    content.Children.OfType<Grid>()
                        .SelectMany(grid => grid.Children.OfType<ProgressBar>())
                        .Count(),
                    Is.EqualTo(1));
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

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in
                     FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher.CurrentDispatcher)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool PumpDispatcherUntil(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < timeout)
            PumpDispatcher(TimeSpan.FromMilliseconds(10));

        return condition();
    }

}
