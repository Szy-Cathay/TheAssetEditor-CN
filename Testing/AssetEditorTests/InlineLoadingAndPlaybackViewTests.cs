using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using AssetEditor.Views;
using AssetEditor.Views.FolderProjectHistory;
using Editors.Audio.AudioExplorer;
using Editors.Shared.Core.Common.AnimationPlayer;
using NUnit.Framework;
using Shared.Core.Misc;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;
using NUnitAssert = NUnit.Framework.Assert;
using SharedPlayerView = AnimationEditor.Common.AnimationPlayer.AnimationPlayerView;
using KitbashPlayerView = KitbasherEditor.Views.EditorViews.AnimationPlayerView;

namespace AssetEditorTests;

[NonParallelizable]
public class InlineLoadingAndPlaybackViewTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void Players_WholeHeaderTogglesExistingBindingAndRowsStayCentered(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var sharedModel = new AnimationPlayerViewModel();
            var kitbashModel = new KitbashPlayerProbe();
            foreach (var view in new UserControl[]
                     {
                         new SharedPlayerView { DataContext = sharedModel },
                         new KitbashPlayerView { DataContext = kitbashModel },
                     })
            {
                var expander = (Expander)view.FindName("AnimationPlayerExpander");
                foreach (var width in new[] { 320d, 760d })
                {
                    Layout(view, width);
                    var header = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
                    NUnitAssert.That(expander.Header, Is.EqualTo("动画播放器"));
                    NUnitAssert.That(AutomationProperties.GetName(expander), Is.EqualTo("动画播放器"));
                    NUnitAssert.That(header.ActualWidth, Is.GreaterThan(width - 32));
                    NUnitAssert.That(header.Focusable, Is.True, "Keep the original switch's keyboard accessibility.");
                    Render(view, $"{theme}-{view.GetType().Namespace}-{width}-collapsed");
                    var hit = VisualTreeHelper.HitTest(header,
                        new Point(header.ActualWidth - 16, header.ActualHeight / 2));
                    NUnitAssert.That(hit, Is.Not.Null, "The blank right side must be clickable.");
                    header.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                    Layout(view, width);
                    NUnitAssert.That(view is SharedPlayerView ? sharedModel.IsEnabled.Value : kitbashModel.IsEnabled, Is.True);

                    var commands = (FrameworkElement)view.FindName("PlaybackCommandRow");
                    var information = (FrameworkElement)view.FindName("PlaybackInformationRow");
                    var timeline = (FrameworkElement)view.FindName("PlaybackTimelineRow");
                    var commandBounds = commands.TransformToAncestor(view).TransformBounds(new Rect(commands.RenderSize));
                    var infoBounds = information.TransformToAncestor(view).TransformBounds(new Rect(information.RenderSize));
                    var timelineBounds = timeline.TransformToAncestor(view).TransformBounds(new Rect(timeline.RenderSize));
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(commandBounds.Left + commandBounds.Width / 2, Is.EqualTo(width / 2).Within(1));
                        NUnitAssert.That(infoBounds.Top, Is.GreaterThanOrEqualTo(commandBounds.Bottom));
                        NUnitAssert.That(timelineBounds.Top, Is.GreaterThanOrEqualTo(infoBounds.Bottom));
                        NUnitAssert.That(timelineBounds.Width, Is.GreaterThan(width - 48));
                    });
                    var slider = Descendants<Slider>(view).Single();
                    NUnitAssert.That(BindingOperations.GetBinding(slider, RangeBase.ValueProperty)?.Mode, Is.EqualTo(BindingMode.TwoWay));
                    if (view is KitbashPlayerView)
                    {
                        var selectors = (Expander)view.FindName("AnimationSelectorsExpander");
                        NUnitAssert.That(selectors.TransformToAncestor(view).Transform(new Point()).Y, Is.GreaterThanOrEqualTo(timelineBounds.Bottom));
                    }
                    Render(view, $"{theme}-{view.GetType().Namespace}-{width}-expanded");
                    header.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                    Layout(view, width);
                    NUnitAssert.That(view is SharedPlayerView ? sharedModel.IsEnabled.Value : kitbashModel.IsEnabled, Is.False);
                    NUnitAssert.That(view.ActualHeight, Is.LessThan(44));
                }
            }
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void LoadingViews_CoverEntireContentAndKeepTabsAndActivityRailOutside(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var initialWindowCount = Application.Current.Windows.Count;
            var probe = new LoadingProbe { IsBusy = true };
            var history = new FolderProjectHistoryView { DataContext = probe };
            var audio = new AudioExplorerView { DataContext = probe };
            var tabs = new CachedTabControl { Style = (Style)audio.FindResource("AeShell.EditorTabs") };
            var editorContent = new FrameworkElementFactory(typeof(ContentPresenter));
            editorContent.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(EditorProbe.View)));
            tabs.ContentTemplateSelector = new FixedTemplateSelector(new DataTemplate { VisualTree = editorContent });
            var editorHeader = new FrameworkElementFactory(typeof(TextBlock));
            editorHeader.SetBinding(TextBlock.TextProperty, new Binding(nameof(EditorProbe.DisplayName)));
            tabs.ItemTemplate = new DataTemplate { VisualTree = editorHeader };
            tabs.Items.Add(new EditorProbe("音频浏览器", audio));
            tabs.SelectedIndex = 0;
            var sidebar = new TabControl { Style = (Style)history.FindResource("AeShell.WorkspaceSidebar") };
            sidebar.Items.Add(new TabItem { Content = history });
            sidebar.SelectedIndex = 0;
            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            var rail = new Border { Background = Brushes.DimGray };
            shell.Children.Add(rail);
            shell.Children.Add(sidebar);
            Grid.SetColumn(sidebar, 1);
            var historyProgress = (OperationProgressView)history.FindName("HistoryLoadingProgress");
            var historyContent = (FrameworkElement)history.FindName("HistoryContent");
            var historyLoadingContent = (FrameworkElement)history.FindName("HistoryLoadingContent");
            var audioContent = (FrameworkElement)audio.FindName("AudioContent");
            var audioLoadingContent = (FrameworkElement)audio.FindName("AudioLoadingContent");
            NUnitAssert.That(audioContent, Is.Not.Null, "All browser controls need one mutually exclusive content root.");
            foreach (var (busy, preview) in new[] { (true, false), (false, false), (false, true), (true, true), (false, false) })
            {
                probe.IsBusy = busy;
                probe.IsAudioPreviewLoading = preview;
                Layout(shell, 420, 650);
                Layout(tabs, 1000, 650);
                var loading = busy || preview;
                var tabStrip = (FrameworkElement)tabs.Template.FindName("EditorTabStrip", tabs);
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(historyLoadingContent.Visibility,
                        Is.EqualTo(busy ? Visibility.Visible : Visibility.Collapsed));
                    NUnitAssert.That(historyContent.Visibility,
                        Is.EqualTo(busy ? Visibility.Collapsed : Visibility.Visible));
                    NUnitAssert.That(audioLoadingContent.Visibility,
                        Is.EqualTo(loading ? Visibility.Visible : Visibility.Collapsed));
                    NUnitAssert.That(audioContent.Visibility,
                        Is.EqualTo(loading ? Visibility.Collapsed : Visibility.Visible));
                    NUnitAssert.That(tabStrip.ActualHeight, Is.GreaterThan(0));
                    NUnitAssert.That(audio.ActualHeight, Is.EqualTo(650 - tabStrip.ActualHeight).Within(1));
                    NUnitAssert.That(history.ActualWidth, Is.EqualTo(380).Within(1));
                    NUnitAssert.That(history.ActualHeight, Is.EqualTo(650).Within(1));
                    NUnitAssert.That(historyProgress.IsOperationActive, Is.EqualTo(busy));
                    NUnitAssert.That(Descendants<OperationProgressView>(audio).Count(progress => IsVisibleWithin(progress, audio)),
                        Is.EqualTo(loading ? 1 : 0), "Resource loading and preview must not show two primary loading surfaces.");
                    NUnitAssert.That(Application.Current.Windows.Count, Is.EqualTo(initialWindowCount));
                });
                if (busy)
                {
                    AssertFillsContent(historyLoadingContent, history);
                    var historyBounds = historyLoadingContent.TransformToAncestor(shell)
                        .TransformBounds(new Rect(historyLoadingContent.RenderSize));
                    NUnitAssert.That(historyBounds.Left, Is.EqualTo(rail.ActualWidth).Within(1));
                    NUnitAssert.That(VisualTreeHelper.HitTest(shell, new Point(20, 325))?.VisualHit, Is.SameAs(rail));
                    Render(shell, $"{theme}-history-loading");
                }
                if (loading)
                {
                    AssertFillsContent(audioLoadingContent, audio);
                    NUnitAssert.That(Descendants<Control>(audioContent).All(control => !IsVisibleWithin(control, audio)), Is.True,
                        "Loading must hide the toolbar, lists, preview and right-hand data together.");
                    Render(tabs, $"{theme}-audio-{(busy ? "loading" : "preview-loading")}");
                }
                else
                {
                    NUnitAssert.That(IsVisibleWithin(Descendants<TreeView>(audio).Single(), audio), Is.True);
                    Render(tabs, $"{theme}-audio-ready");
                }
                foreach (var progress in Descendants<OperationProgressView>(audio).Append(historyProgress))
                {
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(progress.ShowDetails, Is.False);
                        NUnitAssert.That(((Expander)progress.FindName("DetailsExpander")).Visibility, Is.EqualTo(Visibility.Collapsed));
                        NUnitAssert.That(progress.ProgressValue, Is.EqualTo(3));
                        NUnitAssert.That(progress.ProgressMaximum, Is.EqualTo(8));
                        if (progress.IsOperationActive)
                            NUnitAssert.That(progress.ProgressSummaryText, Does.Contain("3 / 8"));
                        NUnitAssert.That(progress.CurrentDetailText, Is.EqualTo("audio/test.bnk"));
                    });
                }
            }
        });
    }

    [Test]
    public void HistoryLoading_StaysInsideSidebarContentColumn()
    {
        var root = new DirectoryInfo(NUnit.Framework.TestContext.CurrentContext.TestDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "AssetEditor.CN.sln")))
            root = root.Parent;
        var document = XDocument.Load(Path.Combine(root!.FullName, "AssetEditor/Views/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var rail = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ActivityRail");
        var sidebar = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "WorkspaceShell");
        var history = document.Descendants().Single(element => element.Name.LocalName == nameof(FolderProjectHistoryView));
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That((string?)rail.Attribute("Grid.Column"), Is.EqualTo("0"));
            NUnitAssert.That((string?)sidebar.Attribute("Grid.Column"), Is.EqualTo("1"));
            NUnitAssert.That(sidebar.Attribute("Grid.ColumnSpan"), Is.Null);
            NUnitAssert.That(history.Ancestors(), Does.Contain(sidebar));
            NUnitAssert.That(history.Ancestors(), Does.Not.Contain(rail));
        });
    }

    [TestCase(ThemeType.DarkTheme, 1000, 650, 380, 1d)]
    [TestCase(ThemeType.LightTheme, 900, 560, 350, 1.25d)]
    [TestCase(ThemeType.HighContrastDark, 720, 510, 360, 1.5d)]
    [TestCase(ThemeType.HighContrastLight, 500, 420, 240, 1d)]
    public void LoadingPrompts_StayCenteredAcrossSizesAndDpiWithLongPaths(
        ThemeType theme, double width, double height, double sidebarWidth, double dpiScale)
    {
        WithTheme(theme, () =>
        {
            foreach (var longPath in new[] { false, true })
            {
                var probe = new LoadingProbe
                {
                    IsBusy = true,
                    OperationDetailText = longPath ? "audio/" + new string('长', 240) + ".bnk" : "audio/test.bnk",
                };
                var audio = new AudioExplorerView { DataContext = probe };
                var history = new FolderProjectHistoryView { DataContext = probe };
                try
                {
                    Layout(audio, width / dpiScale, height / dpiScale);
                    Layout(history, sidebarWidth / dpiScale, height / dpiScale);
                    AssertPromptCentered((FrameworkElement)audio.FindName("AudioLoadingContent"));
                    AssertPromptCentered((FrameworkElement)history.FindName("HistoryLoadingContent"));
                    Render(audio, $"{theme}-centered-audio-{dpiScale}-{longPath}", dpiScale);
                    Render(history, $"{theme}-centered-history-{dpiScale}-{longPath}", dpiScale);

                    probe.IsBusy = false;
                    probe.IsAudioPreviewLoading = true;
                    Layout(audio, width / dpiScale, height / dpiScale);
                    AssertPromptCentered((FrameworkElement)audio.FindName("AudioLoadingContent"));
                    Render(audio, $"{theme}-centered-preview-{dpiScale}-{longPath}", dpiScale);
                }
                finally
                {
                    probe.IsBusy = false;
                    probe.IsAudioPreviewLoading = false;
                }
            }
        });
    }

    private static void AssertPromptCentered(FrameworkElement loadingArea)
    {
        var scroll = Descendants<ScrollViewer>(loadingArea).First();
        var prompt = (FrameworkElement)scroll.Content;
        var bounds = prompt.TransformToAncestor(loadingArea).TransformBounds(new Rect(prompt.RenderSize));
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(prompt.ActualHeight,
                Is.EqualTo(prompt.DesiredSize.Height - prompt.Margin.Top - prompt.Margin.Bottom).Within(0.5),
                "Center the natural prompt height, not a stretched container with empty space below its text.");
            NUnitAssert.That(bounds.Left + bounds.Width / 2, Is.EqualTo(loadingArea.ActualWidth / 2).Within(0.5),
                "The whole prompt must be horizontally centered inside its content area.");
            NUnitAssert.That(bounds.Top + bounds.Height / 2, Is.EqualTo(loadingArea.ActualHeight / 2).Within(0.5),
                "The whole prompt must be vertically centered inside its content area.");
            NUnitAssert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
            NUnitAssert.That(bounds.Right, Is.LessThanOrEqualTo(loadingArea.ActualWidth));
            NUnitAssert.That(bounds.Top, Is.GreaterThanOrEqualTo(0));
            NUnitAssert.That(bounds.Bottom, Is.LessThanOrEqualTo(loadingArea.ActualHeight));
        });
        var progress = Descendants<OperationProgressView>(loadingArea).Single(view => IsVisibleWithin(view, loadingArea));
        foreach (var property in new[] { nameof(OperationProgressView.StatusText), nameof(OperationProgressView.CurrentDetailText) })
        {
            var text = Descendants<TextBlock>(progress).Single(element =>
                BindingOperations.GetBinding(element, TextBlock.TextProperty)?.Path.Path == property);
            NUnitAssert.That(text.TextAlignment, Is.EqualTo(TextAlignment.Center));
            NUnitAssert.That(text.ActualWidth, Is.LessThanOrEqualTo(bounds.Width));
            if (property == nameof(OperationProgressView.CurrentDetailText))
                NUnitAssert.That(text.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
        }
        foreach (var button in Descendants<Button>(prompt).Where(button => IsVisibleWithin(button, loadingArea)))
        {
            var buttonBounds = button.TransformToAncestor(loadingArea).TransformBounds(new Rect(button.RenderSize));
            NUnitAssert.That(buttonBounds.Left + buttonBounds.Width / 2, Is.EqualTo(loadingArea.ActualWidth / 2).Within(0.5));
        }
    }

    private static void AssertFillsContent(FrameworkElement loading, FrameworkElement content)
    {
        var bounds = loading.TransformToAncestor(content).TransformBounds(new Rect(loading.RenderSize));
        NUnitAssert.That(bounds, Is.EqualTo(new Rect(content.RenderSize)), "Loading must occupy the entire content area.");
        foreach (var point in new[] { new Point(2, 2), new Point(content.ActualWidth - 2, content.ActualHeight - 2) })
        {
            var hit = VisualTreeHelper.HitTest(content, point)?.VisualHit;
            while (hit != null && !ReferenceEquals(hit, loading))
                hit = VisualTreeHelper.GetParent(hit);
            NUnitAssert.That(hit, Is.SameAs(loading), "The loading surface must cover even its empty corners.");
        }
    }

    private static bool IsVisibleWithin(DependencyObject element, DependencyObject root)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
                return false;
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    private sealed record EditorProbe(string DisplayName, UserControl View);

    private sealed class FixedTemplateSelector(DataTemplate template) : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container) => template;
    }

    [Test]
    public void ProgressDetails_RemainEnabledForOtherConsumers()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var view = new OperationProgressView { UseDeferredVisibility = false };
            Layout(view, 420);
            NUnitAssert.That(view.ShowDetails, Is.True);
            NUnitAssert.That(((Expander)view.FindName("DetailsExpander")).Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    private static void WithTheme(ThemeType theme, Action action) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                ThemesController.SetTheme(theme);
                action();
            }
            finally
            {
                ThemesController.SetTheme(previous);
            }
        });

    private static void Layout(FrameworkElement element, double width, double height = double.PositiveInfinity)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, double.IsPositiveInfinity(height) ? element.DesiredSize.Height : height));
        element.UpdateLayout();
    }

    private static void Render(FrameworkElement element, string name, double dpiScale = 1)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpiScale),
            (int)Math.Ceiling(element.ActualHeight * dpiScale), 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var output = Environment.GetEnvironmentVariable("AECN_INLINE_UI_OUTPUT");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, $"{name}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class KitbashPlayerProbe
    {
        public bool IsEnabled { get; set; }
        public bool IsPlaying => true;
        public NotifyAttr<Visibility> AnimationControllerVisability { get; } = new(Visibility.Visible);
        public int CurrentFrame { get; set; } = 19;
        public int MaxFrames { get; set; } = 47;
        public double PlaybackPositionSeconds { get; set; } = 0.89;
        public double MaxTimeSeconds => 2.3;
        public string HeaderText { get; set; } = "测试动画";
        public object? SelectedAnimation { get; set; }
        public object? SelectedSkeleton { get; set; }
        public object[] SkeletonList => [];
        public object[] AnimationsForCurrentSkeleton => [];
    }

    private sealed class LoadingProbe : NotifyPropertyChangedImpl
    {
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                SetAndNotify(ref _isBusy, value);
                NotifyPropertyChanged(nameof(IsLoading));
            }
        }
        public bool IsLoading => IsBusy;
        private bool _isAudioPreviewLoading;
        public bool IsAudioPreviewLoading
        {
            get => _isAudioPreviewLoading;
            set => SetAndNotify(ref _isAudioPreviewLoading, value);
        }
        public bool IsExporting => false;
        public bool IsAudioPlaybackVisible => true;
        public bool IsRecoveryRequired => false;
        public bool IsOperationProgressIndeterminate => false;
        public bool LoadProgressIsIndeterminate => false;
        public string OperationStatusText => "正在读取工程历史";
        public string LoadStatus => "正在读取音频库";
        public string OperationDetailText { get; set; } = "audio/test.bnk";
        public string LoadProgressDetail => OperationDetailText;
        public string AudioPreviewProgressDetail => OperationDetailText;
        public double OperationProgressValue => 3;
        public double LoadProgressValue => 3;
        public double AudioPreviewProgressValue => 3;
        public double OperationProgressMaximum => 8;
        public double LoadProgressMaximum => 8;
        public double AudioPreviewProgressMaximum => 8;
    }
}
