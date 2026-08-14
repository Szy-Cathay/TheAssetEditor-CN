using GameWorld.Core.Services;

namespace Editors.AnimatioReTarget.Editor.Saving;

public enum BatchRetargetPathMode
{
    SourcePath,
    SelectedFolder,
}

public sealed record BatchAnimationRetargetRequest(
    IReadOnlyList<AnimationReference> Animations,
    BatchRetargetPathMode PathMode,
    string? TargetFolder,
    bool OverwriteExisting);

public enum BatchAnimationRetargetItemStatus
{
    Success,
    Skipped,
    Failed,
    NotProcessed,
}

public sealed record BatchAnimationRetargetItemResult(
    string SourcePath,
    string? OutputPath,
    BatchAnimationRetargetItemStatus Status,
    string? ErrorMessage = null);

public sealed class BatchAnimationRetargetResult(
    IReadOnlyList<BatchAnimationRetargetItemResult> items)
{
    public IReadOnlyList<BatchAnimationRetargetItemResult> Items { get; } = items;
    public int SuccessCount => Count(BatchAnimationRetargetItemStatus.Success);
    public int SkippedCount => Count(BatchAnimationRetargetItemStatus.Skipped);
    public int FailureCount => Count(BatchAnimationRetargetItemStatus.Failed);
    public int NotProcessedCount => Count(BatchAnimationRetargetItemStatus.NotProcessed);

    private int Count(BatchAnimationRetargetItemStatus status) =>
        Items.Count(item => item.Status == status);
}
