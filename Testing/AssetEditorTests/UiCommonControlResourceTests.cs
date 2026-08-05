using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;
using ShapePath = System.Windows.Shapes.Path;

namespace AssetEditorTests;

[NonParallelizable]
public class UiCommonControlResourceTests
{
    private static readonly IReadOnlyDictionary<string, Type> ExpectedStyles =
        new Dictionary<string, Type>
        {
            ["AeButton.Primary"] = typeof(Button),
            ["AeButton.Secondary"] = typeof(Button),
            ["AeButton.Quiet"] = typeof(Button),
            ["AeButton.Danger"] = typeof(Button),
            ["AeButton.Icon"] = typeof(Button),
            ["AeInput.TextBox"] = typeof(TextBox),
            ["AeInput.ComboBox"] = typeof(ComboBox),
            ["AeInput.CheckBox"] = typeof(CheckBox),
            ["AeInput.RadioButton"] = typeof(RadioButton),
            ["AeInput.Switch"] = typeof(ToggleButton),
            ["AeValidation.Message"] = typeof(TextBlock),
            ["AeTab.Item"] = typeof(TabItem),
            ["AeTag.Container"] = typeof(Border),
            ["AeTag.Text"] = typeof(TextBlock),
            ["AeTree.View"] = typeof(TreeView),
            ["AeTree.Item"] = typeof(TreeViewItem),
            ["AeList.View"] = typeof(ListBox),
            ["AeList.Item"] = typeof(ListBoxItem),
            ["AeTable.Grid"] = typeof(DataGrid),
            ["AeTable.Header"] = typeof(DataGridColumnHeader),
            ["AeTable.Row"] = typeof(DataGridRow),
            ["AeTable.Cell"] = typeof(DataGridCell),
            ["AeMenu.Bar"] = typeof(Menu),
            ["AeMenu.Item"] = typeof(MenuItem),
            ["AeMenu.Context"] = typeof(ContextMenu),
            ["AeToolTip"] = typeof(ToolTip),
            ["AeFeedback.Notice"] = typeof(Border),
            ["AeFeedback.Icon"] = typeof(Border),
            ["AeFeedback.SuccessIcon"] = typeof(Border),
            ["AeFeedback.WarningIcon"] = typeof(Border),
            ["AeFeedback.DangerIcon"] = typeof(Border),
            ["AeEmptyState.Panel"] = typeof(Border),
            ["AeEmptyState.Title"] = typeof(TextBlock),
            ["AeEmptyState.Description"] = typeof(TextBlock),
            ["AeProgress.Bar"] = typeof(ProgressBar),
            ["AeScrollBar.Compact"] = typeof(ScrollBar),
        };

    [Test]
    public void CommonControlDictionaries_ExposeApprovedKeyedStylesOnly()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var dictionaries = LoadDictionaries();

                NUnitAssert.Multiple(() =>
                {
                    foreach (var pair in ExpectedStyles)
                    {
                        var style = dictionaries
                            .Select(dictionary => dictionary[pair.Key])
                            .OfType<Style>()
                            .SingleOrDefault();
                        NUnitAssert.That(style, Is.Not.Null, pair.Key);
                        NUnitAssert.That(
                            style!.TargetType,
                            Is.EqualTo(pair.Value),
                            pair.Key);
                    }

                    foreach (var dictionary in dictionaries)
                    {
                        NUnitAssert.That(
                            dictionary.Keys.OfType<Type>(),
                            Is.Empty,
                            dictionary.Source?.OriginalString);
                    }
                });
            });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void CommonControlStyles_InstantiateInRequiredThemes(ThemeType theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                try
                {
                    ThemesController.SetTheme(theme);
                    var controls = ExpectedStyles.Select(pair =>
                    {
                        var control = (FrameworkElement)Activator.CreateInstance(
                            pair.Value)!;
                        control.Style = (Style)Application.Current.FindResource(
                            pair.Key);
                        return control;
                    }).ToArray();
                    var panel = new StackPanel();
                    foreach (var control in controls)
                    {
                        if (control is ContextMenu or ToolTip)
                        {
                            control.ApplyTemplate();
                            continue;
                        }

                        panel.Children.Add(control);
                    }

                    panel.Measure(new Size(800, double.PositiveInfinity));
                    panel.Arrange(new Rect(panel.DesiredSize));
                    panel.UpdateLayout();

                    NUnitAssert.That(
                        controls.Select(control => control.ActualHeight),
                        Has.All.GreaterThanOrEqualTo(0));
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void TreeChevron_UsesFixedCenteredVectorBox()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var item = new TreeViewItem
                {
                    Header = "帝国将军.pack",
                    Style = (Style)Application.Current.FindResource(
                        "AeTree.Item"),
                };
                var window = new Window
                {
                    Content = item,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    item.ApplyTemplate();
                    var host = (FrameworkElement)item.Template.FindName(
                        "PART_ChevronHost",
                        item);
                    var path = FindDescendant<ShapePath>(host);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(host.Width, Is.EqualTo(20));
                        NUnitAssert.That(
                            host.Height,
                            Is.EqualTo((double)Application.Current.FindResource(
                                "AeSize.CompactRowHeight")));
                        NUnitAssert.That(
                            host.VerticalAlignment,
                            Is.EqualTo(VerticalAlignment.Center));
                        NUnitAssert.That(path, Is.Not.Null);
                        NUnitAssert.That(
                            path!.VerticalAlignment,
                            Is.EqualTo(VerticalAlignment.Center));
                        NUnitAssert.That(
                            path.HorizontalAlignment,
                            Is.EqualTo(HorizontalAlignment.Center));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void FeedbackIcon_IsCenteredAgainstWholeNotice()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var icon = new Border
                {
                    Style = (Style)Application.Current.FindResource(
                        "AeFeedback.SuccessIcon"),
                };

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(icon.Width, Is.EqualTo(17));
                    NUnitAssert.That(icon.Height, Is.EqualTo(17));
                    NUnitAssert.That(
                        icon.VerticalAlignment,
                        Is.EqualTo(VerticalAlignment.Center));
                });
            });
    }

    [Test]
    public void ComboBox_DisplaysConfiguredMemberForSelectedObject()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var option = new NamedOption("master");
                var comboBox = new ComboBox
                {
                    Width = 240,
                    ItemsSource = new[] { option },
                    SelectedItem = option,
                    DisplayMemberPath = nameof(NamedOption.Name),
                    Style = (Style)Application.Current.FindResource(
                        "AeInput.ComboBox"),
                };
                var window = new Window
                {
                    Content = comboBox,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var visibleText = FindDescendants<TextBlock>(comboBox)
                        .Select(text => text.Text)
                        .ToArray();

                    NUnitAssert.That(visibleText, Does.Contain("master"));
                    NUnitAssert.That(
                        visibleText,
                        Has.None.Contains(nameof(NamedOption)));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void Buttons_UseApprovedWeakInteractionDurations()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var style = (Style)Application.Current.FindResource(
                    "AeButton.Primary");
                var template = FindSetter(style, Control.TemplateProperty)
                    ?.Value as ControlTemplate;
                var root = (FrameworkElement)template!.LoadContent();
                var group = VisualStateManager.GetVisualStateGroups(root)
                    .OfType<VisualStateGroup>()
                    .Single(item => item.Name == "CommonStates");
                var durations = group.Transitions
                    .Cast<VisualTransition>()
                    .Select(item => item.GeneratedDuration.TimeSpan)
                    .ToArray();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        durations,
                        Does.Contain(TimeSpan.FromMilliseconds(70)));
                    NUnitAssert.That(
                        durations,
                        Does.Contain(TimeSpan.FromMilliseconds(120)));
                });
            });
    }

    private static ResourceDictionary[] LoadDictionaries() =>
    [
        Load("Themes/DesignSystem/Controls/Buttons.xaml"),
        Load("Themes/DesignSystem/Controls/Inputs.xaml"),
        Load("Themes/DesignSystem/Controls/Collections.xaml"),
        Load("Themes/DesignSystem/Controls/MenusAndFeedback.xaml"),
    ];

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;
            var nested = FindDescendant<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private static Setter? FindSetter(Style? style, DependencyProperty property)
    {
        while (style != null)
        {
            var setter = style.Setters
                .OfType<Setter>()
                .FirstOrDefault(item => item.Property == property);
            if (setter != null)
                return setter;
            style = style.BasedOn;
        }

        return null;
    }

    private static ResourceDictionary Load(string path) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AssetEditor.CN;component/{path}"),
    };

    private sealed record NamedOption(string Name);
}
