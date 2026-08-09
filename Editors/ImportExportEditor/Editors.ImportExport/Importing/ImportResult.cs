namespace Editors.ImportExport.Importing;

public sealed record ImportResult(
    bool Succeeded,
    IReadOnlyList<string> OutputPaths,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    Exception? Exception = null)
{
    public static ImportResult Success(
        IReadOnlyList<string> outputPaths,
        IReadOnlyList<string>? warnings = null) =>
        new(true, outputPaths, warnings ?? [], []);

    public static ImportResult Failure(Exception exception) =>
        new(false, [], [], [exception.Message], exception);

    public static ImportResult Failure(
        IReadOnlyList<string> errors) =>
        new(false, [], [], errors);
}
