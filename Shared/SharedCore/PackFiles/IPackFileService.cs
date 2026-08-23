using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles
{
    public enum FolderProjectReattachMode
    {
        Inactive,
        Editable,
    }

    public interface IPackFileService
    {
        bool EnableFileLookUpEvents { get; set; }
        bool EnforceGameFilesMustBeLoaded { get; set; }

        FolderProjectContainer? AddEditableFolderProject(
            FolderProjectContainer project);
        bool TryActivateFolderProject(string projectRoot);
        PackFileContainer? AddReferencePack(
            PackFileContainer referencePack);
        PackFileContainer? AddContainer(PackFileContainer container);
        FolderProjectContainer? ReattachFolderProject(
            FolderProjectContainer project,
            int insertionIndex,
            FolderProjectReattachMode mode);
        void AddFilesToPack(
            PackFileContainer container,
            List<NewPackFileEntry> newFiles,
            bool overwriteExisting = true);
        IReadOnlyList<PackFile> ApplyFileWrites(
            PackFileContainer container,
            IReadOnlyCollection<PackFileWrite> writes);
        void CopyFileFromOtherPackFile(PackFileContainer source, string path, PackFileContainer target);
        PackFileContainer CreateNewPackFileContainer(
            string name,
            PackFileVersion packFileVersion,
            PackFileCAType type);
        void CreateFolder(PackFileContainer container, string folder);
        void DeleteFile(PackFileContainer pf, PackFile file);
        void DeleteFolder(PackFileContainer pf, string folder);
        PackFile? FindFile(string path, PackFileContainer? container = null);
        List<PackFileContainer> GetAllPackfileContainers();
        PackFileContainer? GetEditablePack();
        string GetFullPath(PackFile file, PackFileContainer? container = null);
        PackFileContainer? GetPackFileContainer(PackFile file);
        void MoveFile(PackFileContainer pf, PackFile file, string newFolderPath);
        void MoveFolder(
            PackFileContainer pf,
            string folderPath,
            string newParentPath);
        void RenameDirectory(PackFileContainer pf, string currentNodeName, string newName);
        void RenameFile(PackFileContainer pf, PackFile file, string newName);
        void SaveFile(PackFile file, byte[] data);
        void SavePackContainer(
            PackFileContainer pf,
            string path,
            GameInformation gameInformation);
        void SetEditablePack(PackFileContainer? pf);
        bool TryUnloadPackContainer(PackFileContainer pf);
        void UnloadPackContainer(PackFileContainer pf);
    }
}
