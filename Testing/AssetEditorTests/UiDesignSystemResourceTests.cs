using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiDesignSystemResourceTests
{
    [Test]
    public void ApplicationResources_LoadDesignSystemInRequiredOrder()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var sources = Application.Current.Resources.MergedDictionaries
                    .Select(dictionary => dictionary.Source?.OriginalString)
                    .Where(source => source != null)
                    .ToList();
                var expected = new[]
                {
                    "Themes/ColourDictionaries/DarkTheme.xaml",
                    "Themes/ControlColours.xaml",
                    "Themes/DesignSystem/DesignTokens.xaml",
                    "Themes/DesignSystem/Typography.xaml",
                    "Themes/DesignSystem/SurfaceStyles.xaml",
                    "Themes/Controls.xaml",
                };

                NUnitAssert.Multiple(() =>
                {
                    for (var index = 0; index < expected.Length; index++)
                    {
                        NUnitAssert.That(
                            sources[index],
                            Does.EndWith(expected[index]),
                            $"Merged dictionary position {index}.");
                    }
                });
            });
    }

    [Test]
    public void ThemeSwitch_UpdatesSemanticBrushConsumers()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var previousTheme = ThemesController.CurrentTheme;
                Window? window = null;

                try
                {
                    ThemesController.SetTheme(ThemeType.DarkTheme);
                    var border = new Border
                    {
                        Style = (Style)Application.Current.FindResource(
                            "AeSurface.Panel"),
                    };
                    border.SetResourceReference(
                        Border.BackgroundProperty,
                        "AeBrush.Surface1");
                    window = new Window
                    {
                        Content = border,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                    };
                    window.Show();
                    var dark = ((SolidColorBrush)border.Background).Color;

                    ThemesController.SetTheme(ThemeType.LightTheme);
                    var light = ((SolidColorBrush)border.Background).Color;

                    NUnitAssert.That(light, Is.Not.EqualTo(dark));
                }
                finally
                {
                    window?.Close();
                    ThemesController.SetTheme(previousTheme);
                }
            });
    }

    [Test]
    public void Typography_ExposesApprovedTextRoles()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var dictionary = Load(
                "Themes/DesignSystem/Typography.xaml");
            var expected = new Dictionary<string, double>
            {
                ["AeText.PageTitle"] = 20,
                ["AeText.SectionTitle"] = 13,
                ["AeText.Body"] = 12,
                ["AeText.Label"] = 11,
                ["AeText.Caption"] = 11,
                ["AeText.Technical"] = 11,
            };

            NUnitAssert.Multiple(() =>
            {
                foreach (var pair in expected)
                {
                    var style = (Style)dictionary[pair.Key];
                    NUnitAssert.That(
                        style.TargetType,
                        Is.EqualTo(typeof(TextBlock)),
                        pair.Key);
                    NUnitAssert.That(
                        style.Setters.OfType<Setter>()
                            .Single(setter =>
                                setter.Property == TextBlock.FontSizeProperty)
                            .Value,
                        Is.EqualTo(pair.Value),
                        pair.Key);
                }
            });
        });
    }

    [Test]
    public void SurfaceStyles_AreKeyedAndDoNotReplaceImplicitBorderStyle()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var tokens = Load("Themes/DesignSystem/DesignTokens.xaml");
            Application.Current.Resources.MergedDictionaries.Add(tokens);

            try
            {
                var dictionary = Load(
                    "Themes/DesignSystem/SurfaceStyles.xaml");
                var keys = new[]
                {
                    "AeSurface.Canvas",
                    "AeSurface.Panel",
                    "AeSurface.Control",
                    "AeSurface.Overlay",
                };

                NUnitAssert.Multiple(() =>
                {
                    foreach (var key in keys)
                    {
                        var style = (Style)dictionary[key];
                        NUnitAssert.That(
                            style.TargetType,
                            Is.EqualTo(typeof(Border)));
                    }

                    NUnitAssert.That(
                        dictionary.Contains(typeof(Border)),
                        Is.False,
                        "Foundation styles must not replace the implicit Border style.");
                });
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(tokens);
            }
        });
    }

    private static readonly string[] SemanticBrushKeys =
    [
        "AeBrush.Canvas",
        "AeBrush.Surface1",
        "AeBrush.Surface2",
        "AeBrush.Surface3",
        "AeBrush.SurfaceHover",
        "AeBrush.Border",
        "AeBrush.BorderStrong",
        "AeBrush.TextPrimary",
        "AeBrush.TextSecondary",
        "AeBrush.TextMuted",
        "AeBrush.Accent",
        "AeBrush.AccentHover",
        "AeBrush.AccentSoft",
        "AeBrush.Success",
        "AeBrush.Warning",
        "AeBrush.Danger",
    ];

    [TestCaseSource(nameof(ThemeNames))]
    public void EveryTheme_ExposesSemanticBrushContract(string themeName)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var dictionary = Load(
                $"Themes/ColourDictionaries/{themeName}.xaml");

            NUnitAssert.Multiple(() =>
            {
                foreach (var key in SemanticBrushKeys)
                {
                    NUnitAssert.That(
                        dictionary.Contains(key),
                        Is.True,
                        $"{themeName} is missing {key}.");
                    NUnitAssert.That(
                        dictionary[key],
                        Is.InstanceOf<SolidColorBrush>(),
                        $"{themeName} {key} is not a SolidColorBrush.");
                }
            });
        });
    }

    [Test]
    public void DarkTheme_UsesApprovedGraphitePalette()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var dictionary = Load(
                "Themes/ColourDictionaries/DarkTheme.xaml");
            var expected = new Dictionary<string, string>
            {
                ["AeBrush.Canvas"] = "#FF151719",
                ["AeBrush.Surface1"] = "#FF1B1E21",
                ["AeBrush.Surface2"] = "#FF212529",
                ["AeBrush.Surface3"] = "#FF282D32",
                ["AeBrush.SurfaceHover"] = "#FF30363C",
                ["AeBrush.Border"] = "#FF343A40",
                ["AeBrush.BorderStrong"] = "#FF464E56",
                ["AeBrush.TextPrimary"] = "#FFE4E7E9",
                ["AeBrush.TextSecondary"] = "#FFB0B6BC",
                ["AeBrush.TextMuted"] = "#FF858D95",
                ["AeBrush.Accent"] = "#FF64A9E2",
                ["AeBrush.AccentHover"] = "#FF75B5E8",
                ["AeBrush.AccentSoft"] = "#FF263A4B",
                ["AeBrush.Success"] = "#FF72BC91",
                ["AeBrush.Warning"] = "#FFE2B45F",
                ["AeBrush.Danger"] = "#FFE17979",
            };

            NUnitAssert.Multiple(() =>
            {
                foreach (var pair in expected)
                {
                    var brush = (SolidColorBrush)dictionary[pair.Key];
                    NUnitAssert.That(
                        brush.Color.ToString(),
                        Is.EqualTo(pair.Value),
                        pair.Key);
                }
            });
        });
    }

    [Test]
    public void DesignTokens_ExposeApprovedMetricsAndDurations()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var dictionary = Load(
                "Themes/DesignSystem/DesignTokens.xaml");

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(dictionary["AeSpace.1"], Is.EqualTo(4d));
                NUnitAssert.That(dictionary["AeSpace.2"], Is.EqualTo(8d));
                NUnitAssert.That(dictionary["AeSpace.3"], Is.EqualTo(12d));
                NUnitAssert.That(dictionary["AeSpace.4"], Is.EqualTo(16d));
                NUnitAssert.That(dictionary["AeSpace.6"], Is.EqualTo(24d));
                NUnitAssert.That(dictionary["AeSpace.8"], Is.EqualTo(32d));
                NUnitAssert.That(
                    dictionary["AeSize.ActivityRailWidth"],
                    Is.EqualTo(34d));
                NUnitAssert.That(dictionary["AeSize.TabHeight"], Is.EqualTo(24d));
                NUnitAssert.That(
                    dictionary["AeSize.CompactRowHeight"],
                    Is.EqualTo(28d));
                NUnitAssert.That(
                    dictionary["AeSize.ControlHeight"],
                    Is.EqualTo(30d));
                NUnitAssert.That(
                    dictionary["AeSize.ProminentControlHeight"],
                    Is.EqualTo(34d));
                NUnitAssert.That(
                    dictionary["AeRadius.Compact"],
                    Is.EqualTo(new CornerRadius(3)));
                NUnitAssert.That(
                    dictionary["AeRadius.Control"],
                    Is.EqualTo(new CornerRadius(4)));
                NUnitAssert.That(
                    dictionary["AeRadius.Surface"],
                    Is.EqualTo(new CornerRadius(6)));
                NUnitAssert.That(
                    dictionary["AeRadius.Overlay"],
                    Is.EqualTo(new CornerRadius(7)));
                NUnitAssert.That(
                    ((Duration)dictionary["AeMotion.Pressed"]).TimeSpan,
                    Is.EqualTo(TimeSpan.FromMilliseconds(70)));
                NUnitAssert.That(
                    ((Duration)dictionary["AeMotion.Hover"]).TimeSpan,
                    Is.EqualTo(TimeSpan.FromMilliseconds(120)));
                NUnitAssert.That(
                    ((Duration)dictionary["AeMotion.Selection"]).TimeSpan,
                    Is.EqualTo(TimeSpan.FromMilliseconds(140)));
                NUnitAssert.That(
                    ((Duration)dictionary["AeMotion.Overlay"]).TimeSpan,
                    Is.EqualTo(TimeSpan.FromMilliseconds(160)));
                NUnitAssert.That(
                    dictionary["AeMotion.OverlayOffset"],
                    Is.EqualTo(2d));
            });
        });
    }

    private static IEnumerable<string> ThemeNames() =>
        Enum.GetNames<ThemeType>();

    [Test]
    public void MigrationLedger_CoversEveryProductXamlSource()
    {
        var solutionRoot = FindSolutionRoot();
        var ledgerPath = Path.Combine(
            solutionRoot,
            "docs",
            "superpowers",
            "plans",
            "ae-ui-migration-ledger.md");
        var ledgerPaths = File.ReadLines(ledgerPath)
            .Select(line => line.Split('|'))
            .Where(cells => cells.Length > 2)
            .Select(cells => cells[1].Trim())
            .Where(value =>
                value.StartsWith('`') &&
                value.EndsWith(".xaml`", StringComparison.OrdinalIgnoreCase))
            .Select(value => value[1..^1].Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sourcePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var productRoot in new[]
                 {
                     "AssetEditor",
                     "Shared",
                     "Editors",
                     "GameWorld",
                 })
        {
            var absoluteRoot = Path.Combine(solutionRoot, productRoot);
            foreach (var path in Directory.EnumerateFiles(
                         absoluteRoot,
                         "*.xaml",
                         SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(solutionRoot, path);
                if (!ContainsBuildOutputDirectory(relativePath))
                    sourcePaths.Add(relativePath.Replace('\\', '/'));
            }
        }

        var missing = sourcePaths.Except(ledgerPaths)
            .OrderBy(path => path)
            .ToArray();
        var extra = ledgerPaths.Except(sourcePaths)
            .OrderBy(path => path)
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                missing,
                Is.Empty,
                $"Ledger is missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
            NUnitAssert.That(
                extra,
                Is.Empty,
                $"Ledger has extra paths:{Environment.NewLine}{string.Join(Environment.NewLine, extra)}");
        });
    }

    private static bool ContainsBuildOutputDirectory(string path) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(part =>
                part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("obj", StringComparison.OrdinalIgnoreCase));

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
