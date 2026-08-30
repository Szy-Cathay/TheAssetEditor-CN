using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
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
        "Editors/MetaDataEditor/AnimationMeta/SuperView/Inspection/MetaDataProblemListView.xaml",
        "Editors/MetaDataEditor/AnimationMeta/SuperView/Inspection/MetaDataTimelineView.xaml",
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
            NUnitAssert.That(sources.Count, Is.EqualTo(33));
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
            NUnitAssert.That(combined, Does.Contain("MethodBinding BrowseAnimation"));
        });
    }

    [Test]
    public void AnimPackTable_PreservesCompleteResizableColumnsAndShowsShortPaths()
    {
        var root = FindSolutionRoot();
        var table = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "AnimationFragmentEditor",
            "Editor.AnimationFragmentEditor",
            "AnimationPack",
            "Views",
            "AnimSetTableEditorView.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(table, Does.Contain("CanUserResizeColumns=\"True\""));
            NUnitAssert.That(table, Does.Contain("ColumnWidth=\"Auto\""));
            NUnitAssert.That(
                table,
                Does.Contain(
                    "ScrollViewer.HorizontalScrollBarVisibility=\"Auto\""));
            NUnitAssert.That(table, Does.Contain("AnimationFileName"));
            NUnitAssert.That(table, Does.Contain("ToolTip=\"{Binding AnimationFile}\""));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.Slot.ToolTip"));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.BlendIn.ToolTip"));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.Weight.ToolTip"));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.BlendIn"));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.Weight"));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.WeaponBone"));
            NUnitAssert.That(table, Does.Contain("AnimPack.Table.Unk"));
            NUnitAssert.That(table, Does.Contain("materialIcons:MaterialIcon"));
            NUnitAssert.That(table, Does.Not.Contain("&#x25B2;"));
            NUnitAssert.That(table, Does.Not.Contain("&#x25BC;"));
        });
    }

    [Test]
    public void AnimationRetargetEditor_UsesValidBindingsAndSharedWindowContract()
    {
        var root = FindSolutionRoot();
        var editorRoot = Path.Combine(
            root,
            "Editors",
            "AnimationReTarget",
            "Editors.AnimatioReTarget",
            "Editor");
        var selectedBoneView = File.ReadAllText(Path.Combine(
            editorRoot,
            "BoneHandling",
            "Presentation",
            "SelectedBoneView.xaml"));
        var boneSettingsView = File.ReadAllText(Path.Combine(
            editorRoot,
            "BoneHandling",
            "Presentation",
            "BoneSettingsView.xaml"));
        var sceneObjectView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "ReferenceModel",
            "SceneObjectView.xaml"));
        var editorSource = File.ReadAllText(Path.Combine(
            editorRoot,
            "AnimationRetargetEditor.cs"));
        var settingsView = File.ReadAllText(Path.Combine(
            editorRoot,
            "Settings",
            "SettingsView.xaml"));
        var saveWindow = File.ReadAllText(Path.Combine(
            editorRoot,
            "Saving",
            "SaveWindow.xaml"));
        var saveWindowCode = File.ReadAllText(Path.Combine(
            editorRoot,
            "Saving",
            "SaveWindow.xaml.cs"));
        var saveSettings = File.ReadAllText(Path.Combine(
            editorRoot,
            "Saving",
            "SaveSettings.cs"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                selectedBoneView,
                Does.Contain("DisplayMemberPath=\"BoneName\""));
            NUnitAssert.That(
                selectedBoneView,
                Does.Not.Contain("DisplayMemberPath=\"BoneName.Value\""));
            NUnitAssert.That(
                settingsView,
                Does.Contain("JustPositivDecimalInput=\"True\""));
            NUnitAssert.That(
                boneSettingsView,
                Does.Contain("AeVerticalGridSplitterStyle"));
            NUnitAssert.That(
                boneSettingsView,
                Does.Contain("Style=\"{StaticResource AeTree.View}\""));
            NUnitAssert.That(
                boneSettingsView,
                Does.Not.Contain("MaxHeight=\"{Binding ActualHeight"));
            NUnitAssert.That(
                sceneObjectView,
                Does.Contain("Visibility=\"{Binding ShowAnimationControls"));
            NUnitAssert.That(
                editorSource,
                Does.Contain("target.ShowAnimationControls = false"));
            NUnitAssert.That(
                boneSettingsView,
                Does.Contain("Height=\"Auto\" MaxHeight=\"520\""));
            NUnitAssert.That(
                boneSettingsView,
                Does.Match(
                    "<TreeView\\s+Grid.Row=\"1\"\\s+Grid.Column=\"0\"\\s+MaxHeight=\"520\""));
            NUnitAssert.That(
                saveSettings,
                Does.Match("PossibleAnimationFormats\\s*\\{\\s*get;"));
            NUnitAssert.That(
                saveWindow,
                Does.Contain("<common:AssetEditorWindow"));
            NUnitAssert.That(
                saveWindow,
                Does.Contain("IsChecked=\"{Binding UseGeneratedSkeleton"));
            NUnitAssert.That(
                saveWindow,
                Does.Not.Contain("UseScaledSkeletonName"));
            NUnitAssert.That(
                saveWindowCode,
                Does.Contain("SaveWindow : AssetEditorWindow"));
            NUnitAssert.That(
                saveWindow,
                Does.Contain("AnimReTarget.Batch.Path.SelectedFolderHint"));
            NUnitAssert.That(
                saveWindowCode,
                Does.Contain("!SaveManager.IsTechAnimation(animation)"));
            NUnitAssert.That(
                saveWindowCode,
                Does.Contain("_saveManager.GetEditableFolderProject()"));
            NUnitAssert.That(
                saveWindowCode,
                Does.Match("DisplayBrowseFolderDialog\\(\\s*outputProject\\)"));
            NUnitAssert.That(
                saveWindowCode,
                Does.Contain("AnimReTarget.Batch.Result.Path"));
            NUnitAssert.That(saveWindowCode, Does.Not.Contain(" → "));
        });
    }

    [Test]
    public void AnimationRetargetEditor_UsesChineseUserFacingMessages()
    {
        var root = FindSolutionRoot();
        var editorRoot = Path.Combine(
            root,
            "Editors",
            "AnimationReTarget",
            "Editors.AnimatioReTarget",
            "Editor");
        var editorSource = File.ReadAllText(Path.Combine(
            editorRoot,
            "AnimationRetargetEditor.cs"));
        var boneManagerSource = File.ReadAllText(Path.Combine(
            editorRoot,
            "BoneHandling",
            "BoneManager.cs"));
        var renderingSource = File.ReadAllText(Path.Combine(
            editorRoot,
            "Settings",
            "AnimationReTargetRenderingComponent.cs"));
        var saveManagerSource = File.ReadAllText(Path.Combine(
            editorRoot,
            "Saving",
            "SaveManager.cs"));
        using var language = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "AssetEditor",
            "Language_Cn.json")));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                editorSource,
                Does.Contain("AnimReTarget.Error.InvalidSpeedMultiplier"));
            NUnitAssert.That(
                boneManagerSource,
                Does.Contain("AnimReTarget.Error.SkeletonSelectionRequired"));
            NUnitAssert.That(
                renderingSource,
                Does.Contain("AnimReTarget.Scene.Generated"));
            NUnitAssert.That(
                saveManagerSource,
                Does.Contain("AnimReTarget.Error.GeneratedAnimationRequired"));
            NUnitAssert.That(
                language.RootElement
                    .GetProperty("AnimReTarget.Scene.Target")
                    .GetString(),
                Is.EqualTo("目标骨架"));
            NUnitAssert.That(
                language.RootElement
                    .GetProperty("AnimReTarget.Scene.Source")
                    .GetString(),
                Is.EqualTo("源骨架"));
            NUnitAssert.That(
                language.RootElement
                    .GetProperty("AnimReTarget.Scene.Generated")
                    .GetString(),
                Is.EqualTo("生成结果"));
            NUnitAssert.That(
                language.RootElement
                    .GetProperty("AnimReTarget.Error.InvalidSpeedMultiplier")
                    .GetString(),
                Is.EqualTo("动画速度倍率必须大于 0。"));
            NUnitAssert.That(
                language.RootElement
                    .GetProperty(
                        "AnimReTarget.Batch.Path.SelectedFolderHint")
                    .GetString(),
                Is.EqualTo(
                    "输出时只保留每个 .anim 所在的直接父文件夹，不判断文件夹名称，也不限制原路径层级。"));
            NUnitAssert.That(
                language.RootElement
                    .GetProperty("AnimReTarget.Batch.Result.Path")
                    .GetString(),
                Is.EqualTo("{0} 到 {1}"));
        });
    }

    [Test]
    public void AnimationRetargetWorldLabels_AreCoveredByWorldTextFont()
    {
        var root = FindSolutionRoot();
        using var language = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "AssetEditor",
            "Language_Cn.json")));
        var labels = new[]
        {
            "AnimReTarget.Scene.Target",
            "AnimReTarget.Scene.Source",
            "AnimReTarget.Scene.Generated",
        }
            .Select(key => language.RootElement.GetProperty(key).GetString()!)
            .ToArray();
        var font = XDocument.Load(
            Path.Combine(
                root,
                "GameWorld",
                "ContentProject",
                "Content",
                "Fonts",
                "DefaultFont.spritefont"),
            LoadOptions.PreserveWhitespace);
        var ranges = font
            .Descendants("CharacterRegion")
            .Select(region => (
                Start: region.Element("Start")!.Value.Single(),
                End: region.Element("End")!.Value.Single()))
            .ToArray();
        var missingCharacters = labels
            .SelectMany(label => label)
            .Distinct()
            .Where(character => ranges.All(range =>
                character < range.Start || character > range.End))
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                font.Descendants("FontName").Single().Value,
                Does.EndWith("HarmonyOS_Sans_SC_Regular.ttf"));
            NUnitAssert.That(missingCharacters, Is.Empty);
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
            NUnitAssert.That(
                sharedPlayer,
                Does.Contain("AeEditor.PlaybackSlider"));
            NUnitAssert.That(
                sharedPlayer,
                Does.Contain("Value=\"{Binding PlaybackPositionSeconds, Mode=TwoWay"));
            NUnitAssert.That(
                sharedPlayer,
                Does.Contain("x:Name=\"PlaybackCommandRow\""));
            NUnitAssert.That(
                sharedPlayer,
                Does.Contain("x:Name=\"PlaybackTimelineRow\""));
            NUnitAssert.That(sharedPlayer, Does.Not.Contain("<ItemsControl"));
            NUnitAssert.That(sharedPlayer, Does.Not.Contain("<ComboBox"));
            NUnitAssert.That(
                sharedPlayer,
                Does.Not.Contain("SelectedMainAnimation"));
            NUnitAssert.That(sharedPlayer, Does.Not.Contain("PlayerItems"));
            NUnitAssert.That(sharedPlayer, Does.Not.Contain("<ListBox"));
            NUnitAssert.That(sharedPlayer, Does.Not.Contain("Width=\".1*\""));
            NUnitAssert.That(sharedPlayer, Does.Not.Contain("Width=\".3*\""));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("AeEditor.PlaybackSlider"));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("Value=\"{Binding PlaybackPositionSeconds, Mode=TwoWay"));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("Maximum=\"{Binding MaxTimeSeconds, Mode=OneWay}\""));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("Text=\"{Binding MaxTimeSeconds, Mode=OneWay"));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("{loc:Loc SharedAnimPlayer.Seconds}"));
            NUnitAssert.That(
                kitbashPlayer,
                Does.Contain("{loc:Loc SharedAnimPlayer.Time}"));
            NUnitAssert.That(kitbashPlayer, Does.Not.Contain("Width=\"240\""));
            NUnitAssert.That(kitbashPlayer, Does.Contain("SelectedSkeleton"));
            NUnitAssert.That(kitbashPlayer, Does.Contain("SelectedAnimation"));
            NUnitAssert.That(cscPlayer, Does.Contain("AeBrush.Danger"));
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"AeEditor.PlaybackToggle\""));
            NUnitAssert.That(
                styles,
                Does.Contain("x:Key=\"AeEditor.PlaybackSlider\""));
            NUnitAssert.That(
                styles,
                Does.Not.Contain("Property=\"IsKeyboardFocused\""));
            NUnitAssert.That(
                styles,
                Does.Contain(
                    "<Setter Property=\"FocusVisualStyle\" Value=\"{StaticResource AeFocus.Keyboard}\" />"));
            NUnitAssert.That(styles, Does.Not.Contain("To=\"1.015\""));
            NUnitAssert.That(styles, Does.Not.Contain("To=\"0.94\""));
            NUnitAssert.That(styles, Does.Not.Contain("ButtonBase.Click"));
            NUnitAssert.That(
                styles,
                Does.Contain("AeMotion.ButtonPressStoryboard"));
            NUnitAssert.That(
                styles,
                Does.Contain("AeMotion.ButtonReleaseStoryboard"));
        });
    }

    [Test]
    public void CampaignAnimationActions_AlignWithSceneObjectFieldColumn()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "AnimationEditor",
            "CampaignAnimationCreator",
            "EditorView.xaml");
        var document = XDocument.Load(path);
        var layout = document.Root!.Elements().Single(element =>
            element.Name.LocalName == "Grid");
        var columns = layout.Elements()
            .Single(element =>
                element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .Select(element => element.Attribute("Width")?.Value)
            .ToArray();
        var rootBoneInput = layout.Elements().Single(element =>
            element.Name.LocalName == "ComboBox");
        var actions = layout.Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                layout.Attribute("Margin")?.Value,
                Is.EqualTo("32,0,8,0"));
            NUnitAssert.That(columns, Is.EqualTo(new[] { "140", "*" }));
            NUnitAssert.That(
                rootBoneInput.Attributes().Single(attribute =>
                    attribute.Name.LocalName == "Grid.Column").Value,
                Is.EqualTo("1"));
            NUnitAssert.That(actions, Has.Length.EqualTo(2));
            NUnitAssert.That(
                actions.All(action =>
                    action.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Grid.Column" &&
                        attribute.Value == "1")),
                Is.True);
            NUnitAssert.That(
                actions.Any(action => action.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Grid.ColumnSpan")),
                Is.False);
        });
    }

    [Test]
    public void MetadataSuperView_ExplainsMissingFilesAndRemovesDeadResetAction()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "SuperView",
            "EditorView.xaml");
        var source = File.ReadAllText(path) + Environment.NewLine +
            File.ReadAllText(Path.Combine(
                FindSolutionRoot(),
                "Editors",
                "MetaDataEditor",
                "AnimationMeta",
                "MetaEditor",
                "View",
                "MetaDataAttributeView.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(source, Does.Contain("AeFeedback.Notice"));
            NUnitAssert.That(source, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(
                source,
                Does.Contain("CanCreateAnimationMetaFile"));
            NUnitAssert.That(
                source,
                Does.Contain("CanCreatePersistentMetaFile"));
            NUnitAssert.That(
                source,
                Does.Contain("IsAnimationMetaReferenceMissing"));
            NUnitAssert.That(
                source,
                Does.Contain("IsPersistentMetaReferenceMissing"));
            NUnitAssert.That(
                source,
                Does.Contain("DataContext.HasPersistentMetaFile"));
            NUnitAssert.That(
                source,
                Does.Contain("DataContext.HasAnimationMetaFile"));
            NUnitAssert.That(source, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(source, Does.Contain("ShowImpactPositions"));
            NUnitAssert.That(source, Does.Contain("ShowTargetPositions"));
            NUnitAssert.That(source, Does.Contain("ShowFirePositions"));
            NUnitAssert.That(source, Does.Contain("ShowSplashAttacks"));
            NUnitAssert.That(source, Does.Contain("CanFocusSelectedMetaData"));
            NUnitAssert.That(source, Does.Contain("FocusSelectedMetaDataAction"));
            NUnitAssert.That(source, Does.Contain("SuperView.ShowImpactPos"));
            NUnitAssert.That(source, Does.Contain("SuperView.ShowTargetPos"));
            NUnitAssert.That(source, Does.Contain("SuperView.ShowFirePos"));
            NUnitAssert.That(source, Does.Contain("SuperView.ShowSplashAttack"));
            NUnitAssert.That(
                source,
                Does.Contain("ShowCombatMetaDataDuringActiveTime"));
            NUnitAssert.That(
                source,
                Does.Contain("ShowCombatMetaDataForEntireAnimation"));
            NUnitAssert.That(
                source,
                Does.Contain("SuperView.CombatDisplayMode.ActiveTime"));
            NUnitAssert.That(
                source,
                Does.Contain("SuperView.CombatDisplayMode.EntireAnimation"));
            NUnitAssert.That(source, Does.Contain("SuperView.FocusSelectedMeta"));
            NUnitAssert.That(source, Does.Contain("AeInput.RadioButton"));
            NUnitAssert.That(source, Does.Contain("CanEditSelectedMetaData3D"));
            NUnitAssert.That(source, Does.Contain("IsSplashMetaDataSelected"));
            NUnitAssert.That(source, Does.Contain("UndoCombatMetaDataAction"));
            NUnitAssert.That(source, Does.Contain("RedoCombatMetaDataAction"));
            NUnitAssert.That(source, Does.Contain("SuperView.Edit3D.SplashStart"));
            NUnitAssert.That(source, Does.Contain("SuperView.Edit3D.SplashEnd"));
            NUnitAssert.That(source, Does.Contain("HasSelectedMetaDataTimeRange"));
            NUnitAssert.That(source, Does.Contain("JumpToSelectedMetaDataStartAction"));
            NUnitAssert.That(source, Does.Contain("JumpToSelectedMetaDataEndAction"));
            NUnitAssert.That(source, Does.Contain("SelectedMetaDataStartToolTip"));
            NUnitAssert.That(source, Does.Not.Contain("SelectedMetaDataZeroRangeHint, RelativeSource"));
            NUnitAssert.That(source, Does.Not.Contain("SuperView.EffectPreview.LocatorMode"));
            NUnitAssert.That(source, Does.Not.Contain("General.Reset"));
            NUnitAssert.That(source, Does.Not.Contain("RefreshAction"));
        });
    }

    [Test]
    public void MetadataSuperView_UsesContextualPropertyLayoutAndIndependentScrollers()
    {
        var root = FindSolutionRoot();
        var superView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "SuperView",
            "EditorView.xaml"));
        var entryView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "MetaEditor",
            "View",
            "MetaDataEntryView.xaml"));
        var attributeView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "MetaEditor",
            "View",
            "MetaDataAttributeView.xaml"));
        var editorHostView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "BaseControl",
            "EditorHostView.xaml"));
        var superViewViewModel = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "SuperView",
            "SuperViewViewModel.cs"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                superView,
                Does.Not.Contain("SuperView.PreviewVisibility"));
            NUnitAssert.That(
                superView,
                Does.Not.Contain("SuperView.Edit3D.Title"));
            NUnitAssert.That(
                superView,
                Does.Not.Contain("SuperView.Time.Title"));
            NUnitAssert.That(
                entryView,
                Does.Not.Contain("MetaData.HeaderLeft"));
            NUnitAssert.That(
                entryView,
                Does.Contain("ScrollViewer.VerticalScrollBarVisibility=\"Auto\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("ScrollViewer.VerticalScrollBarVisibility=\"Auto\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("SuperView.PreviewVisibility"));
            NUnitAssert.That(
                attributeView,
                Does.Contain("IsCombatMetaData3dEditingEnabled"));
            NUnitAssert.That(
                attributeView,
                Does.Contain("IsCombatPositionAnchor"));
            NUnitAssert.That(
                attributeView,
                Does.Contain("JumpToSelectedMetaDataStartActionCommand"));
            NUnitAssert.That(
                attributeView,
                Does.Contain("JumpToSelectedMetaDataEndActionCommand"));
            NUnitAssert.That(
                editorHostView,
                Does.Contain(
                    "VerticalScrollBarVisibility=\"{Binding EditorContentVerticalScrollBarVisibility}\""));
            NUnitAssert.That(
                superViewViewModel,
                Does.Contain("ScrollBarVisibility.Disabled"));
            NUnitAssert.That(
                attributeView,
                Does.Contain("HorizontalAlignment=\"Left\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("MinWidth=\"300\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("Style=\"{StaticResource AeForm.Label}\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("EditEffectOrientation"));
        });
    }

    [Test]
    public void MetadataSuperView_TimelineAnnotationUsesGenericPlayerSlot()
    {
        var root = FindSolutionRoot();
        var player = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "Shared",
            "Editors.Shared.Core",
            "Common",
            "AnimationPlayer",
            "AnimationPlayerView.xaml"));
        var timeline = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "SuperView",
            "Inspection",
            "MetaDataTimelineView.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                player,
                Does.Contain("TimelineAnnotationContent"));
            NUnitAssert.That(player, Does.Not.Contain("IsSuperView"));
            NUnitAssert.That(player, Does.Not.Contain("MetaDataTimeline"));
            NUnitAssert.That(timeline, Does.Contain("SelectCommand"));
            NUnitAssert.That(
                timeline,
                Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(timeline, Does.Contain("ToolTipText"));
            NUnitAssert.That(
                timeline,
                Does.Contain("BasedOn=\"{StaticResource AeButton.Base}\""));
            NUnitAssert.That(
                timeline,
                Does.Contain("AncestorType=Button"));
            NUnitAssert.That(
                timeline,
                Does.Not.Contain("AeBrush.SurfaceHover"));
            NUnitAssert.That(
                timeline,
                Does.Not.Contain("Property=\"IsKeyboardFocused\""));
            NUnitAssert.That(
                timeline,
                Does.Not.Contain("Property=\"Opacity\""));
            NUnitAssert.That(
                timeline,
                Does.Contain("MetaDataTimelineMarkerKind.Instant"));
            NUnitAssert.That(
                timeline,
                Does.Contain("MetaDataTimelineMarkerKind.Range"));
            NUnitAssert.That(
                timeline,
                Does.Contain("MetaDataTimelineMarkerKind.WholeAnimation"));
        });
    }

    [Test]
    public void MetadataSuperView_ProblemListReusesSharedFeedbackAndListStates()
    {
        var root = FindSolutionRoot();
        var problemList = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "SuperView",
            "Inspection",
            "MetaDataProblemListView.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(problemList, Does.Contain("AeList.View"));
            NUnitAssert.That(problemList, Does.Contain("AeList.Item"));
            NUnitAssert.That(problemList, Does.Contain("AeFeedback.Icon"));
            NUnitAssert.That(problemList, Does.Contain("AeEmptyState.Panel"));
            NUnitAssert.That(problemList, Does.Contain("SelectedProblem"));
            NUnitAssert.That(
                problemList,
                Does.Contain("AutomationProperties.Name"));
            NUnitAssert.That(problemList, Does.Contain("ToolTipText"));
        });
    }

    [Test]
    public void MetadataPreviewControls_AreExplicitlyScopedToSuperViewHost()
    {
        var root = FindSolutionRoot();
        var attributeView = File.ReadAllText(Path.Combine(
            root,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "MetaEditor",
            "View",
            "MetaDataAttributeView.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                attributeView,
                Does.Contain("x:Name=\"SuperViewBatchControls\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("x:Name=\"SuperViewSelectedMetaDataControls\""));
            NUnitAssert.That(
                attributeView,
                Does.Contain("<Setter Property=\"Visibility\" Value=\"Collapsed\" />"));
            NUnitAssert.That(
                attributeView,
                Does.Not.Contain("Visibility=\"{Binding DataContext.IsAnimationMetaTabSelected"));
            NUnitAssert.That(
                attributeView,
                Does.Not.Contain("Visibility=\"{Binding DataContext.HasSelectedSceneMarkerSettings"));
            NUnitAssert.That(
                attributeView,
                Does.Contain("EnableAllAnimationMeta3D"));
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
