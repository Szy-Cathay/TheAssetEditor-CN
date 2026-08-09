namespace Editors.ImportExport.Importing;

public sealed record ImportResult(
    bool Succeeded,
    IReadOnlyList<string> OutputPaths,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    Exception? Exception = null,
    HumanoidScaleImportSummary? HumanoidScale = null)
{
    public static ImportResult Success(
        IReadOnlyList<string> outputPaths,
        IReadOnlyList<string>? warnings = null,
        HumanoidScaleImportSummary? humanoidScale = null) =>
        new(true, outputPaths, warnings ?? [], [], HumanoidScale: humanoidScale);

    public static ImportResult Failure(Exception exception) =>
        new(false, [], [], [exception.Message], exception);

    public static ImportResult Failure(
        IReadOnlyList<string> errors) =>
        new(false, [], [], errors);
}

public sealed record HumanoidScaleImportSummary(
    bool Applied,
    float? SourceHeight,
    float? ReferenceHeight,
    float ScaleFactor,
    string Reason);
