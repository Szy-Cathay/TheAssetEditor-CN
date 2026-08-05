using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs;
using WindowHandling;
using NUnitAssert = NUnit.Framework.Assert;
using IOPath = System.IO.Path;

namespace AssetEditorTests;

[NonParallelizable]
public class UiThemeCompletionTests
{
    [Test]
    public void EverySelectableTheme_ExposesTheSameResourceKeys()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var baseline = LoadTheme(ThemeType.DarkTheme)
                .Keys.Cast<object>()
                .Select(key => key.ToString())
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            NUnitAssert.Multiple(() =>
            {
                foreach (var theme in Enum.GetValues<ThemeType>())
                {
                    var actual = LoadTheme(theme)
                        .Keys.Cast<object>()
                        .Select(key => key.ToString())
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToArray();
                    NUnitAssert.That(actual, Is.EqualTo(baseline), theme.ToString());
                }
            });
        });
    }

    [Test]
    public void EverySelectableTheme_InstantiatesTheCompleteControlLayer()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                try
                {
                    NUnitAssert.Multiple(() =>
                    {
                        foreach (var theme in Enum.GetValues<ThemeType>())
                        {
                            ThemesController.SetTheme(theme);
                            NUnitAssert.That(
                                Application.Current.FindResource("AeBrush.Canvas"),
                                Is.InstanceOf<SolidColorBrush>(),
                                theme.ToString());
                            NUnitAssert.That(
                                Application.Current.FindResource("AeButton.Primary"),
                                Is.InstanceOf<Style>(),
                                theme.ToString());
                            NUnitAssert.That(
                                Application.Current.FindResource("AeInput.TextBox"),
                                Is.InstanceOf<Style>(),
                                theme.ToString());
                            NUnitAssert.That(
                                Application.Current.FindResource("AeTree.View"),
                                Is.InstanceOf<Style>(),
                                theme.ToString());
                        }
                    });
                }
                finally
                {
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void ProductXaml_DoesNotReferenceRemovedFontKeys()
    {
        var solutionRoot = FindSolutionRoot();
        var offenders = ProductXamlFiles(solutionRoot)
            .Where(path => File.ReadAllText(path).Contains(
                "AeFont.",
                StringComparison.Ordinal))
            .Select(path => IOPath.GetRelativePath(solutionRoot, path))
            .OrderBy(path => path)
            .ToArray();

        NUnitAssert.That(
            offenders,
            Is.Empty,
            $"Invalid font resource references:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Test]
    public void AssetEditorWindow_TracksThemeFontAndBackgroundResources()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                var previousFont = ThemesController.CurrentFontFamily;
                var previousWeight = ThemesController.CurrentFontWeight;
                var customFont = new FontFamily("Microsoft YaHei UI");
                AssetEditorWindow? window = null;

                try
                {
                    ThemesController.SetTheme(ThemeType.DarkTheme);
                    window = new AssetEditorWindow();
                    NUnitAssert.That(
                        window.Background,
                        Is.EqualTo(Application.Current.FindResource("WindowBackground")));

                    ThemesController.ApplyCustomFont(customFont, FontWeights.SemiBold);
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(window.FontFamily.Source, Does.Contain("Microsoft YaHei UI"));
                        NUnitAssert.That(window.FontWeight, Is.EqualTo(FontWeights.SemiBold));
                    });
                }
                finally
                {
                    window?.Close();
                    ThemesController.ApplyCustomFont(previousFont, previousWeight);
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void OptionalRadioButtonStyle_UsesSemanticVectorTemplate()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/Shared.Ui;component/BaseDialogs/OptionalRadioButtonStyle.xaml"),
                };
                var style = (Style)dictionary["OptionalRadioButtonStyle"];
                var template = (ControlTemplate)style.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == Control.TemplateProperty)
                    .Value;
                var root = template.LoadContent();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(style.TargetType, Is.EqualTo(typeof(OptionalRadioButton)));
                    NUnitAssert.That(Descendants<Ellipse>(root).Count(), Is.EqualTo(2));
                    NUnitAssert.That(Descendants<ContentPresenter>(root), Is.Not.Empty);
                });
            });
    }

    [Test]
    public void OptionalRadioButton_OnlyAllowsClearingWhenOptional()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var optional = new TestOptionalRadioButton
            {
                IsChecked = true,
                IsOptional = true,
            };
            optional.SimulateClick();

            var required = new TestOptionalRadioButton
            {
                IsChecked = true,
                IsOptional = false,
            };
            required.SimulateClick();

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(optional.IsChecked, Is.False);
                NUnitAssert.That(required.IsChecked, Is.True);
            });
        });
    }

    [Test]
    public void EmbeddedOptionalRadioStyleDuplicate_IsRemoved()
    {
        var solutionRoot = FindSolutionRoot();
        var duplicate = IOPath.Combine(
            solutionRoot,
            "Shared",
            "EmbeddedResources",
            "Resources",
            "OptionalRadioButtonStyle.xaml");
        var project = File.ReadAllText(IOPath.Combine(
            solutionRoot,
            "Shared",
            "EmbeddedResources",
            "Shared.EmbeddedResources.csproj"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(File.Exists(duplicate), Is.False);
            NUnitAssert.That(project, Does.Not.Contain("OptionalRadioButtonStyle.xaml"));
        });
    }

    [Test]
    public void MigrationLedger_HasNoUnreviewedRows()
    {
        var ledger = File.ReadAllText(IOPath.Combine(
            FindSolutionRoot(),
            "docs",
            "superpowers",
            "plans",
            "ae-ui-migration-ledger.md"));

        NUnitAssert.That(ledger, Does.Not.Contain("| Unreviewed |"));
    }

    private static ResourceDictionary LoadTheme(ThemeType theme) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AssetEditor.CN;component/Themes/ColourDictionaries/{theme}.xaml"),
    };

    private static IEnumerable<string> ProductXamlFiles(string solutionRoot) =>
        new[] { "AssetEditor", "Shared", "Editors", "GameWorld" }
            .SelectMany(root => Directory.EnumerateFiles(
                IOPath.Combine(solutionRoot, root),
                "*.xaml",
                SearchOption.AllDirectories))
            .Where(path => !path.Split(
                    [IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part =>
                    part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("obj", StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<T> Descendants<T>(object root)
        where T : DependencyObject
    {
        if (root is not DependencyObject dependencyObject)
            yield break;

        var childCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(dependencyObject, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(IOPath.Combine(directory.FullName, "AssetEditor.CN.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AssetEditor.CN.sln.");
    }

    private sealed class TestOptionalRadioButton : OptionalRadioButton
    {
        public void SimulateClick() => OnClick();
    }
}
