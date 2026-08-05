using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public partial class UiPropertyFormLayoutTests
{
    private static readonly string[] ProductRoots =
    [
        "AssetEditor",
        "Editors",
        "GameWorld",
        "Shared",
    ];

    [Test]
    public void FormLabelStyle_RightAlignsLabelsTowardTheControlColumn()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var style = (Style)Application.Current.FindResource(
                    "AeForm.Label");
                var label = new Label { Style = style };
                var textStyle = (Style)Application.Current.FindResource(
                    "AeForm.TextLabel");
                var textLabel = new TextBlock { Style = textStyle };

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        label.HorizontalAlignment,
                        Is.EqualTo(HorizontalAlignment.Stretch));
                    NUnitAssert.That(
                        label.HorizontalContentAlignment,
                        Is.EqualTo(HorizontalAlignment.Right));
                    NUnitAssert.That(
                        label.VerticalContentAlignment,
                        Is.EqualTo(VerticalAlignment.Center));
                    NUnitAssert.That(
                        label.Margin,
                        Is.EqualTo(new Thickness(0, 0, 8, 0)));
                    NUnitAssert.That(
                        textLabel.HorizontalAlignment,
                        Is.EqualTo(HorizontalAlignment.Stretch));
                    NUnitAssert.That(
                        textLabel.TextAlignment,
                        Is.EqualTo(TextAlignment.Right));
                    NUnitAssert.That(
                        textLabel.Margin,
                        Is.EqualTo(new Thickness(0, 0, 8, 0)));
                });
            });
    }

    [Test]
    public void ProductXaml_DoesNotUseStandalonePropertyColons()
    {
        var solutionRoot = FindSolutionRoot();
        var offenders = ProductXamlFiles(solutionRoot)
            .SelectMany(path => StandaloneColonElements(path)
                .Select(element =>
                    $"{Path.GetRelativePath(solutionRoot, path)}: " +
                    element.Name.LocalName))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void ChineseUiLabels_DoNotEndWithDecorativeColons()
    {
        var languagePath = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Language_Cn.json");
        var offenders = File.ReadLines(languagePath)
            .Select(ParseLanguageLine)
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .Where(entry => !entry.Key.StartsWith(
                "Msg.",
                StringComparison.Ordinal))
            .Where(entry => EndsWithColon(entry.Value))
            .Select(entry => $"{entry.Key} = {entry.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    private static IEnumerable<string> ProductXamlFiles(string solutionRoot) =>
        ProductRoots.SelectMany(root => Directory.EnumerateFiles(
            Path.Combine(solutionRoot, root),
            "*.xaml",
            SearchOption.AllDirectories));

    private static IEnumerable<XElement> StandaloneColonElements(string path)
    {
        var document = XDocument.Load(path);
        return document.Descendants().Where(element =>
            element.Attributes()
                .Where(attribute =>
                    attribute.Name.LocalName is "Content" or "Header" or "Text")
                .Any(attribute => IsColon(attribute.Value)) ||
            element.Nodes()
                .OfType<XText>()
                .Any(text => IsColon(text.Value)));
    }

    private static bool IsColon(string value) =>
        value.Trim() is ":" or "：";

    private static bool EndsWithColon(string value) =>
        value.TrimEnd().EndsWith(':') || value.TrimEnd().EndsWith('：');

    private static KeyValuePair<string, string>? ParseLanguageLine(
        string line)
    {
        var match = LanguageEntryRegex().Match(line);
        return match.Success
            ? new KeyValuePair<string, string>(
                match.Groups["key"].Value,
                match.Groups["value"].Value)
            : null;
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AssetEditor.CN.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate the solution root.");
    }

    [GeneratedRegex(
        "^\\s*\\\"(?<key>[^\\\"]+)\\\"\\s*:\\s*\\\"(?<value>.*)\\\"\\s*,?\\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LanguageEntryRegex();
}
