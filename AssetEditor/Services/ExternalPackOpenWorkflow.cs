using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AssetEditor.UiCommands;
using Editors.Ipc;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace AssetEditor.Services
{
    public enum ExternalPackOpenChoice
    {
        OpenAsReference,
        ImportAsFolderProject,
        Cancelled,
    }

    public interface IExternalPackOpenChoiceDialog
    {
        ExternalPackOpenChoice Choose(string packPath);
    }

    public sealed class ExternalPackOpenWorkflow(
        IPackFileService packFileService,
        IExternalPackOpenChoiceDialog choiceDialog,
        IUiCommandFactory commandFactory,
        IStandardDialogs dialogs,
        LocalizationManager localizationManager) : IExternalPackOpenWorkflow
    {
        public Task<ExternalPackOpenResult> OpenAsync(
            string packPathOnDisk,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var application = Application.Current;
            if (application?.Dispatcher == null ||
                application.Dispatcher.CheckAccess())
            {
                return Task.FromResult(OpenCore(
                    packPathOnDisk,
                    cancellationToken));
            }

            return application.Dispatcher.InvokeAsync(
                () => OpenCore(packPathOnDisk, cancellationToken),
                System.Windows.Threading.DispatcherPriority.Normal,
                cancellationToken).Task;
        }

        private ExternalPackOpenResult OpenCore(
            string packPathOnDisk,
            CancellationToken cancellationToken)
        {
            var normalizedPath = ExternalPackPath.Normalize(packPathOnDisk);
            if (!ExternalPackPath.IsPackPath(normalizedPath))
            {
                return new ExternalPackOpenResult(
                    ExternalPackOpenStatus.Failed,
                    "Pack path is invalid");
            }

            try
            {
                var choice = choiceDialog.Choose(normalizedPath);
                if (choice == ExternalPackOpenChoice.Cancelled)
                {
                    return new ExternalPackOpenResult(
                        ExternalPackOpenStatus.Cancelled);
                }

                var requestedRole = choice ==
                    ExternalPackOpenChoice.OpenAsReference
                        ? ExternalPackRole.Reference
                        : ExternalPackRole.FolderProject;
                var existing = packFileService
                    .GetAllPackfileContainers()
                    .Where(container => ContainsPath(
                        container,
                        normalizedPath))
                    .ToList();
                var conflicting = existing.FirstOrDefault(container =>
                    GetRole(container) != requestedRole);
                if (conflicting != null)
                {
                    dialogs.ShowDialogBox(
                        localizationManager.GetFormat(
                            "ExternalPack.Open.RoleConflict",
                            GetRoleText(GetRole(conflicting)),
                            GetRoleText(requestedRole)),
                        localizationManager.Get(
                            "ExternalPack.Open.Title"));
                    return new ExternalPackOpenResult(
                        ExternalPackOpenStatus.RoleConflict);
                }

                var sameRole = existing.FirstOrDefault();
                if (sameRole != null)
                {
                    if (sameRole is FolderProjectContainer project &&
                        !packFileService.TryActivateFolderProject(
                            project.ProjectRoot))
                    {
                        return new ExternalPackOpenResult(
                            ExternalPackOpenStatus.Failed,
                            "Existing folder project could not be activated");
                    }

                    return new ExternalPackOpenResult(
                        requestedRole == ExternalPackRole.Reference
                            ? ExternalPackOpenStatus.OpenedAsReference
                            : ExternalPackOpenStatus
                                .ImportedAsFolderProject);
                }

                if (requestedRole == ExternalPackRole.Reference)
                {
                    var command = commandFactory
                        .Create<OpenReferencePackCommand>();
                    return command.Execute(normalizedPath)
                        ? new ExternalPackOpenResult(
                            ExternalPackOpenStatus.OpenedAsReference)
                        : new ExternalPackOpenResult(
                            ExternalPackOpenStatus.Failed,
                            "Pack file could not be opened as a reference");
                }

                var importCommand = commandFactory
                    .Create<ImportPackAsFolderProjectCommand>();
                var importOutcome = importCommand.Execute(
                    normalizedPath,
                    cancellationToken);
                return importOutcome switch
                {
                    FolderProjectImportOutcome.Imported =>
                        new ExternalPackOpenResult(
                            ExternalPackOpenStatus
                                .ImportedAsFolderProject),
                    FolderProjectImportOutcome.Cancelled =>
                        new ExternalPackOpenResult(
                            ExternalPackOpenStatus.Cancelled),
                    _ => new ExternalPackOpenResult(
                        ExternalPackOpenStatus.Failed,
                        "Pack import failed"),
                };
            }
            catch (Exception exception)
            {
                dialogs.ShowExceptionWindow(
                    exception,
                    localizationManager.Get("ExternalPack.Open.Failed"));
                return new ExternalPackOpenResult(
                    ExternalPackOpenStatus.Failed,
                    "Pack open failed");
            }
        }

        private static bool ContainsPath(
            PackFileContainer container,
            string packPath)
        {
            return PathsEqual(container.SystemFilePath, packPath) ||
                   container.SourcePackFilePaths.Any(sourcePath =>
                       PathsEqual(sourcePath, packPath));
        }

        private static bool PathsEqual(string? left, string right)
        {
            if (string.IsNullOrWhiteSpace(left))
                return false;

            return string.Equals(
                ExternalPackPath.Normalize(left),
                right,
                StringComparison.OrdinalIgnoreCase);
        }

        private static ExternalPackRole GetRole(
            PackFileContainer container)
        {
            return container is FolderProjectContainer ||
                   container.Role == PackFileContainerRole.ProjectWorkspace
                ? ExternalPackRole.FolderProject
                : ExternalPackRole.Reference;
        }

        private string GetRoleText(ExternalPackRole role)
        {
            return localizationManager.Get(
                role == ExternalPackRole.Reference
                    ? "ExternalPack.Open.Role.Reference"
                    : "ExternalPack.Open.Role.FolderProject");
        }

        private enum ExternalPackRole
        {
            Reference,
            FolderProject,
        }
    }
}
