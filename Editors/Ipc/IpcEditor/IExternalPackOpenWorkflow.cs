namespace Editors.Ipc
{
    public enum ExternalPackOpenStatus
    {
        OpenedAsReference,
        ImportedAsFolderProject,
        Cancelled,
        RoleConflict,
        Failed,
    }

    public sealed record ExternalPackOpenResult(
        ExternalPackOpenStatus Status,
        string? Error = null)
    {
        public bool CanOpenRequestedFile =>
            Status is ExternalPackOpenStatus.OpenedAsReference or
                ExternalPackOpenStatus.ImportedAsFolderProject;
    }

    public interface IExternalPackOpenWorkflow
    {
        Task<ExternalPackOpenResult> OpenAsync(
            string packPathOnDisk,
            CancellationToken cancellationToken);
    }
}
