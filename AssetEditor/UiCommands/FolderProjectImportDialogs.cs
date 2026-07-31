using System.IO;
using System.Windows.Forms;

namespace AssetEditor.UiCommands;

public interface IFolderProjectImportDialogs
{
    string? SelectSourcePack(string title, string filter);
    string? SelectTargetFolder(string description);
    string? SelectOutputPack(
        string root,
        string defaultName,
        string title,
        string filter);
}

public sealed class FolderProjectImportDialogs :
    IFolderProjectImportDialogs
{
    public string? SelectSourcePack(string title, string filter)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
        };
        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    public string? SelectTargetFolder(string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
        };
        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    public string? SelectOutputPack(
        string root,
        string defaultName,
        string title,
        string filter)
    {
        using var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = "pack",
            AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(
                Path.TrimEndingDirectorySeparator(root)),
            FileName = defaultName + ".pack",
        };
        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }
}
