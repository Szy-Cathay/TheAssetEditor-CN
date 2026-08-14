using Editors.AnimationMeta.SuperView.Inspection;
using Editors.AnimationMeta.SuperView.Visualisation;
using Microsoft.Xna.Framework;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Definitions;

namespace Test.AnimationMeta
{
    [TestFixture]
    public class MetaDataProblemListViewModelTests
    {
        [SetUp]
        public void SetUp() => new LocalizationManager().LoadLanguage();

        [Test]
        public void Update_ProjectsLocalizedStateAndNavigatesOnlyOnUserSelection()
        {
            var source = new FirePos_v10
            {
                Name = "FIRE_POS",
                Version = 10,
                StartTime = -1,
                EndTime = 1,
                Position = new Vector3(1, 2, 3),
            };
            var index = MetaDataInspectionIndex.Create(
                [new MetaDataInspectionSource(
                    source,
                    MetaDataDocumentOwner.Animation,
                    true)],
                [],
                [],
                5);
            MetaDataInspectionProblem? navigatedProblem = null;
            var navigationCount = 0;
            var viewModel = new MetaDataProblemListViewModel(problem =>
            {
                navigatedProblem = problem;
                navigationCount++;
            });

            viewModel.Update(index);
            var problem = viewModel.Problems.Single();
            viewModel.UpdateSelection(
                MetaDataDocumentOwner.Animation,
                source);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.HasProblems, Is.True);
                Assert.That(viewModel.HeaderText, Is.EqualTo("问题（1）"));
                Assert.That(problem.IsWarning, Is.True);
                Assert.That(problem.IsError, Is.False);
                Assert.That(problem.SeverityText, Is.EqualTo("警告"));
                Assert.That(
                    problem.ReasonText,
                    Is.EqualTo("开始或结束时间不能为负数"));
                Assert.That(problem.ContextText, Does.Contain("动画 META"));
                Assert.That(problem.ContextText, Does.Contain("FIRE_POS"));
                Assert.That(problem.ToolTipText, Does.Contain("警告"));
                Assert.That(problem.AutomationName, Does.Contain("警告"));
                Assert.That(viewModel.SelectedProblem, Is.SameAs(problem));
                Assert.That(navigationCount, Is.Zero);
            });

            viewModel.SelectedProblem = null;
            viewModel.SelectedProblem = problem;

            Assert.Multiple(() =>
            {
                Assert.That(navigationCount, Is.EqualTo(1));
                Assert.That(navigatedProblem, Is.SameAs(problem.Problem));
            });

            viewModel.Update(MetaDataInspectionIndex.Create([], [], [], 5));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.HasProblems, Is.False);
                Assert.That(viewModel.HeaderText, Is.EqualTo("问题（0）"));
                Assert.That(viewModel.SelectedProblem, Is.Null);
            });
        }
    }
}
