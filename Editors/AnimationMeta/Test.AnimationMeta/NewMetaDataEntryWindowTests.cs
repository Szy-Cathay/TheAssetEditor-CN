using System.IO;
using System.Text.Json;
using Editors.AnimationMeta.MetaEditor.Commands;
using Editors.AnimationMeta.Presentation.View;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

[TestFixture]
internal class NewMetaDataEntryWindowTests
{
    [Test]
    public void Search_FuzzyMatchesTagNameAndChineseDescription()
    {
        var model = new NewTagWindowViewModel();
        model.SetItems(
        [
            new("SPLASH_ATTACK_10", "触发溅射攻击并标记影响范围"),
            new("ANIMATED_PROP_14", "显示动画道具模型"),
            new("EFFECT_12", "触发粒子特效"),
        ]);

        model.SearchText = "spl atk";
        Assert.That(
            model.Items.Select(item => item.Name),
            Is.EqualTo(new[] { "SPLASH_ATTACK_10" }));

        model.SearchText = "动画 道具";
        Assert.That(
            model.Items.Select(item => item.Name),
            Is.EqualTo(new[] { "ANIMATED_PROP_14" }));
    }

    [Test]
    public void EverySupportedTag_HasLocalizedHoverDescription()
    {
        var solutionRoot = FindSolutionRoot();
        var languagePath = Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Language_Cn.json");
        var language = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(languagePath))!;
        var database = new MetaDataDatabase();

        var missing = database.GetSupportedTypes()
            .Select(tag => tag[..tag.LastIndexOf('_')])
            .Distinct(StringComparer.Ordinal)
            .Where(tag =>
                !language.TryGetValue(
                    $"MetaData.TagDesc.{tag}",
                    out var description) ||
                string.IsNullOrWhiteSpace(description))
            .ToArray();

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void Window_UsesUnifiedSearchAndDescriptionTooltips()
    {
        var solutionRoot = FindSolutionRoot();
        var xaml = File.ReadAllText(Path.Combine(
            solutionRoot,
            "Editors",
            "MetaDataEditor",
            "AnimationMeta",
            "MetaEditor",
            "View",
            "NewMetaDataEntryWindow.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("AssetEditorWindow"));
            Assert.That(xaml, Does.Contain("AeInput.TextBox"));
            Assert.That(xaml, Does.Contain("SearchText"));
            Assert.That(xaml, Does.Contain("ToolTip=\"{Binding Description}\""));
            Assert.That(xaml, Does.Contain("ItemsSource=\"{Binding GroupedItems}\""));
            Assert.That(xaml, Does.Contain("ListBox.GroupStyle"));
            Assert.That(xaml, Does.Contain("AeBrush.Border"));
        });
    }

    [Test]
    public void Warhammer3Catalog_OnlyShowsVerifiedGameTags()
    {
        var database = new MetaDataDatabase();

        var result = AnimationMetaTagCatalog.FilterForGame(
                GameTypeEnum.Warhammer3,
                database.GetSupportedTypes())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Length.EqualTo(99));
            Assert.That(result, Does.Contain("SPLASH_ATTACK_10"));
            Assert.That(result, Does.Contain("ANIMATED_PROP_15"));
            Assert.That(result, Does.Not.Contain("SPLASH_ATTACK_11"));
            Assert.That(result, Does.Not.Contain("DOCK_EQPT_RHAND_14"));
        });
    }

    [Test]
    public void EveryWarhammer3Tag_HasExactlyOnePurposeCategory()
    {
        var database = new MetaDataDatabase();
        var result = AnimationMetaTagCatalog.FilterForGame(
                GameTypeEnum.Warhammer3,
                database.GetSupportedTypes())
            .Select(AnimationMetaTagCatalog.GetCategoryKey)
            .ToArray();

        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Has.All.StartsWith("MetaData.NewEntryCategory."));
        Assert.That(result.Distinct().Count(), Is.EqualTo(7));
    }

    [Test]
    public void ExplicitGameOnlyDefinitions_AreHiddenFromOtherGames()
    {
        var database = new MetaDataDatabase();
        var definitions = database.GetSupportedTypes();

        var warhammer2 = AnimationMetaTagCatalog.FilterForGame(
                GameTypeEnum.Warhammer2,
                definitions,
                database.GetDefinition)
            .ToArray();
        var troy = AnimationMetaTagCatalog.FilterForGame(
                GameTypeEnum.Troy,
                definitions,
                database.GetDefinition)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                warhammer2,
                Does.Not.Contain("DOCK_EQPT_RHAND_14"));
            Assert.That(troy, Does.Contain("DOCK_EQPT_RHAND_14"));
        });
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AssetEditor.CN.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate solution root.");
    }
}
