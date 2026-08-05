using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class UiAnimationMetadataFamilyTests
{
    private static readonly string[] ProductXamlPaths =
    [
        "Editors/AnimationEditor/AnimationKeyframeEditor/EditorView.xaml",
        "Editors/AnimationEditor/CampaignAnimationCreator/EditorView.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/EditorView.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/Views/AnimationSettingsView.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/Views/BatchProcessOptionsWindow.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/Views/MountLinkSubView.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/Views/RiderAttachmentSubView.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/Views/SaveAndPreviewView.xaml",
        "Editors/AnimationEditor/MountAnimationCreator/Views/VisualisationHelperSubView.xaml",
        "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationBatchExporter/AnimationBatchExportView.xaml",
        "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/AnimationPackView.xaml",
        "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/Views/AnimSetTableEditorView.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/AnimationSettingsView.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/BoneHandling/Presentation/BoneMappingWindow.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/BoneHandling/Presentation/BoneSettingsView.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/BoneHandling/Presentation/SelectedBoneView.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/EditorView.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/Saving/SaveWindow.xaml",
        "Editors/AnimationReTarget/Editors.AnimatioReTarget/Editor/Settings/SettingsView.xaml",
        "Editors/MetaDataEditor/AnimationMeta/MetaEditor/View/MainEditorView.xaml",
        "Editors/MetaDataEditor/AnimationMeta/MetaEditor/View/MetaDataAttributeView.xaml",
        "Editors/MetaDataEditor/AnimationMeta/MetaEditor/View/MetaDataEntryView.xaml",
        "Editors/MetaDataEditor/AnimationMeta/MetaEditor/View/NewMetaDataEntryWindow.xaml",
        "Editors/MetaDataEditor/AnimationMeta/SuperView/EditorView.xaml",
        "Editors/Shared/Editors.Shared.Core/Common/AnimationPlayer/AnimationPlayerView.xaml",
        "Editors/Shared/Editors.Shared.Core/Common/BaseControl/EditorHostView.xaml",
        "Editors/Shared/Editors.Shared.Core/Common/ReferenceModel/SceneObjectView.xaml",
        "Editors/Shared/Editors.Shared.Core/Editors/BoneMapping/View/BoneMappingMetaDataView.xaml",
        "Editors/Shared/Editors.Shared.Core/Editors/BoneMapping/View/BoneMappingView.xaml",
        "Editors/Shared/Editors.Shared.Core/Editors/BoneMapping/View/BoneMappingWindow.xaml",
        "Editors/Shared/Editors.Shared.Core/Editors/TextEditor/TextEditorView.xaml",
    ];

    private static readonly Regex LegacyThemeResource = new(
        @"\{DynamicResource\s+(?:ABrush\.|ToolBarTrayBackground|GroupBox\.Header\.Static\.Background|TreeViewItem\.Selected\.Background|TreeView\.Static\.Background|\{x:Static\s+SystemColors\.)",
        RegexOptions.CultureInvariant);

    private static readonly Regex HardcodedThemeColor = new(
        "(?:Background|Foreground|BorderBrush|Fill|Stroke)\\s*=\\s*\"(?:#|White\"|Black\"|Gray\"|LightGray\"|DarkGray\"|Red\"|OrangeRed\")",
        RegexOptions.CultureInvariant);

    [Test]
    public void AnimationMetadataFamily_UsesSemanticThemeAndTypographyResources()
    {
        var sources = ReadProductSources();
        var combined = string.Join(Environment.NewLine, sources);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sources.Count, Is.EqualTo(31));
            NUnitAssert.That(combined, Does.Contain("AeBrush."));
            NUnitAssert.That(combined, Does.Contain("AppFontFamily"));
            NUnitAssert.That(combined, Does.Contain("AppFontWeight"));
            NUnitAssert.That(LegacyThemeResource.IsMatch(combined), Is.False);
            NUnitAssert.That(HardcodedThemeColor.IsMatch(combined), Is.False);
        });
    }

    [Test]
    public void AnimationMetadataFamily_UsesSharedInteractiveControlFamilies()
    {
        var combined = string.Join(
            Environment.NewLine,
            ReadProductSources().Append(ReadEditorWorkspaceStyleSource()));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("EditorWorkspaceStyles.xaml"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Quiet"));
            NUnitAssert.That(combined, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(combined, Does.Contain("AeInput.ComboBox"));
            NUnitAssert.That(combined, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(combined, Does.Contain("AeTree.View"));
            NUnitAssert.That(combined, Does.Contain("AeList.View"));
            NUnitAssert.That(combined, Does.Contain("AeTable.Grid"));
            NUnitAssert.That(combined, Does.Contain("AeMenu.Context"));
        });
    }

    [Test]
    public void AnimationMetadataFamily_PreservesEditingAndPreviewContracts()
    {
        var combined = string.Join(Environment.NewLine, ReadProductSources());

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("MethodBinding SaveAs"));
            NUnitAssert.That(combined, Does.Contain("PreviewMouseWheel"));
            NUnitAssert.That(combined, Does.Contain("BeginningEdit"));
            NUnitAssert.That(combined, Does.Contain("Key=\"Z\""));
            NUnitAssert.That(combined, Does.Contain("Key=\"Delete\""));
            NUnitAssert.That(combined, Does.Contain("ContentTemplateSelector"));
            NUnitAssert.That(combined, Does.Contain("AvalonEditBehaviour"));
            NUnitAssert.That(combined, Does.Contain("MouseDoubleClick"));
            NUnitAssert.That(combined, Does.Contain("BindableSelectedItemBehavior"));
        });
    }

    [Test]
    public void AnimationPlayers_ExposePersistentPlayingStateWithoutMouseFocusShift()
    {
        var root = FindSolutionRoot();
        var sharedPlayer = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "AnimationPlayer",
            "AnimationPlayerView.xaml"));
        var kitbashPlayer = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Kitbashing",
            "KitbasherEditor",
            "Core",
            "Animation",
            "AnimationView.xaml"));
        var cscPlayer = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "CscEditor",
            "Editors.CscEditor",
            "Views",
            "CscEditorView.xaml"));
        var styles = ReadEditorWorkspaceStyleSource();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                sharedPlayer,
                Does.Contain("AeEditor.PlaybackToggle"));
            NUnitAssert.That(
                sharedPlayer,
                Does.Contain("IsChecked=\"{Binding IsPlaying.Value"));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("AeEditor.PlaybackToggle"));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("IsChecked=\"{Binding IsPlaying"));
            NUnitAssert.That(cscPlayer, Does.Contain("AeBrush.Danger"));
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"AeEditor.PlaybackToggle\""));
            NUnitAssert.That(
                styles,
                Does.Not.Contain("Property=\"IsKeyboardFocused\""));
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
