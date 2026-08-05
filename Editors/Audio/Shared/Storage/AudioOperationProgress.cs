namespace Editors.Audio.Shared.Storage
{
    public sealed record AudioOperationProgress(
        string StageResourceKey,
        string Detail,
        int Completed = 0,
        int Total = 0);
}
