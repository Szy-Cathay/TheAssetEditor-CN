namespace Shared.Ui.Common.OperationProgress;

public sealed record OperationProgressUpdate(
    string Status,
    string? Detail = null,
    long Completed = 0,
    long Total = 0);
