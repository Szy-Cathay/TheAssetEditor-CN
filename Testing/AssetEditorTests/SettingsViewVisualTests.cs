using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AssetEditor.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class SettingsViewVisualTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void CustomLayoutStyles_PreserveApplicationThemeColours()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();

        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var view = new SettingsView();
                var foreground = GetThemeBrush("ABrush.Foreground.Static");
                var textBoxBackground = GetThemeBrush("TextBox.Static.Background");
                var comboBoxBackground = GetThemeBrush("ComboBox.Static.Background");
                var layoutTextBlocks = GetDescendants<TextBlock>(view)
                    .Where(textBlock =>
                        ReferenceEquals(
                            textBlock.Style,
                            view.Resources["SectionTitleStyle"]) ||
                        ReferenceEquals(
                            textBlock.Style,
                            view.Resources["SettingsLabelStyle"]))
                    .ToList();
                var textBoxes = GetDescendants<TextBox>(view).ToList();
                var comboBoxes = GetDescendants<ComboBox>(view).ToList();
                var checkBoxes = GetDescendants<CheckBox>(view).ToList();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(layoutTextBlocks, Is.Not.Empty);
                    NUnitAssert.That(
                        layoutTextBlocks.Select(value => GetBrushSignature(value.Foreground)),
                        Has.All.EqualTo(foreground));
                    NUnitAssert.That(textBoxes, Is.Not.Empty);
                    NUnitAssert.That(
                        textBoxes.Select(value => GetBrushSignature(value.Background)),
                        Has.All.EqualTo(textBoxBackground));
                    NUnitAssert.That(comboBoxes, Is.Not.Empty);
                    NUnitAssert.That(
                        comboBoxes.Select(value => GetBrushSignature(value.Background)),
                        Has.All.EqualTo(comboBoxBackground));
                    NUnitAssert.That(checkBoxes, Is.Not.Empty);
                    NUnitAssert.That(
                        checkBoxes.Select(value => GetBrushSignature(value.Foreground)),
                        Has.All.EqualTo(foreground));
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
                    localization.Get("SettingsWindow.Timing.NextAudioCompile"),
                    localization.Get("SettingsWindow.Timing.SaveImmediate"),
                    localization.Get("SettingsWindow.Timing.NextBackup"),
                    localization.Get("SettingsWindow.Timing.NextPackSave")
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
