namespace Editors.Audio.Shared.Storage
{
    public sealed record AudioLoadProgress(int Completed, int Total, string CurrentFile);
}
