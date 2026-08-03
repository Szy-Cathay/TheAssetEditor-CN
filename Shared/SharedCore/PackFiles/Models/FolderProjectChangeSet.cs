namespace Shared.Core.PackFiles.Models;

public enum FolderProjectFileChangeKind
{
    Added,
    Updated,
    Removed,
    Moved,
}

public sealed record FolderProjectFileChange(
    string Path,
    FolderProjectFileChangeKind Kind,
    PackFile File,
    string? PreviousPath = null);

public enum FolderProjectDirectoryChangeKind
{
    Added,
    Removed,
    Moved,
}

public sealed record FolderProjectDirectoryChange(
    string Path,
    FolderProjectDirectoryChangeKind Kind,
    string? PreviousPath = null);

public sealed record FolderProjectChangeSet
{
    public FolderProjectChangeSet(
        long revision,
        IReadOnlyList<FolderProjectFileChange> fileChanges,
        IReadOnlyList<FolderProjectDirectoryChange>? directoryChanges = null,
        bool requiresReload = false)
    {
        Revision = revision;
        FileChanges = fileChanges;
        DirectoryChanges = directoryChanges ?? [];
        RequiresReload = requiresReload;
    }

    public long Revision { get; }
    public IReadOnlyList<FolderProjectFileChange> FileChanges { get; }
    public IReadOnlyList<FolderProjectDirectoryChange> DirectoryChanges { get; }
    public bool RequiresReload { get; }
}
