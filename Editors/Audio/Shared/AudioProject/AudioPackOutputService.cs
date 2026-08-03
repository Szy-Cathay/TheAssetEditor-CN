using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Editors.Audio.Shared.AudioProject
{
    public sealed record AudioPackOutput(
        string FileName,
        string FilePath,
        byte[] Data);

    public interface IAudioPackOutputService
    {
        bool SaveBatch(
            IReadOnlyCollection<AudioPackOutput> outputs,
            bool promptOnConflict = false);
    }

    public sealed class AudioPackOutputService(
        IPackFileService packFileService,
        IFileSaveService fileSaveService,
        IStandardDialogs standardDialogs) : IAudioPackOutputService
    {
        private sealed record FileBackup(
            string FilePath,
            string FileName,
            PackFile ExistingFile,
            byte[] ExistingData);

        private readonly IPackFileService _packFileService =
            packFileService;
        private readonly IFileSaveService _fileSaveService =
            fileSaveService;
        private readonly IStandardDialogs _standardDialogs =
            standardDialogs;

        public bool SaveBatch(
            IReadOnlyCollection<AudioPackOutput> outputs,
            bool promptOnConflict = false)
        {
            if (outputs.Count == 0)
                return true;

            var uniqueOutputs = outputs
                .GroupBy(
                    output => output.FilePath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            if (uniqueOutputs.Count != outputs.Count)
            {
                throw new InvalidOperationException(
                    "The generated output contains duplicate Pack paths.");
            }

            var editablePack = _packFileService.GetEditablePack() ??
                throw new InvalidOperationException(
                    LocalizationManager.Instance.Get(
                        "Msg.NoEditablePack"));
            if (editablePack is FolderProjectContainer)
            {
                var folderProjectConflicts = uniqueOutputs
                    .Where(output => _packFileService.FindFile(
                        output.FilePath,
                        editablePack) != null)
                    .Select(output => output.FilePath)
                    .ToList();
                if (promptOnConflict &&
                    folderProjectConflicts.Count > 0 &&
                    _standardDialogs.ShowYesNoBox(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.ReplaceGeneratedFiles",
                            string.Join("\n", folderProjectConflicts)),
                        LocalizationManager.Instance.Get("Msg.AreYouSure")) !=
                    ShowMessageBoxResult.OK)
                {
                    return false;
                }

                _packFileService.ApplyFileWrites(
                    editablePack,
                    uniqueOutputs
                        .Select(output => new PackFileWrite(
                            output.FilePath,
                            output.Data))
                        .ToList());
                return true;
            }

            var backups = uniqueOutputs
                .Select(output =>
                {
                    var existingFile = _packFileService.FindFile(
                        output.FilePath,
                        editablePack);
                    return new FileBackup(
                        output.FilePath,
                        output.FileName,
                        existingFile,
                        existingFile?.DataSource.ReadData());
                })
                .ToList();

            var conflicts = backups
                .Where(backup => backup.ExistingFile != null)
                .Select(backup => backup.FilePath)
                .ToList();
            if (promptOnConflict &&
                conflicts.Count > 0 &&
                _standardDialogs.ShowYesNoBox(
                    LocalizationManager.Instance.GetFormat(
                        "Msg.ReplaceGeneratedFiles",
                        string.Join("\n", conflicts)),
                    LocalizationManager.Instance.Get("Msg.AreYouSure")) !=
                ShowMessageBoxResult.OK)
            {
                return false;
            }

            try
            {
                foreach (var output in uniqueOutputs)
                {
                    if (_fileSaveService.Save(
                            output.FilePath,
                            output.Data,
                            false) == null)
                    {
                        throw new InvalidOperationException(
                            $"Unable to save '{output.FilePath}'.");
                    }
                }
            }
            catch (Exception saveException)
            {
                try
                {
                    RollBack(editablePack, backups);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Saving generated audio output failed and rollback was incomplete.",
                        saveException,
                        rollbackException);
                }

                throw;
            }

            return true;
        }

        private void RollBack(
            PackFileContainer editablePack,
            IEnumerable<FileBackup> backups)
        {
            foreach (var backup in backups)
            {
                var currentFile = _packFileService.FindFile(
                    backup.FilePath,
                    editablePack);
                if (backup.ExistingFile == null)
                {
                    if (currentFile != null)
                        _packFileService.DeleteFile(editablePack, currentFile);
                    continue;
                }

                if (currentFile != null)
                {
                    _packFileService.SaveFile(
                        currentFile,
                        backup.ExistingData!);
                    continue;
                }

                var restoredFile = PackFile.CreateFromBytes(
                    backup.FileName,
                    backup.ExistingData!);
                _packFileService.AddFilesToPack(
                    editablePack,
                    [
                        new NewPackFileEntry(
                            Path.GetDirectoryName(backup.FilePath)!,
                            restoredFile)
                    ]);
            }
        }
    }
}
