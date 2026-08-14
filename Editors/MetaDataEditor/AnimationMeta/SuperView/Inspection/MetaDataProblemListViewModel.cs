using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.SuperView.Visualisation;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public sealed class MetaDataProblemViewModel
    {
        public MetaDataInspectionProblem Problem { get; }
        public bool IsError => Problem.Severity ==
            MetaDataDiagnosticSeverity.Error;
        public bool IsWarning => Problem.Severity ==
            MetaDataDiagnosticSeverity.Warning;
        public string SeverityText => LocalizationManager.Instance.Get(
            IsError
                ? "SuperView.Problems.Severity.Error"
                : "SuperView.Problems.Severity.Warning");
        public string ReasonText => Problem.FieldName == null
            ? LocalizationManager.Instance.Get(Problem.ReasonKey)
            : LocalizationManager.Instance.GetFormat(
                Problem.ReasonKey,
                Problem.FieldName);
        public string ContextText { get; }
        public string ToolTipText { get; }
        public string AutomationName { get; }

        public MetaDataProblemViewModel(MetaDataInspectionProblem problem)
        {
            Problem = problem;
            var ownerText = LocalizationManager.Instance.Get(
                problem.Owner == MetaDataDocumentOwner.Persistent
                    ? "SuperView.Problems.Owner.Persistent"
                    : "SuperView.Problems.Owner.Animation");
            ContextText = LocalizationManager.Instance.GetFormat(
                "SuperView.Problems.Context",
                ownerText,
                problem.Source.DisplayName);
            var details = GetDetails(problem);
            ToolTipText = LocalizationManager.Instance.GetFormat(
                "SuperView.Problems.ToolTip",
                SeverityText,
                ReasonText,
                ContextText,
                details);
            AutomationName = LocalizationManager.Instance.GetFormat(
                "SuperView.Problems.AutomationName",
                SeverityText,
                ownerText,
                problem.Source.DisplayName,
                ReasonText);
        }

        private static string GetDetails(MetaDataInspectionProblem problem)
        {
            var details = new List<string>();
            if (problem.TimeRange is MetaDataTimeRange timeRange)
            {
                details.Add(LocalizationManager.Instance.GetFormat(
                    "SuperView.Problems.Detail.Time",
                    timeRange.StartTime,
                    timeRange.EndTime));
            }

            if (problem.Position is { } position)
            {
                details.Add(LocalizationManager.Instance.GetFormat(
                    "SuperView.Problems.Detail.Position",
                    position.X,
                    position.Y,
                    position.Z));
            }

            if (!string.IsNullOrWhiteSpace(problem.ResourcePath))
            {
                details.Add(LocalizationManager.Instance.GetFormat(
                    "SuperView.Problems.Detail.Resource",
                    problem.ResourcePath));
            }

            if (!string.IsNullOrWhiteSpace(problem.BoneName))
            {
                details.Add(LocalizationManager.Instance.GetFormat(
                    "SuperView.Problems.Detail.Bone",
                    problem.BoneName));
            }

            return details.Count == 0
                ? LocalizationManager.Instance.Get(
                    "SuperView.Problems.Detail.None")
                : string.Join(Environment.NewLine, details);
        }
    }

    public sealed partial class MetaDataProblemListViewModel : ObservableObject
    {
        private readonly Action<MetaDataInspectionProblem> _navigate;
        private bool _suppressNavigation;

        [ObservableProperty]
        private MetaDataProblemViewModel? _selectedProblem;

        public ObservableCollection<MetaDataProblemViewModel> Problems
        {
            get;
        } = [];
        public bool HasProblems => Problems.Count != 0;
        public string HeaderText => LocalizationManager.Instance.GetFormat(
            "SuperView.Problems.Header",
            Problems.Count);
        public string EmptyTitle => LocalizationManager.Instance.Get(
            "SuperView.Problems.Empty.Title");
        public string EmptyDescription => LocalizationManager.Instance.Get(
            "SuperView.Problems.Empty.Description");

        public MetaDataProblemListViewModel(
            Action<MetaDataInspectionProblem> navigate)
        {
            _navigate = navigate;
        }

        partial void OnSelectedProblemChanged(
            MetaDataProblemViewModel? value)
        {
            if (!_suppressNavigation && value != null)
                _navigate(value.Problem);
        }

        public void Update(MetaDataInspectionIndex index)
        {
            var previous = SelectedProblem?.Problem;
            _suppressNavigation = true;
            try
            {
                Problems.Clear();
                foreach (var problem in index.Problems)
                    Problems.Add(new MetaDataProblemViewModel(problem));

                SelectedProblem = previous == null
                    ? null
                    : Problems.FirstOrDefault(candidate =>
                        candidate.Problem.Owner == previous.Owner &&
                        ReferenceEquals(
                            candidate.Problem.Source,
                            previous.Source) &&
                        candidate.Problem.ReasonKey == previous.ReasonKey &&
                        candidate.Problem.FieldName == previous.FieldName);
            }
            finally
            {
                _suppressNavigation = false;
            }

            OnPropertyChanged(nameof(HasProblems));
            OnPropertyChanged(nameof(HeaderText));
        }

        public void UpdateSelection(
            MetaDataDocumentOwner owner,
            ParsedMetadataAttribute? source)
        {
            _suppressNavigation = true;
            try
            {
                SelectedProblem = source == null
                    ? null
                    : Problems.FirstOrDefault(candidate =>
                        candidate.Problem.Owner == owner &&
                        ReferenceEquals(candidate.Problem.Source, source));
            }
            finally
            {
                _suppressNavigation = false;
            }
        }

        public void UpdateSelection(MetaDataInspectionProblem problem)
        {
            _suppressNavigation = true;
            try
            {
                SelectedProblem = Problems.FirstOrDefault(candidate =>
                    candidate.Problem.Owner == problem.Owner &&
                    ReferenceEquals(candidate.Problem.Source, problem.Source) &&
                    candidate.Problem.ReasonKey == problem.ReasonKey &&
                    candidate.Problem.FieldName == problem.FieldName);
            }
            finally
            {
                _suppressNavigation = false;
            }
        }
    }
}
