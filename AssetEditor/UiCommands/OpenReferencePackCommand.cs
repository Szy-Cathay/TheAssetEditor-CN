using System;
using System.Windows.Forms;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditor.UiCommands
{
    public sealed class OpenReferencePackCommand(
        IPackFileService packFileService,
        IPackFileContainerLoader packFileContainerLoader,
        IStandardDialogs dialogs,
        LocalizationManager localizationManager,
        Func<string?>? selectPack = null) : IUiCommand
    {
        private readonly Func<string?> _selectPack =
            selectPack ?? (() => SelectPack(localizationManager));

        public void Execute()
        {
            var packPath = _selectPack();
            if (packPath == null)
                return;

            var referencePack = packFileContainerLoader.Load(packPath);
            if (referencePack == null)
            {
                dialogs.ShowDialogBox(
                    localizationManager.GetFormat(
                        "Msg.UnableToLoadPackfiles",
                        packPath),
                    localizationManager.Get("Msg.GeneralError"));
                return;
            }

            packFileService.AddReferencePack(referencePack);
        }

        private static string? SelectPack(
            LocalizationManager localizationManager)
        {
            using var dialog = new OpenFileDialog
            {
                Title = localizationManager.Get(
                    "ReferencePack.Open.SelectPack"),
                Filter = localizationManager.Get(
                    "FolderProject.PackFilter"),
            };
            return dialog.ShowDialog() == DialogResult.OK
                ? dialog.FileName
                : null;
        }
    }
}
