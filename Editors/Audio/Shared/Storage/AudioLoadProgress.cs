namespace Editors.Audio.Shared.Storage
{
    public enum AudioLoadStage
    {
        ReadingBanks,
        ReadingMetadata,
        DiscoveringBanks,
        BuildingIndex
    }

    public sealed record AudioLoadProgress(
        int Completed,
        int Total,
        string CurrentFile,
        AudioLoadStage Stage = AudioLoadStage.ReadingBanks);
}
