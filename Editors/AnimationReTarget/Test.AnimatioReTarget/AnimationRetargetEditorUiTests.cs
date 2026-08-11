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
            var reviewXaml = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Editors",
                "AnimationReTarget",
                "Editors.AnimatioReTarget",
                "Editor",
                "BoneHandling",
                "Presentation",
                "BoneMappingReviewView.xaml"));

            Assert.Multiple(() =>
            {
                Assert.That(editorXaml, Does.Contain("{loc:Loc AnimReTarget.AutoMapBones}"));
                Assert.That(editorXaml, Does.Contain("BoneManager.AutoMapBonesCommand"));
                Assert.That(editorXaml, Does.Contain("AeButton.Primary"));
                Assert.That(editorXaml, Does.Contain("AutomationProperties.Name"));
                Assert.That(reviewXaml, Does.Contain("LastAutoMappingSummary.ConfirmedCount"));
                Assert.That(reviewXaml, Does.Contain("LastAutoMappingSummary.ReviewRequiredCount"));
                Assert.That(reviewXaml, Does.Contain("LastAutoMappingSummary.UnmatchedCount"));
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

        [Test]
        public void BoneReviewView_ShowsOnlyProblemsAndExposesAllCorrectionActions()
        {
            var repositoryRoot = FindRepositoryRoot();
            var reviewXaml = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Editors",
                "AnimationReTarget",
                "Editors.AnimatioReTarget",
                "Editor",
                "BoneHandling",
                "Presentation",
                "BoneMappingReviewView.xaml"));
            var mappingXaml = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Editors",
                "Shared",
                "Editors.Shared.Core",
                "Editors",
                "BoneMapping",
                "View",
                "BoneMappingView.xaml"));
            var managerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Editors",
                "AnimationReTarget",
                "Editors.AnimatioReTarget",
                "Editor",
                "BoneHandling",
                "BoneManager.cs"));

            Assert.Multiple(() =>
            {
                Assert.That(reviewXaml, Does.Contain("BoneManager.ReviewItems"));
                Assert.That(reviewXaml, Does.Not.Contain("BoneManager.FlatBoneList"));
                Assert.That(reviewXaml, Does.Contain("BoneManager.ConfirmCandidateCommand"));
                Assert.That(reviewXaml, Does.Contain("BoneManager.ShowManualBoneMappingCommand"));
                Assert.That(reviewXaml, Does.Contain("BoneManager.MarkIntentionalUnmappedCommand"));
                Assert.That(reviewXaml, Does.Contain("LastAutoMappingSummary.IntentionalUnmappedCount"));
                Assert.That(reviewXaml, Does.Contain("BoneManager.BatchRetargetGateText"));
                Assert.That(reviewXaml, Does.Contain("AeList.View"));
                Assert.That(reviewXaml, Does.Contain("AeEmptyState.Panel"));
                Assert.That(reviewXaml, Does.Contain("AeFeedback.Notice"));
                Assert.That(reviewXaml, Does.Contain("AutomationProperties.Name"));
                Assert.That(reviewXaml, Does.Contain("KeyboardNavigation.TabNavigation=\"Continue\""));
                Assert.That(mappingXaml, Does.Contain("ParentModelBones.Filter"));
                Assert.That(managerSource, Does.Contain("OnlyShowUsedBones.Value = false"));
                Assert.That(managerSource, Does.Contain("ParentModelBones.RefreshFilter()"));
            });
        }

        [Test]
        public void ChineseLanguage_ContainsBoneReviewEmptyErrorAndActionTexts()
        {
            var languagePath = Path.Combine(FindRepositoryRoot(), "AssetEditor", "Language_Cn.json");
            using var document = JsonDocument.Parse(File.ReadAllText(languagePath));
            var language = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(language.GetProperty("AnimReTarget.BoneReview.Title").GetString(), Is.EqualTo("疑难骨骼复核"));
                Assert.That(language.GetProperty("AnimReTarget.BoneReview.Empty.Title").GetString(), Is.EqualTo("疑难骨骼已全部处理"));
                Assert.That(language.GetProperty("AnimReTarget.BoneReview.Empty.Description").GetString(), Does.Contain("无需"));
                Assert.That(language.GetProperty("AnimReTarget.BoneReview.ConfirmCandidate").GetString(), Is.EqualTo("确认候选：{0}"));
                Assert.That(language.GetProperty("AnimReTarget.BoneReview.ManualMapping").GetString(), Is.EqualTo("搜索完整骨骼树"));
                Assert.That(language.GetProperty("AnimReTarget.BoneReview.MarkIntentionalUnmapped").GetString(), Is.EqualTo("标记有意不映射"));
                Assert.That(language.GetProperty("AnimReTarget.AutoMapSummary.IntentionallyUnmapped").GetString(), Is.EqualTo("有意不映射"));
                Assert.That(language.GetProperty("AnimReTarget.BatchGate.BlockedCore").GetString(), Does.Contain("不可用"));
                Assert.That(language.GetProperty("AnimReTarget.BatchGate.Ready").GetString(), Does.Contain("可以"));
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
