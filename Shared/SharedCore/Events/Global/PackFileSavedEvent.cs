using Shared.Core.PackFiles.Models;

namespace Shared.Core.Events.Global
{
    public record PackFileSavedEvent(PackFile File);
    public record PackFileContainerSavedEvent(PackFileContainer Container);
    public record FolderProjectRestorePointCreatedEvent(
        FolderProjectContainer Container);
    public record PackFileLookUpEvent(string FileName, PackFileContainer? Container, bool Found);

    public abstract record PackFileContainerManipulationEvent();
    public enum PackFileContainerAddedReason
    {
        UserOpen,
        InternalReattach,
    }

    public record PackFileContainerAddedEvent(
        PackFileContainer Container,
        PackFileContainerAddedReason Reason =
            PackFileContainerAddedReason.UserOpen)
        : PackFileContainerManipulationEvent;
    public record PackFileContainerRemovedEvent(PackFileContainer Container) : PackFileContainerManipulationEvent;
    public record PackFileContainerSetAsMainEditableEvent(PackFileContainer? Container);
    public record PackFileContainerFilesUpdatedEvent(PackFileContainer Container, List<PackFile> ChangedFiles) : PackFileContainerManipulationEvent;
    public record PackFileContainerFilesAddedEvent(PackFileContainer Container, List<PackFile> AddedFiles) : PackFileContainerManipulationEvent;
    public record PackFileContainerFilesRemovedEvent(PackFileContainer Container, List<PackFile> RemovedFiles) : PackFileContainerManipulationEvent;
    public record PackFileContainerFolderRemovedEvent(PackFileContainer Container, string Folder) : PackFileContainerManipulationEvent;
    public record PackFileContainerFolderRenamedEvent(PackFileContainer Container, string NewNodePath) : PackFileContainerManipulationEvent;
    public record FolderProjectChangedEvent(
        FolderProjectContainer Container,
        FolderProjectChangeSet ChangeSet)
        : PackFileContainerManipulationEvent;

    public class BeforePackFileContainerRemovedEvent(PackFileContainer removed)
    {
        private bool _allowClose = true;
        private Func<bool>? _approvedCloseAction;

        public PackFileContainer Removed { get; internal set; } = removed;
        public bool AllowClose
        {
            get => _allowClose;
            set
            {
                if (!value)
                    _allowClose = false;
            }
        }
        public bool HasApprovedCloseAction =>
            _approvedCloseAction != null;

        public void SetApprovedCloseAction(Func<bool> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (_approvedCloseAction != null)
            {
                throw new InvalidOperationException(
                    "A close action has already been registered.");
            }

            _approvedCloseAction = action;
        }

        internal bool ExecuteApprovedCloseAction()
        {
            return _approvedCloseAction?.Invoke() ?? true;
        }
    }
}
