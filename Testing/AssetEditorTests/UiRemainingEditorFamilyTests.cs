using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class UiRemainingEditorFamilyTests
{
    private static readonly string[] ProductXamlPaths =
    [
        "AssetEditor/Views/Updater/UpdaterWindow.xaml",
        "Editors/CscEditor/Editors.CscEditor/Views/CscEditorView.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/DdsToMaterialPng/DdsToMaterialPngView.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/DdsToNormalPng/DdsToNormalPngView.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/DdsToPng/DdsToPngView.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/ExportWindow.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/RmvToGltf/RmvToGltfExporterView.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Importing/Presentation/GltfToRmv/RmvToGltfImporterView.xaml",
        "Editors/ImportExportEditor/Editors.ImportExport/Importing/Presentation/ImportWindow.xaml",
        "Editors/TextureEditor/Views/TextureInformationView.xaml",
        "Editors/TextureEditor/Views/TexturePreviewView.xaml",
        "Editors/TwuiEditor/Editor.Twui/Editor/ComponentEditor/ComponentView.xaml",
        "Editors/TwuiEditor/Editor.Twui/Editor/Presentation/HierarchyView.xaml",
        "Editors/TwuiEditor/Editor.Twui/Editor/Presentation/TwuiMainView.xaml",
        "Shared/SharedUI/BaseDialogs/AeAttribute.xaml",
        "Shared/SharedUI/BaseDialogs/AeAttribute2.xaml",
        "Shared/SharedUI/BaseDialogs/ColourPickerButton/ColourPickerButtonView.xaml",
        "Shared/SharedUI/BaseDialogs/ControllerHostWindow.xaml",
        "Shared/SharedUI/BaseDialogs/FilterDialog/CollapsableFilterControl.xaml",
        "Shared/SharedUI/BaseDialogs/FilterDialog/FilterUserControl.xaml",
        "Shared/SharedUI/BaseDialogs/MathViews/Matrix3x4View.xaml",
        "Shared/SharedUI/BaseDialogs/MathViews/Vector2View.xaml",
        "Shared/SharedUI/BaseDialogs/MathViews/Vector3View.xaml",
        "Shared/SharedUI/BaseDialogs/MathViews/Vector4View.xaml",
        "Shared/SharedUI/BaseDialogs/SelectionListDialog/SelectionListView.xaml",
        "Shared/SharedUI/BaseDialogs/SelectionListDialog/SelectionListWindow.xaml",
        "Shared/SharedUI/BaseDialogs/ToolSelector/ToolSelectorWindow.xaml",
    ];

    private static readonly Regex LegacyThemeResource = new(
        @"\{DynamicResource\s+(?:ABrush\.|ToolBarTrayBackground|GroupBox\.Header\.Static\.Background|TreeViewItem\.Selected\.Background|TreeView\.Static\.Background|\{x:Static\s+SystemColors\.)|AeFont\.",
        RegexOptions.CultureInvariant);

    private static readonly Regex HardcodedThemeColor = new(
        "(?:Background|Foreground|BorderBrush|Fill|Stroke)\\s*=\\s*\"(?:#|White\"|Black\"|Gray\"|LightGray\"|DarkGray\"|Red\"|Orange\"|OrangeRed\")",
        RegexOptions.CultureInvariant);

    [Test]
    public void RemainingEditorFamily_UsesSemanticThemeTypographyAndSharedControls()
    {
        var sources = ReadProductSources();
        var combined = string.Join(
            Environment.NewLine,
            sources.Append(ReadEditorWorkspaceStyleSource()));
        var themeSurfaceSource = combined.Replace(
            "<Rectangle Fill=\"Black\"/>",
            string.Empty,
            StringComparison.Ordinal);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sources.Count, Is.EqualTo(27));
            NUnitAssert.That(combined, Does.Contain("EditorWorkspaceStyles.xaml"));
            NUnitAssert.That(combined, Does.Contain("AeBrush."));
            NUnitAssert.That(combined, Does.Contain("AppFontFamily"));
            NUnitAssert.That(combined, Does.Contain("AppFontWeight"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(combined, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(combined, Does.Contain("AeTree.View"));
            NUnitAssert.That(combined, Does.Contain("AeTable.Grid"));
            NUnitAssert.That(
                ReadEditorWorkspaceStyleSource(),
                Does.Contain("TargetType=\"{x:Type ListView}\" BasedOn=\"{StaticResource {x:Type ListView}}\""));
            NUnitAssert.That(LegacyThemeResource.IsMatch(combined), Is.False);
            NUnitAssert.That(HardcodedThemeColor.IsMatch(themeSurfaceSource), Is.False);
        });
    }

    [Test]
    public void RemainingEditorFamily_PreservesInteractionContracts()
    {
        var combined = string.Join(Environment.NewLine, ReadProductSources());

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("Command=\"{Binding UpdateCommand}\""));
            NUnitAssert.That(combined, Does.Contain("PreviewMouseWheel=\"OnMarkdownScrollViewerPreviewMouseWheel\""));
            NUnitAssert.That(combined, Does.Contain("Click=\"Save_Click\""));
            NUnitAssert.That(combined, Does.Contain("CurveEditorControl"));
            NUnitAssert.That(combined, Does.Contain("Click=\"ExportButton_Click\""));
            NUnitAssert.That(combined, Does.Contain("Click=\"ImportButton_Click\""));
            NUnitAssert.That(combined, Does.Contain("HandleColourChangedCommand"));
            NUnitAssert.That(combined, Does.Contain("MouseDoubleClick=\"ItemsListView_MouseDoubleClick\""));
            NUnitAssert.That(combined, Does.Contain("Style=\"{StaticResource AeVerticalGridSplitterStyle}\""));
        });
    }

    private static IReadOnlyList<string> ReadProductSources()
    {
        var root = FindSolutionRoot();
        return ProductXamlPaths
            .Select(path => File.ReadAllText(Path.Combine(
                root,
                path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
    }

    private static string ReadEditorWorkspaceStyleSource() => File.ReadAllText(
        Path.Combine(
            FindSolutionRoot(),
            "Shared",
            "SharedUI",
            "Common",
            "Styles",
            "EditorWorkspaceStyles.xaml"));

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
}
