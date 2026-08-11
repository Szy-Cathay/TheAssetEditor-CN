using System.Text.Json;

namespace Test.AnimatioReTarget
{
    public class AnimationRetargetEditorUiTests
    {
        [Test]
        public void EditorView_ExposesLocalizedOneClickAutoMappingSummary()
        {
            var repositoryRoot = FindRepositoryRoot();
            var editorXaml = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Editors",
                "AnimationReTarget",
                "Editors.AnimatioReTarget",
                "Editor",
                "EditorView.xaml"));
            var boneSettingsXaml = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Editors",
                "AnimationReTarget",
                "Editors.AnimatioReTarget",
                "Editor",
                "BoneHandling",
                "Presentation",
                "BoneSettingsView.xaml"));

            Assert.Multiple(() =>
            {
                Assert.That(editorXaml, Does.Contain("{loc:Loc AnimReTarget.AutoMapBones}"));
                Assert.That(editorXaml, Does.Contain("BoneManager.AutoMapBonesCommand"));
                Assert.That(editorXaml, Does.Contain("AeButton.Primary"));
                Assert.That(editorXaml, Does.Contain("AutomationProperties.Name"));
                Assert.That(editorXaml, Does.Contain("LastAutoMappingSummary.ConfirmedCount"));
                Assert.That(editorXaml, Does.Contain("LastAutoMappingSummary.ReviewRequiredCount"));
                Assert.That(editorXaml, Does.Contain("LastAutoMappingSummary.UnmatchedCount"));
                Assert.That(boneSettingsXaml, Does.Contain("AutoMappingStatusText"));
            });
        }

        [Test]
        public void ChineseLanguage_ContainsAutoMappingTexts()
        {
            var languagePath = Path.Combine(FindRepositoryRoot(), "AssetEditor", "Language_Cn.json");
            using var document = JsonDocument.Parse(File.ReadAllText(languagePath));
            var language = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(language.GetProperty("AnimReTarget.AutoMapBones").GetString(), Is.EqualTo("一键自动映射"));
                Assert.That(language.GetProperty("AnimReTarget.AutoMapSummary.Confirmed").GetString(), Is.EqualTo("已确认"));
                Assert.That(language.GetProperty("AnimReTarget.AutoMapSummary.ReviewRequired").GetString(), Is.EqualTo("待复核"));
                Assert.That(language.GetProperty("AnimReTarget.AutoMapSummary.Unmatched").GetString(), Is.EqualTo("未匹配"));
            });
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AssetEditor.CN.sln")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
}
