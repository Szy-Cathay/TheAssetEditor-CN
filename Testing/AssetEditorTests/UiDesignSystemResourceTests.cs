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

    private static ResourceDictionary Load(string path) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AssetEditor.CN;component/{path}"),
    };
}
