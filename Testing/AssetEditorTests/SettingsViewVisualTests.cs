using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.ColourPickerButton;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class SettingsViewVisualTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void CustomLayoutStyles_UseSemanticApplicationThemeColours()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var primary = GetThemeBrush("AeBrush.TextPrimary");
                var secondary = GetThemeBrush("AeBrush.TextSecondary");
                var inputBackground = GetThemeBrush("AeBrush.Surface2");
                var layoutTextBlocks = GetDescendants<TextBlock>(view)
                    .Where(textBlock =>
                        ReferenceEquals(
                            textBlock.Style,
                            view.Resources["SectionTitleStyle"]) ||
                        ReferenceEquals(
                            textBlock.Style,
                            view.Resources["SettingsLabelStyle"]))
                    .ToList();
                var textBoxes = GetDescendants<TextBox>(view)
                    .Where(control => !IsInsideColourPicker(control))
                    .ToList();
                var comboBoxes = GetDescendants<ComboBox>(view)
                    .Where(control => !IsInsideColourPicker(control))
                    .ToList();
                var checkBoxes = GetDescendants<CheckBox>(view)
                    .Where(control => !IsInsideColourPicker(control))
                    .ToList();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(layoutTextBlocks, Is.Not.Empty);
                    NUnitAssert.That(
                        layoutTextBlocks.Select(value => GetBrushSignature(value.Foreground)),
                        Has.All.Matches<string>(value =>
                            value == primary || value == secondary));
                    NUnitAssert.That(
                        layoutTextBlocks.Select(value =>
                            GetBrushSignature(value.Foreground)),
                        Does.Contain(primary));
                    NUnitAssert.That(
                        layoutTextBlocks.Select(value =>
                            GetBrushSignature(value.Foreground)),
                        Does.Contain(secondary));
                    NUnitAssert.That(textBoxes, Is.Not.Empty);
                    NUnitAssert.That(
                        textBoxes.Select(value => GetBrushSignature(value.Background)),
                        Has.All.EqualTo(inputBackground));
                    NUnitAssert.That(comboBoxes, Is.Not.Empty);
                    NUnitAssert.That(
                        comboBoxes.Select(value => GetBrushSignature(value.Background)),
                        Has.All.EqualTo(inputBackground));
                    NUnitAssert.That(checkBoxes, Is.Not.Empty);
                    NUnitAssert.That(
                        checkBoxes.Select(value => GetBrushSignature(value.Foreground)),
                        Has.All.EqualTo(secondary));
                });
            });
    }

    [Test]
    public void Descriptions_AppearInToolTipsInsteadOfPageContent()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var tabs = (TabControl)view.FindName(
                    "SettingsCategories");
                tabs.SelectedIndex = 2;
                view.UpdateLayout();
                var localization = LocalizationManager.Instance;
                var descriptionTexts = new[]
                {
                    localization.Get("SettingsWindow.BackfaceHelp"),
                    localization.Get("SettingsWindow.Timing.ImmediatePreview"),
                    localization.Get("SettingsWindow.Timing.NextStart"),
                    localization.Get("SettingsWindow.Timing.Restart"),
                    localization.Get("SettingsWindow.Timing.SaveRefresh"),
                    localization.Get("SettingsWindow.Timing.NextModelLoad"),
                    localization.Get("SettingsWindow.Timing.GamePath"),
                    localization.Get("SettingsWindow.Timing.NextAudioCompile")
                };
                var pageTexts = GetDescendants<TextBlock>(view)
                    .Select(textBlock => textBlock.Text)
                    .ToList();
                var settingLabels = GetDescendants<TextBlock>(view)
                    .Where(textBlock => ReferenceEquals(
                        textBlock.Style,
                        view.Resources["SettingsLabelStyle"]))
                    .ToList();
                var backfaceLabel = settingLabels.Single(textBlock =>
                    textBlock.Text == localization.Get(
                        "SettingsWindow.BackfaceDisplay"));
                var backfaceToolTipTexts = GetToolTipTexts(
                    backfaceLabel.ToolTip).ToList();

                NUnitAssert.Multiple(() =>
                {
                    foreach (var description in descriptionTexts)
                        NUnitAssert.That(pageTexts, Does.Not.Contain(description));

                    NUnitAssert.That(settingLabels, Is.Not.Empty);
                    NUnitAssert.That(
                        settingLabels.All(label => label.ToolTip != null),
                        Is.True);
                    NUnitAssert.That(
                        backfaceToolTipTexts,
                        Does.Contain(localization.Get(
                            "SettingsWindow.BackfaceHelp")));
                    NUnitAssert.That(
                        backfaceToolTipTexts,
                        Does.Contain(localization.Get(
                            "SettingsWindow.Timing.ImmediatePreview")));
                });
            });
    }

    [Test]
    public void Categories_UseLeftNavigationColourPickersAndCollapsedLighting()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var localization = LocalizationManager.Instance;
                var tabs = (TabControl)view.FindName(
                    "SettingsCategories");
                var headers = tabs.Items.OfType<TabItem>()
                    .Select(item => item.Header)
                    .ToList();
                tabs.SelectedIndex = 2;
                view.UpdateLayout();
                var colourPickers = GetDescendants<
                    ColourPickerButtonView>(view).ToList();
                var lighting = GetDescendants<Expander>(view)
                    .Single(item => Equals(
                        item.Header,
                        localization.Get(
                            "SettingsWindow.PhotoStudioLighting")));

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        tabs.TabStripPlacement,
                        Is.EqualTo(Dock.Left));
                    NUnitAssert.That(
                        headers,
                        Is.EqualTo(new[]
                        {
                            localization.Get("SettingsWindow.General"),
                            localization.Get("SettingsWindow.ThemeCategory"),
                            localization.Get("SettingsWindow.Rendering"),
                            localization.Get("SettingsWindow.Audio")
                        }));
                    NUnitAssert.That(colourPickers, Has.Count.EqualTo(2));
                    NUnitAssert.That(lighting.IsExpanded, Is.False);
                });
            });
    }

    [Test]
    public void CategoryPages_AreAnchoredToTheUpperLeft()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var pageStyle = (Style)view.Resources["SettingsPageStyle"];
                var pages = GetDescendants<StackPanel>(view)
                    .Where(panel => ReferenceEquals(panel.Style, pageStyle))
                    .ToList();
                var pageScrollViewerStyle =
                    (Style)view.Resources["SettingsPageScrollViewerStyle"];
                var pageScrollViewers = GetDescendants<ScrollViewer>(view)
                    .Where(scrollViewer => ReferenceEquals(
                        scrollViewer.Style,
                        pageScrollViewerStyle))
                    .ToList();
                var tabs = (TabControl)view.FindName(
                    "SettingsCategories");
                var availableSize = new Size(780, 500);
                view.Measure(availableSize);
                view.Arrange(new Rect(availableSize));
                tabs.ApplyTemplate();
                view.UpdateLayout();
                var selectedContentHost = (ContentPresenter)tabs.Template
                    .FindName("PART_SelectedContentHost", tabs);
                var selectedTab = (TabItem)tabs.SelectedItem;
                var selectedScrollViewer =
                    (ScrollViewer)selectedTab.Content;
                var selectedPage =
                    (StackPanel)selectedScrollViewer.Content;
                var selectedPageOrigin = selectedPage.TranslatePoint(
                    new Point(),
                    selectedContentHost);
                var selectedPageViewOrigin = selectedPage.TranslatePoint(
                    new Point(),
                    view);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(pages, Has.Count.EqualTo(4));
                    NUnitAssert.That(
                        pages.Select(page => page.HorizontalAlignment),
                        Has.All.EqualTo(HorizontalAlignment.Left));
                    NUnitAssert.That(
                        pages.Select(page => page.VerticalAlignment),
                        Has.All.EqualTo(VerticalAlignment.Top));
                    NUnitAssert.That(
                        pages.Select(page => page.Margin),
                        Has.All.EqualTo(new Thickness(12, 10, 12, 10)));
                    NUnitAssert.That(pageScrollViewers, Has.Count.EqualTo(4));
                    NUnitAssert.That(
                        pageScrollViewers.Select(item =>
                            item.HorizontalContentAlignment),
                        Has.All.EqualTo(HorizontalAlignment.Left));
                    NUnitAssert.That(
                        pageScrollViewers.Select(item =>
                            item.VerticalContentAlignment),
                        Has.All.EqualTo(VerticalAlignment.Top));
                    NUnitAssert.That(
                        selectedPageOrigin,
                        Is.EqualTo(new Point(12, 10)));
                    NUnitAssert.That(
                        selectedPageViewOrigin,
                        Is.EqualTo(new Point(156, 10)));
                    NUnitAssert.That(tabs.ActualHeight, Is.EqualTo(500));
                    NUnitAssert.That(
                        tabs.VerticalContentAlignment,
                        Is.EqualTo(VerticalAlignment.Stretch),
                        "Settings TabControl content alignment");
                    NUnitAssert.That(
                        selectedContentHost.VerticalAlignment,
                        Is.EqualTo(VerticalAlignment.Stretch),
                        "Selected content host alignment");
                    NUnitAssert.That(
                        selectedContentHost.ActualHeight,
                        Is.EqualTo(500));
                });
            });
    }

    [Test]
    public void CategoryNavigationAndFields_UseCompactOperationalSpacing()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var tabs = (TabControl)view.FindName(
                    "SettingsCategories");
                var items = tabs.Items.OfType<TabItem>().ToList();
                var labels = GetDescendants<TextBlock>(view)
                    .Where(textBlock => ReferenceEquals(
                        textBlock.Style,
                        view.Resources["SettingsLabelStyle"]))
                    .ToList();
                var fieldControls = GetDescendants<Control>(view)
                    .Where(control =>
                        ReferenceEquals(
                            control.Style,
                            view.Resources["ComboBoxFieldStyle"]) ||
                        ReferenceEquals(
                            control.Style,
                            view.Resources["CheckBoxFieldStyle"]) ||
                        ReferenceEquals(
                            control.Style,
                            view.Resources["TextBoxFieldStyle"]))
                    .ToList();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        items.Select(item => item.Width),
                        Has.All.EqualTo(144));
                    NUnitAssert.That(
                        items.Select(item => item.MinHeight),
                        Has.All.EqualTo(32));
                    NUnitAssert.That(
                        items.Select(item => item.Padding),
                        Has.All.EqualTo(new Thickness(12, 0, 12, 0)));
                    NUnitAssert.That(labels, Is.Not.Empty);
                    NUnitAssert.That(
                        labels.Select(label => label.Margin.Bottom),
                        Has.All.EqualTo(6));
                    NUnitAssert.That(fieldControls, Is.Not.Empty);
                    NUnitAssert.That(
                        fieldControls.Select(control => control.Margin.Bottom),
                        Has.All.EqualTo(6));
                });
            });
    }

    [Test]
    public void AllCategories_RenderOffscreenAtMinimumContentSize()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var tabs = (TabControl)view.FindName(
                    "SettingsCategories");
                var availableSize = new Size(780, 500);

                view.Measure(availableSize);
                view.Arrange(new Rect(availableSize));

                for (var index = 0; index < tabs.Items.Count; index++)
                {
                    tabs.SelectedIndex = index;
                    view.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(
                        (int)availableSize.Width,
                        (int)availableSize.Height,
                        96,
                        96,
                        PixelFormats.Pbgra32);

                    NUnitAssert.That(
                        () => bitmap.Render(view),
                        Throws.Nothing,
                        $"Settings category {index} failed to render.");
                }
            });
    }

    private static string GetThemeBrush(string resourceKey) =>
        GetBrushSignature((Brush)Application.Current.FindResource(resourceKey));

    private static string GetBrushSignature(Brush brush) =>
        $"{brush.GetType().Name}:{brush}";

    private static IEnumerable<T> GetDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T value)
                yield return value;

            if (child is not DependencyObject dependencyObject)
                continue;

            foreach (var descendant in GetDescendants<T>(dependencyObject))
                yield return descendant;
        }
    }

    private static bool IsInsideColourPicker(
        DependencyObject control)
    {
        var current = control;
        while (current != null)
        {
            if (current is ColourPickerButtonView)
                return true;
            current = LogicalTreeHelper.GetParent(current) ??
                      (current is Visual
                          ? VisualTreeHelper.GetParent(current)
                          : null);
        }

        return false;
    }

    private static IEnumerable<string> GetToolTipTexts(object? toolTip)
    {
        if (toolTip is string text)
        {
            yield return text;
            yield break;
        }

        if (toolTip is TextBlock textBlock)
            yield return textBlock.Text;

        if (toolTip is not DependencyObject dependencyObject)
            yield break;

        foreach (var descendant in GetDescendants<TextBlock>(dependencyObject))
            yield return descendant.Text;
    }
}
