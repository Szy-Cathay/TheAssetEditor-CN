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
    public void FormCheckBoxStyle_LeftAlignsControlsInTheSharedControlColumn()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var style = (Style)Application.Current.FindResource(
                    "AeInput.CheckBox");
                var checkBox = new CheckBox { Style = style };

                NUnitAssert.That(
                    checkBox.HorizontalAlignment,
                    Is.EqualTo(HorizontalAlignment.Left));
            });
    }

    [Test]
    public void ProductXaml_DoesNotRightAlignOrHideCheckBoxesForSpacing()
    {
        var solutionRoot = FindSolutionRoot();
        var offenders = ProductXamlFiles(solutionRoot)
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "CheckBox")
                .Where(element =>
                    HasAttribute(element, "HorizontalAlignment", "Right") ||
                    HasAttribute(element, "Visibility", "Hidden"))
                .Select(element =>
                    $"{Path.GetRelativePath(solutionRoot, path)}: " +
                    string.Join(
                        ", ",
                        element.Attributes()
                            .Where(attribute =>
                                attribute.Name.LocalName is
                                    "HorizontalAlignment" or "Visibility")
                            .Select(attribute =>
                                $"{attribute.Name.LocalName}=" +
                                attribute.Value))))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void SceneObjectResourceVisibility_UsesRowEndEyeToggles()
    {
        var solutionRoot = FindSolutionRoot();
        var path = Path.Combine(
            solutionRoot,
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "ReferenceModel",
            "SceneObjectView.xaml");
        var document = XDocument.Load(path);

        foreach (var bindingPath in new[]
                 {
                     "Data.ShowMesh.Value",
                     "Data.ShowSkeleton.Value",
                 })
        {
            var control = document.Descendants().Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "IsChecked" &&
                    attribute.Value.Contains(
                        bindingPath,
                        StringComparison.Ordinal)));

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    control.Name.LocalName,
                    Is.EqualTo("ToggleButton"));
                NUnitAssert.That(
                    control.Attributes().Single(attribute =>
                        attribute.Name.LocalName == "Style").Value,
                    Does.Contain("AeButton.VisibilityToggle"));
            });
        }

        NUnitAssert.That(
            document.Descendants()
                .Where(element => element.Name.LocalName == "CheckBox")
                .Any(element => element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "IsChecked" &&
                    attribute.Value.Contains(
                        "IsVisible",
                        StringComparison.Ordinal))),
            Is.False);
    }

    [Test]
    public void SceneObjectSkeletonPath_UsesAReadOnlyExpandableInputField()
    {
        var solutionRoot = FindSolutionRoot();
        var path = Path.Combine(
            solutionRoot,
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "ReferenceModel",
            "SceneObjectView.xaml");
        var document = XDocument.Load(path);
        var skeletonPathInput = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBox" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Text" &&
                attribute.Value.Contains(
                    "Data.SkeletonName.Value",
                    StringComparison.Ordinal)));
        var expander = skeletonPathInput.Ancestors().Single(element =>
            element.Name.LocalName == "Expander" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Style" &&
                attribute.Value.Contains(
                    "AeInput.ExpandableField",
                    StringComparison.Ordinal)));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                skeletonPathInput.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "IsReadOnly").Value,
                Is.EqualTo("True").IgnoreCase);
            NUnitAssert.That(
                expander.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "Style").Value,
                Does.Contain("AeInput.ExpandableField"));
        });
    }

    [Test]
    public void SceneObjectHeaders_DoNotAppendDecorativeColons()
    {
        var builderPath = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "SceneObjectViewModelBuilder.cs");

        NUnitAssert.That(
            File.ReadAllText(builderPath),
            Does.Not.Contain("header + \":\""));
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
    public void KitbashMaterialSubsectionHeaders_DoNotDrawTrailingSeparators()
    {
        var solutionRoot = FindSolutionRoot();
        var materialRoot = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "Nodes",
            "MeshNode",
            "Mesh.Material");
        var offenders = Directory.EnumerateFiles(
                materialRoot,
                "*.xaml",
                SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "Separator")
                .Where(element => element.Ancestors().Any(ancestor =>
                    ancestor.Name.LocalName == "Expander.Header"))
                .Select(_ => Path.GetRelativePath(solutionRoot, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void KitbashMaterialSubsections_AreInsetFromTheParentCategory()
    {
        var solutionRoot = FindSolutionRoot();
        var materialRoot = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "Nodes",
            "MeshNode",
            "Mesh.Material");
        var offenders = Directory.EnumerateFiles(
                materialRoot,
                "*View.xaml",
                SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "ModelMaterialView.xaml",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                Expander = XDocument.Load(path)
                    .Descendants()
                    .SingleOrDefault(element =>
                        element.Name.LocalName == "Expander"),
            })
            .Where(item =>
                item.Expander is null ||
                !item.Expander.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Margin" &&
                    attribute.Value.Contains(
                        "MaterialSubsectionMargin",
                        StringComparison.Ordinal)))
            .Select(item => Path.GetRelativePath(solutionRoot, item.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void KitbashMaterialSubsectionHeaders_AreTransparentAndBorderless()
    {
        var solutionRoot = FindSolutionRoot();
        var materialRoot = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "Nodes",
            "MeshNode",
            "Mesh.Material");
        var offenders = Directory.EnumerateFiles(
                materialRoot,
                "*View.xaml",
                SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "ModelMaterialView.xaml",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                Expander = XDocument.Load(path)
                    .Descendants()
                    .Single(element =>
                        element.Name.LocalName == "Expander"),
            })
            .Where(item =>
                !HasAttribute(item.Expander, "Background", "Transparent") ||
                !HasAttribute(item.Expander, "BorderThickness", "0") ||
                item.Expander.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "Expander.Header" ||
                        element.Ancestors().Any(ancestor =>
                            ancestor.Name.LocalName == "Expander.Header"))
                    .SelectMany(element => element.Attributes())
                    .Any(attribute =>
                        attribute.Name.LocalName == "Background"))
            .Select(item => Path.GetRelativePath(solutionRoot, item.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void KitbashMaterialSubsectionHeaders_MatchTheParentFontSizeWithoutBold()
    {
        var solutionRoot = FindSolutionRoot();
        var materialRoot = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "Nodes",
            "MeshNode",
            "Mesh.Material");
        var offenders = Directory.EnumerateFiles(
                materialRoot,
                "*View.xaml",
                SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "ModelMaterialView.xaml",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                Title = XDocument.Load(path)
                    .Descendants()
                    .Where(element =>
                        element.Ancestors().Any(ancestor =>
                            ancestor.Name.LocalName == "Expander.Header"))
                    .SingleOrDefault(element =>
                        element.Name.LocalName == "TextBlock"),
            })
            .Where(item =>
                item.Title is null ||
                item.Title.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "FontSize") ||
                !HasAttribute(item.Title, "FontWeight", "Normal"))
            .Select(item => Path.GetRelativePath(solutionRoot, item.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void KitbashFactionColourPreview_UsesStandardTransparentFormRows()
    {
        var solutionRoot = FindSolutionRoot();
        var path = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "Nodes",
            "MeshNode",
            "Mesh.Material",
            "Tint",
            "TintView.xaml");
        var document = XDocument.Load(path);
        var formLabels = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "Label" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Content" &&
                    attribute.Value.Contains("Tint.", StringComparison.Ordinal)))
            .ToArray();
        var colourPickers = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "ColourPickerButtonView")
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                document.Descendants().Any(element =>
                    element.Name.LocalName == "AeAttribute"),
                Is.False);
            NUnitAssert.That(formLabels, Has.Length.EqualTo(4));
            NUnitAssert.That(
                formLabels.All(label =>
                    label.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Style" &&
                        attribute.Value.Contains(
                            "AeForm.Label",
                            StringComparison.Ordinal))),
                Is.True);
            NUnitAssert.That(colourPickers, Has.Length.EqualTo(3));
            NUnitAssert.That(
                colourPickers.All(picker =>
                    HasAttribute(picker, "Background", "Transparent")),
                Is.True);
        });
    }

    [Test]
    public void KitbashXaml_DoesNotUseLegacyAttributeRows()
    {
        var solutionRoot = FindSolutionRoot();
        var kitbashRoot = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor");
        var offenders = Directory.EnumerateFiles(
                kitbashRoot,
                "*.xaml",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Document = XDocument.Load(path),
            })
            .Where(item => item.Document.Descendants().Any(element =>
                element.Name.LocalName is "AeAttribute" or "AutoAeAttribute"))
            .Select(item => Path.GetRelativePath(solutionRoot, item.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void SharedPropertyEditors_DoNotPaintLegacyCanvasBackgrounds()
    {
        var solutionRoot = FindSolutionRoot();
        var paths = new[]
        {
            "Shared/SharedUI/BaseDialogs/ColourPickerButton/ColourPickerButtonView.xaml",
            "Shared/SharedUI/BaseDialogs/FilterDialog/CollapsableFilterControl.xaml",
            "Shared/SharedUI/BaseDialogs/MathViews/Vector2View.xaml",
            "Shared/SharedUI/BaseDialogs/MathViews/Vector3View.xaml",
            "Shared/SharedUI/BaseDialogs/MathViews/Vector4View.xaml",
        };
        var offenders = paths
            .Where(path =>
            {
                var document = XDocument.Load(Path.Combine(
                    solutionRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                return !HasAttribute(
                    document.Root!,
                    "Background",
                    "Transparent");
            })
            .ToArray();

        NUnitAssert.That(offenders, Is.Empty);
    }

    [Test]
    public void KitbashMaterialType_UsesTheSharedPropertyRowLayout()
    {
        var solutionRoot = FindSolutionRoot();
        var path = Path.Combine(
            solutionRoot,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "SceneNodeEditor",
            "Nodes",
            "MeshNode",
            "Mesh.Material",
            "ModelMaterialView.xaml");
        var document = XDocument.Load(path);
        var materialTypeInput = document.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "SelectedItem" &&
                attribute.Value.Contains(
                    "CurrentMaterialType",
                    StringComparison.Ordinal)));
        var row = materialTypeInput.Parent!;
        var label = row.Elements().Single(element =>
            element.Name.LocalName == "Label");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(row.Name.LocalName, Is.EqualTo("DockPanel"));
            NUnitAssert.That(
                label.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "Style").Value,
                Does.Contain("AeForm.Label"));
            NUnitAssert.That(
                label.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "Width").Value,
                Is.EqualTo("120"));
            NUnitAssert.That(
                label.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "DockPanel.Dock").Value,
                Is.EqualTo("Left"));
            NUnitAssert.That(
                materialTypeInput.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Margin"),
                Is.False);
        });
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

    private static bool HasAttribute(
        XElement element,
        string name,
        string value) =>
        element.Attributes().Any(attribute =>
            attribute.Name.LocalName == name &&
            string.Equals(
                attribute.Value,
                value,
                StringComparison.OrdinalIgnoreCase));

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
