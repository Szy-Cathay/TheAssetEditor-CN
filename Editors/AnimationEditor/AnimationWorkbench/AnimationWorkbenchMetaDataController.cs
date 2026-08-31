using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed class AnimationWorkbenchMetaDataController :
    INotifyPropertyChanged
{
    private readonly AnimationWorkbenchDocument _document;
    private readonly long _documentGeneration;

    public AnimationWorkbenchMetaDataController(
        AnimationWorkbenchDocument document)
    {
        _document = document ?? throw new ArgumentNullException(
            nameof(document));
        _documentGeneration = document.GetState().DocumentGeneration;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public event EventHandler<AnimationWorkbenchMetaDataNavigationLocation>?
        NavigationRequested;

    public ObservableCollection<AnimationWorkbenchMetaDataProblemItem>
        Problems { get; } = [];

    public bool IsSynchronizationEnabled { get; private set; }

    public bool HasProblems => Problems.Count != 0;

    public int WarningCount => Problems.Count(item => item.IsWarning);

    public int ErrorCount => Problems.Count(item => item.IsError);

    public string SummaryText => LocalizationManager.Instance.GetFormat(
        "AnimationWorkbench.MetaData.SummaryFormat",
        Problems.Count,
        WarningCount,
        ErrorCount);

    public string DirtyStateText { get; private set; } = "";

    public bool SetSynchronizationEnabled(bool enabled)
    {
        if (!IsCurrentDocument)
            return false;
        _document.SetMetaDataSynchronizationEnabled(enabled);
        Refresh();
        return true;
    }

    public AnimationWorkbenchMetaDataNavigationResult Navigate(
        AnimationWorkbenchMetaDataProblemItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = _document.NavigateToMetaDataProblem(item.Problem);
        if (result.Succeeded && result.Location != null)
            NavigationRequested?.Invoke(this, result.Location);
        return result;
    }

    public void Refresh()
    {
        if (!IsCurrentDocument)
            return;
        var state = _document.GetState();
        IsSynchronizationEnabled =
            state.IsMetaDataSynchronizationEnabled;
        Problems.Clear();
        foreach (var problem in state.MetaDataProblems)
            Problems.Add(new AnimationWorkbenchMetaDataProblemItem(problem));
        DirtyStateText = LocalizationManager.Instance.Get(
            state.IsMetaDataDirty
                ? "AnimationWorkbench.MetaData.Unsaved"
                : !state.IsMetaDataSynchronizationEnabled
                    ? "AnimationWorkbench.MetaData.Disabled"
                    : "AnimationWorkbench.MetaData.Saved");
        OnPropertyChanged(nameof(IsSynchronizationEnabled));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(DirtyStateText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsCurrentDocument =>
        _document.IsClosed == false &&
        _document.GetState().DocumentGeneration == _documentGeneration;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class AnimationWorkbenchMetaDataProblemItem
{
    public AnimationWorkbenchMetaDataProblemItem(
        AnimationWorkbenchMetaDataProblem problem)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
        var localization = LocalizationManager.Instance;
        SeverityText = localization.Get(
            problem.Severity == AnimationWorkbenchDiagnosticSeverity.Error
                ? "AnimationWorkbench.MetaData.Severity.Error"
                : "AnimationWorkbench.MetaData.Severity.Warning");
        ReasonText = localization.Get(problem.ReasonKey);
        var sourceText = localization.Get(problem.Source switch
        {
            AnimationWorkbenchSourceSlot.AnimationA =>
                "AnimationWorkbench.MetaData.Source.AnimationA",
            AnimationWorkbenchSourceSlot.AnimationB =>
                "AnimationWorkbench.MetaData.Source.AnimationB",
            _ => "AnimationWorkbench.MetaData.Source.Result",
        });
        ContextText = localization.GetFormat(
            "AnimationWorkbench.MetaData.ProblemContextFormat",
            sourceText,
            FormatIndex(problem.SourceAttributeIndex, localization),
            FormatIndex(problem.ResultAttributeIndex, localization),
            FormatTime(problem.SourceStartTime, localization),
            FormatTime(problem.ResultStartTime, localization));
        AutomationName = localization.GetFormat(
            "AnimationWorkbench.MetaData.ProblemAutomationFormat",
            SeverityText,
            ReasonText,
            ContextText);
    }

    public AnimationWorkbenchMetaDataProblem Problem { get; }

    public string SeverityText { get; }

    public string ReasonText { get; }

    public string ContextText { get; }

    public string AutomationName { get; }

    public bool IsError => Problem.Severity ==
        AnimationWorkbenchDiagnosticSeverity.Error;

    public bool IsWarning => Problem.Severity ==
        AnimationWorkbenchDiagnosticSeverity.Warning;

    public bool CanNavigate => Problem.HasNavigationLocation;

    private static string FormatIndex(
        int? index,
        LocalizationManager localization) => index.HasValue
            ? (index.Value + 1).ToString()
            : localization.Get("AnimationWorkbench.MetaData.NotAvailable");

    private static string FormatTime(
        float? seconds,
        LocalizationManager localization) => seconds.HasValue
            ? localization.GetFormat(
                "AnimationWorkbench.MetaData.TimeFormat",
                seconds.Value)
            : localization.Get("AnimationWorkbench.MetaData.NotAvailable");
}
